// IntroSlideshow.cs
// Historia de contexto al empezar una Nueva partida: despues de configurar la
// camara y antes de que la camara viaje hasta Chopi, se muestran las imagenes
// en orden, cada una con su texto. Se pasa a la siguiente con clic / Espacio /
// Enter / A (Cruz), o sola despues de unos segundos. Al terminar se desvanece
// y el menu principal sigue con el viaje de la camara.
//
// Todo se configura en el prefab (Assets/UI/Contexto/IntroContexto):
//   - Slides: imagen, texto y segundos de cada una (0 = espera el clic).
//   - Tiempos de fundido, efecto de escritura del texto, zoom lento...
// Si una imagen no tiene texto, la franja de texto se oculta.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class IntroSlideshow : MonoBehaviour
{
  [Serializable]
  public class Slide
  {
    public Sprite image;
    [TextArea(2, 6), Tooltip("Admite rich text: <b>negrita</b>, <i>cursiva</i>, <color=#E8C66A>color</color>.")]
    public string text;
    [Min(0f), Tooltip("Segundos antes de pasar sola a la siguiente. 0 = usa 'Duracion por defecto'.")]
    public float duration;
  }

  [Header("Imagenes y textos (en orden)")]
  [SerializeField] private List<Slide> _slides = new List<Slide>();

  [Header("Avance")]
  [SerializeField, Tooltip("Pasar sola a la siguiente despues de su duracion.")]
  private bool _autoAdvance = true;

  [SerializeField, Min(0.5f), Tooltip("Duracion de las imagenes que tienen 0.")]
  private float _defaultDuration = 7f;

  [SerializeField, Tooltip("Clic / Espacio / Enter / A del mando pasan a la siguiente.")]
  private bool _clickToAdvance = true;

  [SerializeField, Tooltip("Esc / Start del mando se saltan toda la historia.")]
  private bool _allowSkip = true;

  [Header("Texto")]
  [SerializeField, Min(0f), Tooltip("Letras por segundo al escribirse (0 = aparece completo). Un clic mientras se escribe lo completa.")]
  private float _typeSpeed = 45f;

  [SerializeField, Tooltip("Aviso de 'continuar' (se ve cuando el texto termino).")]
  private string _continueHint = "Clic para continuar";

  [Header("Animacion")]
  [SerializeField, Min(0.01f), Tooltip("Fundido entre imagenes.")]
  private float _crossFade = 0.6f;

  [SerializeField, Min(0.01f), Tooltip("Fundido de entrada y de salida de toda la historia.")]
  private float _fadeInOut = 0.8f;

  [SerializeField, Range(1f, 1.2f), Tooltip("Zoom lento sobre cada imagen (1 = quieta).")]
  private float _slowZoom = 1.06f;

  [Header("Referencias (prefab)")]
  [SerializeField] private CanvasGroup _group;
  [SerializeField, Tooltip("Dos imagenes para el fundido cruzado.")]
  private Image _imageA;
  [SerializeField] private Image _imageB;
  [SerializeField] private CanvasGroup _textBand;
  [SerializeField] private Text _text;
  [SerializeField] private Text _hint;
  [SerializeField, Tooltip("Texto opcional '3 / 7'.")]
  private Text _counter;

  /// <summary>True mientras la historia esta en pantalla.</summary>
  public static bool IsPlaying { get; private set; }

  private bool _advanceRequested;
  private bool _skipRequested;
  private bool _typing;
  private Image _front;
  private Image _back;

  private void Awake()
  {
    if (_group == null) _group = GetComponent<CanvasGroup>();
    if (_group != null) _group.alpha = 0f;
    _front = _imageA;
    _back = _imageB;
  }

  private void OnDisable()
  {
    IsPlaying = false;
  }

  /// <summary>Muestra todas las imagenes y termina cuando se acaban (o se saltan).</summary>
  public IEnumerator Play()
  {
    if (_slides.Count == 0)
    {
      yield break;
    }

    gameObject.SetActive(true);
    IsPlaying = true;
    _skipRequested = false;
    SetAlpha(_imageA, 0f);
    SetAlpha(_imageB, 0f);
    if (_textBand != null) _textBand.alpha = 0f;
    if (_hint != null) _hint.enabled = false;

    // Entra el fondo oscuro.
    yield return FadeGroup(0f, 1f, _fadeInOut);

    for (var i = 0; i < _slides.Count && !_skipRequested; i++)
    {
      yield return ShowSlide(i);
    }

    // Sale todo: la camara sigue hacia Chopi.
    if (_textBand != null) _textBand.alpha = 0f;
    yield return FadeGroup(1f, 0f, _fadeInOut);
    IsPlaying = false;
    gameObject.SetActive(false);
  }

  private IEnumerator ShowSlide(int index)
  {
    var slide = _slides[index];

    if (_counter != null) _counter.text = (index + 1) + " / " + _slides.Count;

    // Fundido cruzado: la nueva imagen entra encima de la anterior.
    var incoming = _back;
    var outgoing = _front;
    incoming.sprite = slide.image;
    incoming.enabled = slide.image != null;
    incoming.transform.SetAsLastSibling();
    if (_textBand != null) _textBand.transform.SetAsLastSibling();
    if (_hint != null) _hint.transform.SetAsLastSibling();
    if (_counter != null) _counter.transform.SetAsLastSibling();
    incoming.rectTransform.localScale = Vector3.one;

    var hasText = !string.IsNullOrWhiteSpace(slide.text);
    var bandFrom = _textBand != null ? _textBand.alpha : 0f;
    var t = 0f;

    while (t < _crossFade)
    {
      t += Time.unscaledDeltaTime;
      var k = Mathf.Clamp01(t / _crossFade);
      SetAlpha(incoming, k);
      if (_textBand != null) _textBand.alpha = Mathf.Lerp(bandFrom, 0f, k);
      if (_skipRequested) break;
      ReadInput();
      yield return null;
    }

    SetAlpha(incoming, 1f);
    SetAlpha(outgoing, 0f);
    _front = incoming;
    _back = outgoing;

    // Texto
    _advanceRequested = false;
    if (_hint != null) _hint.enabled = false;

    if (_text != null) _text.text = "";

    if (hasText && _textBand != null)
    {
      _textBand.alpha = 1f;
    }

    var duration = slide.duration > 0f ? slide.duration : _defaultDuration;
    var elapsed = 0f;
    var visible = 0f;
    var full = slide.text ?? "";
    _typing = hasText && _typeSpeed > 0f && _text != null;

    if (hasText && !_typing && _text != null) _text.text = full;

    while (!_skipRequested)
    {
      var dt = Time.unscaledDeltaTime;
      elapsed += dt;
      ReadInput();

      // Zoom lento
      var zoom = Mathf.Lerp(1f, _slowZoom, Mathf.Clamp01(elapsed / Mathf.Max(duration, 1f)));
      _front.rectTransform.localScale = Vector3.one * zoom;

      if (_typing)
      {
        visible += dt * _typeSpeed;

        if (_advanceRequested)
        {
          // Primer clic: completa el texto.
          visible = full.Length;
          _advanceRequested = false;
        }

        if (visible >= full.Length)
        {
          _typing = false;
          _text.text = full;
        }
        else
        {
          _text.text = Typed(full, (int)visible);
        }
      }
      else
      {
        if (_hint != null && _clickToAdvance)
        {
          _hint.enabled = true;
          _hint.text = _continueHint;
          var c = _hint.color;
          c.a = 0.45f + 0.35f * Mathf.Sin(Time.unscaledTime * 3f);
          _hint.color = c;
        }

        if (_advanceRequested) break;
        if (_autoAdvance && elapsed >= duration) break;
      }

      yield return null;
    }
  }

  private void ReadInput()
  {
    var keyboard = Keyboard.current;
    var mouse = Mouse.current;
    var gamepad = Gamepad.current;

    if (_clickToAdvance &&
        ((mouse != null && mouse.leftButton.wasPressedThisFrame) ||
         (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame)) ||
         (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame)))
    {
      _advanceRequested = true;
    }

    if (_allowSkip &&
        ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ||
         (gamepad != null && gamepad.startButton.wasPressedThisFrame)))
    {
      _skipRequested = true;
    }
  }

  // Corta el texto sin romper las etiquetas de rich text (<b>, <color=...>).
  private static string Typed(string full, int count)
  {
    var sb = new System.Text.StringBuilder();
    var shown = 0;
    var open = new Stack<string>();

    for (var i = 0; i < full.Length; i++)
    {
      if (full[i] == '<')
      {
        var end = full.IndexOf('>', i);

        if (end > i)
        {
          var tag = full.Substring(i, end - i + 1);
          sb.Append(tag);

          if (tag.StartsWith("</")) { if (open.Count > 0) open.Pop(); }
          else
          {
            var name = tag.Substring(1, tag.Length - 2).Split('=')[0];
            open.Push(name);
          }

          i = end;
          continue;
        }
      }

      if (shown >= count) break;
      sb.Append(full[i]);
      shown++;
    }

    while (open.Count > 0) sb.Append("</" + open.Pop() + ">");
    return sb.ToString();
  }

  private IEnumerator FadeGroup(float from, float to, float duration)
  {
    if (_group == null) yield break;
    var t = 0f;

    while (t < duration)
    {
      t += Time.unscaledDeltaTime;
      _group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
      if (to > from) ReadInput();
      yield return null;
    }

    _group.alpha = to;
  }

  private static void SetAlpha(Image image, float alpha)
  {
    if (image == null) return;
    var c = image.color;
    c.a = alpha;
    image.color = c;
  }
}
