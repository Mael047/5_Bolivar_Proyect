// KeyHUDSetup.cs
// Tools > HUD > Agregar llave al HUD
// Pone Assets/UI/llave.png y llave_dorada.png como Sprite y crea el objeto
// "Llave" en el prefab PlayerHUD, a la derecha de las hojas de vida, con
// KeyIndicatorHUD ya conectado. Si ya existe, solo lo vuelve a conectar.

using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class KeyHUDSetup
{
  private const string HudPath = "Assets/UI/Player/PlayerHUD.prefab";
  private const string KeyPath = "Assets/UI/llave.png";
  private const string GoldPath = "Assets/UI/llave_dorada.png";

  [MenuItem("Tools/HUD/Agregar llave al HUD")]
  private static void Run()
  {
    if (EditorApplication.isPlaying)
    {
      EditorUtility.DisplayDialog("HUD", "Sal del modo Play primero.", "OK");
      return;
    }

    var empty = MakeSprite(KeyPath);
    var gold = MakeSprite(GoldPath);

    if (empty == null)
    {
      EditorUtility.DisplayDialog("HUD", "No encuentro " + KeyPath, "OK");
      return;
    }

    var root = PrefabUtility.LoadPrefabContents(HudPath);

    try
    {
      var leaves = root.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == "Leaves");

      if (leaves == null)
      {
        EditorUtility.DisplayDialog("HUD", "No encuentro el objeto 'Leaves' (hojas de vida) en PlayerHUD.", "OK");
        return;
      }

      var parent = leaves.parent;
      var keyTransform = parent.Find("Llave");
      Image image;

      if (keyTransform == null)
      {
        var go = new GameObject("Llave", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        go.transform.SetSiblingIndex(leaves.GetSiblingIndex() + 1);
        image = go.GetComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = leaves.anchorMin;
        rt.pivot = new Vector2(0f, 0.5f);
        var height = leaves.sizeDelta.y * 0.9f;
        rt.sizeDelta = new Vector2(height * empty.rect.width / empty.rect.height, height);
        // Hojas por defecto: 5 x 78 + 4 de espacio. En juego se recoloca sola.
        rt.anchoredPosition = new Vector2(leaves.anchoredPosition.x + 406f + 125f, leaves.anchoredPosition.y);
        keyTransform = rt;
      }
      else
      {
        image = keyTransform.GetComponent<Image>();
      }

      image.sprite = empty;
      image.color = Color.white;

      var indicator = keyTransform.GetComponent<KeyIndicatorHUD>();
      if (indicator == null) indicator = keyTransform.gameObject.AddComponent<KeyIndicatorHUD>();

      var so = new SerializedObject(indicator);
      so.FindProperty("_image").objectReferenceValue = image;
      so.FindProperty("_emptySprite").objectReferenceValue = empty;
      so.FindProperty("_goldSprite").objectReferenceValue = gold;
      so.FindProperty("_placeAfter").objectReferenceValue = leaves;
      so.ApplyModifiedPropertiesWithoutUndo();

      PrefabUtility.SaveAsPrefabAsset(root, HudPath);
    }
    finally
    {
      PrefabUtility.UnloadPrefabContents(root);
    }

    EditorUtility.DisplayDialog("HUD", "Llave agregada al HUD (PlayerHUD > Llave).\nGris sin llave, dorada al recoger una.", "OK");
    Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
  }

  private static Sprite MakeSprite(string path)
  {
    var importer = AssetImporter.GetAtPath(path) as TextureImporter;

    if (importer == null)
    {
      return null;
    }

    if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single || importer.mipmapEnabled)
    {
      importer.textureType = TextureImporterType.Sprite;
      importer.spriteImportMode = SpriteImportMode.Single;
      importer.mipmapEnabled = false;
      importer.alphaIsTransparency = true;
      importer.SaveAndReimport();
    }

    return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
  }
}
