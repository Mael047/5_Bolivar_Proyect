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

        var horizontalVelocity = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);

        var moveZ = Vector3.Dot(horizontalVelocity, transform.forward) / _runSpeed;
        var moveX = Vector3.Dot(horizontalVelocity, transform.right) / _runSpeed;

        _animator.SetFloat("MoveZ", moveZ);
        _animator.SetFloat("MoveX", moveX);
    }
}
