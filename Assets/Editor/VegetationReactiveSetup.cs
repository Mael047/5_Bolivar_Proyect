// VegetationReactiveSetup.cs
// Tools > Vegetacion > Hacer reactivas las flores del Terrain
//
// Para cada detalle de tipo malla (Detail Mesh) de los Terrains de la escena:
//   1. Crea un material Custom/VegetationReactive copiando textura, color y
//      recorte del material original, con 'Plant Height' = alto del modelo.
//      (Los originales no se tocan: suelen estar compartidos con otros modelos.)
//   2. Crea una copia del prefab con ese material (Assets/Prefabs/Vegetation/Reactive).
//   3. Hace que el detalle use la copia, con GPU Instancing (necesario para
//      que el Terrain use el material del prefab).
// Ademas añade VegetationInteractor + FlowerPetals al Player y un
// VegetationInteractor (objeto sostenido) al HandSystem.
// Los detalles de tipo textura (Grass Texture) no se pueden hacer reactivos.

using System.Collections.Generic;
using System.IO;
using NuiGrab;
using UnityEditor;
using UnityEngine;

public static class VegetationReactiveSetup
{
  private const string MaterialFolder = "Assets/Materials/Vegetation/Reactive";
  private const string PrefabFolder = "Assets/Prefabs/Vegetation/Reactive";
  private const string ShaderName = "Custom/VegetationReactive";

  [MenuItem("Tools/Vegetacion/Hacer reactivas las flores del Terrain")]
  private static void Run()
  {
    var shader = Shader.Find(ShaderName);

    if (shader == null)
    {
      EditorUtility.DisplayDialog("Vegetacion", $"No encontre el shader '{ShaderName}'. Revisa la consola por errores de shader.", "OK");
      return;
    }

    var terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None);

    if (terrains.Length == 0)
    {
      EditorUtility.DisplayDialog("Vegetacion", "No hay ningun Terrain en la escena abierta.", "OK");
      return;
    }

    EnsureFolder(MaterialFolder);
    EnsureFolder(PrefabFolder);

    var converted = new Dictionary<GameObject, GameObject>(); // original -> reactivo
    var done = new List<string>();
    var skipped = new List<string>();
    var datas = new HashSet<TerrainData>();

    foreach (var terrain in terrains)
    {
      if (terrain.terrainData != null) datas.Add(terrain.terrainData);
    }

    foreach (var data in datas)
    {
      var protos = data.detailPrototypes;

      for (var i = 0; i < protos.Length; i++)
      {
        var proto = protos[i];

        if (!proto.usePrototypeMesh || proto.prototype == null)
        {
          var texName = proto.prototypeTexture != null ? proto.prototypeTexture.name : $"detalle {i}";
          if (!skipped.Contains(texName)) skipped.Add($"{texName} (Grass Texture: no admite shader propio)");
          continue;
        }

        var source = proto.prototype;

        if (IsReactive(source))
        {
          if (!done.Contains(source.name)) done.Add($"{source.name} (ya era reactiva)");
        }
        else
        {
          if (!converted.TryGetValue(source, out var reactive))
          {
            reactive = MakeReactivePrefab(source, shader);
            converted[source] = reactive;
            done.Add(source.name);
          }

          proto.prototype = reactive;
        }

        proto.useInstancing = true;
        proto.renderMode = DetailRenderMode.VertexLit;
        protos[i] = proto;
      }

      data.detailPrototypes = protos;
      data.RefreshPrototypes();
      EditorUtility.SetDirty(data);
    }

    foreach (var terrain in terrains) terrain.Flush();

    var extras = SetupInteractors();
    AssetDatabase.SaveAssets();
    UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();

