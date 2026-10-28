// RangedAttack.cs
// Ataque a distancia (marimonda): cuando ve al jugador dentro del rango se
// queda a distancia, lo encara y le escupe un proyectil cada cierto tiempo.
// Si el jugador se acerca demasiado, retrocede. Si se aleja o deja de verlo,
// el Enemy vuelve a perseguir/patrullar como siempre.
//
// Animacion: dispara el trigger "Attack" del Animator. El proyectil sale a
// los "Fire Delay" segundos, o en el evento AE_Spit si lo pones en el clip
// (y activas "Wait For Animation Event").
//
// El proyectil es un prefab con EnemyProjectile (se diseña libremente).
// Tools > Enemigos > Configurar ataque a distancia de la marimonda lo deja listo.

using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[RequireComponent(typeof(Enemy))]
public class RangedAttack : MonoBehaviour
{
  private static readonly int AttackId = Animator.StringToHash("Attack");

  [Header("Proyectil")]
  [SerializeField, Tooltip("Prefab con EnemyProjectile.")]
  private EnemyProjectile _projectile;
  [SerializeField, Tooltip("Desde donde sale (la boca). Vacio = la cabeza del modelo.")]
  private Transform _muzzle;
  [SerializeField, Tooltip("Desplazamiento desde la cabeza si no hay 'Muzzle' (hacia delante).")]
  private float _muzzleForward = 0.3f;
  [SerializeField, Min(1)] private int _shotsPerAttack = 1;
  [SerializeField, Range(0f, 60f), Tooltip("Apertura en abanico si escupe varios.")]
  private float _spreadAngle = 20f;
  [SerializeField, Tooltip("Altura del jugador a la que apunta.")]
  private float _targetHeight = 0.9f;
  [SerializeField, Range(0f, 1f), Tooltip("Cuanto se adelanta al movimiento del jugador (0 = apunta a donde esta).")]
  private float _leadFactor = 0.3f;

  [Header("Distancias")]
  [SerializeField, Min(1f), Tooltip("Dentro de este rango ataca a distancia.")]
  private float _attackRange = 11f;
  [SerializeField, Min(0f), Tooltip("Si el jugador se acerca mas, retrocede.")]
  private float _retreatDistance = 4f;
  [SerializeField, Min(0f)] private float _retreatSpeed = 2.5f;
  [SerializeField, Min(0f), Tooltip("Grados por segundo al girar hacia el jugador.")]
  private float _turnSpeed = 360f;

  [Header("Ritmo")]
  [SerializeField, Min(0f), Tooltip("Segundos entre ataques.")]
  private float _cooldown = 2.2f;
  [SerializeField, Min(0f), Tooltip("Espera antes del primer ataque al ver al jugador.")]
  private float _firstAttackDelay = 0.6f;
  [SerializeField, Min(0f), Tooltip("Tiempo desde que empieza la animacion hasta que sale el proyectil.")]
  private float _fireDelay = 0.55f;
  [SerializeField, Tooltip("Espera el evento AE_Spit del clip en vez de 'Fire Delay'.")]
  private bool _waitForAnimationEvent;
  [SerializeField, Min(0f), Tooltip("Quieto despues de escupir.")]
  private float _recoverTime = 0.5f;
  [SerializeField, Tooltip("Si lo golpeas mientras prepara, el ataque se cancela.")]
  private bool _interruptible = true;

  [Header("Sonidos")]
  [SerializeField] private AudioClip _spitSound;
  [SerializeField, Range(0f, 1f)] private float _volume = 0.9f;

  [Header("Eventos")]
  public UnityEvent OnAttackStart = new UnityEvent();
  public UnityEvent OnFire = new UnityEvent();

  private Enemy _enemy;
  private Health _health;
  private HitReaction _hitReaction;
  private Animator _animator;
  private Rigidbody _rb;
  private bool _hasAttackParam;
  private bool _engaged;
  private bool _attacking;
  private bool _spitEventReceived;
  private float _nextAttackTime;

