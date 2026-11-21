// PauseSettingsPanelBuilder.cs
// Tools > Menu de pausa > Agregar panel de Ajustes (audio) al prefab
//
// En la pantalla "Pausa" del prefab PauseMenuUI crea, junto a los botones
// Continuar / Ajustes / Salir, un panel "Panel_Ajustes" (apagado) con:
//   - Musica  [slider]
//   - Efectos [slider]
//   - Ajustes de camara (boton)
//   - Volver (boton)
// Los botones son copias del boton "Continuar", asi tienen el mismo aspecto.
// No toca nada mas del prefab. Si el panel ya existe, solo lo vuelve a
// conectar (se puede rediseñar libremente en el prefab).

using Menus;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class PauseSettingsPanelBuilder
{
  private const string PanelName = "Panel_Ajustes";

  private static readonly Color Ink = new Color(0.16f, 0.12f, 0.08f, 1f);
  private static readonly Color Track = new Color(0.82f, 0.76f, 0.62f, 1f);
  private static readonly Color Fill = new Color(0.2f, 0.5f, 0.85f, 1f);
  private static readonly Color Knob = new Color(0.98f, 0.95f, 0.86f, 1f);

  [MenuItem("Tools/Menu de pausa/Agregar panel de Ajustes (audio) al prefab")]
  private static void Run()
  {
    var prefab = FindPrefab();

    if (prefab == null)
    {
      EditorUtility.DisplayDialog("Ajustes", "No encontre el prefab del menu de pausa (PauseMenuUI).", "OK");
      return;
    }

    var path = AssetDatabase.GetAssetPath(prefab);
    var root = PrefabUtility.LoadPrefabContents(path);
    string result;

    try
    {
      result = Build(root);

      if (result == null)
      {
        PrefabUtility.SaveAsPrefabAsset(root, path);
      }
    }
    finally
    {
      PrefabUtility.UnloadPrefabContents(root);
    }

    if (result != null)
    {
      EditorUtility.DisplayDialog("Ajustes", result, "OK");
      return;
    }

    Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    EditorUtility.DisplayDialog("Ajustes",
      "Panel de Ajustes listo en " + path + ".\n\nAjustes > Musica / Efectos / Ajustes de camara / Volver.\n" +
      "Puedes cambiar su aspecto en el prefab (Page_Pausa > Content > " + PanelName + ").", "OK");
  }

  /// <summary>Devuelve null si todo fue bien, o el mensaje de error.</summary>
  private static string Build(GameObject root)
  {
    var menu = root.GetComponent<PauseMenu>();
    var so = new SerializedObject(menu);
    var continueButton = so.FindProperty("_continueButton").objectReferenceValue as Button;

    if (continueButton == null)
    {
      return "El menu de pausa no tiene asignado el boton Continuar: no se donde poner el panel.";
    }

    var buttons = (RectTransform)continueButton.transform.parent;
    var container = (RectTransform)buttons.parent;

    // Panel (nuevo o el que ya estaba)
    var existing = container.Find(PanelName);
    RectTransform panel;
    Slider music;
    Slider sfx;
    Button camera;
    Button back;

    if (existing != null)
    {
      panel = (RectTransform)existing;
      var sliders = panel.GetComponentsInChildren<Slider>(true);
      var panelButtons = panel.GetComponentsInChildren<Button>(true);
      music = sliders.Length > 0 ? sliders[0] : null;
      sfx = sliders.Length > 1 ? sliders[1] : null;
      camera = panelButtons.Length > 0 ? panelButtons[0] : null;
      back = panelButtons.Length > 1 ? panelButtons[1] : null;
    }
    else
    {
      panel = new GameObject(PanelName, typeof(RectTransform)).GetComponent<RectTransform>();
      panel.SetParent(container, false);
      panel.SetSiblingIndex(buttons.GetSiblingIndex() + 1);
      CopyRect(buttons, panel);
      // Un poco mas ancho que la lista de botones, para los sliders.
      panel.offsetMin += new Vector2(-90f, 0f);
      panel.offsetMax += new Vector2(90f, 0f);

      var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
      var source = buttons.GetComponent<VerticalLayoutGroup>();
      layout.spacing = source != null ? source.spacing : 22f;
      layout.childAlignment = TextAnchor.MiddleCenter;
      layout.childControlWidth = true;
      layout.childControlHeight = true;
      layout.childForceExpandWidth = true;
      layout.childForceExpandHeight = false;

      var font = LabelFont(continueButton);
      music = SliderRow(panel, "Musica", "Música", font);
      sfx = SliderRow(panel, "Efectos", "Efectos", font);
      camera = CopyButton(continueButton, panel, "AjustesCamara", "Ajustes de cámara");
      back = CopyButton(continueButton, panel, "Volver", "Volver");
    }

    panel.gameObject.SetActive(false);

    // Navegacion con mando: de arriba a abajo en el panel.
    Chain(music, sfx, camera, back);

    // Titulo de la pantalla
    var page = continueButton.GetComponentInParent<PauseMenuPage>(true);
    Graphic title = null;
    if (page != null)
    {
      var t = page.transform.Find("TitleBanner/Title");
      if (t != null) title = t.GetComponent<Graphic>();
    }

    so.FindProperty("_mainButtons").objectReferenceValue = buttons.gameObject;
    so.FindProperty("_settingsPanel").objectReferenceValue = panel.gameObject;
    so.FindProperty("_musicSlider").objectReferenceValue = music;
    so.FindProperty("_sfxSlider").objectReferenceValue = sfx;
    so.FindProperty("_cameraSettingsButton").objectReferenceValue = camera;
    so.FindProperty("_backButton").objectReferenceValue = back;
    if (title != null) so.FindProperty("_titleLabel").objectReferenceValue = title;
    so.ApplyModifiedPropertiesWithoutUndo();
    return null;
  }

  // ---------- Piezas ----------

  private static Slider SliderRow(RectTransform parent, string name, string label, Font font)
  {
    var row = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
    row.SetParent(parent, false);
    var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
    rowLayout.spacing = 20f;
    rowLayout.childAlignment = TextAnchor.MiddleLeft;
    rowLayout.childControlWidth = true;
    rowLayout.childControlHeight = true;
    rowLayout.childForceExpandWidth = false;
    rowLayout.childForceExpandHeight = true;
    var rowElement = row.gameObject.AddComponent<LayoutElement>();
    rowElement.preferredHeight = 70f;
    rowElement.minHeight = 70f;

    // Texto
    var textGo = new GameObject("Label", typeof(RectTransform));
    textGo.transform.SetParent(row, false);
    var text = textGo.AddComponent<Text>();
    text.font = font;
    text.text = label;
    text.fontSize = 34;
    text.color = Ink;
    text.alignment = TextAnchor.MiddleLeft;
    text.raycastTarget = false;
    var textElement = textGo.AddComponent<LayoutElement>();
    textElement.preferredWidth = 170f;
    textElement.minWidth = 170f;

    // Slider
    var sliderGo = new GameObject("Slider", typeof(RectTransform));
    sliderGo.transform.SetParent(row, false);
    var sliderElement = sliderGo.AddComponent<LayoutElement>();
    sliderElement.flexibleWidth = 1f;
    sliderElement.preferredHeight = 40f;

    var background = MakeImage("Background", sliderGo.transform, Track, "UI/Skin/Background.psd");
    var bg = background.rectTransform;
    bg.anchorMin = new Vector2(0f, 0.3f);
    bg.anchorMax = new Vector2(1f, 0.7f);
    bg.offsetMin = bg.offsetMax = Vector2.zero;

    var fillArea = new GameObject("Fill Area", typeof(RectTransform)).GetComponent<RectTransform>();
    fillArea.SetParent(sliderGo.transform, false);
    fillArea.anchorMin = new Vector2(0f, 0.3f);
    fillArea.anchorMax = new Vector2(1f, 0.7f);
    fillArea.offsetMin = new Vector2(5f, 0f);
    fillArea.offsetMax = new Vector2(-15f, 0f);

    var fill = MakeImage("Fill", fillArea, Fill, "UI/Skin/UISprite.psd");
    fill.rectTransform.sizeDelta = new Vector2(10f, 0f);

    var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform)).GetComponent<RectTransform>();
    handleArea.SetParent(sliderGo.transform, false);
    handleArea.anchorMin = Vector2.zero;
    handleArea.anchorMax = Vector2.one;
    handleArea.offsetMin = new Vector2(10f, 0f);
    handleArea.offsetMax = new Vector2(-10f, 0f);

    var handle = MakeImage("Handle", handleArea, Knob, "UI/Skin/Knob.psd");
    handle.rectTransform.sizeDelta = new Vector2(34f, 0f);
    var outline = handle.gameObject.AddComponent<Outline>();
    outline.effectColor = Ink;
    outline.effectDistance = new Vector2(1.5f, -1.5f);

    var slider = sliderGo.AddComponent<Slider>();
    slider.fillRect = fill.rectTransform;
    slider.handleRect = handle.rectTransform;
    slider.targetGraphic = handle;
    slider.direction = Slider.Direction.LeftToRight;
    slider.minValue = 0f;
    slider.maxValue = 1f;
    slider.value = 1f;

    var colors = slider.colors;
    colors.highlightedColor = new Color(1f, 0.92f, 0.7f, 1f);
    colors.selectedColor = new Color(1f, 0.85f, 0.45f, 1f);
    slider.colors = colors;

    return slider;
  }

  private static Image MakeImage(string name, Transform parent, Color color, string builtinSprite)
  {
    var go = new GameObject(name, typeof(RectTransform));
    go.transform.SetParent(parent, false);
    var image = go.AddComponent<Image>();
    image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(builtinSprite);
    image.type = image.sprite != null && builtinSprite != "UI/Skin/Knob.psd" ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
    image.color = color;
    return image;
  }

  private static Button CopyButton(Button source, RectTransform parent, string name, string label)
  {
    var copy = Object.Instantiate(source.gameObject, parent);
    copy.name = name;
    copy.SetActive(true);

    var button = copy.GetComponent<Button>();
    button.onClick = new Button.ButtonClickedEvent();

    // El texto del boton (Text o TextMeshPro).
    foreach (var graphic in copy.GetComponentsInChildren<Graphic>(true))
    {
      if (graphic is Text t)
      {
        t.text = label;
        break;
      }

      if (graphic.GetType().Name.Contains("TextMeshPro"))
      {
        var prop = graphic.GetType().GetProperty("text");
        prop?.SetValue(graphic, label);
        break;
      }
    }

    return button;
  }

  private static Font LabelFont(Button source)
  {
    var text = source.GetComponentInChildren<Text>(true);
    return text != null && text.font != null ? text.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
  }

  private static void Chain(params Selectable[] items)
  {
    for (var i = 0; i < items.Length; i++)
    {
      if (items[i] == null) continue;

      var nav = new Navigation
      {
        mode = Navigation.Mode.Explicit,
        selectOnUp = i > 0 ? items[i - 1] : null,
        selectOnDown = i < items.Length - 1 ? items[i + 1] : null,
        // En los sliders izquierda/derecha cambian el valor.
        selectOnLeft = null,
        selectOnRight = null
      };
      items[i].navigation = nav;
    }
  }

  private static void CopyRect(RectTransform from, RectTransform to)
  {
    to.anchorMin = from.anchorMin;
    to.anchorMax = from.anchorMax;
    to.pivot = from.pivot;
    to.offsetMin = from.offsetMin;
    to.offsetMax = from.offsetMax;
  }

  private static GameObject FindPrefab()
  {
    GameObject fallback = null;

    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
    {
      var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));

      if (go == null || go.GetComponent<PauseMenu>() == null)
      {
        continue;
      }

      if (go.name == "PauseMenuUI")
      {
        return go;
      }

      if (fallback == null) fallback = go;
    }

    return fallback;
  }
}
