// JardinCasaSetup.cs
// Prepara la casa del jardin (casacasa_unity) en Jardin_Principal:
//   1. Colliders: quita los Mesh Collider vacios (sin malla, que no chocan con
//      nada) y pone Mesh Collider con la forma real en cada pieza de la casa
//      (paredes, escaleras, columnas). El techo se salta.
//   2. Escaleras: añade StepClimber al prefab del Player (y al de la escena),
//      asi la nutria sube escalones en todas las escenas.
//   3. Entrada: crea "Entrada_Casa" (SceneDoor) en la puerta, delante de las
//      escaleras; al entrar se carga Nivel_CASA. Si ya existe no se mueve
//      (se puede colocar a mano).
//
// Menu: Tools > Jardin > Casa: colliders, escaleras y entrada

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class JardinCasaSetup
{
  private const string HouseName = "casacasa_unity";
  private const string EntranceName = "Entrada_Casa";
  private const string TargetScene = "Nivel_CASA";

  [MenuItem("Tools/Jardin/Casa: colliders, escaleras y entrada")]
  private static void Run()
  {
    if (EditorApplication.isPlaying)
    {
      EditorUtility.DisplayDialog("Casa del jardin", "Sal del modo Play primero.", "OK");
      return;
    }

    var scene = EditorSceneManager.GetActiveScene();
    var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
    var house = all.FirstOrDefault(t => t.name == HouseName);

    if (house == null)
    {
      EditorUtility.DisplayDialog("Casa del jardin", $"No encontre '{HouseName}' en esta escena. Abre Jardin_Principal.", "OK");
      return;
    }

    // ---------- 1. Colliders ----------
    var emptyFixed = 0;
    var added = 0;

    // Mesh Collider sin malla en toda la escena (no chocan con nada).
    foreach (var mc in all.SelectMany(t => t.GetComponents<MeshCollider>()).ToList())
    {
      if (mc == null || mc.sharedMesh != null)
      {
        continue;
      }

      var filter = mc.GetComponent<MeshFilter>();

      if (filter != null && filter.sharedMesh != null)
      {
        Undo.RecordObject(mc, "Colliders casa");
        mc.sharedMesh = filter.sharedMesh;
      }
      else
      {
        var go = mc.gameObject;
        Undo.DestroyObjectImmediate(mc);
        added += AddMeshColliders(go.transform);
      }

      emptyFixed++;
    }

    // Colliders 2D (no sirven en 3D) dentro de la casa.
    foreach (var c2d in house.GetComponentsInChildren<Collider2D>(true).ToList())
    {
      Undo.DestroyObjectImmediate(c2d);
      emptyFixed++;
    }

    added += AddMeshColliders(house);
    PlayerSettings.bakeCollisionMeshes = true;

    // ---------- 2. Escaleras ----------
    var climberInfo = AddStepClimber(scene);

    // ---------- 3. Entrada ----------
    var entranceInfo = SetupEntrance(scene, house, all);
    AddSceneToBuild(TargetScene);

    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);

    EditorUtility.DisplayDialog("Casa del jardin",
      "Colliders añadidos: " + added + "\n" +
      "Colliders vacios arreglados: " + emptyFixed + "\n\n" +
      climberInfo + "\n\n" + entranceInfo, "OK");
  }

  /// <summary>Mesh Collider con la forma real en cada malla sin collider (menos el techo).</summary>
  private static int AddMeshColliders(Transform root)
  {
    var count = 0;

    foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
    {
      var go = filter.gameObject;

      if (filter.sharedMesh == null || go.GetComponent<Collider>() != null || go.name.ToLowerInvariant().Contains("techo"))
      {
        continue;
      }

      var mc = Undo.AddComponent<MeshCollider>(go);
      mc.sharedMesh = filter.sharedMesh;
      count++;
    }

    return count;
  }

  private static string AddStepClimber(UnityEngine.SceneManagement.Scene scene)
  {
    var info = new List<string>();

    // Prefab del Player: lo heredan todas las escenas.
    foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
    {
      var path = AssetDatabase.GUIDToAssetPath(guid);
      var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);

      if (asset == null || asset.GetComponent<Player>() == null || asset.GetComponent<StepClimber>() != null)
      {
        continue;
      }

      var contents = PrefabUtility.LoadPrefabContents(path);
      contents.AddComponent<StepClimber>();
      PrefabUtility.SaveAsPrefabAsset(contents, path);
      PrefabUtility.UnloadPrefabContents(contents);
      info.Add("StepClimber añadido al prefab " + path);
    }

    // Players de la escena que no vienen del prefab.
    foreach (var player in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Player>(true)))
    {
      if (player.GetComponent<StepClimber>() == null)
      {
        Undo.AddComponent<StepClimber>(player.gameObject);
        info.Add("StepClimber añadido a " + player.name + " (escena)");
      }
    }

    return info.Count > 0 ? string.Join("\n", info) : "La nutria ya tenia StepClimber (sube escalones).";
  }

  private static string SetupEntrance(UnityEngine.SceneManagement.Scene scene, Transform house, List<Transform> all)
  {
    var existing = all.FirstOrDefault(t => t != null && t.name == EntranceName);

    if (existing != null)
    {
      var door = existing.GetComponent<SceneDoor>();
      if (door == null) door = Undo.AddComponent<SceneDoor>(existing.gameObject);
      SetScene(door);
      return "'" + EntranceName + "' ya existia: no se movio (escena: " + TargetScene + ").";
    }

    // Cuerpo de la casa (la malla mas grande sin el techo) y techo.
    var renderers = house.GetComponentsInChildren<MeshRenderer>(true);
    var roofs = renderers.Where(r => r.name.ToLowerInvariant().Contains("techo")).ToArray();
    var walls = renderers.Except(roofs).ToArray();

    if (walls.Length == 0)
    {
      return "No encontre las mallas de la casa para colocar la entrada.";
    }

    var body = walls[0].bounds;
    foreach (var r in walls) body.Encapsulate(r.bounds);

    // La escalera sobresale del techo: ese lado es el frente.
    Vector3 pos;
    Vector3 size;

    if (roofs.Length > 0)
    {
      var roof = roofs[0].bounds;
      foreach (var r in roofs) roof.Encapsulate(r.bounds);

      var sides = new[]
      {
        (gap: roof.min.x - body.min.x, edge: new Vector3(roof.min.x, 0f, body.center.z), alongX: false),
        (gap: body.max.x - roof.max.x, edge: new Vector3(roof.max.x, 0f, body.center.z), alongX: false),
        (gap: roof.min.z - body.min.z, edge: new Vector3(body.center.x, 0f, roof.min.z), alongX: true),
        (gap: body.max.z - roof.max.z, edge: new Vector3(body.center.x, 0f, roof.max.z), alongX: true),
      };

      var front = sides.OrderByDescending(s => s.gap).First();
      pos = front.edge;
      size = front.alongX ? new Vector3(3f, 4f, 2.4f) : new Vector3(2.4f, 4f, 3f);
    }
    else
    {
      pos = new Vector3(body.center.x, 0f, body.min.z);
      size = new Vector3(3f, 4f, 2.4f);
    }

    pos.y = body.min.y;

    var go = new GameObject(EntranceName);
    Undo.RegisterCreatedObjectUndo(go, "Entrada casa");
    go.transform.position = pos;

    var box = go.AddComponent<BoxCollider>();
    box.isTrigger = true;
    box.size = size;
    box.center = new Vector3(0f, size.y * 0.5f, 0f);

    SetScene(go.AddComponent<SceneDoor>());
    Selection.activeObject = go;
    EditorGUIUtility.PingObject(go);
    return "Creada '" + EntranceName + "' (caja naranja) en la puerta: al entrar carga " + TargetScene + ".\nSi no queda justo en la puerta, muevela con la herramienta Move.";
  }

  private static void SetScene(SceneDoor door)
  {
    var so = new SerializedObject(door);
    so.FindProperty("_sceneName").stringValue = TargetScene;
    so.ApplyModifiedPropertiesWithoutUndo();
  }

  private static void AddSceneToBuild(string sceneName)
  {
    var path = AssetDatabase.FindAssets(sceneName + " t:Scene")
      .Select(AssetDatabase.GUIDToAssetPath)
      .FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == sceneName);

    if (path == null)
    {
      return;
    }

    var scenes = EditorBuildSettings.scenes.ToList();

    if (scenes.All(s => s.path != path))
    {
      scenes.Add(new EditorBuildSettingsScene(path, true));
      EditorBuildSettings.scenes = scenes.ToArray();
    }
  }
}
