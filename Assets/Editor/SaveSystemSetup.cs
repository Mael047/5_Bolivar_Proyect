// SaveSystemSetup.cs
// Tools > Guardado > Preparar bancos y muerte
//   - Pone PlayerRespawn en el prefab del Player (y en los de la escena):
//     al morir reaparece en el ultimo banco y sus cosas quedan donde murio.
//   - Pone SaveBench en los bancos de la escena abierta: los objetos
//     que se llaman "Bench..." (a otros se les pone SaveBench a mano).
//     Cada banco recibe un "PuntoReaparicion" delante (muevelo si hace falta).
// Tools > Guardado > Borrar partida guardada (para probar desde cero).

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SaveSystemSetup
{
  [MenuItem("Tools/Guardado/Preparar bancos y muerte")]
  private static void Run()
  {
    if (EditorApplication.isPlaying)
    {
      EditorUtility.DisplayDialog("Guardado", "Sal del modo Play primero.", "OK");
      return;
    }

    var info = new List<string>();

    // 1. Player
    foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
    {
      var path = AssetDatabase.GUIDToAssetPath(guid);
      var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);

      if (asset == null || asset.GetComponent<Player>() == null || asset.GetComponent<PlayerRespawn>() != null)
      {
        continue;
      }

      var contents = PrefabUtility.LoadPrefabContents(path);
      contents.AddComponent<PlayerRespawn>();
      PrefabUtility.SaveAsPrefabAsset(contents, path);
      PrefabUtility.UnloadPrefabContents(contents);
      info.Add("PlayerRespawn en " + Path.GetFileName(path));
    }

    var scene = EditorSceneManager.GetActiveScene();

    foreach (var player in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Player>(true)))
    {
      if (player.GetComponent<PlayerRespawn>() == null)
      {
        Undo.AddComponent<PlayerRespawn>(player.gameObject);
        info.Add("PlayerRespawn en " + player.name + " (escena)");
      }
    }

    // 2. Bancos (los que se llaman "Bench...")
    List<GameObject> benches;

    {
      benches = scene.GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
        .Where(t => t.name.ToLowerInvariant().StartsWith("bench"))
        .Select(t => t.gameObject)
        .Where(g => g.transform.parent == null || !g.transform.parent.name.ToLowerInvariant().StartsWith("bench"))
        .ToList();
    }

    var added = 0;

    foreach (var go in benches)
    {
      if (go.GetComponent<SaveBench>() != null)
      {
        continue;
      }

      var bench = Undo.AddComponent<SaveBench>(go);
      added++;

      // Centro del banco y punto de reaparicion delante.
      var renderers = go.GetComponentsInChildren<Renderer>();
      var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(go.transform.position, Vector3.one);
      foreach (var r in renderers) bounds.Encapsulate(r.bounds);

      var forward = Vector3.ProjectOnPlane(go.transform.forward, Vector3.up).normalized;
      if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
      var reach = Mathf.Abs(Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(forward.x), 0f, Mathf.Abs(forward.z))));

      var spawn = new GameObject("PuntoReaparicion").transform;
      Undo.RegisterCreatedObjectUndo(spawn.gameObject, "Banco");
      spawn.SetParent(go.transform, false);
      spawn.position = new Vector3(bounds.center.x, bounds.min.y + 0.1f, bounds.center.z) + forward * (reach + 0.8f);
      spawn.rotation = Quaternion.LookRotation(forward);

      var so = new SerializedObject(bench);
      so.FindProperty("_interactOffset").vector3Value = go.transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
      so.FindProperty("_spawnPoint").objectReferenceValue = spawn;
      so.ApplyModifiedProperties();
    }

    info.Add("Bancos preparados: " + added + (benches.Count == 0 ? " (no encontre objetos 'Bench'; ponles el componente Save Bench a mano)" : ""));

    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);
    EditorUtility.DisplayDialog("Guardado", string.Join("\n", info), "OK");
  }

  [MenuItem("Tools/Guardado/Borrar partida guardada")]
  private static void DeleteSave()
  {
    if (File.Exists(GameState.SavePath))
    {
      File.Delete(GameState.SavePath);
      EditorUtility.DisplayDialog("Guardado", "Partida guardada borrada.", "OK");
    }
    else
    {
      EditorUtility.DisplayDialog("Guardado", "No hay partida guardada.", "OK");
    }
  }
}
