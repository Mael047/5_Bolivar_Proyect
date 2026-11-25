// ShieldPuzzleSetup.cs
// Tools > Puzzle escudos > Montar inventario de escudos (escena abierta)
// Cambia el puzzle de escudos (PuzzleArea del jardin) de "agarrar con la
// mano" a inventario + postes con menu:
//   1. Genera un icono de cada escudo a partir de su modelo 3D
//      (Assets/UI/Escudos/Escudo_<id>.png) y la silueta de casilla vacia.
//      Si ya existen no se rehacen (puedes cambiarlos por los tuyos).
//   2. Crea/actualiza el catalogo (Assets/UI/Escudos/CatalogoEscudos).
//   3. Crea el menu de los postes (Assets/UI/Escudos/ShieldSelectUI) si no
//      existe.
//   4. Añade las 5 casillas al HUD (PlayerHUD > Escudos), junto a la llave.
//   5. En la escena: ShieldPickup en cada escudo (ya no se agarran con la
//      mano) y ShieldPedestal en cada poste.
//   6. El escudo de Peru que da el puzzle de cartas (Escudo_Peru.prefab)
//      tambien se recoge al inventario.
// Se puede volver a ejecutar sin duplicar nada.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class ShieldPuzzleSetup
{
  private const string Folder = "Assets/UI/Escudos";
  private const string CatalogPath = Folder + "/CatalogoEscudos.asset";
  private const string MenuPath = Folder + "/ShieldSelectUI.prefab";
  private const string EmptyIconPath = Folder + "/Escudo_Vacio.png";
  private const string HudPath = "Assets/UI/Player/PlayerHUD.prefab";
  private const string PeruPrefabPath = "Assets/Prefabs/Puzzles/Escudo_Peru.prefab";

  private static readonly string[] PreferredOrder = { "Venezuela", "Colombia", "Ecuador", "Peru", "Bolivia" };
  private static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
  {
    { "Peru", "Perú" }
  };

  [MenuItem("Tools/Puzzle escudos/Montar inventario de escudos (escena abierta)")]
  private static void Run()
  {
    if (EditorApplication.isPlaying)
    {
      EditorUtility.DisplayDialog("Escudos", "Sal del modo Play primero.", "OK");
      return;
    }

    var scene = EditorSceneManager.GetActiveScene();
    var roots = scene.GetRootGameObjects();

    // Puzzle de escudos = PuzzleManager que no es el de las cartas.
    var manager = roots.SelectMany(r => r.GetComponentsInChildren<PuzzleManager>(true))
      .FirstOrDefault(m => m.GetComponent<CardOrderPuzzle>() == null);

    if (manager == null)
    {
      EditorUtility.DisplayDialog("Escudos", "No encontre el puzzle de escudos (PuzzleArea) en esta escena. Abre Jardin_Principal.", "OK");
      return;
    }

    // Los huecos del PuzzleManager (pueden estar fuera de su objeto).
    var slotList = new List<PuzzleSlot>();
    var slotsProp = new SerializedObject(manager).FindProperty("_slots");

    for (var i = 0; slotsProp != null && i < slotsProp.arraySize; i++)
    {
      if (slotsProp.GetArrayElementAtIndex(i).objectReferenceValue is PuzzleSlot s && !slotList.Contains(s)) slotList.Add(s);
    }

    var searchRoot = manager.transform.parent != null ? manager.transform.parent : manager.transform;

    foreach (var s in searchRoot.GetComponentsInChildren<PuzzleSlot>(true))
    {
      if (!slotList.Contains(s)) slotList.Add(s);
    }

    var slots = slotList.ToArray();
    var ids = new HashSet<string>(slots.Select(s => s.AcceptedPieceId).Where(s => !string.IsNullOrEmpty(s)), StringComparer.OrdinalIgnoreCase);
    var pieces = roots.SelectMany(r => r.GetComponentsInChildren<PuzzlePiece>(true))
      .Where(p => ids.Contains(p.PieceId)).ToList();

    if (!AssetDatabase.IsValidFolder(Folder))
    {
      AssetDatabase.CreateFolder("Assets/UI", "Escudos");
    }

    // 1. Iconos
    var log = new System.Text.StringBuilder();
    var icons = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
    Texture2D firstIcon = null;

    foreach (var id in ids)
    {
      var path = Folder + "/Escudo_" + id + ".png";
      var piece = pieces.FirstOrDefault(p => string.Equals(p.PieceId, id, StringComparison.OrdinalIgnoreCase));

      if (!File.Exists(path) && piece != null)
      {
        var slot = slots.FirstOrDefault(s => string.Equals(s.AcceptedPieceId, id, StringComparison.OrdinalIgnoreCase));
        var tex = RenderIcon(piece.gameObject, slot != null ? slot.transform.rotation : piece.transform.rotation);

        if (tex != null)
        {
          File.WriteAllBytes(path, tex.EncodeToPNG());
          if (firstIcon == null) firstIcon = tex;
        }
        else
        {
          log.AppendLine("No pude generar el icono de " + id);
        }
      }
    }

    if (!File.Exists(EmptyIconPath))
    {
      var source = firstIcon;

      if (source == null)
      {
        var existing = ids.Select(id => Folder + "/Escudo_" + id + ".png").FirstOrDefault(File.Exists);
        if (existing != null)
        {
          source = new Texture2D(2, 2);
          source.LoadImage(File.ReadAllBytes(existing));
        }
      }

      if (source != null)
      {
        File.WriteAllBytes(EmptyIconPath, MakeSilhouette(source).EncodeToPNG());
      }
    }

    AssetDatabase.Refresh();

    foreach (var id in ids)
    {
      var sprite = MakeSprite(Folder + "/Escudo_" + id + ".png");
      if (sprite != null) icons[id] = sprite;
    }

    var emptySprite = MakeSprite(EmptyIconPath);

    // 2. Catalogo
    var catalog = AssetDatabase.LoadAssetAtPath<ShieldCatalog>(CatalogPath);

    if (catalog == null)
    {
      catalog = ScriptableObject.CreateInstance<ShieldCatalog>();
      AssetDatabase.CreateAsset(catalog, CatalogPath);
    }

    var cso = new SerializedObject(catalog);
    var entries = cso.FindProperty("_entries");
    var ordered = ids.OrderBy(id =>
    {
      var i = Array.FindIndex(PreferredOrder, p => string.Equals(p, id, StringComparison.OrdinalIgnoreCase));
      return i < 0 ? 100 : i;
    }).ToList();

    // Conserva nombres e iconos que hayas cambiado a mano.
    var previous = new Dictionary<string, (string name, Object icon)>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < entries.arraySize; i++)
    {
      var e = entries.GetArrayElementAtIndex(i);
      var eid = e.FindPropertyRelative("id").stringValue;
      if (!string.IsNullOrEmpty(eid)) previous[eid] = (e.FindPropertyRelative("displayName").stringValue, e.FindPropertyRelative("icon").objectReferenceValue);
    }

    entries.arraySize = ordered.Count;

    for (var i = 0; i < ordered.Count; i++)
    {
      var id = ordered[i];
      var e = entries.GetArrayElementAtIndex(i);
      e.FindPropertyRelative("id").stringValue = id;
      previous.TryGetValue(id, out var old);
      e.FindPropertyRelative("displayName").stringValue = !string.IsNullOrEmpty(old.name) ? old.name : (DisplayNames.TryGetValue(id, out var dn) ? dn : id);
      e.FindPropertyRelative("icon").objectReferenceValue = old.icon != null ? old.icon : (icons.TryGetValue(id, out var sp) ? sp : null);
    }

    if (cso.FindProperty("_emptyIcon").objectReferenceValue == null)
    {
      cso.FindProperty("_emptyIcon").objectReferenceValue = emptySprite;
    }

    cso.ApplyModifiedPropertiesWithoutUndo();
    EditorUtility.SetDirty(catalog);

    // 3. Menu de los postes
    var font = FindFont();
    var menu = AssetDatabase.LoadAssetAtPath<ShieldSelectMenu>(MenuPath);

    if (menu == null)
    {
      menu = BuildMenu(font);
    }

    // 4. HUD
    var hudLog = SetupHud(catalog);

    // 5. Escena
    var messageView = FindMessageView();

    foreach (var piece in pieces)
    {
      var go = piece.gameObject;
      var pickup = go.GetComponent<ShieldPickup>();
      if (pickup == null) pickup = Undo.AddComponent<ShieldPickup>(go);
      AssignPickup(pickup, catalog, messageView);
      PrepareObject(go);
      log.AppendLine("Escudo " + piece.PieceId + " (" + go.name + ")" + (go.activeInHierarchy ? "" : "  [esta desactivado]"));
    }

    foreach (var slot in slots)
    {
      var pedestal = slot.GetComponent<ShieldPedestal>();
      if (pedestal == null) pedestal = Undo.AddComponent<ShieldPedestal>(slot.gameObject);
      var so = new SerializedObject(pedestal);
      so.FindProperty("_catalog").objectReferenceValue = catalog;
      so.FindProperty("_menuPrefab").objectReferenceValue = menu;
      so.FindProperty("_manager").objectReferenceValue = manager;
      so.FindProperty("_messageView").objectReferenceValue = messageView;
      so.ApplyModifiedPropertiesWithoutUndo();
    }

    // 6. Escudo de Peru del puzzle de cartas
    var peruLog = SetupPrefabPickup(PeruPrefabPath, catalog, messageView);

    AssetDatabase.SaveAssets();
    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);

    EditorUtility.DisplayDialog("Escudos",
      "Puzzle de escudos convertido a inventario.\n\n" +
      "Postes: " + slots.Length + "\n" + log + "\n" + hudLog + "\n" + peruLog +
      "\n\nIconos, nombres y orden: " + CatalogPath, "OK");
    Selection.activeObject = catalog;
  }

  // ---------- Escena / prefabs ----------

  private static void AssignPickup(ShieldPickup pickup, ShieldCatalog catalog, WorldMessageView view)
  {
    var so = new SerializedObject(pickup);
    so.FindProperty("_catalog").objectReferenceValue = catalog;
    if (so.FindProperty("_messageView").objectReferenceValue == null) so.FindProperty("_messageView").objectReferenceValue = view;
    so.ApplyModifiedPropertiesWithoutUndo();
  }

  // Fuera de la capa Grabbable, quieto y atravesable.
  private static void PrepareObject(GameObject go)
  {
    foreach (var t in go.GetComponentsInChildren<Transform>(true))
    {
      if (t.gameObject.layer == 8)
      {
        Undo.RecordObject(t.gameObject, "Escudos");
        t.gameObject.layer = 0;
      }
    }

    var body = go.GetComponent<Rigidbody>();

    if (body != null)
    {
      Undo.RecordObject(body, "Escudos");
      body.isKinematic = true;
    }

    foreach (var c in go.GetComponentsInChildren<Collider>(true))
    {
      Undo.RecordObject(c, "Escudos");
      if (c is MeshCollider mc && !mc.convex) mc.convex = true;
      c.isTrigger = true;
    }
  }

  private static string SetupPrefabPickup(string path, ShieldCatalog catalog, WorldMessageView view)
  {
    if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
    {
      return "No encontre " + path + " (escudo de Peru de la casa).";
    }

    var root = PrefabUtility.LoadPrefabContents(path);

    try
    {
      var pickup = root.GetComponent<ShieldPickup>();
      if (pickup == null) pickup = root.AddComponent<ShieldPickup>();
      AssignPickup(pickup, catalog, view);

      foreach (var t in root.GetComponentsInChildren<Transform>(true))
      {
        if (t.gameObject.layer == 8) t.gameObject.layer = 0;
      }

      var body = root.GetComponent<Rigidbody>();
      if (body != null) body.isKinematic = true;

      foreach (var c in root.GetComponentsInChildren<Collider>(true))
      {
        if (c is MeshCollider mc && !mc.convex) mc.convex = true;
        c.isTrigger = true;
      }

      PrefabUtility.SaveAsPrefabAsset(root, path);
    }
    finally
    {
      PrefabUtility.UnloadPrefabContents(root);
    }

    return "Escudo_Peru.prefab (recompensa de la casa): se recoge al inventario.";
  }

  private static string SetupHud(ShieldCatalog catalog)
  {
    var root = PrefabUtility.LoadPrefabContents(HudPath);

    try
    {
      var key = root.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == "Llave");
      var leaves = root.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == "Leaves");
      var anchorRef = key != null ? key : leaves;

      if (anchorRef == null)
      {
        return "HUD: no encontre la llave ni las hojas en PlayerHUD.";
      }

      var parent = anchorRef.parent;
      var container = parent.Find("Escudos") as RectTransform;

      if (container == null)
      {
        var go = new GameObject("Escudos", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        go.transform.SetParent(parent, false);
        go.transform.SetSiblingIndex(anchorRef.GetSiblingIndex() + 1);
        container = (RectTransform)go.transform;
        container.anchorMin = container.anchorMax = anchorRef.anchorMin;
        container.pivot = new Vector2(0f, 0.5f);
        container.sizeDelta = new Vector2(5 * 62f + 4 * 6f, 62f);
        var right = anchorRef.anchoredPosition.x + anchorRef.sizeDelta.x * (1f - anchorRef.pivot.x);
        container.anchoredPosition = new Vector2(right + 22f, anchorRef.anchoredPosition.y);

        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var slot = new GameObject("Casilla", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        slot.transform.SetParent(container, false);
        var image = slot.GetComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.sprite = catalog.EmptyIcon;
        var le = slot.GetComponent<LayoutElement>();
        le.preferredWidth = 62f;
        le.preferredHeight = 62f;
      }

      var template = container.Find("Casilla") != null ? container.Find("Casilla").GetComponent<Image>() : null;
      if (template != null && template.sprite == null) template.sprite = catalog.EmptyIcon;

      var hud = container.GetComponent<ShieldHUD>();
      if (hud == null) hud = container.gameObject.AddComponent<ShieldHUD>();
      var so = new SerializedObject(hud);
      so.FindProperty("_catalog").objectReferenceValue = catalog;
      so.FindProperty("_slotTemplate").objectReferenceValue = container.Find("Casilla") != null ? container.Find("Casilla").GetComponent<Image>() : container.GetComponentInChildren<Image>(true);
      so.FindProperty("_placeAfter").objectReferenceValue = key;
      so.ApplyModifiedPropertiesWithoutUndo();

      PrefabUtility.SaveAsPrefabAsset(root, HudPath);
    }
    finally
    {
      PrefabUtility.UnloadPrefabContents(root);
    }

    return "HUD: 5 casillas de escudos junto a la llave (PlayerHUD > Escudos).";
  }

  // ---------- Menu ----------

  private static ShieldSelectMenu BuildMenu(Font font)
  {
    var root = new GameObject("ShieldSelectUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
    var canvas = root.GetComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = 950;
    var scaler = root.GetComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920, 1080);
    scaler.matchWidthOrHeight = 0.5f;
    var menu = root.AddComponent<ShieldSelectMenu>();

    var shade = NewImage("Fondo", root.transform, new Color(0f, 0f, 0f, 0.45f));
    Stretch(shade.rectTransform);
    shade.raycastTarget = true;

    var panel = NewImage("Panel", root.transform, new Color(0.07f, 0.11f, 0.24f, 0.94f));
    Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1040f, 480f));
    var outline = panel.gameObject.AddComponent<Outline>();
    outline.effectColor = new Color(0.95f, 0.8f, 0.4f, 0.9f);
    outline.effectDistance = new Vector2(3f, -3f);

    var title = NewText("Titulo", panel.transform, font, "Poste de escudos", 46, FontStyle.Bold);
    Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -55f), new Vector2(960f, 70f));

    var subtitle = NewText("Subtitulo", panel.transform, font, "Elige el escudo que quieres colocar", 26, FontStyle.Normal);
    subtitle.color = new Color(0.85f, 0.9f, 1f);
    Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -108f), new Vector2(960f, 40f));

    var options = new GameObject("Opciones", typeof(RectTransform), typeof(HorizontalLayoutGroup));
    options.transform.SetParent(panel.transform, false);
    Place((RectTransform)options.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 5f), new Vector2(980f, 200f));
    var hl = options.GetComponent<HorizontalLayoutGroup>();
    hl.spacing = 18f;
    hl.childAlignment = TextAnchor.MiddleCenter;
    hl.childControlWidth = true;
    hl.childControlHeight = true;
    hl.childForceExpandWidth = false;
    hl.childForceExpandHeight = false;

    // Plantilla de opcion
    var option = NewButton("Opcion", options.transform, font, null, 0);
    var ole = option.gameObject.AddComponent<LayoutElement>();
    ole.preferredWidth = 165f;
    ole.preferredHeight = 195f;
    var icon = NewImage("Icono", option.transform, Color.white);
    icon.preserveAspect = true;
    Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(125f, 125f));
    var name = NewText("Nombre", option.transform, font, "Escudo", 24, FontStyle.Bold);
    Place(name.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(160f, 36f));

    var none = NewText("SinEscudos", panel.transform, font, "No llevas escudos", 30, FontStyle.Italic);
    none.color = new Color(0.8f, 0.85f, 0.95f, 0.8f);
    Place(none.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 5f), new Vector2(900f, 60f));
    none.gameObject.SetActive(false);

    var actions = new GameObject("Acciones", typeof(RectTransform), typeof(HorizontalLayoutGroup));
    actions.transform.SetParent(panel.transform, false);
    Place((RectTransform)actions.transform, new Vector2(0.5f, 0f), new Vector2(0f, 58f), new Vector2(960f, 76f));
    var al = actions.GetComponent<HorizontalLayoutGroup>();
    al.spacing = 30f;
    al.childAlignment = TextAnchor.MiddleCenter;
    al.childControlWidth = true;
    al.childControlHeight = true;
    al.childForceExpandWidth = false;
    al.childForceExpandHeight = false;

    var remove = NewButton("Retirar", actions.transform, font, "Retirar escudo", 28);
    var rle = remove.gameObject.AddComponent<LayoutElement>();
    rle.preferredWidth = 340f;
    rle.preferredHeight = 66f;
    var cancel = NewButton("Cancelar", actions.transform, font, "Cancelar", 28);
    var cle = cancel.gameObject.AddComponent<LayoutElement>();
    cle.preferredWidth = 260f;
    cle.preferredHeight = 66f;

    var so = new SerializedObject(menu);
    so.FindProperty("_title").objectReferenceValue = title;
    so.FindProperty("_subtitle").objectReferenceValue = subtitle;
    so.FindProperty("_optionsRoot").objectReferenceValue = options.transform;
    so.FindProperty("_optionTemplate").objectReferenceValue = option;
    so.FindProperty("_noShieldsLabel").objectReferenceValue = none.gameObject;
    so.FindProperty("_removeButton").objectReferenceValue = remove;
    so.FindProperty("_removeLabel").objectReferenceValue = remove.GetComponentInChildren<Text>(true);
    so.FindProperty("_cancelButton").objectReferenceValue = cancel;
    so.ApplyModifiedPropertiesWithoutUndo();

    var saved = PrefabUtility.SaveAsPrefabAsset(root, MenuPath);
    Object.DestroyImmediate(root);
    return saved.GetComponent<ShieldSelectMenu>();
  }

  private static Button NewButton(string name, Transform parent, Font font, string label, int size)
  {
    var bg = NewImage(name, parent, Color.white);
    bg.raycastTarget = true;
    var button = bg.gameObject.AddComponent<Button>();
    button.targetGraphic = bg;
    var colors = button.colors;
    colors.normalColor = new Color(1f, 1f, 1f, 0.12f);
    colors.highlightedColor = new Color(1f, 0.85f, 0.4f, 0.45f);
    colors.selectedColor = new Color(1f, 0.85f, 0.4f, 0.45f);
    colors.pressedColor = new Color(1f, 0.75f, 0.3f, 0.7f);
    colors.disabledColor = new Color(1f, 1f, 1f, 0.05f);
    colors.colorMultiplier = 1f;
    colors.fadeDuration = 0.08f;
    button.colors = colors;

    if (label != null)
    {
      var text = NewText("Texto", bg.transform, font, label, size, FontStyle.Bold);
      Stretch(text.rectTransform);
    }

    return button;
  }

  private static Image NewImage(string name, Transform parent, Color color)
  {
    var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    go.transform.SetParent(parent, false);
    var image = go.GetComponent<Image>();
    image.color = color;
    image.raycastTarget = false;
    return image;
  }

  private static Text NewText(string name, Transform parent, Font font, string value, int size, FontStyle style)
  {
    var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
    go.transform.SetParent(parent, false);
    var text = go.GetComponent<Text>();
    text.font = font;
    text.text = value;
    text.fontSize = size;
    text.fontStyle = style;
    text.alignment = TextAnchor.MiddleCenter;
    text.horizontalOverflow = HorizontalWrapMode.Overflow;
    text.verticalOverflow = VerticalWrapMode.Overflow;
    text.supportRichText = true;
    text.color = Color.white;
    text.raycastTarget = false;
    return text;
  }

  private static void Stretch(RectTransform rt)
  {
    rt.anchorMin = Vector2.zero;
    rt.anchorMax = Vector2.one;
    rt.offsetMin = rt.offsetMax = Vector2.zero;
  }

  private static void Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
  {
    rt.anchorMin = rt.anchorMax = anchor;
    rt.pivot = new Vector2(0.5f, 0.5f);
    rt.anchoredPosition = position;
    rt.sizeDelta = size;
  }

  private static Font FindFont()
  {
    var pause = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UI/PauseMenu/PauseMenuUI.prefab");
    var text = pause != null ? pause.GetComponentInChildren<Text>(true) : null;
    return text != null && text.font != null ? text.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
  }

  private static WorldMessageView FindMessageView()
  {
    foreach (var guid in AssetDatabase.FindAssets("DoorMessageUI t:Prefab"))
    {
      var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
      if (go != null && go.TryGetComponent<WorldMessageView>(out var view)) return view;
    }

    return null;
  }

  private static Sprite MakeSprite(string path)
  {
    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
    if (importer == null) return null;

    if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single || importer.mipmapEnabled)
    {
      importer.textureType = TextureImporterType.Sprite;
      importer.spriteImportMode = SpriteImportMode.Single;
      importer.mipmapEnabled = false;
      importer.alphaIsTransparency = true;
      importer.SaveAndReimport();
    }

    return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
  }

  // ---------- Iconos ----------

  /// <summary>Foto del escudo de frente, con fondo transparente.</summary>
  private static Texture2D RenderIcon(GameObject source, Quaternion upright)
  {
    const int size = 512;
    var scene = EditorSceneManager.NewPreviewScene();
    RenderTexture rt = null;

    try
    {
      var clone = Object.Instantiate(source);
      clone.SetActive(true);
      SceneManager.MoveGameObjectToScene(clone, scene);
      clone.transform.SetParent(null, false);
      clone.transform.SetPositionAndRotation(Vector3.zero, upright);
      clone.transform.localScale = source.transform.lossyScale;
      DisallowGpuDriven(clone);

      var renderers = clone.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
      if (renderers.Length == 0) return null;
      var bounds = renderers[0].bounds;
      foreach (var r in renderers) bounds.Encapsulate(r.bounds);

      // Cara del escudo: la malla "polySurface2" (o la mas grande).
      var normal = FaceNormal(clone, bounds);
      var up = Vector3.ProjectOnPlane(Vector3.up, normal);
      if (up.sqrMagnitude < 0.01f) up = Vector3.ProjectOnPlane(Vector3.forward, normal);
      up.Normalize();
      var right = Vector3.Cross(up, normal).normalized;
      var distance = bounds.extents.magnitude * 4f + 1f;

      var camGo = new GameObject("IconCamera", typeof(Camera));
      SceneManager.MoveGameObjectToScene(camGo, scene);
      var cam = camGo.GetComponent<Camera>();
      cam.scene = scene;
      cam.enabled = false;
      cam.orthographic = true;
      cam.orthographicSize = bounds.extents.magnitude * 0.85f;
      cam.nearClipPlane = 0.01f;
      cam.farClipPlane = distance * 2f;
      cam.clearFlags = CameraClearFlags.SolidColor;
      cam.transform.SetPositionAndRotation(bounds.center + normal * distance, Quaternion.LookRotation(-normal, up));

      AddLight(scene, Quaternion.LookRotation(-(normal + up * 0.6f + right * 0.4f)), 1.4f);
      AddLight(scene, Quaternion.LookRotation(-(normal - up * 0.3f - right * 0.6f)), 0.6f);

      rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
      rt.antiAliasing = 4;
      var black = Grab(cam, rt, Color.black);
      var white = Grab(cam, rt, Color.white);
      return Matte(black, white);
    }
    catch (Exception e)
    {
      Debug.LogWarning("[Escudos] Icono de " + source.name + ": " + e.Message);
      return null;
    }
    finally
    {
      if (rt != null) Object.DestroyImmediate(rt);
      EditorSceneManager.ClosePreviewScene(scene);
    }
  }

  private static void AddLight(Scene scene, Quaternion rotation, float intensity)
  {
    var go = new GameObject("Luz", typeof(Light));
    SceneManager.MoveGameObjectToScene(go, scene);
    go.transform.rotation = rotation;
    var light = go.GetComponent<Light>();
    light.type = LightType.Directional;
    light.intensity = intensity;
    light.color = Color.white;
  }

  private static void DisallowGpuDriven(GameObject go)
  {
    var type = AppDomain.CurrentDomain.GetAssemblies()
      .Select(a => a.GetType("UnityEngine.Rendering.DisallowGPUDrivenRendering"))
      .FirstOrDefault(t => t != null);

    if (type == null) return;
    var c = go.AddComponent(type);
    var prop = type.GetProperty("applyToChildrenRecursively");
    if (prop != null && prop.CanWrite) prop.SetValue(c, true);
    var field = type.GetField("applyToChildrenRecursively") ?? type.GetField("m_applyToChildrenRecursively", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    if (field != null) field.SetValue(c, true);
  }

  private static Vector3 FaceNormal(GameObject go, Bounds bounds)
  {
    var filters = go.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).ToList();
    var face = filters.FirstOrDefault(f => f.name.Contains("polySurface2"))
      ?? filters.OrderByDescending(f => f.sharedMesh.bounds.size.sqrMagnitude).FirstOrDefault();

    if (face != null)
    {
      try
      {
        var sum = Vector3.zero;
        foreach (var n in face.sharedMesh.normals) sum += n;
        var world = face.transform.TransformDirection(sum);
        if (world.sqrMagnitude > 1e-6f) return world.normalized;
      }
      catch (Exception)
      {
        // Malla no legible: se usa la caja.
      }
    }

    // El eje mas delgado de la caja.
    var e = bounds.extents;
    if (e.x <= e.y && e.x <= e.z) return Vector3.right;
    if (e.y <= e.x && e.y <= e.z) return Vector3.up;
    return Vector3.back;
  }

  private static Texture2D Grab(Camera cam, RenderTexture rt, Color background)
  {
    cam.backgroundColor = background;
    cam.targetTexture = rt;

    var request = new RenderPipeline.StandardRequest { destination = rt };

    if (RenderPipeline.SupportsRenderRequest(cam, request))
    {
      RenderPipeline.SubmitRenderRequest(cam, request);
    }
    else
    {
      cam.Render();
    }

    var previous = RenderTexture.active;
    RenderTexture.active = rt;
    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
    tex.Apply();
    RenderTexture.active = previous;
    cam.targetTexture = null;
    return tex;
  }

  // Fondo negro y fondo blanco -> transparencia real, recortado y cuadrado.
  private static Texture2D Matte(Texture2D black, Texture2D white)
  {
    var w = black.width;
    var h = black.height;
    var b = black.GetPixels();
    var wh = white.GetPixels();
    var result = new Color[b.Length];
    int minX = w, minY = h, maxX = -1, maxY = -1;

    for (var i = 0; i < b.Length; i++)
    {
      var diff = ((wh[i].r - b[i].r) + (wh[i].g - b[i].g) + (wh[i].b - b[i].b)) / 3f;
      var a = Mathf.Clamp01(1f - diff);

      if (a < 0.02f)
      {
        result[i] = Color.clear;
        continue;
      }

      result[i] = new Color(Mathf.Clamp01(b[i].r / a), Mathf.Clamp01(b[i].g / a), Mathf.Clamp01(b[i].b / a), a);
      var x = i % w;
      var y = i / w;
      if (x < minX) minX = x;
      if (x > maxX) maxX = x;
      if (y < minY) minY = y;
      if (y > maxY) maxY = y;
    }

    if (maxX < 0) return null; // no se vio nada

    var cw = maxX - minX + 1;
    var ch = maxY - minY + 1;
    var side = Mathf.CeilToInt(Mathf.Max(cw, ch) * 1.08f);
    var output = new Texture2D(side, side, TextureFormat.RGBA32, false);
    var clear = new Color[side * side];
    output.SetPixels(clear);
    var ox = (side - cw) / 2;
    var oy = (side - ch) / 2;

    for (var y = 0; y < ch; y++)
    {
      for (var x = 0; x < cw; x++)
      {
        output.SetPixel(ox + x, oy + y, result[(minY + y) * w + (minX + x)]);
      }
    }

    output.Apply();
    return output;
  }

  private static Texture2D MakeSilhouette(Texture2D icon)
  {
    var pixels = icon.GetPixels();
    var tint = new Color(0.1f, 0.14f, 0.26f);

    for (var i = 0; i < pixels.Length; i++)
    {
      pixels[i] = new Color(tint.r, tint.g, tint.b, pixels[i].a * 0.6f);
    }

    var output = new Texture2D(icon.width, icon.height, TextureFormat.RGBA32, false);
    output.SetPixels(pixels);
    output.Apply();
    return output;
  }
}
