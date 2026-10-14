// TelekinesisSetup.cs
// Tools > Telequinesis > Agregar efectos al HandSystem:
//   - Crea el material aditivo Assets/Materials/VFX/M_Telekinesis_Additive
//     (URP Particles/Unlit). Tenerlo como asset asegura que el shader se
//     incluya en el juego compilado.
//   - Añade TelekinesisVFX al prefab HandSystem y le asigna el material.
// Opcional: apaga las particulas antiguas del HandMarker.

using System.IO;
using NuiGrab;
using UnityEditor;
using UnityEngine;

public static class TelekinesisSetup
{
  private const string MaterialFolder = "Assets/Materials/VFX";
  private const string MaterialPath = MaterialFolder + "/M_Telekinesis_Additive.mat";

  [MenuItem("Tools/Telequinesis/Agregar efectos al HandSystem")]
  private static void Setup()
  {
    var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

    if (material == null)
    {
      var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

      if (shader == null)
      {
        EditorUtility.DisplayDialog("Telequinesis", "No encontre el shader 'Universal Render Pipeline/Particles/Unlit'.", "OK");
        return;
      }

      EnsureFolder(MaterialFolder);
      material = new Material(shader);
      TelekinesisVFX.ConfigureAdditive(material);
      AssetDatabase.CreateAsset(material, MaterialPath);
      AssetDatabase.SaveAssets();
    }

    string path = null;

    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
    {
      var p = AssetDatabase.GUIDToAssetPath(guid);
      var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);

      if (go != null && go.GetComponentInChildren<HandIndicator>(true) != null)
      {
        path = p;
        if (go.name == "HandSystem") break;
      }
    }

    if (path == null)
    {
      EditorUtility.DisplayDialog("Telequinesis", "No encontre el prefab con HandIndicator (HandSystem).", "OK");
      return;
    }

    var hideOld = EditorUtility.DisplayDialog("Telequinesis",
      "¿Apagar las particulas antiguas del cursor (la bola de chispas)?\n\nPuedes volver a encenderlas desde el HandMarker.",
      "Apagarlas", "Dejarlas");

    var root = PrefabUtility.LoadPrefabContents(path);

    try
    {
      var vfx = root.GetComponentInChildren<TelekinesisVFX>(true);

      if (vfx == null)
      {
        vfx = root.AddComponent<TelekinesisVFX>();
      }

      var so = new SerializedObject(vfx);
      so.FindProperty("_additiveMaterial").objectReferenceValue = material;
      so.ApplyModifiedPropertiesWithoutUndo();

      if (hideOld)
      {
        var indicator = root.GetComponentInChildren<HandIndicator>(true);
        var renderer = indicator != null ? indicator.GetComponent<ParticleSystemRenderer>() : null;
        if (renderer != null) renderer.enabled = false;
      }

      PrefabUtility.SaveAsPrefabAsset(root, path);
    }
    finally
    {
      PrefabUtility.UnloadPrefabContents(root);
    }

    Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    EditorUtility.DisplayDialog("Telequinesis", $"Efectos agregados a {path}.", "OK");
  }

  private static void EnsureFolder(string folder)
  {
    if (AssetDatabase.IsValidFolder(folder)) return;
    var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
    EnsureFolder(parent);
    AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
  }
}
