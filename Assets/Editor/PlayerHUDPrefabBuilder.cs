// PlayerHUDPrefabBuilder.cs
// Crea el prefab por defecto del HUD del jugador (Assets/UI/Player/PlayerHUD):
// retrato 3D a la izquierda y las hojas de vida a la derecha, arriba a la
// izquierda de la pantalla. Usa los sprites Hoja_llena / Hoja_media /
// Hoja_vacia que esten en el proyecto.
// Menu: Tools > HUD > Agregar HUD del jugador a la escena abierta.
// Despues el prefab se diseña libremente (doble clic).

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class PlayerHUDPrefabBuilder
{
  private const string Folder = "Assets/UI/Player";
  private const string PrefabName = "PlayerHUD";

  static PlayerHUDPrefabBuilder()
  {
    EditorApplication.delayCall += () =>
    {
      if (!EditorApplication.isPlayingOrWillChangePlaymode && FindPrefab() == null)
      {
        Save(Build());
        Debug.Log($"[HUD] Prefab creado en {Folder}/{PrefabName}. Usa Tools > HUD > Agregar HUD del jugador a la escena abierta.");
      }
    };
  }

  [MenuItem("Tools/HUD/Agregar HUD del jugador a la escena abierta")]
  private static void AddToScene()
  {
    if (Object.FindAnyObjectByType<PlayerHealthHUD>(FindObjectsInactive.Include) != null)
    {
      EditorUtility.DisplayDialog("HUD", "La escena ya tiene un PlayerHealthHUD.", "OK");
      return;
    }

    var prefab = FindPrefab();

    if (prefab == null)
    {
      prefab = Save(Build()).GetComponent<PlayerHealthHUD>();
    }

    AssignMissingSprites(prefab);

    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject);
    Undo.RegisterCreatedObjectUndo(instance, "Agregar HUD del jugador");
    EditorSceneManager.MarkSceneDirty(instance.scene);
    Selection.activeObject = instance;
  }

  [MenuItem("Tools/HUD/Seleccionar prefab del HUD")]
  private static void SelectPrefab()
  {
    var prefab = FindPrefab();

    if (prefab != null)
    {
      AssignMissingSprites(prefab);
      Selection.activeObject = prefab.gameObject;
      EditorGUIUtility.PingObject(prefab.gameObject);
    }
  }

  private static PlayerHealthHUD FindPrefab()
  {
    PlayerHealthHUD fallback = null;

    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
    {
      var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
      var hud = go != null ? go.GetComponent<PlayerHealthHUD>() : null;

      if (hud == null)
      {
        continue;
      }

      if (go.name == PrefabName)
      {
        return hud;
      }

      fallback ??= hud;
    }

    return fallback;
  }

  private static Sprite FindSprite(string name)
  {
    foreach (var guid in AssetDatabase.FindAssets($"{name} t:Sprite", new[] { "Assets" }))
    {
      var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid));

      if (sprite != null && sprite.name == name)
      {
        return sprite;
      }
    }

    return null;
  }

  /// <summary>Si el prefab se creo antes de que las hojas se importaran como Sprite, se completan ahora.</summary>
  private static void AssignMissingSprites(PlayerHealthHUD prefab)
  {
    var so = new SerializedObject(prefab);
    var changed = false;
    changed |= Fill(so, "_fullSprite", "Hoja_llena");
    changed |= Fill(so, "_halfSprite", "Hoja_media");
    changed |= Fill(so, "_emptySprite", "Hoja_vacia");

    var template = so.FindProperty("_leafTemplate").objectReferenceValue as Image;

    if (template != null && template.sprite == null)
    {
      var leafSo = new SerializedObject(template);
      leafSo.FindProperty("m_Sprite").objectReferenceValue = FindSprite("Hoja_llena");
      leafSo.ApplyModifiedPropertiesWithoutUndo();
      changed = true;
    }

    if (changed)
    {
      so.ApplyModifiedPropertiesWithoutUndo();
      EditorUtility.SetDirty(prefab.gameObject);
      AssetDatabase.SaveAssets();
    }
  }

  private static bool Fill(SerializedObject so, string property, string spriteName)
  {
    var prop = so.FindProperty(property);

    if (prop.objectReferenceValue != null)
    {
      return false;
    }

    prop.objectReferenceValue = FindSprite(spriteName);
    return prop.objectReferenceValue != null;
  }

  private static GameObject Save(GameObject root)
  {
    EnsureFolder(Folder);
    var path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{PrefabName}.prefab");
    var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
    Object.DestroyImmediate(root);
    return prefab;
  }

  private static void EnsureFolder(string folder)
  {
    if (AssetDatabase.IsValidFolder(folder))
    {
      return;
    }

    var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
    EnsureFolder(parent);
    AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
  }

  // ---------- Prefab por defecto ----------

  private static GameObject Build()
  {
    var root = new GameObject(PrefabName, typeof(RectTransform));

    var canvas = root.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = 100;

    var scaler = root.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920f, 1080f);
    scaler.matchWidthOrHeight = 0.5f;

    var group = root.AddComponent<CanvasGroup>();
    group.blocksRaycasts = false;
    group.interactable = false;

    var hud = root.AddComponent<PlayerHealthHUD>();

    // Panel arriba a la izquierda
    var panel = Rect("Panel", root.transform);
    panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
    panel.pivot = new Vector2(0f, 1f);
    panel.anchoredPosition = new Vector2(36f, -28f);
    panel.sizeDelta = new Vector2(640f, 190f);

    // Fondo del retrato: reemplazalo por tu sprite.
    var portraitBg = Rect("PortraitBackground", panel);
    portraitBg.anchorMin = portraitBg.anchorMax = new Vector2(0f, 1f);
    portraitBg.pivot = new Vector2(0f, 1f);
    portraitBg.anchoredPosition = Vector2.zero;
    portraitBg.sizeDelta = new Vector2(180f, 180f);
    var bgImage = portraitBg.gameObject.AddComponent<Image>();
    bgImage.color = new Color(0f, 0f, 0f, 0.35f);
    bgImage.raycastTarget = false;

    var portrait = Rect("Portrait", portraitBg);
    portrait.anchorMin = Vector2.zero;
    portrait.anchorMax = Vector2.one;
    portrait.offsetMin = Vector2.zero;
    portrait.offsetMax = Vector2.zero;
    var raw = portrait.gameObject.AddComponent<RawImage>();
    raw.raycastTarget = false;
    portrait.gameObject.AddComponent<PlayerPortrait>();

    // Hojas
    var leaves = Rect("Leaves", panel);
    leaves.anchorMin = leaves.anchorMax = new Vector2(0f, 1f);
    leaves.pivot = new Vector2(0f, 0.5f);
    leaves.anchoredPosition = new Vector2(196f, -64f);
    leaves.sizeDelta = new Vector2(440f, 84f);

    var layout = leaves.gameObject.AddComponent<HorizontalLayoutGroup>();
    layout.spacing = 4f;
    layout.childAlignment = TextAnchor.MiddleLeft;
    layout.childControlWidth = true;
    layout.childControlHeight = true;
    layout.childForceExpandWidth = false;
    layout.childForceExpandHeight = false;

    var leaf = Rect("Leaf", leaves);
    var leafImage = leaf.gameObject.AddComponent<Image>();
    leafImage.sprite = FindSprite("Hoja_llena");
    leafImage.preserveAspect = true;
    leafImage.raycastTarget = false;
    var element = leaf.gameObject.AddComponent<LayoutElement>();
    element.preferredWidth = 78f;
    element.preferredHeight = 78f;

    var so = new SerializedObject(hud);
    so.FindProperty("_leafTemplate").objectReferenceValue = leafImage;
    so.FindProperty("_fullSprite").objectReferenceValue = FindSprite("Hoja_llena");
    so.FindProperty("_halfSprite").objectReferenceValue = FindSprite("Hoja_media");
    so.FindProperty("_emptySprite").objectReferenceValue = FindSprite("Hoja_vacia");
    so.ApplyModifiedPropertiesWithoutUndo();

    return root;
  }

  private static RectTransform Rect(string name, Transform parent)
  {
    var go = new GameObject(name, typeof(RectTransform));
    go.transform.SetParent(parent, false);
    return (RectTransform)go.transform;
  }
}
