// LockOnIndicator.cs
// Marcador estilo Tunic para el lock-on (va en el Player, junto a PlayerLockOn):
//   - Candidato: el enemigo que se fijaria al pulsar L1 muestra un marcador
//     flotante (color "candidato").
//   - Fijado: el mismo marcador cambia de color, hace un pequeño golpe de
//     escala y (si el prefab lo tiene) gira o muestra elementos extra.
// El aspecto se diseña en el prefab LockOnMarkerUI (LockOnMarkerView).
//
// Altura del marcador: si el enemigo tiene un hijo llamado "LockOnPoint" se
// usa ese punto; si no, la parte de arriba de sus renderers + Height Offset.

using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(1000)] // despues de la camara (TopDownCamera en LateUpdate)
[RequireComponent(typeof(PlayerLockOn))]
public class LockOnIndicator : MonoBehaviour
{
  private const string PointName = "LockOnPoint";

  [Header("Marcador")]
  [SerializeField, Tooltip("Prefab del marcador (con LockOnMarkerView).")]
  private LockOnMarkerView _markerPrefab;

  [SerializeField, Tooltip("Mostrar el marcador sobre el enemigo candidato antes de fijarlo.")]
  private bool _showCandidate = true;

  [SerializeField] private Color _candidateColor = new Color(1f, 1f, 1f, 0.85f);
  [SerializeField] private Color _lockedColor = new Color(1f, 0.55f, 0.15f, 1f);

  [SerializeField, Tooltip("Metros por encima de la cabeza del enemigo (si no tiene LockOnPoint).")]
  private float _heightOffset = 0.35f;

  [Header("Animacion")]
  [SerializeField, Range(1f, 30f)] private float _fadeSpeed = 14f;
  [SerializeField, Range(1f, 30f), Tooltip("Velocidad del cambio de color.")]
  private float _colorSpeed = 16f;
  [SerializeField, Tooltip("Pixeles que sube y baja el elemento 'Bob'.")]
  private float _bobAmplitude = 6f;
  [SerializeField] private float _bobSpeed = 3f;
  [SerializeField, Tooltip("Grados por segundo del elemento 'Spin When Locked'.")]
  private float _spinSpeed = 90f;
  [SerializeField, Tooltip("Escala maxima del golpe al fijar (1 = sin golpe).")]
  private float _lockPunchScale = 1.45f;
  [SerializeField, Min(0.01f)] private float _lockPunchDuration = 0.25f;
  [SerializeField, Tooltip("Velocidad con la que el marcador salta de un enemigo a otro. 0 = instantaneo.")]
  private float _retargetSpeed = 25f;

  private PlayerLockOn _lockOn;
  private Camera _camera;
  private LockOnMarkerView _marker;

  private Enemy _shown;
  private bool _shownLocked;
  private Vector3 _screenPos;
  private Color _color;
  private float _punchTime = -1f;
  private float _time;

  private readonly Dictionary<Enemy, Transform> _points = new Dictionary<Enemy, Transform>();
  private readonly List<Renderer> _rendererBuffer = new List<Renderer>();

  private void Awake()
  {
    _lockOn = GetComponent<PlayerLockOn>();

    if (_markerPrefab == null)
    {
      Debug.LogWarning("[LockOnIndicator] Falta el prefab del marcador (Tools > Lock-on > Agregar indicador al Player).", this);
      enabled = false;
      return;
    }

    _marker = Instantiate(_markerPrefab);
    _marker.name = _markerPrefab.name;
    _marker.Group.alpha = 0f;
    _color = _candidateColor;
    _marker.SetColor(_color);
    _marker.SetLocked(false);
  }

  private void OnDestroy()
  {
    if (_marker != null)
    {
      Destroy(_marker.gameObject);
    }
  }

  private void OnDisable()
  {
    if (_marker != null)
    {
      _marker.Group.alpha = 0f;
    }
  }

