// CollectiblePromptView.cs
// Va en la raiz del Canvas (prefab) del aviso "F / X  Examinar". El diseño
// (panel, iconos de botones, fuentes...) se hace en el prefab; el script solo
// lo mueve sobre el coleccionable, escribe el texto de accion y lo funde.

using UnityEngine;
using UnityEngine.UI;

namespace Collectibles
{
  [RequireComponent(typeof(Canvas))]
  [DisallowMultipleComponent]
  public class CollectiblePromptView : MonoBehaviour
  {
    [Tooltip("Elemento que se coloca sobre el objeto (normalmente el panel del aviso). " +
             "Su pivote es el punto que se alinea con el objeto.")]
    public RectTransform followRoot;

    [Tooltip("Si esta apagado, el aviso se queda donde lo pongas en el Canvas (p. ej. abajo al centro).")]
    public bool followTarget = true;

    [Tooltip("Desplazamiento extra en pantalla (pixeles de referencia).")]
    public Vector2 screenOffset;

    [Tooltip("Texto donde se escribe el 'Prompt Text' de cada coleccionable (Text o TextMeshPro). Vacio = no se cambia.")]
    public Graphic actionLabel;

    private CanvasGroup _group;
    private Canvas _canvas;

    public CanvasGroup Group
    {
      get
      {
        Cache();
        return _group;
      }
    }

    private void Cache()
    {
      if (_group != null)
      {
        return;
      }

      _canvas = GetComponent<Canvas>();
      _group = GetComponent<CanvasGroup>();

      if (_group == null)
      {
        _group = gameObject.AddComponent<CanvasGroup>();
      }

      _group.blocksRaycasts = false;
      _group.interactable = false;
    }

    public void SetAction(string text)
    {
      if (actionLabel != null)
      {
        UIText.Set(actionLabel, text);
      }
    }

    /// <summary>Coloca el aviso en el punto de pantalla. Devuelve false si queda detras de la camara.</summary>
    public bool Place(Vector3 screenPoint)
    {
      Cache();

      if (!followTarget || followRoot == null)
      {
        return true;
      }

      if (screenPoint.z <= 0f)
      {
        return false;
      }

      var root = _canvas.rootCanvas;
      var cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
      var parent = followRoot.parent as RectTransform;
      var point = (Vector2)screenPoint + screenOffset * root.scaleFactor;

      if (parent != null &&
          RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, point, cam, out var world))
      {
        followRoot.position = world;
        return true;
      }

      return false;
    }
  }
}
