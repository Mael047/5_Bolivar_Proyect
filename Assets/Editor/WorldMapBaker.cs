// WorldMapBaker.cs
// Hornea el mapa grande: fotografia el nivel desde arriba (camara ortografica),
// mide el relieve con rayos y lo convierte en un dibujo de pergamino con
// tinta, curvas de nivel y sombreado. Todo se calcula en el editor: en el
// juego el mapa es solo una imagen (costo cero).
//
// Menu Tools > Mapa:
//   Crear area del mapa ........ crea el MapArea ajustado a los terrenos.
//   Hornear mapa ............... fotografia + relieve + estilo (cuando cambias el nivel).
//   Reaplicar estilo ........... solo el estilo, rapido (cuando tocas los ajustes).
//
// Archivos (junto a WorldMapData, por defecto Assets/UI/Mapa):
//   Mapa_Base.png   -> el mapa final que usa el juego.
//   Mapa_Foto.png   -> la fotografia sin estilo (referencia para pintar).
//   Mapa_Altura.bytes -> alturas medidas (solo editor, para reaplicar estilo).
// Se sobrescriben siempre en el mismo archivo: las referencias no se pierden.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class WorldMapBaker
{
  public const string DefaultFolder = "Assets/UI/Mapa";
  private const string BaseName = "Mapa_Base.png";
  private const string PhotoName = "Mapa_Foto.png";
  private const string HeightName = "Mapa_Altura.bytes";

  // ---------- Menus ----------

  [MenuItem("Tools/Mapa/Crear area del mapa (ajustada al terreno)", priority = 20)]
  public static void CreateArea()
  {
    var area = Object.FindAnyObjectByType<MapArea>();

    if (area == null)
    {
      var go = new GameObject("MapArea");
      Undo.RegisterCreatedObjectUndo(go, "Crear area del mapa");
      area = go.AddComponent<MapArea>();
    }

    var bounds = SceneBounds();
    area.transform.position = bounds.center;
    area.size = bounds.size + new Vector3(0f, 10f, 0f);
    if (area.data == null) area.data = GetOrCreateData();
    EditorUtility.SetDirty(area);
    EditorSceneManager.MarkSceneDirty(area.gameObject.scene);
    Selection.activeObject = area.gameObject;
  }

  [MenuItem("Tools/Mapa/Hornear mapa (fotografiar nivel)", priority = 21)]
  public static void BakeMenu()
  {
    var area = Object.FindAnyObjectByType<MapArea>();

    if (area == null)
    {
      CreateArea();
      area = Object.FindAnyObjectByType<MapArea>();
      EditorUtility.DisplayDialog("Mapa",
        "Cree un MapArea ajustado a los terrenos (recuadro amarillo en la escena).\n\n" +
        "Revisa que encuadre bien el nivel y vuelve a usar Hornear mapa.", "OK");
      return;
    }

    Bake(area);
  }

  [MenuItem("Tools/Mapa/Reaplicar estilo (rapido)", priority = 22)]
  public static void RestyleMenu()
  {
    var data = FindData();

    if (data == null || !data.IsBaked)
    {
      EditorUtility.DisplayDialog("Mapa", "Primero hornea el mapa (Tools > Mapa > Hornear mapa).", "OK");
      return;
    }

    Restyle(data);
  }

  // ---------- Datos ----------

  public static WorldMapData FindData()
  {
    foreach (var guid in AssetDatabase.FindAssets("t:WorldMapData"))
    {
      var data = AssetDatabase.LoadAssetAtPath<WorldMapData>(AssetDatabase.GUIDToAssetPath(guid));

      if (data != null)
      {
        return data;
      }
    }

    return null;
  }

  public static WorldMapData GetOrCreateData()
  {
    var data = FindData();

    if (data != null)
    {
      return data;
    }

    EnsureFolder(DefaultFolder);
    data = ScriptableObject.CreateInstance<WorldMapData>();
    AssetDatabase.CreateAsset(data, $"{DefaultFolder}/WorldMapData.asset");
    AssetDatabase.SaveAssets();
    return data;
  }

  private static string FolderOf(WorldMapData data)
  {
    var path = AssetDatabase.GetAssetPath(data);
    return string.IsNullOrEmpty(path) ? DefaultFolder : Path.GetDirectoryName(path).Replace('\\', '/');
  }

  private static Bounds SceneBounds()
  {
    var has = false;
    var bounds = new Bounds();

    foreach (var terrain in Terrain.activeTerrains)
    {
      var b = terrain.terrainData.bounds;
      b.center += terrain.transform.position;
      if (has) bounds.Encapsulate(b); else bounds = b;
      has = true;
    }

    if (!has)
    {
      foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
      {
        if (r.transform.position.y < -1000f) continue; // escenarios de retratos, etc.
        if (has) bounds.Encapsulate(r.bounds); else bounds = r.bounds;
        has = true;
      }
    }

    return has ? bounds : new Bounds(Vector3.zero, new Vector3(100f, 30f, 100f));
  }

  // ---------- Hornear ----------

  public static void Bake(MapArea area)
  {
    var data = area.data != null ? area.data : (area.data = GetOrCreateData());
    EditorUtility.SetDirty(area);

    var bounds = area.Bounds;
    var aspect = bounds.size.x / Mathf.Max(0.01f, bounds.size.z);
    Size(data.resolution, aspect, out var w, out var h);
    Size(data.heightResolution, aspect, out var hw, out var hh);

    try
    {
      EditorUtility.DisplayProgressBar("Mapa", "Fotografiando el nivel...", 0.05f);
      var photo = RenderPhoto(data, bounds, w, h);

      var heights = MeasureHeights(data, bounds, hw, hh, out var hMin, out var hMax);

      EditorUtility.DisplayProgressBar("Mapa", "Guardando...", 0.7f);
      var folder = FolderOf(data);
      EnsureFolder(folder);
      File.WriteAllBytes($"{folder}/{PhotoName}", photo.EncodeToPNG());
      WriteHeights($"{folder}/{HeightName}", heights);

      Undo.RecordObject(data, "Hornear mapa");
      data.worldMin = new Vector2(bounds.min.x, bounds.min.z);
      data.worldSize = new Vector2(bounds.size.x, bounds.size.z);
      data.heightMin = hMin;
      data.heightMax = hMax;
      data.heightWidth = hw;
      data.heightHeight = hh;
      data.bakedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

      var photoPixels = photo.GetPixels32();
      Object.DestroyImmediate(photo);

      EditorUtility.DisplayProgressBar("Mapa", "Dibujando el pergamino...", 0.8f);
      WriteBase(data, folder, photoPixels, w, h, heights);
      AssetDatabase.ImportAsset($"{folder}/{HeightName}");
      ImportTexture($"{folder}/{PhotoName}", 2048);
    }
    finally
    {
      EditorUtility.ClearProgressBar();
    }

    Selection.activeObject = data;
    Debug.Log($"[Mapa] Mapa horneado ({w}x{h}) en {FolderOf(data)}/{BaseName}.");
  }

  public static void Restyle(WorldMapData data)
  {
    var folder = FolderOf(data);
    var photoPath = $"{folder}/{PhotoName}";
    var heightPath = $"{folder}/{HeightName}";

    if (!File.Exists(photoPath) || !File.Exists(heightPath))
    {
      EditorUtility.DisplayDialog("Mapa", "Faltan Mapa_Foto.png o Mapa_Altura.bytes: hornea el mapa de nuevo.", "OK");
      return;
    }

    try
    {
      EditorUtility.DisplayProgressBar("Mapa", "Dibujando el pergamino...", 0.3f);
      var photo = new Texture2D(2, 2, TextureFormat.RGBA32, false);
      photo.LoadImage(File.ReadAllBytes(photoPath));
      var pixels = photo.GetPixels32();
      var w = photo.width;
      var h = photo.height;
      Object.DestroyImmediate(photo);

      var heights = ReadHeights(heightPath, data.heightWidth * data.heightHeight);

      if (heights == null)
      {
        EditorUtility.DisplayDialog("Mapa", "Las alturas guardadas no coinciden: hornea el mapa de nuevo.", "OK");
        return;
      }

      Undo.RecordObject(data, "Reaplicar estilo");
      WriteBase(data, folder, pixels, w, h, heights);
    }
    finally
    {
      EditorUtility.ClearProgressBar();
    }
  }

  private static void Size(int longSide, float aspect, out int w, out int h)
  {
    if (aspect >= 1f)
    {
      w = longSide;
      h = Mathf.Max(8, Mathf.RoundToInt(longSide / aspect));
    }
    else
    {
      h = longSide;
      w = Mathf.Max(8, Mathf.RoundToInt(longSide * aspect));
    }
  }

  private static void WriteBase(WorldMapData data, string folder, Color32[] photo, int w, int h, float[] heights)
  {
    var styled = Stylize(data, photo, w, h, heights);
    var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
    tex.SetPixels32(styled);
    tex.Apply();
    var path = $"{folder}/{BaseName}";
    File.WriteAllBytes(path, tex.EncodeToPNG());
    Object.DestroyImmediate(tex);

    ImportTexture(path, 4096);
    data.baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    EditorUtility.SetDirty(data);
    AssetDatabase.SaveAssets();
  }

  private static void ImportTexture(string path, int maxSize)
  {
    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
    var importer = (TextureImporter)AssetImporter.GetAtPath(path);

    if (importer == null)
    {
      return;
    }

    importer.textureType = TextureImporterType.Default;
    importer.sRGBTexture = true;
    importer.alphaSource = TextureImporterAlphaSource.None;
    importer.npotScale = TextureImporterNPOTScale.None;
    importer.mipmapEnabled = true;
    importer.wrapMode = TextureWrapMode.Clamp;
    importer.filterMode = FilterMode.Trilinear;
    importer.maxTextureSize = maxSize;
    importer.textureCompression = TextureImporterCompression.CompressedHQ;
    importer.SaveAndReimport();
  }

  // ---------- Fotografia ----------

  private struct TerrainBackup
  {
    public Terrain terrain;
    public float detail, tree, billboard, pixelError, basemap;
  }

  private static Texture2D RenderPhoto(WorldMapData data, Bounds bounds, int w, int h)
  {
    var go = new GameObject("MapBakeCamera") { hideFlags = HideFlags.HideAndDontSave };
    var cam = go.AddComponent<Camera>();
    cam.enabled = false;
    cam.orthographic = true;
    cam.orthographicSize = bounds.size.z * 0.5f;
    cam.aspect = bounds.size.x / bounds.size.z;
    cam.clearFlags = CameraClearFlags.SolidColor;
    cam.backgroundColor = data.backgroundColor;
    cam.cullingMask = data.renderLayers;
    cam.nearClipPlane = 0.1f;
    cam.farClipPlane = bounds.size.y + 20f;
    // Mirando hacia abajo: arriba de la imagen = norte (+Z), derecha = este (+X).
    go.transform.SetPositionAndRotation(
      new Vector3(bounds.center.x, bounds.max.y + 10f, bounds.center.z),
      Quaternion.Euler(90f, 0f, 0f));

    var camData = cam.GetUniversalAdditionalCameraData();
    camData.renderPostProcessing = false;
    camData.renderShadows = data.shadows;
    camData.antialiasing = AntialiasingMode.None;

    // Ajustes temporales para que se vea todo con detalle maximo.
    var fog = RenderSettings.fog;
    RenderSettings.fog = false;
    var lodBias = QualitySettings.lodBias;
    QualitySettings.lodBias = 100f;

    var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
    var shadowDistance = urp != null ? urp.shadowDistance : 0f;

    if (urp != null && data.shadows)
    {
      urp.shadowDistance = cam.farClipPlane + Mathf.Max(bounds.size.x, bounds.size.z);
    }

    var backups = new List<TerrainBackup>();

    foreach (var t in Terrain.activeTerrains)
    {
      backups.Add(new TerrainBackup
      {
        terrain = t,
        detail = t.detailObjectDistance,
        tree = t.treeDistance,
        billboard = t.treeBillboardDistance,
        pixelError = t.heightmapPixelError,
        basemap = t.basemapDistance
      });

      t.detailObjectDistance = data.includeGrass ? 250f : 0f;
      t.treeDistance = 5000f;
      t.treeBillboardDistance = 5000f;
      t.heightmapPixelError = 1f;
      t.basemapDistance = 20000f;
    }

    // Supersampling x2 para bordes limpios.
    var rt = RenderTexture.GetTemporary(w * 2, h * 2, 24, RenderTextureFormat.ARGB32);
    cam.targetTexture = rt;

    try
    {
      var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };

      if (RenderPipeline.SupportsRenderRequest(cam, request))
      {
        RenderPipeline.SubmitRenderRequest(cam, request);
      }
      else
      {
        cam.Render();
      }

      var small = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
      Graphics.Blit(rt, small);

      var previous = RenderTexture.active;
      RenderTexture.active = small;
      var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
      tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
      tex.Apply();
      RenderTexture.active = previous;
      RenderTexture.ReleaseTemporary(small);
      return tex;
    }
    finally
    {
      cam.targetTexture = null;
      RenderTexture.ReleaseTemporary(rt);
      Object.DestroyImmediate(go);
      RenderSettings.fog = fog;
      QualitySettings.lodBias = lodBias;

      if (urp != null)
      {
        urp.shadowDistance = shadowDistance;
      }

      foreach (var b in backups)
      {
        if (b.terrain == null) continue;
        b.terrain.detailObjectDistance = b.detail;
        b.terrain.treeDistance = b.tree;
        b.terrain.treeBillboardDistance = b.billboard;
        b.terrain.heightmapPixelError = b.pixelError;
        b.terrain.basemapDistance = b.basemap;
      }
    }
  }

  // ---------- Relieve ----------

  private static float[] MeasureHeights(WorldMapData data, Bounds bounds, int w, int h, out float min, out float max)
  {
    Physics.SyncTransforms();
    var heights = new float[w * h];
    var top = bounds.max.y + 5f;
    var length = bounds.size.y + 10f;
    min = float.MaxValue;
    max = float.MinValue;

    for (var y = 0; y < h; y++)
    {
      if ((y & 15) == 0)
      {
        EditorUtility.DisplayProgressBar("Mapa", "Midiendo el relieve...", 0.1f + 0.6f * y / h);
      }

      var z = bounds.min.z + (y + 0.5f) / h * bounds.size.z;

      for (var x = 0; x < w; x++)
      {
        var wx = bounds.min.x + (x + 0.5f) / w * bounds.size.x;
        var value = bounds.min.y;

        if (Physics.Raycast(new Vector3(wx, top, z), Vector3.down, out var hit, length, data.heightLayers, QueryTriggerInteraction.Ignore))
        {
          value = hit.point.y;
        }

        heights[y * w + x] = value;
        if (value < min) min = value;
        if (value > max) max = value;
      }
    }

    if (min > max)
    {
      min = max = bounds.min.y;
    }

    return heights;
  }

  private static void WriteHeights(string path, float[] heights)
  {
    var bytes = new byte[heights.Length * 4];
    Buffer.BlockCopy(heights, 0, bytes, 0, bytes.Length);
    File.WriteAllBytes(path, bytes);
  }

  private static float[] ReadHeights(string path, int count)
  {
    var bytes = File.ReadAllBytes(path);

    if (count <= 0 || bytes.Length != count * 4)
    {
      return null;
    }

    var heights = new float[count];
    Buffer.BlockCopy(bytes, 0, heights, 0, bytes.Length);
    return heights;
  }

  // ---------- Estilo pergamino ----------

  private static Color32[] Stylize(WorldMapData d, Color32[] photo, int w, int h, float[] heights)
  {
    var hw = d.heightWidth;
    var hh = d.heightHeight;
    var cellX = d.worldSize.x / Mathf.Max(1, hw);
    var cellZ = d.worldSize.y / Mathf.Max(1, hh);

    // --- Mapas derivados del relieve (a la resolucion de alturas) ---
    var shade = new float[hw * hh];
    var edge = new float[hw * hh];
    var contour = new float[hw * hh];
    var light = new Vector3(-1f, 1.6f, 1f).normalized; // desde el noroeste
    var flat = light.y;

    float H(int x, int y) => heights[Mathf.Clamp(y, 0, hh - 1) * hw + Mathf.Clamp(x, 0, hw - 1)];

    for (var y = 0; y < hh; y++)
    {
      for (var x = 0; x < hw; x++)
      {
        var c = H(x, y);
        var dx = (H(x + 1, y) - H(x - 1, y)) / (2f * cellX);
        var dz = (H(x, y + 1) - H(x, y - 1)) / (2f * cellZ);
        var n = new Vector3(-dx, 1f, -dz).normalized;
        shade[y * hw + x] = Vector3.Dot(n, light) - flat;

        var diff = 0f;
        diff = Mathf.Max(diff, Mathf.Abs(H(x + 1, y) - c));
        diff = Mathf.Max(diff, Mathf.Abs(H(x - 1, y) - c));
        diff = Mathf.Max(diff, Mathf.Abs(H(x, y + 1) - c));
        diff = Mathf.Max(diff, Mathf.Abs(H(x, y - 1) - c));
        edge[y * hw + x] = Mathf.Clamp01((diff - d.outlineThreshold) / d.outlineThreshold);

        if (d.contourInterval > 0f)
        {
          var band = Mathf.Floor(c / d.contourInterval);
          var other = Mathf.Max(Mathf.Floor(H(x + 1, y) / d.contourInterval), Mathf.Floor(H(x, y + 1) / d.contourInterval));
          contour[y * hw + x] = other != band && diff < d.outlineThreshold ? 1f : 0f;
        }
      }
    }

    for (var i = 0; i < d.outlineWidth; i++)
    {
      edge = Dilate(edge, hw, hh);
    }

    // --- Color ---
    var r = new float[w * h];
    var g = new float[w * h];
    var b = new float[w * h];

    for (var i = 0; i < photo.Length; i++)
    {
      r[i] = photo[i].r / 255f;
      g[i] = photo[i].g / 255f;
      b[i] = photo[i].b / 255f;
    }

    if (d.blurRadius > 0)
    {
      r = Blur(r, w, h, d.blurRadius);
      g = Blur(g, w, h, d.blurRadius);
      b = Blur(b, w, h, d.blurRadius);
    }

    var result = new Color32[w * h];
    var paper = (Vector4)d.paperColor;
    var ink = (Vector4)d.inkColor;
    var burn = (Vector4)d.edgeBurnColor;

    for (var y = 0; y < h; y++)
    {
      var v = (y + 0.5f) / h;

      for (var x = 0; x < w; x++)
      {
        var u = (x + 0.5f) / w;
        var i = y * w + x;
        var col = new Vector4(r[i], g[i], b[i], 1f);

        // Saturacion y brillo
        var gray = col.x * 0.299f + col.y * 0.587f + col.z * 0.114f;
        col = Vector4.Lerp(new Vector4(gray, gray, gray, 1f), col, d.saturation) * d.brightness;

        // Niveles de color (aspecto ilustrado)
        if (d.posterizeLevels > 0)
        {
          var levels = d.posterizeLevels;
          col.x = Mathf.Round(Mathf.Clamp01(col.x) * levels) / levels;
          col.y = Mathf.Round(Mathf.Clamp01(col.y) * levels) / levels;
          col.z = Mathf.Round(Mathf.Clamp01(col.z) * levels) / levels;
        }

        // Relieve
        var s = Sample(shade, hw, hh, u, v);
        col *= 1f + s * d.hillshade * 1.5f;

        // Papel
        col = Vector4.Lerp(col, Vector4.Scale(col, paper) * 1.15f, d.paperAmount);
        var noise = Fbm(x, y) * d.paperNoise;
        col *= 1f + noise;

        // Tinta: curvas de nivel y contornos
        col = Vector4.Lerp(col, ink, Sample(contour, hw, hh, u, v) * d.contourStrength);
        col = Vector4.Lerp(col, ink, Sample(edge, hw, hh, u, v) * d.outlineStrength);

        // Bordes envejecidos
        if (d.edgeBurn > 0f)
        {
          var border = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
          var amount = 1f - Mathf.Clamp01((border + Fbm(x + 917, y + 331) * 0.03f) / 0.07f);
          col = Vector4.Lerp(col, Vector4.Scale(col, burn), amount * amount * d.edgeBurn * 1.6f);
        }

        result[i] = new Color32(
          (byte)(Mathf.Clamp01(col.x) * 255f),
          (byte)(Mathf.Clamp01(col.y) * 255f),
          (byte)(Mathf.Clamp01(col.z) * 255f),
          255);
      }

      if ((y & 63) == 0)
      {
        EditorUtility.DisplayProgressBar("Mapa", "Dibujando el pergamino...", 0.8f + 0.2f * y / h);
      }
    }

    return result;
  }

  private static float Sample(float[] map, int w, int h, float u, float v)
  {
    var fx = u * w - 0.5f;
    var fy = v * h - 0.5f;
    var x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, w - 1);
    var y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, h - 1);
    var x1 = Mathf.Min(x0 + 1, w - 1);
    var y1 = Mathf.Min(y0 + 1, h - 1);
    var tx = Mathf.Clamp01(fx - x0);
    var ty = Mathf.Clamp01(fy - y0);
    var a = Mathf.Lerp(map[y0 * w + x0], map[y0 * w + x1], tx);
    var b = Mathf.Lerp(map[y1 * w + x0], map[y1 * w + x1], tx);
    return Mathf.Lerp(a, b, ty);
  }

  private static float[] Dilate(float[] src, int w, int h)
  {
    var dst = new float[src.Length];

    for (var y = 0; y < h; y++)
    {
      for (var x = 0; x < w; x++)
      {
        var m = src[y * w + x];
        if (x > 0) m = Mathf.Max(m, src[y * w + x - 1]);
        if (x < w - 1) m = Mathf.Max(m, src[y * w + x + 1]);
        if (y > 0) m = Mathf.Max(m, src[(y - 1) * w + x]);
        if (y < h - 1) m = Mathf.Max(m, src[(y + 1) * w + x]);
        dst[y * w + x] = m;
      }
    }

    return dst;
  }

  private static float[] Blur(float[] src, int w, int h, int radius)
  {
    var tmp = new float[src.Length];
    var dst = new float[src.Length];
    var count = radius * 2 + 1;

    for (var y = 0; y < h; y++)
    {
      for (var x = 0; x < w; x++)
      {
        var sum = 0f;
        for (var k = -radius; k <= radius; k++) sum += src[y * w + Mathf.Clamp(x + k, 0, w - 1)];
        tmp[y * w + x] = sum / count;
      }
    }

    for (var y = 0; y < h; y++)
    {
      for (var x = 0; x < w; x++)
      {
        var sum = 0f;
        for (var k = -radius; k <= radius; k++) sum += tmp[Mathf.Clamp(y + k, 0, h - 1) * w + x];
        dst[y * w + x] = sum / count;
      }
    }

    return dst;
  }

  // Ruido de papel (fractal de ruido de valor), -1..1 aprox.
  private static float Fbm(int x, int y)
  {
    return ValueNoise(x / 96f, y / 96f) * 0.5f + ValueNoise(x / 24f, y / 24f) * 0.3f + ValueNoise(x / 5f, y / 5f) * 0.2f;
  }

  private static float ValueNoise(float x, float y)
  {
    var ix = Mathf.FloorToInt(x);
    var iy = Mathf.FloorToInt(y);
    var fx = x - ix;
    var fy = y - iy;
    fx = fx * fx * (3f - 2f * fx);
    fy = fy * fy * (3f - 2f * fy);
    var a = Mathf.Lerp(Hash(ix, iy), Hash(ix + 1, iy), fx);
    var b = Mathf.Lerp(Hash(ix, iy + 1), Hash(ix + 1, iy + 1), fx);
    return Mathf.Lerp(a, b, fy) * 2f - 1f;
  }

  private static float Hash(int x, int y)
  {
    unchecked
    {
      var n = (uint)(x * 374761393 + y * 668265263);
      n = (n ^ (n >> 13)) * 1274126177u;
      return ((n ^ (n >> 16)) & 0xFFFFFF) / 16777215f;
    }
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
}