  private void Awake()
  {
    _enemy = GetComponent<Enemy>();
    _health = GetComponent<Health>();
    _hitReaction = GetComponent<HitReaction>();
    _rb = GetComponent<Rigidbody>();
    _animator = GetComponentInChildren<Animator>();

    if (_animator != null)
    {
      foreach (var p in _animator.parameters)
      {
        if (p.nameHash == AttackId && p.type == AnimatorControllerParameterType.Trigger)
        {
          _hasAttackParam = true;
        }
      }

      if (_muzzle == null && _animator.isHuman)
      {
        _muzzle = _animator.GetBoneTransform(HumanBodyBones.Head);
      }
    }
  }

  private void OnEnable()
  {
    if (_health != null) _health.Died += OnDied;
  }

  private void OnDisable()
  {
    if (_health != null) _health.Died -= OnDied;
    Release();
  }

  private void OnDied()
  {
    StopAllCoroutines();
    Release();
    enabled = false;
  }

  /// <summary>Lo llama el evento AE_Spit del clip.</summary>
  public void OnSpitEvent()
  {
    _spitEventReceived = true;
  }

  // ---------- Decision ----------

  private void Update()
  {
    if (_enemy == null || (_health != null && _health.IsDead) || Menus.MainMenu.IsOpen)
    {
      return;
    }

    if (_attacking)
    {
      return;
    }

    var target = _enemy.target != null ? _enemy.target.transform : null;
    var inRange = target != null && _enemy.CanSeePlayer() && FlatDistance(target.position) <= _attackRange;

    // Ya enganchado: no hace falta que siga en el cono de vision, basta el rango.
    if (_engaged && target != null && FlatDistance(target.position) <= _attackRange * 1.15f)
    {
      inRange = true;
    }

    if (!inRange)
    {
      Release();
      return;
    }

    if (!_engaged)
    {
      _engaged = true;
      _enemy.ExternalControl = true;
      _enemy.attack = false;
      _nextAttackTime = Mathf.Max(_nextAttackTime, Time.time + _firstAttackDelay);
    }

    var stunned = _hitReaction != null && _hitReaction.IsStunned;
    _enemy.IsMovingNow = false;

    if (stunned)
    {
      return;
    }

    Face(target.position, _turnSpeed);

    // Mantener distancia
    if (_retreatDistance > 0f && FlatDistance(target.position) < _retreatDistance)
    {
      var back = -Flat(transform.forward);

      if (!Physics.Raycast(transform.position + Vector3.up * 0.5f, back, 1f, _enemy.obstacleMask, QueryTriggerInteraction.Ignore))
      {
        Move(back * (_retreatSpeed * Time.deltaTime));
        _enemy.IsMovingNow = true;
      }
    }

    if (Time.time >= _nextAttackTime)
    {
      StartCoroutine(AttackRoutine(target));
    }
  }

  private void Release()
  {
    if (!_engaged)
    {
      return;
    }

    _engaged = false;
    _attacking = false;

    if (_enemy != null)
    {
      _enemy.ExternalControl = false;
      _enemy.IsMovingNow = false;
    }
  }

  // ---------- Ataque ----------

  private IEnumerator AttackRoutine(Transform target)
  {
    _attacking = true;
    _enemy.IsMovingNow = false;
    _spitEventReceived = false;

    if (_hasAttackParam)
    {
      _animator.SetTrigger(AttackId);
    }

    OnAttackStart.Invoke();

    // Preparar: sigue mirando al jugador hasta escupir.
    var t = 0f;
    var maxWait = _waitForAnimationEvent ? Mathf.Max(_fireDelay, 2f) : _fireDelay;

    while (t < maxWait && !(_waitForAnimationEvent && _spitEventReceived))
    {
      if (_interruptible && _hitReaction != null && _hitReaction.IsStunned)
      {
        if (_hasAttackParam) _animator.ResetTrigger(AttackId);
        _attacking = false;
        _nextAttackTime = Time.time + _cooldown * 0.5f;
        yield break;
      }

      if (target != null)
      {
        Face(target.position, _turnSpeed);
      }

      t += Time.deltaTime;
      yield return null;
    }

    Fire(target);

    if (_recoverTime > 0f)
    {
      yield return new WaitForSeconds(_recoverTime);
    }

    _nextAttackTime = Time.time + _cooldown;
    _attacking = false;
  }

