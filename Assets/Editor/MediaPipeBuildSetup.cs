// MediaPipeBuildSetup.cs
// En el editor MediaPipe lee los modelos directamente del paquete
// (Packages/com.github.homuler.mediapipe/PackageResources/MediaPipe), pero esa
// carpeta no existe en la build: alli los modelos tienen que estar en
// Assets/StreamingAssets. Sin ellos la build no detecta la mano ("la camara
// no funciona").
//
// Este script copia los modelos necesarios a StreamingAssets antes de cada
// build (y se puede lanzar a mano). Si quieres usar otro modelo, añadelo a
// la lista "Models".
//
// Menu: Tools > Camara > Copiar modelos de MediaPipe a StreamingAssets

using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class MediaPipeBuildSetup : IPreprocessBuildWithReport
{
  private const string PackageResources = "Packages/com.github.homuler.mediapipe/PackageResources/MediaPipe";
  private const string Target = "Assets/StreamingAssets";

  /// <summary>Modelos que usa el juego (HandTracker usa hand_landmarker).</summary>
  private static readonly string[] Models =
  {
    "hand_landmarker.bytes",
  };

  public int callbackOrder => -100;

  public void OnPreprocessBuild(BuildReport report)
  {
    Copy(false);
  }

  [MenuItem("Tools/Camara/Copiar modelos de MediaPipe a StreamingAssets")]
  private static void CopyMenu()
  {
    var copied = Copy(true);
    EditorUtility.DisplayDialog("MediaPipe", copied ? "Modelos copiados a Assets/StreamingAssets." : "Los modelos ya estaban al dia.", "OK");
  }

  private static bool Copy(bool verbose)
  {
    var source = Path.GetFullPath(PackageResources);

    if (!Directory.Exists(source))
    {
      Debug.LogError($"[MediaPipe] No encontre la carpeta de modelos del paquete: {source}");
      return false;
    }

    Directory.CreateDirectory(Target);
    var changed = false;

    foreach (var model in Models)
    {
      var from = Path.Combine(source, model);
      var to = Path.Combine(Target, model);

      if (!File.Exists(from))
      {
        Debug.LogError($"[MediaPipe] Falta el modelo {model} en el paquete.");
        continue;
      }

      if (File.Exists(to) && new FileInfo(to).Length == new FileInfo(from).Length)
      {
        continue;
      }

      File.Copy(from, to, true);
      changed = true;
      Debug.Log($"[MediaPipe] Copiado {model} a {Target} para la build.");
    }

    if (changed)
    {
      AssetDatabase.Refresh();
    }
    else if (verbose)
    {
      Debug.Log("[MediaPipe] Modelos ya presentes en StreamingAssets.");
    }

    return changed;
  }
}
