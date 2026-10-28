// Minimap.cs
// Minimapa circular con el escenario en 3D (estilo Mario Kart World): una
// camara en perspectiva inclinada sigue al jugador y dibuja el nivel en una
// textura pequeña que se muestra recortada en circulo dentro del marco.
//
// Pensado para costar poco:
//   - Textura de baja resolucion (320 px por defecto).
//   - Se redibuja cada N frames (2 por defecto), no en todos.
//   - Sin sombras, sin post-procesado, sin antialiasing, sin texturas de
//     profundidad/color.
//   - Solo para esta camara: sin pasto del terreno, terreno en menor detalle,
//     LODs mas bajos. La camara principal no se ve afectada.
//   - Si nadie lo ve (pausa, menus, inspeccion) no se dibuja nada.
//
// Iconos: cualquier objeto con MapMarker aparece en el minimapa. Los enemigos
// se marcan solos (opcional). El aspecto de cada categoria se configura en
// "Estilos por categoria".
//
// Va en la raiz del Canvas del prefab MinimapUI. Todo el aspecto (marco,
// tamaño, posicion, iconos) se diseña en el prefab.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public class Minimap : MonoBehaviour
{
  public enum RotationMode
  {
    [Tooltip("Arriba en el minimapa = hacia donde mira la camara del juego (recomendado con camara fija).")]
    SeguirCamara,
    [Tooltip("El norte siempre arriba.")]
    NorteFijo,
    [Tooltip("Gira con el jugador (como Mario Kart).")]
    SeguirJugador
  }

  public enum TerrainVegetation
  {
    [Tooltip("Arboles y pasto (lo mas caro).")]
    Todo,
    [Tooltip("Arboles si, pasto/flores no.")]
    SinPasto,
    [Tooltip("Ni arboles ni pasto (lo mas barato).")]
    Nada
  }

  [Serializable]
  public class CategoryStyle
  {
    public MapMarkerCategory category;
    public Sprite icon;
    public Color color = Color.white;
    [Min(4f)] public float size = 22f;
    [Tooltip("Fuera del minimapa se queda pegado al borde.")]
    public bool clampToEdge;
  }

  [Header("Referencias (del prefab)")]
  [SerializeField, Tooltip("Imagen circular donde se dibuja el escenario.")]
  private RawImage _view;
  [SerializeField, Tooltip("Contenedor de los iconos (hijo de la vista).")]
  private RectTransform _iconsRoot;
  [SerializeField, Tooltip("Icono base que se copia para cada marcador (puede estar desactivado).")]
  private Image _iconTemplate;
  [SerializeField, Tooltip("Flecha del jugador.")]
  private RectTransform _playerIcon;
  [SerializeField, Tooltip("Indicador del norte (opcional).")]
  private RectTransform _northIcon;
  [SerializeField, Tooltip("A quien sigue el minimapa (vacio = objeto con tag Player).")]
  private Transform _target;

  [Header("Vista 3D")]
  [SerializeField] private RotationMode _rotation = RotationMode.SeguirCamara;
  [SerializeField, Range(-180f, 180f), Tooltip("Giro usado en Norte Fijo.")]
  private float _northYaw;
  [SerializeField, Range(15f, 90f), Tooltip("Inclinacion. 90 = totalmente desde arriba, bajo = mas 3D.")]
  private float _pitch = 55f;
  [SerializeField, Range(10f, 90f)] private float _fieldOfView = 35f;
  [SerializeField, Min(1f), Tooltip("Distancia de la camara al jugador (zoom).")]
  private float _distance = 70f;
  [SerializeField, Tooltip("Altura del punto que mira la camara, sobre el jugador.")]
  private float _focusHeight;
  [SerializeField, Range(0f, 1f), Tooltip("Suavizado del seguimiento (0 = pegado).")]
  private float _smoothing = 0.08f;
  [SerializeField, Tooltip("Capas que se ven en el minimapa (quita UI, efectos, etc.).")]
  private LayerMask _cullingMask = ~(1 << 5);
  [SerializeField, Min(1f), Tooltip("Cuanto se ve mas alla del jugador.")]
  private float _viewDepth = 60f;
  [SerializeField] private Color _backgroundColor = new Color(0.55f, 0.75f, 0.9f, 1f);

  [Header("Recorte circular")]
  [SerializeField, Range(0.3f, 0.5f), Tooltip("Radio del circulo visible (0.5 = toca los bordes de la vista).")]
  private float _circleRadius = 0.5f;
  [SerializeField, Range(0f, 0.2f), Tooltip("Borde suave del circulo.")]
  private float _circleSoftness = 0.02f;

  [Header("Rendimiento")]
  [SerializeField, Range(64, 1024), Tooltip("Resolucion de la textura del minimapa.")]
  private int _resolution = 320;
  [SerializeField, Range(1, 6), Tooltip("Redibujar cada N frames (2 = la mitad de costo).")]
  private int _renderEvery = 2;
  [SerializeField] private TerrainVegetation _terrainVegetation = TerrainVegetation.SinPasto;
  [SerializeField, Range(1f, 200f), Tooltip("Error del terreno para esta camara (alto = menos triangulos).")]
  private float _terrainPixelError = 30f;
  [SerializeField, Tooltip("Usa la textura simplificada del terreno (shader mas barato).")]
  private bool _terrainBasemap = true;
  [SerializeField, Range(0.05f, 1f), Tooltip("Multiplica el LOD Bias solo para esta camara (bajo = modelos mas simples).")]
  private float _lodBiasScale = 0.4f;
  [SerializeField, Tooltip("Renderer de URP a usar (-1 = el por defecto). Puedes crear uno sin features extra para el minimapa.")]
  private int _rendererIndex = -1;

  [Header("Iconos")]
  [SerializeField] private CategoryStyle[] _categoryStyles =
  {
    new CategoryStyle { category = MapMarkerCategory.PuntoDeInteres, color = new Color(1f, 0.85f, 0.3f), size = 22f },
    new CategoryStyle { category = MapMarkerCategory.Puerta, color = new Color(0.55f, 0.85f, 1f), size = 20f },
    new CategoryStyle { category = MapMarkerCategory.Objetivo, color = new Color(1f, 0.35f, 0.3f), size = 28f, clampToEdge = true },
    new CategoryStyle { category = MapMarkerCategory.Enemigo, color = new Color(0.9f, 0.2f, 0.25f), size = 14f },
  };
  [SerializeField, Tooltip("Marca automaticamente a todos los enemigos.")]
  private bool _autoMarkEnemies = true;
  [SerializeField, Min(0f), Tooltip("Separacion de los iconos pegados al borde.")]
  private float _edgePadding = 14f;
  [SerializeField, Range(0.5f, 1.5f), Tooltip("Distancia del indicador del norte al centro (1 = en el borde del circulo).")]
  private float _northDistance = 0.92f;

  [Header("Aparecer / ocultar")]
  [SerializeField, Min(0.1f)] private float _fadeSpeed = 8f;

  private static readonly int s_RadiusId = Shader.PropertyToID("_Radius");
  private static readonly int s_SoftnessId = Shader.PropertyToID("_Softness");

  private Camera _camera;
  private RenderTexture _rt;
  private CanvasGroup _group;
  private Camera _mainCamera;
  private Material _viewMaterial;

  private Vector3 _focus;
  private Vector3 _focusVelocity;
  private float _yaw;
  private float _yawVelocity;
  private bool _hasPose;
  private float _cameraYaw;

  private readonly Dictionary<MapMarker, Image> _icons = new Dictionary<MapMarker, Image>();
  private readonly List<Image> _freeIcons = new List<Image>();
  private readonly List<MapMarker> _stale = new List<MapMarker>();
  private float _nextEnemyScan;

  private struct TerrainState
  {
    public Terrain terrain;
    public float pixelError;
    public float basemapDistance;
    public float detailDistance;
    public bool drawFoliage;
  }

  private readonly List<TerrainState> _terrainStates = new List<TerrainState>();
  private float _savedLodBias;

  /// <summary>Camara del minimapa (por si otro sistema la necesita).</summary>
  public Camera Camera => _camera;

  /// <summary>Cambia el zoom en tiempo de juego.</summary>
  public void SetDistance(float distance) => _distance = Mathf.Max(1f, distance);

  private void Awake()
  {
    _group = GetComponent<CanvasGroup>();
    _group.alpha = 0f;
    _group.blocksRaycasts = false;
    _group.interactable = false;

    if (_iconTemplate != null)
    {
      _iconTemplate.gameObject.SetActive(false);
    }
  }

  private void Start()
  {
    if (_view == null)
    {
      Debug.LogWarning("[Minimap] Falta la RawImage de la vista.", this);
      enabled = false;
      return;
    }

    if (_iconsRoot == null)
    {
      _iconsRoot = _view.rectTransform;
    }

    FindTarget();
    CreateCamera();
    CreateTexture();
    ApplyCircle();
  }

  private void OnEnable()
  {
    RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    RenderPipelineManager.endCameraRendering += OnEndCamera;
  }

  private void OnDisable()
  {
    RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
    RenderPipelineManager.endCameraRendering -= OnEndCamera;

    if (_camera != null)
    {
      _camera.enabled = false;
    }
  }

  private void OnDestroy()
  {
    if (_camera != null)
    {
      Destroy(_camera.gameObject);
    }

    if (_rt != null)
    {
      _rt.Release();
      Destroy(_rt);
    }

    if (_viewMaterial != null)
    {
      Destroy(_viewMaterial);
    }
  }

  private void OnValidate()
  {
    if (Application.isPlaying)
    {
      ApplyCircle();

      if (_camera != null)
      {
        _camera.fieldOfView = _fieldOfView;
        _camera.cullingMask = _cullingMask;
        _camera.backgroundColor = _backgroundColor;
      }

      if (_rt != null && _rt.width != _resolution)
      {
        CreateTexture();
      }
    }
  }

  // ---------- Montaje ----------

  private void FindTarget()
  {
    if (_target == null)
    {
      var player = GameObject.FindWithTag("Player");
      _target = player != null ? player.transform : null;
    }
  }

  private void CreateCamera()
  {
    var go = new GameObject("MinimapCamera");
    _camera = go.AddComponent<Camera>();
    _camera.enabled = false;
    _camera.clearFlags = CameraClearFlags.SolidColor;
    _camera.backgroundColor = _backgroundColor;
    _camera.cullingMask = _cullingMask;
    _camera.fieldOfView = _fieldOfView;
    _camera.nearClipPlane = 1f;
    _camera.allowHDR = false;
    _camera.allowMSAA = false;
    _camera.useOcclusionCulling = true;
    _camera.depth = -50f;

    var data = _camera.GetUniversalAdditionalCameraData();
    data.renderShadows = false;
    data.renderPostProcessing = false;
    data.antialiasing = AntialiasingMode.None;
    data.requiresDepthOption = CameraOverrideOption.Off;
    data.requiresColorOption = CameraOverrideOption.Off;
    data.stopNaN = false;
    data.dithering = false;

    if (_rendererIndex >= 0)
    {
      data.SetRenderer(_rendererIndex);
    }
  }

  private void CreateTexture()
  {
    if (_rt != null)
    {
      _rt.Release();
      Destroy(_rt);
    }

    _rt = new RenderTexture(_resolution, _resolution, 16, RenderTextureFormat.ARGB32)
    {
      name = "MinimapRT",
      antiAliasing = 1,
      useMipMap = false,
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Clamp
    };
    _rt.Create();

    if (_camera != null)
    {
      _camera.targetTexture = _rt;
    }

    _view.texture = _rt;
  }

  private void ApplyCircle()
  {
    if (_view == null || _view.material == null || _view.material == _view.defaultMaterial)
    {
      return;
    }

    if (_viewMaterial == null)
    {
      // Copia propia para no modificar el material del proyecto.
      _viewMaterial = new Material(_view.material) { name = "MinimapCircle (instancia)" };
      _view.material = _viewMaterial;
    }

    _viewMaterial.SetFloat(s_RadiusId, _circleRadius);
    _viewMaterial.SetFloat(s_SoftnessId, _circleSoftness);
  }

  // ---------- Cada frame ----------

  private bool ShouldShow()
  {
    return _target != null
      && !Menus.PauseMenu.IsOpen
      && !Menus.MainMenu.IsOpen
      && !Menus.CameraSetup.IsOpen
      && !Collectibles.CollectibleInspector.IsOpen;
  }

  private void LateUpdate()
  {
    if (_camera == null)
    {
      return;
    }

    if (_target == null)
    {
      FindTarget();
    }

    var show = ShouldShow();
    _group.alpha = Mathf.MoveTowards(_group.alpha, show ? 1f : 0f, _fadeSpeed * Time.unscaledDeltaTime);

    if (_group.alpha <= 0f)
    {
      _camera.enabled = false;
      _hasPose = false;
      return;
    }

    if (_autoMarkEnemies && Time.unscaledTime >= _nextEnemyScan)
    {
      _nextEnemyScan = Time.unscaledTime + 1.5f;
      MarkEnemies();
    }

    // Solo se mueve la camara en los frames que dibuja, asi los iconos
    // siempre coinciden con la imagen.
    var render = !_hasPose || Time.frameCount % _renderEvery == 0;

    if (render && _target != null)
    {
      UpdateCameraPose();
    }

    _camera.enabled = render;
    UpdatePlayerIcon();
    UpdateNorthIcon();
    UpdateMarkers();
  }

  private void UpdateCameraPose()
  {
    var dt = Time.deltaTime * _renderEvery;
    var focus = _target.position + Vector3.up * _focusHeight;
    var targetYaw = DesiredYaw();

    if (!_hasPose || _smoothing <= 0f)
    {
      _focus = focus;
      _yaw = targetYaw;
      _focusVelocity = Vector3.zero;
      _yawVelocity = 0f;
      _hasPose = true;
    }
    else
    {
      _focus = Vector3.SmoothDamp(_focus, focus, ref _focusVelocity, _smoothing, Mathf.Infinity, dt);
      _yaw = Mathf.SmoothDampAngle(_yaw, targetYaw, ref _yawVelocity, _smoothing * 2f, Mathf.Infinity, dt);
    }

    var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    _camera.transform.SetPositionAndRotation(_focus - rotation * Vector3.forward * _distance, rotation);
    _camera.fieldOfView = _fieldOfView;
    _camera.farClipPlane = _distance + _viewDepth;
    _cameraYaw = _yaw;
  }

  private float DesiredYaw()
  {
    switch (_rotation)
    {
      case RotationMode.NorteFijo:
        return _northYaw;
      case RotationMode.SeguirJugador:
        return _target.eulerAngles.y;
      default:
        if (_mainCamera == null || !_mainCamera.isActiveAndEnabled)
        {
          _mainCamera = Camera.main;
        }

        return _mainCamera != null ? _mainCamera.transform.eulerAngles.y : _northYaw;
    }
  }

  // ---------- Iconos ----------

  private float VisibleRadius
  {
    get
    {
      var rect = _view.rectTransform.rect;
      return Mathf.Min(rect.width, rect.height) * _circleRadius;
    }
  }

  /// <summary>Posicion de un punto del mundo dentro de la vista (centro = 0,0).</summary>
  private Vector2 Project(Vector3 world)
  {
    var vp = _camera.WorldToViewportPoint(world);

    if (vp.z < 0f)
    {
      // Detras de la camara: se invierte para que quede en la direccion correcta.
      vp.x = 1f - vp.x;
      vp.y = 1f - vp.y;
      var dir = new Vector2(vp.x - 0.5f, vp.y - 0.5f).normalized * 10f;
      vp.x = 0.5f + dir.x;
      vp.y = 0.5f + dir.y;
    }

    var rect = _view.rectTransform.rect;
    return new Vector2((vp.x - 0.5f) * rect.width, (vp.y - 0.5f) * rect.height);
  }

  private void UpdatePlayerIcon()
  {
    if (_playerIcon == null || _target == null)
    {
      return;
    }

    _playerIcon.anchoredPosition = Vector2.ClampMagnitude(Project(_target.position), VisibleRadius);
    _playerIcon.localRotation = Quaternion.Euler(0f, 0f, _cameraYaw - _target.eulerAngles.y);
  }

  private void UpdateNorthIcon()
  {
    if (_northIcon == null)
    {
      return;
    }

    var angle = -_cameraYaw * Mathf.Deg2Rad;
    var r = VisibleRadius * _northDistance;
    _northIcon.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * r;
  }

  private void UpdateMarkers()
  {
    var radius = VisibleRadius;
    var markers = MapMarker.Active;

    foreach (var pair in _icons)
    {
      if (pair.Key == null || !pair.Key.isActiveAndEnabled || !pair.Key.ShowOnMinimap)
      {
        _stale.Add(pair.Key);
      }
    }

    foreach (var marker in _stale)
    {
      Release(_icons[marker]);
      _icons.Remove(marker);
    }

    _stale.Clear();

    for (var i = 0; i < markers.Count; i++)
    {
      var marker = markers[i];

      if (!marker.ShowOnMinimap)
      {
        continue;
      }

      if (!_icons.TryGetValue(marker, out var icon))
      {
        icon = Acquire();

        if (icon == null)
        {
          return;
        }

        Style(icon, marker);
        _icons.Add(marker, icon);
      }

      var clamp = marker.OverrideStyle ? marker.ClampToEdge : GetStyle(marker.Category)?.clampToEdge ?? false;
      var pos = Project(marker.Position);
      var limit = Mathf.Max(0f, radius - _edgePadding);
      var visible = true;

      if (pos.sqrMagnitude > limit * limit)
      {
        if (clamp)
        {
          pos = pos.normalized * limit;
        }
        else
        {
          visible = false;
        }
      }

      if (icon.enabled != visible)
      {
        icon.enabled = visible;
      }

      if (!visible)
      {
        continue;
      }

      var rt = icon.rectTransform;
      rt.anchoredPosition = pos;
      rt.localRotation = marker.RotateWithTarget
        ? Quaternion.Euler(0f, 0f, _cameraYaw - marker.Yaw)
        : Quaternion.identity;
    }
  }

  private CategoryStyle GetStyle(MapMarkerCategory category)
  {
    if (_categoryStyles == null)
    {
      return null;
    }

    foreach (var style in _categoryStyles)
    {
      if (style != null && style.category == category)
      {
        return style;
      }
    }

    return null;
  }

  private void Style(Image icon, MapMarker marker)
  {
    Sprite sprite;
    Color color;
    float size;

    if (marker.OverrideStyle)
    {
      sprite = marker.Icon;
      color = marker.Color;
      size = marker.Size;
    }
    else
    {
      var style = GetStyle(marker.Category);
      sprite = style?.icon;
      color = style?.color ?? Color.white;
      size = style?.size ?? 20f;
    }

    icon.sprite = sprite != null ? sprite : _iconTemplate.sprite;
    icon.color = color;
    icon.rectTransform.sizeDelta = new Vector2(size, size);
    icon.name = $"Icono_{marker.name}";
  }

  private Image Acquire()
  {
    if (_freeIcons.Count > 0)
    {
      var free = _freeIcons[_freeIcons.Count - 1];
      _freeIcons.RemoveAt(_freeIcons.Count - 1);
      free.gameObject.SetActive(true);
      return free;
    }

    if (_iconTemplate == null)
    {
      return null;
    }

    var icon = Instantiate(_iconTemplate, _iconsRoot);
    icon.gameObject.SetActive(true);
    icon.raycastTarget = false;

    // Los marcadores quedan debajo de la flecha del jugador.
    if (_playerIcon != null && _playerIcon.parent == _iconsRoot)
    {
      _playerIcon.SetAsLastSibling();
    }

    return icon;
  }

  private void Release(Image icon)
  {
    if (icon == null)
    {
      return;
    }

    icon.gameObject.SetActive(false);
    _freeIcons.Add(icon);
  }

  private void MarkEnemies()
  {
    foreach (var enemy in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
    {
      if (!enemy.TryGetComponent<MapMarker>(out _))
      {
        enemy.gameObject.AddComponent<MapMarker>().Setup(MapMarkerCategory.Enemigo, false);
      }
    }
  }

  // ---------- Ajustes solo para la camara del minimapa ----------

  private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
  {
    if (cam != _camera)
    {
      return;
    }

    _savedLodBias = QualitySettings.lodBias;
    QualitySettings.lodBias = _savedLodBias * _lodBiasScale;

    _terrainStates.Clear();

    foreach (var terrain in Terrain.activeTerrains)
    {
      if (terrain == null)
      {
        continue;
      }

      _terrainStates.Add(new TerrainState
      {
        terrain = terrain,
        pixelError = terrain.heightmapPixelError,
        basemapDistance = terrain.basemapDistance,
        detailDistance = terrain.detailObjectDistance,
        drawFoliage = terrain.drawTreesAndFoliage
      });

      terrain.heightmapPixelError = _terrainPixelError;

      if (_terrainBasemap)
      {
        terrain.basemapDistance = 0f;
      }

      switch (_terrainVegetation)
      {
        case TerrainVegetation.Nada:
          terrain.drawTreesAndFoliage = false;
          break;
        case TerrainVegetation.SinPasto:
          terrain.detailObjectDistance = 0f;
          break;
      }
    }
  }

  private void OnEndCamera(ScriptableRenderContext context, Camera cam)
  {
    if (cam != _camera)
    {
      return;
    }

    QualitySettings.lodBias = _savedLodBias;

    foreach (var state in _terrainStates)
    {
      if (state.terrain == null)
      {
        continue;
      }

      state.terrain.heightmapPixelError = state.pixelError;
      state.terrain.basemapDistance = state.basemapDistance;
      state.terrain.detailObjectDistance = state.detailDistance;
      state.terrain.drawTreesAndFoliage = state.drawFoliage;
    }

    _terrainStates.Clear();
  }
}
