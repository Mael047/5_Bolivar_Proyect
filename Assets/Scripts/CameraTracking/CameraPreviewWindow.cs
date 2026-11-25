// CameraPreviewWindow.cs
// Ventana provisional que muestra la camara en vivo. Si no se le asigna una
// RawImage en el Inspector, se autoconstruye un Canvas con panel + imagen al
// iniciar (solo en Play/Game). Espejada por defecto para que coincida con el
// marco de deteccion: mano derecha -> objeto a la derecha.
// La tecla P (configurable) la oculta / muestra; se recuerda entre partidas.

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace NuiGrab
{
  public class CameraPreviewWindow : MonoBehaviour
  {
    [SerializeField] private HandTracker _handTracker;
    [SerializeField] private RawImage _rawImage;
    [SerializeField] private bool _mirrorPreview = true;

    [SerializeField] private float _windowWidth = 340f;
    [SerializeField] private float _windowHeight = 200f;
    [SerializeField] private float _cornerMargin = 24f;
    [SerializeField] private Color _panelColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);

    [Header("Ocultar")]
    [SerializeField, Tooltip("Tecla que oculta / muestra la vista de la camara.")]
    private Key _toggleKey = Key.P;
    [SerializeField, Tooltip("Recordar si estaba oculta la proxima vez que se abra el juego.")]
    private bool _rememberChoice = true;

    private const string HiddenPref = "preview_camara_oculta";
    private CanvasGroup _windowGroup;
    private bool _hidden;

    private Texture _assignedTexture;
    private bool _flipped;

    private void Awake()
    {
      if (_handTracker == null)
      {
        _handTracker = FindAnyObjectByType<HandTracker>();
      }

      if (_rawImage == null)
      {
        _rawImage = BuildWindow();
      }

      // Lo que se oculta: la ventana entera (panel + imagen).
      if (_rawImage != null)
      {
        var root = _rawImage.transform.parent != null && _rawImage.transform.parent.GetComponent<Image>() != null
          ? _rawImage.transform.parent.gameObject
          : _rawImage.gameObject;
        _windowGroup = root.GetComponent<CanvasGroup>();
        if (_windowGroup == null) _windowGroup = root.AddComponent<CanvasGroup>();
      }

      _hidden = _rememberChoice && PlayerPrefs.GetInt(HiddenPref, 0) == 1;
      ApplyHidden();
    }

    /// <summary>Oculta o muestra la vista de la camara.</summary>
    public void SetHidden(bool hidden)
    {
      _hidden = hidden;
      ApplyHidden();

      if (_rememberChoice)
      {
        PlayerPrefs.SetInt(HiddenPref, hidden ? 1 : 0);
        PlayerPrefs.Save();
      }
    }

    private void ApplyHidden()
    {
      if (_windowGroup == null) return;
      _windowGroup.alpha = _hidden ? 0f : 1f;
      _windowGroup.blocksRaycasts = !_hidden;
    }

    private RawImage BuildWindow()
    {
      var canvasGo = new GameObject("CameraPreviewCanvas");
      var canvas = canvasGo.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      canvas.sortingOrder = 1000;

      var scaler = canvasGo.AddComponent<CanvasScaler>();
      scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
      scaler.referenceResolution = new Vector2(1920f, 1080f);
      scaler.matchWidthOrHeight = 0.5f;

      var bgGo = new GameObject("PreviewBG");
      bgGo.transform.SetParent(canvasGo.transform, false);
      var bg = bgGo.AddComponent<Image>();
      bg.color = _panelColor;

      var bgRt = bgGo.GetComponent<RectTransform>();
      bgRt.anchorMin = new Vector2(0f, 0f);
      bgRt.anchorMax = new Vector2(0f, 0f);
      bgRt.pivot = new Vector2(0f, 0f);
      bgRt.anchoredPosition = new Vector2(_cornerMargin, _cornerMargin);
      bgRt.sizeDelta = new Vector2(_windowWidth, _windowHeight);

      var imgGo = new GameObject("CameraPreview");
      imgGo.transform.SetParent(bgGo.transform, false);
      var rawImage = imgGo.AddComponent<RawImage>();

      var imgRt = imgGo.GetComponent<RectTransform>();
      imgRt.anchorMin = new Vector2(0f, 0f);
      imgRt.anchorMax = new Vector2(1f, 1f);
      imgRt.offsetMin = new Vector2(8f, 8f);
      imgRt.offsetMax = new Vector2(-8f, -8f);

      var fitter = imgGo.AddComponent<AspectRatioFitter>();
      fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
      fitter.aspectRatio = 16f / 9f;

      return rawImage;
    }

    private void Update()
    {
      var keyboard = Keyboard.current;

      if (keyboard != null && _toggleKey != Key.None && keyboard[_toggleKey].wasPressedThisFrame)
      {
        SetHidden(!_hidden);
      }

      if (_rawImage == null)
      {
        return;
      }

      var texture = _handTracker != null ? _handTracker.PreviewTexture : null;

      if (_rawImage.texture != texture)
      {
        _rawImage.texture = texture;
      }

      _rawImage.enabled = texture != null;

      if (texture != null && _flipped != _mirrorPreview)
      {
        _rawImage.uvRect = _mirrorPreview ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f);
        _flipped = _mirrorPreview;
      }
    }
  }
}
