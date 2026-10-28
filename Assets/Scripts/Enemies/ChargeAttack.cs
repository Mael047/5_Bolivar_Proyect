// ChargeAttack.cs
// Embestida cargada (torito). Ciclo:
//   1. Preparar: mira al jugador, reproduce el idle de carga y aparece una
//      linea en el suelo que se llena hacia donde va a embestir. Al final de
//      la carga la direccion se fija (el jugador puede esquivar).
//   2. Embestir: sale disparado en linea recta. Si toca al jugador le hace
//      daño y lo empuja; si choca contra un muro queda aturdido.
//      Cubrirse con el escudo de frente bloquea la embestida (y lo aturde).
//   3. Recuperarse y esperar el tiempo de recarga. Mientras tanto el Enemy
//      vuelve a perseguir/patrullar como siempre.
//
// Animator (opcional): bools "Charge" (preparar) y "Dash" (embestir). Si el
// controlador no los tiene, el ataque funciona igual sin animacion.
// Tools > Enemigos > Configurar embestida del torito lo deja todo listo.

using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[RequireComponent(typeof(Enemy))]
public class ChargeAttack : MonoBehaviour
{
  private static readonly int ChargeId = Animator.StringToHash("Charge");
  private static readonly int DashId = Animator.StringToHash("Dash");

  public enum Phase { Inactivo, Preparando, Embistiendo, Recuperando }

  [Header("Cuando embiste")]
  [SerializeField, Min(0f), Tooltip("Distancia minima al jugador para embestir.")]
  private float _minRange = 0f;
  [SerializeField, Min(1f), Tooltip("Distancia maxima al jugador para empezar la embestida.")]
  private float _maxRange = 9f;
  [SerializeField, Min(0f), Tooltip("Segundos entre embestidas.")]
  private float _cooldown = 2.5f;
  [SerializeField, Tooltip("Si lo golpeas mientras prepara, la embestida se cancela.")]
  private bool _interruptibleWindup = true;

  [Header("Preparar")]
  [SerializeField, Min(0.1f), Tooltip("Duracion de la carga (lo que dura el idle de carga).")]
  private float _windupTime = 1.1f;
  [SerializeField, Range(0f, 1f), Tooltip("Parte de la carga en la que sigue apuntando al jugador. Despues la direccion queda fija.")]
  private float _trackPortion = 0.75f;
  [SerializeField, Min(0f), Tooltip("Grados por segundo al girar hacia el jugador.")]
  private float _aimTurnSpeed = 360f;
  [SerializeField, Min(0f), Tooltip("Temblor del modelo mientras carga (m).")]
  private float _windupShake = 0.03f;

  [Header("Embestir")]
  [SerializeField, Min(1f)] private float _dashSpeed = 14f;
  [SerializeField, Min(1f), Tooltip("Distancia maxima de la embestida.")]
  private float _dashDistance = 10f;
  [SerializeField, Min(0.1f), Tooltip("Radio con el que detecta muros y al jugador.")]
  private float _bodyRadius = 0.5f;
  [SerializeField, Tooltip("Altura del centro del cuerpo (para detectar choques). Mayor que el radio para no chocar con el suelo.")]
  private float _bodyHeight = 0.9f;
  [SerializeField, Tooltip("Capas que lo detienen (muros, rocas, arboles). Las rampas y el suelo no lo detienen.")]
  private LayerMask _obstacleMask = 1;

  [Header("Daño")]
  [SerializeField, Min(0)] private int _damage = 20;
  [SerializeField, Min(0f)] private float _knockback = 11f;
  [SerializeField, Min(0f), Tooltip("Segundos de invulnerabilidad del jugador tras el golpe.")]
  private float _playerInvulnerability = 1f;
  [SerializeField, Range(0f, 1f)] private float _hitCameraShake = 0.5f;
  [SerializeField, Tooltip("Cubrirse de frente bloquea la embestida.")]
  private bool _blockWithShield = true;
  [SerializeField, Range(0f, 360f)] private float _blockAngle = 140f;

  [Header("Cajas y rompibles")]
  [SerializeField, Tooltip("Rompe las cajas (Breakable) que encuentra en la embestida.")]
  private bool _breakBoxes = true;
  [SerializeField, Tooltip("Tambien rompe los que solo se rompen con espada (puertas). Normalmente apagado.")]
  private bool _breakSwordOnly;
  [SerializeField, Tooltip("Al romper una caja se detiene y queda aturdido, como al chocar con un muro.")]
  private bool _stopOnBreak;
  [SerializeField, Range(0f, 1f)] private float _breakCameraShake = 0.2f;

