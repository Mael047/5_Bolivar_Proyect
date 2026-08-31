using UnityEngine;

public class PuzzleSlot : MonoBehaviour
{
    [SerializeField] private string _acceptedPieceId;
    [SerializeField, Min(0f)] private float _snapRadius = 0.4f;
    [SerializeField] private GameObject _ghostPreview;

    public string AcceptedPieceId => _acceptedPieceId != null ? _acceptedPieceId.Trim() : string.Empty;
    public PuzzlePiece Occupant { get; private set; }
    public bool GhostsEnabled { get; set; } = true;
    public float SnapRadius => _snapRadius;
    public bool IsOccupied => Occupant != null;
    public bool IsCorrect => IsOccupied && Occupant.PieceId == _acceptedPieceId;

    public bool CanAccept(PuzzlePiece piece)
    {
        return !IsOccupied && !piece.IsLocked && !piece.IsSnapping;
    }

    public void Fill(PuzzlePiece piece)
    {
        Occupant = piece;
        piece.CurrentSlot = this;
        UpdateGhost();
    }

    public void Clear(PuzzlePiece piece)
    {
        if (Occupant != piece)
        {
            return;
        }

        Occupant = null;
        UpdateGhost();
    }

    public void SetGhost(GameObject ghost)
    {
        _ghostPreview = ghost;
        UpdateGhost();
    }

    public void UpdateGhost()
    {
        if (_ghostPreview == null)
        {
            return;
        }

        var shouldShow = GhostsEnabled && !IsOccupied;

        if (_ghostPreview.activeSelf != shouldShow)
        {
            _ghostPreview.SetActive(shouldShow);
        }
    }

    private void OnValidate()
    {
        if (_acceptedPieceId != null)
        {
            _acceptedPieceId = _acceptedPieceId.Trim();
        }
    }
}
