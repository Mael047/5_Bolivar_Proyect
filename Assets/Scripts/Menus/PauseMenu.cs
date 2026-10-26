// PauseMenu.cs
// Menu de pausa con pantallas en carrusel (estilo Ocarina of Time): las
// pantallas estan repartidas alrededor de un cilindro y al cambiar de pestaña
// el carrusel gira hasta dejar la nueva de frente.
//
// Controles
//   Abrir / cerrar pausa ........ Esc  | Options (Start)
//   Abrir el mapa ............... M    | Touchpad
//   Pantalla anterior/siguiente . A/D  | L1/R1   (tambien las flechas en pantalla)
//   Cerrar ...................... Esc  | Options / Circulo  (tambien la X)
//
// Mientras esta abierto: el juego se pausa (Time.timeScale = 0) y el input
// del jugador se desactiva. No se abre en el menu principal ni durante la
// inspeccion de coleccionables.
//
// Va en la raiz del Canvas (prefab PauseMenuUI). Todo el aspecto se diseña en
// el prefab; aqui solo se conectan pantallas y botones.
// Por ahora funcionan Continuar y Salir al menu; Ajustes queda pendiente.

using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Menus
{
  [RequireComponent(typeof(Canvas))]
  [DisallowMultipleComponent]
  public class PauseMenu : MonoBehaviour
  {
    /// <summary>True mientras el menu de pausa esta abierto (o animandose).</summary>
    public static bool IsOpen { get; private set; }

    [Header("Pantallas")]
    [SerializeField, Tooltip("Pantallas del carrusel en orden (izquierda a derecha).")]
    private PauseMenuPage[] _pages;

    [SerializeField, Tooltip("Pantalla que abre Esc / Options.")]
    private int _pausePage;

    [SerializeField, Tooltip("Pantalla que abre M / Touchpad.")]
    private int _mapPage = 2;

    [Header("Botones de la pantalla de pausa")]
    [SerializeField] private Button _continueButton;
    [SerializeField] private Button _settingsButton;
    [SerializeField] private Button _exitToMenuButton;

    [Header("Navegacion en pantalla (opcionales)")]
    [SerializeField] private Button _previousButton;
    [SerializeField] private Button _nextButton;
    [SerializeField] private Button _closeButton;

    [SerializeField, Tooltip("Indicador de pantalla: una imagen por pantalla se crea copiando esta.")]
    private Graphic _dotTemplate;
    [SerializeField] private Color _dotActive = new Color(0.25f, 0.45f, 0.65f, 1f);
    [SerializeField] private Color _dotInactive = new Color(0.25f, 0.45f, 0.65f, 0.35f);

    [Header("Carrusel")]
    [SerializeField, Tooltip("Distancia horizontal (px) del centro a las pantallas laterales.")]
    private float _radius = 700f;

    [SerializeField, Range(30f, 180f), Tooltip("Grados entre pantallas en el cilindro.")]
    private float _angleStep = 120f;

    [SerializeField, Range(0.2f, 1f), Tooltip("Escala de las pantallas de los lados.")]
    private float _sideScale = 0.6f;

    [SerializeField, Range(0f, 1f), Tooltip("Opacidad de las pantallas de los lados.")]
    private float _sideAlpha = 0.35f;

    [SerializeField, Min(0.05f), Tooltip("Duracion del giro entre pantallas.")]
    private float _turnDuration = 0.35f;

    [Header("Apertura")]
    [SerializeField, Min(0.01f)] private float _fadeDuration = 0.2f;
    [SerializeField, Tooltip("Pausa el juego (Time.timeScale = 0) mientras esta abierto.")]
    private bool _pauseGame = true;

    private Canvas _canvas;
    private CanvasGroup _group;
    private Graphic[] _dots;
    private PlayerInput _playerInput;

    private float _position;      // pantalla actual, continua (anima el giro)
    private float _target;        // pantalla destino (sin envolver: puede pasar de N-1 a N)
    private float _velocity;
    private int _selectedPage = -1;
    private bool _closing;
    private bool _settingsOpen;
    private Coroutine _fade;

    private InputAction _pauseAction;
    private InputAction _mapAction;
    private InputAction _nextAction;
    private InputAction _previousAction;
    private InputAction _closeAction;

    private int PageCount => _pages != null ? _pages.Length : 0;

    /// <summary>Indice (0..N-1) de la pantalla que esta de frente.</summary>
    public int CurrentPage => PageCount == 0 ? 0 : Wrap(Mathf.RoundToInt(_target));

    /// <summary>Pantalla que esta de frente (null si no hay pantallas).</summary>
    public PauseMenuPage Current => PageCount == 0 ? null : _pages[CurrentPage];

    private void Awake()
    {
      _canvas = GetComponent<Canvas>();
      _group = GetComponent<CanvasGroup>();

      if (_group == null)
      {
        _group = gameObject.AddComponent<CanvasGroup>();
      }

      BuildActions();
      BuildDots();

      if (_continueButton != null) _continueButton.onClick.AddListener(Close);
      if (_exitToMenuButton != null) _exitToMenuButton.onClick.AddListener(ExitToMainMenu);
      if (_settingsButton != null) _settingsButton.onClick.AddListener(OpenSettings);
      if (_previousButton != null) _previousButton.onClick.AddListener(() => Turn(-1));
      if (_nextButton != null) _nextButton.onClick.AddListener(() => Turn(1));
      if (_closeButton != null) _closeButton.onClick.AddListener(Close);

      // Las flechas y la X no deben robar el foco al navegar con el mando.
      NoNavigation(_previousButton);
      NoNavigation(_nextButton);
      NoNavigation(_closeButton);

      _canvas.enabled = false;
      _group.alpha = 0f;
      _group.interactable = false;
      _group.blocksRaycasts = false;
      IsOpen = false;
    }

    private void OnEnable()
    {
      _pauseAction.Enable();
      _mapAction.Enable();
    }

    private void OnDisable()
    {
      _pauseAction.Disable();
      _mapAction.Disable();
      _nextAction.Disable();
      _previousAction.Disable();
      _closeAction.Disable();
    }

    private void OnDestroy()
    {
      if (IsOpen)
      {
        IsOpen = false;

        if (_pauseGame)
        {
          Time.timeScale = 1f;
        }
      }

      _pauseAction?.Dispose();
      _mapAction?.Dispose();
      _nextAction?.Dispose();
      _previousAction?.Dispose();
      _closeAction?.Dispose();
    }

    // ---------- Input ----------

    private void BuildActions()
    {
      _pauseAction = new InputAction("Pause", InputActionType.Button);
      _pauseAction.AddBinding("<Keyboard>/escape");
      _pauseAction.AddBinding("<Gamepad>/start");

      _mapAction = new InputAction("Map", InputActionType.Button);
      _mapAction.AddBinding("<Keyboard>/m");
      _mapAction.AddBinding("<DualShockGamepad>/touchpadButton");

      _nextAction = new InputAction("NextPage", InputActionType.Button);
      _nextAction.AddBinding("<Keyboard>/d");
      _nextAction.AddBinding("<Gamepad>/rightShoulder");

      _previousAction = new InputAction("PreviousPage", InputActionType.Button);
      _previousAction.AddBinding("<Keyboard>/a");
      _previousAction.AddBinding("<Gamepad>/leftShoulder");

      _closeAction = new InputAction("ClosePause", InputActionType.Button);
      _closeAction.AddBinding("<Gamepad>/buttonEast");

      // Mientras hay un coleccionable abierto encima (desde Estado) el menu
      // ignora sus teclas: Esc / Circulo cierran solo la inspeccion.
      _pauseAction.performed += _ => { if (!Collectibles.CollectibleInspector.IsOpen && !CameraSetup.IsOpen) OnPausePressed(); };
      _mapAction.performed += _ => { if (!Collectibles.CollectibleInspector.IsOpen && !CameraSetup.IsOpen) OnMapPressed(); };
      _nextAction.performed += _ => { if (Interactive) Turn(1); };
      _previousAction.performed += _ => { if (Interactive) Turn(-1); };
      _closeAction.performed += _ => { if (Interactive) Close(); };
    }

    private void OnPausePressed()
    {
      if (IsOpen)
      {
        Close();
      }
      else
      {
        Open(_pausePage);
      }
    }

    private void OnMapPressed()
    {
      if (!IsOpen)
      {
        Open(_mapPage);
      }
      else if (CurrentPage == Wrap(_mapPage))
      {
        Close();
      }
      else
      {
        GoTo(_mapPage);
      }
    }

    // ---------- Abrir / cerrar ----------

    private bool Interactive => IsOpen && !_closing && !Collectibles.CollectibleInspector.IsOpen && !CameraSetup.IsOpen;

    /// <summary>Boton Ajustes: abre la configuracion de camara encima de la pausa.</summary>
    private void OpenSettings()
    {
      var setup = FindAnyObjectByType<CameraSetup>(FindObjectsInactive.Include);

      if (setup == null)
      {
        Debug.LogWarning("[PauseMenu] No hay configuracion de camara en la escena (Tools > Configuracion de camara > Agregar a la escena abierta).");
        return;
      }

      if (!setup.gameObject.activeInHierarchy)
      {
        setup.gameObject.SetActive(true);
      }

      _settingsOpen = true;
      setup.Open(CameraSetup.Mode.Ajustes);
    }

    private bool CanOpen =>
      !IsOpen && PageCount > 0 && !MainMenu.IsOpen && !Collectibles.CollectibleInspector.IsOpen;

    public void Open(int page)
    {
      if (!CanOpen)
      {
        return;
      }

      IsOpen = true;
      _closing = false;

      var player = FindAnyObjectByType<global::Player>();
      _playerInput = player != null ? player.GetComponent<PlayerInput>() : null;

      if (_playerInput != null)
      {
        _playerInput.DeactivateInput();
      }

      if (_pauseGame)
      {
        Time.timeScale = 0f;
      }

      // Abre directamente en la pantalla pedida, sin giro.
      _target = _position = Wrap(page);
      _velocity = 0f;
      _selectedPage = -1;
      LayoutPages();

      _nextAction.Enable();
      _previousAction.Enable();
      _closeAction.Enable();

      EnsureEventSystem();
      _canvas.enabled = true;
      _group.interactable = true;
      _group.blocksRaycasts = true;
      StartFade(1f);
    }

    public void Close()
    {
      if (!IsOpen || _closing)
      {
        return;
      }

      _closing = true;
      _group.interactable = false;
      _group.blocksRaycasts = false;
      _nextAction.Disable();
      _previousAction.Disable();
      _closeAction.Disable();

      if (EventSystem.current != null)
      {
        EventSystem.current.SetSelectedGameObject(null);
      }

      StartFade(0f);
    }

    private void FinishClose()
    {
      _canvas.enabled = false;

      if (_pauseGame)
      {
        Time.timeScale = 1f;
      }

      if (_playerInput != null)
      {
        _playerInput.ActivateInput();
      }

      _closing = false;
      IsOpen = false;
    }

    private void ExitToMainMenu()
    {
      // Recarga el nivel: el menu principal vive dentro de la escena.
      Time.timeScale = 1f;
      IsOpen = false;
      SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void StartFade(float to)
    {
      if (_fade != null)
      {
        StopCoroutine(_fade);
      }

      _fade = StartCoroutine(FadeRoutine(to));
    }

    private IEnumerator FadeRoutine(float to)
    {
      var from = _group.alpha;
      var t = 0f;

      while (t < _fadeDuration)
      {
        t += Time.unscaledDeltaTime;
        var k = Mathf.Clamp01(t / _fadeDuration);
        _group.alpha = Mathf.Lerp(from, to, k);
        yield return null;
      }

      _group.alpha = to;
      _fade = null;

      if (to <= 0f)
      {
        FinishClose();
      }
    }

    // ---------- Carrusel ----------

    public void Turn(int direction)
    {
      if (PageCount < 2)
      {
        return;
      }

      _target = Mathf.Round(_target) + Mathf.Sign(direction);
    }

    public void GoTo(int page)
    {
      if (PageCount == 0)
      {
        return;
      }

      // Por el camino mas corto.
      var current = Mathf.RoundToInt(_target);
      var delta = Wrap(page) - Wrap(current);

      if (delta > PageCount / 2) delta -= PageCount;
      if (delta < -PageCount / 2) delta += PageCount;

      _target = current + delta;
    }

    private void Update()
    {
      if (!IsOpen)
      {
        return;
      }

      _position = Mathf.SmoothDamp(_position, _target, ref _velocity, _turnDuration * 0.35f,
        Mathf.Infinity, Time.unscaledDeltaTime);

      if (Mathf.Abs(_position - _target) < 0.001f)
      {
        _position = _target;
      }

      LayoutPages();
      UpdateSelection();

      // Con una inspeccion o los ajustes encima, los botones del menu no responden.
      _group.interactable = !_closing && !Collectibles.CollectibleInspector.IsOpen && !CameraSetup.IsOpen;

      // Al volver de Ajustes, se vuelve a seleccionar el primer boton.
      if (_settingsOpen && !CameraSetup.IsOpen)
      {
        _settingsOpen = false;
        _selectedPage = -1;
      }
    }

    private void LayoutPages()
    {
      var n = PageCount;

      for (var i = 0; i < n; i++)
      {
        var page = _pages[i];

        if (page == null)
        {
          continue;
        }

        // Desfase ciclico de esta pantalla respecto a la de frente (-n/2..n/2).
        var offset = Mathf.Repeat(i - _position + n * 0.5f, n) - n * 0.5f;
        var angle = offset * _angleStep;
        var rad = angle * Mathf.Deg2Rad;
        var facing = Mathf.Cos(rad);           // 1 = de frente, <0 = por detras
        var front = Mathf.Clamp01(facing);

        var rect = page.Rect;
        rect.anchoredPosition = new Vector2(Mathf.Sin(rad) * _radius, 0f);
        rect.localRotation = Quaternion.Euler(0f, angle, 0f);
        rect.localScale = Vector3.one * Mathf.Lerp(_sideScale, 1f, front * front);

        var alpha = facing <= 0f ? 0f : Mathf.Lerp(_sideAlpha, 1f, Mathf.Pow(front, 6f));
        page.Group.alpha = alpha;

        var isFront = Mathf.Abs(offset) < 0.05f;
        page.Group.interactable = isFront && !_closing;
        page.Group.blocksRaycasts = isFront;
        page.gameObject.SetActive(facing > 0f);
      }

      // Las de delante se dibujan encima.
      var order = new System.Collections.Generic.List<PauseMenuPage>(_pages);
      order.RemoveAll(p => p == null);
      order.Sort((a, b) => Facing(a).CompareTo(Facing(b)));

      for (var i = 0; i < order.Count; i++)
      {
        order[i].transform.SetSiblingIndex(i);
      }

      UpdateDots();
    }

    private float Facing(PauseMenuPage page)
    {
      var n = PageCount;
      var i = System.Array.IndexOf(_pages, page);
      var offset = Mathf.Repeat(i - _position + n * 0.5f, n) - n * 0.5f;
      return Mathf.Cos(offset * _angleStep * Mathf.Deg2Rad);
    }

    private void UpdateSelection()
    {
      var current = CurrentPage;

      // Selecciona el primer boton cuando la pantalla termina de girar.
      if (current == _selectedPage || Mathf.Abs(_position - _target) > 0.05f || EventSystem.current == null)
      {
        return;
      }

      _selectedPage = current;
      var first = _pages[current] != null ? _pages[current].firstSelected : null;
      EventSystem.current.SetSelectedGameObject(first != null ? first.gameObject : null);
    }

    // ---------- Indicador ----------

    private void BuildDots()
    {
      if (_dotTemplate == null || PageCount == 0)
      {
        return;
      }

      _dots = new Graphic[PageCount];
      _dots[0] = _dotTemplate;

      for (var i = 1; i < PageCount; i++)
      {
        _dots[i] = Instantiate(_dotTemplate, _dotTemplate.transform.parent);
        _dots[i].name = $"{_dotTemplate.name} ({i + 1})";
      }
    }

    private void UpdateDots()
    {
      if (_dots == null)
      {
        return;
      }

      var current = CurrentPage;

      for (var i = 0; i < _dots.Length; i++)
      {
        _dots[i].color = i == current ? _dotActive : _dotInactive;
      }
    }

    // ---------- Utilidades ----------

    private int Wrap(int index)
    {
      var n = Mathf.Max(1, PageCount);
      return ((index % n) + n) % n;
    }

    private static void NoNavigation(Selectable selectable)
    {
      if (selectable == null)
      {
        return;
      }

      var nav = selectable.navigation;
      nav.mode = Navigation.Mode.None;
      selectable.navigation = nav;
    }

    private static void EnsureEventSystem()
    {
      if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
      {
        return;
      }

      var go = new GameObject("EventSystem");
      go.AddComponent<EventSystem>();
      go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }
  }
}
