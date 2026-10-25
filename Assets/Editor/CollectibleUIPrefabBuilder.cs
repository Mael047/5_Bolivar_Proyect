// CollectibleUIPrefabBuilder.cs
// Prefabs de los coleccionables (por defecto en Assets/UI/Collectibles):
//   - CollectibleInspectorUI: vista de inspeccion (fondo difuminado, modelo,
//     columna de texto, ayuda inferior).
//   - CollectiblePromptUI: aviso "F / X  Examinar".
//   - CollectibleSystem: CollectibleInspector + CollectibleInteractor ya
//     apuntando a esas dos interfaces. Es lo que va en cada escena con
//     coleccionables (Tools > Coleccionables > Agregar sistema a la escena).
// Los prefabs se buscan por tipo en todo el proyecto: se pueden mover o
// renombrar libremente. Solo se crean los que no existan en ningun sitio.

using System.IO;
using Collectibles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class CollectibleUIPrefabBuilder
{
  private const string Folder = "Assets/UI/Collectibles";
  private const string InspectorName = "CollectibleInspectorUI";
  private const string PromptName = "CollectiblePromptUI";
  private const string SystemName = "CollectibleSystem";

  private static readonly Color Accent = new Color(0.91f, 0.78f, 0.42f);

  static CollectibleUIPrefabBuilder()
  {
    EditorApplication.delayCall += () =>
    {
      if (!EditorApplication.isPlayingOrWillChangePlaymode)
      {
        CreateMissing();
      }
    };
  }

  /// <summary>Crea solo los prefabs que no existan en ninguna parte del proyecto.</summary>
  private static void CreateMissing()
  {
    var inspectorView = FindPrefab<CollectibleInspectorView>(InspectorName);
    var promptView = FindPrefab<CollectiblePromptView>(PromptName);
    var created = false;

    if (inspectorView == null)
    {
      inspectorView = Save(BuildInspector(), InspectorName).GetComponent<CollectibleInspectorView>();
      created = true;
    }

    if (promptView == null)
    {
      promptView = Save(BuildPrompt(), PromptName).GetComponent<CollectiblePromptView>();
      created = true;
    }

    if (FindPrefab<CollectibleInspector>(SystemName) == null)
    {
      Save(BuildSystem(inspectorView, promptView), SystemName);
      created = true;
    }

    if (created)
    {
      Debug.Log($"[Coleccionables] Prefabs creados en {Folder}. Puedes moverlos donde quieras.");
    }
  }

  [MenuItem("Tools/Coleccionables/Agregar sistema a la escena abierta")]
  private static void AddSystemToScene()
  {
    CreateMissing();

    if (Object.FindAnyObjectByType<CollectibleInspector>() != null)
    {
      EditorUtility.DisplayDialog("Coleccionables", "La escena ya tiene un CollectibleInspector.", "OK");
      return;
    }

    var prefab = FindPrefab<CollectibleInspector>(SystemName);
    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject);
    Undo.RegisterCreatedObjectUndo(instance, "Agregar sistema de coleccionables");
    EditorSceneManager.MarkSceneDirty(instance.scene);
    Selection.activeObject = instance;
  }

  [MenuItem("Tools/Coleccionables/Seleccionar prefab de inspeccion")]
  private static void SelectInspector()
  {
    CreateMissing();
    Ping(FindPrefab<CollectibleInspectorView>(InspectorName));
  }

  [MenuItem("Tools/Coleccionables/Seleccionar prefab del aviso")]
  private static void SelectPrompt()
  {
    CreateMissing();
    Ping(FindPrefab<CollectiblePromptView>(PromptName));
  }

  private static void Ping(Component component)
  {
    if (component == null)
    {
      return;
    }

    Selection.activeObject = component.gameObject;
    EditorGUIUtility.PingObject(component.gameObject);
  }

  /// <summary>Busca un prefab con el componente T; prefiere el que se llama como el de por defecto.</summary>
  private static T FindPrefab<T>(string preferredName) where T : Component
  {
    T fallback = null;

    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
    {
      var path = AssetDatabase.GUIDToAssetPath(guid);
      var component = AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponent<T>();

      if (component == null)
      {
        continue;
      }

      if (component.gameObject.name == preferredName)
      {
        return component;
      }

      if (fallback == null)
      {
        fallback = component;
      }
    }

    return fallback;
  }

  private static GameObject Save(GameObject root, string name)
  {
    EnsureFolder(Folder);
    var path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{name}.prefab");
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

  // ---------- Sistema ----------

  private static GameObject BuildSystem(CollectibleInspectorView inspectorView, CollectiblePromptView promptView)
  {
    var root = new GameObject(SystemName);
    var inspector = root.AddComponent<CollectibleInspector>();
    var interactor = root.AddComponent<CollectibleInteractor>();

    var so = new SerializedObject(inspector);
    so.FindProperty("_view").objectReferenceValue = inspectorView;
    so.ApplyModifiedPropertiesWithoutUndo();

    so = new SerializedObject(interactor);
    so.FindProperty("_promptView").objectReferenceValue = promptView;
    so.ApplyModifiedPropertiesWithoutUndo();

    return root;
  }

  // ---------- Vista de inspeccion ----------

  private static GameObject BuildInspector()
  {
    var root = CreateCanvas("CollectibleInspectorUI", 900);
    var view = root.AddComponent<CollectibleInspectorView>();
    var rootRect = (RectTransform)root.transform;

    // Fondo difuminado: el componente va apagado en el prefab (sin captura se
    // veria blanco) y se enciende en juego. Su color tiñe la captura.
    var blur = Create<RawImage>("BlurredBackground", rootRect, Vector2.zero, Vector2.one);
    blur.enabled = false;
    view.blurredBackground = blur;

    var tint = Create<Image>("Tint", rootRect, Vector2.zero, Vector2.one);
    tint.color = new Color(0.02f, 0.02f, 0.04f, 0.55f);

    // Zona del modelo: el color tenue solo sirve para verla en el editor.
    var model = Create<RawImage>("ModelImage", rootRect, Vector2.zero, new Vector2(0.55f, 1f));
    model.color = new Color(1f, 1f, 1f, 0.06f);
    view.modelImage = model;

    // Columna de texto
    var column = CreateRect("TextColumn", rootRect, new Vector2(0.55f, 0.1f), new Vector2(0.93f, 0.9f));
    var columnGroup = column.gameObject.AddComponent<CanvasGroup>();
    columnGroup.blocksRaycasts = false;
    view.delayedGroup = columnGroup;

    var content = CreateRect("Content", column, Vector2.zero, Vector2.one);
    var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
    layout.childAlignment = TextAnchor.MiddleLeft;
    layout.spacing = 16f;
    layout.childControlWidth = true;
    layout.childControlHeight = true;
    layout.childForceExpandWidth = false;
    layout.childForceExpandHeight = false;
    view.slideIn = content;
    view.slideFrom = new Vector2(60f, 0f);

    var title = CreateText("Title", content, "Titulo del objeto", 56, Color.white, FontStyle.Bold);
    var shadow = title.gameObject.AddComponent<Shadow>();
    shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
    shadow.effectDistance = new Vector2(2f, -2f);
    view.title = title;

    var divider = Create<Image>("Divider", content, Vector2.zero, Vector2.zero);
    divider.color = Accent;
    var dividerLayout = divider.gameObject.AddComponent<LayoutElement>();
    dividerLayout.preferredWidth = 160f;
    dividerLayout.minHeight = 3f;
    dividerLayout.preferredHeight = 3f;

    var subtitle = CreateText("Subtitle", content, "Subtitulo", 26, Accent, FontStyle.Italic);
    view.subtitle = subtitle;
    view.hideWhenNoSubtitle = new[] { subtitle.gameObject };

    var body = CreateText("Description", content,
      "Descripcion del objeto. Este texto se reemplaza por el de cada coleccionable.", 30,
      new Color(0.88f, 0.88f, 0.88f), FontStyle.Normal);
    body.lineSpacing = 1.15f;
    view.description = body;
    view.hideWhenNoDescription = new[] { body.gameObject };

    // Ayuda inferior (texto fijo, editalo en el prefab)
    var hint = CreateText("Hint", rootRect,
      "Cierra la mano y muévela para girar   ·   Stick / ratón: rotar   ·   Gatillos / rueda: zoom   ·   F / X: salir",
      20, new Color(1f, 1f, 1f, 0.65f), FontStyle.Normal);
    var hintRect = hint.rectTransform;
    hintRect.anchorMin = new Vector2(0f, 0f);
    hintRect.anchorMax = new Vector2(1f, 0f);
    hintRect.pivot = new Vector2(0.5f, 0f);
    hintRect.sizeDelta = new Vector2(-80f, 40f);
    hintRect.anchoredPosition = new Vector2(0f, 28f);
    hint.alignment = TextAnchor.MiddleCenter;

    return root;
  }

  // ---------- Aviso ----------

  private static GameObject BuildPrompt()
  {
    var root = CreateCanvas("CollectiblePromptUI", 400);
    var view = root.AddComponent<CollectiblePromptView>();
    var rootRect = (RectTransform)root.transform;

    var panel = Create<Image>("Prompt", rootRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
    panel.color = new Color(0f, 0f, 0f, 0.6f);
    var panelRect = panel.rectTransform;
    panelRect.pivot = new Vector2(0.5f, 0f);

    var layout = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
    layout.padding = new RectOffset(20, 20, 8, 8);
    layout.spacing = 14f;
    layout.childAlignment = TextAnchor.MiddleCenter;
    layout.childControlWidth = true;
    layout.childControlHeight = true;
    layout.childForceExpandWidth = false;
    layout.childForceExpandHeight = false;

    var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
    fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
    fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

    var button = CreateText("Button", panelRect, "F / X", 26, Accent, FontStyle.Bold);
    button.horizontalOverflow = HorizontalWrapMode.Overflow;

    var action = CreateText("Action", panelRect, "Examinar", 26, Color.white, FontStyle.Normal);
    action.horizontalOverflow = HorizontalWrapMode.Overflow;

    view.followRoot = panelRect;
    view.followTarget = true;
    view.actionLabel = action;

    return root;
  }

  // ---------- Utilidades ----------

  private static GameObject CreateCanvas(string name, int sortingOrder)
  {
    var go = new GameObject(name, typeof(RectTransform));

    var canvas = go.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = sortingOrder;

    var scaler = go.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920f, 1080f);
    scaler.matchWidthOrHeight = 0.5f;

    var group = go.AddComponent<CanvasGroup>();
    group.blocksRaycasts = false;
    group.interactable = false;

    return go;
  }

  private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
  {
    var go = new GameObject(name, typeof(RectTransform));
    go.transform.SetParent(parent, false);
    var rect = (RectTransform)go.transform;
    rect.anchorMin = anchorMin;
    rect.anchorMax = anchorMax;
    rect.offsetMin = Vector2.zero;
    rect.offsetMax = Vector2.zero;
    return rect;
  }

  private static T Create<T>(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax) where T : Graphic
  {
    var graphic = CreateRect(name, parent, anchorMin, anchorMax).gameObject.AddComponent<T>();
    graphic.raycastTarget = false;
    return graphic;
  }

  private static Text CreateText(string name, Transform parent, string value, int size, Color color, FontStyle style)
  {
    var text = Create<Text>(name, parent, Vector2.zero, Vector2.one);
    text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    text.text = value;
    text.fontSize = size;
    text.fontStyle = style;
    text.color = color;
    text.supportRichText = true;
    text.alignment = TextAnchor.UpperLeft;
    text.horizontalOverflow = HorizontalWrapMode.Wrap;
    text.verticalOverflow = VerticalWrapMode.Overflow;
    return text;
  }
}
