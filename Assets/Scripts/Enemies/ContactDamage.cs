// ContactDamage.cs
// Daño por contacto (provisional, hasta tener ataques animados): si el jugador
// toca al enemigo recibe daño, un pequeño empuje y un temblor de camara.
// Funciona con colliders normales (OnCollision) y con triggers (OnTrigger).
//
// Tras un golpe el jugador queda invulnerable un momento (compartido entre
// todos los enemigos, para que dos a la vez no te quiten todo de golpe).
// Respeta la esquiva (Player.IsInvincible) y, si se activa, el escudo:
// cubriendote de frente al enemigo no recibes daño.
//
// Enemy lo añade solo si el enemigo no lo tiene; para ajustarlo por enemigo,
// añadelo a mano al prefab.

using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class ContactDamage : MonoBehaviour
{
  [SerializeField, Tooltip("Daño por contacto. -1 = usa 'Damage' del Enemy.")]
  private int _damage = -1;

  [SerializeField, Min(0f), Tooltip("Segundos de invulnerabilidad del jugador tras recibir un golpe.")]
  private float _playerInvulnerability = 1f;

  [SerializeField, Min(0f), Tooltip("Empuje que recibe el jugador al ser golpeado.")]
  private float _knockback = 6f;

  [SerializeField, Range(0f, 1f), Tooltip("Temblor de camara al golpear.")]
  private float _cameraShake = 0.35f;

  [Header("Escudo")]
  [SerializeField, Tooltip("Cubrirse de frente al enemigo bloquea el daño.")]
  private bool _blockWithShield = true;

  [SerializeField, Range(0f, 360f), Tooltip("Angulo frontal (grados) en el que el escudo protege.")]
  private float _blockAngle = 140f;

  [SerializeField, Range(0f, 1f), Tooltip("Fraccion del empuje que se recibe al bloquear.")]
  private float _blockedKnockback = 0.5f;

  // Proximo momento en que cada jugador puede volver a recibir daño por contacto.
  private static readonly Dictionary<Health, float> s_nextHit = new Dictionary<Health, float>();

  private Enemy _enemy;
  private Health _ownHealth;
  private HitReaction _hitReaction;

  private void Awake()
  {
    _enemy = GetComponent<Enemy>();
    _ownHealth = GetComponent<Health>();
    _hitReaction = GetComponent<HitReaction>();
  }

  private void OnCollisionEnter(Collision collision) => TryHit(collision.collider);
  private void OnCollisionStay(Collision collision) => TryHit(collision.collider);
  private void OnTriggerEnter(Collider other) => TryHit(other);
  private void OnTriggerStay(Collider other) => TryHit(other);

  private void TryHit(Collider other)
  {
    if (!isActiveAndEnabled || Menus.MainMenu.IsOpen)
    {
      return;
    }

    // Un enemigo muerto o aturdido por tu golpe no hace daño.
    if ((_ownHealth != null && _ownHealth.IsDead) || (_hitReaction != null && _hitReaction.IsStunned))
    {
      return;
    }

    var player = other.GetComponentInParent<Player>();

    if (player == null || player.IsInvincible)
    {
      return;
    }

    var health = player.GetComponentInParent<Health>();

    if (health == null)
    {
      health = player.GetComponentInChildren<Health>();
    }

    if (health == null || health.IsDead)
    {
      return;
    }

    if (s_nextHit.TryGetValue(health, out var next) && Time.time < next)
    {
      return;
    }

    var direction = player.transform.position - transform.position;
    direction.y = 0f;
    direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : -player.transform.forward;

    if (_blockWithShield && player.IsBlocking &&
        Vector3.Angle(player.transform.forward, -direction) <= _blockAngle * 0.5f)
    {
      s_nextHit[health] = Time.time + _playerInvulnerability * 0.5f;
      player.NotifyBlocked();
      Push(player, direction, _knockback * _blockedKnockback);
      TopDownCamera.AddShake(_cameraShake * 0.4f);
      return;
    }

    var amount = _damage >= 0 ? _damage : (_enemy != null ? _enemy.Damage : 10);
    s_nextHit[health] = Time.time + _playerInvulnerability;
    health.TakeDamage(amount);

    Push(player, direction, _knockback);
    TopDownCamera.AddShake(_cameraShake);
  }

  private static void Push(Player player, Vector3 direction, float force)
  {
    if (force <= 0f)
    {
      return;
    }

    var rb = player.GetComponent<Rigidbody>();

    if (rb != null && !rb.isKinematic)
    {
      rb.AddForce(direction * force, ForceMode.VelocityChange);
    }
  }
}
