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

    [Header("Debug (live)")]
    [SerializeField] private float _debugStickMagnitude;
    [SerializeField] private float _debugMag01;
    [SerializeField] private float _debugTargetSpeed;

    private Rigidbody rb;
    private Vector2 moveInput;

    /// <summary>
    /// Cuando es false (mano activa), PlayerLookAt controla el giro del cuerpo;
    /// este script solo mueve y no rota.
    /// </summary>
    public bool RotateToMovement { get; set; } = true;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public void OnMovement(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>();
    }

    private void FixedUpdate()
    {
        var input = Vector2.ClampMagnitude(moveInput, 1f);
        var magnitude = input.magnitude;

        var mag01 = Mathf.Clamp01(Mathf.InverseLerp(_deadzone, _maxAnalogMagnitude, magnitude));

        Vector3 moveDir = Vector3.zero;
        float targetSpeed = 0f;

        if (magnitude > _deadzone)
        {
            moveDir = new Vector3(input.x, 0f, input.y) / magnitude;
            targetSpeed = Mathf.Lerp(_walkSpeed, _runSpeed, _speedCurve.Evaluate(mag01));
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
}
