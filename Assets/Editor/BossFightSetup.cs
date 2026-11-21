// BossFightSetup.cs
// Tools > Jefe > Preparar pelea en la escena abierta
//   - Busca el jefe en la escena (objeto con el controller Jefe_Cuerpo).
//   - Le pone Health (300), un collider para la espada, capa Enemy y BossFight
//     con el prefab Tentaculo.
//   - Crea "ArenaJefe": la caja de la sala (trigger). La pelea empieza cuando
//     la nutria entra en ella. Ajusta su tamaño para que cubra la sala.
// Se puede volver a ejecutar: no duplica nada.

using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BossFightSetup
{
  private const string TentaclePath = "Assets/Characters/Boss/Tentaculo.prefab";

  [MenuItem("Tools/Jefe/Preparar pelea en la escena abierta")]
  private static void Run()
  {
    if (EditorApplication.isPlaying)
    {
      EditorUtility.DisplayDialog("Jefe", "Sal del modo Play primero.", "OK");
      return;
    }

    var scene = EditorSceneManager.GetActiveScene();
    var boss = scene.GetRootGameObjects()
      .SelectMany(r => r.GetComponentsInChildren<Animator>(true))
      .FirstOrDefault(a => a.runtimeAnimatorController != null && a.runtimeAnimatorController.name == "Jefe_Cuerpo");

    if (boss == null)
    {
      EditorUtility.DisplayDialog("Jefe", "No encontre el jefe en esta escena (un objeto con el Animator Jefe_Cuerpo). Arrastra Jefe.prefab a la escena primero.", "OK");
      return;
    }

    var go = boss.gameObject;
    var bounds = new Bounds(go.transform.position, Vector3.zero);
    foreach (var r in go.GetComponentsInChildren<Renderer>(true)) bounds.Encapsulate(r.bounds);

    // Vida
    var health = go.GetComponent<Health>();

    if (health == null)
    {
      health = Undo.AddComponent<Health>(go);
      var hso = new SerializedObject(health);
      var max = hso.FindProperty("maxHealth");
      if (max != null) max.intValue = 300;
      hso.ApplyModifiedProperties();
    }

    // Collider para la espada
    if (go.GetComponent<Collider>() == null)
    {
      var capsule = Undo.AddComponent<CapsuleCollider>(go);
      var scale = go.transform.lossyScale;
      capsule.height = bounds.size.y / Mathf.Max(0.001f, scale.y);
      capsule.radius = Mathf.Max(bounds.size.x, bounds.size.z) * 0.35f / Mathf.Max(0.001f, scale.x);
      capsule.center = go.transform.InverseTransformPoint(bounds.center);
    }

    Undo.RecordObject(go, "Jefe");
    go.layer = 10; // Enemy

    var fight = go.GetComponent<BossFight>();
    if (fight == null) fight = Undo.AddComponent<BossFight>(go);

    // Sala
    var arena = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "ArenaJefe");
    BoxCollider box;

    if (arena == null)
    {
      var arenaGo = new GameObject("ArenaJefe");
      Undo.RegisterCreatedObjectUndo(arenaGo, "Arena jefe");
      arenaGo.layer = 2; // Ignore Raycast
      arenaGo.transform.position = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
      box = arenaGo.AddComponent<BoxCollider>();
      box.isTrigger = true;
      box.size = new Vector3(14f, 5f, 14f);
      box.center = new Vector3(0f, 2.5f, 0f);
    }
    else
    {
      box = arena.GetComponent<BoxCollider>();
      if (box == null) box = Undo.AddComponent<BoxCollider>(arena.gameObject);
      box.isTrigger = true;
    }

    var so = new SerializedObject(fight);
    so.FindProperty("_arena").objectReferenceValue = box;

    var fx = so.FindProperty("_fxMaterial");
    if (fx.objectReferenceValue == null)
    {
      // El brillo de los escupitajos: particulas aditivas con textura suave.
      fx.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("d1ce31eb463d7cb4e984abc5c0632dab"));
    }

    var dissolve = so.FindProperty("_dissolveMaterial");
    if (dissolve.objectReferenceValue == null)
    {
      const string matPath = "Assets/Materials/Jefe_Disolver.mat";
      var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
      var shader = Shader.Find("ReQuinta/JefeDisolver");

      if (mat == null && shader != null)
      {
        mat = new Material(shader) { name = "Jefe_Disolver" };
        AssetDatabase.CreateAsset(mat, matPath);
      }

      dissolve.objectReferenceValue = mat;
    }

    var tentacle = so.FindProperty("_tentaclePrefab");
    if (tentacle.objectReferenceValue == null) tentacle.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(TentaclePath);
    so.ApplyModifiedProperties();

    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);
    Selection.activeObject = box.gameObject;

    EditorUtility.DisplayDialog("Jefe",
      "Pelea lista en '" + go.name + "'.\n\n" +
      "- ArenaJefe (seleccionado): ajusta su Box Collider para que cubra la sala; la pelea empieza al entrar.\n" +
      "- Barriers (opcional): objetos que cierran la sala durante la pelea.\n" +
      "- Rewards: lo que suelta al morir.", "OK");
  }
}
