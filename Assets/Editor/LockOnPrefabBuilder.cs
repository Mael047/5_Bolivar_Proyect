// LockOnPrefabBuilder.cs
// Crea el prefab por defecto del marcador de lock-on
// (Assets/UI/Combat/LockOnMarkerUI) si no existe ninguno en el proyecto, y la
// opcion Tools > Lock-on > Agregar indicador al Player, que añade
// LockOnIndicator al prefab del jugador (el que tiene PlayerLockOn) y le
// asigna el marcador. Despues el prefab se diseña libremente (doble clic).

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class LockOnPrefabBuilder
{
  private const string Folder = "Assets/UI/Combat";
  private const string PrefabName = "LockOnMarkerUI";

  static LockOnPrefabBuilder()
  {
    EditorApplication.delayCall += () =>
    {
      if (!EditorApplication.isPlayingOrWillChangePlaymode && FindPrefab<LockOnMarkerView>(PrefabName) == null)
      {
        Save(Build());
        Debug.Log($"[Lock-on] Prefab del marcador creado en {Folder}/{PrefabName}. Usa Tools > Lock-on > Agregar indicador al Player.");
      }
    };
  }

  [MenuItem("Tools/Lock-on/Agregar indicador al Player")]
  private static void AddToPlayer()
  {
    var marker = FindPrefab<LockOnMarkerView>(PrefabName);

    if (marker == null)
    {
      marker = Save(Build()).GetComponent<LockOnMarkerView>();
    }

    var player = FindPrefab<PlayerLockOn>("Player");

    if (player == null)
    {
      EditorUtility.DisplayDialog("Lock-on", "No encontre ningun prefab con PlayerLockOn.", "OK");
      return;
    }

    var path = AssetDatabase.GetAssetPath(player.gameObject);
    var root = PrefabUtility.LoadPrefabContents(path);

    try
    {
      var lockOn = root.GetComponentInChildren<PlayerLockOn>(true);
      var indicator = lockOn.GetComponent<LockOnIndicator>();

      if (indicator == null)
      {
        indicator = lockOn.gameObject.AddComponent<LockOnIndicator>();
      }

      var so = new SerializedObject(indicator);
      so.FindProperty("_markerPrefab").objectReferenceValue = marker;
      so.ApplyModifiedPropertiesWithoutUndo();

      PrefabUtility.SaveAsPrefabAsset(root, path);
    }
    finally
    {
      PrefabUtility.UnloadPrefabContents(root);
    }

    Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    EditorUtility.DisplayDialog("Lock-on", $"LockOnIndicator agregado a {path}.", "OK");
  }

  [MenuItem("Tools/Lock-on/Seleccionar prefab del marcador")]
  private static void SelectMarker()
  {
    var marker = FindPrefab<LockOnMarkerView>(PrefabName);

    if (marker != null)
    {
      Selection.activeObject = marker.gameObject;
      EditorGUIUtility.PingObject(marker.gameObject);
    }
  }

  private static T FindPrefab<T>(string preferredName) where T : Component
  {
    T fallback = null;

    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
    {
      var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
      var component = go != null ? go.GetComponent<T>() : null;

      if (component == null)
      {
        continue;
      }

      if (go.name == preferredName)
      {
        return component;
      }

      fallback ??= component;
    }

    return fallback;
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
  // Rombo pequeño flotando sobre el enemigo. Al fijar: cambia de color y
  // aparece un marco en rombo que gira alrededor.

  private static GameObject Build()
  {
    var root = new GameObject(PrefabName, typeof(RectTransform));

    var canvas = root.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = 300;

    var scaler = root.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920f, 1080f);
    scaler.matchWidthOrHeight = 0.5f;

    var group = root.AddComponent<CanvasGroup>();
    group.alpha = 1f;
    group.blocksRaycasts = false;
    group.interactable = false;

    var view = root.AddComponent<LockOnMarkerView>();

    var marker = CreateRect("Marker", root.transform, new Vector2(70f, 70f));
    marker.pivot = new Vector2(0.5f, 0f);

    var bob = CreateRect("Bob", marker, new Vector2(70f, 70f));
    var punch = CreateRect("Punch", bob, new Vector2(70f, 70f));

    // Marco que gira (solo fijado): cuadrado hueco rotado 45 grados.
    var ring = CreateRect("Ring", punch, new Vector2(38f, 38f));
    ring.localRotation = Quaternion.Euler(0f, 0f, 45f);

    var bars = new[]
    {
      Bar("Top", ring, new Vector2(0.5f, 1f), new Vector2(38f, 3f)),
      Bar("Bottom", ring, new Vector2(0.5f, 0f), new Vector2(38f, 3f)),
      Bar("Left", ring, new Vector2(0f, 0.5f), new Vector2(3f, 38f)),
      Bar("Right", ring, new Vector2(1f, 0.5f), new Vector2(3f, 38f))
    };

    // Rombo central (siempre).
    var diamondRect = CreateRect("Diamond", punch, new Vector2(16f, 16f));
    diamondRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
    var diamond = diamondRect.gameObject.AddComponent<Image>();
    diamond.raycastTarget = false;
    var outline = diamond.gameObject.AddComponent<Outline>();
    outline.effectColor = new Color(0f, 0f, 0f, 0.5f);
    outline.effectDistance = new Vector2(1.5f, -1.5f);

    view.followRoot = marker;
    view.bob = bob;
    view.punch = punch;
    view.spinWhenLocked = ring;
    view.lockedOnly = new[] { ring.gameObject };
    view.candidateOnly = new GameObject[0];
    view.tinted = new Graphic[] { diamond, bars[0], bars[1], bars[2], bars[3] };

    return root;
  }

  private static Image Bar(string name, RectTransform parent, Vector2 anchor, Vector2 size)
  {
    var rect = CreateRect(name, parent, size);
    rect.anchorMin = rect.anchorMax = anchor;
    rect.anchoredPosition = Vector2.zero;
    var image = rect.gameObject.AddComponent<Image>();
    image.raycastTarget = false;
    image.color = new Color(1f, 1f, 1f, 0.9f);
    return image;
  }

  private static RectTransform CreateRect(string name, Transform parent, Vector2 size)
  {
    var go = new GameObject(name, typeof(RectTransform));
    go.transform.SetParent(parent, false);
    var rect = (RectTransform)go.transform;
    rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
    rect.pivot = new Vector2(0.5f, 0.5f);
    rect.sizeDelta = size;
    rect.anchoredPosition = Vector2.zero;
    return rect;
  }
}
