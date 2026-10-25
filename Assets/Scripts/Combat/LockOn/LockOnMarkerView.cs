// LockOnMarkerView.cs
// Va en la raiz del Canvas (prefab LockOnMarkerUI) del marcador de lock-on.
// El diseño (sprites, formas, tamaños) se hace en el prefab; el script
// LockOnIndicator lo coloca sobre el enemigo, lo tiñe segun el estado
// (candidato / fijado) y lo anima.

using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Canvas))]
[DisallowMultipleComponent]
public class LockOnMarkerView : MonoBehaviour
{
  [Tooltip("Elemento que se coloca sobre el enemigo. Su pivote es el punto que se alinea con la cabeza.")]
  public RectTransform followRoot;

  [Tooltip("Graficos que cambian al color del estado (candidato / fijado). Su alfa original se respeta.")]
  public Graphic[] tinted;

  [Tooltip("Se muestran solo cuando el enemigo es candidato (aun no fijado).")]
  public GameObject[] candidateOnly;

  [Tooltip("Se muestran solo cuando el enemigo esta fijado.")]
  public GameObject[] lockedOnly;

  [Tooltip("Elemento que sube y baja suavemente (opcional).")]
  public RectTransform bob;

  [Tooltip("Elemento que gira mientras el enemigo esta fijado (opcional).")]
  public RectTransform spinWhenLocked;

  [Tooltip("Elemento que hace el 'golpe' de escala al fijar (opcional; vacio = Follow Root).")]
  public RectTransform punch;

  private CanvasGroup _group;
  private Canvas _canvas;
  private float[] _baseAlpha;

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

    if (tinted != null)
    {
      _baseAlpha = new float[tinted.Length];

      for (var i = 0; i < tinted.Length; i++)
      {
        _baseAlpha[i] = tinted[i] != null ? tinted[i].color.a : 1f;
      }
    }
  }

  public void SetColor(Color color)
  {
    Cache();

    if (tinted == null)
    {
      return;
    }

    for (var i = 0; i < tinted.Length; i++)
    {
      if (tinted[i] != null)
      {
        var c = color;
        c.a *= _baseAlpha[i];
        tinted[i].color = c;
      }
    }
  }

  public void SetLocked(bool locked)
  {
    SetActive(candidateOnly, !locked);
    SetActive(lockedOnly, locked);
  }

  /// <summary>Coloca el marcador en el punto de pantalla. False si queda detras de la camara.</summary>
  public bool Place(Vector3 screenPoint)
  {
    Cache();

    if (followRoot == null || screenPoint.z <= 0f)
    {
      return false;
    }

    var root = _canvas.rootCanvas;
    var cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
    var parent = followRoot.parent as RectTransform;

    if (parent != null &&
        RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, screenPoint, cam, out var world))
    {
      followRoot.position = world;
      return true;
    }

    return false;
  }

  private static void SetActive(GameObject[] objects, bool active)
  {
    if (objects == null)
    {
      return;
    }

    foreach (var go in objects)
    {
      if (go != null && go.activeSelf != active)
      {
        go.SetActive(active);
      }
    }
  }
}
