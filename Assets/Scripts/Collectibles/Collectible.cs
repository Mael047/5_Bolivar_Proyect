
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Collectibles
{
  public enum CollectibleKind
  {
    [Tooltip("Se guarda en Estado (o se equipa) y desaparece al recogerlo.")]
    Coleccionable,
    [Tooltip("Letrero / tutorial: se lee las veces que quieras, no se guarda ni desaparece.")]
    Letrero
  }

  [DisallowMultipleComponent]
  public class Collectible : MonoBehaviour, IInteractable
  {
    private const string SaveKeyPrefix = "collectible.";
    private const float MaxHeightDifference = 3f;

    private static readonly List<Collectible> s_active = new List<Collectible>();

    [Tooltip("Coleccionable: va al inventario. Letrero: solo se lee (indicaciones, tutoriales), no se guarda ni desaparece.")]
    [SerializeField] private CollectibleKind _kind = CollectibleKind.Coleccionable;

    [Header("Contenido")]
    [Tooltip("Titulo grande que aparece a la derecha del objeto.")]
    [SerializeField] private string _title = "Objeto";

    [Tooltip("Linea corta bajo el titulo (categoria, lugar, fecha...). Vacio = no se muestra.")]
    [SerializeField] private string _subtitle = "";

    [Tooltip("Texto descriptivo. Admite rich text: <b>negrita</b>, <i>cursiva</i>, <color=#E8C66A>color</color>.")]
    [SerializeField, TextArea(5, 15)] private string _description = "";

    [Tooltip("Icono para la cuadricula de coleccionables del menu de pausa (Estado). Vacio = se muestra el titulo.")]
    [SerializeField] private Sprite _icon;

    [Tooltip("Si es Espada o Escudo, al recogerlo el jugador lo equipa (PlayerEquipment) y aparece en Equipamiento en vez de en la cuadricula de coleccionables.")]
    [SerializeField] private EquipmentSlot _equipment = EquipmentSlot.Ninguno;

    [Header("Modelo en primer plano")]
    [Tooltip("Prefab u objeto que se muestra en la inspeccion. Vacio = una copia visual de este mismo objeto.")]
    [SerializeField] private GameObject _inspectModel;

    [Tooltip("Rotacion (grados) con la que aparece el modelo al abrir la inspeccion.")]
    [SerializeField] private Vector3 _inspectRotation = new Vector3(15f, -25f, 0f);

    [Tooltip("Tamaño del modelo en pantalla. 1 = encaja automaticamente en la vista.")]
    [SerializeField, Range(0.3f, 2f)] private float _inspectScale = 1f;

    [Header("Interfaz propia (opcional)")]
    [Tooltip("Interfaz de inspeccion solo para este objeto (prefab con CollectibleInspectorView). Vacio = la general.")]
    [SerializeField] private CollectibleInspectorView _customInspectorView;

    [Tooltip("Aviso solo para este objeto (prefab con CollectiblePromptView). Vacio = el general.")]
    [SerializeField] private CollectiblePromptView _customPromptView;

    [Header("Interaccion")]
    [Tooltip("Distancia (m) a la que el jugador puede examinar el objeto.")]
    [SerializeField, Min(0.1f)] private float _interactRadius = 1.8f;

    [Tooltip("Desplazamiento local del punto de interaccion (centro del radio y del aviso).")]
    [SerializeField] private Vector3 _interactOffset = Vector3.zero;

    [Tooltip("Texto del aviso que aparece al acercarse.")]
    [SerializeField] private string _promptText = "Examinar";

    [Tooltip("Altura (m) del aviso sobre el punto de interaccion.")]
    [SerializeField] private float _promptHeight = 1.2f;

    [Header("Al recoger")]
    [Tooltip("ID para recordar si ya se recogio. Vacio = escena/nombre del objeto.")]
    [SerializeField] private string _id = "";

    [Tooltip("Desactiva el objeto del mundo al cerrar la inspeccion (el jugador se lo lleva).")]
    [SerializeField] private bool _hideAfterInspect = true;

    [Tooltip("Guarda en PlayerPrefs que ya se recogio: al recargar la escena ya no aparece. Mantenlo apagado mientras pruebas.")]
    [SerializeField] private bool _rememberBetweenSessions = false;

    [Header("Eventos")]
    [Tooltip("Al abrir la inspeccion (sonido, contador, desbloqueos...).")]
    public UnityEvent OnInspectOpened = new UnityEvent();

    [Tooltip("Al cerrar la inspeccion.")]
    public UnityEvent OnInspectClosed = new UnityEvent();

    /// <summary>Coleccionables activos en las escenas cargadas.</summary>
    public static IReadOnlyList<Collectible> Active => s_active;

    /// <summary>Se dispara la primera vez que se examina cada coleccionable.</summary>
    public static event Action<Collectible> Collected;

    public CollectibleKind Kind => _kind;

    /// <summary>True si es un letrero (no se guarda en el inventario).</summary>
    public bool IsSign => _kind == CollectibleKind.Letrero;

    public string Title => _title;
    public string Subtitle => _subtitle;
    public string Description => _description;
    public GameObject InspectModel => _inspectModel;
    public Vector3 InspectRotation => _inspectRotation;
    public float InspectScale => _inspectScale;
    public string PromptText => IsSign && _promptText == "Examinar" ? "Leer" : _promptText;
    public Sprite Icon => _icon;
    public EquipmentSlot Equipment => _equipment;
    public CollectibleInspectorView CustomInspectorView => _customInspectorView;
    public CollectiblePromptView CustomPromptView => _customPromptView;
    public string Id => string.IsNullOrEmpty(_id) ? $"{gameObject.scene.name}/{name}" : _id;

    /// <summary>True si ya se examino al menos una vez.</summary>
    public bool IsCollected { get; private set; }

    public Vector3 InteractPoint => transform.TransformPoint(_interactOffset);
    public Vector3 PromptPoint => InteractPoint + Vector3.up * _promptHeight;
    public bool CanInteract => isActiveAndEnabled && (IsSign || !(IsCollected && _hideAfterInspect));

    private void Awake()
    {
      // Interfaces propias colocadas en la escena: ocultas hasta usarse.
      CollectibleInteractor.HideIfSceneObject(_customInspectorView);
      CollectibleInteractor.HideIfSceneObject(_customPromptView);

      if (!IsSign && _rememberBetweenSessions && PlayerPrefs.GetInt(SaveKeyPrefix + Id, 0) == 1)
      {
        IsCollected = true;
        Register();

        if (_hideAfterInspect)
        {
          gameObject.SetActive(false);
        }
      }
    }

    private void OnEnable()
    {
      if (!s_active.Contains(this))
      {
        s_active.Add(this);
      }

      CollectibleInteractor.EnsureExists();
    }

    private void OnDisable()
    {
      s_active.Remove(this);
    }

    public bool IsInRange(Vector3 position, out float sqrDistance)
    {
      var point = InteractPoint;
      var delta = position - point;
      var height = Mathf.Abs(delta.y);
      delta.y = 0f;
      sqrDistance = delta.sqrMagnitude;
      return sqrDistance <= _interactRadius * _interactRadius && height <= MaxHeightDifference;
    }

    /// <summary>Interactuar = abrir la inspeccion.</summary>
    public void Interact(UnityEngine.InputSystem.PlayerInput playerInput)
    {
      CollectibleInspector.Instance.Open(this, playerInput);
    }

    internal void NotifyOpened()
    {
      if (IsSign)
      {
        // Los letreros solo se leen: no van al inventario ni se marcan como recogidos.
        OnInspectOpened?.Invoke();
        return;
      }

      if (!IsCollected)
      {
        IsCollected = true;

        if (_rememberBetweenSessions)
        {
          PlayerPrefs.SetInt(SaveKeyPrefix + Id, 1);
          PlayerPrefs.Save();
        }

        Register();
        Collected?.Invoke(this);
      }

      OnInspectOpened?.Invoke();
    }

    /// <summary>Equipamiento: se equipa. Coleccionable normal: va a la cuadricula de Estado.</summary>
    private void Register()
    {
      if (_equipment == EquipmentSlot.Ninguno)
      {
        CollectibleJournal.Add(this);
        return;
      }

      var equipment = PlayerEquipment.Instance != null
        ? PlayerEquipment.Instance
        : FindAnyObjectByType<PlayerEquipment>();

      if (equipment != null)
      {
        equipment.Equip(_equipment, this);
      }
      else
      {
        Debug.LogWarning($"[Collectible] '{name}' es equipamiento ({_equipment}) pero el jugador no tiene PlayerEquipment.", this);
      }
    }

    internal void NotifyClosed()
    {
      OnInspectClosed?.Invoke();

      if (_hideAfterInspect && !IsSign)
      {
        gameObject.SetActive(false);
      }
    }

    [ContextMenu("Borrar progreso guardado de este coleccionable")]
    private void ClearSavedProgress()
    {
      PlayerPrefs.DeleteKey(SaveKeyPrefix + Id);
      PlayerPrefs.Save();
      Debug.Log($"[Collectible] Progreso borrado: {Id}");
    }

    private void OnDrawGizmosSelected()
    {
      Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
      Gizmos.DrawWireSphere(InteractPoint, _interactRadius);
      Gizmos.DrawLine(InteractPoint, PromptPoint);
      Gizmos.DrawSphere(PromptPoint, 0.05f);
    }
  }
}
