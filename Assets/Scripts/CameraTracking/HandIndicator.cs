// HandIndicator.cs
// Cursor que sigue la mano proyectado sobre el plano frente a la camara (estilo mano de Wii).
// Cambia de color segun el estado:
//   - abierta:  cian
//   - cerrada (puño): naranja
//   - cerrada y sobre el cubo: verde
//   - agarrando: amarillo

using UnityEngine;

namespace NuiGrab
{
  public class HandIndicator : MonoBehaviour
  {
    [SerializeField] private HandTracker _handTracker;
    [SerializeField] private Camera _camera;
    [SerializeField, Min(0f)] private float _holdDistance = 3f;
    [SerializeField, Range(1f, 30f)] private float _smoothSpeed = 15f;

    [SerializeField] private Color _openColor = new Color(0f, 1f, 1f);
    [SerializeField] private Color _closedColor = new Color(1f, 0.5f, 0f);
    [SerializeField] private Color _overCubeColor = new Color(0.3f, 1f, 0.4f);
    [SerializeField] private Color _holdingColor = new Color(1f, 0.92f, 0.2f);

    private Renderer _renderer;
    private Material _material;
    private HandGrabController _grabController;

    private void Awake()
    {
      _renderer = GetComponent<Renderer>();

      if (_renderer != null)
      {
        _material = _renderer.material;
        _material.EnableKeyword("_EMISSION");
      }

      if (_camera == null)
      {
        _camera = Camera.main;
      }

      if (_handTracker == null)
      {
        _handTracker = FindAnyObjectByType<HandTracker>();
      }

      _grabController = FindAnyObjectByType<HandGrabController>();
    }

    private void Update()
    {
      if (_handTracker == null || _camera == null || _material == null)
      {
        return;
      }

      if (!_handTracker.IsHandDetected)
      {
        SetMarkerVisible(false);
        return;
      }

      SetMarkerVisible(true);

      var hand = _handTracker.HandViewportPosition;
      var ray = _camera.ScreenPointToRay(new Vector3(hand.x * _camera.pixelWidth, hand.y * _camera.pixelHeight, 0f));

      var distance = _grabController != null ? _grabController.HoldDistance : _holdDistance;
      var planePoint = _camera.transform.position + _camera.transform.forward * distance;

      if (new Plane(-_camera.transform.forward, planePoint).Raycast(ray, out var hitDistance) && hitDistance > 0f)
      {
        var target = ray.GetPoint(hitDistance);
        transform.position = Vector3.Lerp(transform.position, target, _smoothSpeed * Time.deltaTime);
      }

      var look = _camera.transform.position - transform.position;
      if (look.sqrMagnitude > 0.0001f)
      {
        transform.rotation = Quaternion.FromToRotation(Vector3.up, look.normalized);
      }

      var color = _openColor;

      if (_handTracker.IsHandClosed)
      {
        color = _closedColor;
      }

      if (_grabController != null)
      {
        if (_grabController.IsHolding)
        {
          color = _holdingColor;
        }
        else if (_handTracker.IsHandClosed && _grabController.IsHandOverObject())
        {
          color = _overCubeColor;
        }
      }

      _material.SetColor("_BaseColor", color);
      _material.SetColor("_EmissionColor", color);
    }

    private void SetMarkerVisible(bool visible)
    {
      if (_renderer.enabled != visible)
      {
        _renderer.enabled = visible;
      }
    }
  }
}
