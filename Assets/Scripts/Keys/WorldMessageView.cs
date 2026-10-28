// WorldMessageView.cs
// Mensaje corto que aparece sobre un punto del mundo y se va solo
// ("Cerrada con llave", "Necesitas algo para abrirla"...).
// Va en la raiz del Canvas del prefab (por defecto
// Assets/UI/Puertas/DoorMessageUI). El diseño (panel, fuente, iconos) se hace
// en el prefab; el script lo coloca, escribe el texto y lo funde.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Canvas))]
[DisallowMultipleComponent]
public class WorldMessageView : MonoBehaviour
{
  private static readonly Dictionary<WorldMessageView, WorldMessageView> s_instances =
    new Dictionary<WorldMessageView, WorldMessageView>();

  [Tooltip("Elemento que se coloca sobre el objeto (su pivote se alinea con el punto).")]
  public RectTransform followRoot;

  [Tooltip("Apagado = el mensaje se queda donde lo pongas en el Canvas (p. ej. abajo al centro).")]
  public bool followTarget = true;

  [Tooltip("Desplazamiento extra en pantalla (pixeles de referencia).")]
  public Vector2 screenOffset;

  [Tooltip("Texto del mensaje (Text o TextMeshPro). Admite rich text.")]
  public Graphic messageLabel;

  [Range(1f, 30f)] public float fadeSpeed = 10f;

  [Tooltip("Pequeño salto al aparecer.")]
  public float popScale = 1.12f;

  private Canvas _canvas;
  private CanvasGroup _group;
  private Vector3 _worldPoint;
  private float _hideAt;
  private bool _showing;
  private float _pop;

  /// <summary>
  /// Muestra un mensaje usando una copia del prefab (una por prefab, se
  /// reutiliza). Si 'prefab' esta en la escena, se usa directamente.
  /// </summary>
  public static WorldMessageView Show(WorldMessageView prefab, string text, Vector3 worldPoint, float seconds)
  {
    if (prefab == null)
    {
      return null;
    }

    var view = prefab;

    if (!prefab.gameObject.scene.IsValid())
    {
      if (!s_instances.TryGetValue(prefab, out view) || view == null)
      {
        view = Instantiate(prefab);
        view.name = prefab.name;
        s_instances[prefab] = view;
      }
    }

    view.Show(text, worldPoint, seconds);
    return view;
  }

  private void Cache()
  {
    if (_group != null)
    {
      return;
    }

    _canvas = GetComponent<Canvas>();
    _group = GetComponent<CanvasGroup>();

    if (_group == null)
    {
      _group = gameObject.AddComponent<CanvasGroup>();
    }

    _group.blocksRaycasts = false;
    _group.interactable = false;
  }

  public void Show(string text, Vector3 worldPoint, float seconds)
  {
    Cache();

    if (messageLabel != null)
    {
      Collectibles.UIText.Set(messageLabel, text);
    }

    _worldPoint = worldPoint;
    _hideAt = Time.unscaledTime + Mathf.Max(0.1f, seconds);

    if (!_showing || !gameObject.activeSelf)
    {
      _group.alpha = 0f;
      _pop = 0f;
    }

    _showing = true;
    gameObject.SetActive(true);
    Place();
  }

  public void Hide()
  {
    _showing = false;
  }

  public bool IsShowing => _showing;

  private void LateUpdate()
  {
    Cache();

    if (_showing && Time.unscaledTime >= _hideAt)
    {
      _showing = false;
    }

    var visible = _showing && Place();
    var k = 1f - Mathf.Exp(-fadeSpeed * Time.unscaledDeltaTime);
    _group.alpha = Mathf.Lerp(_group.alpha, visible ? 1f : 0f, k);

    if (followRoot != null)
    {
      _pop = Mathf.Min(1f, _pop + Time.unscaledDeltaTime * 5f);
      var s = _pop < 0.5f ? Mathf.Lerp(0.85f, popScale, _pop * 2f) : Mathf.Lerp(popScale, 1f, (_pop - 0.5f) * 2f);
      followRoot.localScale = Vector3.one * s;
    }

    if (!_showing && _group.alpha < 0.01f)
    {
      _group.alpha = 0f;
      gameObject.SetActive(false);
    }
  }

  private bool Place()
  {
    if (!followTarget || followRoot == null)
    {
      return true;
    }

    var cam = Camera.main;

    if (cam == null)
    {
      return false;
    }

    var screenPoint = cam.WorldToScreenPoint(_worldPoint);

    if (screenPoint.z <= 0f)
    {
      return false;
    }

    var root = _canvas.rootCanvas;
    var uiCam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
    var parent = followRoot.parent as RectTransform;
    var point = (Vector2)screenPoint + screenOffset * root.scaleFactor;

    if (parent != null && RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, point, uiCam, out var world))
    {
      followRoot.position = world;
      return true;
    }

    return false;
  }
}