  [Header("Recuperarse")]
  [SerializeField, Min(0f)] private float _recoverTime = 0.8f;
  [SerializeField, Min(0f), Tooltip("Aturdido al chocar contra un muro o contra el escudo.")]
  private float _crashStunTime = 1.6f;
  [SerializeField, Range(0f, 1f)] private float _crashCameraShake = 0.35f;

  [Header("Linea indicadora")]
  [SerializeField] private bool _showIndicator = true;
  [SerializeField, Min(0.05f)] private float _indicatorWidth = 0.9f;
  [SerializeField] private Color _indicatorColor = new Color(1f, 0.2f, 0.15f, 0.55f);
  [SerializeField, Tooltip("Color cuando la direccion ya quedo fija (a punto de embestir).")]
  private Color _indicatorLockedColor = new Color(1f, 0.05f, 0.05f, 0.85f);
  [SerializeField, Tooltip("Capas del suelo sobre las que se dibuja la linea.")]
  private LayerMask _groundMask = ~((1 << 2) | (1 << 3) | (1 << 5) | (1 << 8) | (1 << 10));
  [SerializeField, Tooltip("Material de la linea (vacio = uno transparente basico).")]
  private Material _indicatorMaterial;

  [Header("Sonidos")]
  [SerializeField] private AudioClip _windupSound;
  [SerializeField] private AudioClip _dashSound;
  [SerializeField] private AudioClip _crashSound;
  [SerializeField, Range(0f, 1f)] private float _volume = 0.9f;

  [Header("Eventos")]
  public UnityEvent OnWindup = new UnityEvent();
  public UnityEvent OnDash = new UnityEvent();
  public UnityEvent OnHitPlayer = new UnityEvent();
  public UnityEvent OnCrash = new UnityEvent();

  private const int IndicatorPoints = 14;
  private static Material s_defaultMaterial;

  private Enemy _enemy;
  private Health _health;
  private HitReaction _hitReaction;
  private ContactDamage _contactDamage;
  private Animator _animator;
  private Rigidbody _rb;
  private Transform _model;
  private LineRenderer _line;
  private bool _hasChargeParam;
  private bool _hasDashParam;

  private float _nextChargeTime;
  private Coroutine _routine;
  private Vector3 _dashDirection;
  private float _indicatorFill;
  private float _indicatorLength;

  public Phase Current { get; private set; } = Phase.Inactivo;

  private void Awake()
  {
    _enemy = GetComponent<Enemy>();
    _health = GetComponent<Health>();
    _hitReaction = GetComponent<HitReaction>();
    _contactDamage = GetComponent<ContactDamage>();
    _rb = GetComponent<Rigidbody>();
    _animator = GetComponentInChildren<Animator>();

    if (_animator != null)
    {
      foreach (var p in _animator.parameters)
      {
        if (p.nameHash == ChargeId && p.type == AnimatorControllerParameterType.Bool) _hasChargeParam = true;
        if (p.nameHash == DashId && p.type == AnimatorControllerParameterType.Bool) _hasDashParam = true;
      }

      // El modelo (no la raiz) es lo que tiembla al cargar.
      _model = _animator.transform != transform ? _animator.transform : null;
    }

    if (_enemy != null && _obstacleMask == 1 && _enemy.obstacleMask.value != 0)
    {
      _obstacleMask |= _enemy.obstacleMask;
    }

    CreateIndicator();
  }

  private void OnEnable()
  {
    if (_health != null) _health.Died += OnDied;
  }

  private void OnDisable()
  {
    if (_health != null) _health.Died -= OnDied;
    Cancel(false);
  }

  private void OnDestroy()
  {
    if (_line != null)
    {
      Destroy(_line.gameObject);
    }
  }

  private void OnDied()
  {
    Cancel(false);
    enabled = false;
  }

  // ---------- Decision ----------

  private void Update()
  {
    if (Current != Phase.Inactivo || Time.time < _nextChargeTime || _enemy == null)
    {
      return;
    }

    if (_health != null && _health.IsDead)
    {
      return;
    }

    if (_hitReaction != null && _hitReaction.IsStunned)
    {
      return;
    }

    if (Menus.MainMenu.IsOpen || _enemy.target == null || !_enemy.CanSeePlayer())
    {
      return;
    }

    var toPlayer = _enemy.target.transform.position - transform.position;
    toPlayer.y = 0f;
    var distance = toPlayer.magnitude;

    if (distance < _minRange || distance > _maxRange)
    {
      return;
    }

    _routine = StartCoroutine(ChargeRoutine());
  }

