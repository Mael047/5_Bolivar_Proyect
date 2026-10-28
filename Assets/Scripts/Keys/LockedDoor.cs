// LockedDoor.cs
// Puerta cerrada con llave. Usa el mismo aviso y el mismo boton que los
// coleccionables ("F / X  Abrir"):
//   - Con llave del tipo correcto: se gasta, el candado salta y la puerta se
//     abre (se hunde en el suelo, se desliza, gira o desaparece).
//   - Sin llave: la puerta tiembla, suena cerrada y aparece un mensaje sobre
//     ella ("Cerrada con llave...").
//
// Uso: ponlo en la puerta (o en un objeto padre). "Door" es lo que se mueve;
// para girar como bisagra, haz que el pivote de "Door" este en el borde
// (un objeto vacio padre en la bisagra).
// El mensaje es el prefab Assets/UI/Puertas/DoorMessageUI (se diseña libre).

using System.Collections;
using Collectibles;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class LockedDoor : MonoBehaviour, IInteractable
{
  public enum OpenMode
  {
    [Tooltip("Se mueve 'Slide Offset' (por defecto se hunde en el suelo).")]
    Deslizar,
    [Tooltip("Gira 'Hinge Angle' grados sobre el pivote de 'Door'.")]
    Girar,
    [Tooltip("Se encoge y desaparece.")]
    Desaparecer
  }

  [Header("Llave")]
  [SerializeField, Tooltip("Tipo de llave que pide. Vacio = cualquier llave pequeña generica.")]
  private string _keyId = "";
  [SerializeField, Tooltip("Se abre sola al llegar con llave, sin pulsar nada.")]
  private bool _openAutomaticallyWithKey;

  [Header("Interaccion")]
  [SerializeField, Min(0.1f), Tooltip("Distancia a la que aparece el aviso.")]
  private float _interactRadius = 2f;
  [SerializeField, Tooltip("Desplazamiento local del punto de interaccion (centro del radio y del aviso).")]
  private Vector3 _interactOffset = Vector3.zero;
  [SerializeField, Tooltip("Altura del aviso y del mensaje sobre el punto de interaccion.")]
  private float _promptHeight = 2.2f;
  [SerializeField] private string _promptText = "Abrir";
  [SerializeField, Tooltip("Aviso propio para esta puerta (vacio = el de los coleccionables).")]
  private CollectiblePromptView _customPromptView;

  [Header("Mensaje sin llave")]
  [SerializeField, TextArea(2, 5), Tooltip("Admite rich text: <b>negrita</b>, <size=22>tamaño</size>, <color=#E8C66A>color</color>.")]
  private string _lockedMessage = "<b>Cerrada con llave</b>\n<size=22>Necesitas una llave para abrirla.</size>";
  [SerializeField, Min(0.5f)] private float _messageDuration = 2.5f;
  [SerializeField, Tooltip("Interfaz del mensaje (prefab con WorldMessageView). Se asigna sola con DoorMessageUI.")]
  private WorldMessageView _messageView;

  [Header("Apertura")]
  [SerializeField, Tooltip("Lo que se mueve. Vacio = este objeto.")]
  private Transform _door;
  [SerializeField] private OpenMode _openMode = OpenMode.Deslizar;
  [SerializeField, Tooltip("Desplazamiento local al deslizar (negativo en Y = se hunde).")]
  private Vector3 _slideOffset = new Vector3(0f, -3.5f, 0f);
  [SerializeField, Tooltip("Grados al girar (eje vertical).")]
  private float _hingeAngle = 100f;
  [SerializeField, Min(0.05f)] private float _openDuration = 1.2f;
  [SerializeField, Min(0f), Tooltip("Espera entre quitar el candado y abrir.")]
  private float _unlockDelay = 0.45f;
  [SerializeField, Tooltip("Apaga los colliders de la puerta al terminar de abrirse.")]
  private bool _disableCollidersWhenOpen = true;
  [SerializeField, Range(0f, 1f), Tooltip("Temblor de camara mientras se abre.")]
  private float _cameraShake = 0.15f;

  [Header("Visuales")]
  [SerializeField, Tooltip("Candado (salta y desaparece al abrir).")]
  private GameObject _lockVisual;
  [SerializeField, Min(0f), Tooltip("Cuanto tiembla la puerta al intentar abrirla sin llave.")]
  private float _lockedRattle = 0.05f;

  [Header("Sonidos")]
  [SerializeField] private AudioClip _unlockSound;
  [SerializeField] private AudioClip _openSound;
  [SerializeField, Tooltip("Al intentar abrir sin llave.")]
  private AudioClip _lockedSound;
  [SerializeField, Range(0f, 1f)] private float _volume = 0.9f;

  [Header("Eventos")]
  public UnityEvent OnUnlocked = new UnityEvent();
  public UnityEvent OnOpened = new UnityEvent();
  [Tooltip("Al intentar abrir sin llave.")]
  public UnityEvent OnLockedAttempt = new UnityEvent();

  private Transform _player;
  private Vector3 _interactPoint;
  private bool _opening;
  private Coroutine _rattle;
  private WorldMessageView _shownMessage;

  public bool IsOpen { get; private set; }

  // ---------- IInteractable ----------

  public bool CanInteract => isActiveAndEnabled && !_opening && !IsOpen;

  public Vector3 PromptPoint => _interactPoint + Vector3.up * _promptHeight;

  public string PromptText => _promptText;

  public CollectiblePromptView CustomPromptView => _customPromptView;

  public bool IsInRange(Vector3 playerPosition, out float sqrDistance)
  {
    var delta = playerPosition - _interactPoint;
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

    if (KeyRing.Use(_keyId))
    {
      HideMessage();
      StartCoroutine(Open());
    }
    else
    {
      LockedAttempt();
    }
  }

  // ---------- Ciclo ----------

  private void Awake()
  {
    if (_door == null)
    {
      _door = transform;
    }

    // Fijo: la puerta se mueve al abrirse, el punto de interaccion no.
    _interactPoint = transform.TransformPoint(_interactOffset);
  }

  private void OnEnable() => InteractableRegistry.Register(this);

  private void OnDisable()
  {
    InteractableRegistry.Unregister(this);
    HideMessage();
  }

  private void Update()
  {
    if (IsOpen || _opening)
    {
      return;
    }

    if (_player == null)
    {
      var go = GameObject.FindWithTag("Player");
      _player = go != null ? go.transform : null;

      if (_player == null)
      {
        return;
      }
    }

    var inRange = IsInRange(_player.position, out _);

    // Al alejarse se quita el mensaje.
    if (!inRange && _shownMessage != null)
    {
      HideMessage();
    }

    if (_openAutomaticallyWithKey && inRange && KeyRing.Has(_keyId)
        && !Menus.PauseMenu.IsOpen && !CollectibleInspector.IsOpen && KeyRing.Use(_keyId))
    {
      StartCoroutine(Open());
    }
  }

  /// <summary>Abre la puerta sin llave (palancas, eventos, cinematicas...).</summary>
  public void ForceOpen()
  {
    if (!_opening && !IsOpen)
    {
      StartCoroutine(Open());
    }
  }

  // ---------- Sin llave ----------

  private void LockedAttempt()
  {
    if (_lockedSound != null)
    {
      AudioSource.PlayClipAtPoint(_lockedSound, _interactPoint, _volume);
    }

    if (_messageView != null && !string.IsNullOrEmpty(_lockedMessage))
    {
      _shownMessage = WorldMessageView.Show(_messageView, _lockedMessage, PromptPoint, _messageDuration);
    }

    if (_lockedRattle > 0f && !_opening)
    {
      if (_rattle != null)
      {
        StopCoroutine(_rattle);
      }

      _rattle = StartCoroutine(Rattle());
    }

    OnLockedAttempt.Invoke();
  }

  private IEnumerator Rattle()
  {
    var start = _door.localPosition;
    var right = _door.parent != null ? _door.parent.InverseTransformDirection(transform.right) : transform.right;
    const float duration = 0.3f;
    var t = 0f;

    while (t < duration)
    {
      t += Time.deltaTime;
      var k = 1f - t / duration;
      _door.localPosition = start + right * (Mathf.Sin(t * 60f) * _lockedRattle * k);
      yield return null;
    }

    _door.localPosition = start;
    _rattle = null;
  }

  private void HideMessage()
  {
    if (_shownMessage != null)
    {
      _shownMessage.Hide();
      _shownMessage = null;
    }
  }

  // ---------- Abrir ----------

  private IEnumerator Open()
  {
    _opening = true;

    if (_rattle != null)
    {
      StopCoroutine(_rattle);
      _rattle = null;
    }

    if (_unlockSound != null)
    {
      AudioSource.PlayClipAtPoint(_unlockSound, _interactPoint, _volume);
    }

    OnUnlocked.Invoke();

    if (_lockVisual != null)
    {
      yield return PopLock();
    }

    if (_unlockDelay > 0f)
    {
      yield return new WaitForSeconds(_unlockDelay);
    }

    if (_openSound != null)
    {
      AudioSource.PlayClipAtPoint(_openSound, _interactPoint, _volume);
    }

    var startPos = _door.localPosition;
    var startRot = _door.localRotation;
    var startScale = _door.localScale;
    var endPos = startPos + _slideOffset;
    var endRot = startRot * Quaternion.Euler(0f, _hingeAngle, 0f);
    var t = 0f;

    while (t < 1f)
    {
      t = Mathf.Min(1f, t + Time.deltaTime / _openDuration);
      var e = t * t * (3f - 2f * t);

      switch (_openMode)
      {
        case OpenMode.Deslizar:
          _door.localPosition = Vector3.LerpUnclamped(startPos, endPos, e);
          break;
        case OpenMode.Girar:
          _door.localRotation = Quaternion.SlerpUnclamped(startRot, endRot, e);
          break;
        case OpenMode.Desaparecer:
          _door.localScale = startScale * (1f - e);
          break;
      }

      if (_cameraShake > 0f && _openMode != OpenMode.Desaparecer)
      {
        TopDownCamera.AddShake(_cameraShake * Time.deltaTime * 4f);
      }

      yield return null;
    }

    if (_disableCollidersWhenOpen || _openMode == OpenMode.Desaparecer)
    {
      foreach (var c in _door.GetComponentsInChildren<Collider>())
      {
        c.enabled = false;
      }
    }

    if (_openMode == OpenMode.Desaparecer)
    {
      foreach (var r in _door.GetComponentsInChildren<Renderer>())
      {
        r.enabled = false;
      }
    }

    if (TryGetComponent<MapMarker>(out var marker))
    {
      marker.SetVisible(false, false);
    }

    IsOpen = true;
    _opening = false;
    OnOpened.Invoke();
  }

  /// <summary>El candado salta hacia arriba girando y se encoge.</summary>
  private IEnumerator PopLock()
  {
    var lockTf = _lockVisual.transform;
    var start = lockTf.position;
    var scale = lockTf.localScale;
    const float duration = 0.45f;
    var t = 0f;

    while (t < 1f)
    {
      t = Mathf.Min(1f, t + Time.deltaTime / duration);
      lockTf.position = start + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.6f + t * 0.3f);
      lockTf.Rotate(0f, 720f * Time.deltaTime, 0f, Space.World);
      lockTf.localScale = scale * (t < 0.6f ? 1f + t * 0.4f : Mathf.Lerp(1.24f, 0f, (t - 0.6f) / 0.4f));
      yield return null;
    }

    _lockVisual.SetActive(false);
  }

#if UNITY_EDITOR
  // Asigna sola la interfaz del mensaje (DoorMessageUI) al añadir la puerta.
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

  private void OnDrawGizmosSelected()
  {
    var point = Application.isPlaying ? _interactPoint : transform.TransformPoint(_interactOffset);
    Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.8f);
    Gizmos.DrawWireSphere(point, _interactRadius);
    Gizmos.DrawLine(point, point + Vector3.up * _promptHeight);
    Gizmos.DrawSphere(point + Vector3.up * _promptHeight, 0.06f);
  }
}
