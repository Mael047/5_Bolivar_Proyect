// NivelCasaSetup.cs
// Deja jugable la escena Nivel_CASA copiando desde Jardin_Principal lo que
// hace falta para jugar (con sus ajustes y conexiones de prefab):
//   Main Camera, Player, Sistemas (audio, manos, coleccionables, UI,
//   minimapa) y EventSystem.
// Ademas:
//   - Quita la camara vieja de la casa y apaga el menu principal (se empieza
//     jugando directamente).
//   - Pone al jugador en el punto de inicio (objeto "PuntoDeInicio", se crea
//     en la entrada si no existe: muevelo donde quieras y vuelve a ejecutar).
//   - Añade CameraOccluderHider a la camara (oculta las paredes que tapan).
//   - Añade InteractiveCameraFocus a "mesabillar" (vista cenital con F / X).
//   - Añade Nivel_CASA a la lista de escenas de la build.
// Se puede volver a ejecutar: no duplica lo que ya existe.
//
// Menu: Tools > Nivel Casa > Preparar escena jugable

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class NivelCasaSetup
{
  private const string CasaScenePath = "Assets/Scenes/Nivel_CASA.unity";
  private const string SourceScenePath = "Assets/Scenes/Jardin_Principal.unity";
  private static readonly string[] RootsToCopy = { "Main Camera", "Player", "Sistemas", "EventSystem" };
  private static readonly Vector3 DefaultSpawn = new Vector3(9.7f, 0.2f, -27f);

  [MenuItem("Tools/Nivel Casa/Preparar escena jugable")]
  private static void Run()
  {
    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
    {
      return;
    }

    var casa = EditorSceneManager.OpenScene(CasaScenePath, OpenSceneMode.Single);
    SceneManager.SetActiveScene(casa);

    var alreadyHasPlayer = casa.GetRootGameObjects().Any(g => g.GetComponentInChildren<Player>(true) != null);

    if (!alreadyHasPlayer)
    {
      CopyGameplayFrom(casa);
    }

    var player = casa.GetRootGameObjects().Select(g => g.GetComponentInChildren<Player>(true)).FirstOrDefault(p => p != null);
    PlacePlayer(casa, player);

    // Camara: la que tiene TopDownCamera manda; las demas se quitan.
    var gameplayCamera = casa.GetRootGameObjects().Select(g => g.GetComponentInChildren<TopDownCamera>(true)).FirstOrDefault(c => c != null);

    foreach (var cam in casa.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).ToList())
    {
      if (gameplayCamera != null && cam.gameObject == gameplayCamera.gameObject) continue;
      if (cam.targetTexture != null || cam.transform.position.y < -1000f) continue; // camaras de retratos/minimapa
      if (cam.GetComponentInParent<Canvas>() != null) continue;

      if (cam.name == "Main Camera")
      {
        Undo.DestroyObjectImmediate(cam.gameObject);
      }
    }

    if (gameplayCamera != null && gameplayCamera.GetComponent<CameraOccluderHider>() == null)
    {
      Undo.AddComponent<CameraOccluderHider>(gameplayCamera.gameObject);
    }

    // Sin menu principal en este nivel: se empieza jugando.
    foreach (var menu in casa.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Menus.MainMenu>(true)))
    {
      menu.gameObject.SetActive(false);
    }

    // Mesa de billar
    var table = casa.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "mesabillar");

    if (table == null)
    {
      Debug.LogWarning("[Nivel Casa] No encontre 'mesabillar' en la escena.");
    }
    else if (table.GetComponent<InteractiveCameraFocus>() == null)
    {
      Undo.AddComponent<InteractiveCameraFocus>(table.gameObject);
    }

    AddToBuild();
    EditorSceneManager.MarkSceneDirty(casa);
    EditorSceneManager.SaveScene(casa);

    if (table != null) Selection.activeObject = table.gameObject;

    EditorUtility.DisplayDialog("Nivel Casa",
      "Escena lista para jugar.\n\n" +
      "- Mueve 'PuntoDeInicio' para cambiar donde aparece la nutria y vuelve a ejecutar este menu (o mueve al Player directamente).\n" +
      "- La mesa de billar tiene InteractiveCameraFocus: acercate y pulsa F / X.", "OK");
  }

  private static void CopyGameplayFrom(Scene casa)
  {
    var source = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Additive);

    try
    {
      var roots = new List<GameObject>();

      foreach (var name in RootsToCopy)
      {
        var go = source.GetRootGameObjects().FirstOrDefault(g => g.name.Trim() == name);

        if (go != null)
        {
          roots.Add(go);
        }
        else
        {
          Debug.LogWarning($"[Nivel Casa] No encontre '{name}' en Jardin_Principal.");
        }
      }

      // Duplicar con el portapapeles conserva las conexiones de prefab, los
      // overrides y las referencias entre los objetos copiados.
      Selection.objects = roots.Cast<Object>().ToArray();
      Unsupported.DuplicateGameObjectsUsingPasteboard();

      foreach (var copy in Selection.gameObjects)
      {
        copy.name = copy.name.Replace("(1)", "").Trim();
        SceneManager.MoveGameObjectToScene(copy, casa);
        Undo.RegisterCreatedObjectUndo(copy, "Copiar sistemas de juego");
      }
    }
    finally
    {
      // Se cierra sin guardar: el jardin queda como estaba.
      EditorSceneManager.CloseScene(source, true);
    }
  }

  private static void PlacePlayer(Scene casa, Player player)
  {
    var spawn = casa.GetRootGameObjects().FirstOrDefault(g => g.name == "PuntoDeInicio");

    if (spawn == null)
    {
      spawn = new GameObject("PuntoDeInicio");
      SceneManager.MoveGameObjectToScene(spawn, casa);
      spawn.transform.position = DefaultSpawn;
      Undo.RegisterCreatedObjectUndo(spawn, "Punto de inicio");
    }

    if (player == null)
    {
      return;
    }

    Undo.RecordObject(player.transform, "Colocar jugador");
    player.transform.SetPositionAndRotation(spawn.transform.position, spawn.transform.rotation);

    // La camara arranca ya sobre el jugador.
    var cam = casa.GetRootGameObjects().Select(g => g.GetComponentInChildren<TopDownCamera>(true)).FirstOrDefault(c => c != null);

    if (cam != null)
    {
      Undo.RecordObject(cam.transform, "Colocar camara");
      cam.transform.position = player.transform.position + new Vector3(0f, 10f, -8f);
    }
  }

  private static void AddToBuild()
  {
    var scenes = EditorBuildSettings.scenes.ToList();

    if (scenes.Any(s => s.path == CasaScenePath))
    {
      return;
    }

    scenes.Add(new EditorBuildSettingsScene(CasaScenePath, true));
    EditorBuildSettings.scenes = scenes.ToArray();
  }
}
