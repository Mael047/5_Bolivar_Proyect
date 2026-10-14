// CameraSetup.cs
// Configuracion de la camara (webcam) para la NUI, en dos pasos:
//   1. "Configura tu camara": lista de webcams a la izquierda y vista previa a
//      la derecha. Elegir una cambia la webcam en vivo y queda guardada.
//   2. "Prueba de gesto": la vista previa con un objeto de prueba. Abre la
//      mano, cierrala sobre el objeto y muevelo, para comprobar que el
//      tracking funciona antes de jugar.
//
// Se abre desde el menu principal (Nueva partida, antes de entrar al juego) y
// desde el menu de pausa (boton Ajustes). Volver (Esc / Circulo) retrocede un
// paso; en el primero, cancela (en el menu principal vuelve al menu).
//
// Va en la raiz del Canvas (prefab CameraSetupUI). Todo el aspecto (libro,
// marcos, botones, textos) se diseña en el prefab.

using System;
using System.Collections;
using System.Collections.Generic;
using Collectibles;
using NuiGrab;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Menus
{
  [RequireComponent(typeof(Canvas))]
  [DisallowMultipleComponent]
  public class CameraSetup : MonoBehaviour
  {
    public enum Mode
    {
      [Tooltip("Desde Nueva partida: al terminar se entra al juego.")]
      NuevaPartida,
      [Tooltip("Desde Ajustes del menu de pausa: al terminar se vuelve a la pausa.")]
      Ajustes
    }

    /// <summary>True mientras la configuracion esta abierta.</summary>
    public static bool IsOpen { get; private set; }

    [Header("Paso 1: elegir camara")]
    [SerializeField] private GameObject _cameraStep;

    [SerializeField, Tooltip("Boton de ejemplo de la lista; se duplica por cada webcam. " +
                              "Un hijo llamado 'Selected' se muestra en la camara elegida.")]
    private Button _cameraButtonTemplate;

    [SerializeField] private RawImage _cameraPreview;

    [SerializeField, Tooltip("Se muestra si no hay ninguna webcam (opcional).")]
    private GameObject _noCameraMessage;

    [SerializeField, Tooltip("Texto de estado: iniciando / buscando la mano / mano detectada (opcional).")]
    private Graphic _cameraStatus;

    [SerializeField] private Button _cameraContinue;

    [Header("Paso 2: prueba de gesto")]
    [SerializeField] private GameObject _gestureStep;
    [SerializeField] private RawImage _gesturePreview;

    [SerializeField, Tooltip("Marcador de la mano, hijo de la vista previa del paso 2.")]
    private RectTransform _handCursor;

    [SerializeField, Tooltip("Objeto que se agarra y se mueve, hijo de la vista previa del paso 2.")]
    private RectTransform _testObject;

    [SerializeField, Tooltip("Se muestran al abrir la mano.")] private GameObject[] _openDone;
    [SerializeField, Tooltip("Se muestran al cerrar la mano.")] private GameObject[] _closeDone;
    [SerializeField, Tooltip("Se muestran al mover el objeto.")] private GameObject[] _moveDone;

    [SerializeField] private Button _gestureContinue;
    [SerializeField, Tooltip("Vuelve al paso 1 (opcional).")] private Button _gestureBack;

    [Header("Prueba")]
    [SerializeField, Tooltip("Pixeles (a 1080p) entre la mano y el objeto para poder agarrarlo.")]
    private float _grabRadius = 110f;

    [SerializeField, Tooltip("Pixeles (a 1080p) que hay que mover el objeto para dar la prueba por buena.")]
    private float _moveDistance = 180f;

    [SerializeField, Range(1f, 40f)] private float _followSpeed = 18f;
    [SerializeField] private Color _cursorOpen = new Color(0.3f, 0.8f, 1f, 0.9f);
    [SerializeField] private Color _cursorClosed = new Color(1f, 0.6f, 0.2f, 1f);
    [SerializeField] private Color _objectIdle = Color.white;
    [SerializeField] private Color _objectHeld = new Color(1f, 0.95f, 0.5f, 1f);

    [Header("Imagen")]
    [SerializeField, Tooltip("Espeja la vista previa (como un espejo).")]
    private bool _mirrorPreview = true;

    [SerializeField, Tooltip("Invierte horizontalmente el marcador de la mano si se mueve al reves que tu mano.")]
    private bool _mirrorCursor;

    [Header("Textos de estado")]
    [SerializeField] private string _textStarting = "Iniciando cámara...";
    [SerializeField] private string _textNoHand = "Muestra tu mano a la cámara";
    [SerializeField] private string _textHand = "¡Mano detectada!";
    [SerializeField] private string _textNoTracker = "El sistema de manos no está en la escena";

    [Header("Animacion")]
    [SerializeField, Min(0.01f)] private float _fadeDuration = 0.25f;

    private Canvas _canvas;
    private CanvasGroup _group;
    private HandTracker _tracker;
    private Mode _mode;
    private int _step;
    private Action<bool> _onClosed;
    private readonly List<Button> _cameraButtons = new List<Button>();

    private InputAction _backAction;

    private Vector2 _objectStart;
    private bool _holding;
    private bool _wasClosed;
    private float _openTimer;
    private Vector2 _grabFrom;
    private bool _didOpen, _didClose, _didMove;

    /// <summary>True si la ultima vez se salio cancelando (Volver en el primer paso).</summary>
    public bool WasCancelled { get; private set; }

    private void Awake()
    {
      _canvas = GetComponent<Canvas>();
      _group = GetComponent<CanvasGroup>();

      if (_group == null)
      {
        _group = gameObject.AddComponent<CanvasGroup>();
      }

      _canvas.enabled = false;
      _group.alpha = 0f;
      _group.interactable = false;
      _group.blocksRaycasts = false;

      if (_cameraButtonTemplate != null)
      {
        _cameraButtonTemplate.gameObject.SetActive(false);
      }

      if (_testObject != null)
      {
        _objectStart = _testObject.anchoredPosition;
      }

      if (_cameraContinue != null) _cameraContinue.onClick.AddListener(() => ShowStep(2));
      if (_gestureContinue != null) _gestureContinue.onClick.AddListener(() => Close(false));
      if (_gestureBack != null) _gestureBack.onClick.AddListener(() => ShowStep(1));

      _backAction = new InputAction("CameraSetupBack", InputActionType.Button);
      _backAction.AddBinding("<Keyboard>/escape");
      _backAction.AddBinding("<Gamepad>/buttonEast");
      _backAction.performed += _ => Back();

      IsOpen = false;
    }

    private void OnDestroy()
    {
      _backAction?.Dispose();

      if (IsOpen)
      {
        IsOpen = false;
      }
    }

    // ---------- Abrir / cerrar ----------

    public void Open(Mode mode, Action<bool> onClosed = null)
    {
      if (IsOpen)
      {
        return;
      }

      IsOpen = true;
      WasCancelled = false;
      _mode = mode;
      _onClosed = onClosed;

      _tracker = FindAnyObjectByType<HandTracker>(FindObjectsInactive.Include);
      EnsureActive(_tracker);

      EnsureEventSystem();
      BuildCameraList();
      ShowStep(1);

      _canvas.enabled = true;
      _group.interactable = true;
      _group.blocksRaycasts = true;
      _backAction.Enable();
      StopAllCoroutines();
      StartCoroutine(Fade(0f, 1f, null));
    }

    /// <summary>Abre y espera a que se cierre (para usar desde otra corrutina).</summary>
    public IEnumerator OpenAndWait(Mode mode)
    {
      Open(mode);

      while (IsOpen)
      {
        yield return null;
      }
    }

    private void Close(bool cancelled)
    {
      if (!IsOpen || !_group.interactable)
      {
        return;
      }

      WasCancelled = cancelled;
      _group.interactable = false;
      _group.blocksRaycasts = false;
      _backAction.Disable();

      if (EventSystem.current != null)
      {
        EventSystem.current.SetSelectedGameObject(null);
      }

      StopAllCoroutines();
      StartCoroutine(Fade(_group.alpha, 0f, () =>
      {
        _canvas.enabled = false;
        IsOpen = false;
        _onClosed?.Invoke(cancelled);
        _onClosed = null;
      }));
    }

    private void Back()
    {
      if (!IsOpen || !_group.interactable)
      {
        return;
      }

      if (_step == 2)
      {
        ShowStep(1);
      }
      else
      {
        // En Ajustes, volver = cerrar sin mas. En Nueva partida = cancelar.
        Close(_mode == Mode.NuevaPartida);
      }
    }

    private IEnumerator Fade(float from, float to, Action done)
    {
      var t = 0f;

      while (t < _fadeDuration)
      {
        t += Time.unscaledDeltaTime;
        _group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / _fadeDuration));
        yield return null;
      }

      _group.alpha = to;
      done?.Invoke();
    }

    // ---------- Pasos ----------

    private void ShowStep(int step)
    {
      _step = step;

      if (_cameraStep != null) _cameraStep.SetActive(step == 1);
      if (_gestureStep != null) _gestureStep.SetActive(step == 2);

      if (step == 2)
      {
        ResetGestureTest();
      }

      Selectable first = null;

      if (step == 1)
      {
        first = _cameraButtons.Count > 0 ? _cameraButtons[Mathf.Max(0, CurrentCameraIndex())] : _cameraContinue;
      }
      else
      {
        first = _gestureContinue;
      }

      if (EventSystem.current != null)
      {
        EventSystem.current.SetSelectedGameObject(first != null ? first.gameObject : null);
      }
    }

    // ---------- Paso 1: lista de camaras ----------

    private void BuildCameraList()
    {
      foreach (var button in _cameraButtons)
      {
        if (button != null) Destroy(button.gameObject);
      }

      _cameraButtons.Clear();
      var names = HandTracker.AvailableCameras;

      if (_noCameraMessage != null)
      {
        _noCameraMessage.SetActive(names.Length == 0);
      }

      if (_cameraButtonTemplate == null)
      {
        return;
      }

      for (var i = 0; i < names.Length; i++)
      {
        var index = i;
        var button = Instantiate(_cameraButtonTemplate, _cameraButtonTemplate.transform.parent);
        button.name = $"Camera_{i}";
        button.gameObject.SetActive(true);
        SetLabel(button, names[i]);
        button.onClick.AddListener(() => SelectCamera(index));
        _cameraButtons.Add(button);
      }

      RefreshSelectedMarker();
    }

    private void SelectCamera(int index)
    {
      if (_tracker != null)
      {
        _tracker.SelectCamera(index);
      }
      else
      {
        // Sin tracker en la escena: al menos se guarda para la proxima vez.
        PlayerPrefs.SetString("nui.cameraName", HandTracker.AvailableCameras[index]);
        PlayerPrefs.Save();
      }

      RefreshSelectedMarker(index);
    }

    private int CurrentCameraIndex()
    {
      var current = _tracker != null ? _tracker.CurrentCamera : PlayerPrefs.GetString("nui.cameraName", "");
      return Array.IndexOf(HandTracker.AvailableCameras, current);
    }

    private void RefreshSelectedMarker(int selected = -1)
    {
      if (selected < 0)
      {
        selected = CurrentCameraIndex();
      }

      for (var i = 0; i < _cameraButtons.Count; i++)
      {
        var marker = _cameraButtons[i].transform.Find("Selected");

        if (marker != null)
        {
          marker.gameObject.SetActive(i == selected);
        }
      }
    }

    private static void SetLabel(Button button, string text)
    {
      foreach (var graphic in button.GetComponentsInChildren<Graphic>(true))
      {
        if (graphic is Text || graphic is TMPro.TMP_Text)
        {
          UIText.Set(graphic, text);
          return;
        }
      }
    }

    // ---------- Update ----------

    private void Update()
    {
      if (!IsOpen)
      {
        return;
      }

      var texture = _tracker != null && _tracker.isActiveAndEnabled ? _tracker.PreviewTexture : null;
      var preview = _step == 1 ? _cameraPreview : _gesturePreview;

      if (preview != null)
      {
        preview.texture = texture;
        preview.color = texture != null ? Color.white : new Color(0f, 0f, 0f, 0.6f);

        if (texture != null)
        {
          preview.uvRect = CoverRect(texture, preview.rectTransform.rect.size, _mirrorPreview);
        }
      }

      if (_cameraStatus != null)
      {
        string status;

        if (_tracker == null) status = _textNoTracker;
        else if (!_tracker.IsCameraReady) status = _textStarting;
        else status = _tracker.IsHandDetected ? _textHand : _textNoHand;

        UIText.Set(_cameraStatus, status);
      }

      if (_step == 2)
      {
        UpdateGestureTest();
      }
    }

    /// <summary>Recorta la imagen para llenar el marco sin deformarla (como 'cover').</summary>
    private static Rect CoverRect(Texture texture, Vector2 size, bool mirror)
    {
      var texAspect = (float)texture.width / Mathf.Max(1, texture.height);
      var boxAspect = size.x / Mathf.Max(1f, size.y);
      var w = 1f;
      var h = 1f;

      if (texAspect > boxAspect) w = boxAspect / texAspect;
      else h = texAspect / boxAspect;

      var x = (1f - w) * 0.5f;
      var y = (1f - h) * 0.5f;
      return mirror ? new Rect(x + w, y, -w, h) : new Rect(x, y, w, h);
    }

    // ---------- Paso 2: prueba de gesto ----------

    private void ResetGestureTest()
    {
      _holding = false;
      _wasClosed = false;
      _didOpen = _didClose = _didMove = false;
      SetDone(_openDone, false);
      SetDone(_closeDone, false);
      SetDone(_moveDone, false);

      if (_testObject != null)
      {
        _testObject.anchoredPosition = _objectStart;
        SetColor(_testObject, _objectIdle);
      }

      if (_handCursor != null)
      {
        _handCursor.gameObject.SetActive(false);
      }
    }

    private void UpdateGestureTest()
    {
      if (_gesturePreview == null)
      {
        return;
      }

      var dt = Time.unscaledDeltaTime;
      // Las posiciones del Canvas ya estan en pixeles de referencia (1080p).
      const float scale = 1f;
      var detected = _tracker != null && _tracker.isActiveAndEnabled && _tracker.IsHandDetected;
      var closed = detected && _tracker.IsHandClosed;

      // Mano -> posicion dentro de la vista previa.
      var cursor = Vector2.zero;

      if (detected)
      {
        var vp = _tracker.HandViewportPosition;
        if (_mirrorCursor) vp.x = 1f - vp.x;
        var size = _gesturePreview.rectTransform.rect.size;
        cursor = new Vector2((vp.x - 0.5f) * size.x, (vp.y - 0.5f) * size.y);
      }

      if (_handCursor != null)
      {
        _handCursor.gameObject.SetActive(detected);

        if (detected)
        {
          _handCursor.anchoredPosition = cursor;
          SetColor(_handCursor, closed ? _cursorClosed : _cursorOpen);
        }
      }

      if (detected && !closed && !_didOpen)
      {
        _didOpen = true;
        SetDone(_openDone, true);
      }

      if (closed && !_didClose)
      {
        _didClose = true;
        SetDone(_closeDone, true);
      }

      if (_testObject == null)
      {
        _wasClosed = closed;
        return;
      }

      var objectPos = _testObject.anchoredPosition;

      if (!_holding)
      {
        // Agarrar: cerrar la mano estando sobre el objeto.
        if (closed && (cursor - objectPos).magnitude <= _grabRadius * scale)
        {
          _holding = true;
          _grabFrom = objectPos;
          _openTimer = 0f;
          SetColor(_testObject, _objectHeld);
        }
      }
      else
      {
        // Soltar al abrir la mano (con un pequeño margen contra el ruido del tracking).
        _openTimer = closed ? 0f : _openTimer + dt;

        if (_openTimer > 0.15f)
        {
          _holding = false;
          SetColor(_testObject, _objectIdle);
        }
        else if (detected)
        {
          var half = _gesturePreview.rectTransform.rect.size * 0.5f;
          var target = new Vector2(Mathf.Clamp(cursor.x, -half.x, half.x), Mathf.Clamp(cursor.y, -half.y, half.y));
          _testObject.anchoredPosition = Vector2.Lerp(objectPos, target, 1f - Mathf.Exp(-_followSpeed * dt));

          if (!_didMove && (_testObject.anchoredPosition - _grabFrom).magnitude >= _moveDistance * scale)
          {
            _didMove = true;
            SetDone(_moveDone, true);
          }
        }
      }

      _wasClosed = closed;
    }

    private static void SetDone(GameObject[] objects, bool done)
    {
      if (objects == null) return;

      foreach (var go in objects)
      {
        if (go != null) go.SetActive(done);
      }
    }

    private static void SetColor(RectTransform rect, Color color)
    {
      var graphic = rect.GetComponent<Graphic>();
      if (graphic != null) graphic.color = color;
    }

    // ---------- Utilidades ----------

    /// <summary>Si el sistema de manos estaba apagado (p. ej. por el menu), se enciende.</summary>
    private static void EnsureActive(Component component)
    {
      if (component == null) return;

      var t = component.transform;

      while (t != null)
      {
        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
        t = t.parent;
      }

      if (component is Behaviour behaviour && !behaviour.enabled)
      {
        behaviour.enabled = true;
      }
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
