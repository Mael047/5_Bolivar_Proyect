using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Z-Target (lock-on): al oprimir L1 con un enemigo dentro de _lockOnDistance, el
/// personaje queda fijado al enemigo mas cercano. El lock se mantiene hasta salir
/// del rango (soltar L1 deja de cubrirte pero SIGUES mirando al objetivo).
/// La rotacion real del cuerpo se decide por prioridad en PlayerLookAt:
/// mano > lock-on > movimiento.
/// </summary>
[RequireComponent(typeof(Player))]
public class PlayerLockOn : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Player _player;
    [SerializeField] private PlayerInput _playerInput;

    [Header("Lock-on")]
    [Tooltip("Distancia horizontal maxima (metros) para activar/validar el lock-on.")]
    [SerializeField, Min(0.1f)] private float _lockOnDistance = 8f;

    private bool _lockedOn;
    private Enemy _target;

    public bool IsLockedOn => _lockedOn;
    public Enemy Target => _target;
    public Transform TargetTransform => _target != null ? _target.transform : null;

    private void Awake()
    {
        if (_player == null)
        {
            _player = GetComponent<Player>();
        }

        if (_playerInput == null)
        {
            _playerInput = GetComponent<PlayerInput>();
        }

        var blockAction = _playerInput?.actions?.FindAction("Block", true);
        if (blockAction != null)
        {
            blockAction.started += OnBlockStarted;
        }
    }

    private void OnDestroy()
    {
        var blockAction = _playerInput?.actions?.FindAction("Block", true);
        if (blockAction != null)
        {
            blockAction.started -= OnBlockStarted;
        }
    }

    private void OnBlockStarted(InputAction.CallbackContext context)
    {
        TryEnterLockOn();
    }

    /// <summary>
    /// Si hay un enemigo en rango al pulsar L1, entra en lock-on y se fija al mas cercano.
    /// </summary>
    private void TryEnterLockOn()
    {
        if (_lockedOn)
        {
            return;
        }

        var target = FindNearestEnemy();
        if (target != null)
        {
            _lockedOn = true;
            _target = target;
        }
    }

    private void Update()
    {
        if (!_lockedOn)
        {
            return;
        }

        // Mientras este en lock-on, se mantiene al enemigo mas cercano en rango.
        // Si el actual muere o sale del rango, cambia al siguiente o se desactiva.
        var nearest = FindNearestEnemy();

        if (nearest == null)
        {
            _lockedOn = false;
            _target = null;
            return;
        }

        _target = nearest;
    }

    /// <summary>Enemigo (con componente <see cref="Enemy"/>) mas cercano dentro del rango horizontal.</summary>
    private Enemy FindNearestEnemy()
    {
        var colliders = Physics.OverlapSphere(transform.position, _lockOnDistance);
        Enemy best = null;
        var bestSqr = float.MaxValue;

        foreach (var c in colliders)
        {
            var enemy = c.GetComponentInParent<Enemy>();
            if (enemy == null)
            {
                continue;
            }

            var dir = enemy.transform.position - transform.position;
            dir.y = 0f;
            var sqr = dir.sqrMagnitude;

            if (sqr <= _lockOnDistance * _lockOnDistance && sqr < bestSqr)
            {
                best = enemy;
                bestSqr = sqr;
            }
        }

        return best;
    }

    private void OnDrawGizmos()
    {
        // Radio de lock-on (siempre visible para calibrar la distancia).
        Gizmos.color = new Color(1f, 0f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, _lockOnDistance);

        // Linea hacia el objetivo actual cuando hay lock-on activo.
        if (_lockedOn && _target != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(transform.position + Vector3.up, _target.transform.position + Vector3.up);
            Gizmos.DrawWireSphere(_target.transform.position + Vector3.up, 0.3f);
        }
    }
}
