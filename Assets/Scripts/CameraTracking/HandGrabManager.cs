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

using UnityEngine;

namespace NuiGrab
{
  public class HandGrabManager : MonoBehaviour
  {
    [SerializeField] private HandTracker _handTracker;
    [SerializeField] private Camera _camera;

    [SerializeField, Min(0f)] private float _grabRadius = 0.12f;
    [SerializeField, Min(0f)] private float _liftHeight = 0.25f;
    [SerializeField, Min(0f)] private float _followSpeed = 100f;
    [SerializeField, Min(0f)] private float _targetSmoothing = 50f;
    [SerializeField] private float _maxGrabDistance = 30f;
    [SerializeField] private LayerMask _grabLayerMask = 1 << 8;

    [SerializeField] private Color _hoverColor = new Color(0.35f, 1f, 0.5f);
    [SerializeField] private Color _holdingColor = new Color(1f, 0.95f, 0.3f);
    [SerializeField, Min(0f)] private float _emissionIntensity = 0.7f;
    [SerializeField, Range(1f, 30f)] private float _glowSmoothSpeed = 8f;

    private Rigidbody _held;
    private bool _isHolding;
    private bool _wasClosed;
    private float _groundY;
    private Vector3 _smoothedTarget;

    private MaterialPropertyBlock _block;
    private Renderer _glowRenderer;
    private Color _glowBase;
    private Color _glowEmission;

    public bool IsHolding => _isHolding;

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
    }

    private void Update()
    {
      var handVisible = _handTracker != null && _handTracker.IsHandDetected;
      var closed = _handTracker != null && _handTracker.IsHandClosed;

      if (_isHolding)
      {
        if (!handVisible || !closed)
        {
          Release();
        }
        else
        {
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
      var hand = _handTracker.HandViewportPosition;
      var ray = ScreenRay(hand);

      if (Physics.Raycast(ray, out var hit, _maxGrabDistance, _grabLayerMask, QueryTriggerInteraction.Ignore))
      {
        var vp = _camera.WorldToViewportPoint(hit.point);
        var dx = vp.x - hand.x;
        var dy = vp.y - hand.y;

        if (dx * dx + dy * dy > _grabRadius * _grabRadius)
        {
          return null;
        }

        var rb = hit.rigidbody != null ? hit.rigidbody : hit.collider.GetComponentInParent<Rigidbody>();
        return rb;
      }

      return null;
    }

    private void Grab(Rigidbody target)
    {
      _held = target;
      _isHolding = true;
      _groundY = _held.position.y;
      _smoothedTarget = _held.position;
      _held.isKinematic = true;
    }

    private void MoveToHand()
    {
      var hand = _handTracker.HandViewportPosition;
      var ray = ScreenRay(hand);
      var planeHeight = _groundY + _liftHeight;

      if (new Plane(Vector3.up, new Vector3(0f, planeHeight, 0f)).Raycast(ray, out var distance) && distance > 0f)
      {
        var desired = ray.GetPoint(distance);
        var dt = Mathf.Min(Time.deltaTime, 0.05f);

        var smoothing = 1f - Mathf.Exp(-_targetSmoothing * dt);
        _smoothedTarget = Vector3.Lerp(_smoothedTarget, desired, smoothing);

        var pos = Vector3.MoveTowards(_held.position, _smoothedTarget, _followSpeed * dt);
        _held.MovePosition(pos);
      }
    }

    private void Release()
    {
      if (_held != null)
      {
        _held.isKinematic = false;
        _held = null;
      }

      _isHolding = false;
      ClearGlow();
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
