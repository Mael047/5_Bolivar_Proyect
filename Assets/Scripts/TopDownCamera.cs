using UnityEngine;

public class TopDownCamera : MonoBehaviour
{
    [SerializeField] private Transform _target;
    [SerializeField, Min(0f)] private float _height = 10f;
    [SerializeField, Min(0f)] private float _distance = 5f;
    [SerializeField, Range(0f, 89f)] private float _tiltAngle = 35f;
    [SerializeField, Range(0f, 89f)] private float _Angle = 40f;
    [SerializeField, Range(1f, 30f)] private float _smoothSpeed = 6f;

    [Header("Position Offset")]
    [Tooltip("Ajuste fino manual de la cámara en los ejes X, Y, Z")]
    [SerializeField] private Vector3 _positionOffset = Vector3.zero;

    [Header("Z-Target")]
    [Tooltip("Fraccion (0..1) de cuanto se desplaza la camara hacia el objetivo del lock-on. 0 = no se mueve, 0.5 = mitad. Nunca llega a tirar del todo al enemigo para no sacar al jugador de plano.")]
    [SerializeField, Range(0f, 0.5f)] private float _lockOnFraming = 0.3f;

    [Header("Camera Shake")]
    [Tooltip("Apaga todos los temblores de camara (accesibilidad / pruebas).")]
    [SerializeField] private bool _shakeEnabled = true;
    [Tooltip("Desplazamiento maximo en metros con trauma = 1.")]
    [SerializeField, Min(0f)] private float _shakeMaxOffset = 0.35f;
    [Tooltip("Giro maximo (roll) en grados con trauma = 1.")]
    [SerializeField, Min(0f)] private float _shakeMaxRoll = 2.5f;
    [Tooltip("Rapidez de la vibracion. Valores altos = temblor nervioso, bajos = balanceo.")]
    [SerializeField, Min(0.1f)] private float _shakeFrequency = 22f;
    [Tooltip("Trauma que se pierde por segundo. Mas alto = temblores mas cortos.")]
    [SerializeField, Min(0.01f)] private float _shakeRecovery = 1.8f;
    [Tooltip("Multiplicador global de todos los temblores.")]
    [SerializeField, Min(0f)] private float _shakeGlobalMultiplier = 1f;

    [Header("Impact Kick & Zoom")]
    [Tooltip("Rigidez del resorte que devuelve la camara tras un kick/zoom de impacto. Mas alto = vuelve mas rapido.")]
    [SerializeField, Min(1f)] private float _impactStiffness = 160f;
    [Tooltip("Amortiguacion del resorte (1 = sin rebote, <1 = rebota un poco).")]
    [SerializeField, Range(0.2f, 1.5f)] private float _impactDamping = 0.65f;
    [Tooltip("Multiplicador global de kicks y zooms de impacto.")]
    [SerializeField, Min(0f)] private float _impactGlobalMultiplier = 1f;

    private static TopDownCamera _instance;

    private Camera _camera;
    private float _baseOrthoSize;
    private float _baseFov;
    private Vector3 _kickOffset;
    private Vector3 _kickVelocity;
    private float _zoom;
    private float _zoomVelocity;

    /// <summary>
    /// Empuje direccional: la camara se desplaza <paramref name="meters"/> en
    /// <paramref name="worldDirection"/> y vuelve con un resorte.
    /// </summary>
    public static void AddKick(Vector3 worldDirection, float meters)
    {
        if (_instance == null) return;
        // Impulso para que el pico del resorte sea ~meters.
        var k = _instance._impactStiffness;
        _instance._kickVelocity += worldDirection * (meters * _instance._impactGlobalMultiplier * Mathf.Sqrt(k) * 1.6f);
    }

    /// <summary>
    /// Zoom de impacto: acerca la camara una fraccion (0.08 = 8 %) y vuelve.
    /// </summary>
    public static void AddZoomPunch(float fraction)
    {
        if (_instance == null) return;
        var k = _instance._impactStiffness;
        _instance._zoomVelocity += fraction * _instance._impactGlobalMultiplier * Mathf.Sqrt(k) * 1.6f;
    }

