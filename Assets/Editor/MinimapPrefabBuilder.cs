// MinimapPrefabBuilder.cs
// Crea el prefab por defecto del minimapa (Assets/UI/Minimap/MinimapUI):
// marco MiniMap_Marco arriba a la derecha, vista circular 3D, flecha del
// jugador, indicador del norte e icono base para los marcadores.
// Tambien genera los sprites basicos (flecha y punto) y el material circular
// si no existen; puedes reemplazarlos por los tuyos cuando quieras.
//
// Menu: Tools > Mapa > Agregar minimapa a la escena abierta
//       Tools > Mapa > Seleccionar prefab del minimapa
// Despues el prefab se diseña libremente (doble clic).

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class MinimapPrefabBuilder
{
  private const string Folder = "Assets/UI/Minimap";
  private const string PrefabName = "MinimapUI";
  private const string ArrowPath = Folder + "/Minimap_Jugador.png";
  private const string DotPath = Folder + "/Minimap_Punto.png";
  private const string MaterialPath = Folder + "/MinimapCircle.mat";
  private const string FrameSprite = "MiniMap_Marco";

  // Medidas del marco (proporcion del PNG 1283x1226) y de su hueco interior.
  private static readonly Vector2 FrameSize = new Vector2(340f, 325f);
  private const float ViewDiameter = 236f;
  private static readonly Vector2 ViewOffset = new Vector2(4f, 2f);

  static MinimapPrefabBuilder()
  {
    EditorApplication.delayCall += () =>
    {
      if (!EditorApplication.isPlayingOrWillChangePlaymode && FindPrefab() == null)
      {
        Save(Build());
        Debug.Log($"[Minimapa] Prefab creado en {Folder}/{PrefabName}. Usa Tools > Mapa > Agregar minimapa a la escena abierta.");
      }
    };
  }

  [MenuItem("Tools/Mapa/Agregar minimapa a la escena abierta")]
  private static void AddToScene()
  {
    if (Object.FindAnyObjectByType<Minimap>(FindObjectsInactive.Include) != null)
    {
      EditorUtility.DisplayDialog("Minimapa", "La escena ya tiene un Minimap.", "OK");
      return;
    }

    var prefab = FindPrefab();

    if (prefab == null)
    {
      prefab = Save(Build()).GetComponent<Minimap>();
    }

    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject);
    Undo.RegisterCreatedObjectUndo(instance, "Agregar minimapa");
    EditorSceneManager.MarkSceneDirty(instance.scene);
    Selection.activeObject = instance;
  }

  [MenuItem("Tools/Mapa/Seleccionar prefab del minimapa")]
  private static void SelectPrefab()
  {
    var prefab = FindPrefab();

    if (prefab != null)
    {
      Selection.activeObject = prefab.gameObject;
      EditorGUIUtility.PingObject(prefab.gameObject);
    }
  }

  [MenuItem("Tools/Mapa/Agregar marcador al objeto seleccionado")]
  private static void AddMarker()
  {
    foreach (var go in Selection.gameObjects)
    {
      if (go.GetComponent<MapMarker>() == null)
      {
        Undo.AddComponent<MapMarker>(go);
      }
    }
  }

  [MenuItem("Tools/Mapa/Agregar marcador al objeto seleccionado", true)]
  private static bool AddMarkerValidate() => Selection.gameObjects.Length > 0;

  private static Minimap FindPrefab()
  {
    Minimap fallback = null;

    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
    {
      var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
      var map = go != null ? go.GetComponent<Minimap>() : null;

      if (map == null)
      {
        continue;
      }

      if (go.name == PrefabName)
      {
        return map;
      }

      fallback ??= map;
    }

    return fallback;
  }

  private static Sprite FindSprite(string name)
  {
    foreach (var guid in AssetDatabase.FindAssets($"{name} t:Sprite", new[] { "Assets" }))
    {
      var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid));

      if (sprite != null && sprite.name == name)
      {
        return sprite;
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

  // ---------- Recursos generados ----------

  private static Material GetMaterial()
  {
    var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

    if (mat != null)
    {
      return mat;
    }

    var shader = Shader.Find("UI/MinimapCircle");

    if (shader == null)
    {
      Debug.LogWarning("[Minimapa] No encontre el shader UI/MinimapCircle; la vista quedara cuadrada hasta asignarle el material.");
      return null;
    }

    EnsureFolder(Folder);
    mat = new Material(shader) { name = "MinimapCircle" };
    AssetDatabase.CreateAsset(mat, MaterialPath);
    return mat;
  }

  private static Sprite GetGeneratedSprite(string path, System.Func<float, float, float> sdf)
  {
    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

    if (sprite != null)
    {
      return sprite;
    }

    EnsureFolder(Folder);
    const int size = 128;
    var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
    var px = 2f / size;
    const float outline = 0.16f;
    var fill = Color.white;
    var line = new Color(0.08f, 0.1f, 0.18f, 1f);

    for (var y = 0; y < size; y++)
    {
      for (var x = 0; x < size; x++)
      {
        var u = (x + 0.5f) / size * 2f - 1f;
        var v = (y + 0.5f) / size * 2f - 1f;
        var d = sdf(u, v);
        var alpha = Mathf.Clamp01(0.5f - d / px);
        var t = Mathf.Clamp01((-d - outline) / px + 0.5f);
        var c = Color.Lerp(line, fill, t);
        c.a = alpha;
        tex.SetPixel(x, y, c);
      }
    }

    File.WriteAllBytes(path, tex.EncodeToPNG());
    Object.DestroyImmediate(tex);
    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
    importer.textureType = TextureImporterType.Sprite;
    importer.spriteImportMode = SpriteImportMode.Single;
    importer.alphaIsTransparency = true;
    importer.mipmapEnabled = false;
    importer.SaveAndReimport();

    return AssetDatabase.LoadAssetAtPath<Sprite>(path);
  }

  /// <summary>Flecha apuntando hacia arriba (distancia con signo, negativa dentro).</summary>
  private static float ArrowSdf(float x, float y)
  {
    var p = new Vector2(x, y);
    Vector2[] poly =
    {
      new Vector2(0f, 0.9f),
      new Vector2(0.7f, -0.8f),
      new Vector2(0f, -0.42f),
      new Vector2(-0.7f, -0.8f)
    };

    var dist = float.MaxValue;
    var inside = false;

    for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
    {
      var a = poly[j];
      var b = poly[i];
      var ab = b - a;
      var h = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
      dist = Mathf.Min(dist, (p - (a + ab * h)).magnitude);

      if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x)
      {
        inside = !inside;
      }
    }

    return inside ? -dist : dist;
  }

  private static float DotSdf(float x, float y) => new Vector2(x, y).magnitude - 0.82f;

  // ---------- Prefab por defecto ----------

  private static GameObject Build()
  {
    var arrow = GetGeneratedSprite(ArrowPath, ArrowSdf);
    var dot = GetGeneratedSprite(DotPath, DotSdf);

    var root = new GameObject(PrefabName, typeof(RectTransform));

    var canvas = root.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = 90;

    var scaler = root.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920f, 1080f);
    scaler.matchWidthOrHeight = 0.5f;

    var group = root.AddComponent<CanvasGroup>();
    group.blocksRaycasts = false;
    group.interactable = false;

    var minimap = root.AddComponent<Minimap>();

    // Panel arriba a la derecha (muevelo donde quieras).
    var panel = Rect("Panel", root.transform);
    panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
    panel.pivot = new Vector2(1f, 1f);
    panel.anchoredPosition = new Vector2(-36f, -24f);
    panel.sizeDelta = FrameSize;

    // Vista 3D recortada en circulo
    var view = Rect("Vista", panel);
    view.anchorMin = view.anchorMax = new Vector2(0.5f, 0.5f);
    view.pivot = new Vector2(0.5f, 0.5f);
    view.anchoredPosition = ViewOffset;
    view.sizeDelta = new Vector2(ViewDiameter, ViewDiameter);
    var raw = view.gameObject.AddComponent<RawImage>();
    raw.raycastTarget = false;
    raw.material = GetMaterial();
    raw.color = Color.white;

    var icons = Rect("Iconos", view);
    icons.anchorMin = icons.anchorMax = new Vector2(0.5f, 0.5f);
    icons.pivot = new Vector2(0.5f, 0.5f);
    icons.anchoredPosition = Vector2.zero;
    icons.sizeDelta = Vector2.zero;

    var template = Rect("IconoBase", icons);
    template.sizeDelta = new Vector2(20f, 20f);
    var templateImage = template.gameObject.AddComponent<Image>();
    templateImage.sprite = dot;
    templateImage.preserveAspect = true;
    templateImage.raycastTarget = false;
    template.gameObject.SetActive(false);

    var player = Rect("Jugador", icons);
    player.sizeDelta = new Vector2(30f, 30f);
    var playerImage = player.gameObject.AddComponent<Image>();
    playerImage.sprite = arrow;
    playerImage.color = new Color(1f, 0.62f, 0.2f);
    playerImage.preserveAspect = true;
    playerImage.raycastTarget = false;

    // Marco por encima de todo
    var frame = Rect("Marco", panel);
    frame.anchorMin = Vector2.zero;
    frame.anchorMax = Vector2.one;
    frame.offsetMin = Vector2.zero;
    frame.offsetMax = Vector2.zero;
    var frameImage = frame.gameObject.AddComponent<Image>();
    frameImage.sprite = FindSprite(FrameSprite);
    frameImage.preserveAspect = true;
    frameImage.raycastTarget = false;

    if (frameImage.sprite == null)
    {
      frameImage.color = new Color(1f, 1f, 1f, 0f);
      Debug.LogWarning($"[Minimapa] No encontre el sprite {FrameSprite}; asigna el marco a mano en el prefab.");
    }

    // Norte: hijo de la vista, por encima del marco no hace falta.
    var north = Rect("Norte", panel);
    north.anchorMin = north.anchorMax = new Vector2(0.5f, 0.5f);
    north.sizeDelta = new Vector2(30f, 30f);
    var northBg = north.gameObject.AddComponent<Image>();
    northBg.sprite = dot;
    northBg.color = new Color(0.95f, 0.92f, 0.82f);
    northBg.raycastTarget = false;

    var northText = Rect("Letra", north);
    northText.anchorMin = Vector2.zero;
    northText.anchorMax = Vector2.one;
    northText.offsetMin = Vector2.zero;
    northText.offsetMax = Vector2.zero;
    var text = northText.gameObject.AddComponent<Text>();
    text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    text.text = "N";
    text.fontSize = 18;
    text.fontStyle = FontStyle.Bold;
    text.alignment = TextAnchor.MiddleCenter;
    text.color = new Color(0.12f, 0.16f, 0.3f);
    text.raycastTarget = false;

    // El norte se mueve dentro del panel pero centrado en la vista.
    var northHolder = Rect("NorteCentro", panel);
    northHolder.anchorMin = northHolder.anchorMax = new Vector2(0.5f, 0.5f);
    northHolder.anchoredPosition = ViewOffset;
    northHolder.sizeDelta = new Vector2(ViewDiameter, ViewDiameter);
    north.SetParent(northHolder, false);
    north.anchoredPosition = Vector2.zero;

    var so = new SerializedObject(minimap);
    so.FindProperty("_view").objectReferenceValue = raw;
    so.FindProperty("_iconsRoot").objectReferenceValue = icons;
    so.FindProperty("_iconTemplate").objectReferenceValue = templateImage;
    so.FindProperty("_playerIcon").objectReferenceValue = player;
    so.FindProperty("_northIcon").objectReferenceValue = north;
    so.FindProperty("_cullingMask").intValue = ~((1 << 5) | (1 << 1));
    so.ApplyModifiedPropertiesWithoutUndo();

    return root;
  }

  private static RectTransform Rect(string name, Transform parent)
  {
    var go = new GameObject(name, typeof(RectTransform));
    go.transform.SetParent(parent, false);
    return (RectTransform)go.transform;
  }
}
