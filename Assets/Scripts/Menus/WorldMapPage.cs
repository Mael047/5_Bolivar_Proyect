// WorldMapPage.cs
// Pantalla "Mapa" del menu de pausa: muestra el mapa horneado (pergamino),
// la capa pintada a mano si existe, la posicion del jugador, los marcadores
// (MapMarker con "Mostrar en mapa") y una leyenda que se arma sola con las
// categorias.
//
// Controles (solo cuando esta pantalla esta de frente):
//   Zoom ........ Rueda del raton | Q / E | L2 / R2
//   Mover ....... Arrastrar con el raton | Flechas | Stick derecho
//   Centrar ..... C | R3
//
// Va en la pantalla Mapa (junto a PauseMenuPage). El aspecto se diseña en el
// prefab PauseMenuUI; aqui solo se conectan las piezas.
// Tools > Mapa > Construir pantalla de Mapa en el menu de pausa la crea.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Menus
{
  [RequireComponent(typeof(PauseMenuPage))]
  [DisallowMultipleComponent]
  public class WorldMapPage : MonoBehaviour, IDragHandler, IScrollHandler
  {
    [Serializable]
    public class CategoryStyle
    {
      public MapMarkerCategory category;
      [Tooltip("Nombre en la leyenda.")]
      public string label = "Punto de interés";
      public Sprite icon;
      public Color color = Color.white;
      [Min(4f)] public float size = 28f;
      public bool showInLegend = true;
    }

    [Header("Datos")]
    [SerializeField, Tooltip("Mapa horneado (Assets/UI/Mapa/WorldMapData).")]
    private WorldMapData _data;
    [SerializeField, Tooltip("Vacio = objeto con tag Player.")]
    private Transform _player;

    [Header("Referencias (del prefab)")]
    [SerializeField, Tooltip("Zona visible del mapa (con RectMask2D).")]
    private RectTransform _viewport;
    [SerializeField, Tooltip("Contenido que se mueve y escala (hijo del viewport).")]
    private RectTransform _content;
    [SerializeField] private RawImage _baseImage;
    [SerializeField] private RawImage _overlayImage;
    [SerializeField] private RectTransform _iconsRoot;
    [SerializeField] private Image _iconTemplate;
    [SerializeField] private RectTransform _playerIcon;
    [SerializeField, Tooltip("Texto que se muestra si aun no se ha horneado el mapa.")]
    private GameObject _notBakedMessage;

    [Header("Leyenda")]
    [SerializeField, Tooltip("Contenedor de las filas (con VerticalLayoutGroup).")]
    private RectTransform _legendRoot;
    [SerializeField, Tooltip("Fila base: un hijo Image llamado 'Icono' y un Text llamado 'Nombre'.")]
    private RectTransform _legendRowTemplate;
    [SerializeField] private string _playerLabel = "Tú";

    [SerializeField] private CategoryStyle[] _categoryStyles =
    {
      new CategoryStyle { category = MapMarkerCategory.PuntoDeInteres, label = "Puntos de interés", color = new Color(0.95f, 0.75f, 0.2f) },
      new CategoryStyle { category = MapMarkerCategory.Puerta, label = "Puertas", color = new Color(0.3f, 0.55f, 0.85f) },
      new CategoryStyle { category = MapMarkerCategory.Objetivo, label = "Objetivos", color = new Color(0.85f, 0.2f, 0.2f), size = 34f },
      new CategoryStyle { category = MapMarkerCategory.Enemigo, label = "Enemigos", color = new Color(0.6f, 0.1f, 0.15f), size = 18f, showInLegend = false },

    };

    [Header("Zoom y movimiento")]
    [SerializeField, Min(1f)] private float _minZoom = 1f;
    [SerializeField, Min(1f)] private float _maxZoom = 4f;
    [SerializeField, Min(1f), Tooltip("Zoom al abrir el mapa.")]
    private float _openZoom = 1.6f;
    [SerializeField, Tooltip("Al abrir, centrar en el jugador.")]
    private bool _centerOnOpen = true;
    [SerializeField, Min(0.1f)] private float _zoomSpeed = 2f;
    [SerializeField, Min(10f), Tooltip("Pixeles por segundo al mover con stick/teclas.")]
    private float _panSpeed = 700f;
    [SerializeField, Range(0f, 0.5f), Tooltip("Suavizado del movimiento.")]
    private float _smoothing = 0.08f;

    private PauseMenu _menu;
    private PauseMenuPage _page;
    private bool _wasActive;

    private float _zoom = 1f;
    private float _targetZoom = 1f;
    private Vector2 _offset;
    private Vector2 _targetOffset;
    private Vector2 _offsetVelocity;
    private float _zoomVelocity;
    private Vector2 _baseSize;

    private InputAction _zoomAction;
    private InputAction _panAction;
    private InputAction _centerAction;

    private readonly Dictionary<MapMarker, Image> _icons = new Dictionary<MapMarker, Image>();
    private readonly List<Image> _freeIcons = new List<Image>();
    private readonly List<MapMarker> _stale = new List<MapMarker>();

    private void Awake()
    {
      _menu = GetComponentInParent<PauseMenu>(true);
      _page = GetComponent<PauseMenuPage>();

      if (_iconTemplate != null)
      {
        _iconTemplate.gameObject.SetActive(false);
      }

      _zoomAction = new InputAction("MapZoom", InputActionType.Value);
      _zoomAction.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/q").With("Positive", "<Keyboard>/e");
      _zoomAction.AddCompositeBinding("1DAxis").With("Negative", "<Gamepad>/leftTrigger").With("Positive", "<Gamepad>/rightTrigger");

      _panAction = new InputAction("MapPan", InputActionType.Value);
      _panAction.AddBinding("<Gamepad>/rightStick");
      _panAction.AddCompositeBinding("2DVector")
        .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
        .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");

      _centerAction = new InputAction("MapCenter", InputActionType.Button);
      _centerAction.AddBinding("<Keyboard>/c");
      _centerAction.AddBinding("<Gamepad>/rightStickPress");
      _centerAction.performed += _ => { if (IsActive) CenterOnPlayer(false); };

      BuildLegend();
    }

    private void OnEnable()
    {
      _zoomAction.Enable();
      _panAction.Enable();
      _centerAction.Enable();
      ApplyData();
    }

    private void OnDisable()
    {
      _zoomAction.Disable();
      _panAction.Disable();
      _centerAction.Disable();
    }

    private void OnDestroy()
    {
      _zoomAction?.Dispose();
      _panAction?.Dispose();
      _centerAction?.Dispose();
    }

    /// <summary>Cambia el mapa en tiempo de juego (por ejemplo otra zona).</summary>
    public void SetData(WorldMapData data)
    {
      _data = data;
      ApplyData();
    }

    private bool Baked => _data != null && _data.IsBaked;

    /// <summary>Esta pantalla esta de frente con el menu abierto.</summary>
    private bool IsActive =>
      PauseMenu.IsOpen && _menu != null && _menu.Current == _page
      && !Collectibles.CollectibleInspector.IsOpen && !CameraSetup.IsOpen;

    private void ApplyData()
    {
      var baked = Baked;

      if (_notBakedMessage != null)
      {
        _notBakedMessage.SetActive(!baked);
      }

      if (_content != null)
      {
        _content.gameObject.SetActive(baked);
      }

      if (!baked)
      {
        return;
      }

      if (_baseImage != null)
      {
        _baseImage.texture = _data.baseMap;
      }

      if (_overlayImage != null)
      {
        _overlayImage.texture = _data.overlay;
        _overlayImage.enabled = _data.overlay != null;
      }
    }

    // ---------- Cada frame ----------

    private void LateUpdate()
    {
      if (!PauseMenu.IsOpen || !Baked || _viewport == null || _content == null)
      {
        _wasActive = false;
        return;
      }

      if (_player == null)
      {
        var go = GameObject.FindWithTag("Player");
        _player = go != null ? go.transform : null;
      }

      FitContent();
      var active = IsActive;

      if (active && !_wasActive)
      {
        _targetZoom = _zoom = Mathf.Clamp(_openZoom, _minZoom, _maxZoom);

        if (_centerOnOpen)
        {
          CenterOnPlayer(true);
        }
      }

      _wasActive = active;

      if (active)
      {
        HandleInput();
      }

      var dt = Time.unscaledDeltaTime;
      _zoom = _smoothing > 0f ? Mathf.SmoothDamp(_zoom, _targetZoom, ref _zoomVelocity, _smoothing, Mathf.Infinity, dt) : _targetZoom;
      _targetOffset = ClampOffset(_targetOffset, _targetZoom);
      _offset = _smoothing > 0f ? Vector2.SmoothDamp(_offset, _targetOffset, ref _offsetVelocity, _smoothing, Mathf.Infinity, dt) : _targetOffset;
      _offset = ClampOffset(_offset, _zoom);

      _content.localScale = new Vector3(_zoom, _zoom, 1f);
      _content.anchoredPosition = _offset;

      UpdatePlayer();
      UpdateMarkers();
    }

    private void HandleInput()
    {
      var zoom = _zoomAction.ReadValue<float>();

      if (Mathf.Abs(zoom) > 0.1f)
      {
        _targetZoom = Mathf.Clamp(_targetZoom * Mathf.Pow(1f + _zoomSpeed * 0.5f, zoom * Time.unscaledDeltaTime * 2f), _minZoom, _maxZoom);
      }

      var pan = _panAction.ReadValue<Vector2>();

      if (pan.sqrMagnitude > 0.04f)
      {
        // Mover la vista hacia la derecha = el contenido va a la izquierda.
        _targetOffset -= pan * (_panSpeed * Time.unscaledDeltaTime);
      }
    }

    public void OnScroll(PointerEventData eventData)
    {
      if (!IsActive || !Baked)
      {
        return;
      }

      var steps = Mathf.Clamp(eventData.scrollDelta.y, -3f, 3f);
      var newZoom = Mathf.Clamp(_targetZoom * Mathf.Pow(1.15f, steps * _zoomSpeed * 0.5f), _minZoom, _maxZoom);

      // Zoom hacia el cursor.
      if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, eventData.position, eventData.pressEventCamera, out var local))
      {
        var mapPoint = (local - _targetOffset) / _targetZoom;
        _targetOffset = local - mapPoint * newZoom;
      }

      _targetZoom = newZoom;
    }

    public void OnDrag(PointerEventData eventData)
    {
      if (!IsActive || !Baked)
      {
        return;
      }

      var canvas = _viewport.GetComponentInParent<Canvas>();
      var scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
      _targetOffset += eventData.delta / Mathf.Max(0.01f, scale);
      _offset = _targetOffset;
      _offsetVelocity = Vector2.zero;
    }

    // ---------- Encuadre ----------

    /// <summary>A zoom 1 el mapa entero cabe en el viewport.</summary>
    private void FitContent()
    {
      var view = _viewport.rect.size;
      var aspect = _data.Aspect;
      var size = view.x / view.y > aspect ? new Vector2(view.y * aspect, view.y) : new Vector2(view.x, view.x / aspect);

      if (size != _baseSize)
      {
        _baseSize = size;
        _content.anchorMin = _content.anchorMax = new Vector2(0.5f, 0.5f);
        _content.pivot = new Vector2(0.5f, 0.5f);
        _content.sizeDelta = size;
      }
    }

    private Vector2 ClampOffset(Vector2 offset, float zoom)
    {
      var view = _viewport.rect.size;
      var map = _baseSize * zoom;
      var limit = new Vector2(Mathf.Max(0f, (map.x - view.x) * 0.5f), Mathf.Max(0f, (map.y - view.y) * 0.5f));
      return new Vector2(Mathf.Clamp(offset.x, -limit.x, limit.x), Mathf.Clamp(offset.y, -limit.y, limit.y));
    }

    private Vector2 ToMap(Vector3 world)
    {
      var uv = _data.WorldToUV(world);
      return new Vector2((uv.x - 0.5f) * _baseSize.x, (uv.y - 0.5f) * _baseSize.y);
    }

    private void CenterOnPlayer(bool instant)
    {
      if (_player == null)
      {
        return;
      }

      _targetOffset = ClampOffset(-ToMap(_player.position) * _targetZoom, _targetZoom);

      if (instant)
      {
        _offset = _targetOffset;
        _offsetVelocity = Vector2.zero;
      }
    }

    // ---------- Iconos ----------

    private void UpdatePlayer()
    {
      if (_playerIcon == null)
      {
        return;
      }

      var show = _player != null;
      _playerIcon.gameObject.SetActive(show);

      if (!show)
      {
        return;
      }

      _playerIcon.anchoredPosition = ToMap(_player.position);
      _playerIcon.localScale = Vector3.one / _zoom;
      _playerIcon.localRotation = Quaternion.Euler(0f, 0f, -_player.eulerAngles.y);
      _playerIcon.SetAsLastSibling();
    }

    private void UpdateMarkers()
    {
      foreach (var pair in _icons)
      {
        if (pair.Key == null || !pair.Key.isActiveAndEnabled || !pair.Key.ShowOnMap)
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

      var markers = MapMarker.Active;
      var inverse = Vector3.one / _zoom;

      for (var i = 0; i < markers.Count; i++)
      {
        var marker = markers[i];

        if (!marker.ShowOnMap)
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

        var rt = icon.rectTransform;
        rt.anchoredPosition = ToMap(marker.Position);
        rt.localScale = inverse;
        rt.localRotation = marker.RotateWithTarget ? Quaternion.Euler(0f, 0f, -marker.Yaw) : Quaternion.identity;
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
      var style = GetStyle(marker.Category);
      var sprite = marker.OverrideStyle ? marker.Icon : style?.icon;
      icon.sprite = sprite != null ? sprite : _iconTemplate.sprite;
      icon.color = marker.OverrideStyle ? marker.Color : style?.color ?? Color.white;
      var size = marker.OverrideStyle ? marker.Size * 1.3f : style?.size ?? 28f;
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

      if (_iconTemplate == null || _iconsRoot == null)
      {
        return null;
      }

      var icon = Instantiate(_iconTemplate, _iconsRoot);
      icon.gameObject.SetActive(true);
      icon.raycastTarget = false;
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

    // ---------- Leyenda ----------

    private void BuildLegend()
    {
      if (_legendRoot == null || _legendRowTemplate == null)
      {
        return;
      }

      _legendRowTemplate.gameObject.SetActive(false);

      var playerImage = _playerIcon != null ? _playerIcon.GetComponent<Image>() : null;

      if (playerImage != null)
      {
        AddLegendRow(_playerLabel, playerImage.sprite, playerImage.color);
      }

      if (_categoryStyles == null)
      {
        return;
      }

      foreach (var style in _categoryStyles)
      {
        if (style != null && style.showInLegend)
        {
          var sprite = style.icon != null ? style.icon : _iconTemplate != null ? _iconTemplate.sprite : null;
          AddLegendRow(style.label, sprite, style.color);
        }
      }
    }

    private void AddLegendRow(string label, Sprite sprite, Color color)
    {
      var row = Instantiate(_legendRowTemplate, _legendRoot);
      row.gameObject.SetActive(true);
      row.name = $"Leyenda_{label}";

      var icon = row.Find("Icono");
      var image = icon != null ? icon.GetComponent<Image>() : null;

      if (image != null)
      {
        image.sprite = sprite;
        image.color = color;
      }

      var nameTf = row.Find("Nombre");

      var text = nameTf != null ? nameTf.GetComponent<Graphic>() : null;

      if (text != null)
      {
        Collectibles.UIText.Set(text, label);
      }
    }
  }
}
