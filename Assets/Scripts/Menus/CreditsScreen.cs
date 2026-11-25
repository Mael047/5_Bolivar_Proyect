// CreditsScreen.cs
// Pantalla de creditos del menu principal (prefab CreditsUI). Dos paginas:
//   1. Equipo: fondo de cielo, avatares y un texto encima de cada uno
//      (Avatares > Titulo, Nombre_1..6).
//   2. Colaboradores: recuadro con texto libre y scroll (Colaboradores >
//      Panel > Lista > Viewport > Texto) para colaboradores, agradecimientos,
//      musica, herramientas... Crece solo segun lo que escribas.
// El boton "Mas creditos" cambia de pagina. Volver, Esc o B/Circulo cierran.
// Los textos se escriben directamente en el prefab.

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Menus
{
  [RequireComponent(typeof(CanvasGroup))]
  [DisallowMultipleComponent]
  public class CreditsScreen : MonoBehaviour
  {
    [Header("Botones")]
    [SerializeField, Tooltip("Boton para volver al menu principal.")]
    private Button _backButton;

    [SerializeField, Tooltip("Boton que cambia entre la pagina del equipo y la de colaboradores.")]
    private Button _pageButton;

    [SerializeField, Tooltip("Texto del boton de pagina (cambia segun la pagina).")]
    private Text _pageButtonLabel;

    [SerializeField] private string _toExtraLabel = "Más créditos";
    [SerializeField] private string _toTeamLabel = "Equipo";

    [SerializeField, Tooltip("Seleccionado al abrir (para el mando). Vacio = Volver.")]
    private Selectable _firstSelected;

    [Header("Paginas")]
    [SerializeField, Tooltip("Pagina 1: avatares y nombres.")]
    private GameObject _teamPage;

    [SerializeField, Tooltip("Pagina 2: colaboradores y texto adicional.")]
    private GameObject _extraPage;

    [SerializeField, Tooltip("Scroll del texto de colaboradores.")]
    private ScrollRect _extraScroll;

    [SerializeField, Min(0f), Tooltip("Velocidad del scroll con el stick o las flechas (pantallas por segundo).")]
    private float _scrollSpeed = 0.6f;

    [Header("Animacion")]
    [SerializeField, Min(0f), Tooltip("Duracion del fundido al abrir y cerrar.")]
    private float _fadeDuration = 0.3f;

    [SerializeField, Tooltip("Esc / B del mando tambien cierran los creditos.")]
    private bool _cancelCloses = true;

    /// <summary>True mientras los creditos estan en pantalla.</summary>
    public bool IsOpen { get; private set; }

    private CanvasGroup _group;
    private Action _onClosed;
    private Coroutine _fade;
    private bool _onExtra;

    private void Awake()
    {
      _group = GetComponent<CanvasGroup>();

      if (_backButton != null)
      {
        _backButton.onClick.AddListener(Close);
      }

      if (_pageButton != null)
      {
        _pageButton.onClick.AddListener(TogglePage);
      }

      if (!IsOpen)
      {
        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = false;
        gameObject.SetActive(false);
      }
    }

    /// <summary>Muestra los creditos (pagina del equipo). onClosed se llama al volver.</summary>
    public void Open(Action onClosed = null)
    {
      _onClosed = onClosed;

      if (_group == null)
      {
        _group = GetComponent<CanvasGroup>();
      }

      // Independiente del menu de atras (que se bloquea mientras tanto).
      _group.ignoreParentGroups = true;

      if (!gameObject.activeSelf)
      {
        _group.alpha = 0f;
      }

      IsOpen = true;
      gameObject.SetActive(true);
      transform.SetAsLastSibling();
      ShowPage(false);
      StartFade(1f);

      var first = _firstSelected != null ? _firstSelected : _backButton;

      if (first != null && EventSystem.current != null)
      {
        EventSystem.current.SetSelectedGameObject(first.gameObject);
      }
    }

    public void Close()
    {
      if (!IsOpen)
      {
        return;
      }

      IsOpen = false;
      StartFade(0f);
      var callback = _onClosed;
      _onClosed = null;
      callback?.Invoke();
    }

    public void TogglePage()
    {
      ShowPage(!_onExtra);
    }

    /// <summary>extra = true muestra la pagina de colaboradores.</summary>
    public void ShowPage(bool extra)
    {
      _onExtra = extra && _extraPage != null;

      if (_teamPage != null) _teamPage.SetActive(!_onExtra);
      if (_extraPage != null) _extraPage.SetActive(_onExtra);
      if (_pageButtonLabel != null) _pageButtonLabel.text = _onExtra ? _toTeamLabel : _toExtraLabel;

      if (_onExtra && _extraScroll != null)
      {
        Canvas.ForceUpdateCanvases();
        _extraScroll.verticalNormalizedPosition = 1f; // empezar arriba
      }
    }

    private void Update()
    {
      if (!IsOpen || _group.alpha < 0.99f)
      {
        return;
      }

      var keyboard = Keyboard.current;
      var gamepad = Gamepad.current;

      if (_cancelCloses &&
          ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ||
           (gamepad != null && gamepad.buttonEast.wasPressedThisFrame)))
      {
        Close();
        return;
      }

      // Scroll del texto con stick derecho/izquierdo o flechas.
      if (_onExtra && _extraScroll != null && _scrollSpeed > 0f)
      {
        var input = 0f;
        if (gamepad != null) input += gamepad.rightStick.ReadValue().y;
        if (keyboard != null)
        {
          if (keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed) input += 1f;
          if (keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed) input -= 1f;
        }

        if (Mathf.Abs(input) > 0.15f)
        {
          var content = _extraScroll.content;
          var viewport = _extraScroll.viewport != null ? _extraScroll.viewport : (RectTransform)_extraScroll.transform;
          var overflow = content != null ? content.rect.height - viewport.rect.height : 0f;

          if (overflow > 1f)
          {
            var step = _scrollSpeed * viewport.rect.height / overflow * Time.unscaledDeltaTime;
            _extraScroll.verticalNormalizedPosition = Mathf.Clamp01(_extraScroll.verticalNormalizedPosition + Mathf.Clamp(input, -1f, 1f) * step);
          }
        }
      }
    }

    private void StartFade(float to)
    {
      if (_fade != null)
      {
        StopCoroutine(_fade);
      }

      _fade = StartCoroutine(Fade(to));
    }

    private IEnumerator Fade(float to)
    {
      _group.interactable = false;
      _group.blocksRaycasts = true;
      var from = _group.alpha;
      var t = 0f;

      while (t < _fadeDuration)
      {
        t += Time.unscaledDeltaTime;
        _group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / _fadeDuration));
        yield return null;
      }

      _group.alpha = to;
      _fade = null;

      if (to > 0f)
      {
        _group.interactable = true;
      }
      else
      {
        _group.blocksRaycasts = false;
        gameObject.SetActive(false);
      }
    }
  }
}
