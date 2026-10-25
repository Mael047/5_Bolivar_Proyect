using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Z-Target (lock-on): al oprimir L1 (teclado: Q) con un enemigo en rango, el
/// personaje queda fijado a el. El lock se mantiene sobre ESE enemigo hasta
/// que muere, sale del rango o se suelta con R1 (teclado: T).
///
/// A quien se fija / se cambia:
///   - Mando: al fijar, el mas cercano. Con el lock activo, mover la palanca
///     derecha hacia otro enemigo cambia a ese (un toque por cambio).
///   - Raton: el enemigo al que apunta el cursor (si no apunta a ninguno, el
///     mas cercano). Con el lock activo, mover el raton sobre otro enemigo
///     cambia a ese.
/// Se usa el modo del ultimo dispositivo que se toco (raton o mando).
///
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

    [Header("Cambio de objetivo con mando")]
    [Tooltip("Cuanto hay que empujar la palanca derecha para cambiar de objetivo (0..1).")]
    [SerializeField, Range(0.3f, 1f)] private float _stickThreshold = 0.7f;

    [Tooltip("Angulo maximo (grados) entre la palanca y la direccion en pantalla hacia el otro enemigo.")]
    [SerializeField, Range(10f, 90f)] private float _stickConeAngle = 60f;

    [Header("Apuntar con raton")]
    [Tooltip("Distancia maxima (pixeles a 1080p) entre el cursor y un enemigo para considerarlo apuntado.")]
    [SerializeField, Min(10f)] private float _mouseRadius = 160f;

    [Tooltip("Altura (m) del punto del enemigo que se compara con el cursor / la palanca.")]
    [SerializeField] private float _aimHeight = 1f;

    private bool _lockedOn;
    private Enemy _target;
    private Enemy _candidate;
    private InputAction _unlockAction;
    private bool _ownsUnlockAction;

    private bool _usingMouse;
    private bool _stickArmed = true;
    private Camera _camera;

    private static readonly Collider[] s_overlap = new Collider[64];
    private readonly System.Collections.Generic.List<Enemy> _inRange = new System.Collections.Generic.List<Enemy>();

    public bool IsLockedOn => _lockedOn;
    public Enemy Target => _target;

    /// <summary>
    /// Enemigo que se fijaria si se pulsa L1 ahora mismo. Null mientras hay
    /// lock-on activo o si no hay nadie en rango. Lo usa LockOnIndicator.
    /// </summary>
    public Enemy Candidate => _candidate;

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

        // Soltar el objetivo. Si el asset de input no tiene la accion, se crea aqui.
        _unlockAction = _playerInput?.actions?.FindAction("Unlock", false);
        if (_unlockAction == null)
        {
            _unlockAction = new InputAction("Unlock", InputActionType.Button);
            _unlockAction.AddBinding("<Gamepad>/rightShoulder");
            _unlockAction.AddBinding("<Keyboard>/t");
            _unlockAction.Enable();
            _ownsUnlockAction = true;
        }
        _unlockAction.performed += OnUnlockPerformed;
    }

    private void OnDestroy()
    {
        var blockAction = _playerInput?.actions?.FindAction("Block", true);
        if (blockAction != null)
        {
            blockAction.started -= OnBlockStarted;
        }

        if (_unlockAction != null)
        {
            _unlockAction.performed -= OnUnlockPerformed;
            if (_ownsUnlockAction) _unlockAction.Dispose();
        }
    }

    private void OnUnlockPerformed(InputAction.CallbackContext context)
    {
        // Con menus abiertos el input del jugador esta apagado; la accion
        // propia (si se creo) no, asi que se comprueba aqui tambien.
        if (MenusOpen())
        {
            return;
        }

        ReleaseLock();
    }

    /// <summary>Suelta el objetivo actual (el enemigo pasa a ser solo candidato).</summary>
    public void ReleaseLock()
    {
        _lockedOn = false;
        _target = null;
    }

    private void OnBlockStarted(InputAction.CallbackContext context)
    {
        TryEnterLockOn();
    }

    /// <summary>Si hay un enemigo en rango al pulsar L1, entra en lock-on (raton: el apuntado).</summary>
    private void TryEnterLockOn()
    {
        if (_lockedOn)
        {
            return;
        }

        CollectInRange();
        var target = PickDefault();

        if (target != null)
        {
            _lockedOn = true;
            _target = target;
            _candidate = null;
        }
    }

    private void Update()
    {
        if (_camera == null || !_camera.isActiveAndEnabled)
        {
            _camera = Camera.main;
        }

        DetectDevice();
        CollectInRange();

        if (!_lockedOn)
        {
            _candidate = PickDefault();
            return;
        }

        _candidate = null;

        // El objetivo actual se mantiene mientras siga vivo y en rango.
        if (!IsValid(_target))
        {
            _target = Nearest();

            if (_target == null)
            {
                _lockedOn = false;
                return;
            }
        }

        if (MenusOpen())
        {
            return;
        }

        if (_usingMouse)
        {
            SwitchWithMouse();
        }
        else
        {
            SwitchWithStick();
        }
    }

    // ---------- Cambio de objetivo ----------

    private void SwitchWithStick()
    {
        var pad = Gamepad.current;

        if (pad == null || _camera == null)
        {
            return;
        }

        var stick = pad.rightStick.ReadValue();

        // Un cambio por empujon: hay que soltar la palanca para volver a cambiar.
        if (stick.magnitude < 0.35f)
        {
            _stickArmed = true;
            return;
        }

        if (!_stickArmed || stick.magnitude < _stickThreshold)
        {
            return;
        }

        _stickArmed = false;

        var from = ScreenPoint(_target);
        var dir = stick.normalized;
        var cosLimit = Mathf.Cos(_stickConeAngle * Mathf.Deg2Rad);
        Enemy best = null;
        var bestScore = float.MaxValue;

        foreach (var enemy in _inRange)
        {
            if (enemy == _target)
            {
                continue;
            }

            var to = ScreenPoint(enemy);
            var delta = to - from;
            var distance = delta.magnitude;

            if (distance < 1f)
            {
                continue;
            }

            var cos = Vector2.Dot(delta / distance, dir);

            if (cos < cosLimit)
            {
                continue;
            }

            // Prefiere el mas alineado con la palanca y, entre esos, el mas cerca.
            var score = distance / cos;

            if (score < bestScore)
            {
                bestScore = score;
                best = enemy;
            }
        }

        if (best != null)
        {
            _target = best;
        }
    }

    private void SwitchWithMouse()
    {
        var mouse = Mouse.current;

        // Solo cambia cuando el raton se mueve: si no, se quedaria pegado al
        // enemigo mas cercano al cursor aunque el jugador no lo este apuntando.
        if (mouse == null || mouse.delta.ReadValue().sqrMagnitude < 4f)
        {
            return;
        }

        var aimed = Aimed();

        if (aimed != null)
        {
            _target = aimed;
        }
    }

    // ---------- Seleccion ----------

    /// <summary>Raton: el apuntado (o el mas cercano). Mando: el mas cercano.</summary>
    private Enemy PickDefault()
    {
        if (_usingMouse)
        {
            var aimed = Aimed();

            if (aimed != null)
            {
                return aimed;
            }
        }

        return Nearest();
    }

    /// <summary>Enemigo en rango mas cercano al cursor en pantalla (dentro del radio).</summary>
    private Enemy Aimed()
    {
        var mouse = Mouse.current;

        if (mouse == null || _camera == null)
        {
            return null;
        }

        var cursor = mouse.position.ReadValue();
        var radius = _mouseRadius * (Screen.height / 1080f);
        Enemy best = null;
        var bestSqr = radius * radius;

        foreach (var enemy in _inRange)
        {
            var sqr = (ScreenPoint(enemy) - cursor).sqrMagnitude;

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = enemy;
            }
        }

        return best;
    }

    private Enemy Nearest()
    {
        Enemy best = null;
        var bestSqr = float.MaxValue;

        foreach (var enemy in _inRange)
        {
            var dir = enemy.transform.position - transform.position;
            dir.y = 0f;
            var sqr = dir.sqrMagnitude;

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = enemy;
            }
        }

        return best;
    }

    /// <summary>Enemigos (vivos) dentro del rango horizontal, sin repetidos.</summary>
    private void CollectInRange()
    {
        _inRange.Clear();
        var count = Physics.OverlapSphereNonAlloc(transform.position, _lockOnDistance, s_overlap);

        for (var i = 0; i < count; i++)
        {
            var enemy = s_overlap[i].GetComponentInParent<Enemy>();

            if (enemy != null && !_inRange.Contains(enemy) && IsValid(enemy))
            {
                _inRange.Add(enemy);
            }
        }
    }

    private bool IsValid(Enemy enemy)
    {
        if (enemy == null || !enemy.isActiveAndEnabled)
        {
            return false;
        }

        var health = enemy.GetComponent<Health>();

        if (health != null && health.IsDead)
        {
            return false;
        }

        var dir = enemy.transform.position - transform.position;
        dir.y = 0f;
        return dir.sqrMagnitude <= _lockOnDistance * _lockOnDistance;
    }

    private Vector2 ScreenPoint(Enemy enemy)
    {
        var p = _camera.WorldToScreenPoint(enemy.transform.position + Vector3.up * _aimHeight);
        return p.z > 0f ? (Vector2)p : new Vector2(-10000f, -10000f);
    }

    /// <summary>El ultimo dispositivo que se toco decide el modo (raton o mando).</summary>
    private void DetectDevice()
    {
        var mouse = Mouse.current;

        if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 9f || mouse.leftButton.wasPressedThisFrame))
        {
            _usingMouse = true;
        }

        var pad = Gamepad.current;

        if (pad != null &&
            (pad.leftStick.ReadValue().sqrMagnitude > 0.1f || pad.rightStick.ReadValue().sqrMagnitude > 0.1f ||
             pad.buttonSouth.isPressed || pad.buttonWest.isPressed || pad.leftShoulder.isPressed))
        {
            _usingMouse = false;
        }
    }

    private static bool MenusOpen()
    {
        return Menus.PauseMenu.IsOpen || Menus.MainMenu.IsOpen || Collectibles.CollectibleInspector.IsOpen;
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
