// IntroBuilder.cs
// Tools > Menu principal > Crear historia de contexto
// 1. Pone las imagenes de Assets/UI/Contexto como Sprite (en orden por
//    numero: Chopi, Chopi2, Chopi3, CHOPI4...).
// 2. Crea Assets/UI/Contexto/IntroContexto.prefab con una diapositiva por
//    imagen (texto de ejemplo y duracion para que lo cambies).
// 3. Lo conecta al menu principal (MainMenuUI > Intro) para que salga al
//    empezar una Nueva partida, despues de configurar la camara.
// Si el prefab ya existe NO se rehace (para no borrar tus textos): solo se
// añaden imagenes nuevas al final y se vuelve a conectar.

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Menus;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class IntroBuilder
{
  private const string Folder = "Assets/UI/Contexto";
  private const string PrefabPath = Folder + "/IntroContexto.prefab";
  private const string MenuPath = "Assets/UI/MainMenu/MainMenuUI.prefab";

  [MenuItem("Tools/Menu principal/Crear historia de contexto")]
  private static void Run()
  {
    if (EditorApplication.isPlaying)
    {
      EditorUtility.DisplayDialog("Historia", "Sal del modo Play primero.", "OK");
      return;
    }

    var files = Directory.GetFiles(Folder)
      .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg"))
      .Select(f => f.Replace('\\', '/'))
      .OrderBy(f => Number(Path.GetFileNameWithoutExtension(f)))
      .ThenBy(f => f)
      .ToList();

    var sprites = files.Select(MakeSprite).Where(s => s != null).ToList();

    if (sprites.Count == 0)
    {
      EditorUtility.DisplayDialog("Historia", "No hay imagenes en " + Folder, "OK");
      return;
    }

    var created = false;

    if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
    {
      Build(sprites);
      created = true;
    }
    else
    {
      AddMissing(sprites);
    }

    // Conectar al menu principal.
    var intro = AssetDatabase.LoadAssetAtPath<IntroSlideshow>(PrefabPath);
    var menu = PrefabUtility.LoadPrefabContents(MenuPath);

    try
    {
      var so = new SerializedObject(menu.GetComponent<MainMenu>());
      so.FindProperty("_intro").objectReferenceValue = intro;
      so.ApplyModifiedPropertiesWithoutUndo();
      PrefabUtility.SaveAsPrefabAsset(menu, MenuPath);
    }
    finally
    {
      PrefabUtility.UnloadPrefabContents(menu);
    }

    EditorUtility.DisplayDialog("Historia",
      (created ? "Creado " : "Actualizado ") + PrefabPath + " con " + sprites.Count + " imagenes y conectado al menu principal.\n\n" +
      "Abre el prefab y en 'Intro Slideshow > Slides' escribe el texto y los segundos de cada imagen.", "OK");
    Selection.activeObject = intro;
  }

  private static int Number(string name)
  {
    var m = Regex.Match(name, @"(\d+)$");
    return m.Success ? int.Parse(m.Groups[1].Value) : 1;
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
      importer.maxTextureSize = 2048;
      importer.SaveAndReimport();
    }

    return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
  }

  private static void AddMissing(System.Collections.Generic.List<Sprite> sprites)
  {
    var root = PrefabUtility.LoadPrefabContents(PrefabPath);

    try
    {
      var so = new SerializedObject(root.GetComponent<IntroSlideshow>());
      var slides = so.FindProperty("_slides");
      var present = Enumerable.Range(0, slides.arraySize)
        .Select(i => slides.GetArrayElementAtIndex(i).FindPropertyRelative("image").objectReferenceValue)
        .ToList();

      foreach (var sprite in sprites.Where(s => !present.Contains(s)))
      {
        slides.arraySize++;
        var e = slides.GetArrayElementAtIndex(slides.arraySize - 1);
        e.FindPropertyRelative("image").objectReferenceValue = sprite;
        e.FindPropertyRelative("text").stringValue = "Texto de la imagen " + slides.arraySize;
        e.FindPropertyRelative("duration").floatValue = 0f;
      }

      so.ApplyModifiedPropertiesWithoutUndo();
      PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
    }
    finally
    {
      PrefabUtility.UnloadPrefabContents(root);
    }
  }

  private static void Build(System.Collections.Generic.List<Sprite> sprites)
  {
    var font = FindFont();
    var root = new GameObject("IntroContexto", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
    var canvas = root.GetComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = 990;
    var scaler = root.GetComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920, 1080);
    scaler.matchWidthOrHeight = 0.5f;
    var group = root.GetComponent<CanvasGroup>();
    group.alpha = 0f;
    group.blocksRaycasts = true;

    var bg = NewImage("Fondo", root.transform, new Color(0.03f, 0.03f, 0.05f, 1f));
    Stretch(bg.rectTransform, 0f);
    bg.raycastTarget = true;

    var a = NewImage("ImagenA", root.transform, new Color(1f, 1f, 1f, 0f));
    a.preserveAspect = true;
    Stretch(a.rectTransform, 0f);
    var b = NewImage("ImagenB", root.transform, new Color(1f, 1f, 1f, 0f));
    b.preserveAspect = true;
    Stretch(b.rectTransform, 0f);

    // Franja de texto abajo.
    var band = NewImage("FranjaTexto", root.transform, new Color(0.02f, 0.03f, 0.08f, 0.78f));
    var brt = band.rectTransform;
    brt.anchorMin = new Vector2(0.08f, 0.04f);
    brt.anchorMax = new Vector2(0.92f, 0.04f);
    brt.pivot = new Vector2(0.5f, 0f);
    brt.sizeDelta = new Vector2(0f, 190f);
    brt.anchoredPosition = Vector2.zero;
    var bandGroup = band.gameObject.AddComponent<CanvasGroup>();
    bandGroup.alpha = 0f;
    var outline = band.gameObject.AddComponent<Outline>();
    outline.effectColor = new Color(0.95f, 0.8f, 0.4f, 0.6f);
    outline.effectDistance = new Vector2(2f, -2f);

    var text = NewText("Texto", band.transform, font, "", 36, FontStyle.Normal);
    Stretch(text.rectTransform, 0f);
    text.rectTransform.offsetMin = new Vector2(50f, 28f);
    text.rectTransform.offsetMax = new Vector2(-50f, -22f);
    text.alignment = TextAnchor.MiddleCenter;
    text.horizontalOverflow = HorizontalWrapMode.Wrap;
    text.lineSpacing = 1.1f;
    text.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.6f);

    var hint = NewText("Continuar", root.transform, font, "Clic para continuar", 24, FontStyle.Italic);
    var hrt = hint.rectTransform;
    hrt.anchorMin = hrt.anchorMax = new Vector2(0.92f, 0.04f);
    hrt.pivot = new Vector2(1f, 1f);
    hrt.anchoredPosition = new Vector2(0f, -6f);
    hrt.sizeDelta = new Vector2(500f, 34f);
    hint.alignment = TextAnchor.UpperRight;
    hint.color = new Color(1f, 0.92f, 0.7f, 0.8f);

    var counter = NewText("Contador", root.transform, font, "1 / 7", 24, FontStyle.Bold);
    var crt = counter.rectTransform;
    crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
    crt.pivot = new Vector2(1f, 1f);
    crt.anchoredPosition = new Vector2(-40f, -30f);
    crt.sizeDelta = new Vector2(200f, 40f);
    counter.alignment = TextAnchor.UpperRight;
    counter.color = new Color(1f, 1f, 1f, 0.7f);

    var intro = root.AddComponent<IntroSlideshow>();
    var so = new SerializedObject(intro);
    so.FindProperty("_group").objectReferenceValue = group;
    so.FindProperty("_imageA").objectReferenceValue = a;
    so.FindProperty("_imageB").objectReferenceValue = b;
    so.FindProperty("_textBand").objectReferenceValue = bandGroup;
    so.FindProperty("_text").objectReferenceValue = text;
    so.FindProperty("_hint").objectReferenceValue = hint;
    so.FindProperty("_counter").objectReferenceValue = counter;

    var slides = so.FindProperty("_slides");
    slides.arraySize = sprites.Count;

    for (var i = 0; i < sprites.Count; i++)
    {
      var e = slides.GetArrayElementAtIndex(i);
      e.FindPropertyRelative("image").objectReferenceValue = sprites[i];
      e.FindPropertyRelative("text").stringValue = "Texto de la imagen " + (i + 1);
      e.FindPropertyRelative("duration").floatValue = 0f;
    }

    so.ApplyModifiedPropertiesWithoutUndo();
    root.SetActive(false);
    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
    Object.DestroyImmediate(root);
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
    text.supportRichText = true;
    text.color = Color.white;
    text.raycastTarget = false;
    text.verticalOverflow = VerticalWrapMode.Overflow;
    return text;
  }

  private static void Stretch(RectTransform rt, float pad)
  {
    rt.anchorMin = Vector2.zero;
    rt.anchorMax = Vector2.one;
    rt.offsetMin = new Vector2(pad, pad);
    rt.offsetMax = new Vector2(-pad, -pad);
  }

  private static Font FindFont()
  {
    var menu = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPath);
    var text = menu != null ? menu.GetComponentInChildren<Text>(true) : null;
    return text != null && text.font != null ? text.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
  }
}
