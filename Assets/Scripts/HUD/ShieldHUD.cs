// ShieldHUD.cs
// Casillas de los escudos en el HUD, a la derecha de la llave. Una casilla
// por escudo del catalogo (5): vacia (silueta) hasta conseguirlo; con el
// escudo a todo color cuando esta en el inventario; mas transparente si ya
// esta colocado en un poste.
//
// Va en el objeto "Escudos" del prefab PlayerHUD. "Casilla" es la plantilla
// (Image) que se duplica.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ShieldHUD : MonoBehaviour
{
  [SerializeField] private ShieldCatalog _catalog;

  [SerializeField, Tooltip("Casilla de ejemplo (Image). Se duplica una por escudo.")]
  private Image _slotTemplate;

  [Header("Colores")]
  [SerializeField] private Color _emptyColor = new Color(1f, 1f, 1f, 0.55f);
  [SerializeField] private Color _heldColor = Color.white;
  [SerializeField, Tooltip("Escudo ya colocado en un poste.")]
  private Color _placedColor = new Color(1f, 1f, 1f, 0.4f);

  [Header("Posicion")]
  [SerializeField, Tooltip("Se coloca a la derecha de este objeto (la llave). Vacio = se queda donde esta.")]
  private RectTransform _placeAfter;
  [SerializeField] private float _gap = 22f;

  [Header("Animacion")]
  [SerializeField] private float _popScale = 1.5f;
  [SerializeField, Min(0.05f)] private float _popDuration = 0.45f;

  private readonly List<Image> _slots = new List<Image>();
  private readonly List<string> _ids = new List<string>();
  private readonly HashSet<string> _heldBefore = new HashSet<string>();
  private bool _built;

  private void Awake()
  {
    Build();
  }

  private void OnEnable()
  {
    ShieldInventory.Changed += Refresh;
    _heldBefore.Clear();
    foreach (var id in ShieldInventory.Held()) _heldBefore.Add(id);
    Refresh();
    StartCoroutine(PlaceLater());
  }

  private void OnDisable()
  {
    ShieldInventory.Changed -= Refresh;
  }

  private void Build()
  {
    if (_built || _catalog == null || _slotTemplate == null) return;
    _built = true;

    var parent = _slotTemplate.transform.parent;

    for (var i = 0; i < _catalog.Entries.Count; i++)
    {
      var entry = _catalog.Entries[i];
      if (entry == null) continue;
      var image = i == 0 ? _slotTemplate : Instantiate(_slotTemplate, parent);
      image.name = "Casilla_" + entry.id;
      image.gameObject.SetActive(true);
      _slots.Add(image);
      _ids.Add(entry.id);
    }
  }

  private void Refresh()
  {
    if (!_built) Build();

    for (var i = 0; i < _slots.Count; i++)
    {
      var image = _slots[i];
      var id = _ids[i];
      var held = ShieldInventory.Has(id);
      var placed = !held && ShieldInventory.IsPlaced(id);
      var icon = _catalog.IconOf(id);

      if ((held || placed) && icon != null)
      {
        image.sprite = icon;
        image.color = held ? _heldColor : _placedColor;
      }
      else
      {
        image.sprite = _catalog.EmptyIcon;
        image.color = _emptyColor;
      }

      image.enabled = image.sprite != null;

      // Salto al conseguirlo.
      if (held && !_heldBefore.Contains(id) && isActiveAndEnabled)
      {
        StartCoroutine(Pop(image.rectTransform));
      }
    }

    _heldBefore.Clear();
    foreach (var id in ShieldInventory.Held()) _heldBefore.Add(id);
  }

  private IEnumerator Pop(RectTransform rect)
  {
    var t = 0f;

    while (t < _popDuration)
    {
      t += Time.unscaledDeltaTime;
      var k = Mathf.Clamp01(t / _popDuration);
      rect.localScale = Vector3.one * (1f + (_popScale - 1f) * Mathf.Sin(k * Mathf.PI) * (1f - k * 0.5f));
      rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(k * Mathf.PI * 3f) * 10f * (1f - k));
      yield return null;
    }

    rect.localScale = Vector3.one;
    rect.localRotation = Quaternion.identity;
  }

  // La llave se recoloca sola un frame despues; esto va detras de ella.
  private IEnumerator PlaceLater()
  {
    if (_placeAfter == null) yield break;
    yield return null;
    yield return null;

    var rect = (RectTransform)transform;
    var parent = rect.parent as RectTransform;
    if (parent == null) yield break;

    var corners = new Vector3[4];
    _placeAfter.GetWorldCorners(corners);
    var right = parent.InverseTransformPoint((corners[2] + corners[3]) * 0.5f);
    var middleY = parent.InverseTransformPoint((corners[0] + corners[2]) * 0.5f).y;
    var width = rect.rect.width * rect.localScale.x;
    rect.localPosition = new Vector3(right.x + _gap + width * rect.pivot.x, middleY, rect.localPosition.z);
  }
}
