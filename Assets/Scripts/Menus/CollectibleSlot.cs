// CollectibleSlot.cs
// Casilla de la cuadricula de coleccionables (pantalla de Estado). Muestra el
// icono del coleccionable (o su titulo si no tiene icono); vacia, queda
// desactivada. Al pulsarla se vuelve a abrir la inspeccion del objeto.
// El aspecto (marco, fondo, resaltado) se diseña en el prefab.

using Collectibles;
using UnityEngine;
using UnityEngine.UI;

namespace Menus
{
  [RequireComponent(typeof(Button))]
  [DisallowMultipleComponent]
  public class CollectibleSlot : MonoBehaviour
  {
    [Tooltip("Ninguno = casilla de coleccionable. Espada / Escudo = casilla de equipamiento.")]
    public EquipmentSlot equipment = EquipmentSlot.Ninguno;

    [Tooltip("Imagen donde va el icono del coleccionable.")]
    public Image icon;

    [Tooltip("Texto que se muestra si el coleccionable no tiene icono (Text o TextMeshPro, opcional).")]
    public Graphic fallbackLabel;

    [Tooltip("Objetos visibles solo cuando la casilla esta vacia (p. ej. un candado o una silueta).")]
    public GameObject[] emptyOnly;

    private Button _button;

    public Button Button => _button != null ? _button : (_button = GetComponent<Button>());

    public Collectible Collectible { get; private set; }

    public void Set(Collectible collectible)
    {
      Collectible = collectible;
      var has = collectible != null;
      var sprite = has ? collectible.Icon : null;

      if (icon != null)
      {
        icon.sprite = sprite;
        icon.enabled = sprite != null;
        icon.preserveAspect = true;
      }

      if (fallbackLabel != null)
      {
        UIText.Set(fallbackLabel, has && sprite == null ? collectible.Title : "");
        fallbackLabel.enabled = has && sprite == null;
      }

      if (emptyOnly != null)
      {
        foreach (var go in emptyOnly)
        {
          if (go != null) go.SetActive(!has);
        }
      }

      Button.interactable = has;
    }
  }
}
