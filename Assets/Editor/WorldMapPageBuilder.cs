// WorldMapPageBuilder.cs
// Construye el contenido de la pantalla "Mapa" dentro del prefab PauseMenuUI:
// mapa a la izquierda (con zoom y desplazamiento), y a la derecha la leyenda,
// la brujula y un recuadro de notas, como en el boceto.
// Conserva el marco, el fondo y el titulo de la pantalla; solo reemplaza lo
// que hay dentro de "Content".
//
// Menu: Tools > Mapa > Construir pantalla de Mapa en el menu de pausa
// Despues todo se diseña en el prefab (doble clic): mueve, cambia sprites,
// colores, tipografias...

using Menus;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class WorldMapPageBuilder
{
  private static readonly Color Ink = new Color(0.16f, 0.12f, 0.08f, 1f);
  private static readonly Color Panel = new Color(0.98f, 0.94f, 0.84f, 1f);
  private static readonly Color MapBack = new Color(0.86f, 0.78f, 0.6f, 1f);

  [MenuItem("Tools/Mapa/Construir pantalla de Mapa en el menu de pausa", priority = 40)]
  private static void BuildInPauseMenu()
  {
    var menu = FindPauseMenuPrefab();

    if (menu == null)
    {
      EditorUtility.DisplayDialog("Mapa", "No encontre el prefab del menu de pausa (Tools > Menu de pausa).", "OK");
      return;
    }

    if (!EditorUtility.DisplayDialog("Mapa",
          "Se reemplazara el contenido de la pantalla 'Mapa' del menu de pausa (el marco y el titulo se conservan).\n\n" +
          "Si ya habias diseñado el interior de esa pantalla, se perdera.", "Construir", "Cancelar"))
    {
      return;
    }

    var data = WorldMapBaker.GetOrCreateData();
    var path = AssetDatabase.GetAssetPath(menu.gameObject);
    var root = PrefabUtility.LoadPrefabContents(path);

    try
    {
      var pause = root.GetComponent<PauseMenu>();
      var so = new SerializedObject(pause);
      var pages = so.FindProperty("_pages");
      PauseMenuPage page = null;

      for (var i = 0; i < pages.arraySize; i++)
      {
        var p = pages.GetArrayElementAtIndex(i).objectReferenceValue as PauseMenuPage;

        if (p != null && (p.pageName == "Mapa" || p.name == "Page_Mapa"))
        {
          page = p;
          break;
        }
      }

      if (page == null)
      {
        var mapIndex = so.FindProperty("_mapPage").intValue;

        if (mapIndex >= 0 && mapIndex < pages.arraySize)
        {
          page = pages.GetArrayElementAtIndex(mapIndex).objectReferenceValue as PauseMenuPage;
        }
      }

      if (page == null)
      {
        EditorUtility.DisplayDialog("Mapa", "No encontre la pantalla 'Mapa' en el menu de pausa.", "OK");
        return;
      }

      var content = page.transform.Find("Content") as RectTransform;

      if (content == null)
      {
        content = Rect("Content", page.transform);
        Stretch(content);
        content.offsetMin = new Vector2(40f, 40f);
        content.offsetMax = new Vector2(-40f, -160f);
      }

      for (var i = content.childCount - 1; i >= 0; i--)
      {
        Object.DestroyImmediate(content.GetChild(i).gameObject);
      }

      var old = page.GetComponent<WorldMapPage>();

      if (old != null)
      {
        Object.DestroyImmediate(old);
      }

      Build(page, content, data);
      PrefabUtility.SaveAsPrefabAsset(root, path);
    }
    finally
    {
      PrefabUtility.UnloadPrefabContents(root);
    }

    Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    var msg = data.IsBaked
      ? "Pantalla de Mapa construida."
      : "Pantalla de Mapa construida.\n\nAun no hay mapa horneado: usa Tools > Mapa > Crear area del mapa y luego Hornear mapa.";
    EditorUtility.DisplayDialog("Mapa", msg, "OK");
  }

  private static PauseMenu FindPauseMenuPrefab()
  {
    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
    {
      var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
      var menu = go != null ? go.GetComponent<PauseMenu>() : null;

      if (menu != null)
      {
        return menu;
      }
    }

    return null;
  }

  private static Sprite LoadSprite(string path)
  {
    return AssetDatabase.LoadAssetAtPath<Sprite>(path);
  }

  private static void Build(PauseMenuPage page, RectTransform content, WorldMapData data)
  {
    var arrow = LoadSprite("Assets/UI/Minimap/Minimap_Jugador.png");
    var dot = LoadSprite("Assets/UI/Minimap/Minimap_Punto.png");

    // ----- Mapa (izquierda) -----
    var frame = Rect("MarcoMapa", content);
    frame.anchorMin = new Vector2(0f, 0f);
    frame.anchorMax = new Vector2(0.66f, 1f);
    frame.offsetMin = Vector2.zero;
    frame.offsetMax = Vector2.zero;
    var frameImage = frame.gameObject.AddComponent<Image>();
    frameImage.color = Ink;
    frameImage.raycastTarget = false;

    var viewport = Rect("Viewport", frame);
    Stretch(viewport);
    viewport.offsetMin = new Vector2(4f, 4f);
    viewport.offsetMax = new Vector2(-4f, -4f);
    var viewportImage = viewport.gameObject.AddComponent<Image>();
    viewportImage.color = MapBack;
    viewportImage.raycastTarget = true; // recibe arrastre y rueda
    viewport.gameObject.AddComponent<RectMask2D>();

    var mapContent = Rect("Contenido", viewport);
    mapContent.anchorMin = mapContent.anchorMax = new Vector2(0.5f, 0.5f);
    mapContent.pivot = new Vector2(0.5f, 0.5f);
    mapContent.sizeDelta = new Vector2(560f, 440f);

    var baseImage = Rect("MapaBase", mapContent);
    Stretch(baseImage);
    var baseRaw = baseImage.gameObject.AddComponent<RawImage>();
    baseRaw.raycastTarget = false;
    baseRaw.texture = data != null ? data.baseMap : null;

    var overlay = Rect("CapaPintada", mapContent);
    Stretch(overlay);
    var overlayRaw = overlay.gameObject.AddComponent<RawImage>();
    overlayRaw.raycastTarget = false;
    overlayRaw.texture = data != null ? data.overlay : null;

    var icons = Rect("Iconos", mapContent);
    Stretch(icons);

    var template = Rect("IconoBase", icons);
    template.sizeDelta = new Vector2(28f, 28f);
    var templateImage = template.gameObject.AddComponent<Image>();
    templateImage.sprite = dot;
    templateImage.preserveAspect = true;
    templateImage.raycastTarget = false;
    template.gameObject.SetActive(false);

    var player = Rect("Jugador", icons);
    player.sizeDelta = new Vector2(40f, 40f);
    var playerImage = player.gameObject.AddComponent<Image>();
    playerImage.sprite = arrow;
    playerImage.color = new Color(1f, 0.62f, 0.2f);
    playerImage.preserveAspect = true;
    playerImage.raycastTarget = false;

    var notBaked = Text("SinMapa", viewport, "Mapa sin hornear\n(Tools > Mapa > Hornear mapa)", 26, new Color(Ink.r, Ink.g, Ink.b, 0.6f));
    Stretch(notBaked.rectTransform);
    notBaked.alignment = TextAnchor.MiddleCenter;

    var hint = Text("Controles", frame, "Rueda / Q·E / L2·R2: zoom   ·   Arrastrar / Flechas / Stick der.: mover   ·   C / R3: centrar", 15, new Color(Ink.r, Ink.g, Ink.b, 0.75f));
    var hintRect = hint.rectTransform;
    hintRect.anchorMin = new Vector2(0f, 0f);
    hintRect.anchorMax = new Vector2(1f, 0f);
    hintRect.pivot = new Vector2(0.5f, 1f);
    hintRect.anchoredPosition = new Vector2(0f, -4f);
    hintRect.sizeDelta = new Vector2(0f, 26f);
    hint.alignment = TextAnchor.MiddleCenter;

    // ----- Columna derecha -----
    var side = Rect("Lateral", content);
    side.anchorMin = new Vector2(0.69f, 0f);
    side.anchorMax = new Vector2(1f, 1f);
    side.offsetMin = Vector2.zero;
    side.offsetMax = Vector2.zero;

    // Leyenda
    var legendPanel = Box("Leyenda", side, new Vector2(0f, 0.5f), new Vector2(1f, 1f));
    var legendTitle = Text("Titulo", legendPanel, "Leyenda", 26, Ink);
    legendTitle.fontStyle = FontStyle.Bold;
    TopBar(legendTitle.rectTransform, 40f, -6f);
    legendTitle.alignment = TextAnchor.MiddleCenter;

    var legendList = Rect("Lista", legendPanel);
    Stretch(legendList);
    legendList.offsetMin = new Vector2(16f, 10f);
    legendList.offsetMax = new Vector2(-12f, -48f);
    var layout = legendList.gameObject.AddComponent<VerticalLayoutGroup>();
    layout.spacing = 4f;
    layout.childAlignment = TextAnchor.UpperLeft;
    layout.childControlWidth = true;
    layout.childControlHeight = true;
    layout.childForceExpandWidth = true;
    layout.childForceExpandHeight = false;

    var row = Rect("FilaBase", legendList);
    row.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
    var rowIcon = Rect("Icono", row);
    rowIcon.anchorMin = rowIcon.anchorMax = new Vector2(0f, 0.5f);
    rowIcon.pivot = new Vector2(0f, 0.5f);
    rowIcon.sizeDelta = new Vector2(26f, 26f);
    var rowIconImage = rowIcon.gameObject.AddComponent<Image>();
    rowIconImage.sprite = dot;
    rowIconImage.preserveAspect = true;
    rowIconImage.raycastTarget = false;
    var rowText = Text("Nombre", row, "Categoria", 20, Ink);
    Stretch(rowText.rectTransform);
    rowText.rectTransform.offsetMin = new Vector2(38f, 0f);
    rowText.alignment = TextAnchor.MiddleLeft;

    // Brujula (el mapa siempre tiene el norte arriba)
    var compass = Rect("Brujula", side);
    compass.anchorMin = compass.anchorMax = new Vector2(0.5f, 0.355f);
    compass.sizeDelta = new Vector2(120f, 120f);
    var ring = compass.gameObject.AddComponent<Image>();
    ring.sprite = dot;
    ring.color = Panel;
    ring.raycastTarget = false;
    CompassLetter(compass, "N", new Vector2(0f, 40f), true);
    CompassLetter(compass, "S", new Vector2(0f, -40f), false);
    CompassLetter(compass, "E", new Vector2(40f, 0f), false);
    CompassLetter(compass, "O", new Vector2(-40f, 0f), false);
    var needle = Rect("Aguja", compass);
    needle.sizeDelta = new Vector2(26f, 50f);
    var needleImage = needle.gameObject.AddComponent<Image>();
    needleImage.sprite = arrow;
    needleImage.color = new Color(0.8f, 0.2f, 0.2f);
    needleImage.preserveAspect = true;
    needleImage.raycastTarget = false;

    // Notas
    var notes = Box("Notas", side, new Vector2(0f, 0f), new Vector2(1f, 0.22f));
    var notesText = Text("Texto", notes, "Notas:\n", 18, Ink);
    Stretch(notesText.rectTransform);
    notesText.rectTransform.offsetMin = new Vector2(14f, 8f);
    notesText.rectTransform.offsetMax = new Vector2(-12f, -8f);
    notesText.alignment = TextAnchor.UpperLeft;

    // ----- Componente -----
    var map = page.gameObject.AddComponent<WorldMapPage>();
    var so = new SerializedObject(map);
    so.FindProperty("_data").objectReferenceValue = data;
    so.FindProperty("_viewport").objectReferenceValue = viewport;
    so.FindProperty("_content").objectReferenceValue = mapContent;
    so.FindProperty("_baseImage").objectReferenceValue = baseRaw;
    so.FindProperty("_overlayImage").objectReferenceValue = overlayRaw;
    so.FindProperty("_iconsRoot").objectReferenceValue = icons;
    so.FindProperty("_iconTemplate").objectReferenceValue = templateImage;
    so.FindProperty("_playerIcon").objectReferenceValue = player;
    so.FindProperty("_notBakedMessage").objectReferenceValue = notBaked.gameObject;
    so.FindProperty("_legendRoot").objectReferenceValue = legendList;
    so.FindProperty("_legendRowTemplate").objectReferenceValue = row;
    so.ApplyModifiedPropertiesWithoutUndo();
  }

  // ---------- Ayudas ----------

  private static RectTransform Box(string name, RectTransform parent, Vector2 min, Vector2 max)
  {
    var rect = Rect(name, parent);
    rect.anchorMin = min;
    rect.anchorMax = max;
    rect.offsetMin = Vector2.zero;
    rect.offsetMax = Vector2.zero;
    var image = rect.gameObject.AddComponent<Image>();
    image.color = Panel;
    image.raycastTarget = false;
    var outline = rect.gameObject.AddComponent<Outline>();
    outline.effectColor = Ink;
    outline.effectDistance = new Vector2(2f, -2f);
    return rect;
  }

  private static void TopBar(RectTransform rect, float height, float y)
  {
    rect.anchorMin = new Vector2(0f, 1f);
    rect.anchorMax = new Vector2(1f, 1f);
    rect.pivot = new Vector2(0.5f, 1f);
    rect.anchoredPosition = new Vector2(0f, y);
    rect.sizeDelta = new Vector2(0f, height);
  }

  private static void CompassLetter(RectTransform parent, string letter, Vector2 position, bool bold)
  {
    var text = Text(letter, parent, letter, bold ? 24 : 18, Ink);
    text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
    text.alignment = TextAnchor.MiddleCenter;
    text.rectTransform.sizeDelta = new Vector2(30f, 30f);
    text.rectTransform.anchoredPosition = position;
  }

  private static Text Text(string name, Transform parent, string value, int size, Color color)
  {
    var rect = Rect(name, parent);
    var text = rect.gameObject.AddComponent<Text>();
    text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    text.text = value;
    text.fontSize = size;
    text.color = color;
    text.raycastTarget = false;
    text.horizontalOverflow = HorizontalWrapMode.Wrap;
    text.verticalOverflow = VerticalWrapMode.Overflow;
    return text;
  }

  private static RectTransform Rect(string name, Transform parent)
  {
    var go = new GameObject(name, typeof(RectTransform));
    go.transform.SetParent(parent, false);
    return (RectTransform)go.transform;
  }

  private static void Stretch(RectTransform rect)
  {
    rect.anchorMin = Vector2.zero;
    rect.anchorMax = Vector2.one;
    rect.offsetMin = Vector2.zero;
    rect.offsetMax = Vector2.zero;
  }
}
