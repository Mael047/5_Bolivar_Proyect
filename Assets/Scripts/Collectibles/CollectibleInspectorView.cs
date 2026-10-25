// CollectibleInspectorView.cs
// Va en la raiz del Canvas (prefab) de la vista de inspeccion. Solo dice
// al CollectibleInspector que elemento es cada cosa; el aspecto (imagenes de
// fondo, marcos, fuentes, colores, posiciones, decoraciones extra) se diseña
// libremente en el prefab desde el editor.
// Los campos de texto aceptan Text (legacy) o TextMeshPro.

using UnityEngine;
using UnityEngine.UI;

namespace Collectibles
{
  [RequireComponent(typeof(Canvas))]
  [DisallowMultipleComponent]
  public class CollectibleInspectorView : MonoBehaviour
  {
    [Header("Modelo 3D (obligatorio)")]
    [Tooltip("RawImage donde se dibuja el objeto. Su tamaño y posicion deciden donde aparece el modelo. " +
             "En juego su color se pone en blanco y se le asigna la textura del modelo.")]
    public RawImage modelImage;

    [Header("Fondo difuminado (opcional)")]
    [Tooltip("RawImage que recibe la captura difuminada de la pantalla (el color tiñe la captura). " +
             "Vacio = no se captura nada y se ve solo tu propio fondo.")]
    public RawImage blurredBackground;

    [Header("Textos (Text o TextMeshPro, opcionales)")]
    public Graphic title;
    public Graphic subtitle;
    public Graphic description;

    [Tooltip("Objetos que se ocultan si el coleccionable no tiene subtitulo (el texto, una linea decorativa...).")]
    public GameObject[] hideWhenNoSubtitle;

    [Tooltip("Objetos que se ocultan si el coleccionable no tiene descripcion.")]
    public GameObject[] hideWhenNoDescription;

    [Header("Animacion de entrada (opcional)")]
    [Tooltip("Bloque que entra deslizandose (normalmente la columna de texto).")]
    public RectTransform slideIn;

    [Tooltip("Desplazamiento desde el que entra el bloque (pixeles de referencia).")]
    public Vector2 slideFrom = new Vector2(60f, 0f);

    [Tooltip("Grupo que aparece con retraso (normalmente la columna de texto).")]
    public CanvasGroup delayedGroup;

    private CanvasGroup _group;
    private Canvas _canvas;
    private Vector2 _slideBase;
    private bool _cached;

    public CanvasGroup Group
    {
      get
      {
        Cache();
        return _group;
      }
    }

    public Canvas Canvas
    {
      get
      {
        Cache();
        return _canvas;
      }
    }

    private void Cache()
    {
      if (_cached)
      {
        return;
      }

      _cached = true;
      _canvas = GetComponent<Canvas>();
      _group = GetComponent<CanvasGroup>();

      if (_group == null)
      {
        _group = gameObject.AddComponent<CanvasGroup>();
      }

      _group.blocksRaycasts = false;
      _group.interactable = false;

      if (slideIn != null)
      {
        _slideBase = slideIn.anchoredPosition;
      }
    }

    public void Fill(Collectible target)
    {
      Cache();
      UIText.Set(title, target.Title);
      UIText.Set(subtitle, target.Subtitle);
      UIText.Set(description, target.Description);
      SetActive(hideWhenNoSubtitle, !string.IsNullOrEmpty(target.Subtitle));
      SetActive(hideWhenNoDescription, !string.IsNullOrEmpty(target.Description));
    }

    /// <summary>Progreso de la entrada (0 = oculto, 1 = visible) del conjunto y del bloque retrasado.</summary>
    public void ApplyEntrance(float main, float delayed)
    {
      Cache();
      _group.alpha = main;

      if (modelImage != null)
      {
        modelImage.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, main);
      }

      if (delayedGroup != null)
      {
        delayedGroup.alpha = delayed;
      }

      if (slideIn != null)
      {
        slideIn.anchoredPosition = _slideBase + slideFrom * (1f - delayed);
      }
    }

    private static void SetActive(GameObject[] objects, bool active)
    {
      if (objects == null)
      {
        return;
      }

      foreach (var go in objects)
      {
        if (go != null)
        {
          go.SetActive(active);
        }
      }
    }
  }

  /// <summary>Escribe en un Text legacy o en un TextMeshPro indistintamente.</summary>
  public static class UIText
  {
    public static void Set(Graphic graphic, string value)
    {
      if (graphic is Text text)
      {
        text.text = value;
      }
      else if (graphic is TMPro.TMP_Text tmp)
      {
        tmp.text = value;
      }
    }
  }
}
