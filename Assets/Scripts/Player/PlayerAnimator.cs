using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerAnimator : MonoBehaviour
{
    [SerializeField] private Animator _animator;
    [SerializeField, Min(0f)] private float _runSpeed = 6f;

    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();

        if (_animator == null)
        {
            _animator = GetComponentInChildren<Animator>();
        }
    }

    private void FixedUpdate()
    {
        if (_animator == null || _rb == null)
        {
            return;
        }

        var horizontalSpeed = new Vector2(_rb.linearVelocity.x, _rb.linearVelocity.z).magnitude;
        _animator.SetFloat("Speed", Mathf.Clamp01(horizontalSpeed / _runSpeed));
    }
}
