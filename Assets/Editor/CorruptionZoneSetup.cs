// CorruptionZoneSetup.cs
// Tools > Zona corrupta > Preparar proyecto (una vez)
//   - Activa en los URP Asset: Depth Texture y Opaque Texture (sin reducir
//     resolucion), que el shader necesita para ver y distorsionar la escena.
//   - Añade a cada Renderer un Renderer Feature "Zona corrupta: personajes
//     encima" (Render Objects) que vuelve a dibujar despues de la zona las
//     capas Player, Enemy, Grabbable (cajas), Fragmentos y SinCorrupcion
//     (letreros y objetos con CorruptionExempt): esos se ven normales.
//   - Nombra las capas 12 "Fragmentos" y 13 "SinCorrupcion" si estan libres.
//   - Crea el material Assets/Materials/ZonaCorrupta.mat.
//
// Tools > Zona corrupta > Crear zona corrupta (y GameObject > Zona corrupta)
//   - Crea un cubo con CorruptionZone y el material, delante de la vista de
//     la escena. Escalalo y muevelo para cubrir la zona.

using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class CorruptionZoneSetup
{
  private const string MaterialPath = "Assets/Materials/ZonaCorrupta.mat";
  private const string ShaderName = "ReQuinta/ZonaCorrupta";
  private const string FeatureName = "Zona corrupta: personajes encima";

  [MenuItem("Tools/Zona corrupta/Preparar proyecto (una vez)")]
  private static void Setup()
  {
    var log = new System.Text.StringBuilder();

    // 1. URP Assets
    foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
    {
      var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
      if (asset == null) continue;

      var so = new SerializedObject(asset);
      Set(so, "m_RequireDepthTexture", true);
      Set(so, "m_RequireOpaqueTexture", true);
      var down = so.FindProperty("m_OpaqueDownsampling");
      if (down != null) down.enumValueIndex = 0; // sin reducir: la zona se ve nitida
      so.ApplyModifiedProperties();
      EditorUtility.SetDirty(asset);
      log.AppendLine("URP: " + asset.name + " (Depth + Opaque Texture)");
    }

    // 2. Capas con nombre (si estan libres)
    NameLayer(12, "Fragmentos", log);
    NameLayer(13, "SinCorrupcion", log);
    var cleanMask = (1 << 3) | (1 << 8) | (1 << 10) | (1 << 12) | (1 << 13); // Player, Grabbable, Enemy, Fragmentos, SinCorrupcion

    // 3. Renderer Feature en cada Renderer
    var renderObjectsType = TypeCache.GetTypesDerivedFrom<ScriptableRendererFeature>().FirstOrDefault(t => t.Name == "RenderObjects");

    foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
    {
      var path = AssetDatabase.GUIDToAssetPath(guid);
      var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
      if (data == null) continue;

      var current = data.rendererFeatures.FirstOrDefault(f => f != null && f.name == FeatureName);

      if (current != null)
      {
        var cso = new SerializedObject(current);
        var currentMask = cso.FindProperty("settings.filterSettings.LayerMask");
        if (currentMask != null) currentMask.intValue = cleanMask;
        cso.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(current);
        EditorUtility.SetDirty(data);
        log.AppendLine("Renderer: " + data.name + " (feature actualizado: cajas y letreros sin corrupcion)");
        continue;
      }

      if (renderObjectsType == null)
      {
        log.AppendLine("No encontre Render Objects en URP: añadelo a mano (ver abajo).");
        break;
      }

      var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(renderObjectsType);
      feature.name = FeatureName;

      var fso = new SerializedObject(feature);
      SetEnum(fso, "settings.Event", (int)RenderPassEvent.AfterRenderingTransparents);
      var mask = fso.FindProperty("settings.filterSettings.LayerMask");
      if (mask != null) mask.intValue = cleanMask;
      SetEnum(fso, "settings.filterSettings.RenderQueueType", 0); // Opaque
      fso.ApplyModifiedPropertiesWithoutUndo();

      AssetDatabase.AddObjectToAsset(feature, data);
      data.rendererFeatures.Add(feature);

      // Mapa interno de features (si no, Unity no lo guarda bien).
      var dso = new SerializedObject(data);
      var map = dso.FindProperty("m_RendererFeatureMap");

      if (map != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId))
      {
        map.arraySize++;
        map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
      }

      dso.ApplyModifiedPropertiesWithoutUndo();
      data.SetDirty();
      EditorUtility.SetDirty(data);
      log.AppendLine("Renderer: " + data.name + " (+ " + FeatureName + ")");
    }

    // 3. Material
    var material = GetOrCreateMaterial();
    log.AppendLine(material != null ? "Material: " + MaterialPath : "No encontre el shader " + ShaderName);

    AssetDatabase.SaveAssets();
    EditorUtility.DisplayDialog("Zona corrupta", log.ToString(), "OK");
  }

  [MenuItem("Tools/Zona corrupta/Crear zona corrupta")]
  [MenuItem("GameObject/Zona corrupta", false, 10)]
  private static void CreateZone()
  {
    var material = GetOrCreateMaterial();

    if (material == null)
    {
      EditorUtility.DisplayDialog("Zona corrupta", "No encontre el shader " + ShaderName + ".", "OK");
      return;
    }

    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
    go.name = "ZonaCorrupta";
    go.layer = 2; // Ignore Raycast
    Object.DestroyImmediate(go.GetComponent<Collider>());
    go.GetComponent<MeshRenderer>().sharedMaterial = material;
    go.AddComponent<CorruptionZone>();

    var view = SceneView.lastActiveSceneView;
    var center = view != null ? view.pivot : Vector3.zero;
    go.transform.position = center;
    go.transform.localScale = new Vector3(12f, 6f, 12f);

    if (Selection.activeTransform != null && Selection.activeTransform.gameObject.scene.IsValid())
    {
      // Junto a lo seleccionado, en la misma escena.
      UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, Selection.activeTransform.gameObject.scene);
    }

    Undo.RegisterCreatedObjectUndo(go, "Crear zona corrupta");
    Selection.activeObject = go;
  }

  private static void NameLayer(int index, string name, System.Text.StringBuilder log)
  {
    var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
    if (assets == null || assets.Length == 0) return;

    var tm = new SerializedObject(assets[0]);
    var layers = tm.FindProperty("layers");
    if (layers == null || index >= layers.arraySize) return;

    var element = layers.GetArrayElementAtIndex(index);

    if (string.IsNullOrEmpty(element.stringValue))
    {
      element.stringValue = name;
      tm.ApplyModifiedProperties();
      log.AppendLine("Capa " + index + ": " + name);
    }
    else if (element.stringValue != name)
    {
      log.AppendLine("Ojo: la capa " + index + " ya se llama '" + element.stringValue + "' (se esperaba " + name + ").");
    }
  }

  private static Material GetOrCreateMaterial()
  {
    var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
    if (existing != null) return existing;

    var shader = Shader.Find(ShaderName);
    if (shader == null) return null;

    System.IO.Directory.CreateDirectory("Assets/Materials");
    var mat = new Material(shader) { name = "ZonaCorrupta" };
    AssetDatabase.CreateAsset(mat, MaterialPath);
    return mat;
  }

  private static void Set(SerializedObject so, string prop, bool value)
  {
    var p = so.FindProperty(prop);
    if (p != null) p.boolValue = value;
  }

  private static void SetEnum(SerializedObject so, string prop, int value)
  {
    var p = so.FindProperty(prop);
    if (p == null) return;
    if (p.propertyType == SerializedPropertyType.Enum) p.intValue = value;
    else p.intValue = value;
  }
}
