// NivelCasaScale.cs
// Escala la casa (paredes, suelos y todos los muebles) sin tocar al jugador,
// la camara ni los sistemas, que estan ajustados al tamaño del jardin.
// Todo lo que no es de juego se mete en un objeto "Casa_Escala"; cambiar su
// Scale en el Inspector agranda o achica la casa entera de una vez.
//
// Menu: Tools > Nivel Casa > Escalar casa (x0.4)   (o x0.5 / x0.6)
// Se puede repetir: cada vez deja la casa a ese tamaño respecto al original,
// y recoloca al jugador y al PuntoDeInicio en el mismo sitio relativo.

using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NivelCasaScale
{
  private const string GroupName = "Casa_Escala";
  private static readonly string[] Keep = { "Main Camera", "Player", "Sistemas", "EventSystem", "Directional Light", "PuntoDeInicio" };

  [MenuItem("Tools/Nivel Casa/Escalar casa (x0.4)")] private static void S04() => Apply(0.4f);
  [MenuItem("Tools/Nivel Casa/Escalar casa (x0.5)")] private static void S05() => Apply(0.5f);
  [MenuItem("Tools/Nivel Casa/Escalar casa (x0.6)")] private static void S06() => Apply(0.6f);
  [MenuItem("Tools/Nivel Casa/Escalar casa (x1, tamaño original)")] private static void S1() => Apply(1f);

  private static void Apply(float scale)
  {
    var scene = EditorSceneManager.GetActiveScene();

    if (scene.name != "Nivel_CASA")
    {
      EditorUtility.DisplayDialog("Nivel Casa", "Abre la escena Nivel_CASA primero.", "OK");
      return;
    }

    var group = scene.GetRootGameObjects().FirstOrDefault(g => g.name == GroupName);

    if (group == null)
    {
      group = new GameObject(GroupName);
      Undo.RegisterCreatedObjectUndo(group, "Escalar casa");
      group.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

      foreach (var root in scene.GetRootGameObjects())
      {
        if (root == group || Keep.Contains(root.name.Trim()) || root.GetComponentInChildren<Player>(true) != null)
        {
          continue;
        }

        Undo.SetTransformParent(root.transform, group.transform, "Escalar casa");
      }
    }

    var previous = group.transform.localScale.x;
    var ratio = scale / Mathf.Max(0.0001f, previous);

    Undo.RecordObject(group.transform, "Escalar casa");
    group.transform.localScale = Vector3.one * scale;

    // El jugador y el punto de inicio conservan su sitio dentro de la casa.
    foreach (var root in scene.GetRootGameObjects())
    {
      var isPlayer = root.GetComponentInChildren<Player>(true) != null;

      if (isPlayer || root.name == "PuntoDeInicio")
      {
        Undo.RecordObject(root.transform, "Escalar casa");
        var p = root.transform.position;
        root.transform.position = new Vector3(p.x * ratio, p.y * ratio + (isPlayer ? 0.05f : 0f), p.z * ratio);
      }
    }

    // La camara arranca sobre el jugador.
    var player = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<Player>(true)).FirstOrDefault(p => p != null);
    var cam = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<TopDownCamera>(true)).FirstOrDefault(c => c != null);

    if (player != null && cam != null)
    {
      Undo.RecordObject(cam.transform, "Escalar casa");
      cam.transform.position = player.transform.position + new Vector3(0f, 10f, -8f);
    }

    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);
    Selection.activeObject = group;
  }
}
