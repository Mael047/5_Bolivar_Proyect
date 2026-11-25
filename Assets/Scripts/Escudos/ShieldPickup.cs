// ShieldPickup.cs
// Escudo del puzzle que se recoge al pasar por encima: se guarda en el
// inventario de escudos (ShieldInventory) y aparece en el HUD. Ya no se
// agarra con la mano.
//
// El mismo objeto es el que se ve en el poste cuando se coloca ahi
// (ShieldPedestal lo mueve y lo muestra), por eso se oculta en vez de
// destruirse.
//
// Va en cada escudo (junto a PuzzlePiece). Tools > Puzzle escudos > Montar
// lo pone solo.

using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class ShieldPickup : MonoBehaviour
{
  [SerializeField, Tooltip("Id del escudo. Vacio = el Piece Id de su PuzzlePiece.")]
  private string _shieldId;

  [Header("Recoger")]
  [SerializeField, Min(0.1f), Tooltip("Distancia (horizontal) a la que se recoge.")]
  private float _pickupRadius = 1.3f;

  [SerializeField, Tooltip("Flota y gira un poco mientras esta en el suelo.")]
  private bool _idleMotion = true;

  [SerializeField, Min(0f)] private float _bobHeight = 0.08f;
  [SerializeField] private float _spinSpeed = 40f;

  [Header("Aviso")]
  [SerializeField, Tooltip("Catalogo (para el nombre del escudo en el mensaje).")]
  private ShieldCatalog _catalog;

  [SerializeField, TextArea(2, 4), Tooltip("{0} = nombre del escudo.")]
  private string _message = "<b>Escudo de {0}</b>\n<size=22>Guardado en tu inventario</size>";

  [SerializeField, Tooltip("Interfaz del mensaje (DoorMessageUI).")]
  private WorldMessageView _messageView;

  [SerializeField, Min(0.5f)] private float _messageDuration = 2.5f;

  [Header("Efectos")]
  [SerializeField] private AudioClip _pickupSound;
  [SerializeField, Range(0f, 1f)] private float _volume = 0.9f;

  public UnityEvent OnPickedUp = new UnityEvent();

  /// <summary>Esta colocado en un poste (no se recoge ni flota).</summary>
  public bool OnPole { get; private set; }

  public string Id
  {
    get
    {
      if (!string.IsNullOrEmpty(_shieldId)) return _shieldId.Trim();
      var piece = GetComponent<PuzzlePiece>();
      return piece != null ? piece.PieceId : "";
    }
  }

  private string PickupKey => gameObject.scene.name + ":" + Id;

  private Transform _player;
  private Vector3 _basePosition;
  private Quaternion _baseRotation;
  private Vector3 _baseScale;
  private bool _collecting;
  private float _phase;

  private void Awake()
  {
    _baseScale = transform.localScale;
    _basePosition = transform.position;
    _baseRotation = transform.rotation;
    _phase = Random.value * 10f;

    // Ya no es un objeto fisico agarrable: fuera de la capa Grabbable y quieto.
    foreach (var t in GetComponentsInChildren<Transform>(true))
    {
      if (t.gameObject.layer == 8) t.gameObject.layer = 0;
    }

    var body = GetComponent<Rigidbody>();
    if (body != null) body.isKinematic = true;

    foreach (var c in GetComponentsInChildren<Collider>(true))
    {
      c.isTrigger = true; // se atraviesa al recogerlo
    }
  }

  private void Start()
  {
    // Si esta en un poste, el poste lo muestra (ShieldPedestal). Si ya se
    // recogio (aqui o en otra escena), no se ve en el suelo.
    if (!OnPole && !ShieldInventory.IsPlaced(Id) && (ShieldInventory.WasPicked(PickupKey) || ShieldInventory.Has(Id)))
    {
      gameObject.SetActive(false);
    }
  }

  private void Update()
  {
    if (OnPole || _collecting)
    {
      return;
    }

    if (_idleMotion)
    {
      _phase += Time.deltaTime;
      transform.position = _basePosition + Vector3.up * (Mathf.Sin(_phase * 2f) * _bobHeight + _bobHeight);
      transform.rotation = Quaternion.AngleAxis(_phase * _spinSpeed, Vector3.up) * _baseRotation;
    }

    // Puesto en un poste de otra forma (partida cargada): no se recoge.
    if (ShieldInventory.IsPlaced(Id))
    {
      return;
    }

    if (_player == null)
    {
      var go = GameObject.FindWithTag("Player");
      _player = go != null ? go.transform : null;
      if (_player == null) return;
    }

    var delta = _player.position - _basePosition;
    var height = Mathf.Abs(delta.y);
    delta.y = 0f;

    if (delta.sqrMagnitude <= _pickupRadius * _pickupRadius && height < 2.5f)
    {
      StartCoroutine(Collect());
    }
  }

  private IEnumerator Collect()
  {
    _collecting = true;
    ShieldInventory.MarkPicked(PickupKey);
    ShieldInventory.Add(Id);

    if (_pickupSound != null)
    {
      AudioSource.PlayClipAtPoint(_pickupSound, transform.position, _volume);
    }

    if (_messageView != null && !string.IsNullOrEmpty(_message))
    {
      var name = _catalog != null ? _catalog.NameOf(Id) : Id;
      WorldMessageView.Show(_messageView, string.Format(_message, name), transform.position + Vector3.up * 1.8f, _messageDuration);
    }

    OnPickedUp.Invoke();

    // Sube girando y se encoge.
    var start = transform.position;
    var t = 0f;

    while (t < 0.45f)
    {
      t += Time.deltaTime;
      var k = Mathf.Clamp01(t / 0.45f);
      transform.position = start + Vector3.up * (k * 1.2f);
      transform.rotation = Quaternion.AngleAxis(720f * Time.deltaTime, Vector3.up) * transform.rotation;
      transform.localScale = _baseScale * Mathf.Lerp(1.15f, 0f, k * k);
      yield return null;
    }

    _collecting = false;
    transform.localScale = _baseScale;
    gameObject.SetActive(false);
  }

  /// <summary>Lo pone en el poste (posicion y giro del hueco).</summary>
  public void ShowOnPole(Transform anchor)
  {
    StopAllCoroutines();
    _collecting = false;
    OnPole = true;
    gameObject.SetActive(true);
    transform.localScale = _baseScale;
    transform.SetPositionAndRotation(anchor.position, anchor.rotation);
  }

  /// <summary>Lo quita del poste (vuelve al inventario: no se ve).</summary>
  public void HideFromPole()
  {
    OnPole = false;
    gameObject.SetActive(false);
  }

  private void OnDrawGizmosSelected()
  {
    Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.8f);
    Gizmos.DrawWireSphere(Application.isPlaying ? _basePosition : transform.position, _pickupRadius);
  }
}
