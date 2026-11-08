// InteractiveCameraFocus.cs
// Objeto que, al interactuar con el (F / X, mismo aviso que los
// coleccionables), lleva la camara a una vista cenital sobre el, encuadrandolo
// entero (ej. la mesa de billar). El jugador queda quieto mientras tanto.
//
// Salir: F / X otra vez, Circulo (mando) o Retroceso (teclado).
// Eventos OnFocusStart / OnFocusEnd para enganchar el minijuego mas adelante.
//
// Ponlo en la raiz del objeto (la mesa). El encuadre se calcula con sus
// mallas; si quieres otro encuadre asigna "Frame Target".

using System.Collections;
using Collectibles;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class InteractiveCameraFocus : MonoBehaviour, IInteractable
{
  public enum FitMode
  {
    [Tooltip("Se ve el objeto entero.")]
    Todo,
    [Tooltip("El ancho del objeto llena la pantalla (arriba y abajo se recorta).")]
    Ancho,
    [Tooltip("El alto del objeto llena la pantalla (los lados se recortan).")]
    Alto
  }

  /// <summary>True mientras alguna camara enfocada esta activa.</summary>
  public static bool IsActive { get; private set; }

  [Header("Interaccion")]
  [SerializeField] private string _promptText = "Mirar";
  [SerializeField, Min(0f), Tooltip("Distancia extra al borde del objeto a la que aparece el aviso.")]
  private float _interactMargin = 2f;
  [SerializeField, Tooltip("Altura del aviso sobre el objeto.")]
  private float _promptHeight = 1f;
  [SerializeField, Tooltip("Aviso propio (vacio = el de los coleccionables).")]
  private CollectiblePromptView _customPromptView;

  [Header("Encuadre")]
  [SerializeField, Tooltip("Lo que se encuadra (vacio = este objeto y sus hijos).")]
  private Transform _frameTarget;
  [SerializeField, Tooltip("Como se encaja el objeto en la pantalla.")]
  private FitMode _fit = FitMode.Todo;
  [SerializeField, Range(0.3f, 2f), Tooltip("Margen alrededor del objeto (1 = justo, menos de 1 = mas cerca, recortando los bordes).")]
  private float _padding = 1.15f;
  [SerializeField, Tooltip("Gira la vista para que el lado largo quede horizontal en pantalla.")]
  private bool _longSideHorizontal = true;
  [SerializeField, Tooltip("Giro extra de la vista (grados).")]
  private float _yawOffset = 0f;
  [Header("Zona libre de la pantalla (el HUD sigue visible)")]
  [SerializeField, Range(0f, 0.6f), Tooltip("Parte de arriba de la pantalla que ocupa el HUD (hojas, minimapa). El objeto se encuadra debajo.")]
  private float _reserveTop = 0.3f;
  [SerializeField, Range(0f, 0.6f), Tooltip("Parte de abajo reservada (ej. ventanita de la webcam).")]
  private float _reserveBottom = 0.05f;
  [SerializeField, Range(0f, 0.6f)] private float _reserveLeft = 0f;
  [SerializeField, Range(0f, 0.6f)] private float _reserveRight = 0f;

  [SerializeField, Min(1f), Tooltip("Altura de la camara sobre la parte mas alta del objeto.")]
  private float _heightAbove = 15f;
  [SerializeField, Min(0.05f), Tooltip("Duracion del movimiento de camara.")]
  private float _transitionTime = 0.8f;

  [Header("Mientras se mira")]
  [SerializeField, Range(0.05f, 1f), Tooltip("Tamaño de los efectos de la mano (cursor, anillo, particulas) en esta vista. Bajo = mas pequeños, para agarrar cosas pequeñas con calma.")]
  private float _handEffectScale = 0.3f;
  [SerializeField, Tooltip("Oculta el lazo que va de la nutria al objeto agarrado.")]
  private bool _hideHandTether = true;
  [SerializeField, Tooltip("Objetos que se ocultan (ej. HUD, minimapa). Se vuelven a mostrar al salir.")]
  private GameObject[] _hideWhileFocused;

  [Header("Eventos")]
  public UnityEvent OnFocusStart = new UnityEvent();
  public UnityEvent OnFocusEnd = new UnityEvent();

  private Camera _camera;
  private TopDownCamera _topDown;
  private PlayerInput _playerInput;
  private Rigidbody _playerBody;
  private InputAction _exitAction;
  private Bounds _bounds;
  private bool _focused;
  private bool _busy;
  private float _canExitAt;

  private Vector3 _savedPosition;
  private Quaternion _savedRotation;
  private float _savedSize;
  private float _savedFov;
  private float _savedNear;
  private float _savedFar;

  /// <summary>True mientras este objeto tiene la camara.</summary>
  public bool IsFocused => _focused;

  // ---------- IInteractable ----------

  public bool CanInteract => isActiveAndEnabled && !_focused && !_busy && !IsActive;

  public Vector3 PromptPoint => new Vector3(_bounds.center.x, _bounds.max.y + _promptHeight, _bounds.center.z);

  public string PromptText => _promptText;

  public CollectiblePromptView CustomPromptView => _customPromptView;

  public bool IsInRange(Vector3 playerPosition, out float sqrDistance)
  {
    // Distancia al borde del objeto (no al centro): sirve para mesas grandes.
    var closest = _bounds.ClosestPoint(new Vector3(playerPosition.x, _bounds.center.y, playerPosition.z));
    var delta = playerPosition - closest;
    delta.y = 0f;
    sqrDistance = delta.sqrMagnitude;
    return sqrDistance <= _interactMargin * _interactMargin && Mathf.Abs(playerPosition.y - _bounds.min.y) <= 3f;
  }

  public void Interact(PlayerInput playerInput)
  {
    if (CanInteract)
    {
      _playerInput = playerInput;
      StartCoroutine(FocusRoutine());
    }
  }

  // ---------- Ciclo ----------

  private void Awake()
  {
    RecalculateBounds();

    _exitAction = new InputAction("ExitFocus", InputActionType.Button);
    _exitAction.AddBinding("<Keyboard>/f");
    _exitAction.AddBinding("<Keyboard>/backspace");
    _exitAction.AddBinding("<Gamepad>/buttonSouth");
    _exitAction.AddBinding("<Gamepad>/buttonEast");
  }

  private void OnEnable()
  {
    InteractableRegistry.Register(this);
    _exitAction.Enable();
  }

  private void OnDisable()
  {
    InteractableRegistry.Unregister(this);
    _exitAction.Disable();

    if (_focused)
    {
      Restore(true);
    }
  }

  private void OnDestroy() => _exitAction?.Dispose();

  private void Update()
  {
    // Si el menu de pausa devolvio el control al jugador, se vuelve a quitar.
    if (_focused && _playerInput != null && _playerInput.inputIsActive && !Menus.PauseMenu.IsOpen)
    {
      _playerInput.DeactivateInput();
    }

    if (_focused && !_busy && Time.unscaledTime >= _canExitAt && !Menus.PauseMenu.IsOpen && _exitAction.WasPressedThisFrame())
    {
      StartCoroutine(UnfocusRoutine());
    }
  }

  /// <summary>Vuelve a medir el objeto (si cambia de tamaño o se mueve).</summary>
  public void RecalculateBounds()
  {
    var root = _frameTarget != null ? _frameTarget : transform;
    var found = false;
    _bounds = new Bounds(root.position, Vector3.one);

    foreach (var r in root.GetComponentsInChildren<Renderer>())
    {
      if (r is ParticleSystemRenderer) continue;
      if (found) _bounds.Encapsulate(r.bounds); else _bounds = r.bounds;
      found = true;
    }
  }

  /// <summary>Sale de la vista desde codigo (ej. al terminar el minijuego).</summary>
  public void ExitFocus()
  {
    if (_focused && !_busy)
    {
      StartCoroutine(UnfocusRoutine());
    }
  }

  // ---------- Entrar / salir ----------

  private IEnumerator FocusRoutine()
  {
    _camera = Camera.main;

    if (_camera == null)
    {
      yield break;
    }

    _busy = true;
    _focused = true;
    IsActive = true;
    RecalculateBounds();

    _topDown = _camera.GetComponent<TopDownCamera>();
    _savedPosition = _camera.transform.position;
    _savedRotation = _camera.transform.rotation;
    _savedSize = _camera.orthographicSize;
    _savedFov = _camera.fieldOfView;
    _savedNear = _camera.nearClipPlane;
    _savedFar = _camera.farClipPlane;

    if (_topDown != null) _topDown.enabled = false;

    // Jugador quieto.
    if (_playerInput == null)
    {
      var player = FindAnyObjectByType<Player>();
      _playerInput = player != null ? player.GetComponent<PlayerInput>() : null;
    }

    if (_playerInput != null)
    {
      _playerInput.DeactivateInput();
      _playerBody = _playerInput.GetComponent<Rigidbody>();

      if (_playerBody != null && !_playerBody.isKinematic)
      {
        _playerBody.linearVelocity = new Vector3(0f, _playerBody.linearVelocity.y, 0f);
      }
    }

    SetHidden(true);
    NuiGrab.TelekinesisVFX.EffectScale = _handEffectScale;
    NuiGrab.TelekinesisVFX.HideTether = _hideHandTether;
    OnFocusStart.Invoke();

    ComputeView(out var position, out var rotation, out var size, out var fov);
    var far = Mathf.Max(_savedFar, _heightAbove + _bounds.size.y + 20f);
    yield return Move(position, rotation, size, fov, 0.3f, far);

    _busy = false;
    _canExitAt = Time.unscaledTime + 0.3f;
  }

  private IEnumerator UnfocusRoutine()
  {
    _busy = true;

    var position = _savedPosition;
    var rotation = _savedRotation;

    if (_topDown != null && _topDown.TryGetTargetPose(out var p, out var r))
    {
      position = p;
      rotation = r;
    }

    yield return Move(position, rotation, _savedSize, _savedFov, _savedNear, _savedFar);
    Restore(false);
  }

  private void Restore(bool instant)
  {
    if (_camera != null)
    {
      _camera.orthographicSize = _savedSize;
      _camera.fieldOfView = _savedFov;
      _camera.nearClipPlane = _savedNear;
      _camera.farClipPlane = _savedFar;

      if (instant)
      {
        _camera.transform.SetPositionAndRotation(_savedPosition, _savedRotation);
      }
    }

    if (_topDown != null)
    {
      _topDown.enabled = true;
      _topDown.SnapToTarget();
    }

    if (_playerInput != null && !Menus.PauseMenu.IsOpen)
    {
      _playerInput.ActivateInput();
    }

    SetHidden(false);
    NuiGrab.TelekinesisVFX.EffectScale = 1f;
    NuiGrab.TelekinesisVFX.HideTether = false;

    // Que el F / X de salida no vuelva a abrir la vista ni examine nada.
    if (CollectibleInteractor.Instance != null)
    {
      CollectibleInteractor.Instance.BlockFor(0.35f);
    }

    _focused = false;
    _busy = false;
    IsActive = false;
    OnFocusEnd.Invoke();
  }

  private void ComputeView(out Vector3 position, out Quaternion rotation, out float orthoSize, out float fov)
  {
    var yaw = _yawOffset;

    if (_longSideHorizontal)
    {
      // Lado largo en X -> la pantalla mira hacia +Z (yaw 0). Lado largo en Z -> girar 90.
      yaw += _bounds.size.z > _bounds.size.x ? 90f : 0f;
    }

    rotation = Quaternion.Euler(90f, yaw, 0f);

    // Tamaño en pantalla: ancho = lado horizontal, alto = lado vertical.
    var rotated = Mathf.Abs(Mathf.DeltaAngle(yaw, 90f)) < 45f || Mathf.Abs(Mathf.DeltaAngle(yaw, -90f)) < 45f;
    var screenWidth = rotated ? _bounds.size.z : _bounds.size.x;
    var screenHeight = rotated ? _bounds.size.x : _bounds.size.z;
    var aspect = Mathf.Max(0.1f, _camera.aspect);

    // Zona libre (sin HUD) en fracciones de pantalla.
    var freeW = Mathf.Max(0.1f, 1f - _reserveLeft - _reserveRight);
    var freeH = Mathf.Max(0.1f, 1f - _reserveTop - _reserveBottom);
    var byHeight = screenHeight * 0.5f / freeH;
    var byWidth = screenWidth * 0.5f / (aspect * freeW);
    var halfHeight = (_fit == FitMode.Ancho ? byWidth : _fit == FitMode.Alto ? byHeight : Mathf.Max(byHeight, byWidth)) * _padding;

    orthoSize = halfHeight;
    var height = _heightAbove;
    fov = _savedFov;

    if (!_camera.orthographic)
    {
      // En perspectiva: distancia para que quepa con el campo de vision actual.
      height = halfHeight / Mathf.Tan(_savedFov * 0.5f * Mathf.Deg2Rad);
    }

    // El objeto se centra en la zona libre, no en el centro de la pantalla.
    var cx = _reserveLeft + freeW * 0.5f;
    var cy = _reserveBottom + freeH * 0.5f;
    var right = rotation * Vector3.right;
    var up = rotation * Vector3.up;
    var shift = right * ((cx - 0.5f) * 2f * halfHeight * aspect) + up * ((cy - 0.5f) * 2f * halfHeight);

    position = new Vector3(_bounds.center.x, _bounds.max.y + height, _bounds.center.z) - shift;
  }

  private IEnumerator Move(Vector3 position, Quaternion rotation, float size, float fov, float near, float far)
  {
    var startPos = _camera.transform.position;
    var startRot = _camera.transform.rotation;
    var startSize = _camera.orthographicSize;
    var startFov = _camera.fieldOfView;
    _camera.farClipPlane = Mathf.Max(_camera.farClipPlane, far);
    _camera.nearClipPlane = Mathf.Min(_camera.nearClipPlane, near);
    var t = 0f;

    while (t < 1f)
    {
      t = Mathf.Min(1f, t + Time.unscaledDeltaTime / _transitionTime);
      var e = t * t * (3f - 2f * t);
      _camera.transform.SetPositionAndRotation(Vector3.Lerp(startPos, position, e), Quaternion.Slerp(startRot, rotation, e));
      _camera.orthographicSize = Mathf.Lerp(startSize, size, e);
      _camera.fieldOfView = Mathf.Lerp(startFov, fov, e);
      yield return null;
    }

    _camera.nearClipPlane = near;
    _camera.farClipPlane = far;
  }

  private void SetHidden(bool hidden)
  {
    if (_hideWhileFocused == null)
    {
      return;
    }

    foreach (var go in _hideWhileFocused)
    {
      if (go != null) go.SetActive(!hidden);
    }
  }

  private void OnDrawGizmosSelected()
  {
    if (!Application.isPlaying)
    {
      RecalculateBounds();
    }

    Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.6f);
    Gizmos.DrawWireCube(_bounds.center, _bounds.size + new Vector3(_interactMargin * 2f, 0f, _interactMargin * 2f));
    Gizmos.DrawSphere(PromptPoint, 0.1f);
  }
}
