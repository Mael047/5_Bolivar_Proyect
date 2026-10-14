// TerrainTreeColliders.cs
// Tools > Terreno > Agregar colliders a los arboles del Terrain
// Los arboles pintados con la herramienta Terrain solo chocan si:
//   1. su prefab tiene un Capsule (o Box) Collider - los Mesh Collider NO
//      funcionan en arboles de Terrain;
//   2. el TerrainCollider tiene activado "Enable Tree Colliders".
// Esta herramienta, para cada Terrain de la escena abierta:
//   - añade a cada prefab de arbol sin collider un Capsule Collider del grosor
//     aproximado del tronco (y quita los Mesh Collider, que no sirven ahi);
//   - activa "Enable Tree Colliders" y refresca el Terrain.
// Despues puedes ajustar el radio/altura de la capsula en cada prefab.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TerrainTreeColliders
{
  [MenuItem("Tools/Terreno/Agregar colliders a los arboles del Terrain")]
  private static void Run()
  {
    var terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None);

    if (terrains.Length == 0)
    {
      EditorUtility.DisplayDialog("Arboles", "No hay ningun Terrain en la escena abierta.", "OK");
      return;
    }

    var prefabs = new HashSet<GameObject>();

    foreach (var terrain in terrains)
    {
      if (terrain.terrainData == null) continue;

      foreach (var proto in terrain.terrainData.treePrototypes)
      {
        if (proto.prefab != null) prefabs.Add(proto.prefab);
      }
    }

    var fixedNames = new List<string>();
    var okNames = new List<string>();

    foreach (var prefab in prefabs)
    {
      var path = AssetDatabase.GetAssetPath(prefab);

      if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
      {
        // Modelo importado (.fbx) usado directamente: no se puede editar como prefab.
        okNames.Add($"{prefab.name} (es un modelo, no un prefab: crea un prefab y ponle un Capsule Collider)");
        continue;
      }

      var root = PrefabUtility.LoadPrefabContents(path);

      try
      {
        var meshColliders = root.GetComponentsInChildren<MeshCollider>(true);
        var hasUsable = false;

        foreach (var c in root.GetComponentsInChildren<Collider>(true))
        {
          if (!(c is MeshCollider)) hasUsable = true;
        }

        if (hasUsable && meshColliders.Length == 0)
        {
          okNames.Add($"{prefab.name} (ya tenia collider)");
          continue;
        }

        foreach (var mc in meshColliders)
        {
          Object.DestroyImmediate(mc);
        }

        if (!hasUsable)
        {
          AddTrunkCapsule(root);
        }

        PrefabUtility.SaveAsPrefabAsset(root, path);
        fixedNames.Add(prefab.name);
      }
      finally
      {
        PrefabUtility.UnloadPrefabContents(root);
      }
    }

    // Activar colliders de arboles y refrescar cada Terrain.
    foreach (var terrain in terrains)
    {
      var collider = terrain.GetComponent<TerrainCollider>();

      if (collider != null)
      {
        var so = new SerializedObject(collider);
        var prop = so.FindProperty("m_EnableTreeColliders");

        if (prop != null)
        {
          prop.boolValue = true;
          so.ApplyModifiedProperties();
        }
      }

      if (terrain.terrainData != null)
      {
        terrain.terrainData.RefreshPrototypes();
      }

      terrain.Flush();
      EditorUtility.SetDirty(terrain);
    }

    UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();

    EditorUtility.DisplayDialog("Arboles",
      (fixedNames.Count > 0 ? "Collider agregado a:\n- " + string.Join("\n- ", fixedNames) + "\n\n" : "") +
      (okNames.Count > 0 ? "Sin cambios:\n- " + string.Join("\n- ", okNames) + "\n\n" : "") +
      $"'Enable Tree Colliders' activado en {terrains.Length} Terrain(s). Guarda la escena.\n\n" +
      "Si el collider queda muy grueso o delgado, ajusta el Radius del Capsule Collider en el prefab del arbol.",
      "OK");
  }

  /// <summary>Capsula vertical del alto del arbol y del grosor aproximado del tronco.</summary>
  private static void AddTrunkCapsule(GameObject root)
  {
    var renderers = root.GetComponentsInChildren<Renderer>(true);
    var bounds = new Bounds(root.transform.position, Vector3.one);
    var first = true;

    foreach (var r in renderers)
    {
      if (first) { bounds = r.bounds; first = false; }
      else bounds.Encapsulate(r.bounds);
    }

    // Al prefab raiz (en el origen): de mundo a local del raiz.
    var scale = root.transform.lossyScale;
    var sx = Mathf.Max(0.0001f, Mathf.Abs(scale.x));
    var sy = Mathf.Max(0.0001f, Mathf.Abs(scale.y));

    var capsule = root.AddComponent<CapsuleCollider>();
    capsule.direction = 1; // eje Y

    // El tronco suele ser ~12 % del ancho de la copa.
    var crown = Mathf.Min(bounds.size.x, bounds.size.z);
    var radius = Mathf.Clamp(crown * 0.12f, 0.15f, 1.2f);
    var height = Mathf.Max(bounds.size.y, radius * 2f);

    capsule.radius = radius / sx;
    capsule.height = height / sy;
    var localBottom = root.transform.InverseTransformPoint(new Vector3(root.transform.position.x, bounds.min.y, root.transform.position.z));
    capsule.center = new Vector3(0f, localBottom.y + capsule.height * 0.5f, 0f);
  }
}
