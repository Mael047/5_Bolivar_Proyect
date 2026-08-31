using UnityEngine;

public class TopDownCamera : MonoBehaviour
{
    [SerializeField] private Transform _target;
    [SerializeField, Min(0f)] private float _height = 10f;
    [SerializeField, Min(0f)] private float _distance = 5f;
    [SerializeField, Range(0f, 89f)] private float _tiltAngle = 70f;
    [SerializeField, Range(1f, 30f)] private float _smoothSpeed = 6f;

    private void Awake()
    {
        if (_target == null)
        {
            var player = FindAnyObjectByType<Player>();
            _target = player != null ? player.transform : null;
        }
    }

    private void LateUpdate()
    {
        if (_target == null)
        {
            return;
        }

        var rotation = Quaternion.Euler(_tiltAngle, 0f, 0f);
        var offset = rotation * (Vector3.back * _distance) + Vector3.up * _height;
        var desired = _target.position + offset;

        transform.position = Vector3.Lerp(transform.position, desired, _smoothSpeed * Time.deltaTime);
        transform.rotation = rotation;
    }
}
