// PlayerPortrait.cs
// Retrato 3D en vivo del personaje (estilo LEGO): una copia solo-visual del
// modelo del jugador en un escenario aislado, lejos del nivel, con camara y
// luces propias, dibujada en una RawImage del HUD con fondo transparente.
// Cada frame la copia imita la pose del jugador real (copia las
// posiciones/rotaciones de sus huesos), asi que el retrato respira, ataca,
// esquiva y recibe golpes igual que el personaje, pero siempre mirando a camara.
// Al recibir daño el retrato tiembla y se tiñe de rojo un instante.
//
// Modo "Animacion Propia" (pantalla de Estado del menu de pausa): la copia
// conserva su Animator y reproduce su estado por defecto (idle) en tiempo no
// escalado, asi se anima aunque el juego este en pausa.
//
// Va en una RawImage (retrato del HUD, modelo de la pantalla de Estado...).
// Cada retrato monta su propio escenario, separado de los demas.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public class PlayerPortrait : MonoBehaviour
{
  public enum AnimationMode
  {
    [Tooltip("Imita en vivo la pose del jugador (HUD).")]
    CopiarJugador,
    [Tooltip("Su propio Animator reproduce el estado por defecto (idle), tambien en pausa.")]
    AnimacionPropia
  }

  private static int s_stageCount;

  [Header("Modelo")]
  [SerializeField] private AnimationMode _animationMode = AnimationMode.CopiarJugador;

  [SerializeField, Tooltip("Permite girar el modelo arrastrando con el raton o con el stick derecho (solo Animacion Propia).")]
  private bool _allowRotate = true;

  [SerializeField, Tooltip("Raiz visual del jugador (normalmente el objeto con el Animator). Vacio = Animator del objeto con tag Player.")]
  private Transform _source;

  [Header("Encuadre")]
  [SerializeField, Range(0f, 1f), Tooltip("Altura del punto al que mira la camara (0 = pies, 1 = punta de la cabeza).")]
  private float _focusHeight = 0.8f;

  [SerializeField, Range(0.1f, 2f), Tooltip("Que tanto del cuerpo se ve. Bajo = solo la cara, alto = cuerpo entero.")]
  private float _framing = 0.45f;

  [SerializeField, Range(-90f, 90f), Tooltip("Giro del personaje respecto a la camara (0 = de frente).")]
  private float _yaw = -20f;

  [SerializeField, Range(-30f, 45f), Tooltip("Inclinacion de la camara (positivo = desde arriba).")]
  private float _pitch = 5f;

  [SerializeField, Range(10f, 60f)] private float _fov = 25f;

  [SerializeField, Range(0.25f, 2f), Tooltip("Resolucion de la imagen respecto a su tamaño en pantalla.")]
  private float _resolutionScale = 1f;

  [SerializeField, Tooltip("Donde se monta el escenario del retrato (lejos del nivel).")]
  private Vector3 _stagePosition = new Vector3(0f, -6000f, 0f);

  [Header("Luces")]
  [SerializeField] private Color _keyLightColor = new Color(1f, 0.96f, 0.88f);
  [SerializeField, Min(0f)] private float _keyLightIntensity = 2.5f;
  [SerializeField] private Color _rimLightColor = new Color(0.6f, 0.8f, 1f);
  [SerializeField, Min(0f)] private float _rimLightIntensity = 2f;

  [Header("Al recibir daño")]
  [SerializeField] private Color _hurtColor = new Color(1f, 0.35f, 0.35f);
  [SerializeField, Min(0.01f)] private float _hurtDuration = 0.35f;
  [SerializeField] private float _hurtShake = 10f;

  private RawImage _image;
  private PlayerHealthHUD _hud;
  private Transform _stage;
  private Transform _cloneRoot;
  private Camera _camera;
  private RenderTexture _rt;
  private Vector2 _basePosition;
  private Coroutine _hurt;

  private Animator _cloneAnimator;
  private float _extraYaw;
  private Transform[] _sourceBones;
  private Transform[] _cloneBones;
  private float _modelHeight = 1.7f;
  private float _modelBottom;

  private void Awake()
  {
    _image = GetComponent<RawImage>();
    _image.color = Color.white;
    _image.enabled = false;
    _basePosition = _image.rectTransform.anchoredPosition;
    _hud = GetComponentInParent<PlayerHealthHUD>();
  }

  private void OnEnable()
  {
    if (_hud != null)
    {
      _hud.Damaged += OnDamaged;
    }
  }

  private void OnDisable()
  {
    if (_camera != null)
    {
      _camera.enabled = false;
    }

    if (_hud != null)
    {
      _hud.Damaged -= OnDamaged;
    }
  }

  private IEnumerator Start()
  {
    // Un frame para que el Canvas calcule el tamaño de la RawImage.
    yield return null;

    if (_source == null)
    {
      var player = GameObject.FindWithTag("Player");
      var animator = player != null ? player.GetComponentInChildren<Animator>() : null;
      _source = animator != null ? animator.transform : player != null ? player.transform : null;
    }

    if (_source == null)
    {
      Debug.LogWarning("[PlayerPortrait] No encontre el modelo del jugador.", this);
      yield break;
    }

    BuildStage();
    BuildClone();
    _camera.enabled = IsShown();
    CreateTexture();
    PlaceCamera();
    _image.enabled = true;
  }

  private void OnDestroy()
  {
    if (_stage != null)
    {
      Destroy(_stage.gameObject);
    }

    if (_rt != null)
    {
      _rt.Release();
      Destroy(_rt);
    }
  }

  private bool IsShown()
  {
    var canvas = _image.canvas;
    return isActiveAndEnabled && (canvas == null || canvas.rootCanvas.enabled);
  }

  private void LateUpdate()
  {
    if (_camera != null)
    {
      // No renderizar si nadie lo ve (menu cerrado, pantalla oculta).
      var shown = IsShown();

      if (_camera.enabled != shown)
      {
        _camera.enabled = shown;
      }
    }

    if (_animationMode == AnimationMode.AnimacionPropia)
    {
      HandleRotate();
      SyncActiveOnly();
      return;
    }

    if (_cloneBones == null)
    {
      return;
    }

    if (_source == null)
    {
      // El jugador murio o se destruyo: el retrato se queda congelado.
      return;
    }

    // Copia la pose del jugador (despues de su Animator).
    for (var i = 1; i < _sourceBones.Length; i++)
    {
      var s = _sourceBones[i];
      var c = _cloneBones[i];

      if (s == null || c == null)
      {
        continue;
      }

      c.localPosition = s.localPosition;
      c.localRotation = s.localRotation;
      c.localScale = s.localScale;

      if (c.gameObject.activeSelf != s.gameObject.activeSelf)
      {
        c.gameObject.SetActive(s.gameObject.activeSelf);
      }
    }

    // La resolucion sigue al tamaño en pantalla.
    if (_rt != null && NeedsResize())
    {
      CreateTexture();
    }
  }

  // ---------- Montaje ----------

  private void BuildStage()
  {
    // Cada retrato en su propio sitio: si no, unas camaras verian los modelos de otras.
    _stage = new GameObject("PortraitStage").transform;
    _stage.position = _stagePosition + Vector3.right * (60f * s_stageCount++);

    var camGo = new GameObject("PortraitCamera");
    camGo.transform.SetParent(_stage, false);
    _camera = camGo.AddComponent<Camera>();
    _camera.clearFlags = CameraClearFlags.SolidColor;
    _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
    _camera.fieldOfView = _fov;
    _camera.nearClipPlane = 0.05f;
    _camera.farClipPlane = 30f;
    _camera.allowHDR = false;

    var data = _camera.GetUniversalAdditionalCameraData();
    data.renderPostProcessing = false;
    data.renderShadows = false;
    data.antialiasing = AntialiasingMode.None;

    CreateLight("KeyLight", new Vector3(-1.2f, 1.8f, 1.6f), _keyLightColor, _keyLightIntensity);
    CreateLight("RimLight", new Vector3(1.5f, 1.4f, -1.4f), _rimLightColor, _rimLightIntensity);
  }

  private void CreateLight(string lightName, Vector3 localPosition, Color color, float intensity)
  {
    var go = new GameObject(lightName);
    go.transform.SetParent(_stage, false);
    go.transform.localPosition = localPosition;

    var light = go.AddComponent<Light>();
    light.type = LightType.Point;
    light.color = color;
    light.intensity = intensity;
    light.range = 8f;
    light.shadows = LightShadows.None;
    light.renderMode = LightRenderMode.ForcePixel;
  }

  private void BuildClone()
  {
    // Bajo un padre inactivo: la copia no ejecuta los scripts del jugador.
    var holder = new GameObject("PortraitModel");
    holder.SetActive(false);
    holder.transform.SetParent(_stage, false);

    var clone = Instantiate(_source.gameObject, holder.transform);
    clone.transform.localPosition = Vector3.zero;
    clone.transform.localRotation = Quaternion.identity;
    clone.transform.localScale = _source.lossyScale;

    // Mismo orden de recorrido en ambos: la lista queda emparejada hueso a hueso.
    _sourceBones = _source.GetComponentsInChildren<Transform>(true);
    _cloneBones = clone.GetComponentsInChildren<Transform>(true);

    if (_sourceBones.Length != _cloneBones.Length)
    {
      Debug.LogWarning("[PlayerPortrait] La copia no coincide con el modelo; el retrato no se animara.", this);
      _sourceBones = _cloneBones = null;
    }

    Animator keptAnimator = null;

    if (_animationMode == AnimationMode.AnimacionPropia)
    {
      // Las listas solo se usan para copiar que partes estan visibles.
      keptAnimator = clone.GetComponent<Animator>();
    }

    StripToVisuals(clone, keptAnimator);
    holder.SetActive(true);

    if (keptAnimator != null)
    {
      _cloneAnimator = keptAnimator;
      _cloneAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
      _cloneAnimator.applyRootMotion = false;
      _cloneAnimator.fireEvents = false;
      _cloneAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
      _cloneAnimator.Rebind();
      _cloneAnimator.Update(0f);
    }
    _cloneRoot = holder.transform;
    _cloneRoot.localRotation = Quaternion.Euler(0f, 180f + _yaw, 0f);

    // Altura del modelo en reposo para encuadrar.
    var renderers = clone.GetComponentsInChildren<Renderer>();

    if (renderers.Length > 0)
    {
      var b = renderers[0].bounds;

      foreach (var r in renderers)
      {
        b.Encapsulate(r.bounds);
      }

      _modelBottom = b.min.y - _stage.position.y;
      _modelHeight = Mathf.Max(0.1f, b.size.y);
    }
  }

  private void PlaceCamera()
  {
    var focus = new Vector3(0f, _modelBottom + _modelHeight * _focusHeight, 0f);
    var halfFov = _fov * 0.5f * Mathf.Deg2Rad;
    var distance = (_modelHeight * _framing) / Mathf.Tan(halfFov);
    var rotation = Quaternion.Euler(_pitch, 0f, 0f);

    _camera.fieldOfView = _fov;
    _camera.transform.localRotation = rotation;
    _camera.transform.localPosition = focus - rotation * Vector3.forward * distance;
  }

  private bool NeedsResize()
  {
    var size = TargetSize();
    return size.x != _rt.width || size.y != _rt.height;
  }

  private Vector2Int TargetSize()
  {
    var rect = _image.rectTransform.rect;
    var canvas = _image.canvas != null ? _image.canvas.rootCanvas : null;
    var scale = (canvas != null ? canvas.scaleFactor : 1f) * _resolutionScale;
    return new Vector2Int(
      Mathf.Clamp(Mathf.RoundToInt(rect.width * scale), 32, 2048),
      Mathf.Clamp(Mathf.RoundToInt(rect.height * scale), 32, 2048));
  }

  private void CreateTexture()
  {
    var size = TargetSize();

    if (_rt != null)
    {
      _camera.targetTexture = null;
      _rt.Release();
      Destroy(_rt);
    }

    _rt = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32)
    {
      name = "PlayerPortrait",
      antiAliasing = 4
    };
    _rt.Create();

    _camera.targetTexture = _rt;
    _image.texture = _rt;
  }

  /// <summary>Deja solo transforms y mallas: sin scripts, fisica, audio ni Animator.</summary>
  private static void StripToVisuals(GameObject root, Animator keep)
  {
    DestroyAllReversed<MonoBehaviour>(root);
    DestroyAllReversed<Joint>(root);
    DestroyAllReversed<ParticleSystemRenderer>(root);
    DestroyAllReversed<ParticleSystem>(root);
    DestroyAllReversed<Collider>(root);
    DestroyAllReversed<Rigidbody>(root);

    var rest = root.GetComponentsInChildren<Component>(true);

    for (var i = rest.Length - 1; i >= 0; i--)
    {
      var c = rest[i];

      if (c == null || c == keep || c is Transform || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer)
      {
        continue;
      }

      DestroyImmediate(c);
    }

    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
    {
      r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

      // Sin animator, la malla podria salir del volumen de culling al moverse.
      if (r is SkinnedMeshRenderer skinned)
      {
        skinned.updateWhenOffscreen = true;
      }
    }
  }

  private static void DestroyAllReversed<T>(GameObject root) where T : Component
  {
    var list = root.GetComponentsInChildren<T>(true);

    for (var i = list.Length - 1; i >= 0; i--)
    {
      if (list[i] != null)
      {
        DestroyImmediate(list[i]);
      }
    }
  }

  // ---------- Reaccion al daño ----------

  private void OnDamaged(int amount)
  {
    if (_hurt != null)
    {
      StopCoroutine(_hurt);
    }

    _hurt = StartCoroutine(HurtRoutine());
  }

  private IEnumerator HurtRoutine()
  {
    var rect = _image.rectTransform;
    var t = 0f;

    while (t < _hurtDuration)
    {
      t += Time.unscaledDeltaTime;
      var k = 1f - Mathf.Clamp01(t / _hurtDuration);
      _image.color = Color.Lerp(Color.white, _hurtColor, k);
      rect.anchoredPosition = _basePosition + Random.insideUnitCircle * (_hurtShake * k);
      yield return null;
    }

    _image.color = Color.white;
    rect.anchoredPosition = _basePosition;
    _hurt = null;
  }

  /// <summary>Solo copia que objetos estan activos (espada / escudo equipados).</summary>
  private void SyncActiveOnly()
  {
    if (_sourceBones == null || _source == null)
    {
      return;
    }

    for (var i = 1; i < _sourceBones.Length; i++)
    {
      var s = _sourceBones[i];
      var c = _cloneBones[i];

      if (s != null && c != null && c.gameObject.activeSelf != s.gameObject.activeSelf)
      {
        c.gameObject.SetActive(s.gameObject.activeSelf);
      }
    }
  }

  private void HandleRotate()
  {
    if (!_allowRotate || _cloneRoot == null)
    {
      return;
    }

    var delta = 0f;
    var mouse = UnityEngine.InputSystem.Mouse.current;

    if (mouse != null && mouse.leftButton.isPressed &&
        RectTransformUtility.RectangleContainsScreenPoint(_image.rectTransform, mouse.position.ReadValue(), null))
    {
      delta -= mouse.delta.ReadValue().x * 0.4f;
    }

    var pad = UnityEngine.InputSystem.Gamepad.current;

    if (pad != null)
    {
      var x = pad.rightStick.ReadValue().x;

      if (Mathf.Abs(x) > 0.2f)
      {
        delta -= x * 180f * Time.unscaledDeltaTime;
      }
    }

    if (delta != 0f)
    {
      _extraYaw += delta;
      _cloneRoot.localRotation = Quaternion.Euler(0f, 180f + _yaw + _extraYaw, 0f);
    }
  }

  [ContextMenu("Reencuadrar (aplica cambios de encuadre en juego)")]
  private void Reframe()
  {
    if (_camera != null)
    {
      if (_cloneRoot != null)
      {
        _cloneRoot.localRotation = Quaternion.Euler(0f, 180f + _yaw + _extraYaw, 0f);
      }

      PlaceCamera();
    }
  }

  private void OnValidate()
  {
    if (Application.isPlaying)
    {
      Reframe();
    }
  }
}