  // ---------- Ciclo de la embestida ----------

  private IEnumerator ChargeRoutine()
  {
    _enemy.ExternalControl = true;
    _enemy.IsMovingNow = false;
    _enemy.attack = false;

    // 1. Preparar
    Current = Phase.Preparando;
    SetAnim(charge: true, dash: false);
    Play(_windupSound);
    OnWindup.Invoke();

    var t = 0f;
    var modelRest = _model != null ? _model.localPosition : Vector3.zero;

    while (t < _windupTime)
    {
      if (_interruptibleWindup && _hitReaction != null && _hitReaction.IsStunned)
      {
        RestoreModel(modelRest);
        Finish(_recoverTime);
        yield break;
      }

      var portion = t / _windupTime;
      var locked = portion >= _trackPortion;

      if (!locked && _enemy.target != null)
      {
        var toPlayer = _enemy.target.transform.position - transform.position;
        toPlayer.y = 0f;

        if (toPlayer.sqrMagnitude > 0.001f)
        {
          var look = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
          SetRotation(Quaternion.RotateTowards(transform.rotation, look, _aimTurnSpeed * Time.deltaTime));
        }
      }

      _dashDirection = Flat(transform.forward);

      if (_model != null && _windupShake > 0f)
      {
        _model.localPosition = modelRest + Random.insideUnitSphere * (_windupShake * portion);
      }

      _indicatorFill = Mathf.Clamp01(portion / Mathf.Max(0.01f, _trackPortion));
      UpdateIndicator(locked, portion);

      t += Time.deltaTime;
      yield return null;
    }

    RestoreModel(modelRest);

    // 2. Embestir
    Current = Phase.Embistiendo;
    SetAnim(charge: false, dash: true);
    Play(_dashSound);
    OnDash.Invoke();

    if (_contactDamage != null)
    {
      _contactDamage.enabled = false; // el daño lo hace la embestida
    }

    var travelled = 0f;
    var hitPlayer = false;
    var crashed = false;

    while (travelled < _dashDistance)
    {
      var step = Mathf.Min(_dashSpeed * Time.deltaTime, _dashDistance - travelled);
      var origin = transform.position + Vector3.up * _bodyHeight;

      // Fade de la linea mientras embiste.
      _indicatorFill = 1f;
      UpdateIndicator(true, 1f, 1f - travelled / _dashDistance);

      if (_breakBoxes && BreakBoxes(origin) && _stopOnBreak)
      {
        crashed = true;
        break;
      }

      if (!hitPlayer && TryHitPlayer(origin, out var blocked))
      {
        hitPlayer = true;

        if (blocked)
        {
          crashed = true;
          break;
        }
      }

      if (Physics.SphereCast(origin, _bodyRadius, _dashDirection, out var wall, step + 0.05f, _obstacleMask, QueryTriggerInteraction.Ignore)
          && wall.normal.y < 0.5f && !IsOwnCollider(wall.collider) && !IsPlayer(wall.collider) && !WillBreak(wall.collider))
      {
        Move(_dashDirection * Mathf.Max(0f, wall.distance - 0.05f));
        crashed = true;
        break;
      }

      Move(_dashDirection * step);
      travelled += step;
      yield return null;
    }

    HideIndicator();

    if (_contactDamage != null)
    {
      _contactDamage.enabled = true;
    }

    // 3. Recuperarse
    if (crashed)
    {
      Play(_crashSound);
      TopDownCamera.AddShake(_crashCameraShake);
      OnCrash.Invoke();

      // Queda quieto y vulnerable un rato (buen momento para atacarlo).
      Finish(_crashStunTime);
    }
    else
    {
      Finish(_recoverTime);
    }
  }

  private void Finish(float recover)
  {
    _routine = null;
    HideIndicator();
    SetAnim(charge: false, dash: false);
    Current = Phase.Recuperando;
    StartCoroutine(Recover(recover));
  }

  private IEnumerator Recover(float seconds)
  {
    if (seconds > 0f)
    {
      yield return new WaitForSeconds(seconds);
    }

    Current = Phase.Inactivo;
    _enemy.ExternalControl = false;
    _nextChargeTime = Time.time + _cooldown;
  }