    EditorUtility.DisplayDialog("Vegetacion",
      (done.Count > 0 ? "Reactivas:\n- " + string.Join("\n- ", done) + "\n\n" : "No habia detalles de malla.\n\n") +
      (skipped.Count > 0 ? "Sin cambios:\n- " + string.Join("\n- ", skipped) + "\n\n" : "") +
      extras + "\n\nGuarda la escena.",
      "OK");
  }

  private static bool IsReactive(GameObject prefab)
  {
    foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
    {
      foreach (var m in r.sharedMaterials)
      {
        if (m != null && m.shader != null && m.shader.name == ShaderName) return true;
      }
    }

    return false;
  }

  private static GameObject MakeReactivePrefab(GameObject source, Shader shader)
  {
    var sourcePath = AssetDatabase.GetAssetPath(source);
    var targetPath = AssetDatabase.GenerateUniqueAssetPath($"{PrefabFolder}/{source.name}_Reactive.prefab");

    // Un modelo (.fbx) se instancia y se guarda como prefab nuevo; un prefab se copia.
    var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
    PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
    instance.transform.position = Vector3.zero;
    instance.transform.rotation = Quaternion.identity;

    var height = PlantHeight(instance);
    var materialCache = new Dictionary<Material, Material>();

    foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
    {
      var mats = renderer.sharedMaterials;

      for (var m = 0; m < mats.Length; m++)
      {
        if (mats[m] == null) continue;

        if (!materialCache.TryGetValue(mats[m], out var reactive))
        {
          reactive = MakeReactiveMaterial(mats[m], shader, source.name, height);
          materialCache[mats[m]] = reactive;
        }

        mats[m] = reactive;
      }

      renderer.sharedMaterials = mats;
    }

    // Los detalles del Terrain no usan colliders: fuera.
    foreach (var c in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

    var prefab = PrefabUtility.SaveAsPrefabAsset(instance, targetPath);
    Object.DestroyImmediate(instance);
    return prefab;
  }

  private static Material MakeReactiveMaterial(Material original, Shader shader, string plantName, float height)
  {
    var path = AssetDatabase.GenerateUniqueAssetPath($"{MaterialFolder}/{plantName}_{original.name}_Reactive.mat");
    var mat = new Material(shader);

    var tex = original.HasProperty("_BaseMap") ? original.GetTexture("_BaseMap")
      : original.HasProperty("_MainTex") ? original.GetTexture("_MainTex") : null;
    mat.SetTexture("_BaseMap", tex);

    if (original.HasProperty("_BaseMap"))
    {
      mat.SetTextureScale("_BaseMap", original.GetTextureScale("_BaseMap"));
      mat.SetTextureOffset("_BaseMap", original.GetTextureOffset("_BaseMap"));
    }

    var color = original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor")
      : original.HasProperty("_Color") ? original.GetColor("_Color") : Color.white;
    mat.SetColor("_BaseColor", color);

    var clip = original.HasProperty("_AlphaClip") && original.GetFloat("_AlphaClip") > 0.5f;
    mat.SetFloat("_AlphaClip", clip ? 1f : 0f);
    if (clip) mat.EnableKeyword("_ALPHATEST_ON"); else mat.DisableKeyword("_ALPHATEST_ON");
    if (original.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", original.GetFloat("_Cutoff"));

    mat.SetFloat("_PlantHeight", height);
    mat.enableInstancing = true;

    AssetDatabase.CreateAsset(mat, path);
    return mat;
  }

  /// <summary>Alto del modelo (m) desde su origen.</summary>
  private static float PlantHeight(GameObject go)
  {
    var max = 0f;

    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
    {
      max = Mathf.Max(max, r.bounds.max.y - go.transform.position.y);
    }

    return Mathf.Max(0.05f, max);
  }

  // ---------- Interactores ----------

  private static string SetupInteractors()
  {
    var notes = new List<string>();
    var player = FindPrefabWith<Player>("Player");

    if (player != null)
    {
      EditPrefab(player, root =>
      {
        var target = root.GetComponentInChildren<Player>(true).gameObject;
        if (target.GetComponent<VegetationInteractor>() == null) target.AddComponent<VegetationInteractor>();
        if (target.GetComponent<FlowerPetals>() == null) target.AddComponent<FlowerPetals>();
      });
      notes.Add("Player: VegetationInteractor + FlowerPetals.");
    }

    var hand = FindPrefabWith<HandGrabManager>("HandSystem");

    if (hand != null)
    {
      EditPrefab(hand, root =>
      {
        if (root.GetComponent<VegetationInteractor>() != null) return;
        var interactor = root.AddComponent<VegetationInteractor>();
        var so = new SerializedObject(interactor);
        so.FindProperty("_source").enumValueIndex = (int)VegetationInteractor.Source.ObjetoSostenido;
        so.FindProperty("_radius").floatValue = 0.6f;
        so.ApplyModifiedPropertiesWithoutUndo();
      });
      notes.Add("HandSystem: VegetationInteractor (objeto sostenido).");
    }

    return string.Join("\n", notes);
  }

  private static GameObject FindPrefabWith<T>(string preferredName) where T : Component
  {
    GameObject fallback = null;

    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
    {
      var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
      if (go == null || go.GetComponentInChildren<T>(true) == null) continue;
      if (go.name == preferredName) return go;
      if (fallback == null) fallback = go;
    }

    return fallback;
  }

  private static void EditPrefab(GameObject prefab, System.Action<GameObject> edit)
  {
    var path = AssetDatabase.GetAssetPath(prefab);
    var root = PrefabUtility.LoadPrefabContents(path);

    try
    {
      edit(root);
      PrefabUtility.SaveAsPrefabAsset(root, path);
    }
    finally
    {
      PrefabUtility.UnloadPrefabContents(root);
    }
  }

  private static void EnsureFolder(string folder)
  {
    if (AssetDatabase.IsValidFolder(folder)) return;
    var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
    EnsureFolder(parent);
    AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
  }
}
