// HandGrabManager.cs
// Maneja el agarre de objetos marcados con la capa "Grabbable". Un unico raycast
// por frame desde la camara a traves de la posicion de la mano (coste O(1) por
// frame, independiente del numero de objetos). Mano cerrada sobre un objeto ->
// lo agarra (se levanta _liftHeight y sigue la mano proyectada sobre el plano
// horizontal de su altura de reposo). Al abrir la mano o perderla, lo suelta.
// El seguimiento suaviza el objetivo (filtra el ruido del tracking, evitando el
// temblor) y luego lo persigue con MoveTowards a _followSpeed (respuesta rapida).
// El glow se aplica con MaterialPropertyBlock solo al objeto senalado/agarrado,
// sin instanciar materiales por objeto.
// Expone eventos para sistemas externos (ej. puzzles): PickedUp al agarrar y
// DropInterceptor en el release; si un suscriptor consume el drop (snap a hueco),
// no se reactiva la gravedad. Las piezas bloqueadas o en snap no se pueden agarrar.

using System;
using UnityEngine;

namespace NuiGrab
{
  public class HandGrabManager : MonoBehaviour
  {
    [SerializeField] private HandTracker _handTracker;
    [SerializeField] private Camera _camera;
    [SerializeField, Tooltip("Jugador cuyo ataque bloquea el agarre (si no se asigna, se busca en la escena).")]
    private global::Player _player;

    [SerializeField, Min(0f)] private float _grabRadius = 0.22f;
    [SerializeField, Min(0f)] private float _grabSearchRadius = 0.35f;
    [SerializeField, Min(0f)] private float _gripReleaseDelay = 0.12f;
    [SerializeField, Min(0f)] private float _liftHeight = 0.25f;
    [SerializeField, Min(0f)] private float _followSpeed = 100f;
    [SerializeField, Min(0f)] private float _targetSmoothing = 50f;
    [SerializeField] private float _maxGrabDistance = 30f;
    [SerializeField] private LayerMask _grabLayerMask = 1 << 8;

    [Header("Colisiones del objeto agarrado")]
    [SerializeField, Tooltip("Capas que detienen al objeto agarrado (muros, piso, pedestales). Por defecto todas menos Grabbable.")]
    private LayerMask _collisionBlockers = ~(1 << 8);
    [SerializeField, Min(0f), Tooltip("Margen entre el objeto y la superficie al frenar.")]
    private float _wallSkin = 0.04f;

    [SerializeField] private Color _hoverColor = new Color(0.35f, 1f, 0.5f);
    [SerializeField] private Color _holdingColor = new Color(1f, 0.95f, 0.3f);
    [SerializeField, Min(0f)] private float _emissionIntensity = 0.7f;
    [SerializeField, Range(1f, 30f)] private float _glowSmoothSpeed = 8f;

    [Header("Tolerancia de tracking")]
    [SerializeField, Min(0f), Tooltip("Segundos que se mantiene el agarre usando la ultima posicion conocida al perder la mano. Evita sueltos por parpadeos del tracking durante movimientos rapidos.")]
    private float _handLostGracePeriod = 0.35f;
    [SerializeField, Min(0f), Tooltip("Multiplicador de la velocidad estimada de la mano que hereda el objeto al soltar (lanzamiento).")]
    private float _throwSpeedMultiplier = 1.15f;
    [SerializeField, Min(0f), Tooltip("Velocidad maxima de lanzamiento para evitar disparos absurdos.")]
    private float _maxThrowSpeed = 10f;

  private Rigidbody _held;
  private Rigidbody _hovered;
  private bool _isHolding;
  private bool _wasClosed;
  private float _notClosedTimer;
    private float _groundY;
    private Vector3 _smoothedTarget;
    private Vector3 _lastHeldPosition;
    private Vector3 _heldVelocity;
    private float _heldRadius;
    private float _handLostTimer;
    private Vector2 _lastKnownViewport;

    private MaterialPropertyBlock _block;
    private Renderer _glowRenderer;
    private Color _glowBase;
    private Color _glowEmission;

    public bool IsHolding => _isHolding;

  /// <summary>True mientras la mano (sin agarrar) apunta a un objeto agarrable valido.</summary>
  public bool IsHoveringObject => _hovered != null;

