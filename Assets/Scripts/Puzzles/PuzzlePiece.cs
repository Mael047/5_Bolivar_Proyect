using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PuzzlePiece : MonoBehaviour
{
    [SerializeField] private string _pieceId;

    private Rigidbody _rb;

    public string PieceId => _pieceId != null ? _pieceId.Trim() : string.Empty;
    public bool IsLocked { get; private set; }
    public bool IsSnapping { get; internal set; }
    public PuzzleSlot CurrentSlot { get; internal set; }

    public Rigidbody Body => _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    public void Lock()
    {
        if (IsLocked)
        {
            return;
        }

        IsLocked = true;
        IsSnapping = false;

        // Las piezas bloqueadas ya suelen venir kinematicas (snap); asignar
        // velocidades a un cuerpo kinematico genera warning en Unity 6.
        if (!_rb.isKinematic)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        _rb.isKinematic = true;
    }

    private void OnValidate()
    {
        if (_pieceId != null)
        {
            _pieceId = _pieceId.Trim();
        }
    }
}
