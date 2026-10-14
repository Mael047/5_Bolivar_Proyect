// Collectible.cs
// Objeto coleccionable del mundo. Cuando el jugador entra en el radio de
// interaccion aparece un aviso en pantalla; al pulsar Interact (F en teclado /
// X del mando) se abre la vista de inspeccion (CollectibleInspector): fondo
// difuminado, el modelo en primer plano -que se gira con la mano (puño
// cerrado + mover), el stick o el raton- y el texto a la derecha.
// Todo el contenido (titulo, subtitulo, descripcion y modelo) se edita desde
// el Inspector. No hace falta montar nada mas en la escena: el detector de
// cercania y la vista de inspeccion se crean solos la primera vez.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Collectibles
{
  [DisallowMultipleComponent]
  public class Collectible : MonoBehaviour
  {
    private const string SaveKeyPrefix = "collectible.";
    private const float MaxHeightDifference = 3f;

    private static readonly List<Collectible> s_active = new List<Collectible>();

    [Header("Contenido")]
    [Tooltip("Titulo grande que aparece a la derecha del objeto.")]
    [SerializeField] private string _title = "Objeto";

    [Tooltip("Linea corta bajo el titulo (categoria, lugar, fecha...). Vacio = no se muestra.")]
    [SerializeField] private string _subtitle = "";

    [Tooltip("Texto descriptivo. Admite rich text: <b>negrita</b>, <i>cursiva</i>, <color=#E8C66A>color</color>.")]
    [SerializeField, TextArea(5, 15)] private string _description = "";

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

    public string Title => _title;
    public string Subtitle => _subtitle;
    public string Description => _description;
    public GameObject InspectModel => _inspectModel;
    public Vector3 InspectRotation => _inspectRotation;
    public float InspectScale => _inspectScale;
    public string PromptText => _promptText;
    public CollectibleInspectorView CustomInspectorView => _customInspectorView;
    public CollectiblePromptView CustomPromptView => _customPromptView;
    public string Id => string.IsNullOrEmpty(_id) ? $"{gameObject.scene.name}/{name}" : _id;

    /// <summary>True si ya se examino al menos una vez.</summary>
    public bool IsCollected { get; private set; }

    public Vector3 InteractPoint => transform.TransformPoint(_interactOffset);
    public Vector3 PromptPoint => InteractPoint + Vector3.up * _promptHeight;
    public bool CanInteract => isActiveAndEnabled && !(IsCollected && _hideAfterInspect);

    private void Awake()
    {
      // Interfaces propias colocadas en la escena: ocultas hasta usarse.
      CollectibleInteractor.HideIfSceneObject(_customInspectorView);
      CollectibleInteractor.HideIfSceneObject(_customPromptView);

      if (_rememberBetweenSessions && PlayerPrefs.GetInt(SaveKeyPrefix + Id, 0) == 1)
      {
        IsCollected = true;

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

    internal void NotifyOpened()
    {
      if (!IsCollected)
      {
        IsCollected = true;

        if (_rememberBetweenSessions)
        {
          PlayerPrefs.SetInt(SaveKeyPrefix + Id, 1);
          PlayerPrefs.Save();
        }

        Collected?.Invoke(this);
      }

      OnInspectOpened?.Invoke();
    }

    internal void NotifyClosed()
    {
      OnInspectClosed?.Invoke();

      if (_hideAfterInspect)
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