  /// <summary>Corta la embestida (muerte, desactivado...).</summary>
  public void Cancel(bool keepCooldown = true)
  {
    StopAllCoroutines();
    _routine = null;
    HideIndicator();
    SetAnim(charge: false, dash: false);

    if (_contactDamage != null)
    {
      _contactDamage.enabled = true;
    }

    if (_enemy != null)
    {
      _enemy.ExternalControl = false;
    }

    Current = Phase.Inactivo;

    if (keepCooldown)
    {
      _nextChargeTime = Time.time + _cooldown;
    }
  }

  // ---------- Golpe al jugador ----------

  private readonly Collider[] _overlap = new Collider[16];

  /// <summary>Rompe las cajas que toca el frente del torito. Devuelve true si rompio alguna.</summary>
  private bool BreakBoxes(Vector3 origin)
  {
    var broke = false;
    var center = origin + _dashDirection * (_bodyRadius * 0.6f);
    var count = Physics.OverlapSphereNonAlloc(center, _bodyRadius * 1.3f, _overlap, ~0, QueryTriggerInteraction.Ignore);

    for (var i = 0; i < count; i++)
    {
      var breakable = _overlap[i].GetComponentInParent<NuiGrab.Breakable>();

      if (breakable == null || breakable.IsBroken || (breakable.OnlySword && !_breakSwordOnly))
      {
        continue;
      }

      var point = _overlap[i].ClosestPoint(center);
      breakable.Break(point, _dashDirection * _dashSpeed);
      broke = true;
    }

    if (broke && _breakCameraShake > 0f)
    {
      TopDownCamera.AddShake(_breakCameraShake);
    }

    return broke;
  }

  private bool TryHitPlayer(Vector3 origin, out bool blocked)
  {
    blocked = false;
    var count = Physics.OverlapSphereNonAlloc(origin + _dashDirection * (_bodyRadius * 0.5f), _bodyRadius * 1.2f, _overlap, ~0, QueryTriggerInteraction.Ignore);

    for (var i = 0; i < count; i++)
    {
      var player = _overlap[i].GetComponentInParent<Player>();

      if (player == null)
      {
        continue;
      }

      if (player.IsInvincible)
      {
        return false; // esquivo: pasa de largo
      }

      var health = player.GetComponentInParent<Health>();
      if (health == null) health = player.GetComponentInChildren<Health>();

      if (health == null || health.IsDead || ContactDamage.IsInvulnerable(health))
      {
        return false;
      }

      var push = Flat(player.transform.position - transform.position);
      push = (push + _dashDirection).normalized;

      if (_blockWithShield && player.IsBlocking &&
          Vector3.Angle(player.transform.forward, -_dashDirection) <= _blockAngle * 0.5f)
      {
        blocked = true;
        player.NotifyBlocked();
        ContactDamage.SetInvulnerable(health, _playerInvulnerability * 0.5f);
        Push(player, push, _knockback * 0.5f);
        return true;
      }

      ContactDamage.SetInvulnerable(health, _playerInvulnerability);
      health.TakeDamage(_damage);
      Push(player, push, _knockback);
      TopDownCamera.AddShake(_hitCameraShake);
      OnHitPlayer.Invoke();
      return true;
    }

    return false;
  }

  private static void Push(Player player, Vector3 direction, float force)
  {
    var rb = player.GetComponent<Rigidbody>();

    if (rb != null && !rb.isKinematic && force > 0f)
    {
      rb.AddForce(direction * force + Vector3.up * force * 0.15f, ForceMode.VelocityChange);
    }
  }

  // ---------- Movimiento ----------

  private void Move(Vector3 delta)
  {
    if (_rb != null && !_rb.isKinematic)
    {
      _rb.position += delta;
      _rb.linearVelocity = new Vector3(0f, _rb.linearVelocity.y, 0f);
    }
    else
    {
      transform.position += delta;
    }
  }

  private void SetRotation(Quaternion rotation)
  {
    if (_rb != null && !_rb.isKinematic)
    {
      _rb.rotation = rotation;
    }

    transform.rotation = rotation;
  }

  private void RestoreModel(Vector3 rest)
  {
    if (_model != null)
    {
      _model.localPosition = rest;
    }
  }

  private bool IsOwnCollider(Collider c) => c.transform.IsChildOf(transform);

  private static bool IsPlayer(Collider c) => c.GetComponentInParent<Player>() != null;

  /// <summary>Cosas que no cuentan como muro: cajas que rompe y objetos sueltos.</summary>
  private bool WillBreak(Collider c)
  {
    // Pedazos de cajas y objetos sueltos (con fisica) no lo frenan.
    if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic)
    {
      return true;
    }