  private void LateUpdate()
  {
    if (_camera == null || !_camera.isActiveAndEnabled)
    {
      _camera = Camera.main;
    }

    var dt = Time.unscaledDeltaTime;
    _time += dt;

    var locked = _lockOn.IsLockedOn && _lockOn.Target != null;
    var target = locked ? _lockOn.Target : (_showCandidate ? _lockOn.Candidate : null);

    // Nada de marcadores en el menu ni durante la inspeccion de coleccionables.
    if (Menus.MainMenu.IsOpen || Collectibles.CollectibleInspector.IsOpen)
    {
      target = null;
    }

    var visible = false;

    if (target != null && _camera != null)
    {
      var screen = _camera.WorldToScreenPoint(MarkerPoint(target));

      if (screen.z > 0f)
      {
        // Salto suave al cambiar de enemigo; instantaneo al aparecer.
        if (_shown == null || _retargetSpeed <= 0f)
        {
          _screenPos = screen;
        }
        else
        {
          _screenPos = Vector3.Lerp(_screenPos, screen, 1f - Mathf.Exp(-_retargetSpeed * dt));
        }

        visible = _marker.Place(_screenPos);
      }

      if (locked && (!_shownLocked || _shown != target))
      {
        _punchTime = 0f;
      }

      if (locked != _shownLocked)
      {
        _marker.SetLocked(locked);
      }

      _shown = target;
      _shownLocked = locked;
    }

    // Fundido
    var group = _marker.Group;
    group.alpha = Mathf.Lerp(group.alpha, visible ? 1f : 0f, 1f - Mathf.Exp(-_fadeSpeed * dt));

    if (!visible && group.alpha < 0.01f)
    {
      _shown = null;
      _shownLocked = false;
    }

    // Color
    _color = Color.Lerp(_color, _shownLocked ? _lockedColor : _candidateColor, 1f - Mathf.Exp(-_colorSpeed * dt));
    _marker.SetColor(_color);

    Animate(dt);
  }

  private void Animate(float dt)
  {
    if (_marker.bob != null)
    {
      _marker.bob.anchoredPosition = new Vector2(0f, Mathf.Sin(_time * _bobSpeed * Mathf.PI) * _bobAmplitude);
    }

    if (_marker.spinWhenLocked != null && _shownLocked)
    {
      _marker.spinWhenLocked.Rotate(0f, 0f, -_spinSpeed * dt);
    }

    var punchTarget = _marker.punch != null ? _marker.punch : _marker.followRoot;

    if (punchTarget == null)
    {
      return;
    }

    var scale = 1f;

    if (_punchTime >= 0f)
    {
      _punchTime += dt;
      var t = Mathf.Clamp01(_punchTime / _lockPunchDuration);

      // Sube rapido y vuelve con un leve rebote.
      scale = Mathf.LerpUnclamped(_lockPunchScale, 1f, 1f - Mathf.Pow(1f - t, 3f));

      if (t >= 1f)
      {
        _punchTime = -1f;
      }
    }

    punchTarget.localScale = Vector3.one * scale;
  }

  private Vector3 MarkerPoint(Enemy enemy)
  {
    if (!_points.TryGetValue(enemy, out var point))
    {
      point = FindChild(enemy.transform, PointName);
      _points[enemy] = point;
    }

    if (point != null)
    {
      return point.position;
    }

    // Parte superior de los renderers del enemigo (sigue la animacion).
    enemy.GetComponentsInChildren(false, _rendererBuffer);
    var found = false;
    var top = enemy.transform.position.y;

    foreach (var r in _rendererBuffer)
    {
      if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
      {
        continue;
      }

      top = found ? Mathf.Max(top, r.bounds.max.y) : r.bounds.max.y;
      found = true;
    }

    var p = enemy.transform.position;
    p.y = (found ? top : p.y + 1.5f) + _heightOffset;
    return p;
  }

  private static Transform FindChild(Transform root, string childName)
  {
    foreach (var t in root.GetComponentsInChildren<Transform>(true))
    {
      if (t.name == childName)
      {
        return t;
      }
    }

    return null;
  }
}
