// HandIndicator.cs
// Cursor de particulas que sigue la mano proyectado sobre el suelo (plano
// horizontal en _cursorPlaneY), respetando la inclinacion de la camara.
// Cambia de color segun el estado:
//   - abierta:  cian
//   - cerrada (puño): naranja
//   - cerrada y sobre el cubo: verde
//   - agarrando: amarillo
// Las particulas son pequeños destellos (puntitos tipo estrellas) que se emiten
// dentro de una esfera de radio _sphereRadius apoyada sobre el suelo, formando
// un balón de chispas en el punto donde apunta la mano (coincide con el agarre).

using UnityEngine;

namespace NuiGrab
{
  public class HandIndicator : MonoBehaviour
  {
    [SerializeField] private HandTracker _handTracker;
    [SerializeField] private Camera _camera;
    [SerializeField] private ParticleSystem _particles;
    [SerializeField, Min(0f)] private float _cursorPlaneY = 0.1f;
    [SerializeField, Range(1f, 30f)] private float _smoothSpeed = 15f;

    [SerializeField] private Color _openColor = new Color(0f, 1f, 1f);
    [SerializeField] private Color _closedColor = new Color(1f, 0.5f, 0f);
    [SerializeField] private Color _overCubeColor = new Color(0.3f, 1f, 0.4f);
    [SerializeField] private Color _holdingColor = new Color(1f, 0.92f, 0.2f);

    [SerializeField, Min(0f)] private float _lifetime = 0.35f;
    [SerializeField, Min(0f)] private float _size = 0.09f;
    [SerializeField, Min(0f)] private float _emissionRate = 80f;
    [SerializeField, Min(0f)] private float _sphereRadius = 0.25f;

    private ParticleSystem.MainModule _main;
    private ParticleSystem.EmissionModule _emission;
    private ParticleSystem.SizeOverLifetimeModule _sizeOverLifetime;
    private ParticleSystem.ColorOverLifetimeModule _colorOverLifetime;
    private ParticleSystemRenderer _renderer;
    private HandGrabManager _grabManager;
    private bool _wasVisible;

    private void Awake()
    {
      if (_camera == null)
      {
        _camera = Camera.main;
      }

      if (_handTracker == null)
      {
        _handTracker = FindAnyObjectByType<HandTracker>();
      }

      if (_particles == null)
      {
        _particles = GetComponent<ParticleSystem>();
      }

      _grabManager = FindAnyObjectByType<HandGrabManager>();

      if (_particles != null)
      {
        _main = _particles.main;
        _emission = _particles.emission;

        _main.simulationSpace = ParticleSystemSimulationSpace.World;
        _main.playOnAwake = false;
        _main.loop = true;
        _main.startLifetime = _lifetime;
        _main.startSpeed = 0f;
        _main.startSize = new ParticleSystem.MinMaxCurve(_size, _size * 1.8f);
        _main.startRotation = 0f;
        _main.maxParticles = 500;

        _emission.rateOverTime = _emissionRate;

        var shape = _particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = _sphereRadius;

        _sizeOverLifetime = _particles.sizeOverLifetime;
        _sizeOverLifetime.enabled = true;
        _sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
          new Keyframe(0f, 0.3f),
          new Keyframe(0.3f, 1f),
          new Keyframe(1f, 0f)
        ));

        _colorOverLifetime = _particles.colorOverLifetime;
        _colorOverLifetime.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(
          new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) }
        );
        _colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

        _renderer = _particles.GetComponent<ParticleSystemRenderer>();
        _renderer.renderMode = ParticleSystemRenderMode.Billboard;

        _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
      }
    }

    private void Update()
    {
      if (_handTracker == null || _camera == null || _particles == null)
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

      var planeY = _cursorPlaneY + _sphereRadius;

      if (new Plane(Vector3.up, new Vector3(0f, planeY, 0f)).Raycast(ray, out var hitDistance) && hitDistance > 0f)
      {
        var target = ray.GetPoint(hitDistance);
        transform.position = Vector3.Lerp(transform.position, target, _smoothSpeed * Time.deltaTime);
      }

      var color = _openColor;

      if (_handTracker.IsHandClosed)
      {
        color = _closedColor;
      }

      if (_grabManager != null)
      {
        if (_grabManager.IsHolding)
        {
          color = _holdingColor;
        }
        else if (_handTracker.IsHandClosed && _grabManager.IsHoveringObject)
        {
          color = _overCubeColor;
        }
      }

      _main.startColor = color;
    }

    private void SetMarkerVisible(bool visible)
    {
      if (_wasVisible == visible)
      {
        return;
      }

      _wasVisible = visible;

      if (visible)
      {
        _particles.Clear(true);
        _particles.Play(true);
      }
      else
      {
        _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
      }
    }
  }
}