    if (!_breakBoxes)
    {
      return false;
    }

    var breakable = c.GetComponentInParent<NuiGrab.Breakable>();
    return breakable != null && !breakable.IsBroken && (!breakable.OnlySword || _breakSwordOnly);
  }

  private static Vector3 Flat(Vector3 v)
  {
    v.y = 0f;
    return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
  }

  // ---------- Animacion y sonido ----------

  private void SetAnim(bool charge, bool dash)
  {
    if (_animator == null)
    {
      return;
    }

    if (_hasChargeParam) _animator.SetBool(ChargeId, charge);
    if (_hasDashParam) _animator.SetBool(DashId, dash);
  }

  private void Play(AudioClip clip)
  {
    if (clip != null)
    {
      AudioSource.PlayClipAtPoint(clip, transform.position, _volume);
    }
  }

  // ---------- Linea indicadora ----------

  private void CreateIndicator()
  {
    if (!_showIndicator)
    {
      return;
    }

    var go = new GameObject($"{name}_Embestida");
    _line = go.AddComponent<LineRenderer>();
    _line.useWorldSpace = true;
    _line.positionCount = IndicatorPoints;
    _line.alignment = LineAlignment.TransformZ;
    go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // plana sobre el suelo
    _line.numCapVertices = 2;
    _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    _line.receiveShadows = false;
    _line.sharedMaterial = _indicatorMaterial != null ? _indicatorMaterial : DefaultMaterial();

    // Forma de flecha: ancho constante y punta al final.
    var curve = new AnimationCurve(
      new Keyframe(0f, 0.55f),
      new Keyframe(0.82f, 0.55f),
      new Keyframe(0.83f, 1f),
      new Keyframe(1f, 0f));
    _line.widthCurve = curve;
    _line.widthMultiplier = _indicatorWidth;
    _line.enabled = false;
  }

  private static Material DefaultMaterial()
  {
    if (s_defaultMaterial == null)
    {
      var shader = Shader.Find("Sprites/Default");
      s_defaultMaterial = shader != null ? new Material(shader) { name = "ChargeIndicator" } : null;
    }

    return s_defaultMaterial;
  }

  private void UpdateIndicator(bool locked, float portion, float alphaScale = 1f)
  {
    if (_line == null)
    {
      return;
    }

    _line.enabled = true;

    // Largo real: hasta el primer obstaculo.
    var origin = transform.position + Vector3.up * _bodyHeight;
    _indicatorLength = _dashDistance;

    if (Physics.SphereCast(origin, _bodyRadius, _dashDirection, out var hit, _dashDistance, _obstacleMask, QueryTriggerInteraction.Ignore)
        && hit.normal.y < 0.5f && !IsOwnCollider(hit.collider) && !IsPlayer(hit.collider) && !WillBreak(hit.collider))
    {
      _indicatorLength = Mathf.Max(0.5f, hit.distance + _bodyRadius * 0.5f);
    }

    var length = Mathf.Max(0.3f, _indicatorLength * _indicatorFill);
    var start = transform.position;

    for (var i = 0; i < IndicatorPoints; i++)
    {
      var p = start + _dashDirection * (length * i / (IndicatorPoints - 1));
      p.y = GroundY(p) + 0.06f;
      _line.SetPosition(i, p);
    }

    // Parpadeo que se acelera al acercarse la embestida.
    var color = locked ? _indicatorLockedColor : _indicatorColor;
    var blink = 0.75f + 0.25f * Mathf.Sin(Time.time * Mathf.Lerp(8f, 30f, portion));
    color.a *= blink * alphaScale;
    var start0 = color;
    start0.a *= 0.35f;
    _line.startColor = start0;
    _line.endColor = color;
  }

  private float GroundY(Vector3 p)
  {
    if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 8f, _groundMask, QueryTriggerInteraction.Ignore)
        && !IsOwnCollider(hit.collider))
    {
      return hit.point.y;
    }

    return transform.position.y;
  }

  private void HideIndicator()
  {
    if (_line != null)
    {
      _line.enabled = false;
    }
  }

  private void OnDrawGizmosSelected()
  {
    Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.6f);
    Gizmos.DrawWireSphere(transform.position, _maxRange);
    Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.8f);
    Gizmos.DrawLine(transform.position + Vector3.up * 0.1f, transform.position + Vector3.up * 0.1f + transform.forward * _dashDistance);
  }
}
