using System;
using System.Collections;
using System.Collections.Generic;
using NuiGrab;
using UnityEngine;

public class PuzzleManager : MonoBehaviour
{
    [SerializeField] private PuzzleSlot[] _slots = Array.Empty<PuzzleSlot>();
    [SerializeField] private HandGrabManager _grabManager;
    [SerializeField] private bool _showGhostPreviews = true;
    [SerializeField] private GameObject _rewardPrefab;
    [SerializeField] private Transform _rewardSpawnPoint;
    [SerializeField, Min(0.01f)] private float _snapDuration = 0.15f;

    public event Action<PuzzleManager> Completed;

    public bool IsComplete { get; private set; }

    private readonly HashSet<PuzzlePiece> _snapping = new HashSet<PuzzlePiece>();

    private void Awake()
    {
        if (_grabManager == null)
        {
            _grabManager = FindAnyObjectByType<HandGrabManager>();
        }
    }

    private void OnEnable()
    {
        if (_grabManager != null)
        {
            _grabManager.PickedUp += HandlePickedUp;
            _grabManager.DropInterceptor += HandleDropInterceptor;
        }
    }

    private void OnDisable()
    {
        if (_grabManager != null)
        {
            _grabManager.PickedUp -= HandlePickedUp;
            _grabManager.DropInterceptor -= HandleDropInterceptor;
        }
    }

    private void Start()
    {
        ApplyGhostSettings();
    }

    public void SetGhostPreviewsEnabled(bool show)
    {
        _showGhostPreviews = show;
        ApplyGhostSettings();
    }

    private void ApplyGhostSettings()
    {
        foreach (var slot in _slots)
        {
            if (slot == null)
            {
                continue;
            }

            slot.GhostsEnabled = _showGhostPreviews;
            slot.UpdateGhost();
        }
    }

    private void HandlePickedUp(Rigidbody body)
    {
        var piece = body != null ? body.GetComponent<PuzzlePiece>() : null;

        if (piece == null || piece.CurrentSlot == null || IsComplete)
        {
            return;
        }

        piece.CurrentSlot.Clear(piece);
        piece.CurrentSlot = null;
    }

    private bool HandleDropInterceptor(Rigidbody body)
    {
        var piece = body != null ? body.GetComponent<PuzzlePiece>() : null;

        if (piece == null || IsComplete || piece.IsLocked || piece.IsSnapping || _snapping.Contains(piece))
        {
            return false;
        }

        var nearest = FindNearestFreeSlot(piece, body.position);

        if (nearest == null)
        {
            return false;
        }

        StartCoroutine(SnapRoutine(piece, nearest));
        return true;
    }

    private PuzzleSlot FindNearestFreeSlot(PuzzlePiece piece, Vector3 position)
    {
        PuzzleSlot nearest = null;
        var nearestSqr = float.MaxValue;

        foreach (var slot in _slots)
        {
            if (slot == null || !slot.CanAccept(piece))
            {
                continue;
            }

            var delta = slot.transform.position - position;
            delta.y = 0f;
            var sqr = delta.sqrMagnitude;

            if (sqr < nearestSqr && sqr <= slot.SnapRadius * slot.SnapRadius)
            {
                nearestSqr = sqr;
                nearest = slot;
            }
        }

        return nearest;
    }

    private IEnumerator SnapRoutine(PuzzlePiece piece, PuzzleSlot slot)
    {
      _snapping.Add(piece);
      piece.IsSnapping = true;

      var body = piece.Body;

      // Cero velocidades solo si el cuerpo esta dinamico: asignarlas a un
      // cuerpo kinematico genera warning en Unity 6.
      if (!body.isKinematic)
      {
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
      }

      body.isKinematic = true;

      // Sin colisiones durante el vuelo: evita empujones/explosiones con piezas
      // vecinas o pedestales y que algo desvie la pieza de su ancla.
      var colliders = piece.GetComponentsInChildren<Collider>();

      foreach (var collider in colliders)
      {
        collider.isTrigger = true;
      }

      var startPos = body.position;
      var startRot = body.rotation;
      // La pieza termina con la orientacion del slot (la misma del fantasma).
      var endPos = slot.transform.position;
      var endRot = slot.transform.rotation;
      var duration = Mathf.Max(_snapDuration, 0.001f);
      var t = 0f;

      while (t < 1f && !piece.IsLocked)
      {
        t += Time.deltaTime / duration;
        var eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
        body.MovePosition(Vector3.Lerp(startPos, endPos, eased));
        body.MoveRotation(Quaternion.Slerp(startRot, endRot, eased));
        yield return null;
      }

      body.position = endPos;
      body.rotation = endRot;

      foreach (var collider in colliders)
      {
        collider.isTrigger = false;
      }

      piece.IsSnapping = false;
      _snapping.Remove(piece);

      if (!piece.IsLocked)
      {
        // El cuerpo sigue kinematico tras el vuelo: no toca velocidades aqui.
        slot.Fill(piece);
        CheckCompletion();
      }
    }

    private void CheckCompletion()
    {
        if (IsComplete)
        {
            return;
        }

        foreach (var slot in _slots)
        {
            if (slot == null || !slot.IsCorrect)
            {
                return;
            }
        }

        IsComplete = true;
        Debug.Log("¡Puzzle completado!");
        SpawnReward();

        foreach (var slot in _slots)
        {
            if (slot != null && slot.Occupant != null)
            {
                slot.Occupant.Lock();
            }
        }

        Completed?.Invoke(this);
    }

    private void SpawnReward()
    {
        if (_rewardPrefab == null)
        {
            Debug.LogWarning("PuzzleManager: reward prefab no asignado");
            return;
        }

        var position = _rewardSpawnPoint != null
            ? _rewardSpawnPoint.position
            : transform.position + Vector3.up * 1.5f;

        Instantiate(_rewardPrefab, position, Quaternion.identity);
    }
}
