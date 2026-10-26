// FloatingPickup.cs
// Hace que un objeto recogible tenga presencia: al aparecer (por ejemplo al
// romper una caja) sale disparado hacia arriba con un pequeño rebote, se
// queda flotando sobre el suelo, gira despacio y brilla (luz que late,
// brillo en el propio modelo, halo y chispas).
//
// Sirve para cualquier objeto: la espada y el escudo (junto a Collectible),
// una hoja que cura (junto a HealthPickup), monedas, llaves...
// Ponlo en la raiz del prefab que va en "Contained Pickup" de la caja.
//
// Todo es configurable en el Inspector. En la inspeccion de coleccionables el
// modelo aparece limpio (sin luz, halo ni chispas).

using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class FloatingPickup : MonoBehaviour
{
  [Header("Aparicion")]
  [SerializeField, Min(0f), Tooltip("Duracion del salto al aparecer (0 = aparece directamente flotando).")]
  private float _popDuration = 0.7f;
  [SerializeField, Min(0f), Tooltip("Altura extra del salto antes de asentarse.")]
  private float _popOvershoot = 0.5f;
  [SerializeField, Tooltip("Crece desde cero al aparecer.")]
  private bool _growOnSpawn = true;

  [Header("Flotar")]
  [SerializeField, Min(0f), Tooltip("Altura sobre el suelo a la que flota.")]
  private float _hoverHeight = 0.9f;
  [SerializeField, Min(0f), Tooltip("Cuanto sube y baja.")]
  private float _bobAmplitude = 0.12f;
  [SerializeField, Min(0f)] private float _bobSpeed = 2f;
  [SerializeField, Tooltip("Grados por segundo alrededor del eje vertical (0 = no gira).")]
  private float _spinSpeed = 60f;
  [SerializeField, Tooltip("Inclinacion fija del modelo mientras flota (ej. espada en diagonal).")]
  private Vector3 _tilt = new Vector3(0f, 0f, 0f);
  [SerializeField, Tooltip("Capas que cuentan como suelo.")]
  private LayerMask _groundMask = ~((1 << 2) | (1 << 3) | (1 << 5) | (1 << 8) | (1 << 10));

  [Header("Brillo")]
  [SerializeField, ColorUsage(false, true)] private Color _glowColor = new Color(1f, 0.85f, 0.45f, 1f);
  [SerializeField, Min(0f), Tooltip("Intensidad de la luz (0 = sin luz).")]
  private float _lightIntensity = 2.5f;
  [SerializeField, Min(0.1f), Tooltip("Alcance de la luz (se multiplica por el tamaño del objeto).")]
  private float _lightRange = 3f;
  [SerializeField, Min(0f), Tooltip("Brillo propio del modelo (emision). 0 = no tocar los materiales.")]
  private float _modelGlow = 0.6f;
  [SerializeField, Min(0f), Tooltip("Velocidad del latido.")]
  private float _pulseSpeed = 2.5f;
  [SerializeField, Range(0f, 1f), Tooltip("Cuanto varia el brillo en cada latido.")]
  private float _pulseAmount = 0.35f;

  [Header("Halo y chispas")]
  [SerializeField] private bool _halo = true;
  [SerializeField, Min(0.1f)] private float _haloSize = 2.2f;
  [SerializeField, Range(0f, 1f)] private float _haloOpacity = 0.35f;
  [SerializeField] private bool _sparkles = true;
  [SerializeField, Min(0f), Tooltip("Chispas por segundo.")]
  private float _sparkleRate = 10f;
  [SerializeField, Tooltip("Material aditivo para halo y chispas. Vacio = se crea uno (URP Particles/Unlit).")]
  private Material _effectMaterial;

  private static readonly int s_EmissionId = Shader.PropertyToID("_EmissionColor");
  private static Texture2D s_softCircle;
  private static Material s_defaultMaterial;

  private Vector3 _spawnPosition;
  private float _restY;
  private float _time;
  private float _size = 0.5f;
  private Vector3 _baseScale;
  private float _yaw;
  private Light _light;
  private Transform _effects;
  private ParticleSystem _haloSystem;
  private readonly List<Renderer> _renderers = new List<Renderer>();
  private MaterialPropertyBlock _block;
  private bool _hidden;

  /// <summary>True mientras sale de la caja (aun no se puede recoger bien).</summary>
  public bool IsPopping => _time < _popDuration;

  /// <summary>Altura a la que flota (otros scripts, como HealthPickup, la usan).</summary>
  public float RestHeight => _restY;

  /// <summary>Tamaño aproximado (radio) del modelo.</summary>
  public float Size => _size;

  private void Start()
  {
    _spawnPosition = transform.position;
    _baseScale = transform.localScale;
    _yaw = transform.eulerAngles.y;

    foreach (var rb in GetComponentsInChildren<Rigidbody>())
    {
      rb.isKinematic = true;
      rb.useGravity = false;
    }

    MeasureSize();
    _restY = GroundHeight() + _hoverHeight + _size * 0.5f;

    SetupModelGlow();
    CreateEffects();

    if (_popDuration <= 0f)
    {
      _time = 0f;
      transform.position = new Vector3(_spawnPosition.x, _restY, _spawnPosition.z);
    }
    else if (_growOnSpawn)
    {
      transform.localScale = _baseScale * 0.01f;
    }
  }

  private void OnDestroy()
  {
    if (_effects != null)
    {
      Destroy(_effects.gameObject);
    }
  }

  private void OnDisable()
  {
    if (_effects != null)
    {
      _effects.gameObject.SetActive(false);
    }
  }

  private void OnEnable()
  {
    if (_effects != null && !_hidden)
    {
      _effects.gameObject.SetActive(true);
    }
  }

  /// <summary>Oculta o muestra luz, halo y chispas (por ejemplo al recogerlo).</summary>
  public void SetEffectsVisible(bool visible)
  {
    _hidden = !visible;

    if (_effects != null)
    {
      _effects.gameObject.SetActive(visible && isActiveAndEnabled);
    }
  }

  /// <summary>
  /// Si es true, otro script mueve el objeto (por ejemplo HealthPickup al
  /// atraerse al jugador): deja de flotar pero sigue brillando.
  /// </summary>
  public bool ExternallyMoved { get; set; }

  private void Update()
  {
    UpdateGlow();

    if (ExternallyMoved)
    {
      return;
    }

    _time += Time.deltaTime;
    var position = transform.position;

    if (_popDuration > 0f && _time < _popDuration)
    {
      var t = Mathf.Clamp01(_time / _popDuration);
      // Sube rapido, se pasa un poco y baja a su sitio.
      var arc = Mathf.Sin(t * Mathf.PI) * _popOvershoot;
      var ease = 1f - Mathf.Pow(1f - t, 3f);
      position.y = Mathf.Lerp(_spawnPosition.y, _restY, ease) + arc;

      if (_growOnSpawn)
      {
        var s = EaseOutBack(Mathf.Clamp01(t * 1.6f));
        transform.localScale = _baseScale * Mathf.Max(0.01f, s);
      }
    }
    else
    {
      if (_growOnSpawn && transform.localScale != _baseScale)
      {
        transform.localScale = _baseScale;
      }

      var bobTime = _time - _popDuration;
      position.y = _restY + Mathf.Sin(bobTime * _bobSpeed) * _bobAmplitude;
    }

    transform.position = position;
    _yaw += _spinSpeed * Time.deltaTime;
    transform.rotation = Quaternion.Euler(0f, _yaw, 0f) * Quaternion.Euler(_tilt);
  }

  private void LateUpdate()
  {
    if (_effects != null)
    {
      _effects.position = transform.position;
    }
  }

  private static float EaseOutBack(float t)
  {
    const float c1 = 1.70158f;
    const float c3 = c1 + 1f;
    return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
  }

  // ---------- Medidas ----------

  private void MeasureSize()
  {
    var found = false;
    var bounds = new Bounds(transform.position, Vector3.zero);

    foreach (var r in GetComponentsInChildren<Renderer>())
    {
      if (r is ParticleSystemRenderer)
      {
        continue;
      }

      _renderers.Add(r);
      if (found) bounds.Encapsulate(r.bounds); else bounds = r.bounds;
      found = true;
    }

    if (found)
    {
      _size = Mathf.Max(0.15f, bounds.extents.magnitude);
    }
  }

  private float GroundHeight()
  {
    var origin = _spawnPosition + Vector3.up * 1.5f;
    var hits = Physics.RaycastAll(origin, Vector3.down, 30f, _groundMask, QueryTriggerInteraction.Ignore);
    var best = float.MaxValue;
    var ground = _spawnPosition.y - 0.25f;

    foreach (var hit in hits)
    {
      if (hit.collider.transform.IsChildOf(transform)
          || hit.collider.attachedRigidbody != null && !hit.collider.attachedRigidbody.isKinematic
          || hit.collider.GetComponentInParent<NuiGrab.Breakable>() != null)
      {
        continue; // el propio objeto o los pedazos de la caja
      }

      if (hit.distance < best)
      {
        best = hit.distance;
        ground = hit.point.y;
      }
    }

    return ground;
  }

  // ---------- Brillo ----------

  private void SetupModelGlow()
  {
    if (_modelGlow <= 0f)
    {
      return;
    }

    _block = new MaterialPropertyBlock();

    foreach (var r in _renderers)
    {
      // Copias de los materiales (no se toca el asset) con emision activada y
      // en negro: el brillo se pone con MaterialPropertyBlock, asi la copia
      // que usa la inspeccion de coleccionables se ve normal.
      var materials = r.materials;

      foreach (var m in materials)
      {
        if (m != null && m.HasProperty(s_EmissionId))
        {
          m.EnableKeyword("_EMISSION");
          m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
          m.SetColor(s_EmissionId, Color.black);
        }
      }
    }
  }

  private float Pulse => 1f + Mathf.Sin(Time.time * _pulseSpeed) * _pulseAmount;

  private void UpdateGlow()
  {
    var pulse = Pulse;

    if (_light != null)
    {
      _light.intensity = _lightIntensity * pulse;
    }

    if (_block != null)
    {
      var color = _glowColor * (_modelGlow * pulse);

      foreach (var r in _renderers)
      {
        if (r == null) continue;
        r.GetPropertyBlock(_block);
        _block.SetColor(s_EmissionId, color);
        r.SetPropertyBlock(_block);
      }
    }
  }

  // ---------- Luz, halo y chispas ----------

  private void CreateEffects()
  {
    // Los efectos van en un objeto aparte que sigue al modelo, para que la
    // inspeccion de coleccionables no los copie.
    var go = new GameObject($"{name}_Brillo");
    _effects = go.transform;
    go.transform.position = transform.position;

    if (_lightIntensity > 0f)
    {
      var lightGo = new GameObject("Luz");
      lightGo.transform.SetParent(_effects, false);
      _light = lightGo.AddComponent<Light>();
      _light.type = LightType.Point;
      _light.color = _glowColor;
      _light.range = _lightRange * Mathf.Max(0.5f, _size);
      _light.intensity = _lightIntensity;
      _light.shadows = LightShadows.None;
    }

    var material = EffectMaterial();

    if (material == null)
    {
      return;
    }

    if (_halo)
    {
      _haloSystem = CreateSystem("Halo", material, ps =>
      {
        var main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = 1000f;
        main.startSpeed = 0f;
        main.startSize = _haloSize * _size;
        var haloColor = _glowColor;
        haloColor.a = _haloOpacity;
        main.startColor = haloColor;
        main.maxParticles = 1;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.playOnAwake = true;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
        var shape = ps.shape;
        shape.enabled = false;
      });
    }

    if (_sparkles && _sparkleRate > 0f)
    {
      CreateSystem("Chispas", material, ps =>
      {
        var main = ps.main;
        main.loop = true;
        main.duration = 2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f * _size, 0.14f * _size);
        main.startColor = new ParticleSystem.MinMaxGradient(_glowColor, Color.white);
        main.gravityModifier = -0.04f;
        main.maxParticles = 60;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;

        var emission = ps.emission;
        emission.rateOverTime = _sparkleRate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = _size * 0.9f;

        var color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
          new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));
      });
    }
  }

  private ParticleSystem CreateSystem(string systemName, Material material, System.Action<ParticleSystem> configure)
  {
    var go = new GameObject(systemName);
    go.SetActive(false);
    go.transform.SetParent(_effects, false);
    var ps = go.AddComponent<ParticleSystem>();
    var renderer = go.GetComponent<ParticleSystemRenderer>();
    renderer.sharedMaterial = material;
    renderer.renderMode = ParticleSystemRenderMode.Billboard;
    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    renderer.receiveShadows = false;
    configure(ps);
    go.SetActive(true);
    ps.Play();
    return ps;
  }

  private Material EffectMaterial()
  {
    if (_effectMaterial != null)
    {
      return _effectMaterial;
    }

    if (s_defaultMaterial != null)
    {
      return s_defaultMaterial;
    }

    var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

    if (shader == null)
    {
      return null;
    }

    s_defaultMaterial = new Material(shader) { name = "Pickup_Glow" };
    NuiGrab.TelekinesisVFX.ConfigureAdditive(s_defaultMaterial);
    s_defaultMaterial.SetTexture("_BaseMap", SoftCircle());
    s_defaultMaterial.SetColor("_BaseColor", Color.white);
    return s_defaultMaterial;
  }

  private static Texture2D SoftCircle()
  {
    if (s_softCircle != null)
    {
      return s_softCircle;
    }

    const int size = 64;
    s_softCircle = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Pickup_SoftCircle" };
    var pixels = new Color32[size * size];

    for (var y = 0; y < size; y++)
    {
      for (var x = 0; x < size; x++)
      {
        var dx = (x + 0.5f) / size * 2f - 1f;
        var dy = (y + 0.5f) / size * 2f - 1f;
        var d = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
        var a = (byte)(d * d * 255f);
        pixels[y * size + x] = new Color32(255, 255, 255, a);
      }
    }

    s_softCircle.SetPixels32(pixels);
    s_softCircle.Apply();
    return s_softCircle;
  }
}
