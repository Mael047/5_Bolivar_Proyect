// CollectibleInteractor.cs
// Detecta el coleccionable mas cercano al jugador, muestra el aviso encima de
// el y abre la inspeccion al pulsar la accion Interact del PlayerInput.
// El aviso es un prefab con CollectiblePromptView que se diseña en el editor
// (por defecto Assets/UI/Collectibles/CollectiblePromptUI). Cada coleccionable
// puede usar su propio aviso (Collectible > Custom Prompt View).
// Se crea solo la primera vez que se activa un Collectible; si quieres
// cambiar sus ajustes, añade este componente a un GameObject de la escena.
//
// La F del teclado tambien es Atacar: Player consulta ShouldBlockAttack()
// para no atacar cuando esa pulsacion sirve para examinar un objeto.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Collectibles
{
  public class CollectibleInteractor : MonoBehaviour
  {
    public const string DefaultPromptResource = "Collectibles/CollectiblePromptUI";

    private static CollectibleInteractor s_instance;

    [SerializeField, Tooltip("Jugador (si no se asigna, se busca en la escena).")]
    private global::Player _player;

    [SerializeField, Tooltip("Accion del PlayerInput que abre la inspeccion.")]
    private string _interactActionName = "Interact";

    [Header("Aviso")]
    [SerializeField, Tooltip("Prefab (o objeto de la escena) con la interfaz. El prefab CollectibleSystem ya lo trae asignado.")]
    private CollectiblePromptView _promptView;

    [SerializeField, Range(1f, 30f), Tooltip("Velocidad del fundido del aviso.")]
    private float _fadeSpeed = 12f;

    private PlayerInput _playerInput;
    private InputAction _interactAction;
    private bool _ownsAction;

    private Collectible _current;
    private Camera _camera;
    private float _blockedUntil;

    private readonly Dictionary<CollectiblePromptView, CollectiblePromptView> _instances =
      new Dictionary<CollectiblePromptView, CollectiblePromptView>();
    private CollectiblePromptView _shownView;
    private Collectible _shownTarget;
    private bool _missingWarned;

    public static CollectibleInteractor Instance => s_instance;

    /// <summary>Coleccionable al alcance ahora mismo (null si ninguno).</summary>
    public Collectible Current => _current;

    public static void EnsureExists()
    {
      if (s_instance != null)
      {
        return;
      }

      s_instance = FindAnyObjectByType<CollectibleInteractor>();

      if (s_instance == null)
      {
        new GameObject("CollectibleInteractor").AddComponent<CollectibleInteractor>();
      }
    }

    /// <summary>
    /// True si la pulsacion de Atacar debe ignorarse: la inspeccion esta
    /// abierta, o hay un coleccionable al alcance y ese mismo boton es
    /// Interact (la F del teclado esta en ambas acciones).
    /// </summary>
    public static bool ShouldBlockAttack(InputControl control)
    {
      if (CollectibleInspector.IsOpen)
      {
        return true;
      }

      var self = s_instance;

      if (self == null || self._interactAction == null || control == null)
      {
        return false;
      }

      if (self._current == null && self.FindBest() == null)
      {
        return false;
      }

      foreach (var c in self._interactAction.controls)
      {
        if (c == control)
        {
          return true;
        }
      }

      return false;
    }

    /// <summary>Ignora Interact durante unos segundos (tras cerrar la inspeccion).</summary>
    public void BlockFor(float seconds)
    {
      _blockedUntil = Time.unscaledTime + seconds;
    }

    private void Awake()
    {
      if (s_instance != null && s_instance != this)
      {
        Destroy(this);
        return;
      }

      s_instance = this;
      HideIfSceneObject(_promptView);
    }

    private void OnDestroy()
    {
      if (_interactAction != null)
      {
        _interactAction.performed -= OnInteract;

        if (_ownsAction)
        {
          _interactAction.Dispose();
        }
      }

      if (s_instance == this)
      {
        s_instance = null;
      }
    }

    private void Update()
    {
      TryBindInput();

      if (_camera == null || !_camera.isActiveAndEnabled)
      {
        _camera = Camera.main;
      }

      _current = CollectibleInspector.IsOpen ? null : FindBest();
      UpdatePrompt();
    }

    private void TryBindInput()
    {
      if (_interactAction != null)
      {
        return;
      }

      if (_player == null)
      {
        _player = FindAnyObjectByType<global::Player>();
      }

      if (_player == null)
      {
        return;
      }

      _playerInput = _player.GetComponent<PlayerInput>();

      if (_playerInput != null && _playerInput.actions != null)
      {
        _interactAction = _playerInput.actions.FindAction(_interactActionName, false);
      }

      if (_interactAction == null)
      {
        Debug.LogWarning($"[CollectibleInteractor] No existe la accion '{_interactActionName}' en el PlayerInput del jugador; uso F / boton sur del mando por defecto.");
        _interactAction = new InputAction("Interact", InputActionType.Button);
        _interactAction.AddBinding("<Keyboard>/f");
        _interactAction.AddBinding("<Gamepad>/buttonSouth");
        _interactAction.Enable();
        _ownsAction = true;
      }

      _interactAction.performed += OnInteract;
    }

    private Collectible FindBest()
    {
      if (Menus.MainMenu.IsOpen || Menus.PauseMenu.IsOpen || _player == null || !_player.isActiveAndEnabled || _player.IsBusy)
      {
        return null;
      }

      var position = _player.transform.position;
      Collectible best = null;
      var bestSqr = float.MaxValue;
      var list = Collectible.Active;

      for (var i = 0; i < list.Count; i++)
      {
        var c = list[i];

        if (c != null && c.CanInteract && c.IsInRange(position, out var sqr) && sqr < bestSqr)
        {
          best = c;
          bestSqr = sqr;
        }
      }

      return best;
    }

    private void OnInteract(InputAction.CallbackContext context)
    {
      if (CollectibleInspector.IsOpen || Time.unscaledTime < _blockedUntil)
      {
        return;
      }

      var target = _current != null ? _current : FindBest();

      if (target == null)
      {
        return;
      }

      // Se oculta ya: la inspeccion captura este mismo frame para el fondo.
      _current = null;
      HidePromptNow();

      CollectibleInspector.Instance.Open(target, _playerInput);
    }

    // ---------- Aviso ----------

    private CollectiblePromptView ResolveView(Collectible target)
    {
      var source = target.CustomPromptView != null ? target.CustomPromptView : _promptView;

      if (source == null)
      {
        source = Resources.Load<CollectiblePromptView>(DefaultPromptResource);
      }

      if (source == null)
      {
        if (!_missingWarned)
        {
          _missingWarned = true;
          Debug.LogWarning("[CollectibleInteractor] No hay prefab de aviso. Agrega el sistema a la escena: Tools > Coleccionables > Agregar sistema a la escena abierta.");
        }

        return null;
      }

      if (source.gameObject.scene.IsValid())
      {
        return source;
      }

      if (!_instances.TryGetValue(source, out var instance) || instance == null)
      {
        instance = Instantiate(source, transform);
        instance.name = source.name;
        instance.Group.alpha = 0f;
        instance.gameObject.SetActive(false);
        _instances[source] = instance;
      }

      return instance;
    }

    private void UpdatePrompt()
    {
      var view = _current != null ? ResolveView(_current) : null;

      // Cambio de aviso (otro objeto con un aviso distinto): el anterior se apaga ya.
      if (_shownView != null && _shownView != view && view != null)
      {
        HidePromptNow();
      }

      var visible = false;

      if (view != null && _camera != null)
      {
        if (_shownView != view || _shownTarget != _current)
        {
          _shownView = view;
          _shownTarget = _current;

          if (!view.gameObject.activeSelf)
          {
            view.Group.alpha = 0f;
            view.gameObject.SetActive(true);
          }

          view.SetAction(_current.PromptText);
        }

        visible = view.Place(_camera.WorldToScreenPoint(_current.PromptPoint));
      }

      if (_shownView == null)
      {
        return;
      }

      var group = _shownView.Group;
      var k = 1f - Mathf.Exp(-_fadeSpeed * Time.unscaledDeltaTime);
      group.alpha = Mathf.Lerp(group.alpha, visible ? 1f : 0f, k);

      if (!visible && group.alpha < 0.01f)
      {
        HidePromptNow();
      }
    }

    private void HidePromptNow()
    {
      if (_shownView != null)
      {
        _shownView.Group.alpha = 0f;
        _shownView.gameObject.SetActive(false);
      }

      _shownView = null;
      _shownTarget = null;
    }

    internal static void HideIfSceneObject(Component view)
    {
      if (view != null && view.gameObject.scene.IsValid())
      {
        view.gameObject.SetActive(false);
      }
    }
  }
}