  /// <summary>Objeto agarrado actualmente (null si la mano esta libre).</summary>
  public Rigidbody Held => _held;

  /// <summary>
  /// Velocidad estimada del objeto agarrado, derivada del seguimiento manual
  /// (los cuerpos kinematicos no exponen velocidad a la fisica). La usa el
  /// sistema de rompibles para detectar golpes contra muros mientras se sostiene.
  /// </summary>
  public Vector3 HeldVelocity => _heldVelocity;

  /// <summary>True si el cuerpo indicado es el que la mano sostiene ahora mismo.</summary>
  public bool IsHoldingBody(Rigidbody body)
  {
    return _isHolding && _held == body;
  }

  /// <summary>Se dispara al agarrar un Rigidbody.</summary>
  public event Action<Rigidbody> PickedUp;

  /// <summary>
  /// Se dispara al soltar. Si algun suscriptor devuelve true, consume el drop
  /// (ej. snap del puzzle) y no se reactiva la gravedad del objeto.
  /// </summary>
  public event Func<Rigidbody, bool> DropInterceptor;

    private void Awake()
    {
      _block = new MaterialPropertyBlock();

      if (_camera == null)
      {
        _camera = Camera.main;
      }

      if (_handTracker == null)
      {
        _handTracker = FindAnyObjectByType<HandTracker>();
      }

      if (_player == null)
      {
        _player = FindAnyObjectByType<global::Player>();
      }
    }

    private void Update()
    {
      // isActiveAndEnabled: un tracker desactivado deja de actualizar su estado
      // interno pero sus propiedades seguirian devolviendo el ultimo dato
      // valido (mano detectada/cerrada), haciendo agarrar objetos fantasma.
      var trackerOk = _handTracker != null && _handTracker.isActiveAndEnabled;
      var detectedNow = trackerOk && _handTracker.IsHandDetected;

      if (detectedNow)
      {
        _handLostTimer = 0f;
        _lastKnownViewport = _handTracker.HandViewportPosition;
      }
      else
      {
        _handLostTimer += Time.deltaTime;
      }

      // Periodo de gracia: al perder la mano (parpadeo del tracking en
      // movimientos rapidos) seguimos operando con la ultima posicion conocida
      // un instante, en vez de soltar el objeto a mitad de un lanzamiento o de
      // un golpe contra una pared.
      var handVisible = detectedNow || _handLostTimer <= _handLostGracePeriod;

      // Cerrar mano solo con deteccion REAL (evita agarrar con datos viejos);
      // si ya sostenemos algo y la mano se pierde dentro de la gracia, se
      // mantiene el agarre.
      var closed = detectedNow && _handTracker.IsHandClosed;

      if (_isHolding && !detectedNow)
      {
        closed = true;
      }

      // Mientras el jugador ataca no se puede agarrar: se suelta cualquier
      // objeto ya agarrado y se ignora la mano (no se agarran objetos nuevos).
      if (_player != null && _player.IsAttacking)
      {
        if (_isHolding)
        {
          Release();
        }

        _wasClosed = closed;
        return;
      }

      if (_isHolding)
      {
        _hovered = null;

        if (!handVisible)
        {
          Release();
        }
        else if (!closed)
        {
          // Debounce: la mano debe estar abierta _gripReleaseDelay seguidos para
          // soltar; evita sueltos accidentales por ruido del tracking.
          _notClosedTimer += Time.deltaTime;

          if (_notClosedTimer >= _gripReleaseDelay)
          {
            Release();
          }
        }
        else
        {
          _notClosedTimer = 0f;
          MoveToHand();
        }

        if (_isHolding)
        {
          var heldRenderer = _held != null ? _held.GetComponent<Renderer>() : null;
          SetGlow(heldRenderer, _holdingColor);
        }
      }
      else
      {
        var target = handVisible ? FindTargetUnderHand() : null;
        _hovered = target;
        var renderer = target != null ? target.GetComponent<Renderer>() : null;
        SetGlow(renderer, _hoverColor);

        if (closed && !_wasClosed && target != null)
        {
          Grab(target);
        }
      }

      _wasClosed = closed;
    }

