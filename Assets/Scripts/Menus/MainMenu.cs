// MainMenu.cs
// Menu principal dentro del propio nivel (Jardin_Principal): el nivel se ve de
// fondo, vivo, desde un punto de camara elegido (MenuCameraPoint) y el menu va
// encima. Asi el nivel se carga una sola vez.
//
// Mientras el menu esta abierto: el input del jugador esta apagado, la
// TopDownCamera esta apagada y la camara principal se queda en el punto del
// menu. Al pulsar "Nueva partida" el menu se funde y la camara viaja (o hace
// fundido a negro) hasta la posicion de juego; despues se devuelve el control.
//
// Va en la raiz del Canvas del menu (prefab MainMenuUI). El aspecto del menu
// se diseña libremente en el prefab; aqui solo se conectan los botones.
// Por ahora solo "Nueva partida" hace algo; el resto se iran conectando.

using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Menus
{
  [RequireComponent(typeof(Canvas))]
  [DisallowMultipleComponent]
  public class MainMenu : MonoBehaviour
  {
    public enum Transition
    {
      [Tooltip("La camara viaja desde el punto del menu hasta la camara de juego.")]
      MoverCamara,
      [Tooltip("Fundido a negro, cambio de camara y fundido de vuelta (necesita Fade Image).")]
      FundidoANegro
    }

    /// <summary>True mientras el menu esta abierto o haciendo la transicion al juego.</summary>
    public static bool IsOpen { get; private set; }

    [Header("Botones")]
    [SerializeField] private Button _newGameButton;
    [SerializeField] private Button _loadGameButton;
    [SerializeField] private Button _settingsButton;
    [SerializeField] private Button _creditsButton;
    [SerializeField] private Button _quitButton;

    [SerializeField, Tooltip("Boton seleccionado al abrir (para navegar con el mando). Vacio = Nueva partida.")]
    private Selectable _firstSelected;

    [Header("Camara")]
    [SerializeField, Tooltip("Punto (posicion + rotacion) desde el que se ve el nivel en el menu.")]
    private Transform _menuCameraPoint;

    [SerializeField, Tooltip("Camara del juego. Vacio = Camera.main.")]
    private Camera _camera;

    [SerializeField, Tooltip("Movimiento suave de la camara en el menu (balanceo lento). 0 = quieta.")]
    private float _idleSway = 0.15f;

    [Header("Al empezar")]
    [SerializeField, Tooltip("Mostrar la configuracion de camara (webcam) antes de entrar al juego.")]
    private bool _cameraSetupOnNewGame = true;

    [SerializeField, Tooltip("Configuracion de camara. Vacio = se busca en la escena.")]
    private CameraSetup _cameraSetup;

    [SerializeField] private Transition _transition = Transition.MoverCamara;

    [SerializeField, Min(0.1f), Tooltip("Duracion del viaje de camara o de cada fundido.")]
    private float _transitionDuration = 2f;

    [SerializeField, Tooltip("Curva del viaje de camara (X = tiempo 0..1, Y = progreso 0..1).")]
    private AnimationCurve _transitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [SerializeField, Min(0.01f), Tooltip("Duracion del fundido de salida del menu.")]
    private float _menuFadeDuration = 0.4f;

    [SerializeField, Tooltip("Imagen negra a pantalla completa para el fundido (opcional).")]
    private Graphic _fadeImage;

    [Header("Escena")]
    [SerializeField, Tooltip("Abrir el menu al cargar la escena. Apagalo para probar el nivel directamente.")]
    private bool _showOnStart = true;

    [SerializeField, Tooltip("Objetos que se apagan mientras el menu esta abierto y se encienden al jugar " +
                              "(HUD, vista de la webcam, enemigos que no deben atacar en el menu...).")]
    private GameObject[] _disableDuringMenu;

    [Header("Eventos")]
    [Tooltip("Al terminar la transicion y devolver el control al jugador (p. ej. lanzar la cinematica).")]
    public UnityEvent OnGameStarted = new UnityEvent();

    private CanvasGroup _group;
    private global::Player _player;
    private PlayerInput _playerInput;
    private TopDownCamera _topDown;
    private bool _starting;
    private float _swayTime;

    private void Awake()
    {
      _group = GetComponent<CanvasGroup>();

      if (_group == null)
      {
        _group = gameObject.AddComponent<CanvasGroup>();
      }

      if (_camera == null)
      {
        _camera = Camera.main;
      }

      if (_camera != null)
      {
        _topDown = _camera.GetComponent<TopDownCamera>();
      }

      _player = FindAnyObjectByType<global::Player>();
      _playerInput = _player != null ? _player.GetComponent<PlayerInput>() : null;

      if (_newGameButton != null)
      {
        _newGameButton.onClick.AddListener(StartNewGame);
      }

      // Pendientes: dejan constancia en consola hasta que se implementen.
      AddPending(_loadGameButton, "Cargar partida");
      AddPending(_settingsButton, "Ajustes");
      AddPending(_creditsButton, "Creditos");
      AddPending(_quitButton, "Salir del juego");

      if (_fadeImage != null)
      {
        SetFade(0f);
        _fadeImage.raycastTarget = false;
      }

      if (_showOnStart)
      {
        IsOpen = true;
      }
      else
      {
        gameObject.SetActive(false);
      }
    }

    private void Start()
    {
      if (!_showOnStart)
      {
        return;
      }

      // En Start: PlayerInput ya activo sus acciones en OnEnable.
      if (_playerInput != null)
      {
        _playerInput.DeactivateInput();
      }

      if (_topDown != null)
      {
        _topDown.enabled = false;
      }

      SetObjectsActive(false);
      PlaceMenuCamera(0f);
      EnsureEventSystem();
      SelectFirst();

      _group.alpha = 1f;
      _group.interactable = true;
      _group.blocksRaycasts = true;
    }

    private void OnDestroy()
    {
      if (IsOpen && !_starting)
      {
        IsOpen = false;
      }
    }

    private void Update()
    {
      if (!_starting && IsOpen)
      {
        _swayTime += Time.unscaledDeltaTime;
        PlaceMenuCamera(_swayTime);
      }
    }

    private void PlaceMenuCamera(float time)
    {
      if (_camera == null || _menuCameraPoint == null)
      {
        return;
      }

      var position = _menuCameraPoint.position;
      var rotation = _menuCameraPoint.rotation;

      if (_idleSway > 0f)
      {
        position += _menuCameraPoint.right * (Mathf.Sin(time * 0.23f) * _idleSway) +
                    _menuCameraPoint.up * (Mathf.Sin(time * 0.31f) * _idleSway * 0.4f);
        rotation *= Quaternion.Euler(Mathf.Sin(time * 0.19f) * _idleSway * 2f, Mathf.Sin(time * 0.17f) * _idleSway * 3f, 0f);
      }

      _camera.transform.SetPositionAndRotation(position, rotation);
    }

    // ---------- Nueva partida ----------

    public void StartNewGame()
    {
      if (_starting)
      {
        return;
      }

      StartCoroutine(StartGameRoutine());
    }

    private IEnumerator StartGameRoutine()
    {
      _starting = true;
      _group.interactable = false;
      _group.blocksRaycasts = false;

      if (EventSystem.current != null)
      {
        EventSystem.current.SetSelectedGameObject(null);
      }

      var useFade = _transition == Transition.FundidoANegro && _fadeImage != null;

      // 1. Desvanecer el menu (el fondo de fundido es aparte, no se desvanece).
      yield return FadeMenu(1f, 0f, _menuFadeDuration);

      // 2. Configuracion de camara (webcam) antes de entrar al juego.
      if (_cameraSetupOnNewGame)
      {
        var setup = _cameraSetup != null ? _cameraSetup : FindAnyObjectByType<CameraSetup>(FindObjectsInactive.Include);

        if (setup != null)
        {
          if (!setup.gameObject.activeInHierarchy)
          {
            setup.gameObject.SetActive(true);
          }

          SetObjectsActive(true); // el sistema de manos debe estar activo
          yield return setup.OpenAndWait(CameraSetup.Mode.NuevaPartida);

          if (setup.WasCancelled)
          {
            // Volver al menu principal.
            SetObjectsActive(false);
            _starting = false;
            yield return FadeMenu(0f, 1f, _menuFadeDuration);
            _group.interactable = true;
            _group.blocksRaycasts = true;
            SelectFirst();
            yield break;
          }
        }
      }

      if (useFade)
      {
        yield return FadeScreen(0f, 1f, _transitionDuration * 0.5f);
        SetObjectsActive(true);
        SnapToGameplayCamera();
        yield return FadeScreen(1f, 0f, _transitionDuration * 0.5f);
      }
      else
      {
        SetObjectsActive(true);
        yield return MoveCameraToGameplay();
      }

      // 2. Devolver el control.
      if (_topDown != null)
      {
        _topDown.SnapToTarget();
        _topDown.enabled = true;
      }

      if (_playerInput != null)
      {
        _playerInput.ActivateInput();
      }

      IsOpen = false;
      _starting = false;
      gameObject.SetActive(false);
      OnGameStarted?.Invoke();
    }

    private IEnumerator MoveCameraToGameplay()
    {
      if (_camera == null)
      {
        yield break;
      }

      var startPos = _camera.transform.position;
      var startRot = _camera.transform.rotation;
      var t = 0f;

      while (t < _transitionDuration)
      {
        t += Time.unscaledDeltaTime;
        var k = _transitionCurve.Evaluate(Mathf.Clamp01(t / _transitionDuration));

        // Se recalcula cada frame por si el jugador se mueve (fisica, animacion).
        if (_topDown != null && _topDown.TryGetTargetPose(out var endPos, out var endRot))
        {
          _camera.transform.SetPositionAndRotation(
            Vector3.LerpUnclamped(startPos, endPos, k),
            Quaternion.SlerpUnclamped(startRot, endRot, k));
        }

        yield return null;
      }
    }

    private void SnapToGameplayCamera()
    {
      if (_topDown != null)
      {
        _topDown.SnapToTarget();
      }
    }

    private IEnumerator FadeMenu(float from, float to, float duration)
    {
      var t = 0f;

      while (t < duration)
      {
        t += Time.unscaledDeltaTime;
        _group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
        yield return null;
      }

      _group.alpha = to;
    }

    private IEnumerator FadeScreen(float from, float to, float duration)
    {
      var t = 0f;

      while (t < duration)
      {
        t += Time.unscaledDeltaTime;
        SetFade(Mathf.Lerp(from, to, Mathf.Clamp01(t / duration)));
        yield return null;
      }

      SetFade(to);
    }

    /// <summary>
    /// El fundido negro no puede depender del CanvasGroup del menu (que ya se
    /// desvanecio): se le pone su propio CanvasGroup que ignora al padre.
    /// </summary>
    private void SetFade(float alpha)
    {
      if (_fadeImage == null)
      {
        return;
      }

      var group = _fadeImage.GetComponent<CanvasGroup>();

      if (group == null)
      {
        group = _fadeImage.gameObject.AddComponent<CanvasGroup>();
        group.ignoreParentGroups = true;
        group.blocksRaycasts = false;
        group.interactable = false;
      }

      group.alpha = alpha;
    }

    // ---------- Utilidades ----------

    private static void AddPending(Button button, string label)
    {
      if (button != null)
      {
        button.onClick.AddListener(() => Debug.Log($"[MainMenu] '{label}' aun no esta implementado."));
      }
    }

    private void SetObjectsActive(bool active)
    {
      if (_disableDuringMenu == null)
      {
        return;
      }

      foreach (var go in _disableDuringMenu)
      {
        if (go != null)
        {
          go.SetActive(active);
        }
      }
    }

    private void SelectFirst()
    {
      var first = _firstSelected != null ? _firstSelected : _newGameButton;

      if (first != null && EventSystem.current != null)
      {
        EventSystem.current.SetSelectedGameObject(first.gameObject);
      }
    }

    /// <summary>Los botones necesitan un EventSystem con el modulo del Input System nuevo.</summary>
    private static void EnsureEventSystem()
    {
      if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
      {
        return;
      }

      var go = new GameObject("EventSystem");
      go.AddComponent<EventSystem>();
      var module = go.AddComponent<InputSystemUIInputModule>();
      module.AssignDefaultActions();
    }
  }
}
