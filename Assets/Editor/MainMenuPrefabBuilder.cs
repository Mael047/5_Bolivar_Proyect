// MainMenuPrefabBuilder.cs
// Crea el prefab por defecto del menu principal (Assets/UI/MainMenu/MainMenuUI)
// si no existe ninguno en el proyecto, y añade la opcion
// Tools > Menu principal > Agregar menu a la escena abierta, que lo coloca en
// la escena y conecta MenuCameraPoint y la camara automaticamente.
// Despues el prefab se diseña libremente (doble clic).

using System.IO;
using Menus;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class MainMenuPrefabBuilder
{
  private const string Folder = "Assets/UI/MainMenu";
  private const string PrefabName = "MainMenuUI";
  private const string CameraPointName = "MenuCameraPoint";

  private static readonly Color Accent = new Color(0.91f, 0.78f, 0.42f);

  static MainMenuPrefabBuilder()
  {
    EditorApplication.delayCall += () =>
    {
      if (!EditorApplication.isPlayingOrWillChangePlaymode && FindPrefab() == null)
      {
        Save(Build());
        Debug.Log($"[Menu principal] Prefab creado en {Folder}/{PrefabName}. Usa Tools > Menu principal > Agregar menu a la escena abierta.");
      }
    };
  }

  [MenuItem("Tools/Menu principal/Agregar menu a la escena abierta")]
  private static void AddToScene()
  {
    if (Object.FindAnyObjectByType<MainMenu>(FindObjectsInactive.Include) != null)
    {
      EditorUtility.DisplayDialog("Menu principal", "La escena ya tiene un MainMenu.", "OK");
      return;
    }

    var prefab = FindPrefab();

    if (prefab == null)
    {
      prefab = Save(Build()).GetComponent<MainMenu>();
    }

    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject);
    Undo.RegisterCreatedObjectUndo(instance, "Agregar menu principal");

    // Conexiones con objetos de la escena (no pueden guardarse en el prefab).
    var menu = instance.GetComponent<MainMenu>();
    var so = new SerializedObject(menu);
    var point = GameObject.Find(CameraPointName);

    if (point != null)
    {
      so.FindProperty("_menuCameraPoint").objectReferenceValue = point.transform;
    }

    if (Camera.main != null)
    {
      so.FindProperty("_camera").objectReferenceValue = Camera.main;
    }

    so.ApplyModifiedProperties();
    EditorSceneManager.MarkSceneDirty(instance.scene);
    Selection.activeObject = instance;

    if (point == null)
    {
      EditorUtility.DisplayDialog("Menu principal",
        $"No encontre un objeto llamado '{CameraPointName}'. Asigna el punto de camara en el campo 'Menu Camera Point' del MainMenu.", "OK");
    }
  }

  [MenuItem("Tools/Menu principal/Seleccionar prefab del menu")]
  private static void SelectPrefab()
  {
    var prefab = FindPrefab();

    if (prefab != null)
    {
      Selection.activeObject = prefab.gameObject;
      EditorGUIUtility.PingObject(prefab.gameObject);
    }
  }

  private static MainMenu FindPrefab()
  {
    MainMenu fallback = null;

    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
    {
      var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
      var menu = go != null ? go.GetComponent<MainMenu>() : null;

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
    canvas.sortingOrder = 800;

    var scaler = root.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920f, 1080f);
    scaler.matchWidthOrHeight = 0.5f;

    root.AddComponent<GraphicRaycaster>();
    root.AddComponent<CanvasGroup>();
    var menu = root.AddComponent<MainMenu>();
    var rootRect = (RectTransform)root.transform;

    // Degradado lateral para que los botones se lean sobre el nivel.
    var shade = CreateGraphic<Image>("Shade", rootRect, Vector2.zero, new Vector2(0.45f, 1f));
    shade.color = new Color(0f, 0f, 0f, 0.45f);

    // Titulo
    var title = CreateText("Title", rootRect, "Re:Quinta", 110, Color.white, FontStyle.Bold, TextAnchor.LowerLeft);
    SetRect(title.rectTransform, new Vector2(0.06f, 0.62f), new Vector2(0.6f, 0.86f));
    var shadow = title.gameObject.AddComponent<Shadow>();
    shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
    shadow.effectDistance = new Vector2(3f, -3f);

    var subtitle = CreateText("Subtitle", rootRect, "La Quinta de Bolívar", 32, Accent, FontStyle.Italic, TextAnchor.UpperLeft);
    SetRect(subtitle.rectTransform, new Vector2(0.06f, 0.56f), new Vector2(0.6f, 0.62f));

    // Botones
    var list = CreateRect("Buttons", rootRect, new Vector2(0.06f, 0.12f), new Vector2(0.34f, 0.52f));
    var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
    layout.childAlignment = TextAnchor.UpperLeft;
    layout.spacing = 10f;
    layout.childControlWidth = true;
    layout.childControlHeight = true;
    layout.childForceExpandWidth = true;
    layout.childForceExpandHeight = false;

    var so = new SerializedObject(menu);
    so.FindProperty("_newGameButton").objectReferenceValue = CreateButton(list, "NuevaPartida", "Nueva partida");
    so.FindProperty("_loadGameButton").objectReferenceValue = CreateButton(list, "CargarPartida", "Cargar partida");
    so.FindProperty("_settingsButton").objectReferenceValue = CreateButton(list, "Ajustes", "Ajustes");
    so.FindProperty("_creditsButton").objectReferenceValue = CreateButton(list, "Creditos", "Créditos");
    so.FindProperty("_quitButton").objectReferenceValue = CreateButton(list, "SalirDelJuego", "Salir del juego");

    // Fundido negro (encima de todo, transparente hasta que se usa)
    var fade = CreateGraphic<Image>("Fade", rootRect, Vector2.zero, Vector2.one);
    fade.color = Color.black;
    var fadeGroup = fade.gameObject.AddComponent<CanvasGroup>();
    fadeGroup.alpha = 0f;
    fadeGroup.ignoreParentGroups = true;
    fadeGroup.blocksRaycasts = false;
    fadeGroup.interactable = false;
    so.FindProperty("_fadeImage").objectReferenceValue = fade;

    so.ApplyModifiedPropertiesWithoutUndo();
    return root;
  }

  private static Button CreateButton(RectTransform parent, string name, string label)
  {
    var image = CreateGraphic<Image>(name, parent, Vector2.zero, Vector2.one);
    image.color = Color.white;
    image.raycastTarget = true;

    var element = image.gameObject.AddComponent<LayoutElement>();
    element.preferredHeight = 64f;
    element.minHeight = 64f;

    var button = image.gameObject.AddComponent<Button>();
    var colors = button.colors;
    colors.normalColor = new Color(0f, 0f, 0f, 0.35f);
    colors.highlightedColor = new Color(Accent.r, Accent.g, Accent.b, 0.55f);
    colors.selectedColor = new Color(Accent.r, Accent.g, Accent.b, 0.55f);
    colors.pressedColor = new Color(Accent.r, Accent.g, Accent.b, 0.85f);
    colors.disabledColor = new Color(0f, 0f, 0f, 0.2f);
    colors.fadeDuration = 0.1f;
    button.colors = colors;

    var text = CreateText("Label", image.rectTransform, label, 34, Color.white, FontStyle.Normal, TextAnchor.MiddleLeft);
    text.rectTransform.offsetMin = new Vector2(28f, 0f);
    text.rectTransform.offsetMax = new Vector2(-16f, 0f);

    return button;
  }

  private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
  {
    var go = new GameObject(name, typeof(RectTransform));
    go.transform.SetParent(parent, false);
    var rect = (RectTransform)go.transform;
    SetRect(rect, anchorMin, anchorMax);
    return rect;
  }

  private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
  {
    rect.anchorMin = anchorMin;
    rect.anchorMax = anchorMax;
    rect.offsetMin = Vector2.zero;
    rect.offsetMax = Vector2.zero;
  }

  private static T CreateGraphic<T>(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax) where T : Graphic
  {
    var graphic = CreateRect(name, parent, anchorMin, anchorMax).gameObject.AddComponent<T>();
    graphic.raycastTarget = false;
    return graphic;
  }

  private static Text CreateText(string name, Transform parent, string value, int size, Color color, FontStyle style, TextAnchor alignment)
  {
    var text = CreateGraphic<Text>(name, parent, Vector2.zero, Vector2.one);
    text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    text.text = value;
    text.fontSize = size;
    text.fontStyle = style;
    text.color = color;
    text.alignment = alignment;
    text.horizontalOverflow = HorizontalWrapMode.Wrap;
    text.verticalOverflow = VerticalWrapMode.Overflow;
    return text;
  }
}
