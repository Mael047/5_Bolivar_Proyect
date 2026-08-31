// CameraPreviewWindow.cs
// Ventana provisional que muestra la camara en vivo. Si no se le asigna una
// RawImage en el Inspector, se autoconstruye un Canvas con panel + imagen al
// iniciar (solo en Play/Game). Espejada por defecto para que coincida con el
// marco de deteccion: mano derecha -> objeto a la derecha.

using UnityEngine;
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
