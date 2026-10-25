// PauseMenuPage.cs
// Una pantalla del carrusel del menu de pausa (Pausa, Estado, Mapa...).
// Va en la raiz de cada pantalla dentro del prefab PauseMenuUI. El contenido
// (fondo, titulo, botones, decoraciones) se diseña libremente en el prefab.

using UnityEngine;
using UnityEngine.UI;

namespace Menus
{
  [RequireComponent(typeof(RectTransform))]
  [DisallowMultipleComponent]
  public class PauseMenuPage : MonoBehaviour
  {
    [Tooltip("Nombre de la pantalla (solo informativo).")]
    public string pageName = "Pausa";

    [Tooltip("Elemento seleccionado al llegar a esta pantalla (para navegar con mando). Vacio = ninguno.")]
    public Selectable firstSelected;

    private CanvasGroup _group;

    public RectTransform Rect => (RectTransform)transform;

    public CanvasGroup Group
    {
      get
      {
        if (_group == null)
        {
          _group = GetComponent<CanvasGroup>();

          if (_group == null)
          {
            _group = gameObject.AddComponent<CanvasGroup>();
          }
        }

        return _group;
      }
    }
  }
}