    private Rigidbody FindTargetUnderHand()
    {
      // Ultima posicion conocida: sigue funcionando durante el periodo de gracia.
      var hand = _lastKnownViewport;
      var ray = ScreenRay(hand);

      RaycastHit hit;

      // Primero un rayo preciso; si falla (ruido del tracking), una esfera
      // tolerante a lo largo del rayo para no perder el objeto de vista.
      if (!Physics.Raycast(ray, out hit, _maxGrabDistance, _grabLayerMask, QueryTriggerInteraction.Ignore) &&
          !Physics.SphereCast(ray.origin, _grabSearchRadius, ray.direction, out hit, _maxGrabDistance, _grabLayerMask, QueryTriggerInteraction.Ignore))
      {
        return null;
      }

      var vp = _camera.WorldToViewportPoint(hit.point);
      var dx = vp.x - hand.x;
      var dy = vp.y - hand.y;

      if (dx * dx + dy * dy > _grabRadius * _grabRadius)
      {
        return null;
      }

      var rb = hit.rigidbody != null ? hit.rigidbody : hit.collider.GetComponentInParent<Rigidbody>();

      if (rb != null)
      {
        var piece = rb.GetComponent<PuzzlePiece>();

        if (piece != null && (piece.IsLocked || piece.IsSnapping))
        {
          return null;
        }
      }

      return rb;
    }

    private void Grab(Rigidbody target)
    {
      _held = target;
      _isHolding = true;
      _groundY = _held.position.y;
      _smoothedTarget = _held.position;
      _lastHeldPosition = _held.position;
      _heldVelocity = Vector3.zero;

      // Radio de barrido para no atravesar muros: el extent mayor del collider.
      var heldCollider = _held.GetComponent<Collider>();
      if (heldCollider != null)
      {
        var extents = heldCollider.bounds.extents;
        _heldRadius = Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z));
      }
      else
      {
        _heldRadius = 0.25f;
      }

