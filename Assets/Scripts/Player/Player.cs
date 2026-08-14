using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class Player : MonoBehaviour
{
    [SerializeField] private float _moveSpeed = 5f;
    [SerializeField, Range(1f, 30f)] private float _rotationSpeed = 12f;

    private Rigidbody rb;
    private Vector2 moveInput;

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
        var dir = Vector2.ClampMagnitude(moveInput, 1f);
        var move = new Vector3(dir.x, 0f, dir.y);

        rb.linearVelocity = new Vector3(move.x * _moveSpeed, rb.linearVelocity.y, move.z * _moveSpeed);

        if (move.sqrMagnitude > 0.001f)
        {
            var targetRotation = Quaternion.LookRotation(move, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, _rotationSpeed * Time.deltaTime);
        }
    }
}
