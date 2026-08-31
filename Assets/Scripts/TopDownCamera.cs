using UnityEngine;

public class TopDownCamera : MonoBehaviour
{
    [SerializeField] private Transform _target;
    [SerializeField, Min(0f)] private float _height = 10f;
    [SerializeField, Min(0f)] private float _distance = 5f;
    [SerializeField, Range(0f, 89f)] private float _tiltAngle = 70f;
    [SerializeField, Range(1f, 30f)] private float _smoothSpeed = 6f;
    [Header("Z-Target")]
    [Tooltip("Fraccion (0..1) de cuanto se desplaza la camara hacia el objetivo del lock-on. 0 = no se mueve, 0.5 = mitad. Nunca llega a tirar del todo al enemigo para no sacar al jugador de plano.")]
    [SerializeField, Range(0f, 0.5f)] private float _lockOnFraming = 0.3f;

    private PlayerLockOn _lockOn;

    private void Awake()
    {
        if (_target == null)
        {
            var player = FindAnyObjectByType<Player>();
            _target = player != null ? player.transform : null;
        }

        if (_target != null)
        {
            _lockOn = _target.GetComponent<PlayerLockOn>();
        }
    }

    private void LateUpdate()
    {
        if (_target == null)
        {
            return;
        }

        // Al estar en lock-on, el foco se desplaza un poco hacia el objetivo
        // para ensenar mejor a quien miras, pero sin sacar al jugador del encuadre.
        var focus = _target.position;
        if (_lockOn != null && _lockOn.IsLockedOn && _lockOn.TargetTransform != null)
        {
            focus = Vector3.Lerp(_target.position, _lockOn.TargetTransform.position, _lockOnFraming);
        }

        var rotation = Quaternion.Euler(_tiltAngle, 0f, 0f);
        var offset = rotation * (Vector3.back * _distance) + Vector3.up * _height;
        var desired = focus + offset;

        transform.position = Vector3.Lerp(transform.position, desired, _smoothSpeed * Time.deltaTime);
        transform.rotation = rotation;
    }
}
