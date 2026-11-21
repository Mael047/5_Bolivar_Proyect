// BossTentacle.cs
// Un tentaculo del jefe (lo crea BossFight; no hace falta ponerlo a mano).
//
//   1. Aviso: un circulo donde va a salir y otro donde va a golpear (delante,
//      hacia la nutria). El tentaculo apunta a la nutria hasta poco antes del
//      golpe; despues se "compromete" y ya no gira (asi se puede esquivar).
//   2. Sale del suelo (tierra y energia), y golpea hacia la nutria (Ataque1/2/3
//      al azar): daño si esta en la zona del golpe; el escudo de frente bloquea.
//   3. Se cansa (Cansado, con humo morado): la espada le hace daño y ese daño
//      pasa al jefe. Antes de cansarse los golpes no le hacen nada.
//   4. Se mete en el suelo (Derrota) y desaparece.

using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class BossTentacle : MonoBehaviour, IDamageable
{
  public BossFight Boss { get; set; }

  [Header("Tiempos")]
  public float WarningTime = 1f;
  public float TiredTime = 3f;
  [Range(0f, 1f), Tooltip("Momento del golpe dentro de la animacion de ataque.")]
  public float HitMoment = 0.5f;
  [Tooltip("Segundos antes del golpe en los que deja de girar hacia la nutria.")]
  public float CommitTime = 0.18f;
  [Tooltip("Velocidad de la animacion de golpe (1 = normal).")]
  public float AttackSpeed = 1.8f;

  [Header("Golpe")]
  public int Damage = 15;
  [Tooltip("Hasta donde llega el golpe por delante del tentaculo.")]
  public float Reach = 2.5f;
  [Tooltip("Radio de la zona del golpe.")]
  public float HitRadius = 1.4f;
  public float Knockback = 9f;
  public float TurnSpeed = 300f;

  [Header("Al jefe")]
  public float DamageToBoss = 1f;
  public int HitsToRetreat = 3;

  [Header("Efectos")]
  public Material FxMaterial;
  public Color EnergyColor = new Color(0.75f, 0.25f, 1f, 1f);
  public Color DirtColor = new Color(0.35f, 0.25f, 0.2f, 1f);

  public Color FlashColor = new Color(2.5f, 2.2f, 2.8f, 1f);

  public bool IsDone { get; private set; }
  public bool IsTired { get; private set; }

  private Animator _animator;
  private Renderer[] _renderers;
  private Collider _collider;
  private LineRenderer _baseRing;
  private LineRenderer _strikeRing;
  private ParticleSystem _wisps;
  private Transform _player;
  private int _hits;
  private bool _retreating;
  private bool _tracking = true;

  private Vector3 StrikeCenter => transform.position + transform.forward * (Reach * 0.6f);

  private void Awake()
  {
    _animator = GetComponentInChildren<Animator>();
    _renderers = GetComponentsInChildren<Renderer>(true);
    SetVisible(false);
  }

  public void Begin(float speed)
  {
    var p = FindAnyObjectByType<Player>();
    _player = p != null ? p.transform : null;
    if (_animator != null) _animator.speed = speed;
    StartCoroutine(Run(speed));
  }

  private void Update()
  {
    if (_tracking && _player != null && !_retreating)
    {
      var dir = _player.position - transform.position;
      dir.y = 0f;

      if (dir.sqrMagnitude > 0.01f)
      {
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), TurnSpeed * Time.deltaTime);
      }
    }

    if (_strikeRing != null)
    {
      _strikeRing.transform.position = StrikeCenter + Vector3.up * 0.05f;
    }
  }

  private IEnumerator Run(float speed)
  {
    // 1. Aviso
    _baseRing = Ring("AvisoSalida", 0.6f, transform.position);
    _strikeRing = Ring("AvisoGolpe", HitRadius, StrikeCenter);
    var t = 0f;

    while (t < WarningTime / speed)
    {
      t += Time.deltaTime;
      var c = EnergyColor;
      c.a = 0.4f + 0.5f * Mathf.Abs(Mathf.Sin(t * 9f));
      _baseRing.startColor = _baseRing.endColor = c;
      _strikeRing.startColor = _strikeRing.endColor = c;
      yield return null;
    }

    Destroy(_baseRing.gameObject);

    // 2. Sale del suelo
    SetVisible(true);
    EnsureCollider();
    BossFx.Burst(transform.position, DirtColor, 25, 5f, 0.35f, 0.9f, FxMaterial, false, 1.2f);
    BossFx.Burst(transform.position + Vector3.up * 0.2f, EnergyColor, 20, 3f, 0.4f, 0.7f, FxMaterial, false, 0f);
    TopDownCamera.AddShake(0.15f);
    Trigger("Aparecer");
    yield return new WaitForSeconds(ClipLength("Tentaculo_Aparecer", 0.6f) / speed);

    // Golpe hacia la nutria
    var attack = Random.Range(1, 4);
    var attackSpeed = speed * AttackSpeed;
    if (_animator != null) _animator.speed = attackSpeed;
    Trigger("Ataque" + attack);
    var length = ClipLength("Tentaculo_Ataque" + attack, 1.2f) / attackSpeed;
    var untilHit = length * HitMoment;

    yield return new WaitForSeconds(Mathf.Max(0f, untilHit - CommitTime));
    _tracking = false; // ya no gira: se puede esquivar
    if (_strikeRing != null) _strikeRing.startColor = _strikeRing.endColor = new Color(1f, 0.3f, 0.5f, 1f);
    yield return new WaitForSeconds(Mathf.Min(CommitTime, untilHit));

    if (_strikeRing != null) Destroy(_strikeRing.gameObject);
    HitPlayer();
    yield return new WaitForSeconds(length * (1f - HitMoment));

    // 3. Cansado (vulnerable)
    if (_animator != null) _animator.speed = speed;
    IsTired = true;
    if (_animator != null) _animator.SetBool("Cansado", true);
    _wisps = BossFx.Wisps(transform, Vector3.up * 0.3f, new Color(EnergyColor.r, EnergyColor.g, EnergyColor.b, 0.6f), 0.4f, FxMaterial);

    t = 0f;
    while (t < TiredTime && !_retreating)
    {
      t += Time.deltaTime;
      yield return null;
    }

    yield return Retreat();
  }

  /// <summary>Se mete en el suelo y desaparece (tambien al morir el jefe).</summary>
  public void ForceRetreat()
  {
    if (_retreating || IsDone) return;
    StopAllCoroutines();
    if (_baseRing != null) Destroy(_baseRing.gameObject);
    if (_strikeRing != null) Destroy(_strikeRing.gameObject);
    StartCoroutine(Retreat());
  }

  private IEnumerator Retreat()
  {
    _retreating = true;
    IsTired = false;
    if (_animator != null) _animator.SetBool("Cansado", false);
    if (_wisps != null) _wisps.Stop();

    if (_renderers.Length > 0 && _renderers[0] != null && _renderers[0].enabled)
    {
      Trigger("Derrota");
      yield return new WaitForSeconds(ClipLength("Tentaculo_Derrota", 0.8f));
      BossFx.Burst(transform.position, DirtColor, 15, 3f, 0.3f, 0.7f, FxMaterial, false, 1.2f);
    }

    IsDone = true;
    Destroy(gameObject);
  }

  // ---------- Daño ----------

  /// <summary>La espada: solo hace algo mientras esta cansado.</summary>
  public void TakeDamage(int damageAmount)
  {
    if (!IsTired || _retreating)
    {
      // Aun no esta cansado: el golpe rebota (chispas grises, sin daño).
      if (_renderers.Length > 0 && _renderers[0] != null && _renderers[0].enabled)
      {
        BossFx.Burst(transform.position + Vector3.up * 1f, new Color(0.8f, 0.8f, 0.8f, 1f), 8, 4f, 0.15f, 0.25f, FxMaterial, true, 0f);
      }

      return;
    }

    BossFx.Flash(this, _renderers, FlashColor, 0.18f);

    if (Boss != null)
    {
      Boss.DamageFromTentacle(Mathf.RoundToInt(damageAmount * DamageToBoss));
    }

    BossFx.Burst(transform.position + Vector3.up * 1f, EnergyColor, 12, 4f, 0.3f, 0.4f, FxMaterial, true, 0f);
    _hits++;

    if (_hits >= HitsToRetreat)
    {
      _retreating = true;
    }
  }

  private void HitPlayer()
  {
    var center = StrikeCenter;
    BossFx.Burst(center, EnergyColor, 30, 6f, 0.45f, 0.6f, FxMaterial, true, 0.3f);
    BossFx.Burst(center, DirtColor, 20, 4f, 0.3f, 0.8f, FxMaterial, false, 1.5f);
    TopDownCamera.AddShake(0.3f);

    var player = _player != null ? _player.GetComponent<Player>() : null;
    if (player == null) return;

    var toPlayer = player.transform.position - center;
    toPlayer.y = 0f;

    if (toPlayer.magnitude > HitRadius)
    {
      return;
    }

    BossFight.HurtPlayer(player, Damage, transform.forward, Knockback);
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
    foreach (var r in _renderers)
    {
      if (r != null) r.enabled = visible;
    }

    if (_collider != null) _collider.enabled = visible;
  }

  private void EnsureCollider()
  {
    _collider = GetComponent<Collider>();

    if (_collider == null)
    {
      var capsule = gameObject.AddComponent<CapsuleCollider>();
      var bounds = new Bounds(transform.position, Vector3.zero);
      foreach (var r in _renderers) if (r != null) bounds.Encapsulate(r.bounds);
      var scaleY = Mathf.Max(0.001f, transform.lossyScale.y);
      var scaleX = Mathf.Max(0.001f, transform.lossyScale.x);
      capsule.height = Mathf.Max(1f, bounds.size.y) / scaleY;
      capsule.radius = Mathf.Max(0.25f, Mathf.Min(bounds.size.x, bounds.size.z) * 0.3f) / scaleX;
      capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
      _collider = capsule;
    }

    _collider.enabled = true;
  }

  private LineRenderer Ring(string name, float radius, Vector3 center)
  {
    var go = new GameObject(name);
    go.transform.position = center + Vector3.up * 0.05f;
    var line = go.AddComponent<LineRenderer>();
    line.useWorldSpace = false;
    line.loop = true;
    line.widthMultiplier = 0.1f;
    line.positionCount = 40;
    line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    line.receiveShadows = false;
    line.material = new Material(Shader.Find("Sprites/Default"));

    for (var i = 0; i < line.positionCount; i++)
    {
      var a = i / (float)line.positionCount * Mathf.PI * 2f;
      line.SetPosition(i, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
    }

    return line;
  }
}
