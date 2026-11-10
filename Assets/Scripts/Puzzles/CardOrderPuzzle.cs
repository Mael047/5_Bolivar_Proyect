// CardOrderPuzzle.cs
// Puzzle de las cartas sobre la mesa de billar.
//   - Solo se juega desde la vista cenital de la mesa (InteractiveCameraFocus):
//     ahi las cartas se pueden agarrar con la mano (camara) y soltar en los
//     huecos. Fuera de esa vista no se pueden tocar.
//   - Al entrar en la vista aparece una pista; se repite cada cierto tiempo.
//   - Si se llenan todos los huecos en un orden equivocado, aparece un aviso.
//   - Con el orden correcto: las cartas se bloquean, la camara vuelve a la
//     nutria y se sueltan las recompensas (escudo de Peru y llave) junto a la
//     mesa.
//
// Usa el sistema de puzzles existente (PuzzleManager / PuzzleSlot /
// PuzzlePiece). Tools > Nivel Casa > Billar: crear puzzle de cartas lo monta.

using System.Collections;
using NuiGrab;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[RequireComponent(typeof(PuzzleManager))]
public class CardOrderPuzzle : MonoBehaviour
{
  [Header("Referencias")]
  [SerializeField, Tooltip("Vista cenital de la mesa.")]
  private InteractiveCameraFocus _focus;
  [SerializeField] private PuzzleSlot[] _slots;
  [SerializeField] private PuzzlePiece[] _pieces;

  [Header("Pista")]
  [SerializeField, TextArea(2, 5)]
  private string _hint = "<b>El abuelo siempre ordenaba su mano igual</b>\n<size=22>Primero el rey... y al final, el as.</size>";
  [SerializeField, TextArea(2, 4)]
  private string _wrongOrder = "<b>Mmm... asi no las ordenaba</b>\n<size=22>De la carta mas alta a la mas baja.</size>";
  [SerializeField, TextArea(1, 3)]
  private string _solved = "<b>¡Asi era!</b>";
  [SerializeField, Tooltip("Interfaz del mensaje (prefab con WorldMessageView, por ejemplo DoorMessageUI).")]
  private WorldMessageView _messageView;
  [SerializeField, Tooltip("Punto sobre el que aparece el mensaje (vacio = el centro de este objeto).")]
  private Transform _messagePoint;
  [SerializeField, Min(1f)] private float _hintDuration = 6f;
  [SerializeField, Min(0f), Tooltip("Cada cuantos segundos se repite la pista mientras se mira la mesa (0 = solo al entrar).")]
  private float _hintRepeat = 25f;

  [Header("Recompensas")]
  [SerializeField, Tooltip("Lo que se suelta al resolverlo (escudo de Peru, llave...).")]
  private GameObject[] _rewards;
  [SerializeField, Tooltip("Donde aparece cada recompensa (si hay menos puntos que recompensas, se reparten).")]
  private Transform[] _rewardPoints;
  [SerializeField, Min(0f), Tooltip("Espera tras resolverlo antes de salir de la vista.")]
  private float _exitDelay = 1.2f;
  [SerializeField, Min(0f)] private float _rewardInterval = 0.4f;

  [Header("Sonidos")]
  [SerializeField] private AudioClip _wrongSound;
  [SerializeField] private AudioClip _solvedSound;

  [Header("Eventos")]
  public UnityEvent OnSolved = new UnityEvent();

  private PuzzleManager _manager;
  private HandGrabManager _grab;
  private bool _focused;
  private bool _wrongShown;
  private float _nextHint;

  public bool IsSolved => _manager != null && _manager.IsComplete;

  private void Awake()
  {
    _manager = GetComponent<PuzzleManager>();
    _grab = FindAnyObjectByType<HandGrabManager>();
    SetGrabbable(false);
  }

  private void OnEnable()
  {
    _manager.Completed += OnCompleted;

    if (_focus != null)
    {
      _focus.OnFocusStart.AddListener(OnFocusStart);
      _focus.OnFocusEnd.AddListener(OnFocusEnd);
    }
  }

  private void OnDisable()
  {
    _manager.Completed -= OnCompleted;

    if (_focus != null)
    {
      _focus.OnFocusStart.RemoveListener(OnFocusStart);
      _focus.OnFocusEnd.RemoveListener(OnFocusEnd);
    }
  }

  // ---------- Vista de la mesa ----------

