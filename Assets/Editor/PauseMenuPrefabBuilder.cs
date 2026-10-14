// PauseMenuPrefabBuilder.cs
// Crea el prefab por defecto del menu de pausa (Assets/UI/PauseMenu/PauseMenuUI)
// con tres pantallas en carrusel: Pausa (Continuar / Ajustes / Salir al menu),
// Estado y Mapa (estas dos vacias por ahora). Todo es provisional y se diseña
// en el prefab: fondos, botones, flechas, X de cerrar, puntos indicadores.
// Menu: Tools > Menu de pausa > Agregar menu de pausa a la escena abierta.

using System.IO;
using Menus;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class PauseMenuPrefabBuilder
{
  private const string Folder = "Assets/UI/PauseMenu";
  private const string PrefabName = "PauseMenuUI";

  private static readonly Color Parchment = new Color(0.96f, 0.9f, 0.76f, 1f);
  private static readonly Color Frame = new Color(0.55f, 0.68f, 0.78f, 1f);
  private static readonly Color Ink = new Color(0.16f, 0.12f, 0.08f, 1f);
  private static readonly Color Blue = new Color(0.2f, 0.5f, 0.85f, 1f);

  static PauseMenuPrefabBuilder()
  {
    EditorApplication.delayCall += () =>
    {
      if (!EditorApplication.isPlayingOrWillChangePlaymode && FindPrefab() == null)
      {
        Save(Build());
        Debug.Log($"[Pausa] Prefab creado en {Folder}/{PrefabName}. Usa Tools > Menu de pausa > Agregar menu de pausa a la escena abierta.");
      }
    };
  }

  [MenuItem("Tools/Menu de pausa/Agregar menu de pausa a la escena abierta")]
  private static void AddToScene()
  {
    if (Object.FindAnyObjectByType<PauseMenu>(FindObjectsInactive.Include) != null)
    {
      EditorUtility.DisplayDialog("Menu de pausa", "La escena ya tiene un PauseMenu.", "OK");
      return;
    }

    var prefab = FindPrefab();

    if (prefab == null)
    {
      prefab = Save(Build()).GetComponent<PauseMenu>();
    }

    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject);
    Undo.RegisterCreatedObjectUndo(instance, "Agregar menu de pausa");
    EditorSceneManager.MarkSceneDirty(instance.scene);
    Selection.activeObject = instance;
  }

  [MenuItem("Tools/Menu de pausa/Reconstruir pantalla de Estado (en el prefab)")]
  private static void RebuildStatusPage()
  {
    var prefab = FindPrefab();

    if (prefab == null)
    {
      EditorUtility.DisplayDialog("Menu de pausa", "No hay prefab del menu de pausa todavia.", "OK");
      return;
    }

    if (!EditorUtility.DisplayDialog("Menu de pausa",
          "Se reemplazara la pantalla 'Estado' del prefab por la version con modelo, equipamiento y coleccionables.\n\n" +
          "Las otras pantallas no se tocan. Si ya habias diseñado la pantalla de Estado, se perdera.", "Reemplazar", "Cancelar"))
    {
      return;
    }

    var path = AssetDatabase.GetAssetPath(prefab.gameObject);
    var root = PrefabUtility.LoadPrefabContents(path);

    try
    {
      var menu = root.GetComponent<PauseMenu>();
      var so = new SerializedObject(menu);
      var pages = so.FindProperty("_pages");
      var index = -1;

      for (var i = 0; i < pages.arraySize; i++)
      {
        var page = pages.GetArrayElementAtIndex(i).objectReferenceValue as PauseMenuPage;

        if (page != null && (page.pageName == "Estado" || page.name == "Page_Estado"))
        {
          index = i;
          break;
        }
      }

      if (index < 0)
      {
        index = Mathf.Min(1, pages.arraySize);
        pages.InsertArrayElementAtIndex(index);
        pages.GetArrayElementAtIndex(index).objectReferenceValue = null;
      }

      var old = pages.GetArrayElementAtIndex(index).objectReferenceValue as PauseMenuPage;
      var parent = old != null ? (RectTransform)old.transform.parent : (RectTransform)root.transform.Find("Carousel");
      var sibling = old != null ? old.transform.GetSiblingIndex() : parent.childCount;

      var status = BuildStatusPage(parent);
      status.transform.SetSiblingIndex(sibling);

      if (old != null)
      {
        Object.DestroyImmediate(old.gameObject);
      }

      pages.GetArrayElementAtIndex(index).objectReferenceValue = status;
      so.ApplyModifiedPropertiesWithoutUndo();
      PrefabUtility.SaveAsPrefabAsset(root, path);
    }
    finally
    {
      PrefabUtility.UnloadPrefabContents(root);
    }

    Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    EditorUtility.DisplayDialog("Menu de pausa", "Pantalla de Estado reconstruida.", "OK");
  }

  [MenuItem("Tools/Menu de pausa/Seleccionar prefab del menu de pausa")]
  private static void SelectPrefab()
  {
    var prefab = FindPrefab();

    if (prefab != null)
    {
      Selection.activeObject = prefab.gameObject;
      EditorGUIUtility.PingObject(prefab.gameObject);
    }
  }

  private static PauseMenu FindPrefab()
  {
    PauseMenu fallback = null;

    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
    {
      var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
      var menu = go != null ? go.GetComponent<PauseMenu>() : null;

      if (menu == null)
      {
        continue;
      }

      if (go.name == PrefabName)
      {
        return menu;
      }

      fallback ??= menu;
    }

    return fallback;
  }

  private static GameObject Save(GameObject root)
  {
    EnsureFolder(Folder);
    var path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{PrefabName}.prefab");
    var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
    Object.DestroyImmediate(root);
    return prefab;
  }

  private static void EnsureFolder(string folder)
  {
    if (AssetDatabase.IsValidFolder(folder))
    {
      return;
    }

    var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
    EnsureFolder(parent);
    AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
  }

  // ---------- Prefab por defecto ----------

  private static GameObject Build()
  {
    var root = new GameObject(PrefabName, typeof(RectTransform));

    var canvas = root.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = 700;

    var scaler = root.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920f, 1080f);
    scaler.matchWidthOrHeight = 0.5f;

    root.AddComponent<GraphicRaycaster>();
    root.AddComponent<CanvasGroup>();
    var menu = root.AddComponent<PauseMenu>();
    var rootRect = (RectTransform)root.transform;

    // Oscurecer el juego detras
    var dim = Graphic<Image>("Dim", rootRect);
    Stretch(dim.rectTransform);
    dim.color = new Color(0.12f, 0.1f, 0.08f, 0.6f);

    // Carrusel
    var carousel = Rect("Carousel", rootRect);
    carousel.sizeDelta = new Vector2(1000f, 680f);

    var pause = BuildPausePage(carousel, out var continueButton, out var settingsButton, out var exitButton);
    var state = BuildStatusPage(carousel);
    var map = BuildEmptyPage(carousel, "Mapa");

    // Flechas y cerrar
    var prev = BuildButton(rootRect, "PreviousPage", "<", 72, new Vector2(80f, 120f), Blue, Color.white);
    Place(prev, new Vector2(0.5f, 0.5f), new Vector2(-600f, 0f));
    var next = BuildButton(rootRect, "NextPage", ">", 72, new Vector2(80f, 120f), Blue, Color.white);
    Place(next, new Vector2(0.5f, 0.5f), new Vector2(600f, 0f));
    var close = BuildButton(rootRect, "Close", "X", 40, new Vector2(70f, 70f), Blue, Color.white);
    Place(close, new Vector2(0.5f, 0.5f), new Vector2(470f, 310f));

    // Indicador de pantallas
    var dots = Rect("Dots", rootRect);
    dots.anchoredPosition = new Vector2(0f, -375f);
    dots.sizeDelta = new Vector2(200f, 24f);
    var dotsLayout = dots.gameObject.AddComponent<HorizontalLayoutGroup>();
    dotsLayout.spacing = 12f;
    dotsLayout.childAlignment = TextAnchor.MiddleCenter;
    dotsLayout.childControlWidth = true;
    dotsLayout.childControlHeight = true;
    dotsLayout.childForceExpandWidth = false;
    dotsLayout.childForceExpandHeight = false;
    var dot = Graphic<Image>("Dot", dots);
    var dotLayout = dot.gameObject.AddComponent<LayoutElement>();
    dotLayout.preferredWidth = 18f;
    dotLayout.preferredHeight = 18f;

    // Ayuda inferior
    var hint = Text("Hint", rootRect, "A / D  ·  L1 / R1: cambiar pantalla      Esc / Options: cerrar", 22, new Color(1f, 1f, 1f, 0.7f));
    hint.alignment = TextAnchor.MiddleCenter;
    hint.rectTransform.anchorMin = new Vector2(0f, 0f);
    hint.rectTransform.anchorMax = new Vector2(1f, 0f);
    hint.rectTransform.pivot = new Vector2(0.5f, 0f);
    hint.rectTransform.sizeDelta = new Vector2(0f, 40f);
    hint.rectTransform.anchoredPosition = new Vector2(0f, 24f);

    var so = new SerializedObject(menu);
    var pages = so.FindProperty("_pages");
    pages.arraySize = 3;
    pages.GetArrayElementAtIndex(0).objectReferenceValue = pause;
    pages.GetArrayElementAtIndex(1).objectReferenceValue = state;
    pages.GetArrayElementAtIndex(2).objectReferenceValue = map;
    so.FindProperty("_pausePage").intValue = 0;
    so.FindProperty("_mapPage").intValue = 2;
    so.FindProperty("_continueButton").objectReferenceValue = continueButton;
    so.FindProperty("_settingsButton").objectReferenceValue = settingsButton;
    so.FindProperty("_exitToMenuButton").objectReferenceValue = exitButton;
    so.FindProperty("_previousButton").objectReferenceValue = prev;
    so.FindProperty("_nextButton").objectReferenceValue = next;
    so.FindProperty("_closeButton").objectReferenceValue = close;
    so.FindProperty("_dotTemplate").objectReferenceValue = dot;
    so.ApplyModifiedPropertiesWithoutUndo();

    return root;
  }

  private static PauseMenuPage BuildPausePage(RectTransform parent, out Button continueButton, out Button settingsButton, out Button exitButton)
  {
    var page = BuildPageFrame(parent, "En pausa", out var content);
    page.name = "Page_Pausa";

    var list = Rect("Buttons", content);
    list.anchorMin = new Vector2(0.5f, 0f);
    list.anchorMax = new Vector2(0.5f, 1f);
    list.offsetMin = new Vector2(-170f, 60f);
    list.offsetMax = new Vector2(170f, -40f);
    var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
    layout.spacing = 22f;
    layout.childAlignment = TextAnchor.MiddleCenter;
    layout.childControlWidth = true;
    layout.childControlHeight = true;
    layout.childForceExpandWidth = true;
    layout.childForceExpandHeight = false;

    continueButton = BuildListButton(list, "Continuar");
    settingsButton = BuildListButton(list, "Ajustes");
    exitButton = BuildListButton(list, "Salir al menú");

    var p = page.GetComponent<PauseMenuPage>();
    p.pageName = "Pausa";
    p.firstSelected = continueButton;
    return p;
  }

  // ---------- Pantalla de Estado ----------
  // Izquierda: modelo 3D del personaje (idle) y su nombre.
  // Derecha: equipamiento (espada y escudo) y cuadricula de coleccionables.

  private static PauseMenuPage BuildStatusPage(RectTransform parent)
  {
    var page = BuildPageFrame(parent, "Estado", out var content);
    page.name = "Page_Estado";

    // Titulo a la izquierda, como en el boceto.
    var banner = (RectTransform)page.Find("TitleBanner");
    banner.anchorMin = banner.anchorMax = new Vector2(0f, 1f);
    banner.pivot = new Vector2(0f, 1f);
    banner.anchoredPosition = new Vector2(50f, -36f);
    banner.sizeDelta = new Vector2(360f, 96f);

    content.offsetMax = new Vector2(-40f, -40f);

    // Modelo del personaje
    var modelArea = Rect("Model", content);
    modelArea.anchorMin = new Vector2(0f, 0f);
    modelArea.anchorMax = new Vector2(0.42f, 1f);
    modelArea.offsetMin = new Vector2(0f, 90f);
    modelArea.offsetMax = new Vector2(0f, -110f);
    var raw = modelArea.gameObject.AddComponent<RawImage>();
    raw.raycastTarget = false;
    var portrait = modelArea.gameObject.AddComponent<PlayerPortrait>();
    var pso = new SerializedObject(portrait);
    pso.FindProperty("_animationMode").enumValueIndex = (int)PlayerPortrait.AnimationMode.AnimacionPropia;
    pso.FindProperty("_focusHeight").floatValue = 0.5f;
    pso.FindProperty("_framing").floatValue = 0.62f;
    pso.FindProperty("_yaw").floatValue = -15f;
    pso.FindProperty("_pitch").floatValue = 4f;
    pso.ApplyModifiedPropertiesWithoutUndo();

    // Nombre
    var nameBanner = Graphic<Image>("NameBanner", content);
    nameBanner.color = new Color(0.98f, 0.95f, 0.86f, 1f);
    var nameOutline = nameBanner.gameObject.AddComponent<Outline>();
    nameOutline.effectColor = Ink;
    nameOutline.effectDistance = new Vector2(2f, -2f);
    var nameRect = nameBanner.rectTransform;
    nameRect.anchorMin = nameRect.anchorMax = new Vector2(0.21f, 0f);
    nameRect.pivot = new Vector2(0.5f, 0f);
    nameRect.sizeDelta = new Vector2(300f, 84f);
    nameRect.anchoredPosition = new Vector2(0f, 4f);
    var nameText = Text("Name", nameRect, "Chopi", 50, Ink);
    Stretch(nameText.rectTransform);
    nameText.alignment = TextAnchor.MiddleCenter;
    nameText.fontStyle = FontStyle.Bold;

    // Columna derecha
    var right = Rect("RightColumn", content);
    right.anchorMin = new Vector2(0.46f, 0f);
    right.anchorMax = new Vector2(1f, 1f);
    right.offsetMin = Vector2.zero;
    right.offsetMax = Vector2.zero;

    // Equipamiento
    var equipHeader = Header("EquipmentHeader", right, "Equipamiento", new Vector2(0f, 0f));
    var equip = Rect("Equipment", right);
    equip.anchorMin = equip.anchorMax = new Vector2(0f, 1f);
    equip.pivot = new Vector2(0f, 1f);
    equip.anchoredPosition = new Vector2(10f, -70f);
    equip.sizeDelta = new Vector2(480f, 130f);
    var equipLayout = equip.gameObject.AddComponent<HorizontalLayoutGroup>();
    equipLayout.spacing = 20f;
    equipLayout.childAlignment = TextAnchor.MiddleLeft;
    equipLayout.childControlWidth = true;
    equipLayout.childControlHeight = true;
    equipLayout.childForceExpandWidth = false;
    equipLayout.childForceExpandHeight = false;
    var swordSlot = EquipmentSlotView(equip, global::EquipmentSlot.Espada);
    var shieldSlot = EquipmentSlotView(equip, global::EquipmentSlot.Escudo);

    // Coleccionables
    var collHeader = Header("CollectiblesHeader", right, "Coleccionables", new Vector2(0f, -225f));
    var grid = Rect("Collectibles", right);
    grid.anchorMin = grid.anchorMax = new Vector2(0f, 1f);
    grid.pivot = new Vector2(0f, 1f);
    grid.anchoredPosition = new Vector2(10f, -295f);
    grid.sizeDelta = new Vector2(500f, 240f);
    var gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
    gridLayout.cellSize = new Vector2(108f, 108f);
    gridLayout.spacing = new Vector2(14f, 14f);
    gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
    gridLayout.constraintCount = 4;
    gridLayout.childAlignment = TextAnchor.UpperLeft;

    var slot = CollectibleSlotTemplate(grid);

    var status = page.gameObject.AddComponent<StatusPage>();
    var sso = new SerializedObject(status);
    sso.FindProperty("_slotTemplate").objectReferenceValue = slot;
    sso.FindProperty("_minSlots").intValue = 8;
    var equipSlots = sso.FindProperty("_equipmentSlots");
    equipSlots.arraySize = 2;
    equipSlots.GetArrayElementAtIndex(0).objectReferenceValue = swordSlot;
    equipSlots.GetArrayElementAtIndex(1).objectReferenceValue = shieldSlot;
    sso.ApplyModifiedPropertiesWithoutUndo();

    var p = page.GetComponent<PauseMenuPage>();
    p.pageName = "Estado";
    return p;
  }

  private static RectTransform Header(string name, RectTransform parent, string label, Vector2 position)
  {
    var image = Graphic<Image>(name, parent);
    image.color = new Color(0.98f, 0.95f, 0.86f, 1f);
    var outline = image.gameObject.AddComponent<Outline>();
    outline.effectColor = Ink;
    outline.effectDistance = new Vector2(1.5f, -1.5f);
    var rect = image.rectTransform;
    rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
    rect.pivot = new Vector2(0f, 1f);
    rect.anchoredPosition = position;
    rect.sizeDelta = new Vector2(280f, 54f);
    var text = Text("Label", rect, label, 32, Ink);
    Stretch(text.rectTransform);
    text.alignment = TextAnchor.MiddleCenter;
    text.fontStyle = FontStyle.Bold;
    return rect;
  }

  /// <summary>Casilla de equipamiento: vacia muestra el nombre tenue; llena, el icono del objeto.</summary>
  private static CollectibleSlot EquipmentSlotView(RectTransform parent, global::EquipmentSlot equipment)
  {
    var slot = CollectibleSlotTemplate(parent);
    slot.name = $"Slot_{equipment}";
    slot.equipment = equipment;

    var element = slot.gameObject.AddComponent<LayoutElement>();
    element.preferredWidth = 125f;
    element.preferredHeight = 125f;

    var empty = Text("EmptyName", slot.transform, equipment.ToString(), 22, new Color(1f, 1f, 1f, 0.45f));
    Stretch(empty.rectTransform);
    empty.alignment = TextAnchor.MiddleCenter;
    slot.emptyOnly = new[] { empty.gameObject };

    return slot;
  }

  private static CollectibleSlot CollectibleSlotTemplate(RectTransform parent)
  {
    var frame = Graphic<Image>("Slot", parent);
    frame.color = Color.white;
    frame.raycastTarget = true;
    var outline = frame.gameObject.AddComponent<Outline>();
    outline.effectColor = new Color(0.45f, 0.35f, 0.2f, 1f);
    outline.effectDistance = new Vector2(2f, -2f);

    var button = frame.gameObject.AddComponent<Button>();
    var colors = button.colors;
    colors.normalColor = new Color(0.17f, 0.15f, 0.13f, 0.9f);
    colors.highlightedColor = new Color(0.35f, 0.3f, 0.22f, 1f);
    colors.selectedColor = new Color(0.85f, 0.65f, 0.25f, 1f);
    colors.pressedColor = new Color(0.95f, 0.75f, 0.35f, 1f);
    colors.disabledColor = new Color(0.17f, 0.15f, 0.13f, 0.9f);
    colors.fadeDuration = 0.08f;
    button.colors = colors;

    var icon = Graphic<Image>("Icon", frame.rectTransform);
    Stretch(icon.rectTransform);
    icon.rectTransform.offsetMin = new Vector2(10f, 10f);
    icon.rectTransform.offsetMax = new Vector2(-10f, -10f);
    icon.preserveAspect = true;
    icon.enabled = false;

    var label = Text("Label", frame.rectTransform, "", 18, new Color(1f, 0.95f, 0.85f, 1f));
    Stretch(label.rectTransform);
    label.rectTransform.offsetMin = new Vector2(6f, 6f);
    label.rectTransform.offsetMax = new Vector2(-6f, -6f);
    label.alignment = TextAnchor.MiddleCenter;
    label.enabled = false;

    var slot = frame.gameObject.AddComponent<CollectibleSlot>();
    slot.icon = icon;
    slot.fallbackLabel = label;
    slot.emptyOnly = new GameObject[0];
    return slot;
  }

  private static PauseMenuPage BuildEmptyPage(RectTransform parent, string title)
  {
    var page = BuildPageFrame(parent, title, out var content);
    page.name = $"Page_{title}";

    var soon = Text("Placeholder", content, "Próximamente", 36, new Color(Ink.r, Ink.g, Ink.b, 0.4f));
    Stretch(soon.rectTransform);
    soon.alignment = TextAnchor.MiddleCenter;

    var p = page.GetComponent<PauseMenuPage>();
    p.pageName = title;
    return p;
  }

  /// <summary>Marco + pergamino + titulo. 'content' es la zona bajo el titulo.</summary>
  private static RectTransform BuildPageFrame(RectTransform parent, string title, out RectTransform content)
  {
    var page = Rect("Page", parent);
    Stretch(page);
    page.gameObject.AddComponent<CanvasGroup>();
    page.gameObject.AddComponent<PauseMenuPage>();

    var frame = Graphic<Image>("Frame", page);
    Stretch(frame.rectTransform);
    frame.color = Frame;

    var background = Graphic<Image>("Background", page);
    Stretch(background.rectTransform);
    background.rectTransform.offsetMin = new Vector2(24f, 24f);
    background.rectTransform.offsetMax = new Vector2(-24f, -24f);
    background.color = Parchment;

    var titleBanner = Graphic<Image>("TitleBanner", page);
    titleBanner.color = new Color(0.98f, 0.95f, 0.86f, 1f);
    var bannerRect = titleBanner.rectTransform;
    bannerRect.anchorMin = bannerRect.anchorMax = new Vector2(0.5f, 1f);
    bannerRect.pivot = new Vector2(0.5f, 1f);
    bannerRect.sizeDelta = new Vector2(440f, 100f);
    bannerRect.anchoredPosition = new Vector2(0f, -40f);
    var outline = titleBanner.gameObject.AddComponent<Outline>();
    outline.effectColor = Ink;
    outline.effectDistance = new Vector2(2f, -2f);

    var titleText = Text("Title", bannerRect, title, 60, Ink);
    Stretch(titleText.rectTransform);
    titleText.alignment = TextAnchor.MiddleCenter;
    titleText.fontStyle = FontStyle.Bold;

    content = Rect("Content", page);
    Stretch(content);
    content.offsetMin = new Vector2(40f, 40f);
    content.offsetMax = new Vector2(-40f, -160f);

    return page;
  }

  private static Button BuildListButton(RectTransform parent, string label)
  {
    var button = BuildButton(parent, label.Replace(" ", ""), label, 34, new Vector2(340f, 70f),
      new Color(0.93f, 0.9f, 0.82f, 1f), Ink);
    var element = button.gameObject.AddComponent<LayoutElement>();
    element.preferredHeight = 70f;
    element.minHeight = 70f;
    return button;
  }

  private static Button BuildButton(RectTransform parent, string name, string label, int fontSize, Vector2 size, Color color, Color textColor)
  {
    var image = Graphic<Image>(name, parent);
    image.rectTransform.sizeDelta = size;
    image.color = Color.white;
    image.raycastTarget = true;

    var outline = image.gameObject.AddComponent<Outline>();
    outline.effectColor = new Color(0.25f, 0.4f, 0.55f, 1f);
    outline.effectDistance = new Vector2(2f, -2f);

    var button = image.gameObject.AddComponent<Button>();
    var colors = button.colors;
    colors.normalColor = color;
    colors.highlightedColor = Color.Lerp(color, Color.white, 0.4f);
    colors.selectedColor = new Color(1f, 0.85f, 0.45f, 1f);
    colors.pressedColor = Color.Lerp(color, Color.black, 0.2f);
    colors.fadeDuration = 0.08f;
    button.colors = colors;

    var text = Text("Label", image.rectTransform, label, fontSize, textColor);
    Stretch(text.rectTransform);
    text.alignment = TextAnchor.MiddleCenter;

    return button;
  }

  private static void Place(Button button, Vector2 anchor, Vector2 position)
  {
    var rect = (RectTransform)button.transform;
    rect.anchorMin = rect.anchorMax = anchor;
    rect.anchoredPosition = position;
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

  private static T Graphic<T>(string name, Transform parent) where T : Graphic
  {
    var graphic = Rect(name, parent).gameObject.AddComponent<T>();
    graphic.raycastTarget = false;
    return graphic;
  }

  private static Text Text(string name, Transform parent, string value, int size, Color color)
  {
    var text = Graphic<Text>(name, parent);
    text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    text.text = value;
    text.fontSize = size;
    text.color = color;
    text.horizontalOverflow = HorizontalWrapMode.Wrap;
    text.verticalOverflow = VerticalWrapMode.Overflow;
    return text;
  }
}
