// ShieldPedestal.cs
// Poste del puzzle de escudos. Al acercarse aparece el aviso de interaccion
// ("F / X  Colocar escudo"); al pulsarlo se abre un menu con los escudos que
// lleva la nutria. Se puede colocar uno, cambiarlo por otro o retirarlo (si
// esta en el poste equivocado). Cuando los 5 estan en su sitio, el
// PuzzleManager da el puzzle por resuelto (recompensa, guardado...) y los
// postes ya no se pueden tocar.
//
// Va en cada poste, junto a su PuzzleSlot. Tools > Puzzle escudos > Montar
// lo pone y lo conecta solo.

using System.Collections;
using Collectibles;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(PuzzleSlot))]
public class ShieldPedestal : MonoBehaviour, IInteractable
{
  [Header("Referencias")]
  [SerializeField] private ShieldCatalog _catalog;

  [SerializeField, Tooltip("Menu para elegir el escudo (prefab ShieldSelectUI).")]
  private ShieldSelectMenu _menuPrefab;

  [SerializeField, Tooltip("Puzzle al que pertenece. Vacio = el del padre.")]
  private PuzzleManager _manager;

  [Header("Interaccion")]
  [SerializeField, Min(0.1f)] private float _interactRadius = 2.2f;
  [SerializeField, Tooltip("Altura del aviso sobre el poste.")]
  private float _promptHeight = 2.2f;
  [SerializeField] private string _promptEmpty = "Colocar escudo";
  [SerializeField] private string _promptFull = "Cambiar escudo";
  [SerializeField, Tooltip("Aviso propio (vacio = el general).")]
  private CollectiblePromptView _customPromptView;

  [Header("Mensajes")]
  [SerializeField, Tooltip("Interfaz del mensaje (DoorMessageUI).")]
  private WorldMessageView _messageView;
  [SerializeField, TextArea(2, 4)]
  private string _noShieldsMessage = "<b>No tienes escudos</b>\n<size=22>Busca los escudos y vuelve aqui.</size>";
  [SerializeField, TextArea(2, 4)]
  private string _wrongOrderMessage = "<b>Algo no encaja...</b>\n<size=22>Algun escudo no esta en su poste.</size>";
  [SerializeField, Min(0.5f)] private float _messageDuration = 3f;

  [Header("Sonidos")]
  [SerializeField] private AudioClip _placeSound;
  [SerializeField] private AudioClip _removeSound;
  [SerializeField, Range(0f, 1f)] private float _volume = 0.9f;

  private PuzzleSlot _slot;

  /// <summary>Clave del poste para el guardado.</summary>
  public string Key => gameObject.scene.name + ":" + gameObject.name;

  /// <summary>Escudo colocado ahora (null si esta vacio).</summary>
  public string CurrentId => ShieldInventory.GetPlaced(Key);

  public ShieldCatalog Catalog => _catalog;

  private bool Solved => _manager != null && _manager.IsComplete;

  // ---------- IInteractable ----------

  public bool CanInteract => isActiveAndEnabled && !Solved && !ShieldSelectMenu.IsOpen;

  public Vector3 PromptPoint => transform.position + Vector3.up * _promptHeight;

  public string PromptText => CurrentId != null ? _promptFull : _promptEmpty;

  public CollectiblePromptView CustomPromptView => _customPromptView;

  public bool IsInRange(Vector3 playerPosition, out float sqrDistance)
  {
    var delta = playerPosition - transform.position;
    var height = Mathf.Abs(delta.y);
    delta.y = 0f;
    sqrDistance = delta.sqrMagnitude;
    return sqrDistance <= _interactRadius * _interactRadius && height <= 3f;
  }

  public void Interact(PlayerInput playerInput)
  {
    if (!CanInteract)
    {
      return;
    }

    if (CurrentId == null && ShieldInventory.Held().Count == 0)
    {
      ShowMessage(_noShieldsMessage);
      return;
    }

    var menu = ShieldSelectMenu.Get(_menuPrefab);

    if (menu == null)
    {
      Debug.LogWarning("[ShieldPedestal] Falta el menu de escudos (ShieldSelectUI).", this);
      return;
    }

    menu.Open(this, playerInput);
  }

  // ---------- Ciclo ----------

  private void Awake()
  {
    _slot = GetComponent<PuzzleSlot>();

    if (_manager == null)
    {
      _manager = GetComponentInParent<PuzzleManager>();
    }
  }

  private void OnEnable() => InteractableRegistry.Register(this);

  private void OnDisable() => InteractableRegistry.Unregister(this);

  private IEnumerator Start()
  {
    // Un frame despues: los escudos ya hicieron su Start.
    yield return null;
    var id = CurrentId;

    if (id != null)
    {
      ShowPiece(id);
    }
  }

  // ---------- Colocar / retirar ----------

  /// <summary>Coloca un escudo del inventario (si habia otro, vuelve al inventario).</summary>
  public void Place(string id)
  {
    if (Solved || !ShieldInventory.Remove(id))
    {
      return;
    }

    var previous = CurrentId;

    if (previous != null)
    {
      HidePiece(previous);
      ShieldInventory.Add(previous);
    }

    ShieldInventory.SetPlaced(Key, id);
    ShowPiece(id);
    Play(_placeSound);

    if (_manager != null)
    {
      _manager.Evaluate();

      if (!_manager.IsComplete && _manager.AllSlotsFilled)
      {
        ShowMessage(_wrongOrderMessage);
      }
    }
  }

  /// <summary>Retira el escudo del poste y lo devuelve al inventario.</summary>
  public void Remove()
  {
    var id = CurrentId;

    if (Solved || id == null)
    {
      return;
    }

    HidePiece(id);
    ShieldInventory.SetPlaced(Key, null);
    ShieldInventory.Add(id);
    Play(_removeSound);
  }

  private void ShowPiece(string id)
  {
    var pickup = FindPickup(id);

    if (pickup == null)
    {
      Debug.LogWarning("[ShieldPedestal] No hay ningun escudo '" + id + "' en la escena para mostrar en el poste.", this);
      return;
    }

    pickup.ShowOnPole(transform);

    var piece = pickup.GetComponent<PuzzlePiece>();

    if (piece != null && _slot != null)
    {
      if (_slot.Occupant != null && _slot.Occupant != piece) _slot.Clear(_slot.Occupant);
      _slot.Fill(piece);
    }
  }

  private void HidePiece(string id)
  {
    var pickup = FindPickup(id);

    if (pickup == null)
    {
      return;
    }

    var piece = pickup.GetComponent<PuzzlePiece>();

    if (piece != null && _slot != null)
    {
      _slot.Clear(piece);
      piece.CurrentSlot = null;
    }

    pickup.HideFromPole();
  }

  private ShieldPickup FindPickup(string id)
  {
    foreach (var p in FindObjectsByType<ShieldPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
    {
      if (p.gameObject.scene == gameObject.scene && string.Equals(p.Id, id, System.StringComparison.OrdinalIgnoreCase))
      {
        return p;
      }
    }

    return null;
  }

  private void ShowMessage(string text)
  {
    if (_messageView != null && !string.IsNullOrEmpty(text))
    {
      WorldMessageView.Show(_messageView, text, PromptPoint, _messageDuration);
    }
  }

  private void Play(AudioClip clip)
  {
    if (clip != null)
    {
      AudioSource.PlayClipAtPoint(clip, transform.position, _volume);
    }
  }

  private void OnDrawGizmosSelected()
  {
    Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.8f);
    Gizmos.DrawWireSphere(transform.position, _interactRadius);
  }
}