  private void OnFocusStart()
  {
    _focused = true;

    if (IsSolved)
    {
      return;
    }

    SetGrabbable(true);
    // La pista sale cuando la camara ya llego a la mesa.
    _nextHint = Time.unscaledTime + 0.9f;
  }

  private void OnFocusEnd()
  {
    _focused = false;
    SetGrabbable(false);

    // Si se sale con una carta en la mano, la carta cae sobre la mesa.
    if (_grab != null && _grab.Held != null && _grab.Held.GetComponent<PuzzlePiece>() != null)
    {
      var body = _grab.Held;
      _grab.ForceRelease();
      body.isKinematic = false;
    }
  }

  private void Update()
  {
    if (!_focused || IsSolved)
    {
      return;
    }

    KeepHeldCardUpright();

    if (_nextHint > 0f && Time.unscaledTime >= _nextHint)
    {
      Say(_hint, _hintDuration);
      _nextHint = _hintRepeat > 0f ? Time.unscaledTime + _hintRepeat : 0f;
    }

    // Todos los huecos llenos pero en mal orden.
    var allFilled = _slots != null && _slots.Length > 0;

    if (_slots != null)
    {
      foreach (var slot in _slots)
      {
        if (slot == null || !slot.IsOccupied)
        {
          allFilled = false;
          break;
        }
      }
    }

    if (allFilled && !_wrongShown)
    {
      _wrongShown = true;
      Say(_wrongOrder, 4f);
      Play(_wrongSound);
      TopDownCamera.AddShake(0.1f);
    }
    else if (!allFilled)
    {
      _wrongShown = false;
    }
  }

  [Header("Cartas en la mano")]
  [SerializeField, Min(0f), Tooltip("Velocidad con la que la carta agarrada gira hasta quedar derecha.")]
  private float _uprightSpeed = 12f;

  /// <summary>La carta que se sostiene gira hasta quedar derecha (como en los huecos).</summary>
  private void KeepHeldCardUpright()
  {
    if (_grab == null || _grab.Held == null || _slots == null || _slots.Length == 0 || _slots[0] == null)
    {
      return;
    }

    var body = _grab.Held;
    var piece = body.GetComponent<PuzzlePiece>();

    if (piece == null || System.Array.IndexOf(_pieces, piece) < 0)
    {
      return;
    }

    var target = _slots[0].transform.rotation;
    var k = 1f - Mathf.Exp(-_uprightSpeed * Time.deltaTime);
    body.MoveRotation(Quaternion.Slerp(body.rotation, target, k));
  }

  // ---------- Resuelto ----------

  private void OnCompleted(PuzzleManager manager)
  {
    StartCoroutine(SolvedRoutine());
  }

  private IEnumerator SolvedRoutine()
  {
    SetGrabbable(false);
    Say(_solved, 2.5f);
    Play(_solvedSound);
    OnSolved.Invoke();

    yield return new WaitForSecondsRealtime(_exitDelay);

    if (_focus != null && _focus.IsFocused)
    {
      _focus.ExitFocus();

      while (_focus.IsFocused)
      {
        yield return null;
      }
    }

    if (_rewards == null)
    {
      yield break;
    }

    for (var i = 0; i < _rewards.Length; i++)
    {
      if (_rewards[i] == null) continue;

      var point = _rewardPoints != null && _rewardPoints.Length > 0 ? _rewardPoints[i % _rewardPoints.Length] : transform;
      Instantiate(_rewards[i], point.position + Vector3.up * 0.25f, Quaternion.identity);

      if (_rewardInterval > 0f)
      {
        yield return new WaitForSeconds(_rewardInterval);
      }
    }
  }

  // ---------- Utilidades ----------

  private void SetGrabbable(bool grabbable)
  {
    if (_pieces == null)
    {
      return;
    }

    foreach (var piece in _pieces)
    {
      if (piece != null) piece.GrabEnabled = grabbable && !piece.IsLocked;
    }
  }

  private void Say(string text, float seconds)
  {
    if (_messageView == null || string.IsNullOrEmpty(text))
    {
      return;
    }

    var point = _messagePoint != null ? _messagePoint.position : transform.position;
    WorldMessageView.Show(_messageView, text, point, seconds);
  }

  private void Play(AudioClip clip)
  {
    if (clip != null)
    {
      AudioSource.PlayClipAtPoint(clip, transform.position);
    }
  }
}
