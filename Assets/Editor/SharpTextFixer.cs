// SharpTextFixer.cs
// Tools > UI > Corregir textos pixelados
//
// Los textos (UI Text) se dibujan al tamaño de su "Font Size". Si el texto,
// o el boton/panel que lo contiene, tiene Scale mayor que 1, Unity agranda esa
// imagen ya dibujada y las letras se ven pixeladas/borrosas.
//
// Esta herramienta revisa los prefabs de Assets/UI (y la escena abierta) y, en
// cada texto agrandado, deja la escala total en 1 y sube el Font Size en la
// misma proporcion: se ve igual de grande, pero nitido. No toca imagenes ni
// botones, solo los textos.

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class SharpTextFixer
{
  private const float Threshold = 1.05f;

  [MenuItem("Tools/UI/Corregir textos pixelados")]
  private static void Run()
  {
    if (EditorApplication.isPlaying)
    {
      EditorUtility.DisplayDialog("Textos", "Sal del modo Play primero.", "OK");
      return;
    }

    var report = new StringBuilder();
    var total = 0;

    // Prefabs de interfaz
    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/UI" }))
    {
      var path = AssetDatabase.GUIDToAssetPath(guid);
      var root = PrefabUtility.LoadPrefabContents(path);

      try
      {
        var fixedHere = FixAll(root.transform, report, System.IO.Path.GetFileNameWithoutExtension(path), false);

        if (fixedHere > 0)
        {
          PrefabUtility.SaveAsPrefabAsset(root, path);
          total += fixedHere;
        }
      }
      finally
      {
        PrefabUtility.UnloadPrefabContents(root);
      }
    }

    // Escena abierta (textos propios de la escena)
    var scene = EditorSceneManager.GetActiveScene();
    var sceneFixed = 0;

    foreach (var go in scene.GetRootGameObjects())
    {
      sceneFixed += FixAll(go.transform, report, scene.name, true);
    }

    if (sceneFixed > 0)
    {
      EditorSceneManager.MarkSceneDirty(scene);
      EditorSceneManager.SaveScene(scene);
      total += sceneFixed;
    }

    var text = total == 0
      ? "No hay textos agrandados: ninguno deberia verse pixelado por escala."
      : "Textos corregidos: " + total + "\n\n" + report;

    Debug.Log("[SharpTextFixer] " + text);
    EditorUtility.DisplayDialog("Textos", text.Length > 1500 ? text.Substring(0, 1500) + "\n... (lista completa en la Console)" : text, "OK");
  }

  private static int FixAll(Transform root, StringBuilder report, string owner, bool undo)
  {
    var count = 0;

    // Los botones dentro de listas (Layout Group) no tienen tamaño hasta que
    // se calcula el layout: se calcula antes de medir nada.
    if (root is RectTransform rootRect)
    {
      LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
    }

    foreach (var text in root.GetComponentsInChildren<Text>(true))
    {
      // En la escena, los textos de prefabs se corrigen en el prefab.
      if (undo && PrefabUtility.IsPartOfPrefabInstance(text))
      {
        continue;
      }

      var factor = ScaleUpToCanvas(text.transform);

      if (factor <= Threshold)
      {
        continue;
      }

      if (undo)
      {
        Undo.RecordObject(text, "Corregir textos");
        Undo.RecordObject(text.rectTransform, "Corregir textos");
      }

      if (!Fix(text, factor))
      {
        report.AppendLine($"- {owner}: {PathOf(text.transform, root)} (x{factor:0.##}) NO corregido: su contenedor no tiene tamaño. Baja la Scale a mano.");
        continue;
      }

      report.AppendLine($"- {owner}: {PathOf(text.transform, root)} (x{factor:0.##} -> tamaño {text.fontSize})");
      count++;
    }

    return count;
  }

  /// <summary>Escala acumulada del texto hasta su Canvas (sin contar el Canvas raiz).</summary>
  private static float ScaleUpToCanvas(Transform t)
  {
    var factor = 1f;

    for (var current = t; current != null; current = current.parent)
    {
      if (current.GetComponent<Canvas>() != null && (current.parent == null || current.GetComponent<Canvas>().isRootCanvas))
      {
        break;
      }

      factor *= Mathf.Abs(current.localScale.x);
    }

    return factor;
  }

  /// <summary>Corrige un texto. False si no se puede medir (no se toca).</summary>
  private static bool Fix(Text text, float factor)
  {
    var rt = text.rectTransform;
    var parent = rt.parent as RectTransform;
    var scale = rt.localScale;
    var stretched = rt.anchorMin != rt.anchorMax;

    if (stretched)
    {
      // Sigue estirado sobre su contenedor: misma area, escala total 1.
      if (parent == null || parent.rect.width < 1f || parent.rect.height < 1f)
      {
        return false;
      }

      var span = Vector2.Scale(parent.rect.size, rt.anchorMax - rt.anchorMin);
      var visible = span + rt.sizeDelta;
      rt.localScale = new Vector3(scale.x / factor, scale.y / factor, scale.z);
      rt.sizeDelta = visible * factor - span;
    }
    else
    {
      var size = rt.rect.size;

      if (size.x < 1f || size.y < 1f)
      {
        return false;
      }

      rt.localScale = new Vector3(scale.x / factor, scale.y / factor, scale.z);
      rt.sizeDelta = size * factor;
    }

    text.fontSize = Mathf.Max(1, Mathf.RoundToInt(text.fontSize * factor));

    if (text.resizeTextForBestFit)
    {
      text.resizeTextMinSize = Mathf.RoundToInt(text.resizeTextMinSize * factor);
      text.resizeTextMaxSize = Mathf.RoundToInt(text.resizeTextMaxSize * factor);
    }

    foreach (var effect in text.GetComponents<Shadow>())
    {
      effect.effectDistance *= factor;
    }

    return true;
  }

  private static string PathOf(Transform t, Transform root)
  {
    var parts = new List<string>();

    for (var current = t; current != null && current != root; current = current.parent)
    {
      parts.Insert(0, current.name);
    }

    return string.Join("/", parts);
  }
}