  private void Fire(Transform target)
  {
    if (_projectile == null)
    {
      Debug.LogWarning($"[RangedAttack] '{name}' no tiene prefab de proyectil.", this);
      return;
    }

    var origin = MuzzlePosition();
    var aimPoint = target != null ? target.position + Vector3.up * _targetHeight : origin + transform.forward * 5f;

    if (target != null && _leadFactor > 0f)
    {
      var body = target.GetComponentInParent<Rigidbody>();

      if (body != null)
      {
        var travel = Vector3.Distance(origin, aimPoint) / Mathf.Max(0.1f, _projectile.Speed);
        var v = body.linearVelocity;
        v.y = 0f;
        aimPoint += v * travel * _leadFactor;
      }
    }

    var direction = (aimPoint - origin).normalized;

    for (var i = 0; i < _shotsPerAttack; i++)
    {
      var angle = _shotsPerAttack > 1 ? Mathf.Lerp(-_spreadAngle * 0.5f, _spreadAngle * 0.5f, i / (float)(_shotsPerAttack - 1)) : 0f;
      var dir = Quaternion.AngleAxis(angle, Vector3.up) * direction;
      var projectile = Instantiate(_projectile, origin, Quaternion.LookRotation(dir, Vector3.up));
      projectile.Launch(dir, gameObject);
    }

    if (_spitSound != null)
    {
      AudioSource.PlayClipAtPoint(_spitSound, origin, _volume);
    }

    OnFire.Invoke();
  }

  private Vector3 MuzzlePosition()
  {
    if (_muzzle == null)
    {
      return transform.position + Vector3.up * 1.2f + transform.forward * _muzzleForward;
    }

    // Si es un hueso (la cabeza), se adelanta un poco hacia donde mira el cuerpo.
    var isBone = _animator != null && _animator.isHuman && _muzzle == _animator.GetBoneTransform(HumanBodyBones.Head);
    return isBone ? _muzzle.position + Flat(transform.forward) * _muzzleForward : _muzzle.position;
  }

  // ---------- Utilidades ----------

  private float FlatDistance(Vector3 point)
  {
    var d = point - transform.position;
    d.y = 0f;
    return d.magnitude;
  }

  private void Face(Vector3 point, float speed)
  {
    var dir = point - transform.position;
    dir.y = 0f;

    if (dir.sqrMagnitude < 0.0001f)
    {
      return;
    }

    var rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir.normalized, Vector3.up), speed * Time.deltaTime);

    if (_rb != null && !_rb.isKinematic)
    {
      _rb.rotation = rotation;
    }

    transform.rotation = rotation;
  }

  private void Move(Vector3 delta)
  {
    if (_rb != null && !_rb.isKinematic)
    {
      _rb.position += delta;
    }
    else
    {
      transform.position += delta;
    }
  }

  private static Vector3 Flat(Vector3 v)
  {
    v.y = 0f;
    return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
  }

  private void OnDrawGizmosSelected()
  {
    Gizmos.color = new Color(0.7f, 0.4f, 1f, 0.7f);
    Gizmos.DrawWireSphere(transform.position, _attackRange);
    Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.7f);
    Gizmos.DrawWireSphere(transform.position, _retreatDistance);

    if (_muzzle != null)
    {
      Gizmos.color = Color.magenta;
      Gizmos.DrawSphere(_muzzle.position, 0.06f);
    }
  }
}
