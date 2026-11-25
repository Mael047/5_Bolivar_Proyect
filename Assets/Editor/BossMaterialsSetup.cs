// BossMaterialsSetup.cs
// Tools > Jefe > Texturas y dificultad
//   - Crea Jefe_Cuerpo.mat (Boss.png) y Jefe_Tentaculo.mat (Tentacle.png),
//     URP Lit con emision lista para el destello al recibir golpes, y los pone
//     en Jefe.prefab y Tentaculo.prefab.
//   - En el jefe de la escena abierta: mucha mas vida (1000) y mas daño de
//     los tentaculos (30) y del ataque cargado (40). Todo se puede cambiar
//     luego en Health y Boss Fight.

using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BossMaterialsSetup
{
  private const string Folder = "Assets/Characters/Boss";

  [MenuItem("Tools/Jefe/Texturas y dificultad")]
  private static void Run()
  {
    if (EditorApplication.isPlaying)
    {
      EditorUtility.DisplayDialog("Jefe", "Sal del modo Play primero.", "OK");
      return;
    }

    var log = new System.Text.StringBuilder();

    var body = MakeMaterial("Jefe_Cuerpo", Folder + "/Boss.png", log);
    var tentacle = MakeMaterial("Jefe_Tentaculo", Folder + "/Tentacle.png", log);
    Apply(Folder + "/Jefe.prefab", body, log);
    Apply(Folder + "/Tentaculo.prefab", tentacle, log);

    // Dificultad en el jefe de la escena
    var scene = EditorSceneManager.GetActiveScene();
    var fight = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BossFight>(true)).FirstOrDefault();

    if (fight != null)
    {
      var health = fight.GetComponent<Health>();
      var hso = new SerializedObject(health);
      hso.FindProperty("maxHealth").intValue = 1000;
      hso.ApplyModifiedProperties();

      var so = new SerializedObject(fight);
      so.FindProperty("_tentacleDamage").intValue = 30;
      so.FindProperty("_chargeDamage").intValue = 40;
      so.ApplyModifiedProperties();

      EditorSceneManager.MarkSceneDirty(scene);
      EditorSceneManager.SaveScene(scene);
      log.AppendLine("Jefe de la escena: vida 1000, tentaculos 30, ataque cargado 40.");
    }
    else
    {
      log.AppendLine("No hay jefe con Boss Fight en esta escena (la dificultad no se cambio).");
    }

    AssetDatabase.SaveAssets();
    EditorUtility.DisplayDialog("Jefe", log.ToString(), "OK");
  }

  private static Material MakeMaterial(string name, string texturePath, System.Text.StringBuilder log)
  {
    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

    if (texture == null)
    {
      log.AppendLine("Falta la textura " + texturePath);
      return null;
    }

    var path = Folder + "/" + name + ".mat";
    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

    if (mat == null)
    {
      mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
      AssetDatabase.CreateAsset(mat, path);
    }

    mat.SetTexture("_BaseMap", texture);
    mat.SetColor("_BaseColor", Color.white);
    mat.SetFloat("_Smoothness", 0.15f);
    mat.SetFloat("_Metallic", 0f);
    // Emision negra: el destello del golpe la enciende por un instante.
    mat.EnableKeyword("_EMISSION");
    mat.SetColor("_EmissionColor", Color.black);
    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
    EditorUtility.SetDirty(mat);
    log.AppendLine("Material " + name + " con " + texture.name);
    return mat;
  }

  private static void Apply(string prefabPath, Material material, System.Text.StringBuilder log)
  {
    if (material == null || AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
    {
      return;
    }

    var root = PrefabUtility.LoadPrefabContents(prefabPath);

    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
    {
      if (r is ParticleSystemRenderer) continue;
      r.sharedMaterials = Enumerable.Repeat(material, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
    }

    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
    PrefabUtility.UnloadPrefabContents(root);
    log.AppendLine("Texturas puestas en " + System.IO.Path.GetFileName(prefabPath));
  }
}
