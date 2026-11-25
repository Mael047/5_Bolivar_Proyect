// BossFight.cs
// Pelea con el jefe. Va en el jefe (con Health y su Animator Jefe_Cuerpo).
//
//   - Al aparecer rompe la mesa (la asignada o la mas cercana).
//   - Esta escondido hasta que la nutria entra en la sala ("Arena": una caja
//     trigger). La sala se abre con la llave (puerta LockedDoor normal).
//   - Al entrar: se cierran las barreras (opcional), suena la musica de
//     combate y el jefe aparece (Aparecer).
//   - Ataques, en ciclo:
//       * Tentaculos: salen del suelo donde esta la nutria (con aviso),
//         golpean y se quedan cansados: ahi la espada les hace daño y ese
//         daño se lo pasan al jefe.
//       * Ataque cargado: onda alrededor del jefe; hay que alejarse.
//   - Con la mitad de vida entra en fase 2: mas tentaculos y mas rapido.
//   - Al llegar a 0: Derrota, se van los tentaculos, se abren las barreras,
//     salen las recompensas y queda guardado como vencido.
//   - Al jefe tambien se le puede pegar directamente con la espada.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public class BossFight : MonoBehaviour
{
  [Header("Sala")]
  [SerializeField, Tooltip("Caja (Box Collider) de la sala: la pelea empieza cuando la nutria entra.")]
  private BoxCollider _arena;
  [SerializeField, Tooltip("Se activan al empezar (cierran la sala) y se apagan al vencer.")]
  private GameObject[] _barriers;

  [Header("Al aparecer")]
  [SerializeField, Tooltip("Lo que se rompe cuando sale el jefe (la mesa). Vacio = la mesa mas cercana al jefe.")]
  private GameObject[] _breakOnAppear;
  [SerializeField, Min(0f)] private float _breakForce = 7f;

  [Header("Tentaculos")]
  [SerializeField] private GameObject _tentaclePrefab;
  [SerializeField, Min(0.1f), Tooltip("Tamaño de los tentaculos.")]
  private float _tentacleScale = 1.6f;
  [SerializeField, Min(0.5f), Tooltip("Hasta donde llega el golpe del tentaculo hacia la nutria.")]
  private float _tentacleReach = 2.8f;
  [SerializeField, Min(1)] private int _tentaclesPhase1 = 2;
  [SerializeField, Min(1)] private int _tentaclesPhase2 = 4;
  [SerializeField, Min(0f), Tooltip("Separacion entre tentaculos (el primero sale donde esta la nutria).")]
  private float _tentacleSpread = 3f;
  [SerializeField, Min(0f), Tooltip("Segundos entre la salida de un tentaculo y el siguiente.")]
  private float _tentacleStagger = 0.35f;
  [SerializeField, Min(0f), Tooltip("Segundos de aviso (circulos) antes de que salga el tentaculo.")]
  private float _tentacleWarning = 0.6f;
  [SerializeField, Min(0.5f), Tooltip("Velocidad de la animacion de golpe del tentaculo (1 = normal).")]
  private float _tentacleAttackSpeed = 1.8f;
  [SerializeField, Min(0f), Tooltip("Segundos antes del golpe en los que el tentaculo deja de apuntar a la nutria (margen para esquivar).")]
  private float _tentacleCommit = 0.18f;
  [SerializeField, Min(0f)] private float _tiredTime = 3f;
  [SerializeField, Min(0)] private int _tentacleDamage = 30;
  [SerializeField, Min(0f)] private float _tentacleHitRadius = 1.4f;
  [SerializeField, Min(0f), Tooltip("Multiplica el daño de la espada al tentaculo cansado que pasa al jefe.")]
  private float _tentacleDamageToBoss = 1f;

  [Header("Efectos")]
  [SerializeField, Tooltip("Material de las particulas (aditivo, con textura suave). Lo asigna la herramienta.")]
  private Material _fxMaterial;
  [SerializeField] private Color _energyColor = new Color(0.75f, 0.25f, 1f, 1f);
  [SerializeField, Tooltip("Color del destello al recibir un golpe (HDR: mas de 1 = mas brillante).")]
  private Color _hitFlashColor = new Color(2.5f, 2.2f, 2.8f, 1f);
  [SerializeField, Min(0.02f)] private float _hitFlashTime = 0.18f;
  [SerializeField, Range(0f, 0.3f), Tooltip("Cuanto se 'hincha' al recibir un golpe.")]
  private float _hitPunch = 0.06f;

  [Header("Ataque cargado")]
  [SerializeField, Min(0), Tooltip("Cada cuantas oleadas de tentaculos hace el ataque cargado (0 = nunca).")]
  private int _chargeEvery = 2;
  [SerializeField, Min(0)] private int _chargeDamage = 40;
  [SerializeField, Min(0f)] private float _chargeRadius = 5f;
  [SerializeField, Range(0f, 1f), Tooltip("Momento del golpe dentro de la animacion.")]
  private float _chargeHitMoment = 0.6f;
  [SerializeField, Min(0f)] private float _chargeKnockback = 12f;

  [Header("Ritmo")]
  [SerializeField, Min(0f)] private float _restTime = 2f;
  [SerializeField, Range(0.1f, 1f), Tooltip("Vida (fraccion) a la que entra en fase 2.")]
  private float _phase2At = 0.5f;
  [SerializeField, Min(1f), Tooltip("Velocidad de todo en fase 2.")]
  private float _phase2Speed = 1.3f;
  [SerializeField, Min(0f), Tooltip("Gira hacia la nutria (grados por segundo).")]
  private float _turnSpeed = 120f;

  [Header("Muerte (desvanecido de polvo)")]
  [SerializeField, Tooltip("Material con el shader ReQuinta/JefeDisolver (lo asigna la herramienta).")]
  private Material _dissolveMaterial;
  [SerializeField, Min(0.1f)] private float _dissolveTime = 2.5f;
  [SerializeField, Range(0f, 1f), Tooltip("En que momento de la animacion de Derrota empieza a desvanecerse.")]
  private float _dissolveStart = 0.6f;
  [SerializeField] private Color _dustColor = new Color(0.8f, 0.6f, 1f, 0.8f);

  [Header("Al vencer")]
  [SerializeField] private GameObject[] _rewards;
  [SerializeField] private AudioClip _appearSound;
  [SerializeField] private AudioClip _defeatSound;
  [SerializeField, Range(0f, 1f)] private float _volume = 1f;

  [Header("Eventos")]
  public UnityEvent OnFightStart = new UnityEvent();
  public UnityEvent OnDefeated = new UnityEvent();

  /// <summary>Lo usa el guardado.</summary>
  public event System.Action<BossFight> Defeated;

  public bool IsFighting { get; private set; }
  public bool IsDefeated { get; private set; }
  public Health Health => _health;

  private Health _health;
  private Animator _animator;
  private Renderer[] _renderers;
  private Collider[] _colliders;
  private Transform _player;
  private readonly List<BossTentacle> _tentacles = new List<BossTentacle>();
  private bool _phase2;
  private float _nextHurtAnim;
  private static readonly Dictionary<Health, float> s_nextHit = new Dictionary<Health, float>();

  private float Speed => _phase2 ? _phase2Speed : 1f;
  private Vector3 _baseScale;
  private Coroutine _punch;

  private void Awake()
  {
    _health = GetComponent<Health>();
    _health.DestroyOnDeath = false;
    _health.Died += OnDied;
    _health.Changed += OnHealthChanged;
    _animator = GetComponentInChildren<Animator>();
    _renderers = GetComponentsInChildren<Renderer>(true);
    _colliders = GetComponentsInChildren<Collider>(true);
    MakeLockable(gameObject);
    _baseScale = transform.localScale;

    SetVisible(false);
    SetBarriers(false);
  }

  private void Update()
  {
    if (IsDefeated)
    {
      return;
    }

    if (_player == null)
    {
      var p = FindAnyObjectByType<Player>();
      _player = p != null ? p.transform : null;
      if (_player == null) return;
    }

    if (!IsFighting)
    {
      if (_arena != null && InsideArena(_player.position))
      {
        StartCoroutine(Fight());
      }

      return;
    }

    // Mirar a la nutria
    var dir = _player.position - transform.position;
    dir.y = 0f;

    if (dir.sqrMagnitude > 0.01f && _turnSpeed > 0f)
    {
      transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), _turnSpeed * Time.deltaTime);
    }
  }

  private bool InsideArena(Vector3 point)
  {
    return (_arena.ClosestPoint(point) - point).sqrMagnitude < 0.0001f;
  }

  // ---------- Pelea ----------

  private IEnumerator Fight()
  {
    IsFighting = true;
    SetBarriers(true);
    OnFightStart.Invoke();
    if (AudioManager.Instance != null) AudioManager.Instance.SetCombatState(true);

    SetVisible(true);
    Play(_appearSound);
    BreakTables();
    BossFx.Burst(transform.position, _energyColor, 40, 6f, 0.6f, 1.2f, _fxMaterial, true, 0f);
    TopDownCamera.AddShake(0.6f);
    Trigger("Aparecer");
    yield return new WaitForSeconds(ClipLength("Jefe_Aparecer", 2f));

    var waves = 0;

    while (!IsDefeated)
    {
      // Oleada de tentaculos
      yield return TentacleWave();
      waves++;
      if (IsDefeated) break;
      yield return new WaitForSeconds(_restTime / Speed);

      // Ataque cargado
      if (_chargeEvery > 0 && waves % _chargeEvery == 0 && !IsDefeated)
      {
        yield return ChargeAttack();
        if (IsDefeated) break;
        yield return new WaitForSeconds(_restTime / Speed);
      }
    }
  }

  private IEnumerator TentacleWave()
  {
    if (_tentaclePrefab == null || _player == null)
    {
      yield return new WaitForSeconds(1f);
      yield break;
    }

    var count = _phase2 ? _tentaclesPhase2 : _tentaclesPhase1;

    for (var i = 0; i < count && !IsDefeated; i++)
    {
      // Salen a un lado de la nutria y golpean hacia ella. El primero, entre
      // la nutria y el jefe; los demas alrededor.
      Vector3 dir;

      if (i == 0)
      {
        dir = transform.position - _player.position;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
      }
      else
      {
        var angle = Random.Range(0f, Mathf.PI * 2f);
        dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
      }

      var distance = i == 0 ? _tentacleReach * 0.7f : Mathf.Max(_tentacleReach * 0.7f, _tentacleSpread * Random.Range(0.7f, 1.1f));
      var pos = _player.position + dir * distance;

      pos = KeepInArena(pos);
      SpawnTentacle(pos);

      if (_tentacleStagger > 0f)
      {
        yield return new WaitForSeconds(_tentacleStagger / Speed);
      }
    }

    // Esperar a que todos se vayan
    while (!IsDefeated && _tentacles.Exists(t => t != null && !t.IsDone))
    {
      yield return null;
    }

    _tentacles.RemoveAll(t => t == null);
  }

  private void SpawnTentacle(Vector3 position)
  {
    // Al suelo
    if (Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out var hit, 10f, ~((1 << 3) | (1 << 10)), QueryTriggerInteraction.Ignore))
    {
      position = hit.point;
    }

    var look = _player != null ? _player.position - position : Vector3.forward;
    look.y = 0f;
    var rotation = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity;

    var go = Instantiate(_tentaclePrefab, position, rotation);
    go.transform.localScale *= _tentacleScale;
    SetLayer(go.transform, 10); // Enemy: la espada le pega

    var tentacle = go.GetComponent<BossTentacle>();
    if (tentacle == null) tentacle = go.AddComponent<BossTentacle>();
    MakeLockable(go);

    tentacle.Boss = this;
    tentacle.WarningTime = _tentacleWarning;
    tentacle.AttackSpeed = _tentacleAttackSpeed;
    tentacle.CommitTime = _tentacleCommit;
    tentacle.TiredTime = _tiredTime;
    tentacle.Damage = _tentacleDamage;
    tentacle.HitRadius = _tentacleHitRadius;
    tentacle.Reach = _tentacleReach;
    tentacle.FxMaterial = _fxMaterial;
    tentacle.EnergyColor = _energyColor;
    tentacle.DamageToBoss = _tentacleDamageToBoss;
    tentacle.Begin(Speed);
    _tentacles.Add(tentacle);
  }

  private Vector3 KeepInArena(Vector3 p)
  {
    if (_arena == null) return p;
    var inside = _arena.ClosestPoint(p);
    return new Vector3(inside.x, p.y, inside.z);
  }

  private IEnumerator ChargeAttack()
  {
    Trigger("AtaqueCargado");
    var length = ClipLength("Jefe_AtaqueCargado", 2f) / Speed;
    if (_animator != null) _animator.speed = Speed;

    // Aviso en el suelo durante la carga
    var ring = Ring(_chargeRadius, new Color(1f, 0.25f, 0.6f, 0.8f));
    var t = 0f;

    while (t < length * _chargeHitMoment && !IsDefeated)
    {
      t += Time.deltaTime;
      var c = new Color(1f, 0.25f, 0.6f, 0.4f + 0.5f * Mathf.PingPong(t * 4f, 1f));
      ring.startColor = ring.endColor = c;
      yield return null;
    }

    Destroy(ring.gameObject);

    if (!IsDefeated && _player != null)
    {
      TopDownCamera.AddShake(0.5f);
      BossFx.Burst(transform.position + Vector3.up * 0.3f, _energyColor, 60, _chargeRadius * 2.5f, 0.5f, 0.5f, _fxMaterial, true, 0f);
      var dir = _player.position - transform.position;
      dir.y = 0f;

      if (dir.magnitude <= _chargeRadius)
      {
        var player = _player.GetComponent<Player>();
        if (player != null) HurtPlayer(player, _chargeDamage, dir.normalized, _chargeKnockback);
      }
    }

    yield return new WaitForSeconds(length * (1f - _chargeHitMoment));
    if (_animator != null) _animator.speed = 1f;
  }

  // ---------- Daño ----------

  /// <summary>Daño que le pasa un tentaculo cansado al recibir un espadazo.</summary>
  public void DamageFromTentacle(int amount)
  {
    if (!IsDefeated && amount > 0)
    {
      _health.TakeDamage(amount);
    }
  }

  private void OnHealthChanged(int current, int max, int delta)
  {
    if (!IsFighting || IsDefeated || delta >= 0)
    {
      return;
    }

    // Indicador de golpe: destello, chispas y un pequeño "bote".
    BossFx.Flash(this, _renderers, _hitFlashColor, _hitFlashTime);
    var b = BoundsOf(gameObject);
    BossFx.Burst(b.center, Color.white, 14, 5f, 0.25f, 0.35f, _fxMaterial, true, 0f);
    BossFx.Burst(b.center, _energyColor, 10, 3f, 0.4f, 0.5f, _fxMaterial, true, 0f);
    if (_punch != null) StopCoroutine(_punch);
    _punch = StartCoroutine(Punch());

    if (!_phase2 && current <= max * _phase2At)
    {
      _phase2 = true;
      TopDownCamera.AddShake(0.4f);
    }

    if (Time.time >= _nextHurtAnim)
    {
      _nextHurtAnim = Time.time + 1.2f;
      Trigger("Danio");
    }
  }

  private IEnumerator Punch()
  {
    var t = 0f;

    while (t < 0.2f)
    {
      t += Time.deltaTime;
      var k = Mathf.Sin(Mathf.Clamp01(t / 0.2f) * Mathf.PI);
      transform.localScale = _baseScale * (1f + _hitPunch * k);
      yield return null;
    }

    transform.localScale = _baseScale;
  }

  private void OnDied()
  {
    if (IsDefeated) return;
    StopAllCoroutines();
    StartCoroutine(DefeatRoutine());
  }

  private IEnumerator DefeatRoutine()
  {
    IsDefeated = true;
    transform.localScale = _baseScale;

    foreach (var t in _tentacles)
    {
      if (t != null) t.ForceRetreat();
    }

    if (_animator != null) _animator.speed = 1f;
    Trigger("Derrota");
    Play(_defeatSound);
    TopDownCamera.AddShake(0.6f);

    foreach (var c in _colliders) if (c != null) c.enabled = false;

    if (AudioManager.Instance != null) AudioManager.Instance.SetCombatState(false);

    // Derrota y, al final, se deshace en polvo.
    var defeatLength = ClipLength("Jefe_Derrota", 3f);
    yield return new WaitForSeconds(defeatLength * _dissolveStart);
    yield return Dissolve();

    SetBarriers(false);

    if (_rewards != null)
    {
      var i = 0;

      foreach (var reward in _rewards)
      {
        if (reward == null) continue;
        var offset = Quaternion.Euler(0f, i * 360f / _rewards.Length, 0f) * Vector3.forward * 1.5f;
        Instantiate(reward, transform.position + offset + Vector3.up * 0.5f, Quaternion.identity);
        i++;
      }
    }

    OnDefeated.Invoke();
    Defeated?.Invoke(this);
  }

  /// <summary>Partida guardada: ya estaba vencido. Desaparece sin pelea.</summary>
  public void MarkDefeatedFromSave()
  {
    StopAllCoroutines();

    // La mesa ya estaba rota.
    foreach (var t in TablesToBreak())
    {
      if (t != null) t.SetActive(false);
    }

    IsDefeated = true;
    IsFighting = false;
    SetVisible(false);
    SetBarriers(false);
  }

  /// <summary>Daño a la nutria con escudo, invulnerabilidad y empuje (como los enemigos).</summary>
  public static void HurtPlayer(Player player, int damage, Vector3 direction, float knockback)
  {
    if (player == null || player.IsInvincible || Menus.MainMenu.IsOpen) return;

    var health = player.GetComponentInParent<Health>();
    if (health == null) health = player.GetComponentInChildren<Health>();
    if (health == null || health.IsDead) return;
    if (s_nextHit.TryGetValue(health, out var next) && Time.time < next) return;

    var body = player.GetComponent<Rigidbody>();

    // Escudo de frente: bloquea
    if (player.IsBlocking && Vector3.Angle(player.transform.forward, -direction) <= 60f)
    {
      s_nextHit[health] = Time.time + 0.4f;
      player.NotifyBlocked();
      if (body != null && !body.isKinematic) body.AddForce(direction * knockback * 0.5f, ForceMode.VelocityChange);
      TopDownCamera.AddShake(0.15f);
      return;
    }

    s_nextHit[health] = Time.time + 0.8f;
    health.TakeDamage(damage);
    if (body != null && !body.isKinematic) body.AddForce(direction * knockback, ForceMode.VelocityChange);
    TopDownCamera.AddShake(0.35f);
  }

  // ---------- Desvanecido ----------

  /// <summary>Cambia los materiales por el de disolver (con su textura y color) y lo anima.</summary>
  private IEnumerator Dissolve()
  {
    var bounds = BoundsOf(gameObject);
    var dissolving = new List<Material>();

    if (_dissolveMaterial != null)
    {
      foreach (var r in _renderers)
      {
        if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;

        var originals = r.sharedMaterials;
        var copies = new Material[originals.Length];

        for (var i = 0; i < originals.Length; i++)
        {
          var m = new Material(_dissolveMaterial);
          var src = originals[i];

          if (src != null)
          {
            var tex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
            if (tex != null) m.SetTexture("_BaseMap", tex);
            if (src.HasProperty("_BaseColor")) m.SetColor("_BaseColor", src.GetColor("_BaseColor"));
            else if (src.HasProperty("_Color")) m.SetColor("_BaseColor", src.GetColor("_Color"));
          }

          m.SetFloat("_HeightMin", bounds.min.y);
          m.SetFloat("_HeightMax", bounds.max.y);
          copies[i] = m;
          dissolving.Add(m);
        }

        r.sharedMaterials = copies;
      }
    }

    // Polvo que sube mientras se deshace
    var dust = BossFx.Wisps(transform, transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)),
      _dustColor, Mathf.Max(bounds.extents.x, bounds.extents.z), _fxMaterial);

    if (dust != null)
    {
      var shape = dust.shape;
      shape.shapeType = ParticleSystemShapeType.Box;
      shape.scale = new Vector3(bounds.size.x, bounds.size.z, bounds.size.y) * 0.8f;
      shape.position = new Vector3(0f, 0f, bounds.extents.y);
      var emission = dust.emission;
      emission.rateOverTime = 80f;
      var main = dust.main;
      main.maxParticles = 300;
      main.gravityModifier = -0.15f;
    }

    var t = 0f;

    while (t < _dissolveTime)
    {
      t += Time.deltaTime;
      var progress = Mathf.Clamp01(t / _dissolveTime);
      foreach (var m in dissolving) m.SetFloat("_Progress", progress);
      yield return null;
    }

    foreach (var r in _renderers) if (r != null) r.enabled = false;
    if (dust != null) dust.Stop();
    foreach (var m in dissolving) Destroy(m, 0.1f);
  }

  /// <summary>
  /// Se puede fijar con L1 (lock-on): el lock-on busca componentes Enemy.
  /// Enemy con control externo no hace nada mas (sin IA ni daño por contacto).
  /// </summary>
  private static void MakeLockable(GameObject go)
  {
    var enemy = go.GetComponent<Enemy>();

    if (enemy == null)
    {
      enemy = go.AddComponent<Enemy>();
      var contact = go.GetComponent<ContactDamage>();
      if (contact != null) Destroy(contact);
    }

    enemy.ExternalControl = true;
  }

  // ---------- Mesa ----------

  private void BreakTables()
  {
    foreach (var t in TablesToBreak())
    {
      if (t == null || !t.activeInHierarchy) continue;
      var b = BoundsOf(t);
      BossFx.Burst(b.center, new Color(0.45f, 0.32f, 0.2f, 1f), 30, 5f, 0.35f, 1f, _fxMaterial, true, 1f);
      BossFx.Shatter(t, transform.position + Vector3.down * 0.5f, _breakForce);
    }
  }

  /// <summary>Las asignadas, o la mesa mas cercana al jefe (sin puzzles).</summary>
  private System.Collections.Generic.IEnumerable<GameObject> TablesToBreak()
  {
    if (_breakOnAppear != null && _breakOnAppear.Length > 0)
    {
      return _breakOnAppear;
    }

    GameObject best = null;
    var bestDistance = 4f;

    foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
    {
      var root = r.transform;

      // Sube hasta el objeto que se llama "mesa..."
      while (root != null && !root.name.ToLowerInvariant().StartsWith("mesa")) root = root.parent;
      if (root == null || root.IsChildOf(transform)) continue;
      if (root.GetComponentInChildren<InteractiveCameraFocus>(true) != null) continue; // el billar del puzzle no

      var d = Vector3.Distance(new Vector3(r.bounds.center.x, 0f, r.bounds.center.z), new Vector3(transform.position.x, 0f, transform.position.z));

      if (d < bestDistance)
      {
        bestDistance = d;
        best = root.gameObject;
      }
    }

    _breakOnAppear = best != null ? new[] { best } : new GameObject[0];
    return _breakOnAppear;
  }

  private static Bounds BoundsOf(GameObject go)
  {
    var renderers = go.GetComponentsInChildren<Renderer>();
    if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
    var b = renderers[0].bounds;
    foreach (var r in renderers) b.Encapsulate(r.bounds);
    return b;
  }

  // ---------- Utilidades ----------

  private void Trigger(string name)
  {
    if (_animator != null) _animator.SetTrigger(name);
  }

  private float ClipLength(string clipName, float fallback)
  {
    if (_animator == null || _animator.runtimeAnimatorController == null) return fallback;

    foreach (var clip in _animator.runtimeAnimatorController.animationClips)
    {
      if (clip != null && clip.name == clipName) return clip.length;
    }

    return fallback;
  }

  private void SetVisible(bool visible)
  {
    foreach (var r in _renderers) if (r != null) r.enabled = visible;
    foreach (var c in _colliders) if (c != null && c != _arena) c.enabled = visible;
  }

  private void SetBarriers(bool active)
  {
    if (_barriers == null) return;
    foreach (var b in _barriers) if (b != null) b.SetActive(active);
  }

  private void Play(AudioClip clip)
  {
    if (clip != null) AudioSource.PlayClipAtPoint(clip, transform.position, _volume);
  }

  private static void SetLayer(Transform t, int layer)
  {
    t.gameObject.layer = layer;
    for (var i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
  }

  private LineRenderer Ring(float radius, Color color)
  {
    var go = new GameObject("AvisoCargado");
    go.transform.position = transform.position + Vector3.up * 0.05f;
    var line = go.AddComponent<LineRenderer>();
    line.useWorldSpace = false;
    line.loop = true;
    line.widthMultiplier = 0.15f;
    line.positionCount = 64;
    line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    line.material = new Material(Shader.Find("Sprites/Default"));
    line.startColor = line.endColor = color;

    for (var i = 0; i < line.positionCount; i++)
    {
      var a = i / (float)line.positionCount * Mathf.PI * 2f;
      line.SetPosition(i, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
    }

    return line;
  }

  private void OnDrawGizmosSelected()
  {
    Gizmos.color = new Color(1f, 0.25f, 0.6f, 0.8f);
    Gizmos.DrawWireSphere(transform.position, _chargeRadius);
  }
}