    private PlayerLockOn _lockOn;
    private Vector3 _smoothedPosition;
    private float _trauma;
    private float _shakeTime;
    private float _seed;

    /// <summary>
    /// Suma trauma (0..1) a la camara activa. El temblor visible es trauma^2,
    /// asi que varios golpes pequenos se acumulan de forma natural.
    /// </summary>
    public static void AddShake(float trauma)
    {
        if (_instance != null)
        {
            _instance.AddTrauma(trauma);
        }
    }

    public void AddTrauma(float trauma)
    {
        if (!_shakeEnabled)
        {
            return;
        }

        _trauma = Mathf.Clamp01(_trauma + trauma * _shakeGlobalMultiplier);
    }

    private void OnEnable()
    {
        _instance = this;
    }

    private void OnDisable()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    private void Awake()
    {
        _smoothedPosition = transform.position;
        _seed = Random.value * 100f;

        _camera = GetComponent<Camera>();
        if (_camera != null)
        {
            _baseOrthoSize = _camera.orthographicSize;
            _baseFov = _camera.fieldOfView;
        }

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

        var rotation = Quaternion.Euler(_tiltAngle, _Angle, 0f);

        // Renombrado a baseOffset para evitar confusiones
        var baseOffset = rotation * (Vector3.back * _distance) + Vector3.up * _height;

        // Sumamos tu nuevo _positionOffset al cálculo final
        var desired = focus + baseOffset + _positionOffset;

        // El suavizado trabaja sobre una posicion "limpia"; el temblor se suma
        // encima al final para que no contamine el Lerp del siguiente frame.
        _smoothedPosition = Vector3.Lerp(_smoothedPosition, desired, _smoothSpeed * Time.deltaTime);

        var shakeOffset = Vector3.zero;
        var shakeRoll = 0f;

        if (_trauma > 0f)
        {
            // Tiempo real: la camara sigue temblando durante el hit-stop.
            _shakeTime += Time.unscaledDeltaTime * _shakeFrequency;

            var amount = _trauma * _trauma;
            var nx = Mathf.PerlinNoise(_seed, _shakeTime) * 2f - 1f;
            var ny = Mathf.PerlinNoise(_seed + 17.3f, _shakeTime) * 2f - 1f;
            var nr = Mathf.PerlinNoise(_seed + 41.7f, _shakeTime) * 2f - 1f;

            // Solo en el plano de la pantalla: no acerca/aleja la camara.
            shakeOffset = rotation * new Vector3(nx, ny, 0f) * (_shakeMaxOffset * amount);
            shakeRoll = nr * _shakeMaxRoll * amount;

            _trauma = Mathf.Max(0f, _trauma - _shakeRecovery * Time.unscaledDeltaTime);
        }

        // Resortes de impacto (tiempo real: se ven durante el hit-stop).
        var dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        var stiffness = _impactStiffness;
        var damping = 2f * Mathf.Sqrt(stiffness) * _impactDamping;

        _kickVelocity += (-stiffness * _kickOffset - damping * _kickVelocity) * dt;
        _kickOffset += _kickVelocity * dt;

        _zoomVelocity += (-stiffness * _zoom - damping * _zoomVelocity) * dt;
        _zoom += _zoomVelocity * dt;

        if (_camera != null)
        {
            var zoomFactor = 1f - Mathf.Clamp(_zoom, -0.5f, 0.5f);
            if (_camera.orthographic)
            {
                _camera.orthographicSize = _baseOrthoSize * zoomFactor;
            }
            else
            {
                _camera.fieldOfView = _baseFov * zoomFactor;
            }
        }

        transform.position = _smoothedPosition + shakeOffset + _kickOffset;
        transform.rotation = rotation * Quaternion.Euler(0f, 0f, shakeRoll);
    }
}