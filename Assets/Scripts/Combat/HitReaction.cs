// HitReaction.cs
// Reaccion de un personaje al recibir un golpe: empuje hacia atras con peso,
// aturdimiento breve, destello de color, "squash" de escala, chispas, sonido,
// temblor de camara y hit-stop. Todo se ajusta desde el inspector y cada efecto
// se puede apagar por separado.
//
// Lo invoca MeleeHitbox al conectar (React). Enemy consulta IsStunned para
// pausar su IA mientras dura el empuje.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class HitReaction : MonoBehaviour
{
  // ---------------------------------------------------------------- Empuje
  [Header("Empuje (knockback)")]
  [SerializeField] private bool _knockbackEnabled = true;

  [Tooltip("Distancia en metros que retrocede el personaje con cada golpe.")]
  [SerializeField, Min(0f)] private float _knockbackDistance = 1.2f;

  [Tooltip("Segundos que tarda en recorrer esa distancia.")]
  [SerializeField, Min(0.01f)] private float _knockbackDuration = 0.22f;

  [Tooltip("Forma del empuje: X = tiempo (0..1), Y = fraccion de distancia recorrida (0..1). " +
           "Una curva que sube rapido y se aplana da sensacion de golpe seco con frenado pesado.")]
  [SerializeField] private AnimationCurve _knockbackCurve = new AnimationCurve(
      new Keyframe(0f, 0f, 0f, 3.5f),
      new Keyframe(1f, 1f, 0f, 0f));

  [Tooltip("Pequeno salto vertical en m/s al recibir el golpe (0 = pegado al suelo).")]
  [SerializeField, Min(0f)] private float _knockbackLift = 0f;

  [Tooltip("0 = inamovible, 1 = normal, >1 = sale volando. Util para enemigos pesados o ligeros.")]
  [SerializeField, Min(0f)] private float _knockbackMultiplier = 1f;

  [Tooltip("Tiempo total (incluido el empuje) en que el personaje no actua tras el golpe.")]
  [SerializeField, Min(0f)] private float _stunDuration = 0.35f;

  [Tooltip("Gira al personaje para que encare a quien le pego.")]
  [SerializeField] private bool _faceAttacker = false;

  // ---------------------------------------------------------------- Camara
  [Header("Temblor de camara")]
  [SerializeField] private bool _cameraShakeEnabled = true;

  [Tooltip("Trauma que se suma a la camara (0..1). El temblor visible es trauma^2, " +
           "asi que golpes pequenos casi no se notan y los grandes si.")]
  [SerializeField, Range(0f, 1f)] private float _cameraShake = 0.45f;

  // ---------------------------------------------------------------- Hit-stop
  [Header("Hit-stop (congelado)")]
  [SerializeField] private bool _hitStopEnabled = true;

  [Tooltip("Segundos reales que se congela el juego al conectar. 0.04-0.08 se siente bien.")]
  [SerializeField, Range(0f, 0.3f)] private float _hitStopDuration = 0.06f;

  [Tooltip("Velocidad del tiempo durante el congelado (0 = quieto del todo).")]
  [SerializeField, Range(0f, 1f)] private float _hitStopTimeScale = 0.05f;

  // ---------------------------------------------------------------- Destello
  [Header("Destello")]
  [SerializeField] private bool _flashEnabled = true;
  [SerializeField, ColorUsage(false, true)] private Color _flashColor = new Color(1f, 1f, 1f, 1f);

  [Tooltip("Intensidad de la emision del destello (necesita un shader con _EmissionColor, p. ej. URP/Lit).")]
  [SerializeField, Min(0f)] private float _flashIntensity = 2.5f;

  [Tooltip("Cuanto se tine el color base hacia el color del destello (0..1).")]
  [SerializeField, Range(0f, 1f)] private float _flashBaseTint = 0.6f;

  [SerializeField, Min(0.01f)] private float _flashDuration = 0.14f;

  // ---------------------------------------------------------------- Squash
  [Header("Squash & stretch")]
  [SerializeField] private bool _squashEnabled = true;

  [Tooltip("Transform que se deforma. Vacio = este objeto. Cuando pongas la malla " +
           "definitiva conviene asignar aqui el hijo visual para no deformar el collider.")]
  [SerializeField] private Transform _visualRoot;

  [Tooltip("Cuanto se aplasta (0.2 = 20 %).")]
  [SerializeField, Range(0f, 0.6f)] private float _squashAmount = 0.18f;

  [SerializeField, Min(0.01f)] private float _squashDuration = 0.25f;

  // ---------------------------------------------------------------- VFX
  [Header("Particulas")]
  [SerializeField] private bool _vfxEnabled = true;

  [Tooltip("Prefab propio de impacto. Si se deja vacio se genera un estallido de chispas sencillo.")]
  [SerializeField] private GameObject _hitVfxPrefab;

  [SerializeField, Min(0.1f)] private float _hitVfxLifetime = 1.5f;

  [Tooltip("Solo para las chispas generadas: color, cantidad, velocidad y tamano.")]
  [SerializeField] private Color _sparkColor = new Color(1f, 0.85f, 0.45f, 1f);
  [SerializeField, Range(1, 80)] private int _sparkCount = 18;
  [SerializeField, Min(0f)] private float _sparkSpeed = 5f;
  [SerializeField, Min(0.005f)] private float _sparkSize = 0.08f;

  [Tooltip("Los golpes mas fuertes (estocada, remate) sueltan mas chispas, mas rapidas y grandes.")]
  [SerializeField] private bool _vfxScalesWithIntensity = true;

  [Header("Onda de golpe fuerte")]
  [Tooltip("A partir de esta contundencia (1 = golpe normal) aparece un anillo de polvo a ras de suelo.")]
  [SerializeField, Min(0f)] private float _shockwaveThreshold = 1.25f;
  [SerializeField] private Color _shockwaveColor = new Color(0.85f, 0.78f, 0.65f, 0.8f);
  [SerializeField, Range(4, 80)] private int _shockwaveCount = 28;
  [SerializeField, Min(0f)] private float _shockwaveSpeed = 4.5f;
  [SerializeField, Min(0.01f)] private float _shockwaveSize = 0.35f;

  // ---------------------------------------------------------------- Sonido
  [Header("Sonido")]
  [SerializeField] private AudioClip[] _hitClips;
  [SerializeField, Range(0f, 1f)] private float _hitVolume = 1f;

  // ---------------------------------------------------------------- Remate
  [Header("Golpe final")]
  [Tooltip("Multiplica temblor, hit-stop y empuje cuando el golpe mata al personaje.")]
  [SerializeField, Min(1f)] private float _killingBlowMultiplier = 1.6f;

  // ---------------------------------------------------------------- Estado
  /// <summary>True mientras el personaje esta aturdido por un golpe.</summary>
  public bool IsStunned => Time.time < _stunnedUntil;

  private Rigidbody _rb;
  private Health _health;
  private float _stunnedUntil;
  private Coroutine _knockbackRoutine;
  private Coroutine _flashRoutine;
  private Coroutine _squashRoutine;
  private Vector3 _visualBaseScale;

  private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
  private static readonly int ColorId = Shader.PropertyToID("_Color");
  private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

  private struct FlashTarget
  {
    public Material Material;
    public int ColorProperty;   // -1 si no tiene color base
    public Color BaseColor;
    public bool HasEmission;
    public Color BaseEmission;
  }

  private readonly List<FlashTarget> _flashTargets = new List<FlashTarget>();

  private static Material _sparkMaterial;

  private void Awake()
  {
    _rb = GetComponent<Rigidbody>();
    _health = GetComponent<Health>();

    if (_visualRoot == null)
    {
      _visualRoot = transform;
    }

    _visualBaseScale = _visualRoot.localScale;
    CacheFlashMaterials();
  }

  private void OnDisable()
  {
    // Si el objeto se desactiva a mitad de un efecto, lo deja en estado limpio.
    if (_visualRoot != null)
    {
      _visualRoot.localScale = _visualBaseScale;
    }

    ApplyFlash(0f);
  }

  private void OnDestroy()
  {
    // Las instancias de material creadas en CacheFlashMaterials son nuestras.
    foreach (var target in _flashTargets)
    {
      if (target.Material != null)
      {
        Destroy(target.Material);
      }
    }

    _flashTargets.Clear();
  }

  // =====================================================================
  // API
  // =====================================================================

  /// <summary>
  /// Reproduce la reaccion al golpe.
  /// </summary>
  /// <param name="hitPoint">Punto de contacto en mundo.</param>
  /// <param name="hitDirection">Direccion del golpe (del atacante hacia este personaje).</param>
  /// <param name="attacker">Quien golpea (opcional, para encararlo).</param>
  /// <param name="intensity">Contundencia del golpe (1 = normal). La estocada del combo usa mas.</param>
  public void React(Vector3 hitPoint, Vector3 hitDirection, Transform attacker = null, float intensity = 1f)
  {
    var killing = _health != null && _health.CurrentHealth <= 0;
    var boost = (killing ? _killingBlowMultiplier : 1f) * Mathf.Max(0f, intensity);

    var flatDir = hitDirection;
    flatDir.y = 0f;
    if (flatDir.sqrMagnitude < 0.0001f)
    {
      flatDir = attacker != null ? transform.position - attacker.position : -transform.forward;
      flatDir.y = 0f;
    }
    flatDir = flatDir.sqrMagnitude > 0.0001f ? flatDir.normalized : -transform.forward;

    if (_faceAttacker)
    {
      transform.rotation = Quaternion.LookRotation(-flatDir, Vector3.up);
    }

    if (_hitStopEnabled)
    {
      HitStop.Play(_hitStopDuration * boost, _hitStopTimeScale);
    }

    if (_cameraShakeEnabled)
    {
      TopDownCamera.AddShake(_cameraShake * boost);
    }

    _stunnedUntil = Mathf.Max(_stunnedUntil, Time.time + Mathf.Max(_stunDuration, _knockbackEnabled ? _knockbackDuration : 0f));

    if (_knockbackEnabled && _rb != null && _knockbackMultiplier > 0f && isActiveAndEnabled)
    {
      if (_knockbackRoutine != null)
      {
        StopCoroutine(_knockbackRoutine);
      }

      _knockbackRoutine = StartCoroutine(KnockbackRoutine(flatDir, _knockbackDistance * _knockbackMultiplier * boost));
    }

    if (_flashEnabled && isActiveAndEnabled)
    {
      if (_flashRoutine != null)
      {
        StopCoroutine(_flashRoutine);
      }

      _flashRoutine = StartCoroutine(FlashRoutine());
    }

    if (_squashEnabled && isActiveAndEnabled)
    {
      if (_squashRoutine != null)
      {
        StopCoroutine(_squashRoutine);
      }

      _squashRoutine = StartCoroutine(SquashRoutine());
    }

    if (_vfxEnabled)
    {
      SpawnVfx(hitPoint, flatDir, _vfxScalesWithIntensity ? boost : 1f);

      if (_hitVfxPrefab == null && boost >= _shockwaveThreshold && _shockwaveThreshold > 0f)
      {
        var col = GetComponent<Collider>();
        var feetY = col != null ? col.bounds.min.y + 0.05f : transform.position.y;
        SpawnShockwave(new Vector3(transform.position.x, feetY, transform.position.z), boost);
      }
    }

    PlaySound(hitPoint);
  }

  // =====================================================================
  // Empuje
  // =====================================================================

  private IEnumerator KnockbackRoutine(Vector3 direction, float distance)
  {
    // Espera un frame fisico: si hay hit-stop, el empuje arranca justo al
    // "soltarse" el tiempo, que es lo que da la sensacion de impacto.
    yield return new WaitForFixedUpdate();

    if (_knockbackLift > 0f)
    {
      var v = _rb.linearVelocity;
      v.y = Mathf.Max(v.y, _knockbackLift);
      _rb.linearVelocity = v;
    }

    var elapsed = 0f;
    var travelled = 0f;
    var duration = Mathf.Max(_knockbackDuration, 0.01f);

    while (elapsed < duration)
    {
      elapsed += Time.fixedDeltaTime;
      var t = Mathf.Clamp01(elapsed / duration);
      var target = distance * Mathf.Clamp01(_knockbackCurve.Evaluate(t));
      var step = target - travelled;
      travelled = target;

      // Velocidad horizontal que recorre exactamente el tramo de este paso,
      // respetando colisiones (no atraviesa paredes como un MovePosition).
      var horizontal = direction * (step / Time.fixedDeltaTime);
      _rb.linearVelocity = new Vector3(horizontal.x, _rb.linearVelocity.y, horizontal.z);

      yield return new WaitForFixedUpdate();
    }

    // Frena en seco al terminar para que no patine.
    _rb.linearVelocity = new Vector3(0f, _rb.linearVelocity.y, 0f);
    _knockbackRoutine = null;
  }

  // =====================================================================
  // Destello
  // =====================================================================

  private void CacheFlashMaterials()
  {
    _flashTargets.Clear();

    foreach (var renderer in GetComponentsInChildren<Renderer>())
    {
      if (renderer is ParticleSystemRenderer)
      {
        continue;
      }

      // .materials crea instancias propias de este personaje: el destello no
      // afecta a otros objetos que compartan material.
      foreach (var material in renderer.materials)
      {
        var target = new FlashTarget { Material = material, ColorProperty = -1 };

        if (material.HasProperty(BaseColorId))
        {
          target.ColorProperty = BaseColorId;
        }
        else if (material.HasProperty(ColorId))
        {
          target.ColorProperty = ColorId;
        }

        if (target.ColorProperty != -1)
        {
          target.BaseColor = material.GetColor(target.ColorProperty);
        }

        if (material.HasProperty(EmissionColorId))
        {
          target.HasEmission = true;
          target.BaseEmission = material.IsKeywordEnabled("_EMISSION")
              ? material.GetColor(EmissionColorId)
              : Color.black;
          material.EnableKeyword("_EMISSION");
          material.SetColor(EmissionColorId, target.BaseEmission);
        }

        _flashTargets.Add(target);
      }
    }
  }

  private IEnumerator FlashRoutine()
  {
    var elapsed = 0f;
    var duration = Mathf.Max(_flashDuration, 0.01f);

    while (elapsed < duration)
    {
      // Tiempo real: el destello se ve incluso durante el hit-stop.
      elapsed += Time.unscaledDeltaTime;
      var k = 1f - Mathf.Clamp01(elapsed / duration);
      ApplyFlash(k * k);
      yield return null;
    }

    ApplyFlash(0f);
    _flashRoutine = null;
  }

  private void ApplyFlash(float amount)
  {
    foreach (var target in _flashTargets)
    {
      if (target.Material == null)
      {
        continue;
      }

      if (target.ColorProperty != -1)
      {
        var tinted = Color.Lerp(target.BaseColor, _flashColor, amount * _flashBaseTint);
        tinted.a = target.BaseColor.a;
        target.Material.SetColor(target.ColorProperty, tinted);
      }

      if (target.HasEmission)
      {
        target.Material.SetColor(EmissionColorId,
            target.BaseEmission + _flashColor * (_flashIntensity * amount));
      }
    }
  }

  // =====================================================================
  // Squash & stretch
  // =====================================================================

  private IEnumerator SquashRoutine()
  {
    var elapsed = 0f;
    var duration = Mathf.Max(_squashDuration, 0.01f);

    while (elapsed < duration)
    {
      elapsed += Time.unscaledDeltaTime;
      var t = Mathf.Clamp01(elapsed / duration);

      // Oscilacion amortiguada: se aplasta, rebota un poco estirado y se asienta.
      var wobble = Mathf.Cos(t * Mathf.PI * 2.5f) * (1f - t) * (1f - t);
      var s = _squashAmount * wobble;

      _visualRoot.localScale = Vector3.Scale(_visualBaseScale,
          new Vector3(1f + s * 0.5f, 1f - s, 1f + s * 0.5f));

      yield return null;
    }

    _visualRoot.localScale = _visualBaseScale;
    _squashRoutine = null;
  }

  // =====================================================================
  // Particulas
  // =====================================================================

  private void SpawnVfx(Vector3 point, Vector3 direction, float scale)
  {
    scale = Mathf.Max(0.1f, scale);
    var sizeScale = Mathf.Sqrt(scale);
    var rotation = Quaternion.LookRotation(direction, Vector3.up);

    if (_hitVfxPrefab != null)
    {
      var vfx = Instantiate(_hitVfxPrefab, point, rotation);
      Destroy(vfx, _hitVfxLifetime);
      return;
    }

    var material = SparkMaterial();
    if (material == null)
    {
      return;
    }

    var go = new GameObject("HitSparks");
    go.transform.SetPositionAndRotation(point, rotation);

    var ps = go.AddComponent<ParticleSystem>();
    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

    var main = ps.main;
    main.duration = 0.2f;
    main.loop = false;
    main.playOnAwake = false;
    main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
    main.startSpeed = new ParticleSystem.MinMaxCurve(_sparkSpeed * 0.5f * sizeScale, _sparkSpeed * sizeScale);
    main.startSize = new ParticleSystem.MinMaxCurve(_sparkSize * 0.5f * sizeScale, _sparkSize * sizeScale);
    main.startColor = _sparkColor;
    main.gravityModifier = 1.2f;
    main.simulationSpace = ParticleSystemSimulationSpace.World;
    var count = Mathf.Clamp(Mathf.RoundToInt(_sparkCount * scale), 1, 200);
    main.maxParticles = count;
    // Tiempo real: las chispas salen aunque el juego este en hit-stop.
    main.useUnscaledTime = true;

    var emission = ps.emission;
    emission.rateOverTime = 0f;
    emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

    var shape = ps.shape;
    shape.shapeType = ParticleSystemShapeType.Cone;
    shape.angle = 40f;
    shape.radius = 0.05f;

    var sizeOverLifetime = ps.sizeOverLifetime;
    sizeOverLifetime.enabled = true;
    sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

    var renderer = go.GetComponent<ParticleSystemRenderer>();
    renderer.sharedMaterial = material;
    renderer.renderMode = ParticleSystemRenderMode.Stretch;
    renderer.velocityScale = 0.04f;
    renderer.lengthScale = 1.5f;

    ps.Play();
    Destroy(go, _hitVfxLifetime);
  }

  /// <summary>
  /// Anillo de polvo que sale disparado a ras de suelo desde los pies del
  /// personaje: vende que el golpe tuvo fuerza de sobra.
  /// </summary>
  private void SpawnShockwave(Vector3 groundPoint, float intensity)
  {
    var material = SparkMaterial();
    if (material == null)
    {
      return;
    }

    var go = new GameObject("HitShockwave");
    go.transform.SetPositionAndRotation(groundPoint, Quaternion.Euler(-90f, 0f, 0f));

    var ps = go.AddComponent<ParticleSystem>();
    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

    var main = ps.main;
    main.duration = 0.1f;
    main.loop = false;
    main.playOnAwake = false;
    main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
    main.startSpeed = new ParticleSystem.MinMaxCurve(_shockwaveSpeed * 0.7f, _shockwaveSpeed * Mathf.Sqrt(intensity));
    main.startSize = new ParticleSystem.MinMaxCurve(_shockwaveSize * 0.6f, _shockwaveSize);
    main.startColor = _shockwaveColor;
    main.gravityModifier = -0.05f;
    main.simulationSpace = ParticleSystemSimulationSpace.World;
    main.maxParticles = _shockwaveCount;
    main.useUnscaledTime = true;

    var emission = ps.emission;
    emission.rateOverTime = 0f;
    emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)_shockwaveCount) });

    // Circulo en el plano del suelo, disparando hacia fuera.
    var shape = ps.shape;
    shape.shapeType = ParticleSystemShapeType.Circle;
    shape.radius = 0.25f;
    shape.radiusThickness = 0f;
    shape.arcMode = ParticleSystemShapeMultiModeValue.Loop;

    // Frena rapido: la onda "revienta" y se queda flotando.
    var limit = ps.limitVelocityOverLifetime;
    limit.enabled = true;
    limit.limit = 0.3f;
    limit.dampen = 0.2f;

    var sizeOverLifetime = ps.sizeOverLifetime;
    sizeOverLifetime.enabled = true;
    sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.4f));

    var colorOverLifetime = ps.colorOverLifetime;
    colorOverLifetime.enabled = true;
    var gradient = new Gradient();
    gradient.SetKeys(
        new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
    colorOverLifetime.color = gradient;

    go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;

    ps.Play();
    Destroy(go, 1.5f);
  }

  private static Material SparkMaterial()
  {
    if (_sparkMaterial != null)
    {
      return _sparkMaterial;
    }

    var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
        ?? Shader.Find("Sprites/Default");

    if (shader == null)
    {
      return null;
    }

    _sparkMaterial = new Material(shader) { name = "HitSparks (runtime)" };
    if (_sparkMaterial.HasProperty(BaseColorId))
    {
      _sparkMaterial.SetColor(BaseColorId, Color.white);
    }

    return _sparkMaterial;
  }

  // =====================================================================
  // Sonido
  // =====================================================================

  private void PlaySound(Vector3 point)
  {
    if (_hitClips == null || _hitClips.Length == 0)
    {
      return;
    }

    var clip = _hitClips[Random.Range(0, _hitClips.Length)];
    if (clip == null)
    {
      return;
    }

    if (AudioManager.Instance != null && Mathf.Approximately(_hitVolume, 1f))
    {
      AudioManager.Instance.playClip(clip);
    }
    else
    {
      AudioSource.PlayClipAtPoint(clip, point, _hitVolume);
    }
  }
}
