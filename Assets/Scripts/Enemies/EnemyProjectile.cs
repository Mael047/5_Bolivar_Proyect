// EnemyProjectile.cs
// Proyectil de enemigo (escupitajo de la marimonda). Vuela en linea recta (o
// en arco con gravedad, o persiguiendo un poco al jugador), hace daño al
// tocar al jugador y se deshace al chocar con el escenario.
//
// Respeta la esquiva (lo atraviesa) y el escudo: cubriendote de frente lo
// bloqueas. Comparte la invulnerabilidad tras un golpe con el resto de
// ataques (ContactDamage).
//
// El aspecto es libre: particulas, mallas, luces... como hijos del prefab.
// Los sistemas de particulas con "Detach On Impact" se quedan desvaneciendose
// cuando el proyectil choca (la estela no desaparece de golpe).
// No necesita collider ni Rigidbody: detecta los choques con un barrido.

using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyProjectile : MonoBehaviour
{
  [Header("Vuelo")]
  [SerializeField, Min(0.1f)] private float _speed = 9f;
  [SerializeField, Min(0.1f), Tooltip("Segundos hasta deshacerse solo.")]
  private float _lifetime = 3f;
  [SerializeField, Tooltip("Caida (m/s²). 0 = recto; positivo = hace arco.")]
  private float _gravity = 0f;
  [SerializeField, Min(0f), Tooltip("Grados por segundo que corrige hacia el jugador (0 = no persigue).")]
  private float _homing = 0f;
  [SerializeField, Min(0.01f), Tooltip("Radio de choque.")]
  private float _radius = 0.25f;
  [SerializeField, Tooltip("Capas con las que choca (suelo, muros...). El jugador se detecta aparte.")]
  private LayerMask _hitMask = ~((1 << 2) | (1 << 5) | (1 << 10));

  [Header("Daño")]
  [SerializeField, Min(0)] private int _damage = 10;
  [SerializeField, Min(0f)] private float _knockback = 4f;
  [SerializeField, Min(0f)] private float _playerInvulnerability = 0.8f;
  [SerializeField, Range(0f, 1f)] private float _cameraShake = 0.2f;
  [SerializeField] private bool _blockWithShield = true;
  [SerializeField, Range(0f, 360f)] private float _blockAngle = 140f;
  [SerializeField, Tooltip("Rompe cajas (Breakable) al chocar.")]
  private bool _breaksBoxes;

  [Header("Efectos")]
  [SerializeField, Tooltip("Prefab al chocar (salpicadura). Se destruye solo si es un sistema de particulas.")]
  private GameObject _impactEffect;
  [SerializeField, Tooltip("Particulas que se sueltan al chocar para que se desvanezcan (estela, gotas).")]
  private ParticleSystem[] _detachOnImpact;
  [SerializeField] private AudioClip _impactSound;
  [SerializeField, Range(0f, 1f)] private float _volume = 0.8f;

  private Vector3 _velocity;
  private GameObject _owner;
  private Transform _target;
  private float _age;
  private bool _done;
  private readonly HashSet<Collider> _ownerColliders = new HashSet<Collider>();

  public float Speed => _speed;

  /// <summary>Lanza el proyectil. 'owner' no recibe choques (el que lo escupio).</summary>
  public void Launch(Vector3 direction, GameObject owner)
  {
    _velocity = direction.normalized * _speed;
    _owner = owner;

    if (owner != null)
    {
      foreach (var c in owner.GetComponentsInChildren<Collider>(true))
      {
        _ownerColliders.Add(c);
      }
    }

    var player = GameObject.FindWithTag("Player");
    _target = player != null ? player.transform : null;

    if (_velocity.sqrMagnitude > 0.0001f)
    {
      transform.rotation = Quaternion.LookRotation(_velocity, Vector3.up);
    }
  }

  private void Start()
  {
    // Por si se coloca a mano en la escena sin Launch.
    if (_velocity == Vector3.zero)
    {
      Launch(transform.forward, null);
    }
  }

  private void Update()
  {
    if (_done)
    {
      return;
    }

    var dt = Time.deltaTime;
    _age += dt;

    if (_age >= _lifetime)
    {
      Impact(transform.position, Vector3.up, false);
      return;
    }

    if (_homing > 0f && _target != null)
    {
      var to = (_target.position + Vector3.up * 0.9f) - transform.position;
      var speed = _velocity.magnitude;
      _velocity = Vector3.RotateTowards(_velocity, to, _homing * Mathf.Deg2Rad * dt, 0f).normalized * speed;
    }

    if (_gravity != 0f)
    {
      _velocity += Vector3.down * (_gravity * dt);
    }

    var step = _velocity * dt;
    var distance = step.magnitude;

    if (distance > 0f)
    {
      var direction = step / distance;
      var hits = Physics.SphereCastAll(transform.position, _radius, direction, distance, ~0, QueryTriggerInteraction.Ignore);
      System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

      foreach (var hit in hits)
      {
        var col = hit.collider;

        if (_ownerColliders.Contains(col))
        {
          continue;
        }

        var player = col.GetComponentInParent<Player>();

        if (player != null)
        {
          if (player.IsInvincible)
          {
            continue; // esquiva: lo atraviesa
          }

          var point = hit.point == Vector3.zero ? transform.position : hit.point;
          HitPlayer(player, point);
          return;
        }

        if (((1 << col.gameObject.layer) & _hitMask.value) == 0)
        {
          continue;
        }

        if (_breaksBoxes)
        {
          var breakable = col.GetComponentInParent<NuiGrab.Breakable>();

          if (breakable != null && !breakable.OnlySword)
          {
            breakable.Break(hit.point, _velocity);
          }
        }

        var p = hit.point == Vector3.zero ? transform.position : hit.point;
        Impact(p, hit.normal, false);
        return;
      }

      transform.position += step;
      transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }
  }

  private void HitPlayer(Player player, Vector3 point)
  {
    var health = player.GetComponentInParent<Health>();

    if (health == null)
    {
      health = player.GetComponentInChildren<Health>();
    }

    var flat = _velocity;
    flat.y = 0f;
    flat = flat.sqrMagnitude > 0.0001f ? flat.normalized : player.transform.forward * -1f;

    if (health != null && !health.IsDead && !ContactDamage.IsInvulnerable(health))
    {
      if (_blockWithShield && player.IsBlocking && Vector3.Angle(player.transform.forward, -flat) <= _blockAngle * 0.5f)
      {
        player.NotifyBlocked();
        Push(player, flat, _knockback * 0.3f);
      }
      else
      {
        ContactDamage.SetInvulnerable(health, _playerInvulnerability);
        health.TakeDamage(_damage);
        Push(player, flat, _knockback);

        if (_cameraShake > 0f)
        {
          TopDownCamera.AddShake(_cameraShake);
        }
      }
    }

    Impact(point, -flat, true);
  }

  private static void Push(Player player, Vector3 direction, float force)
  {
    var rb = player.GetComponent<Rigidbody>();

    if (rb != null && !rb.isKinematic && force > 0f)
    {
      rb.AddForce(direction * force, ForceMode.VelocityChange);
    }
  }

  private void Impact(Vector3 point, Vector3 normal, bool hitPlayer)
  {
    if (_done)
    {
      return;
    }

    _done = true;

    if (_impactEffect != null)
    {
      var rotation = normal.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(normal) : Quaternion.identity;
      var fx = Instantiate(_impactEffect, point, rotation);

      // Seguridad: si no se destruye solo, a los 3 s.
      if (!fx.TryGetComponent<ParticleSystem>(out var ps) || ps.main.stopAction != ParticleSystemStopAction.Destroy)
      {
        Destroy(fx, 3f);
      }
    }

    if (_impactSound != null)
    {
      AudioSource.PlayClipAtPoint(_impactSound, point, _volume);
    }

    if (_detachOnImpact != null)
    {
      foreach (var ps in _detachOnImpact)
      {
        if (ps == null) continue;
        ps.transform.SetParent(null, true);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        var main = ps.main;
        Destroy(ps.gameObject, main.startLifetime.constantMax + 0.5f);
      }
    }

    Destroy(gameObject);
  }

  private void OnDrawGizmosSelected()
  {
    Gizmos.color = new Color(0.7f, 0.3f, 1f, 0.8f);
    Gizmos.DrawWireSphere(transform.position, _radius);
  }
}
