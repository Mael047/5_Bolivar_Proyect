// CameraSetupPrefabBuilder.cs
// Crea el prefab por defecto de la configuracion de camara
// (Assets/UI/CameraSetup/CameraSetupUI) con forma de libro abierto:
//   Paso 1: pagina izquierda = lista de webcams; pagina derecha = titulo,
//           vista previa, "Mueve la camara para ajustar la imagen", Continuar.
//   Paso 2: vista previa grande con un objeto de prueba (una hoja) y a la
//           derecha "Prueba de gesto": 1. Abre, 2. Cierra, 3. Mueve.
// Todo es provisional: reemplaza fondos, marcos y botones por tus sprites.
// Menu: Tools > Configuracion de camara > Agregar a la escena abierta.

using System.IO;
using Menus;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class CameraSetupPrefabBuilder
{
  private const string Folder = "Assets/UI/CameraSetup";
  private const string PrefabName = "CameraSetupUI";

  private static readonly Color Paper = new Color(0.97f, 0.93f, 0.84f, 1f);
  private static readonly Color Cover = new Color(0.22f, 0.36f, 0.55f, 1f);
  private static readonly Color Ink = new Color(0.14f, 0.2f, 0.32f, 1f);
  private static readonly Color Line = new Color(0.25f, 0.3f, 0.4f, 1f);
  private static readonly Color Green = new Color(0.35f, 0.65f, 0.3f, 1f);

  static CameraSetupPrefabBuilder()
  {
    EditorApplication.delayCall += () =>
    {
      if (!EditorApplication.isPlayingOrWillChangePlaymode && FindPrefab() == null)
      {
        Save(Build());
        Debug.Log($"[Camara] Prefab creado en {Folder}/{PrefabName}. Usa Tools > Configuracion de camara > Agregar a la escena abierta.");
      }
    };
  }

  [MenuItem("Tools/Configuracion de camara/Agregar a la escena abierta")]
  private static void AddToScene()
  {
    if (Object.FindAnyObjectByType<CameraSetup>(FindObjectsInactive.Include) != null)
    {
      EditorUtility.DisplayDialog("Configuracion de camara", "La escena ya tiene un CameraSetup.", "OK");
      return;
    }

    var prefab = FindPrefab();

    if (prefab == null)
    {
      prefab = Save(Build()).GetComponent<CameraSetup>();
    }

    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject);
    Undo.RegisterCreatedObjectUndo(instance, "Agregar configuracion de camara");
    EditorSceneManager.MarkSceneDirty(instance.scene);
    Selection.activeObject = instance;
  }

  [MenuItem("Tools/Configuracion de camara/Seleccionar prefab")]
  private static void SelectPrefab()
  {
    var prefab = FindPrefab();

    if (prefab != null)
    {
      Selection.activeObject = prefab.gameObject;
      EditorGUIUtility.PingObject(prefab.gameObject);
    }
  }

  private static CameraSetup FindPrefab()
  {
    CameraSetup fallback = null;

    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
    {
      var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
      var setup = go != null ? go.GetComponent<CameraSetup>() : null;

      if (setup == null) continue;
      if (go.name == PrefabName) return setup;
      fallback ??= setup;
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
    if (AssetDatabase.IsValidFolder(folder)) return;
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
    canvas.sortingOrder = 950;

    var scaler = root.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920f, 1080f);
    scaler.matchWidthOrHeight = 0.5f;

    root.AddComponent<GraphicRaycaster>();
    root.AddComponent<CanvasGroup>();
    var setup = root.AddComponent<CameraSetup>();
    var rootRect = (RectTransform)root.transform;

    // Fondo transparente: el nivel sigue viendose detras (sube el alfa si
    // quieres oscurecerlo o pon aqui tu propia imagen).
    var background = Image("Background", rootRect, new Color(0.8f, 0.86f, 0.88f, 0f));
    Stretch(background.rectTransform);

    var so = new SerializedObject(setup);
    BuildCameraStep(rootRect, so);
    BuildGestureStep(rootRect, so);
    so.ApplyModifiedPropertiesWithoutUndo();

    return root;
  }

  // Paso 1: libro abierto
  private static void BuildCameraStep(RectTransform parent, SerializedObject so)
  {
    var step = Rect("Step_Camara", parent);
    Stretch(step);

    var book = Image("Book", step, Cover);
    book.rectTransform.sizeDelta = new Vector2(1560f, 900f);

    var left = Page("LeftPage", book.rectTransform, new Vector2(0.02f, 0.03f), new Vector2(0.495f, 0.97f));
    var right = Page("RightPage", book.rectTransform, new Vector2(0.505f, 0.03f), new Vector2(0.98f, 0.97f));

    // Izquierda: lista de camaras
    var listTitle = Label("Title", left, "Cámaras", 46, Ink, FontStyle.Bold);
    Place(listTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(500f, 70f));

    var list = Rect("CameraList", left);
    list.anchorMin = new Vector2(0.12f, 0.1f);
    list.anchorMax = new Vector2(0.88f, 0.83f);
    list.offsetMin = list.offsetMax = Vector2.zero;
    var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
    layout.spacing = 26f;
    layout.childAlignment = TextAnchor.UpperCenter;
    layout.childControlWidth = true;
    layout.childControlHeight = true;
    layout.childForceExpandWidth = true;
    layout.childForceExpandHeight = false;

    var template = Button(list, "CameraButton", "Nombre de la cámara", 30, 120f);
    var selected = Image("Selected", template.transform, new Color(0.35f, 0.55f, 0.85f, 1f));
    var selRect = selected.rectTransform;
    selRect.anchorMin = new Vector2(0f, 0f);
    selRect.anchorMax = new Vector2(0f, 1f);
    selRect.pivot = new Vector2(0f, 0.5f);
    selRect.sizeDelta = new Vector2(12f, 0f);
    selRect.anchoredPosition = Vector2.zero;
    selected.gameObject.SetActive(false);

    var noCamera = Label("NoCamera", left, "No se encontró ninguna cámara.\nConecta una webcam y vuelve a abrir esta pantalla.", 28, Ink, FontStyle.Italic);
    Place(noCamera.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 160f));
    noCamera.gameObject.SetActive(false);

    // Derecha: titulo, vista previa, ayuda, estado, continuar
    var banner = Image("TitleBanner", right, new Color(0.99f, 0.96f, 0.88f, 1f));
    Outline(banner, Line);
    Place(banner.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(460f, 120f));
    var title = Label("Title", banner.rectTransform, "Configura tu\ncámara", 44, Ink, FontStyle.Normal);
    Stretch(title.rectTransform);

    var frame = Image("PreviewFrame", right, new Color(0.99f, 0.96f, 0.88f, 1f));
    Outline(frame, Line);
    Place(frame.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(560f, 340f));
    var preview = Raw("Preview", frame.rectTransform);
    Stretch(preview.rectTransform);
    preview.rectTransform.offsetMin = new Vector2(16f, 16f);
    preview.rectTransform.offsetMax = new Vector2(-16f, -16f);

    var hint = Image("HintBanner", right, new Color(0.99f, 0.96f, 0.88f, 1f));
    Outline(hint, Line);
    Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -545f), new Vector2(420f, 80f));
    var hintText = Label("Text", hint.rectTransform, "Mueve la cámara para\najustar la imagen", 24, Ink, FontStyle.Normal);
    Stretch(hintText.rectTransform);

    var status = Label("Status", right, "Iniciando cámara...", 24, new Color(Ink.r, Ink.g, Ink.b, 0.7f), FontStyle.Italic);
    Place(status.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -635f), new Vector2(560f, 40f));

    var cont = Button(right, "Continue", "Continuar  →", 32, 76f);
    Place((RectTransform)cont.transform, new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(300f, 76f));

    so.FindProperty("_cameraStep").objectReferenceValue = step.gameObject;
    so.FindProperty("_cameraButtonTemplate").objectReferenceValue = template;
    so.FindProperty("_cameraPreview").objectReferenceValue = preview;
    so.FindProperty("_noCameraMessage").objectReferenceValue = noCamera.gameObject;
    so.FindProperty("_cameraStatus").objectReferenceValue = status;
    so.FindProperty("_cameraContinue").objectReferenceValue = cont;
  }

  // Paso 2: prueba de gesto
  private static void BuildGestureStep(RectTransform parent, SerializedObject so)
  {
    var step = Rect("Step_Gesto", parent);
    Stretch(step);

    var frame = Image("PreviewFrame", step, new Color(0.99f, 0.96f, 0.88f, 1f));
    Outline(frame, Line);
    Place(frame.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-260f, 60f), new Vector2(1040f, 640f));

    var preview = Raw("Preview", frame.rectTransform);
    Stretch(preview.rectTransform);
    preview.rectTransform.offsetMin = new Vector2(16f, 16f);
    preview.rectTransform.offsetMax = new Vector2(-16f, -16f);

    // Objeto de prueba: una hoja (si existe el sprite) a la izquierda.
    var leaf = Image("TestObject", preview.rectTransform, Color.white);
    leaf.sprite = FindSprite("Hoja_llena");
    leaf.preserveAspect = true;
    Place(leaf.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-260f, 0f), new Vector2(130f, 130f));

    var cursor = Image("HandCursor", preview.rectTransform, new Color(0.3f, 0.8f, 1f, 0.9f));
    Place(cursor.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46f, 46f));
    cursor.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
    Outline(cursor, Color.white);

    var instructions = Label("Instructions", step, "Cierra la mano sobre la hoja y muévela", 30, Ink, FontStyle.Normal);
    Place(instructions.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-260f, -305f), new Vector2(1000f, 50f));

    // Columna derecha
    var banner = Image("TitleBanner", step, new Color(0.99f, 0.96f, 0.88f, 1f));
    Outline(banner, Line);
    Place(banner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(560f, 300f), new Vector2(400f, 120f));
    var title = Label("Title", banner.rectTransform, "Prueba\nde gesto", 44, Ink, FontStyle.Normal);
    Stretch(title.rectTransform);

    var open = Step(step, "Open", "1. Abre la mano", new Vector2(560f, 130f));
    var close = Step(step, "Close", "2. Cierra la mano", new Vector2(560f, 10f));
    var move = Step(step, "Move", "3. Agarra y mueve", new Vector2(560f, -110f));

    var back = Button(step, "Back", "←  Volver", 30, 72f);
    Place((RectTransform)back.transform, new Vector2(0.5f, 0.5f), new Vector2(-560f, -400f), new Vector2(260f, 72f));
    var cont = Button(step, "Continue", "Continuar  →", 32, 76f);
    Place((RectTransform)cont.transform, new Vector2(0.5f, 0.5f), new Vector2(560f, -400f), new Vector2(300f, 76f));

    so.FindProperty("_gestureStep").objectReferenceValue = step.gameObject;
    so.FindProperty("_gesturePreview").objectReferenceValue = preview;
    so.FindProperty("_handCursor").objectReferenceValue = cursor.rectTransform;
    so.FindProperty("_testObject").objectReferenceValue = leaf.rectTransform;
    SetArray(so.FindProperty("_openDone"), open);
    SetArray(so.FindProperty("_closeDone"), close);
    SetArray(so.FindProperty("_moveDone"), move);
    so.FindProperty("_gestureContinue").objectReferenceValue = cont;
    so.FindProperty("_gestureBack").objectReferenceValue = back;

    step.gameObject.SetActive(false);
  }

  /// <summary>Fila de instruccion con su marca de "hecho" (apagada al principio).</summary>
  private static GameObject Step(RectTransform parent, string name, string text, Vector2 position)
  {
    var row = Image($"Step_{name}", parent, new Color(0.99f, 0.96f, 0.88f, 0.9f));
    Outline(row, Line);
    Place(row.rectTransform, new Vector2(0.5f, 0.5f), position, new Vector2(400f, 90f));

    var label = Label("Label", row.rectTransform, text, 30, Ink, FontStyle.Bold);
    Stretch(label.rectTransform);
    label.rectTransform.offsetMin = new Vector2(20f, 0f);
    label.alignment = TextAnchor.MiddleLeft;

    var done = Image("Done", row.rectTransform, Green);
    var doneRect = done.rectTransform;
    doneRect.anchorMin = doneRect.anchorMax = new Vector2(1f, 0.5f);
    doneRect.pivot = new Vector2(1f, 0.5f);
    doneRect.sizeDelta = new Vector2(100f, 54f);
    doneRect.anchoredPosition = new Vector2(-16f, 0f);
    var doneText = Label("Text", doneRect, "Listo", 24, Color.white, FontStyle.Bold);
    Stretch(doneText.rectTransform);
    done.gameObject.SetActive(false);

    return done.gameObject;
  }

  // ---------- Utilidades ----------

  private static RectTransform Page(string name, RectTransform book, Vector2 min, Vector2 max)
  {
    var page = Image(name, book, Paper);
    var rect = page.rectTransform;
    rect.anchorMin = min;
    rect.anchorMax = max;
    rect.offsetMin = rect.offsetMax = Vector2.zero;
    return rect;
  }

  private static Button Button(RectTransform parent, string name, string label, int fontSize, float height)
  {
    var image = Image(name, parent, Color.white);
    image.raycastTarget = true;
    Outline(image, Line);
    var element = image.gameObject.AddComponent<LayoutElement>();
    element.preferredHeight = height;
    element.minHeight = height;

    var button = image.gameObject.AddComponent<UnityEngine.UI.Button>();
    var colors = button.colors;
    colors.normalColor = new Color(0.99f, 0.96f, 0.88f, 1f);
    colors.highlightedColor = new Color(1f, 0.92f, 0.7f, 1f);
    colors.selectedColor = new Color(1f, 0.85f, 0.5f, 1f);
    colors.pressedColor = new Color(0.9f, 0.75f, 0.45f, 1f);
    colors.fadeDuration = 0.08f;
    button.colors = colors;

    var text = Label("Label", image.rectTransform, label, fontSize, Ink, FontStyle.Normal);
    Stretch(text.rectTransform);
    return button;
  }

  private static void SetArray(SerializedProperty property, GameObject go)
  {
    property.arraySize = 1;
    property.GetArrayElementAtIndex(0).objectReferenceValue = go;
  }

  private static Sprite FindSprite(string name)
  {
    foreach (var guid in AssetDatabase.FindAssets($"{name} t:Sprite", new[] { "Assets" }))
    {
      var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid));
      if (sprite != null && sprite.name == name) return sprite;
    }

    return null;
  }

  private static void Outline(Graphic graphic, Color color)
  {
    var outline = graphic.gameObject.AddComponent<UnityEngine.UI.Outline>();
    outline.effectColor = color;
    outline.effectDistance = new Vector2(2f, -2f);
  }

  private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
  {
    rect.anchorMin = rect.anchorMax = anchor;
    rect.pivot = new Vector2(0.5f, anchor.y >= 1f ? 1f : anchor.y <= 0f ? 0f : 0.5f);
    rect.anchoredPosition = position;
    rect.sizeDelta = size;
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
    rect.offsetMin = rect.offsetMax = Vector2.zero;
  }

  private static Image Image(string name, Transform parent, Color color)
  {
    var image = Rect(name, parent).gameObject.AddComponent<UnityEngine.UI.Image>();
    image.color = color;
    image.raycastTarget = false;
    return image;
  }

  private static RawImage Raw(string name, Transform parent)
  {
    var raw = Rect(name, parent).gameObject.AddComponent<RawImage>();
    raw.color = new Color(0f, 0f, 0f, 0.6f);
    raw.raycastTarget = false;
    return raw;
  }

  private static Text Label(string name, Transform parent, string value, int size, Color color, FontStyle style)
  {
    var text = Rect(name, parent).gameObject.AddComponent<Text>();
    text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    text.text = value;
    text.fontSize = size;
    text.fontStyle = style;
    text.color = color;
    text.alignment = TextAnchor.MiddleCenter;
    text.horizontalOverflow = HorizontalWrapMode.Wrap;
    text.verticalOverflow = VerticalWrapMode.Overflow;
    text.raycastTarget = false;
    return text;
  }
}
