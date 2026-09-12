using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class Player : MonoBehaviour
{
    [Header("Speeds")]
    [SerializeField] private float _walkSpeed = 2f;
    [SerializeField] private float _runSpeed = 6f;
    [SerializeField] private AnimationCurve _speedCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.2f, 0.15f),
        new Keyframe(0.4f, 0.35f),
        new Keyframe(0.6f, 0.6f),
        new Keyframe(0.8f, 0.85f),
        new Keyframe(1f, 1f));

    [Header("Feel")]
    [SerializeField, Min(0f)] private float _deadzone = 0.05f;
    [SerializeField, Range(0.5f, 1f)] private float _maxAnalogMagnitude = 0.85f;
    [SerializeField, Range(0.1f, 40f)] private float _acceleration = 12f;
    [SerializeField, Range(0.1f, 40f)] private float _friction = 8f;
    [SerializeField, Range(1f, 30f)] private float _rotationSpeed = 12f;
    [SerializeField, Range(0.1f, 1f)] private float _backwardSpeedFactor = 0.6f;

    [Header("Combat")]
    [Tooltip("Multiplicador de velocidad de movimiento mientras ataca (0 = quieto, 1 = velocidad normal).")]
    [SerializeField, Range(0f, 1f)] private float _attackSpeedFactor = 0.4f;
    [Tooltip("Multiplicador de velocidad de movimiento mientras se cubre con el escudo (0 = quieto, 1 = velocidad normal).")]
    [SerializeField, Range(0f, 1f)] private float _blockSpeedFactor = 0.5f;
    [SerializeField] private Animator _animator;

    [Header("Debug (live)")]
    [SerializeField] private float _debugStickMagnitude;
    [SerializeField] private float _debugMag01;
    [SerializeField] private float _debugTargetSpeed;

    [Header("Dodge")]
    [SerializeField] private float _dodgeForce = 15f; // Fuerza del empuje
    [SerializeField] private float _dodgeDuration = 0.5f; // Cuánto dura el empuje
    [SerializeField] private float _dodgeCooldown = 1f; // Tiempo de espera entre esquivas

    [Tooltip("Velocidad de empuje automático hacia adelante durante el ataque.")]
    [SerializeField] private float _attackLungeSpeed = 3f;

    private bool _isDodging;
    private float _dodgeTimer;
    private float _dodgeCooldownTimer;
    private Vector3 _dodgeDirection;

    private Rigidbody rb;
    private Vector2 moveInput;
    private PlayerLookAt _lookAt;
    private PlayerInput _playerInput;
    private bool _isAttacking;
    private bool _hasEnteredAttackState;
    private bool _blockHeld;

    [SerializeField] private MeleeHitbox playerSwordHitbox;
    [SerializeField] private Collider playerCollider;

    public void AE_StartAttack()
    {
        if (playerSwordHitbox != null) playerSwordHitbox.EnableHitbox();
    }

    public void AE_EndAttack()
    {
        if (playerSwordHitbox != null) playerSwordHitbox.DisableHitbox();
    }

    public bool IsInvincible { get; private set; }

    // Llama a este evento de animación al inicio del Dodge
    public void AE_StartInvincibility()
    {
        IsInvincible = true;
    }

    // Llama a este evento de animación al final del Dodge
    public void AE_EndInvincibility()
    {
        IsInvincible = false;
    }

    /// <summary>
    /// Cuando es false (mano activa), PlayerLookAt controla el giro del cuerpo;
    /// este script solo mueve y no rota.
    /// </summary>
    public bool RotateToMovement { get; set; } = true;

    /// <summary>
    /// Cuando es true (p.ej. durante un ataque), el personaje no se mueve ni rota.
    /// </summary>
    public bool IsBusy { get; set; }

    /// <summary>
    /// True mientras el personaje esta ejecutando un ataque. Durante ese tiempo
    /// la velocidad de movimiento se multiplica por _attackSpeedFactor.
    /// </summary>
    public bool IsAttacking => _isAttacking;

    /// <summary>
    /// True mientras el personaje se cubre con el escudo (L1 sostenido). No se
    /// cubre durante un ataque: atacar cancela el block hasta terminar el ataque.
    /// </summary>
    public bool IsBlocking =>
        _blockHeld && !_isAttacking && !IsInAttackingState() && !IsBusy;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        _lookAt = GetComponent<PlayerLookAt>();
        _playerInput = GetComponent<PlayerInput>();

        if (_animator == null)
        {
            _animator = GetComponentInChildren<Animator>();
        }

        var attackAction = _playerInput?.actions?.FindAction("Attack", true);
        if (attackAction != null)
        {
            attackAction.performed += OnAttackPerformed;
        }

        var blockAction = _playerInput?.actions?.FindAction("Block", true);
        if (blockAction != null)
        {
            blockAction.started += OnBlockStarted;
            blockAction.canceled += OnBlockCanceled;
        }

        var dodgeAction = _playerInput?.actions?.FindAction("Dodge", true);
        if (dodgeAction != null)
        {
            dodgeAction.started += OnDodgePerformed;
        }
    }

    private void OnDestroy()
    {
        var attackAction = _playerInput?.actions?.FindAction("Attack", true);
        if (attackAction != null)
        {
            attackAction.performed -= OnAttackPerformed;
        }

        var blockAction = _playerInput?.actions?.FindAction("Block", true);
        if (blockAction != null)
        {
            blockAction.started -= OnBlockStarted;
            blockAction.canceled -= OnBlockCanceled;
        }

        var dodgeAction = _playerInput?.actions?.FindAction("Dodge", true);
        if (dodgeAction != null)
        {
            dodgeAction.started -= OnDodgePerformed;
        }
    }

    /// <summary>
    /// Se dispara una sola vez por pulsacion (flanco ascendente) gracias al
    /// callback "performed" de la accion Attack. Cada pulsacion re-arma el
    /// trigger del Animator: si el personaje ya esta en Attack1, pasa a Attack2
    /// (combo), y al terminar vuelve a Locomotion.
    /// </summary>
    private void OnAttackPerformed(InputAction.CallbackContext context)
    {
        TriggerAttack();
    }

    /// <summary>Se dispara al presionar L1 (flanco ascendente): comienza a cubrirse.</summary>
    private void OnBlockStarted(InputAction.CallbackContext context)
    {
        _blockHeld = true;
    }

    /// <summary>Se dispara al soltar L1: deja de cubrirse.</summary>
    private void OnBlockCanceled(InputAction.CallbackContext context)
    {
        _blockHeld = false;
    }

    /// <summary>Llamado por PlayerInput (modo Send Messages) con la accion "Move".</summary>
    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    /// <summary>Dispara un golpe. El AnimatorController resuelve la secuencia de combo.</summary>
    public void TriggerAttack()
    {
        if (_animator == null)
        {
            return;
        }

        if (_lookAt != null)
        {
            _lookAt.SetBusy(true);
        }

        _animator.SetTrigger("Attack");
    }

    private void OnDodgePerformed(InputAction.CallbackContext context)
    {
        // No esquivar si está ocupado o si el cooldown sigue activo
        if (IsBusy || _dodgeCooldownTimer > 0f) return;

        if (_lookAt != null) _lookAt.SetBusy(true);
        if (_animator != null) _animator.SetTrigger("Dodge");

        StartDodge();
    }

    private void StartDodge()
    {
        _isDodging = true;
        _dodgeTimer = _dodgeDuration;
        _dodgeCooldownTimer = _dodgeCooldown; // Reinicia el temporizador aquí

        if (moveInput.sqrMagnitude > 0.1f)
        {
            Transform camTransform = Camera.main.transform;
            Vector3 camForward = camTransform.forward;
            Vector3 camRight = camTransform.right;
            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();
            _dodgeDirection = (camRight * moveInput.x + camForward * moveInput.y).normalized;

            transform.rotation = Quaternion.LookRotation(_dodgeDirection, Vector3.up);
        }
        else
        {
            _dodgeDirection = transform.forward;
        }
    }

    private void Update()
    {
        var attacking = IsInAttackingState();

        // Flujo constante del cooldown
        if (_dodgeCooldownTimer > 0f)
        {
            _dodgeCooldownTimer -= Time.deltaTime;
        }

        // Manejo del tiempo del dodge
        if (_isDodging)
        {
            _dodgeTimer -= Time.deltaTime;
            if (_dodgeTimer <= 0f)
            {
                _isDodging = false;
            }
        }

        _isAttacking = attacking;
        IsBusy = attacking || _isDodging;

        if (_animator != null)
        {
            _animator.SetLayerWeight(1, IsBlocking ? 1f : 0f);
        }

        if (!IsBusy)
        {
            if (_hasEnteredAttackState)
            {
                _hasEnteredAttackState = false;
                if (_lookAt != null) _lookAt.SetBusy(false);
            }
            return;
        }

        _hasEnteredAttackState = true;
    }

    private bool IsInAttackingState()
    {
        if (_animator == null)
        {
            return false;
        }

        // Comprueba tanto el estado actual como el siguiente durante una
        // transicion, para detectar el inicio/fin del ataque de forma robusta.
        var current = _animator.GetCurrentAnimatorStateInfo(0);
        var next = _animator.GetNextAnimatorStateInfo(0);

        return IsAttackName(current) || IsAttackName(next);
    }

    private static bool IsAttackName(AnimatorStateInfo info)
    {
        return info.IsName("Attack1") || info.IsName("Attack2");
    }

    private void FixedUpdate()
    {
        // 1. LÓGICA DE EMPUJE DEL DODGE
        if (_isDodging)
        {
            rb.linearVelocity = new Vector3(_dodgeDirection.x * _dodgeForce, rb.linearVelocity.y, _dodgeDirection.z * _dodgeForce);
            return;
        }

        // 2. LÓGICA DE IMPULSO DE ATAQUE
        if (_isAttacking)
        {
            // Empuja suavemente al jugador en la dirección que está mirando
            Vector3 lunge = transform.forward * _attackLungeSpeed;
            rb.linearVelocity = new Vector3(lunge.x, rb.linearVelocity.y, lunge.z);
            return;
        }

        // 3. BLOQUEO PARA OTRAS ACCIONES (Si añades usos de objetos, curaciones, etc.)
        if (IsBusy && !_isAttacking && !_isDodging)
        {
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            return;
        }

        var input = Vector2.ClampMagnitude(moveInput, 1f);
        var magnitude = input.magnitude;

        var mag01 = Mathf.Clamp01(Mathf.InverseLerp(_deadzone, _maxAnalogMagnitude, magnitude));

        Vector3 moveDir = Vector3.zero;
        float targetSpeed = 0f;

        if (magnitude > _deadzone)
        {
            Transform camTransform = Camera.main.transform;

            Vector3 camForward = camTransform.forward;
            Vector3 camRight = camTransform.right;

            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();

            moveDir = (camRight * input.x + camForward * input.y) / magnitude;
            targetSpeed = Mathf.Lerp(_walkSpeed, _runSpeed, _speedCurve.Evaluate(mag01));

            if (IsInAttackingState())
            {
                targetSpeed *= _attackSpeedFactor;
            }
            else if (IsBlocking)
            {
                targetSpeed *= _blockSpeedFactor;
            }
        }

        if (moveDir.sqrMagnitude > 0.001f)
        {
            var backwardAmount = Mathf.Clamp01(-Vector3.Dot(moveDir, transform.forward));
            targetSpeed *= Mathf.Lerp(1f, _backwardSpeedFactor, backwardAmount);
        }

        _debugStickMagnitude = magnitude;
        _debugMag01 = mag01;
        _debugTargetSpeed = targetSpeed;

        var targetVel = new Vector3(moveDir.x * targetSpeed, rb.linearVelocity.y, moveDir.z * targetSpeed);
        var smoothing = targetSpeed > 0f ? _acceleration : _friction;
        var k = 1f - Mathf.Exp(-smoothing * Time.fixedDeltaTime);

        rb.linearVelocity = new Vector3(
            Mathf.Lerp(rb.linearVelocity.x, targetVel.x, k),
            rb.linearVelocity.y,
            Mathf.Lerp(rb.linearVelocity.z, targetVel.z, k));

        if (RotateToMovement && moveDir.sqrMagnitude > 0.001f)
        {
            var targetRotation = Quaternion.LookRotation(moveDir, Vector3.up);
            var rotSpeed = _rotationSpeed * (0.3f + 0.7f * mag01);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotSpeed * Time.deltaTime);
        }
    }

    private bool IsInDodgeState()
    {
        if (_animator == null) return false;
        var current = _animator.GetCurrentAnimatorStateInfo(0);
        var next = _animator.GetNextAnimatorStateInfo(0);
        // Cambia "Dodge" si tu estado en el Animator se llama diferente
        return current.IsName("Dodge") || next.IsName("Dodge");
    }

}
