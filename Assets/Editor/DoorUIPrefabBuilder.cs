// DoorUIPrefabBuilder.cs
// Crea el prefab por defecto del mensaje de las puertas
// (Assets/UI/Puertas/DoorMessageUI): un panel con el mismo estilo que el aviso
// de los coleccionables, con borde dorado y el texto del mensaje.
// Las puertas (LockedDoor) lo usan solas. Diseñalo libremente (doble clic).
//
// Menu: Tools > Puertas y llaves > Seleccionar interfaz del mensaje
//       Tools > Puertas y llaves > Asignar interfaz a las puertas de la escena

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class DoorUIPrefabBuilder
{
  private const string Folder = "Assets/UI/Puertas";
  private const string PrefabName = "DoorMessageUI";
  private static readonly Color Accent = new Color(0.91f, 0.78f, 0.42f);

  static DoorUIPrefabBuilder()
  {
    EditorApplication.delayCall += () =>
    {
      if (!EditorApplication.isPlayingOrWillChangePlaymode && FindPrefab() == null)
      {
        Save(Build());
        Debug.Log($"[Puertas] Interfaz del mensaje creada en {Folder}/{PrefabName}.");
      }
    };
  }

  [MenuItem("Tools/Puertas y llaves/Seleccionar interfaz del mensaje")]
  private static void Select()
  {
    var prefab = FindPrefab() ?? Save(Build()).GetComponent<WorldMessageView>();
    Selection.activeObject = prefab.gameObject;
    EditorGUIUtility.PingObject(prefab.gameObject);
  }

  [MenuItem("Tools/Puertas y llaves/Asignar interfaz a las puertas de la escena")]
  private static void AssignToDoors()
  {
    var prefab = FindPrefab() ?? Save(Build()).GetComponent<WorldMessageView>();
    var count = 0;

    foreach (var door in Object.FindObjectsByType<LockedDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
    {
      var so = new SerializedObject(door);
      var prop = so.FindProperty("_messageView");

      if (prop.objectReferenceValue == null)
      {
        prop.objectReferenceValue = prefab;
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(door.gameObject.scene);
        count++;
      }
    }

    EditorUtility.DisplayDialog("Puertas", count == 0 ? "Todas las puertas ya tenian interfaz." : $"Interfaz asignada a {count} puerta(s).", "OK");
  }

  private static WorldMessageView FindPrefab()
  {
    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
    {
      var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));

      if (go != null && go.name == PrefabName && go.TryGetComponent<WorldMessageView>(out var view))
      {
        return view;
      }
    }

    return null;
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

  private static GameObject Build()
  {
    var root = new GameObject(PrefabName, typeof(RectTransform));

    var canvas = root.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = 410; // justo encima del aviso de interaccion

    var scaler = root.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920f, 1080f);
    scaler.matchWidthOrHeight = 0.5f;

    var group = root.AddComponent<CanvasGroup>();
    group.blocksRaycasts = false;
    group.interactable = false;
    group.alpha = 0f;

    var view = root.AddComponent<WorldMessageView>();

    // Panel: mismo estilo que el aviso "F / X  Examinar", con borde dorado.
    var panelGo = new GameObject("Mensaje", typeof(RectTransform));
    panelGo.transform.SetParent(root.transform, false);
    var panel = (RectTransform)panelGo.transform;
    panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
    panel.pivot = new Vector2(0.5f, 0f);

    var background = panelGo.AddComponent<Image>();
    background.color = new Color(0f, 0f, 0f, 0.72f);
    background.raycastTarget = false;

    var outline = panelGo.AddComponent<Outline>();
    outline.effectColor = new Color(Accent.r, Accent.g, Accent.b, 0.8f);
    outline.effectDistance = new Vector2(2f, -2f);

    var layout = panelGo.AddComponent<VerticalLayoutGroup>();
    layout.padding = new RectOffset(28, 28, 14, 14);
    layout.childAlignment = TextAnchor.MiddleCenter;
    layout.childControlWidth = true;
    layout.childControlHeight = true;
    layout.childForceExpandWidth = false;
    layout.childForceExpandHeight = false;

    var fitter = panelGo.AddComponent<ContentSizeFitter>();
    fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
    fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

    var textGo = new GameObject("Texto", typeof(RectTransform));
    textGo.transform.SetParent(panel, false);
    var text = textGo.AddComponent<Text>();
    text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    text.text = "<b>Cerrada con llave</b>\n<size=22>Necesitas una llave para abrirla.</size>";
    text.supportRichText = true;
    text.fontSize = 28;
    text.lineSpacing = 1.1f;
    text.color = Color.white;
    text.alignment = TextAnchor.MiddleCenter;
    text.horizontalOverflow = HorizontalWrapMode.Overflow;
    text.verticalOverflow = VerticalWrapMode.Overflow;
    text.raycastTarget = false;

    var shadow = textGo.AddComponent<Shadow>();
    shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
    shadow.effectDistance = new Vector2(1.5f, -1.5f);

    view.followRoot = panel;
    view.followTarget = true;
    view.screenOffset = new Vector2(0f, 60f);
    view.messageLabel = text;

    root.SetActive(false);
    return root;
  }
}
