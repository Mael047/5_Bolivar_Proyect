// CasaJardinDoors.cs
// Tools > Puertas > Conectar casa y jardin
// Ejecutalo con cada escena abierta (una vez en Jardin_Principal y otra en
// Nivel_CASA). Hace lo que toque segun la escena:
//
//   Jardin_Principal:
//     - Crea "Llegada_DesdeCasa": donde aparece la nutria al salir de la casa,
//       delante de la entrada (fuera de la caja de Entrada_Casa).
//     - Entrada_Casa lleva a la nutria a "PuntoDeInicio" de la casa.
//
//   Nivel_CASA:
//     - Crea "Salida_Casa" (caja naranja) en la puerta de la casa: al entrar
//       vuelve al jardin, a Llegada_DesdeCasa. Se coloca un poco detras de
//       PuntoDeInicio (hacia la entrada); muevela a la puerta si hace falta.
//
// La nutria pasa con todo: vida, espada, escudo, llaves, coleccionables y lo
// que lleve agarrado con la mano.

using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CasaJardinDoors
{
  private const string GardenScene = "Jardin_Principal";
  private const string HouseScene = "Nivel_CASA";
  private const string GardenDoor = "Entrada_Casa";
  private const string GardenArrival = "Llegada_DesdeCasa";
  private const string HouseExit = "Salida_Casa";
  private const string HouseArrival = "PuntoDeInicio";

  [MenuItem("Tools/Puertas/Conectar casa y jardin")]
  private static void Run()
  {
    if (EditorApplication.isPlaying)
    {
      EditorUtility.DisplayDialog("Puertas", "Sal del modo Play primero.", "OK");
      return;
    }

    var scene = EditorSceneManager.GetActiveScene();
    var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
    string result;

    var entrance = all.FirstOrDefault(t => t.name == GardenDoor);
    var houseSpawn = all.FirstOrDefault(t => t.name == HouseArrival);

    if (entrance != null)
    {
      result = SetupGarden(scene, entrance, all);
    }
    else if (houseSpawn != null)
    {
      result = SetupHouse(houseSpawn, all);
    }
    else
    {
      EditorUtility.DisplayDialog("Puertas", "Abre Jardin_Principal (con Entrada_Casa) o Nivel_CASA (con PuntoDeInicio).", "OK");
      return;
    }

    AddToBuild(GardenScene);
    AddToBuild(HouseScene);
    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);
    EditorUtility.DisplayDialog("Puertas", result, "OK");
  }

  // ---------- Jardin ----------

  private static string SetupGarden(UnityEngine.SceneManagement.Scene scene, Transform entrance, System.Collections.Generic.List<Transform> all)
  {
    var door = entrance.GetComponent<SceneDoor>();

    if (door != null)
    {
      var so = new SerializedObject(door);
      so.FindProperty("_sceneName").stringValue = HouseScene;
      so.FindProperty("_arrivalPoint").stringValue = HouseArrival;
      so.ApplyModifiedProperties();
    }

    var arrival = all.FirstOrDefault(t => t.name == GardenArrival);

    if (arrival != null)
    {
      return "'" + GardenArrival + "' ya existia: no se movio.\nEntrada_Casa lleva a " + HouseArrival + " de la casa.";
    }

    // Hacia fuera de la casa: desde el centro de la casa hacia la entrada.
    var house = all.FirstOrDefault(t => t.name == "casacasa_unity");
    var houseCenter = house != null ? Bounds(house).center : entrance.position - entrance.forward * 5f;
    var outward = entrance.position - houseCenter;
    outward.y = 0f;
    outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.back;

    // Fuera de la caja de la puerta (mitad de su fondo) + margen.
    var box = entrance.GetComponent<BoxCollider>();
    var depth = box != null ? Mathf.Abs(Vector3.Dot(Vector3.Scale(box.size, entrance.lossyScale), new Vector3(Mathf.Abs(outward.x), 0f, Mathf.Abs(outward.z)))) : 2.4f;
    var pos = entrance.position + outward * (depth * 0.5f + 1.2f);

    // Apoyar en el suelo.
    Physics.SyncTransforms();

    if (Physics.Raycast(pos + Vector3.up * 10f, Vector3.down, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore))
    {
      pos = hit.point + Vector3.up * 0.1f;
    }

    var go = new GameObject(GardenArrival);
    Undo.RegisterCreatedObjectUndo(go, "Llegada desde la casa");
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
    go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(outward));
    var icon = EditorGUIUtility.IconContent("sv_icon_dot3_pix16_gizmo").image as Texture2D;
    if (icon != null) EditorGUIUtility.SetIconForObject(go, icon);

    Selection.activeObject = go;
    EditorGUIUtility.PingObject(go);
    return "Creado '" + GardenArrival + "' delante de la entrada de la casa (la nutria sale mirando hacia fuera).\n" +
           "Entrada_Casa lleva a " + HouseArrival + " de la casa.\n\nAhora abre Nivel_CASA y usa este menu otra vez.";
  }

  // ---------- Casa ----------

  private static string SetupHouse(Transform spawn, System.Collections.Generic.List<Transform> all)
  {
    var exit = all.FirstOrDefault(t => t.name == HouseExit);
    var created = exit == null;

    if (created)
    {
      var go = new GameObject(HouseExit);
      Undo.RegisterCreatedObjectUndo(go, "Salida de la casa");
      exit = go.transform;

      // La entrada de la casa esta abajo (sur, -Z): la salida, detras del punto de inicio.
      exit.position = spawn.position + Vector3.back * 1.6f;
      exit.rotation = Quaternion.identity;

      var box = go.AddComponent<BoxCollider>();
      box.isTrigger = true;
      box.size = new Vector3(2.5f, 3f, 1f);
      box.center = new Vector3(0f, 1.5f, 0f);
      go.AddComponent<SceneDoor>();
    }

    var door = exit.GetComponent<SceneDoor>();
    if (door == null) door = Undo.AddComponent<SceneDoor>(exit.gameObject);

    var so = new SerializedObject(door);
    so.FindProperty("_sceneName").stringValue = GardenScene;
    so.FindProperty("_arrivalPoint").stringValue = GardenArrival;
    so.ApplyModifiedProperties();

    Selection.activeObject = exit.gameObject;
    EditorGUIUtility.PingObject(exit.gameObject);

    return (created
        ? "Creada '" + HouseExit + "' (caja naranja) detras de " + HouseArrival + ". Muevela para que tape la puerta de la casa."
        : "'" + HouseExit + "' ya existia: no se movio.") +
      "\nAl entrar vuelve al jardin, a " + GardenArrival + ".";
  }

  // ---------- Utilidades ----------

  private static Bounds Bounds(Transform root)
  {
    var renderers = root.GetComponentsInChildren<Renderer>();
    if (renderers.Length == 0) return new Bounds(root.position, Vector3.one);
    var b = renderers[0].bounds;
    foreach (var r in renderers) b.Encapsulate(r.bounds);
    return b;
  }

  private static void AddToBuild(string sceneName)
  {
    var path = AssetDatabase.FindAssets(sceneName + " t:Scene")
      .Select(AssetDatabase.GUIDToAssetPath)
      .FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == sceneName);

    if (path == null || EditorBuildSettings.scenes.Any(s => s.path == path))
    {
      return;
    }

    var scenes = EditorBuildSettings.scenes.ToList();
    scenes.Add(new EditorBuildSettingsScene(path, true));
    EditorBuildSettings.scenes = scenes.ToArray();
  }
}
