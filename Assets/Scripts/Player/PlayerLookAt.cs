// PlayerLookAt.cs
// Un unico escritor de rotacion: mientras la mano este detectada, el cuerpo gira
// hacia el cursor (Player.cs desactiva su rotacion de movimiento via RotateToMovement).
// La cabeza/ojos siguen la mano con el look-at humanoide del Animator (se suma al
// clip). El giro del cuerpo se hace en FixedUpdate con MoveRotation para respetar
// la interpolacion del Rigidbody y evitar temblor.

using NuiGrab;
using UnityEngine;

public class PlayerLookAt : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HandTracker _handTracker;
    [SerializeField] private Camera _camera;

    [Header("Body turn")]
    [SerializeField, Min(0f)] private float _turnSpeed = 300f;

    [Header("Look target")]
    [SerializeField] private float _floorY = 0f;
    [SerializeField, Min(0f)] private float _lookHeightRange = 2.5f;
    [SerializeField, Min(0f)] private float _lookDistanceMax = 20f;
    [SerializeField, Min(0f)] private float _lookSmoothing = 15f;

    [Header("Look weights")]
    [SerializeField, Range(0f, 1f)] private float _headWeight = 1f;
    [SerializeField, Range(0f, 1f)] private float _eyeWeight = 0.6f;
    [SerializeField, Range(0f, 1f)] private float _clamp = 0.9f;
    [SerializeField, Min(0f)] private float _weightSmoothing = 8f;

    private Player _player;
    private PlayerLockOn _lockOn;
    private Rigidbody _rb;
    private Animator _animator;
    private Vector3 _lookTarget;
    private Vector3 _cursorTarget;
    private float _lookWeight;

    /// <summary>Direccion horizontal a la que apunta el cuerpo (fiable en FixedUpdate).</summary>
    public Vector3 Facing { get; private set; }

    private bool HasHand => _handTracker != null && _handTracker.IsHandDetected;

    private bool _busy;

    public void SetBusy(bool busy)
    {
        _busy = busy;
    }

    private void Awake()
    {
        _player = GetComponent<Player>();
        _lockOn = GetComponent<PlayerLockOn>();
        _rb = GetComponent<Rigidbody>();
        _animator = GetComponentInChildren<Animator>();
        Facing = transform.forward;

        if (_camera == null)
        {
            _camera = Camera.main;
        }

        if (_handTracker == null)
        {
            _handTracker = FindAnyObjectByType<HandTracker>();
        }
    }

    private void Update()
    {
        if (_player != null)
        {
            // Prioridad de rotacion: si hay mano o lock-on, el Player no rota al
            // movimiento (PlayerLookAt escribe la rotacion del cuerpo).
            var lockOnActive = _lockOn != null && _lockOn.IsLockedOn;
            _player.RotateToMovement = !HasHand && !lockOnActive;
        }
    }

    private void FixedUpdate()
    {
        if (_busy)
        {
            Facing = transform.forward;
            return;
        }

        // Prioridad mano > lock-on.
        if (HasHand)
        {
            _cursorTarget = ComputeCursor();
            RotateTowards(_cursorTarget);
        }
        else if (_lockOn != null && _lockOn.IsLockedOn && _lockOn.TargetTransform != null)
        {
            RotateTowards(_lockOn.TargetTransform.position);
        }

        Facing = transform.forward;
    }

    private void RotateTowards(Vector3 worldPoint)
    {
        if (_rb == null)
        {
            return;
        }

        var toTarget = worldPoint - transform.position;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude > 0.0001f)
        {
            var targetRotation = Quaternion.LookRotation(toTarget, Vector3.up);
            _rb.MoveRotation(Quaternion.RotateTowards(transform.rotation, targetRotation, _turnSpeed * Time.deltaTime));
        }
    }

    private void LateUpdate()
    {
        var hasHand = HasHand;
        _lookWeight = Mathf.MoveTowards(_lookWeight, hasHand ? 1f : 0f, _weightSmoothing * Time.deltaTime);

        if (hasHand)
        {
            var vp = _handTracker.HandViewportPosition;
            var desired = _cursorTarget + Vector3.up * (vp.y - 0.5f) * _lookHeightRange;
            var k = 1f - Mathf.Exp(-_lookSmoothing * Time.deltaTime);
            _lookTarget = Vector3.Lerp(_lookTarget, desired, k);
        }

        Facing = transform.forward;
    }

    // El Animator evalua el IK humanoide aqui. Las llamadas SetLookAt* solo son
    // validas dentro de este callback, no en LateUpdate/Update.
    private void OnAnimatorIK(int layerIndex)
    {
        if (_animator == null)
        {
            return;
        }

        _animator.SetLookAtPosition(_lookTarget);
        _animator.SetLookAtWeight(_lookWeight, 0f, _headWeight * _lookWeight, _eyeWeight * _lookWeight, _clamp);
    }

    private Vector3 ComputeCursor()
    {
        var vp = _handTracker.HandViewportPosition;
        var ray = _camera.ScreenPointToRay(new Vector3(vp.x * _camera.pixelWidth, vp.y * _camera.pixelHeight, 0f));
        var plane = new Plane(Vector3.up, new Vector3(0f, _floorY, 0f));

        if (plane.Raycast(ray, out var distance) && distance > 0f)
        {
            return ray.GetPoint(Mathf.Min(distance, _lookDistanceMax));
        }

        return transform.position;
    }
}
