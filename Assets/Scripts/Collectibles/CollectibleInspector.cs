
using System.Collections;
using System.Collections.Generic;
using NuiGrab;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Collectibles
{
  public class CollectibleInspector : MonoBehaviour
  {
    public const string DefaultViewResource = "Collectibles/CollectibleInspectorUI";

    private enum State { Closed, Opening, Open, Closing }

    private static CollectibleInspector s_instance;

    [Header("Interfaz")]
    [SerializeField, Tooltip("Prefab (o objeto de la escena) con la interfaz. El prefab CollectibleSystem ya lo trae asignado.")]
    private CollectibleInspectorView _view;

    [Header("Juego")]
    [SerializeField, Tooltip("Pausa el juego (Time.timeScale = 0) mientras se examina.")]
    private bool _pauseGame = true;

    [Header("Fondo difuminado")]
    [SerializeField, Range(1, 6), Tooltip("Intensidad del difuminado (si la interfaz tiene Blurred Background).")]
    private int _blurStrength = 4;

    [Header("Camara y luces del modelo")]
    [SerializeField, Range(10f, 60f)] private float _cameraFov = 28f;

    [SerializeField, Range(0.3f, 1f), Tooltip("Cuanto llena el modelo su RawImage (antes del zoom).")]
    private float _modelFill = 0.7f;

    [SerializeField, Range(1, 8), Tooltip("Antialiasing (MSAA) de la imagen del modelo.")]
    private int _msaa = 4;

    [SerializeField, Tooltip("Donde se monta el escenario de inspeccion: lejos del nivel para que nada mas salga en camara.")]
    private Vector3 _stagePosition = new Vector3(0f, -5000f, 0f);

    [SerializeField] private Color _keyLightColor = new Color(1f, 0.95f, 0.85f);
    [SerializeField, Min(0f)] private float _keyLightIntensity = 3f;
    [SerializeField] private Color _rimLightColor = new Color(0.6f, 0.75f, 1f);
    [SerializeField, Min(0f)] private float _rimLightIntensity = 2f;

    [Header("Animacion")]
    [SerializeField, Min(0.01f)] private float _fadeDuration = 0.35f;
    [SerializeField, Min(0f), Tooltip("Retraso del bloque 'Delayed Group' / 'Slide In' respecto al fondo.")]
    private float _textDelay = 0.15f;

    [Header("Rotacion")]
    [SerializeField, Min(0f), Tooltip("Grados por segundo con el stick / teclado.")]
    private float _stickSpeed = 160f;

    [SerializeField, Min(0f), Tooltip("Grados por pixel arrastrando con el raton.")]
    private float _mouseSensitivity = 0.25f;

    [SerializeField, Min(0f), Tooltip("Grados al cruzar la imagen completa de la webcam con la mano cerrada.")]
    private float _handSensitivity = 420f;

    [SerializeField, Range(1f, 40f), Tooltip("Respuesta a la mano. Bajo = mas suave (filtra el temblor del tracking), alto = mas directo.")]
    private float _handResponsiveness = 14f;

    [SerializeField, Min(0f), Tooltip("Frenado del giro al soltar (inercia). Alto = se detiene antes.")]
    private float _inertiaDamping = 4f;

    [SerializeField] private bool _idleSpin = true;
    [SerializeField] private float _idleSpinSpeed = 10f;
    [SerializeField, Min(0f)] private float _idleSpinDelay = 2.5f;

    [Header("Zoom")]
    [SerializeField] private Vector2 _zoomRange = new Vector2(0.7f, 1.7f);
    [SerializeField, Min(0f)] private float _zoomSpeed = 1.2f;

    // Estado
    private State _state = State.Closed;
    private Collectible _target;
    private CollectibleInspectorView _activeView;
    private PlayerInput _playerInput;
    private HandGrabManager _handGrab;
    private bool _disabledHandGrab;
    private HandTracker _handTracker;
    private bool _paused;
    private bool _keepGameState;
    private bool _missingWarned;

    private readonly Dictionary<CollectibleInspectorView, CollectibleInspectorView> _instances =
      new Dictionary<CollectibleInspectorView, CollectibleInspectorView>();

    // Escenario
    private Transform _stageRoot;
    private Transform _pivot;
    private Camera _stageCamera;
    private GameObject _modelHolder;
    private RenderTexture _modelRT;
    private RenderTexture _blurRT;

    // Rotacion / zoom
    private Vector2 _angularVelocity;
    private float _idleTimer;
    private bool _handWasGripping;
    private Vector2 _smoothedHand;
    private float _zoomTarget = 1f;
    private float _zoom = 1f;
    private float _baseDistance = 4f;

    // Input propio (el del jugador esta desactivado mientras se examina)
    private InputAction _closeAction;
    private InputAction _rotateAction;
    private InputAction _zoomAction;

    public static CollectibleInspector Instance
    {
      get
      {
        if (s_instance == null)
        {
          s_instance = FindAnyObjectByType<CollectibleInspector>();

          if (s_instance == null)
          {
            s_instance = new GameObject("CollectibleInspector").AddComponent<CollectibleInspector>();
          }
        }

        return s_instance;
      }
    }

    /// <summary>True desde que se pide abrir hasta que termina de cerrarse.</summary>
    public static bool IsOpen => s_instance != null && s_instance._state != State.Closed;

    /// <summary>Coleccionable que se esta examinando (null si cerrado).</summary>
    public Collectible Target => _target;

    private void Awake()
    {
      if (s_instance != null && s_instance != this)
      {
        Destroy(this);
        return;
      }

      s_instance = this;
      CollectibleInteractor.HideIfSceneObject(_view);
    }

    private void OnDestroy()
    {
      if (_paused)
      {
        Time.timeScale = 1f;
      }

      _closeAction?.Dispose();
      _rotateAction?.Dispose();
      _zoomAction?.Dispose();

      ReleaseTexture(ref _modelRT);
      ReleaseTexture(ref _blurRT);

      if (s_instance == this)
      {
        s_instance = null;
      }
    }

    /// <summary>
    /// Abre la inspeccion. Con keepGameState (p. ej. desde el menu de pausa)
    /// no toca la pausa, el input del jugador ni el agarre: quien la abre ya
    /// los controla.
    /// </summary>
    public void Open(Collectible target, PlayerInput playerInput, bool keepGameState = false)
    {
      if (_state != State.Closed || target == null)
      {
        return;
      }

      var view = ResolveView(target);

      if (view == null || view.modelImage == null)
      {
        if (view != null)
        {
          Debug.LogError($"[CollectibleInspector] La interfaz '{view.name}' no tiene asignada 'Model Image'.", view);
        }

        return;
      }

      _keepGameState = keepGameState;
      StartCoroutine(OpenRoutine(target, view, keepGameState ? null : playerInput));
    }

    public void Close()
    {
      if (_state == State.Open)
      {
        StartCoroutine(CloseRoutine());
      }
    }

    private CollectibleInspectorView ResolveView(Collectible target)
    {
      var source = target.CustomInspectorView != null ? target.CustomInspectorView : _view;

      if (source == null)
      {
        source = Resources.Load<CollectibleInspectorView>(DefaultViewResource);
      }

      if (source == null)
      {
        if (!_missingWarned)
        {
          _missingWarned = true;
          Debug.LogError("[CollectibleInspector] No hay prefab de interfaz. Agrega el sistema a la escena: Tools > Coleccionables > Agregar sistema a la escena abierta.");
        }

        return null;
      }

      if (source.gameObject.scene.IsValid())
      {
        return source;
      }

      if (!_instances.TryGetValue(source, out var instance) || instance == null)
      {
        instance = Instantiate(source, transform);
        instance.name = source.name;
        instance.gameObject.SetActive(false);
        _instances[source] = instance;
      }

      return instance;
    }

    // ---------- Apertura / cierre ----------

    private IEnumerator OpenRoutine(Collectible target, CollectibleInspectorView view, PlayerInput playerInput)
    {
      _state = State.Opening;
      _target = target;
      _activeView = view;
      _playerInput = playerInput;

      EnsureStage();
      EnsureActions();

      if (_playerInput != null)
      {
        _playerInput.DeactivateInput();
      }

      _handGrab = _keepGameState ? null : FindAnyObjectByType<HandGrabManager>();
      _disabledHandGrab = _handGrab != null && _handGrab.enabled && !_handGrab.IsHolding;

      if (_disabledHandGrab)
      {
        _handGrab.enabled = false;
      }

      // Fondo: el frame actual, ya renderizado (sin la interfaz), difuminado.
      yield return new WaitForEndOfFrame();

      if (view.blurredBackground != null)
      {
        CaptureBlurredBackground(view.blurredBackground);
      }

      if (_pauseGame && !_keepGameState)
      {
        Time.timeScale = 0f;
        _paused = true;
      }

      // Interfaz activa pero invisible, para medir la RawImage del modelo.
      view.Fill(target);
      view.ApplyEntrance(0f, 0f);
      view.gameObject.SetActive(true);
      Canvas.ForceUpdateCanvases();
      LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)view.transform);

      EnsureModelTexture(view);
      view.modelImage.texture = _modelRT;
      view.modelImage.color = Color.white;
      view.modelImage.enabled = true;

      SpawnModel(target);
      _stageCamera.enabled = true;

      _closeAction.Enable();
      _rotateAction.Enable();
      _zoomAction.Enable();

      target.NotifyOpened();

      var total = _fadeDuration + _textDelay + _fadeDuration;
      var t = 0f;

      while (t < total)
      {
        t += Time.unscaledDeltaTime;
        view.ApplyEntrance(Smooth(t / _fadeDuration), Smooth((t - _textDelay) / _fadeDuration));
        yield return null;
      }

      view.ApplyEntrance(1f, 1f);
      _state = State.Open;
    }

    private IEnumerator CloseRoutine()
    {
      _state = State.Closing;
      var view = _activeView;

      var t = 0f;
      var duration = _fadeDuration * 0.8f;

      while (t < duration)
      {
        t += Time.unscaledDeltaTime;
        var a = 1f - Smooth(t / duration);
        view.ApplyEntrance(a, a);
        yield return null;
      }

      view.gameObject.SetActive(false);
      _activeView = null;
      _stageCamera.enabled = false;

      if (_modelHolder != null)
      {
        Destroy(_modelHolder);
        _modelHolder = null;
      }

      if (_paused)
      {
        // Siempre a 1: si un hit-stop estaba activo al abrir, ya se retiro.
        Time.timeScale = 1f;
        _paused = false;
      }

      // Espera a soltar el boton de cerrar para que no ataque / esquive.
      while (_closeAction.IsPressed())
      {
        yield return null;
      }

      _closeAction.Disable();
      _rotateAction.Disable();
      _zoomAction.Disable();

      if (_disabledHandGrab && _handGrab != null)
      {
        _handGrab.enabled = true;
      }

      _disabledHandGrab = false;

      if (_playerInput != null)
      {
        _playerInput.ActivateInput();
      }

      var target = _target;
      _target = null;
      _state = State.Closed;

      if (CollectibleInteractor.Instance != null)
      {
        CollectibleInteractor.Instance.BlockFor(0.3f);
      }

      _keepGameState = false;

      if (target != null)
      {
        target.NotifyClosed();
      }
    }

    // ---------- Update: rotacion y zoom ----------

    private void Update()
    {
      if (_state == State.Closed || _modelHolder == null)
      {
        return;
      }

      var dt = Time.unscaledDeltaTime;

      if (_state == State.Open && _closeAction.WasPressedThisFrame())
      {
        Close();
      }

      HandleRotation(dt);
      HandleZoom(dt);
    }

    private void HandleRotation(float dt)
    {
      if (dt <= 0f)
      {
        return;
      }

      var delta = Vector2.zero;
      var manual = false;

      // Stick / teclado
      var stick = _rotateAction.ReadValue<Vector2>();

      if (stick.sqrMagnitude > 0.01f)
      {
        delta += stick * (_stickSpeed * dt);
        manual = true;
      }

      // Raton (arrastrar con clic izquierdo)
      var mouse = Mouse.current;

      if (mouse != null && mouse.leftButton.isPressed)
      {
        delta += mouse.delta.ReadValue() * _mouseSensitivity;
        manual = true;
      }

      // Mano: puño cerrado + mover, como la telequinesis.
      if (_handTracker == null)
      {
        _handTracker = FindAnyObjectByType<HandTracker>();
      }

      var gripping = _handTracker != null && _handTracker.isActiveAndEnabled &&
                     _handTracker.IsHandDetected && _handTracker.IsHandClosed;

      if (gripping)
      {
        var hand = _handTracker.HandViewportPosition;

        if (!_handWasGripping)
        {
          _smoothedHand = hand;
        }

        var previous = _smoothedHand;
        var k = 1f - Mathf.Exp(-_handResponsiveness * dt);
        _smoothedHand = Vector2.Lerp(_smoothedHand, hand, k);
        delta += (_smoothedHand - previous) * _handSensitivity;
        manual = true;
      }

      _handWasGripping = gripping;

      if (manual)
      {
        _angularVelocity = Vector2.Lerp(_angularVelocity, delta / dt, 0.5f);
        _idleTimer = 0f;
        Rotate(delta);
        return;
      }

      // Inercia al soltar
      _angularVelocity *= Mathf.Exp(-_inertiaDamping * dt);

      if (_angularVelocity.sqrMagnitude > 0.5f)
      {
        Rotate(_angularVelocity * dt);
      }

      _idleTimer += dt;

      if (_idleSpin && _idleTimer >= _idleSpinDelay)
      {
        var ramp = Mathf.Clamp01((_idleTimer - _idleSpinDelay) / 1.5f);
        _pivot.Rotate(Vector3.up, -_idleSpinSpeed * ramp * dt, Space.Self);
      }
    }

    /// <summary>
    /// Giro tipo trackball respecto a la camara: horizontal = alrededor del
    /// eje vertical de la vista, vertical = alrededor del eje lateral. Mover a
    /// la derecha lleva la cara frontal a la derecha.
    /// </summary>
    private void Rotate(Vector2 degrees)
    {
      var cam = _stageCamera.transform;
      _pivot.rotation = Quaternion.AngleAxis(-degrees.x, cam.up) *
                        Quaternion.AngleAxis(degrees.y, cam.right) *
                        _pivot.rotation;
    }

    private void HandleZoom(float dt)
    {
      var input = _zoomAction.ReadValue<float>() * _zoomSpeed * dt;

      var mouse = Mouse.current;

      if (mouse != null)
      {
        var scroll = mouse.scroll.ReadValue().y;

        // Segun la plataforma/ajustes la rueda da ±1 o ±120 por paso.
        if (Mathf.Abs(scroll) > 10f)
        {
          scroll /= 120f;
        }

        input += scroll * 0.1f;
      }

      _zoomTarget = Mathf.Clamp(_zoomTarget + input, _zoomRange.x, _zoomRange.y);
      _zoom = Mathf.Lerp(_zoom, _zoomTarget, 1f - Mathf.Exp(-12f * dt));
      PlaceCamera();
    }

    private void PlaceCamera()
    {
      _stageCamera.transform.localPosition = new Vector3(0f, 0f, -_baseDistance / Mathf.Max(0.01f, _zoom));
    }

    // ---------- Fondo ----------

    private void CaptureBlurredBackground(RawImage target)
    {
      var shot = ScreenCapture.CaptureScreenshotAsTexture();

      if (shot == null)
      {
        target.texture = null;
        return;
      }

      var temps = new List<RenderTexture>();
      var w = Mathf.Max(1, shot.width / 2);
      var h = Mathf.Max(1, shot.height / 2);
      var current = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
      current.filterMode = FilterMode.Bilinear;
      Graphics.Blit(shot, current);
      temps.Add(current);

      // Bajar de resolucion varias veces (cada paso promedia vecinos) ...
      for (var i = 0; i < _blurStrength; i++)
      {
        w = Mathf.Max(1, w / 2);
        h = Mathf.Max(1, h / 2);
        var next = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        next.filterMode = FilterMode.Bilinear;
        Graphics.Blit(current, next);
        temps.Add(next);
        current = next;
      }

      // ... y volver a subir por los mismos tamaños: difuminado suave.
      for (var i = temps.Count - 2; i >= 1; i--)
      {
        Graphics.Blit(current, temps[i]);
        current = temps[i];
      }

      var finalW = Mathf.Max(1, shot.width / 4);
      var finalH = Mathf.Max(1, shot.height / 4);

      if (_blurRT == null || _blurRT.width != finalW || _blurRT.height != finalH)
      {
        ReleaseTexture(ref _blurRT);
        _blurRT = new RenderTexture(finalW, finalH, 0, RenderTextureFormat.ARGB32)
        {
          name = "CollectibleBlur",
          filterMode = FilterMode.Bilinear
        };
        _blurRT.Create();
      }

      Graphics.Blit(current, _blurRT);

      foreach (var rt in temps)
      {
        RenderTexture.ReleaseTemporary(rt);
      }

      Destroy(shot);
      target.texture = _blurRT;
      target.enabled = true;
    }

    // ---------- Escenario y modelo ----------

    private void EnsureStage()
    {
      if (_stageRoot != null)
      {
        return;
      }

      _stageRoot = new GameObject("InspectStage").transform;
      _stageRoot.SetParent(transform, false);
      _stageRoot.position = _stagePosition;
      _stageRoot.rotation = Quaternion.identity;

      _pivot = new GameObject("Pivot").transform;
      _pivot.SetParent(_stageRoot, false);

      var camGo = new GameObject("InspectCamera");
      camGo.transform.SetParent(_stageRoot, false);
      _stageCamera = camGo.AddComponent<Camera>();
      _stageCamera.clearFlags = CameraClearFlags.SolidColor;
      _stageCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
      _stageCamera.fieldOfView = _cameraFov;
      _stageCamera.nearClipPlane = 0.01f;
      _stageCamera.farClipPlane = 50f;
      _stageCamera.allowHDR = false;
      _stageCamera.enabled = false;

      // Sin post-proceso: asi la RenderTexture conserva el fondo transparente.
      var data = _stageCamera.GetUniversalAdditionalCameraData();
      data.renderPostProcessing = false;
      data.renderShadows = false;
      data.antialiasing = AntialiasingMode.None;

      // Luces propias de corto alcance (no llegan al nivel).
      CreateLight("KeyLight", new Vector3(-1.6f, 1.8f, -2.4f), _keyLightColor, _keyLightIntensity);
      CreateLight("RimLight", new Vector3(2f, 1.2f, 1.8f), _rimLightColor, _rimLightIntensity);
    }

    private void CreateLight(string lightName, Vector3 localPosition, Color color, float intensity)
    {
      var go = new GameObject(lightName);
      go.transform.SetParent(_stageRoot, false);
      go.transform.localPosition = localPosition;

      var light = go.AddComponent<Light>();
      light.type = LightType.Point;
      light.color = color;
      light.intensity = intensity;
      light.range = 12f;
      light.shadows = LightShadows.None;
      light.renderMode = LightRenderMode.ForcePixel;
    }

    /// <summary>RenderTexture del tamaño real en pantalla de la RawImage del modelo.</summary>
    private void EnsureModelTexture(CollectibleInspectorView view)
    {
      var rect = view.modelImage.rectTransform.rect;
      var scale = view.Canvas != null ? view.Canvas.rootCanvas.scaleFactor : 1f;
      var w = Mathf.Clamp(Mathf.RoundToInt(rect.width * scale), 64, 4096);
      var h = Mathf.Clamp(Mathf.RoundToInt(rect.height * scale), 64, 4096);
      var msaa = Mathf.ClosestPowerOfTwo(Mathf.Clamp(_msaa, 1, 8));

      if (_modelRT == null || _modelRT.width != w || _modelRT.height != h || _modelRT.antiAliasing != msaa)
      {
        ReleaseTexture(ref _modelRT);
        _modelRT = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
        {
          name = "CollectibleModel",
          antiAliasing = msaa
        };
        _modelRT.Create();
      }

      _stageCamera.targetTexture = _modelRT;
    }

    private void SpawnModel(Collectible target)
    {
      var source = target.InspectModel != null ? target.InspectModel : target.gameObject;

      _pivot.localRotation = Quaternion.identity;

      // Bajo un padre inactivo la copia no ejecuta Awake/OnEnable de sus
      // scripts, y se le quitan antes de mostrarla.
      _modelHolder = new GameObject("InspectModel");
      _modelHolder.SetActive(false);
      _modelHolder.transform.SetParent(_pivot, false);

      var instance = Instantiate(source, _modelHolder.transform);
      instance.transform.localPosition = Vector3.zero;
      instance.transform.localRotation = Quaternion.identity;
      instance.transform.localScale = source.transform.lossyScale;
      instance.SetActive(true);
      StripToVisuals(instance);

      _modelHolder.SetActive(true);

      // Normaliza: esfera envolvente de radio 1, centrada en el pivote.
      if (TryGetBounds(instance, out var bounds) && bounds.extents.sqrMagnitude > 0f)
      {
        instance.transform.localScale *= 1f / bounds.extents.magnitude;
        TryGetBounds(instance, out bounds);
        instance.transform.position += _pivot.position - bounds.center;
      }

      _modelHolder.transform.localScale = Vector3.one * target.InspectScale;
      _pivot.localRotation = Quaternion.Euler(target.InspectRotation);

      // Distancia para que una esfera de radio 1 ocupe _modelFill de la vista.
      var vFov = _cameraFov * Mathf.Deg2Rad;
      var aspect = (float)_modelRT.width / _modelRT.height;
      var hFov = 2f * Mathf.Atan(Mathf.Tan(vFov * 0.5f) * aspect);
      var halfAngle = Mathf.Min(vFov, hFov) * 0.5f * _modelFill;
      _baseDistance = 1f / Mathf.Sin(Mathf.Max(0.01f, halfAngle));

      _stageCamera.fieldOfView = _cameraFov;
      _stageCamera.transform.localRotation = Quaternion.identity;

      _zoom = _zoomTarget = 1f;
      _angularVelocity = Vector2.zero;
      _idleTimer = 0f;
      _handWasGripping = false;
      PlaceCamera();
    }

    /// <summary>Deja solo Transform + mallas: sin scripts, fisica, audio, etc.</summary>
    private static void StripToVisuals(GameObject root)
    {
      // Orden: primero lo que depende de otros componentes (scripts con
      // RequireComponent, joints, renderers de particulas), luego lo demas.
      DestroyAll<MonoBehaviour>(root);
      DestroyAll<Joint>(root);
      DestroyAll<ParticleSystemRenderer>(root);
      DestroyAll<ParticleSystem>(root);
      DestroyAll<Collider>(root);
      DestroyAll<Rigidbody>(root);

      var rest = root.GetComponentsInChildren<Component>(true);

      for (var i = rest.Length - 1; i >= 0; i--)
      {
        var component = rest[i];

        if (component == null || component is Transform || component is MeshFilter ||
            component is MeshRenderer || component is SkinnedMeshRenderer)
        {
          continue;
        }

        DestroyImmediate(component);
      }

      foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
      {
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.SetPropertyBlock(null);
      }
    }

    private static void DestroyAll<T>(GameObject root) where T : Component
    {
      // Al reves: los componentes añadidos despues suelen depender de los
      // anteriores (RequireComponent).
      var components = root.GetComponentsInChildren<T>(true);

      for (var i = components.Length - 1; i >= 0; i--)
      {
        if (components[i] != null)
        {
          DestroyImmediate(components[i]);
        }
      }
    }

    private static bool TryGetBounds(GameObject root, out Bounds bounds)
    {
      bounds = default;
      var found = false;

      foreach (var renderer in root.GetComponentsInChildren<Renderer>())
      {
        if (!found)
        {
          bounds = renderer.bounds;
          found = true;
        }
        else
        {
          bounds.Encapsulate(renderer.bounds);
        }
      }

      return found;
    }

    // ---------- Input ----------

    private void EnsureActions()
    {
      if (_closeAction != null)
      {
        return;
      }

      _closeAction = new InputAction("InspectClose", InputActionType.Button);
      _closeAction.AddBinding("<Keyboard>/f");
      _closeAction.AddBinding("<Keyboard>/escape");
      _closeAction.AddBinding("<Gamepad>/buttonSouth");
      _closeAction.AddBinding("<Gamepad>/buttonEast");

      _rotateAction = new InputAction("InspectRotate", InputActionType.Value, expectedControlType: "Vector2");
      _rotateAction.AddBinding("<Gamepad>/rightStick", processors: "stickDeadzone");
      _rotateAction.AddBinding("<Gamepad>/leftStick", processors: "stickDeadzone");
      _rotateAction.AddCompositeBinding("2DVector")
        .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
        .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
      _rotateAction.AddCompositeBinding("2DVector")
        .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
        .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");

      _zoomAction = new InputAction("InspectZoom", InputActionType.Value, expectedControlType: "Axis");
      _zoomAction.AddCompositeBinding("1DAxis")
        .With("Positive", "<Gamepad>/rightTrigger")
        .With("Negative", "<Gamepad>/leftTrigger");
    }

    // ---------- Utilidades ----------

    private static float Smooth(float t)
    {
      t = Mathf.Clamp01(t);
      return t * t * (3f - 2f * t);
    }

    private static void ReleaseTexture(ref RenderTexture rt)
    {
      if (rt == null)
      {
        return;
      }

      rt.Release();
      Destroy(rt);
      rt = null;
    }
  }
}
