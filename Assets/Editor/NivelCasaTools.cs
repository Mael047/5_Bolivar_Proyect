// NivelCasaTools.cs
// Ajustes de la escena Nivel_CASA:
//   - Colliders: añade MeshCollider a los muebles que no tienen (forma real:
//     se puede pasar por debajo de mesas altas, se choca con patas y sofas).
//     Los objetos muy pequeños (platos, tazas, cartas) se saltan.
//   - Camara: acerca la camara de esta escena (tamaño ortografico).
//   - Paredes: prepara el desvanecido suave de lo que tapa a la nutria.
//
// Menu: Tools > Nivel Casa > ...

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class NivelCasaTools
{
  private const float MinColliderSize = 0.35f; // metros (tras escalar la casa)
  private const string TemplatePath = "Assets/Materials/Casa_Transparente_Ref.mat";

  [MenuItem("Tools/Nivel Casa/Añadir colliders a los muebles")]
  private static void AddColliders()
  {
    var scene = EditorSceneManager.GetActiveScene();
    var house = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Casa_Escala");

    if (house == null)
    {
      EditorUtility.DisplayDialog("Nivel Casa", "No encontre 'Casa_Escala'. Usa primero Tools > Nivel Casa > Escalar casa.", "OK");
      return;
    }

    var added = 0;
    var skipped = 0;

    foreach (var filter in house.GetComponentsInChildren<MeshFilter>(true))
    {
      var go = filter.gameObject;

      if (filter.sharedMesh == null || go.GetComponent<Collider>() != null || go.GetComponent<MeshRenderer>() == null)
      {
        continue;
      }

      var size = go.GetComponent<MeshRenderer>().bounds.size;

      if (Mathf.Max(size.x, size.y, size.z) < MinColliderSize)
      {
        skipped++;
        continue;
      }

      var collider = Undo.AddComponent<MeshCollider>(go);
      collider.sharedMesh = filter.sharedMesh;
      added++;
    }

    // En la build las mallas de colision se preparan al compilar.
    PlayerSettings.bakeCollisionMeshes = true;

    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);
    EditorUtility.DisplayDialog("Nivel Casa", $"Colliders añadidos: {added}\nObjetos pequeños sin collider: {skipped}", "OK");
  }

  [MenuItem("Tools/Nivel Casa/Camara mas cerca")]
  private static void CameraCloser() => SetCameraSize(6f);

  [MenuItem("Tools/Nivel Casa/Camara normal (como el jardin)")]
  private static void CameraNormal() => SetCameraSize(10f);

  private static void SetCameraSize(float size)
  {
    var scene = EditorSceneManager.GetActiveScene();
    var topDown = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<TopDownCamera>(true)).FirstOrDefault(c => c != null);
    var cam = topDown != null ? topDown.GetComponent<Camera>() : null;

    if (cam == null)
    {
      EditorUtility.DisplayDialog("Nivel Casa", "No encontre la camara con TopDownCamera en esta escena.", "OK");
      return;
    }

    Undo.RecordObject(cam, "Camara mas cerca");

    if (cam.orthographic)
    {
      cam.orthographicSize = size;
    }
    else
    {
      cam.fieldOfView = size * 6f;
    }

    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);
    Selection.activeObject = cam.gameObject;
  }

  [MenuItem("Tools/Nivel Casa/Billar: vista cercana del paño")]
  private static void FrameTableOnly()
  {
    var scene = EditorSceneManager.GetActiveScene();
    var focus = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<InteractiveCameraFocus>(true)).FirstOrDefault();

    if (focus == null)
    {
      EditorUtility.DisplayDialog("Nivel Casa", "No encontre InteractiveCameraFocus en la escena.", "OK");
      return;
    }

    // La mesa sin las sillas ni la escupidera: el hijo "mesa" (o el mas grande).
    var table = focus.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != focus.transform && t.name.ToLowerInvariant() == "mesa");

    if (table == null)
    {
      table = focus.GetComponentsInChildren<MeshRenderer>(true)
        .OrderByDescending(r => r.bounds.size.x * r.bounds.size.z)
        .Select(r => r.transform)
        .FirstOrDefault();
    }

    var so = new SerializedObject(focus);
    so.FindProperty("_frameTarget").objectReferenceValue = table;
    so.FindProperty("_padding").floatValue = 0.97f;
    // Como en la referencia: mesa a lo largo (vertical), el paño llena el
    // ancho de la pantalla con las bandas en los bordes izquierdo y derecho.
    so.FindProperty("_fit").enumValueIndex = (int)InteractiveCameraFocus.FitMode.Ancho;
    so.FindProperty("_longSideHorizontal").boolValue = false;
    so.FindProperty("_yawOffset").floatValue = 0f;
    so.ApplyModifiedProperties();

    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);
    Selection.activeObject = focus.gameObject;
  }

  [MenuItem("Tools/Nivel Casa/Minimapa cenital")]
  private static void TopDownMinimap()
  {
    var scene = EditorSceneManager.GetActiveScene();
    var map = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Minimap>(true)).FirstOrDefault();

    if (map == null)
    {
      EditorUtility.DisplayDialog("Nivel Casa", "No encontre el minimapa en esta escena.", "OK");
      return;
    }

    var so = new SerializedObject(map);
    so.FindProperty("_pitch").floatValue = 90f; // mirando recto hacia abajo
    // Recto, con la entrada de la casa (sur, -Z) abajo.
    so.FindProperty("_rotation").enumValueIndex = (int)Minimap.RotationMode.NorteFijo;
    so.FindProperty("_northYaw").floatValue = 0f;
    so.ApplyModifiedProperties();

    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);
    Selection.activeObject = map.gameObject;
  }

  [MenuItem("Tools/Nivel Casa/Mapa: preparar y hornear mapa de la casa")]
  private static void BakeHouseMap()
  {
    var scene = EditorSceneManager.GetActiveScene();
    var house = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Casa_Escala");

    if (house == null)
    {
      EditorUtility.DisplayDialog("Nivel Casa", "No encontre 'Casa_Escala'.", "OK");
      return;
    }

    // 1. Datos propios de la casa (no se toca el mapa del jardin).
    const string folder = "Assets/UI/Mapa/Casa";
    const string dataPath = folder + "/WorldMapData_Casa.asset";
    var data = AssetDatabase.LoadAssetAtPath<WorldMapData>(dataPath);

    if (data == null)
    {
      Directory.CreateDirectory(folder);
      AssetDatabase.Refresh();
      data = ScriptableObject.CreateInstance<WorldMapData>();
      AssetDatabase.CreateAsset(data, dataPath);
      AssetDatabase.SaveAssets();
    }

    // 2. Area del mapa ajustada a la casa.
    var bounds = new Bounds();
    var found = false;

    foreach (var r in house.GetComponentsInChildren<Renderer>())
    {
      if (r is ParticleSystemRenderer) continue;
      if (found) bounds.Encapsulate(r.bounds); else bounds = r.bounds;
      found = true;
    }

    var area = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<MapArea>(true)).FirstOrDefault(a => a != null);

    if (area == null)
    {
      var go = new GameObject("MapArea");
      Undo.RegisterCreatedObjectUndo(go, "Mapa de la casa");
      area = go.AddComponent<MapArea>();
    }

    Undo.RecordObject(area, "Mapa de la casa");
    Undo.RecordObject(area.transform, "Mapa de la casa");
    area.transform.position = bounds.center;
    area.size = new Vector3(bounds.size.x + 2f, bounds.size.y + 4f, bounds.size.z + 2f);
    area.data = data;

    // 3. El menu de pausa de esta escena usa el mapa de la casa.
    foreach (var page in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Menus.WorldMapPage>(true)))
    {
      var so = new SerializedObject(page);
      so.FindProperty("_data").objectReferenceValue = data;
      so.ApplyModifiedProperties();
    }

    // 4. Hornear
    WorldMapBaker.Bake(area);

    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);
    Selection.activeObject = data;
  }

  [MenuItem("Tools/Nivel Casa/Preparar desvanecido de paredes")]
  private static void SetupFade()
  {
    var scene = EditorSceneManager.GetActiveScene();
    var topDown = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<TopDownCamera>(true)).FirstOrDefault(c => c != null);

    if (topDown == null)
    {
      return;
    }

    var hider = topDown.GetComponent<CameraOccluderHider>();
    if (hider == null) hider = Undo.AddComponent<CameraOccluderHider>(topDown.gameObject);

    var so = new SerializedObject(hider);
    so.FindProperty("_transparentTemplate").objectReferenceValue = TransparentTemplate();
    so.ApplyModifiedProperties();

    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);
  }

  /// <summary>Material URP Lit transparente: al estar referenciado, la build conserva esa version del shader.</summary>
  private static Material TransparentTemplate()
  {
    var existing = AssetDatabase.LoadAssetAtPath<Material>(TemplatePath);

    if (existing != null)
    {
      return existing;
    }

    var shader = Shader.Find("Universal Render Pipeline/Lit");

    if (shader == null)
    {
      return null;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(TemplatePath));
    var mat = new Material(shader) { name = "Casa_Transparente_Ref" };
    mat.SetFloat("_Surface", 1f);
    mat.SetFloat("_Blend", 0f);
    mat.SetOverrideTag("RenderType", "Transparent");
    mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
    mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
    mat.SetFloat("_ZWrite", 0f);
    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    mat.renderQueue = (int)RenderQueue.Transparent;
    AssetDatabase.CreateAsset(mat, TemplatePath);
    return mat;
  }
}
