// SaveBench.cs
// Banco de descanso (como una hoguera): con F / X la nutria se sienta y
//   - se guarda la partida (escena, banco, vida, espada/escudo, llaves,
//     coleccionables y lo que ya se hizo en el mundo),
//   - recupera toda la vida,
//   - las cajas normales rotas vuelven a aparecer (las que tenian espada,
//     escudo, llave o coleccionable no),
//   - si muere, reaparece aqui (en "Spawn Point").
//
// Animacion: cuando exista, escribe el nombre del trigger del Animator en
// "Sit Trigger" (por ejemplo "Sit") y ajusta "Sit Duration".

using System.Collections;
using Collectibles;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class SaveBench : MonoBehaviour, IInteractable
{
  [Header("Identidad")]
  [SerializeField, Tooltip("Id unico del banco (vacio = se usa su ruta en la escena).")]
  private string _id = "";

  [Header("Interaccion")]
  [SerializeField, Min(0.1f)] private float _interactRadius = 1.8f;
  [SerializeField] private Vector3 _interactOffset = Vector3.zero;
  [SerializeField] private float _promptHeight = 1.6f;
  [SerializeField] private string _promptText = "Descansar";
  [SerializeField, Tooltip("Aviso propio (vacio = el de los coleccionables).")]
  private CollectiblePromptView _customPromptView;

  [Header("Reaparecer")]
  [SerializeField, Tooltip("Donde aparece la nutria al morir o al cargar la partida (vacio = delante del banco).")]
  private Transform _spawnPoint;

  [Header("Descanso")]
  [SerializeField, Tooltip("Trigger del Animator para sentarse (vacio = sin animacion, por ahora).")]
  private string _sitTrigger = "";
  [SerializeField, Min(0f), Tooltip("Segundos sentada (sin control) antes de poder moverse.")]
  private float _sitDuration = 1.2f;
  [SerializeField] private bool _healOnRest = true;
  [SerializeField, Tooltip("Las cajas normales rotas vuelven a aparecer.")]
  private bool _respawnBoxes = true;

  [Header("Mensaje")]
  [SerializeField, TextArea(1, 3)] private string _savedMessage = "<b>Partida guardada</b>";
  [SerializeField, Min(0.5f)] private float _messageDuration = 2f;
  [SerializeField, Tooltip("Interfaz del mensaje (prefab con WorldMessageView). Se asigna sola con DoorMessageUI.")]
  private WorldMessageView _messageView;
  [SerializeField] private AudioClip _restSound;
  [SerializeField, Range(0f, 1f)] private float _volume = 0.9f;

  [Header("Eventos")]
  public UnityEvent OnRest = new UnityEvent();

  private bool _resting;
  private string _cachedId;

  /// <summary>Id del banco (se calcula al empezar, antes de que nada cambie la jerarquia).</summary>
  public string Id
  {
    get
    {
      if (string.IsNullOrEmpty(_cachedId))
      {
        _cachedId = string.IsNullOrEmpty(_id) ? WorldState.IdOf(this, "banco") : _id;
      }

      return _cachedId;
    }
  }

  private void Awake() => _ = Id;

  /// <summary>Donde aparece la nutria (mirando hacia fuera del banco).</summary>
  public Pose SpawnPose
  {
    get
    {
      if (_spawnPoint != null)
      {
        return new Pose(_spawnPoint.position, Quaternion.Euler(0f, _spawnPoint.eulerAngles.y, 0f));
      }

      var forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
      if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
      return new Pose(transform.position + forward * 1.2f + Vector3.up * 0.1f, Quaternion.LookRotation(forward));
    }
  }

  // ---------- IInteractable ----------

  public bool CanInteract => isActiveAndEnabled && !_resting;

  public Vector3 PromptPoint => transform.TransformPoint(_interactOffset) + Vector3.up * _promptHeight;

  public string PromptText => _promptText;

  public CollectiblePromptView CustomPromptView => _customPromptView;

  public bool IsInRange(Vector3 playerPosition, out float sqrDistance)
  {
    var delta = playerPosition - transform.TransformPoint(_interactOffset);
    var height = Mathf.Abs(delta.y);
    delta.y = 0f;
    sqrDistance = delta.sqrMagnitude;
    return sqrDistance <= _interactRadius * _interactRadius && height <= 2.5f;
  }

  public void Interact(PlayerInput playerInput)
  {
    if (CanInteract)
    {
      StartCoroutine(Rest(playerInput));
    }
  }

  private void OnEnable() => InteractableRegistry.Register(this);

  private void OnDisable() => InteractableRegistry.Unregister(this);

  // ---------- Descansar ----------

  private IEnumerator Rest(PlayerInput playerInput)
  {
    _resting = true;

    var player = playerInput != null ? playerInput.GetComponent<Player>() : FindAnyObjectByType<Player>();

    if (playerInput != null)
    {
      playerInput.DeactivateInput();
    }

    if (player != null && !string.IsNullOrEmpty(_sitTrigger))
    {
      var animator = player.GetComponentInChildren<Animator>();
      if (animator != null) animator.SetTrigger(_sitTrigger);
    }

    if (_restSound != null)
    {
      AudioSource.PlayClipAtPoint(_restSound, transform.position, _volume);
    }

    if (_healOnRest && player != null)
    {
      var health = player.GetComponentInParent<Health>();
      if (health == null) health = player.GetComponentInChildren<Health>();
      if (health != null) health.SetHealth(health.MaxHealth);
    }

    if (_respawnBoxes && WorldState.Current != null)
    {
      WorldState.Current.RespawnBreakables();
    }

    GameState.SaveAtBench(Id);

    if (_messageView != null && !string.IsNullOrEmpty(_savedMessage))
    {
      WorldMessageView.Show(_messageView, _savedMessage, PromptPoint, _messageDuration);
    }

    OnRest.Invoke();

    if (_sitDuration > 0f)
    {
      yield return new WaitForSeconds(_sitDuration);
    }

    if (playerInput != null)
    {
      playerInput.ActivateInput();
    }

    _resting = false;
  }

  private void OnDrawGizmosSelected()
  {
    Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.8f);
    Gizmos.DrawWireSphere(transform.TransformPoint(_interactOffset), _interactRadius);
    var pose = SpawnPose;
    Gizmos.DrawSphere(pose.position, 0.15f);
    Gizmos.DrawLine(pose.position, pose.position + pose.rotation * Vector3.forward * 0.8f);
  }

#if UNITY_EDITOR
  private void OnValidate()
  {
    if (_messageView != null || Application.isPlaying)
    {
      return;
    }

    foreach (var guid in UnityEditor.AssetDatabase.FindAssets("DoorMessageUI t:Prefab"))
    {
      var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));

      if (go != null && go.TryGetComponent<WorldMessageView>(out var view))
      {
        _messageView = view;
        UnityEditor.EditorUtility.SetDirty(this);
        break;
      }
    }
  }
#endif
}
