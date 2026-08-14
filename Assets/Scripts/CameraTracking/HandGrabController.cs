// HandGrabController.cs
// Mano abierta -> no agarra. Si la mano cerrada (puño) esta sobre el cubo y venia
// abierta, agarra el cubo; mientras esta agarrado sigue la mano. Al abrir la mano
// (o perderla) se suelta y cae por gravedad sobre el plano.

using UnityEngine;

namespace NuiGrab
{
  [RequireComponent(typeof(Rigidbody))]
  public class HandGrabController : MonoBehaviour
  {
    [SerializeField] private HandTracker _handTracker;
    [SerializeField] private Camera _camera;

    [SerializeField, Min(0f)] private float _grabRadius = 0.08f;
    [SerializeField, Min(0f)] private float _holdDistance = 3f;
    [SerializeField, Range(1f, 30f)] private float _smoothSpeed = 10f;

    [SerializeField] private Renderer _highlightRenderer;
    [SerializeField] private Color _hoverColor = new Color(0.35f, 1f, 0.5f);
    [SerializeField] private Color _holdingColor = new Color(1f, 0.95f, 0.3f);
    [SerializeField, Min(0f)] private float _emissionIntensity = 0.7f;
    [SerializeField, Range(1f, 30f)] private float _glowSmoothSpeed = 8f;

    private Rigidbody _rigidbody;
    private bool _isHolding;
    private bool _wasClosed;

    private Material _glowMaterial;
    private Color _originalColor;
    private Color _currentBase;
    private Color _currentEmission;

    public bool IsHolding => _isHolding;

    public float HoldDistance => _holdDistance;

    private void Awake()
    {
      _rigidbody = GetComponent<Rigidbody>();

      if (_camera == null)
      {
        _camera = Camera.main;
      }

      if (_handTracker == null)
      {
        _handTracker = FindAnyObjectByType<HandTracker>();
      }

      if (_highlightRenderer == null)
      {
        _highlightRenderer = GetComponent<Renderer>();
      }

      if (_highlightRenderer != null)
      {
        _glowMaterial = _highlightRenderer.material;
        _glowMaterial.EnableKeyword("_EMISSION");
        _originalColor = _glowMaterial.GetColor("_BaseColor");
        _currentBase = _originalColor;
        _currentEmission = Color.black;
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
      }
      else if (handVisible && closed && !_wasClosed && IsHandOverObject())
      {
        Grab();
      }

      _wasClosed = closed;

      UpdateGlow(handVisible);
    }

    public bool IsHandOverObject()
    {
      var hand = _handTracker.HandViewportPosition;
      var obj = _camera.WorldToViewportPoint(transform.position);
      var dx = hand.x - obj.x;
      var dy = hand.y - obj.y;
      return (dx * dx + dy * dy) <= _grabRadius * _grabRadius;
    }

    private void Grab()
    {
      _isHolding = true;
      _rigidbody.isKinematic = true;
    }

    private void UpdateGlow(bool handVisible)
    {
      if (_glowMaterial == null)
      {
        return;
      }

      var baseTarget = _originalColor;
      var emissionTarget = Color.black;

      if (_isHolding)
      {
        baseTarget = _holdingColor;
        emissionTarget = _holdingColor;
      }
      else if (handVisible && IsHandOverObject())
      {
        baseTarget = _hoverColor;
        emissionTarget = _hoverColor;
      }

      var t = _glowSmoothSpeed * Time.deltaTime;
      _currentBase = Color.Lerp(_currentBase, baseTarget, t);
      _currentEmission = Color.Lerp(_currentEmission, emissionTarget, t);

      _glowMaterial.SetColor("_BaseColor", _currentBase);
      _glowMaterial.SetColor("_EmissionColor", _currentEmission * _emissionIntensity);
    }

    private void Release()
    {
      _isHolding = false;
      _rigidbody.isKinematic = false;
    }

    private void MoveToHand()
    {
      var hand = _handTracker.HandViewportPosition;
      var ray = _camera.ScreenPointToRay(new Vector3(hand.x * _camera.pixelWidth, hand.y * _camera.pixelHeight, 0f));
      var planePoint = _camera.transform.position + _camera.transform.forward * _holdDistance;

      if (new Plane(-_camera.transform.forward, planePoint).Raycast(ray, out var distance) && distance > 0f)
      {
        var target = ray.GetPoint(distance);
        target = Vector3.Lerp(transform.position, target, _smoothSpeed * Time.deltaTime);
        _rigidbody.MovePosition(target);
      }
    }
  }
}
