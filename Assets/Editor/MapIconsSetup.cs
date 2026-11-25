// MapIconsSetup.cs
// Tools > Mapa > Aplicar iconos nuevos de UI-Mapa
// Pone los iconos de Assets/UI/Mapa en el minimapa (MinimapUI) y en el mapa
// del menu de pausa (PauseMenuUI > WorldMapPage):
//   Punto de interes, Puerta Cerrada, Puerta que se puede romper, Objetivos,
//   Letreros y llave.
// Ademas:
//   - Door1 (puerta que solo rompe la espada) -> categoria "Puerta rompible".
//   - LockedDoor1 (puerta con llave) -> marcador "Puerta cerrada" (se oculta
//     al abrirla).
//   - Llaves (Key (1), KeyFinal) -> marcador "Llave" (desaparece al recogerla).
// Los iconos se ven con sus colores (color blanco). Tamaños, colores y textos
// de la leyenda se pueden cambiar despues en cada prefab.

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class MapIconsSetup
{
  private const string Folder = "Assets/UI/Mapa/";
  private const string MinimapPath = "Assets/UI/Minimap/MinimapUI.prefab";
  private const string PausePath = "Assets/UI/PauseMenu/PauseMenuUI.prefab";

  private struct Def
  {
    public MapMarkerCategory category;
    public string file;          // null = sin icono (punto de color)
    public string label;
    public Color color;
    public float miniSize;
    public float mapSize;
    public bool clamp;
    public bool legend;
  }

  private static readonly Def[] Defs =
  {
    new Def { category = MapMarkerCategory.PuntoDeInteres, file = "Punto de interes.PNG", label = "Puntos de interés", color = Color.white, miniSize = 20f, mapSize = 26f, legend = true },
    new Def { category = MapMarkerCategory.Cofre, file = null, label = "Cofres", color = new Color(0.95f, 0.7f, 0.3f), miniSize = 16f, mapSize = 22f, legend = false },
    new Def { category = MapMarkerCategory.Puerta, file = "Puerta Cerrada.PNG", label = "Puertas cerradas", color = Color.white, miniSize = 26f, mapSize = 34f, legend = true },
    new Def { category = MapMarkerCategory.Objetivo, file = "Objetivos.PNG", label = "Objetivos", color = Color.white, miniSize = 30f, mapSize = 38f, clamp = true, legend = true },
    new Def { category = MapMarkerCategory.Enemigo, file = null, label = "Enemigos", color = new Color(0.9f, 0.2f, 0.25f), miniSize = 14f, mapSize = 18f, legend = false },
    new Def { category = MapMarkerCategory.Otro, file = null, label = "Otros", color = new Color(0.8f, 0.8f, 0.8f), miniSize = 14f, mapSize = 20f, legend = false },
    new Def { category = MapMarkerCategory.Letrero, file = "Letreros.PNG", label = "Letreros", color = Color.white, miniSize = 24f, mapSize = 30f, legend = true },
    new Def { category = MapMarkerCategory.PuertaRompible, file = "Puerta que se puede romper.PNG", label = "Puertas rompibles", color = Color.white, miniSize = 26f, mapSize = 34f, legend = true },
    new Def { category = MapMarkerCategory.Llave, file = "llave.png", label = "Llaves", color = Color.white, miniSize = 22f, mapSize = 28f, legend = true },
  };

  [MenuItem("Tools/Mapa/Aplicar iconos nuevos de UI-Mapa")]
  private static void Run()
  {
    if (EditorApplication.isPlaying)
    {
      EditorUtility.DisplayDialog("Mapa", "Sal del modo Play primero.", "OK");
      return;
    }

    var log = new System.Text.StringBuilder();
    var sprites = new Dictionary<MapMarkerCategory, Sprite>();

    foreach (var d in Defs)
    {
      if (d.file == null) continue;
      var s = MakeSprite(Folder + d.file);
      if (s != null) sprites[d.category] = s;
      else log.AppendLine("Falta " + Folder + d.file);
    }

    // Minimapa
    EditPrefab(MinimapPath, root =>
    {
      var mini = root.GetComponentInChildren<Minimap>(true);
      if (mini == null) return "MinimapUI: no tiene Minimap";
      var so = new SerializedObject(mini);
      var arr = so.FindProperty("_categoryStyles");
      arr.arraySize = Defs.Length;

      for (var i = 0; i < Defs.Length; i++)
      {
        var d = Defs[i];
        var e = arr.GetArrayElementAtIndex(i);
        e.FindPropertyRelative("category").intValue = (int)d.category;
        e.FindPropertyRelative("icon").objectReferenceValue = sprites.TryGetValue(d.category, out var sp) ? sp : null;
        e.FindPropertyRelative("color").colorValue = d.color;
        e.FindPropertyRelative("size").floatValue = d.miniSize;
        e.FindPropertyRelative("clampToEdge").boolValue = d.clamp;
      }

      so.ApplyModifiedPropertiesWithoutUndo();
      return "Minimapa: " + Defs.Length + " categorias";
    }, log);

    // Mapa grande (menu de pausa)
    EditPrefab(PausePath, root =>
    {
      var page = root.GetComponentInChildren<Menus.WorldMapPage>(true);
      if (page == null) return "PauseMenuUI: no tiene WorldMapPage";
      var so = new SerializedObject(page);
      var arr = so.FindProperty("_categoryStyles");
      arr.arraySize = Defs.Length;

      for (var i = 0; i < Defs.Length; i++)
      {
        var d = Defs[i];
        var e = arr.GetArrayElementAtIndex(i);
        e.FindPropertyRelative("category").intValue = (int)d.category;
        e.FindPropertyRelative("label").stringValue = d.label;
        e.FindPropertyRelative("icon").objectReferenceValue = sprites.TryGetValue(d.category, out var sp) ? sp : null;
        e.FindPropertyRelative("color").colorValue = d.color;
        e.FindPropertyRelative("size").floatValue = d.mapSize;
        e.FindPropertyRelative("showInLegend").boolValue = d.legend;
      }

      so.ApplyModifiedPropertiesWithoutUndo();
      return "Mapa de pausa: " + Defs.Length + " categorias (con leyenda)";
    }, log);

    // Puertas y llaves
    SetMarker("Assets/Prefabs/Doors/Door1.prefab", MapMarkerCategory.PuertaRompible, log);
    SetMarker("Assets/Prefabs/Doors/LockedDoor1.prefab", MapMarkerCategory.Puerta, log);
    SetMarker("Assets/Prefabs/Objects/Key (1).prefab", MapMarkerCategory.Llave, log);
    SetMarker("Assets/Prefabs/Objects/KeyFinal.prefab", MapMarkerCategory.Llave, log);

    AssetDatabase.SaveAssets();
    EditorUtility.DisplayDialog("Mapa", "Iconos aplicados.\n\n" + log, "OK");
  }

  private static void EditPrefab(string path, System.Func<GameObject, string> edit, System.Text.StringBuilder log)
  {
    if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
    {
      log.AppendLine("No encontre " + path);
      return;
    }

    var root = PrefabUtility.LoadPrefabContents(path);

    try
    {
      log.AppendLine(edit(root));
      PrefabUtility.SaveAsPrefabAsset(root, path);
    }
    finally
    {
      PrefabUtility.UnloadPrefabContents(root);
    }
  }

  private static void SetMarker(string path, MapMarkerCategory category, System.Text.StringBuilder log)
  {
    EditPrefab(path, root =>
    {
      var marker = root.GetComponent<MapMarker>();
      if (marker == null) marker = root.AddComponent<MapMarker>();
      var so = new SerializedObject(marker);
      so.FindProperty("_category").intValue = (int)category;
      so.ApplyModifiedPropertiesWithoutUndo();
      return System.IO.Path.GetFileNameWithoutExtension(path) + " -> " + ObjectNames.NicifyVariableName(category.ToString());
    }, log);
  }

  private static Sprite MakeSprite(string path)
  {
    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
    if (importer == null) return null;

    if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single || importer.mipmapEnabled || importer.maxTextureSize > 256)
    {
      importer.textureType = TextureImporterType.Sprite;
      importer.spriteImportMode = SpriteImportMode.Single;
      importer.mipmapEnabled = false;
      importer.alphaIsTransparency = true;
      importer.maxTextureSize = 256;
      importer.SaveAndReimport();
    }

    return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
  }
}