// ---------- Botones en el Inspector ----------

[CustomEditor(typeof(MapArea))]
public class MapAreaEditor : Editor
{
  public override void OnInspectorGUI()
  {
    DrawDefaultInspector();
    EditorGUILayout.Space();
    EditorGUILayout.HelpBox("Arriba del mapa = norte (+Z). Si ya pintaste una capa a mano, no muevas ni cambies el tamaño del area.", MessageType.Info);

    if (GUILayout.Button("Ajustar al terreno"))
    {
      WorldMapBaker.CreateArea();
    }

    if (GUILayout.Button("Hornear mapa", GUILayout.Height(30)))
    {
      WorldMapBaker.Bake((MapArea)target);
    }
  }
}

[CustomEditor(typeof(WorldMapData))]
public class WorldMapDataEditor : Editor
{
  public override void OnInspectorGUI()
  {
    var data = (WorldMapData)target;

    if (data.IsBaked)
    {
      EditorGUILayout.HelpBox($"Horneado: {data.bakedAt}   ({data.baseMap.width}x{data.baseMap.height})", MessageType.None);
      var rect = GUILayoutUtility.GetAspectRect(data.Aspect, GUILayout.MaxHeight(260));
      EditorGUI.DrawPreviewTexture(rect, data.baseMap, null, ScaleMode.ScaleToFit);

      if (data.overlay != null)
      {
        GUI.DrawTexture(rect, data.overlay, ScaleMode.ScaleToFit, true);
      }
    }
    else
    {
      EditorGUILayout.HelpBox("Aun no hay mapa. Usa Tools > Mapa > Hornear mapa.", MessageType.Warning);
    }

    EditorGUILayout.Space();
    DrawDefaultInspector();
    EditorGUILayout.Space();

    using (new EditorGUI.DisabledScope(!data.IsBaked))
    {
      if (GUILayout.Button("Reaplicar estilo (rapido)", GUILayout.Height(30)))
      {
        WorldMapBaker.Restyle(data);
      }
    }

    if (GUILayout.Button("Hornear de nuevo (fotografiar nivel)"))
    {
      var area = Object.FindAnyObjectByType<MapArea>();

      if (area == null)
      {
        EditorUtility.DisplayDialog("Mapa", "Abre la escena del nivel (con su MapArea) para hornear.", "OK");
      }
      else
      {
        WorldMapBaker.Bake(area);
      }
    }
  }
}