      _held.isKinematic = true;
      _notClosedTimer = 0f;
      PickedUp?.Invoke(_held);
    }

    private void MoveToHand()
    {
      // Ultima posicion conocida: el objeto sigue la mano incluso durante un
      // parpadeo breve del tracking.
      var hand = _lastKnownViewport;
      var ray = ScreenRay(hand);
      var planeHeight = _groundY + _liftHeight;

      if (new Plane(Vector3.up, new Vector3(0f, planeHeight, 0f)).Raycast(ray, out var distance) && distance > 0f)
      {
        var desired = ray.GetPoint(distance);
        var dt = Mathf.Min(Time.deltaTime, 0.05f);

        var smoothing = 1f - Mathf.Exp(-_targetSmoothing * dt);
        _smoothedTarget = Vector3.Lerp(_smoothedTarget, desired, smoothing);

        var pos = Vector3.MoveTowards(_held.position, _smoothedTarget, _followSpeed * dt);

        // Los cuerpos kinematicos ignoran colisiones al moverse con
        // MovePosition: sin este barrido el objeto atraviesa muros. Se corta el
        // avance en el primer obstaculo de _collisionBlockers. La capa Grabbable
        // queda excluida para no chocar contra otras piezas sueltas ni consigo
        // mismo; los triggers se ignoran (slots, zonas del puzzle).
        var delta = pos - _held.position;
        var remaining = delta.magnitude;
        RaycastHit surfaceHit = default;
        var blockedBySurface = false;

        if (remaining > 0.0001f && _heldRadius > 0f)
        {
          var direction = delta / remaining;

          if (Physics.SphereCast(
                  _held.position,
                  _heldRadius,
                  direction,
                  out surfaceHit,
                  remaining,
                  _collisionBlockers.value,
                  QueryTriggerInteraction.Ignore))
          {
            pos = _held.position + direction * Mathf.Max(surfaceHit.distance - _wallSkin, 0f);
            blockedBySurface = true;
          }
        }

        _held.MovePosition(pos);

        // Velocidad estimada del seguimiento: necesaria porque los cuerpos
        // kinematicos no reportan velocidad a la fisica. Suavizada para que el
        // ruido del tracking no dispare falsos golpes.
        if (Time.deltaTime > 0f)
        {
          var instant = (pos - _lastHeldPosition) / Time.deltaTime;
          _heldVelocity = Vector3.Lerp(_heldVelocity, instant, 0.4f);
        }

        _lastHeldPosition = pos;

        // Como el barrido evita el contacto fisico, un estampado fuerte contra
        // una superficie nunca generaria OnCollisionEnter: se notifica a mano
        // con la velocidad real del golpe antes del frenado.
        if (blockedBySurface && _held != null)
        {
          NotifyImpactWhileHeld(surfaceHit, remaining / Mathf.Max(dt, 0.0001f));
        }
      }
    }

    /// <summary>
    /// Impacto del objeto agarrado contra una superficie solida detectado por
    /// el barrido del agarre. Si la velocidad del golpe supera el umbral del
    /// componente Breakable, lo rompe en el punto de contacto.
    /// </summary>
    private void NotifyImpactWhileHeld(RaycastHit hit, float intendedSpeed)
    {
      // Los fragmentos de roturas previas no cuentan como pared.
      if (hit.rigidbody != null && hit.rigidbody.GetComponent<Fragment>() != null)
      {
        return;
      }

      var breakable = _held.GetComponent<Breakable>();

      if (breakable == null || breakable.IsBroken)
      {
        return;
      }

      // Misma regla de piso que OnCollisionEnter: el suelo no rompe salvo que
      // el objeto lo permita explicitamente.
      if (hit.normal.y > 0.6f && !breakable.BreaksOnFloor)
      {
        return;
      }

      if (intendedSpeed < breakable.BreakSpeed)
      {
        return;
      }

      var direction = (hit.point - _held.position).normalized;
      breakable.Break(hit.point, direction * intendedSpeed);
    }

    private void Release()
    {
      var held = _held;

      if (held != null && !TryInterceptDrop(held))
      {
        held.isKinematic = false;

        // Lanzamiento: el objeto hereda la velocidad estimada de la mano. Sin
        // esto, soltar (a proposito o por perdida de tracking a mitad de un
        // golpe) dejaria caer el objeto sin impulso.
        var throwVelocity = _heldVelocity * _throwSpeedMultiplier;
        var speed = throwVelocity.magnitude;

        if (speed > _maxThrowSpeed)
        {
          throwVelocity *= _maxThrowSpeed / speed;
        }

        held.linearVelocity = throwVelocity;
      }

      _held = null;
      _isHolding = false;
      _heldVelocity = Vector3.zero;
      ClearGlow();
    }

    /// <summary>
    /// Soltado sin interceptores ni gravedad: lo usa el sistema de rompibles
    /// cuando el objeto agarrado se destruye y hay que limpiar el estado ya.
    /// </summary>
    public void ForceRelease()
    {
      _held = null;
      _isHolding = false;
      _notClosedTimer = 0f;
      _heldVelocity = Vector3.zero;
      ClearGlow();
    }

    private bool TryInterceptDrop(Rigidbody body)
    {
      if (DropInterceptor == null)
      {
        return false;
      }

      foreach (var handler in DropInterceptor.GetInvocationList())
      {
        if (((Func<Rigidbody, bool>)handler)(body))
        {
          return true;
        }
      }

      return false;
    }

    private Ray ScreenRay(Vector2 handViewport)
    {
      return _camera.ScreenPointToRay(new Vector3(handViewport.x * _camera.pixelWidth, handViewport.y * _camera.pixelHeight, 0f));
    }

    private void SetGlow(Renderer renderer, Color color)
    {
      if (renderer != _glowRenderer)
      {
        ClearGlow();

        if (renderer != null)
        {
          _glowRenderer = renderer;
          var shared = _glowRenderer.sharedMaterial;
          _glowBase = shared.GetColor("_BaseColor");
          _glowEmission = Color.black;

          if (!shared.IsKeywordEnabled("_EMISSION"))
          {
            shared.EnableKeyword("_EMISSION");
          }
        }
      }

      if (_glowRenderer == null)
      {
        return;
      }

      var t = _glowSmoothSpeed * Time.deltaTime;
      _glowBase = Color.Lerp(_glowBase, color, t);
      _glowEmission = Color.Lerp(_glowEmission, color, t);

      _block.SetColor("_BaseColor", _glowBase);
      _block.SetColor("_EmissionColor", _glowEmission * _emissionIntensity);
      _glowRenderer.SetPropertyBlock(_block);
    }

    private void ClearGlow()
    {
      if (_glowRenderer != null)
      {
        _glowRenderer.SetPropertyBlock(null);
        _glowRenderer = null;
      }
    }
  }
}
