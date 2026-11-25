// ShieldSelectMenu.cs
// Menu de los postes del puzzle de escudos (prefab ShieldSelectUI): muestra
// los escudos que lleva la nutria para colocar uno, el boton "Retirar" si el
// poste ya tiene escudo, y "Cancelar". Mientras esta abierto el juego se
// pausa y la nutria no se mueve. Esc / B / Circulo cierran.
//
// El diseño se hace en el prefab: "Opcion" es la plantilla de cada escudo
// (Button con un hijo Image "Icono" y un Text "Nombre") y se duplica.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ShieldSelectMenu : MonoBehaviour
{
  [Header("Textos")]
  [SerializeField] private Text _title;
  [SerializeField, Tooltip("Debajo del titulo: que escudo hay en el poste.")]
  private Text _subtitle;
  [SerializeField] private string _titleText = "Poste de escudos";
  [SerializeField] private string _emptyPole = "Elige el escudo que quieres colocar";
  [SerializeField, Tooltip("{0} = escudo colocado.")]
  private string _fullPole = "Ahora tiene el escudo de {0}";

  [Header("Opciones")]
  [SerializeField, Tooltip("Contenedor de las opciones (con Horizontal Layout Group).")]
  private Transform _optionsRoot;
  [SerializeField, Tooltip("Plantilla de cada escudo: Button con hijos 'Icono' (Image) y 'Nombre' (Text).")]
  private Button _optionTemplate;
  [SerializeField, Tooltip("Texto que se ve si no lleva escudos.")]
  private GameObject _noShieldsLabel;

  [Header("Botones")]
  [SerializeField] private Button _removeButton;
  [SerializeField] private Text _removeLabel;
  [SerializeField, Tooltip("{0} = escudo colocado.")]
  private string _removeText = "Retirar {0}";
  [SerializeField] private Button _cancelButton;

  [Header("Animacion")]
  [SerializeField, Min(0.01f)] private float _fadeDuration = 0.15f;

  public static bool IsOpen => s_instance != null && s_instance._open;

  private static ShieldSelectMenu s_instance;

  private readonly List<Button> _options = new List<Button>();
  private CanvasGroup _group;
  private ShieldPedestal _pedestal;
  private PlayerInput _playerInput;
  private bool _open;
  private bool _inputWasActive;
  private float _previousTimeScale = 1f;
  private float _fade;
  private float _openedAt;

  /// <summary>Instancia del menu (se crea del prefab la primera vez).</summary>
  public static ShieldSelectMenu Get(ShieldSelectMenu prefab)
  {
    if (s_instance != null) return s_instance;
    if (prefab == null) return null;
    s_instance = Instantiate(prefab);
    s_instance.name = prefab.name;
    return s_instance;
  }

  private void Awake()
  {
    if (s_instance == null) s_instance = this;

    _group = GetComponent<CanvasGroup>();
    if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
    _group.alpha = 0f;
    _group.interactable = false;
    _group.blocksRaycasts = false;

    if (_optionTemplate != null) _optionTemplate.gameObject.SetActive(false);
    if (_removeButton != null) _removeButton.onClick.AddListener(OnRemove);
    if (_cancelButton != null) _cancelButton.onClick.AddListener(Close);
  }

  private void OnDestroy()
  {
    if (s_instance == this)
    {
      if (_open) RestoreGame();
      s_instance = null;
    }
  }

  public void Open(ShieldPedestal pedestal, PlayerInput playerInput)
  {
    _pedestal = pedestal;
    _playerInput = playerInput;
    var catalog = pedestal.Catalog;
    var current = pedestal.CurrentId;

    if (_title != null) _title.text = _titleText;

    if (_subtitle != null)
    {
      _subtitle.text = current != null
        ? string.Format(_fullPole, catalog != null ? catalog.NameOf(current) : current)
        : _emptyPole;
    }

    BuildOptions(catalog);

    if (_removeButton != null)
    {
      _removeButton.gameObject.SetActive(current != null);

      if (_removeLabel != null && current != null)
      {
        _removeLabel.text = string.Format(_removeText, catalog != null ? catalog.NameOf(current) : current);
      }
    }

    // Pausa: el juego se detiene y la nutria no recibe controles.
    if (!_open)
    {
      _previousTimeScale = Time.timeScale;
      Time.timeScale = 0f;

      if (_playerInput != null)
      {
        _inputWasActive = _playerInput.inputIsActive;
        _playerInput.DeactivateInput();
      }
    }

    _open = true;
    _openedAt = Time.unscaledTime;
    _group.interactable = true;
    _group.blocksRaycasts = true;

    var first = _options.Count > 0 ? _options[0] : (_removeButton != null && _removeButton.gameObject.activeSelf ? _removeButton : _cancelButton);

    if (first != null && EventSystem.current != null)
    {
      EventSystem.current.SetSelectedGameObject(null);
      EventSystem.current.SetSelectedGameObject(first.gameObject);
    }
  }

  private void BuildOptions(ShieldCatalog catalog)
  {
    foreach (var b in _options) if (b != null) Destroy(b.gameObject);
    _options.Clear();

    var held = ShieldInventory.Held();
    if (catalog != null) held.Sort((a, b) => catalog.OrderOf(a).CompareTo(catalog.OrderOf(b)));

    if (_noShieldsLabel != null) _noShieldsLabel.SetActive(held.Count == 0);
    if (_optionTemplate == null || _optionsRoot == null) return;

    foreach (var id in held)
    {
      var button = Instantiate(_optionTemplate, _optionsRoot);
      button.gameObject.SetActive(true);
      button.name = "Opcion_" + id;

      var icon = button.transform.Find("Icono");
      if (icon != null && icon.TryGetComponent<Image>(out var image))
      {
        image.sprite = catalog != null ? catalog.IconOf(id) : null;
        image.enabled = image.sprite != null;
      }

      var label = button.GetComponentInChildren<Text>(true);
      if (label != null) label.text = catalog != null ? catalog.NameOf(id) : id;

      var chosen = id;
      button.onClick.AddListener(() => Choose(chosen));
      _options.Add(button);
    }
  }

  private void Choose(string id)
  {
    // Evita que la misma pulsacion que abrio el menu elija algo.
    if (Time.unscaledTime - _openedAt < 0.15f) return;
    var pedestal = _pedestal;
    Close();
    if (pedestal != null) pedestal.Place(id);
  }

  private void OnRemove()
  {
    if (Time.unscaledTime - _openedAt < 0.15f) return;
    var pedestal = _pedestal;
    Close();
    if (pedestal != null) pedestal.Remove();
  }

  public void Close()
  {
    if (!_open) return;
    _open = false;
    _group.interactable = false;
    _group.blocksRaycasts = false;

    if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    RestoreGame();
    _pedestal = null;
  }

  private void RestoreGame()
  {
    Time.timeScale = _previousTimeScale <= 0f ? 1f : _previousTimeScale;

    if (_playerInput != null && _inputWasActive)
    {
      _playerInput.ActivateInput();
    }

    _playerInput = null;
  }

  private void Update()
  {
    var target = _open ? 1f : 0f;
    _fade = Mathf.MoveTowards(_fade, target, Time.unscaledDeltaTime / _fadeDuration);
    _group.alpha = _fade;

    if (!_open || Time.unscaledTime - _openedAt < 0.15f) return;

    var keyboard = Keyboard.current;
    var gamepad = Gamepad.current;

    if ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ||
        (gamepad != null && gamepad.buttonEast.wasPressedThisFrame))
    {
      Close();
      return;
    }

    // Si se pierde la seleccion (clic fuera), se recupera para el mando.
    if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null && gamepad != null &&
        gamepad.leftStick.ReadValue().sqrMagnitude > 0.2f)
    {
      var first = _options.Count > 0 ? _options[0] : _cancelButton;
      if (first != null) EventSystem.current.SetSelectedGameObject(first.gameObject);
    }
  }
}
