// BreakablePhysicsFixer.cs
// Tools > Objetos > Quitar rompible de muebles (escena abierta)
// Deja los muebles (sillas, mesas...) como decorado fijo otra vez:
//   - Quita Breakable y Rigidbody de los objetos donde se añadio a mano en la
//     escena (las cajas y jarrones vienen de su prefab y no se tocan).
//   - Devuelve sus Mesh Collider originales (no convexos) y quita las cajas
//     que se habian puesto en su lugar.
//   - Si estaban en la capa Grabbable, vuelven a Default.
// En las cajas/jarrones de prefab solo se deshacen los cambios de fisica
// hechos en la escena (vuelven a lo que tenga el prefab).

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BreakablePhysicsFixer
{
  [MenuItem("Tools/Objetos/Quitar rompible de muebles")]
  private static void Run()
  {
    if (EditorApplication.isPlaying)
    {
      EditorUtility.DisplayDialog("Objetos", "Sal del modo Play primero.", "OK");
      return;
    }

    var scene = EditorSceneManager.GetActiveScene();
    int furniture = 0, prefabs = 0;
    var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NuiGrab.Breakable>(true)).ToList();

    foreach (var breakable in all)
    {
      if (breakable == null || breakable.OnlySword)
      {
        continue;
      }

      var go = breakable.gameObject;
      var fromPrefab = PrefabUtility.IsPartOfPrefabInstance(breakable) && !PrefabUtility.IsAddedComponentOverride(breakable);
      RestoreRemovedColliders(go);

      if (fromPrefab)
      {
        // Caja / jarron: volver a lo que define su prefab.
        foreach (var c in go.GetComponentsInChildren<Component>(true).Where(c => c is Collider || c is Rigidbody).ToList())
        {
          if (PrefabUtility.IsAddedComponentOverride(c)) Undo.DestroyObjectImmediate(c);
          else if (PrefabUtility.IsPartOfPrefabInstance(c)) PrefabUtility.RevertObjectOverride(c, InteractionMode.UserAction);
        }

        prefabs++;
        continue;
      }

      // Mueble: fuera Breakable y Rigidbody.
      Undo.DestroyObjectImmediate(breakable);
      var body = go.GetComponent<Rigidbody>();

      if (body != null)
      {
        if (PrefabUtility.IsPartOfPrefabInstance(body) && !PrefabUtility.IsAddedComponentOverride(body))
        {
          Undo.RecordObject(body, "Quitar rompible");
          body.isKinematic = true;
        }
        else
        {
          Undo.DestroyObjectImmediate(body);
        }
      }

      // Colliders: malla original (no convexa) en vez de cajas.
      foreach (var box in go.GetComponentsInChildren<BoxCollider>(true).ToList())
      {
        if (box.isTrigger) continue;
        var filter = box.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) continue;
        var fromAsset = PrefabUtility.IsPartOfPrefabInstance(box) && !PrefabUtility.IsAddedComponentOverride(box);
        if (fromAsset) continue;

        var hasMesh = box.GetComponent<MeshCollider>() != null;
        var material = box.sharedMaterial;
        var host = box.gameObject;
        Undo.DestroyObjectImmediate(box);

        if (!hasMesh)
        {
          var mesh = Undo.AddComponent<MeshCollider>(host);
          mesh.sharedMesh = filter.sharedMesh;
          mesh.sharedMaterial = material;
        }
      }

      foreach (var mesh in go.GetComponentsInChildren<MeshCollider>(true))
      {
        if (mesh.convex)
        {
          Undo.RecordObject(mesh, "Quitar rompible");
          mesh.convex = false;
        }
      }

      foreach (var t in go.GetComponentsInChildren<Transform>(true))
      {
        if (t.gameObject.layer == 8)
        {
          Undo.RecordObject(t.gameObject, "Quitar rompible");
          t.gameObject.layer = 0;
        }
      }

      furniture++;
    }

    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);
    EditorUtility.DisplayDialog("Objetos",
      "Muebles devueltos a decorado fijo: " + furniture + "\nCajas/jarrones de prefab restaurados: " + prefabs, "OK");
  }

  // Si un Mesh Collider del modelo se habia quitado en la escena, se recupera.
  private static void RestoreRemovedColliders(GameObject go)
  {
    if (!PrefabUtility.IsPartOfPrefabInstance(go)) return;
    var root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
    if (root == null) return;

    foreach (var removed in PrefabUtility.GetRemovedComponents(root))
    {
      if (removed.assetComponent is Collider && removed.containingInstanceGameObject != null &&
          removed.containingInstanceGameObject.transform.IsChildOf(go.transform))
      {
        removed.Revert(InteractionMode.UserAction);
      }
    }
  }
}
