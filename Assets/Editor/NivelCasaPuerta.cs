// NivelCasaPuerta.cs
// Convierte la puerta del medio de la casa ("puertacerrojo") en una puerta
// con llave, igual que las del jardin (LockedDoor1):
//   - Sin llave: tiembla y sale "Cerrada con llave".
//   - Con la llave del puzzle de las cartas (llave generica, Key Id vacio):
//     se gasta la llave y la puerta se hunde en el suelo.
//
// Crea un objeto "PuertaCerrada_Llave" (fuera de Casa_Escala, escala 1) en la
// base de la puerta, mete dentro la puerta y le pone LockedDoor. Todo se
// puede retocar despues en el Inspector (Key Id, modo de apertura, radio...).
// Se puede volver a ejecutar: no duplica nada, solo reajusta.
//
// Menu: Tools > Nivel Casa > Puerta del medio: cerrar con llave

using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NivelCasaPuerta
{
  private const string DoorName = "puertacerrojo";
  private const string RootName = "PuertaCerrada_Llave";

  [MenuItem("Tools/Nivel Casa/Puerta del medio: cerrar con llave")]
  private static void Run()
  {
    if (EditorApplication.isPlaying)
    {
      EditorUtility.DisplayDialog("Puerta", "Sal del modo Play primero.", "OK");
      return;
    }

    var scene = EditorSceneManager.GetActiveScene();
    var door = scene.GetRootGameObjects()
      .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
      .FirstOrDefault(t => t.name == DoorName);

    if (door == null)
    {
      EditorUtility.DisplayDialog("Puerta", $"No encontre '{DoorName}' en esta escena.", "OK");
      return;
    }

    // Tamaño real de la puerta en el mundo.
    var renderers = door.GetComponentsInChildren<Renderer>(true);

    if (renderers.Length == 0)
    {
      EditorUtility.DisplayDialog("Puerta", "La puerta no tiene mallas visibles.", "OK");
      return;
    }

    var bounds = renderers[0].bounds;
    foreach (var r in renderers) bounds.Encapsulate(r.bounds);

    // Raiz: la existente o una nueva en la base de la puerta.
    var lockedDoor = door.GetComponentInParent<LockedDoor>();
    Transform root;

    if (lockedDoor != null)
    {
      root = lockedDoor.transform;
    }
    else
    {
      var go = new GameObject(RootName);
      Undo.RegisterCreatedObjectUndo(go, "Puerta con llave");
      root = go.transform;
      root.position = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
      root.rotation = Quaternion.Euler(0f, door.eulerAngles.y, 0f);
      Undo.SetTransformParent(door, root, "Puerta con llave"); // conserva la posicion en el mundo
      lockedDoor = Undo.AddComponent<LockedDoor>(go);
    }

    var so = new SerializedObject(lockedDoor);
    so.FindProperty("_keyId").stringValue = "";                // llave generica (la del puzzle)
    so.FindProperty("_openAutomaticallyWithKey").boolValue = false;
    so.FindProperty("_interactRadius").floatValue = Mathf.Max(2f, Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f + 1f);
    so.FindProperty("_interactOffset").vector3Value = Vector3.zero;
    so.FindProperty("_promptHeight").floatValue = Mathf.Clamp(bounds.size.y * 0.8f, 1.2f, 2.5f);
    so.FindProperty("_door").objectReferenceValue = door;
    so.FindProperty("_openMode").enumValueIndex = (int)LockedDoor.OpenMode.Deslizar;
    // Se hunde toda su altura (+ margen) bajo el suelo. La raiz tiene escala 1.
    so.FindProperty("_slideOffset").vector3Value = new Vector3(0f, -(bounds.size.y + 0.3f), 0f);
    so.FindProperty("_disableCollidersWhenOpen").boolValue = true;

    var message = so.FindProperty("_messageView");

    if (message.objectReferenceValue == null)
    {
      foreach (var guid in AssetDatabase.FindAssets("DoorMessageUI t:Prefab"))
      {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));

        if (prefab != null && prefab.TryGetComponent<WorldMessageView>(out var view))
        {
          message.objectReferenceValue = view;
          break;
        }
      }
    }

    so.ApplyModifiedProperties();

    // La puerta necesita collider para cerrar el paso.
    var hasCollider = door.GetComponentsInChildren<Collider>(true).Any(c => !c.isTrigger);

    if (!hasCollider)
    {
      foreach (var filter in door.GetComponentsInChildren<MeshFilter>(true))
      {
        if (filter.sharedMesh == null) continue;
        var mc = Undo.AddComponent<MeshCollider>(filter.gameObject);
        mc.sharedMesh = filter.sharedMesh;
      }
    }

    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);
    Selection.activeObject = root.gameObject;
    EditorGUIUtility.PingObject(root.gameObject);

    EditorUtility.DisplayDialog("Puerta",
      "Listo: '" + root.name + "' se abre solo con la llave del puzzle de las cartas.\n\n" +
      "Altura de la puerta: " + bounds.size.y.ToString("0.0") + " m (se hunde en el suelo al abrir).", "OK");
  }
}
