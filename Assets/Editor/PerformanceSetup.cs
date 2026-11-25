// PerformanceSetup.cs
// Tools > Rendimiento > Activar GPU Resident Drawer (proyecto, una vez)
//   - URP (PC): GPU Resident Drawer en "Instanced Drawing" + oclusion por GPU
//     (no dibuja lo que esta tapado por paredes, la casa, etc.).
//   - Graficos: BatchRendererGroup Variants = Keep All (lo exige el anterior).
//   - Static batching apagado en PC: no es compatible con el GPU Resident
//     Drawer (los objetos con static batching no se beneficiarian).
//   - Distancia de sombras a 35 (camara cenital: no hacen falta mas).
//
// Tools > Rendimiento > Optimizar escena abierta
//   - RenderOptimizer en la Main Camera (terreno + activacion por distancia).
//   - DistanceCullable en enemigos, particulas y animaciones de decorado.
//     No toca al jugador, la camara, la UI ni al jefe.

using System.Linq;
using System.Reflection;
using Rendimiento;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class PerformanceSetup
{
  private const string PcAssetPath = "Assets/Settings/PC_RPAsset.asset";

  [MenuItem("Tools/Rendimiento/Activar GPU Resident Drawer (proyecto, una vez)")]
  private static void SetupProject()
  {
    var log = new System.Text.StringBuilder();

    // 1. Shader stripping de BatchRendererGroup: Keep All.
    var graphics = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();

    if (graphics != null)
    {
      var so = new SerializedObject(graphics);
      var brg = so.FindProperty("m_BrgStripping");

      if (brg != null)
      {
        var names = brg.enumNames;
        var keepAll = System.Array.FindIndex(names, n => n.Replace(" ", "").ToLower().Contains("keepall"));

        if (keepAll >= 0)
        {
          brg.enumValueIndex = keepAll;
          so.ApplyModifiedPropertiesWithoutUndo();
          log.AppendLine("BatchRendererGroup Variants: Keep All");
        }
        else
        {
          log.AppendLine("No encontre 'Keep All' en BatchRendererGroup Variants (" + string.Join(", ", names) + "). Ponlo a mano en Project Settings > Graphics.");
        }
      }
    }

    // 2. URP PC: GPU Resident Drawer + oclusion por GPU + sombras.
    var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcAssetPath);

    if (pc != null)
    {
      pc.useSRPBatcher = true;
      pc.gpuResidentDrawerMode = GPUResidentDrawerMode.InstancedDrawing;
      pc.gpuResidentDrawerEnableOcclusionCullingInCameras = true;
      pc.shadowDistance = Mathf.Min(pc.shadowDistance, 35f);
      EditorUtility.SetDirty(pc);
      log.AppendLine("PC_RPAsset: GPU Resident Drawer = Instanced Drawing, oclusion GPU activada, sombras " + pc.shadowDistance + " m");

      // Algunas versiones tienen tambien un interruptor de oclusion en el renderer.
      foreach (var data in GetRenderers(pc))
      {
        var so = new SerializedObject(data);
        var it = so.GetIterator();
        var changed = false;

        while (it.NextVisible(true))
        {
          if (it.propertyType == SerializedPropertyType.Boolean && it.name.ToLower().Contains("gpuocclusion"))
          {
            it.boolValue = true;
            changed = true;
          }
        }

        if (changed)
        {
          so.ApplyModifiedPropertiesWithoutUndo();
          log.AppendLine(data.name + ": GPU Occlusion activada");
        }
      }
    }
    else
    {
      log.AppendLine("No encontre " + PcAssetPath);
    }

    // 3. Static batching apagado en PC.
    if (SetStaticBatching(BuildTarget.StandaloneWindows64, false))
    {
      log.AppendLine("Static batching (PC): apagado");
    }
    else
    {
      log.AppendLine("No pude apagar Static Batching: hazlo en Project Settings > Player > Other Settings.");
    }

    AssetDatabase.SaveAssets();
    EditorUtility.DisplayDialog("Rendimiento", log.ToString(), "OK");
  }

  private static ScriptableRendererData[] GetRenderers(UniversalRenderPipelineAsset asset)
  {
    var field = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
    var list = field != null ? field.GetValue(asset) as ScriptableRendererData[] : null;
    return list != null ? list.Where(r => r != null).ToArray() : new ScriptableRendererData[0];
  }

  private static bool SetStaticBatching(BuildTarget target, bool enabled)
  {
    var method = typeof(PlayerSettings).GetMethod("SetStaticBatchingForPlatform",
      BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

    if (method != null)
    {
      method.Invoke(null, new object[] { target, enabled });
      return true;
    }

    // Plan B: el bloque m_BuildTargetBatching de ProjectSettings.
    var settings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");

    if (settings == null)
    {
      return false;
    }

    var so = new SerializedObject(settings);
    var list = so.FindProperty("m_BuildTargetBatching");

    if (list == null)
    {
      return false;
    }

    for (var i = 0; i < list.arraySize; i++)
    {
      var item = list.GetArrayElementAtIndex(i);

      if (item.FindPropertyRelative("m_BuildTarget").stringValue == "Standalone")
      {
        item.FindPropertyRelative("m_StaticBatching").boolValue = enabled;
        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
      }
    }

    return false;
  }

  [MenuItem("Tools/Rendimiento/Optimizar escena abierta")]
  private static void SetupScene()
  {
    if (EditorApplication.isPlaying)
    {
      EditorUtility.DisplayDialog("Rendimiento", "Sal del modo Play primero.", "OK");
      return;
    }

    var scene = EditorSceneManager.GetActiveScene();
    var camera = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();

    if (camera == null)
    {
      EditorUtility.DisplayDialog("Rendimiento", "No hay camara en la escena.", "OK");
      return;
    }

    if (camera.GetComponent<RenderOptimizer>() == null)
    {
      Undo.AddComponent<RenderOptimizer>(camera.gameObject);
    }

    int enemies = 0, particles = 0, animators = 0;
    var roots = scene.GetRootGameObjects();

    // Enemigos (menos el jefe).
    foreach (var enemy in roots.SelectMany(r => r.GetComponentsInChildren<Enemy>(true)))
    {
      var go = enemy.gameObject;
      if (Excluded(go.transform)) continue;
      if (AddCullable(go, DistanceCullable.Kind.Enemigo, true)) enemies++;
    }

    // Particulas: solo el sistema de arriba de cada grupo.
    foreach (var ps in roots.SelectMany(r => r.GetComponentsInChildren<ParticleSystem>(true)))
    {
      var t = ps.transform;
      if (Excluded(t) || t.parent != null && t.parent.GetComponentInParent<ParticleSystem>(true) != null) continue;
      if (t.GetComponentInParent<DistanceCullable>(true) != null) continue;
      if (AddCullable(ps.gameObject, DistanceCullable.Kind.Decorado, false)) particles++;
    }

    // Animaciones de decorado (banderas, puertas, plantas...).
    foreach (var animator in roots.SelectMany(r => r.GetComponentsInChildren<Animator>(true)))
    {
      var t = animator.transform;
      if (Excluded(t) || t.GetComponentInParent<DistanceCullable>(true) != null) continue;
      if (AddCullable(animator.gameObject, DistanceCullable.Kind.Decorado, false)) animators++;
    }

    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);
    EditorUtility.DisplayDialog("Rendimiento",
      "RenderOptimizer en " + camera.name + ".\n\nDistanceCullable añadido a:\n" +
      "- Enemigos: " + enemies + "\n- Particulas: " + particles + "\n- Animaciones de decorado: " + animators +
      "\n\nAjusta el radio y el terreno en el RenderOptimizer de la camara.", "OK");
  }

  private static bool AddCullable(GameObject go, DistanceCullable.Kind kind, bool moves)
  {
    if (go.GetComponent<DistanceCullable>() != null)
    {
      return false;
    }

    var c = Undo.AddComponent<DistanceCullable>(go);
    var so = new SerializedObject(c);
    so.FindProperty("_kind").enumValueIndex = (int)kind;
    so.FindProperty("_moves").boolValue = moves;
    so.ApplyModifiedPropertiesWithoutUndo();
    return true;
  }

  // Jugador, camara, UI, jefe y efectos del agarre: nunca.
  private static bool Excluded(Transform t)
  {
    return t.GetComponentInParent<Player>(true) != null
        || t.GetComponentInParent<Camera>(true) != null
        || t.GetComponentInParent<Canvas>(true) != null
        || t.GetComponentInParent<BossFight>(true) != null
        || t.GetComponentInParent<RenderOptimizer>(true) != null
        || t.name.Contains("Hand") || t.name.Contains("Mano");
  }
}
