// PlayerEquipmentSetup.cs
// Tools > Equipamiento > Agregar PlayerEquipment al Player: añade el componente
// al prefab del jugador (el que tiene Player) y le asigna como partes visibles
// los objetos 'Espada' y 'escudo'. Desde ese momento el jugador empieza sin
// espada ni escudo (marca 'Start With...' en el componente para probar).

using UnityEditor;
using UnityEngine;

public static class PlayerEquipmentSetup
{
  [MenuItem("Tools/Equipamiento/Agregar PlayerEquipment al Player")]
  private static void AddToPlayer()
  {
    string path = null;

    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
    {
      var p = AssetDatabase.GUIDToAssetPath(guid);
      var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);

      if (go != null && go.GetComponent<Player>() != null)
      {
        path = p;

        if (go.name == "Player")
        {
          break;
        }
      }
    }

    if (path == null)
    {
      EditorUtility.DisplayDialog("Equipamiento", "No encontre ningun prefab con el componente Player.", "OK");
      return;
    }

    var root = PrefabUtility.LoadPrefabContents(path);

    try
    {
      var player = root.GetComponentInChildren<Player>(true);
      var equipment = player.GetComponent<PlayerEquipment>();

      if (equipment == null)
      {
        equipment = player.gameObject.AddComponent<PlayerEquipment>();
      }

      var so = new SerializedObject(equipment);
      Assign(so.FindProperty("_swordVisuals"), Find(root.transform, "Espada"));
      Assign(so.FindProperty("_shieldVisuals"), Find(root.transform, "escudo"));
      so.ApplyModifiedPropertiesWithoutUndo();

      PrefabUtility.SaveAsPrefabAsset(root, path);
    }
    finally
    {
      PrefabUtility.UnloadPrefabContents(root);
    }

    Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    EditorUtility.DisplayDialog("Equipamiento",
      $"PlayerEquipment agregado a {path}.\n\nEl jugador empieza sin espada ni escudo. Para probar el combate marca 'Start With Sword' / 'Start With Shield'.", "OK");
  }

  private static void Assign(SerializedProperty array, GameObject go)
  {
    if (go == null || array.arraySize > 0)
    {
      return;
    }

    array.arraySize = 1;
    array.GetArrayElementAtIndex(0).objectReferenceValue = go;
  }

  private static GameObject Find(Transform root, string name)
  {
    foreach (var t in root.GetComponentsInChildren<Transform>(true))
    {
      if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase))
      {
        return t.gameObject;
      }
    }

    return null;
  }
}
