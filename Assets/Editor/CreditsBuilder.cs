// CreditsBuilder.cs
// Tools > Menu principal > Crear pantalla de creditos
// 1. Pone credits1.0 / credits2.0 (Assets/UI) como Sprite.
// 2. Crea UI/MainMenu/CreditsUI.prefab: fondo de cielo, avatares, titulo,
//    un texto encima de cada avatar (Nombre_1..6) y un boton Volver copiado
//    del estilo de los botones del menu.
// 3. Añade la pagina de Colaboradores (recuadro de texto con scroll) y el
//    boton "Mas creditos", si aun no estan.
// 4. Mete el prefab dentro de MainMenuUI y lo conecta al boton Creditos.
// Si CreditsUI ya existe NO se rehace (para no borrar tus textos); solo se
// le añade lo que falte y se vuelve a conectar al menu.

using System.Linq;
using Menus;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class CreditsBuilder
{
  private const string BackgroundPath = "Assets/UI/credits1.0.png";
  private const string AvatarsPath = "Assets/UI/credits2.0.png";
  private const string MenuPath = "Assets/UI/MainMenu/MainMenuUI.prefab";
  private const string CreditsPath = "Assets/UI/MainMenu/CreditsUI.prefab";

  // Centro horizontal de cada avatar dentro de la imagen (0..1).
  private static readonly float[] AvatarX = { 0.095f, 0.24f, 0.40f, 0.565f, 0.73f, 0.895f };

  [MenuItem("Tools/Menu principal/Crear pantalla de creditos")]
  private static void Run()
  {
    if (EditorApplication.isPlaying)
    {
      EditorUtility.DisplayDialog("Creditos", "Sal del modo Play primero.", "OK");
      return;
    }

    var background = MakeSprite(BackgroundPath);
    var avatars = MakeSprite(AvatarsPath);

    if (background == null || avatars == null)
    {
      EditorUtility.DisplayDialog("Creditos", "No encuentro credits1.0.png / credits2.0.png en Assets/UI.", "OK");
      return;
    }

    var menu = PrefabUtility.LoadPrefabContents(MenuPath);

    try
    {
      var mainMenu = menu.GetComponent<MainMenu>();
      var so = new SerializedObject(mainMenu);
      var creditsButton = so.FindProperty("_creditsButton").objectReferenceValue as Button;
      var font = menu.GetComponentInChildren<Text>(true) != null ? menu.GetComponentInChildren<Text>(true).font : null;
      var created = false;

      if (AssetDatabase.LoadAssetAtPath<GameObject>(CreditsPath) == null)
      {
        BuildCreditsPrefab(background, avatars, creditsButton, font);
        created = true;
      }

      EnsureExtraPage(font);

      // Instancia dentro del menu (una sola).
      var screen = menu.GetComponentInChildren<CreditsScreen>(true);

      if (screen == null)
      {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(CreditsPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, menu.transform);
        var rt = (RectTransform)instance.transform;
        Stretch(rt);
        instance.transform.SetAsLastSibling();
        instance.SetActive(false);
        screen = instance.GetComponent<CreditsScreen>();
      }

      so.FindProperty("_creditsScreen").objectReferenceValue = screen;
      so.ApplyModifiedPropertiesWithoutUndo();
      PrefabUtility.SaveAsPrefabAsset(menu, MenuPath);

      EditorUtility.DisplayDialog("Creditos",
        (created ? "Creado " : "Ya existia ") + CreditsPath + " y conectado al boton Creditos del menu.\n\n" +
        "Abre el prefab CreditsUI y cambia los textos:\n- Avatares > Titulo y Nombre_1..6\n- Colaboradores > Titulo_Extra y Panel > Lista > Viewport > Texto", "OK");
      Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(CreditsPath);
    }
    finally
    {
      PrefabUtility.UnloadPrefabContents(menu);
    }
  }

  private static void BuildCreditsPrefab(Sprite background, Sprite avatars, Button styleButton, Font font)
  {
    var root = new GameObject("CreditsUI", typeof(RectTransform), typeof(CanvasGroup));
    Stretch((RectTransform)root.transform);
    var screen = root.AddComponent<CreditsScreen>();

    // Fondo: cubre toda la pantalla sin deformarse.
    var bg = NewImage("Fondo", root.transform, background);
    Stretch(bg.rectTransform);
    var bgFit = bg.gameObject.AddComponent<AspectRatioFitter>();
    bgFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
    bgFit.aspectRatio = background.rect.width / background.rect.height;
    bg.raycastTarget = true; // bloquea los clics al menu de atras

    // Avatares: entran enteros; los textos van dentro para seguirlos.
    var av = NewImage("Avatares", root.transform, avatars);
    Stretch(av.rectTransform);
    var avFit = av.gameObject.AddComponent<AspectRatioFitter>();
    avFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
    avFit.aspectRatio = avatars.rect.width / avatars.rect.height;

    var title = NewText("Titulo", av.transform, font, "Créditos", 96, FontStyle.Bold);
    Place(title.rectTransform, 0.5f, 0.9f, new Vector2(1200, 140));

    for (var i = 0; i < AvatarX.Length; i++)
    {
      var label = NewText("Nombre_" + (i + 1), av.transform, font,
        "<b>Nombre " + (i + 1) + "</b>\n<size=24>Rol</size>", 32, FontStyle.Normal);
      Place(label.rectTransform, AvatarX[i], 0.73f, new Vector2(255, 110));
    }

    // Boton Volver arriba a la izquierda, con el estilo de los del menu.
    Button back;

    if (styleButton != null)
    {
      var copy = Object.Instantiate(styleButton.gameObject, root.transform);
      copy.name = "Volver";
      back = copy.GetComponent<Button>();
      back.onClick = new Button.ButtonClickedEvent();
      foreach (var layout in copy.GetComponents<LayoutElement>()) Object.DestroyImmediate(layout);
      var text = copy.GetComponentInChildren<Text>(true);
      if (text != null) text.text = "Volver";
    }
    else
    {
      var img = NewImage("Volver", root.transform, null);
      img.color = new Color(0.1f, 0.15f, 0.3f, 0.8f);
      back = img.gameObject.AddComponent<Button>();
      var text = NewText("Label", img.transform, font, "Volver", 34, FontStyle.Bold);
      Stretch(text.rectTransform);
    }

    var brt = (RectTransform)back.transform;
    brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f);
    brt.pivot = new Vector2(0f, 1f);
    brt.anchoredPosition = new Vector2(40f, -40f);
    brt.sizeDelta = new Vector2(320f, 80f);
    brt.localScale = Vector3.one;

    var so = new SerializedObject(screen);
    so.FindProperty("_backButton").objectReferenceValue = back;
    so.ApplyModifiedPropertiesWithoutUndo();

    PrefabUtility.SaveAsPrefabAsset(root, CreditsPath);
    Object.DestroyImmediate(root);
  }

  // ---------- Pagina de colaboradores ----------

  private const string ExtraPlaceholder =
    "<b><size=44>Colaboradores</size></b>\n" +
    "Nombre — Lo que hizo\nNombre — Lo que hizo\n\n" +
    "<b><size=44>Música y sonido</size></b>\n" +
    "Nombre — Lo que hizo\n\n" +
    "<b><size=44>Agradecimientos especiales</size></b>\n" +
    "Nombre\nNombre\n\n" +
    "<b><size=44>Herramientas</size></b>\n" +
    "Unity 6";

  private static void EnsureExtraPage(Font font)
  {
    var root = PrefabUtility.LoadPrefabContents(CreditsPath);

    try
    {
      var screen = root.GetComponent<CreditsScreen>();
      var team = root.transform.Find("Avatares");
      var page = root.transform.Find("Colaboradores");
      ScrollRect scroll;

      if (page == null)
      {
        var go = new GameObject("Colaboradores", typeof(RectTransform));
        go.transform.SetParent(root.transform, false);
        Stretch((RectTransform)go.transform);
        go.transform.SetSiblingIndex(team != null ? team.GetSiblingIndex() + 1 : 1);
        page = go.transform;

        var title = NewText("Titulo_Extra", page, font, "Colaboradores y agradecimientos", 72, FontStyle.Bold);
        Place(title.rectTransform, 0.5f, 0.9f, new Vector2(1500, 130));

        // Recuadro semitransparente para que el texto se lea sobre el cielo.
        var panel = NewImage("Panel", page, null);
        panel.color = new Color(0.06f, 0.12f, 0.28f, 0.6f);
        var prt = panel.rectTransform;
        prt.anchorMin = new Vector2(0.15f, 0.07f);
        prt.anchorMax = new Vector2(0.85f, 0.82f);
        prt.offsetMin = prt.offsetMax = Vector2.zero;

        // Lista con scroll (rueda del raton, arrastrar, stick o flechas).
        var list = NewImage("Lista", panel.transform, null);
        list.color = new Color(0f, 0f, 0f, 0f);
        list.raycastTarget = true;
        Stretch(list.rectTransform);
        list.rectTransform.offsetMin = new Vector2(40f, 30f);
        list.rectTransform.offsetMax = new Vector2(-40f, -30f);

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(list.transform, false);
        Stretch((RectTransform)viewport.transform);

        var text = NewText("Texto", viewport.transform, font, ExtraPlaceholder, 34, FontStyle.Normal);
        text.alignment = TextAnchor.UpperCenter;
        text.lineSpacing = 1.1f;
        var trt = text.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = Vector2.zero;
        trt.sizeDelta = Vector2.zero;
        var fitter = text.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll = list.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        scroll.viewport = (RectTransform)viewport.transform;
        scroll.content = trt;

        go.SetActive(false);
      }
      else
      {
        scroll = page.GetComponentInChildren<ScrollRect>(true);
      }

      // Boton "Mas creditos" arriba a la derecha (copia de Volver).
      var pageButton = root.transform.Find("MasCreditos");
      var back = root.transform.Find("Volver");

      if (pageButton == null && back != null)
      {
        var copy = Object.Instantiate(back.gameObject, root.transform);
        copy.name = "MasCreditos";
        copy.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();
        var brt = (RectTransform)copy.transform;
        brt.anchorMin = brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot = new Vector2(1f, 1f);
        brt.anchoredPosition = new Vector2(-40f, -40f);
        var label = copy.GetComponentInChildren<Text>(true);
        if (label != null) label.text = "Más créditos";
        pageButton = copy.transform;
      }

      var so = new SerializedObject(screen);
      if (team != null) so.FindProperty("_teamPage").objectReferenceValue = team.gameObject;
      so.FindProperty("_extraPage").objectReferenceValue = page.gameObject;
      so.FindProperty("_extraScroll").objectReferenceValue = scroll;

      if (pageButton != null)
      {
        so.FindProperty("_pageButton").objectReferenceValue = pageButton.GetComponent<Button>();
        so.FindProperty("_pageButtonLabel").objectReferenceValue = pageButton.GetComponentInChildren<Text>(true);
      }

      so.ApplyModifiedPropertiesWithoutUndo();
      PrefabUtility.SaveAsPrefabAsset(root, CreditsPath);
    }
    finally
    {
      PrefabUtility.UnloadPrefabContents(root);
    }
  }

  private static Sprite MakeSprite(string path)
  {
    var importer = AssetImporter.GetAtPath(path) as TextureImporter;

    if (importer == null)
    {
      return null;
    }

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

  private static Image NewImage(string name, Transform parent, Sprite sprite)
  {
    var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    go.transform.SetParent(parent, false);
    var image = go.GetComponent<Image>();
    image.sprite = sprite;
    image.raycastTarget = false;
    return image;
  }

  private static Text NewText(string name, Transform parent, Font font, string value, int size, FontStyle style)
  {
    var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
    go.transform.SetParent(parent, false);
    var text = go.GetComponent<Text>();
    text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    text.text = value;
    text.fontSize = size;
    text.fontStyle = style;
    text.alignment = TextAnchor.MiddleCenter;
    text.horizontalOverflow = HorizontalWrapMode.Wrap;
    text.verticalOverflow = VerticalWrapMode.Overflow;
    text.supportRichText = true;
    text.color = Color.white;
    text.raycastTarget = false;

    // Contorno azul oscuro para que se lea sobre el cielo.
    var outline = go.AddComponent<Outline>();
    outline.effectColor = new Color(0.08f, 0.16f, 0.35f, 0.9f);
    outline.effectDistance = new Vector2(2f, -2f);
    var shadow = go.AddComponent<Shadow>();
    shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
    shadow.effectDistance = new Vector2(3f, -4f);
    return text;
  }

  private static void Stretch(RectTransform rt)
  {
    rt.anchorMin = Vector2.zero;
    rt.anchorMax = Vector2.one;
    rt.offsetMin = rt.offsetMax = Vector2.zero;
    rt.pivot = new Vector2(0.5f, 0.5f);
    rt.localScale = Vector3.one;
  }

  // Ancla en un punto relativo (0..1) de la imagen de avatares, con tamaño fijo.
  private static void Place(RectTransform rt, float x, float y, Vector2 size)
  {
    rt.anchorMin = rt.anchorMax = new Vector2(x, y);
    rt.pivot = new Vector2(0.5f, 0.5f);
    rt.anchoredPosition = Vector2.zero;
    rt.sizeDelta = size;
  }
}
