// ToritoSetup.cs
// Deja lista la embestida de los toritos:
//   1. Importa TORO_idle_iniAta y TORITO_embestida como Humanoid con el avatar
//      del torito (si estaban como Generic) y en bucle.
//   2. Crea Torito.controller (copia de Enemy.controller) con los bools
//      "Charge" y "Dash" y los estados "Carga" y "Embestida".
//   3. En los prefabs TORITO_*: asigna ese controlador, añade ChargeAttack y
//      apaga el ataque cuerpo a cuerpo del Enemy.
// Se puede volver a ejecutar: no duplica nada. Cuando tengas la animacion de
// embestida final, reemplaza el FBX TORITO_embestida (mismo nombre) y vuelve a
// ejecutarlo, o arrastra el clip al estado "Embestida" del controlador.
//
// Menu: Tools > Enemigos > Configurar embestida del torito

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class ToritoSetup
{
  private const string ChargeClipFile = "TORO_idle_iniAta";
  private const string DashClipFile = "TORITO_embestida";
  private const string ControllerName = "Torito.controller";

  [MenuItem("Tools/Enemigos/Configurar embestida del torito")]
  private static void Run()
  {
    var prefabs = FindToritoPrefabs();

    if (prefabs.Count == 0)
    {
      EditorUtility.DisplayDialog("Torito", "No encontre prefabs de torito (nombre con 'TORITO' y componente Enemy).", "OK");
      return;
    }

    var firstAnimator = prefabs[0].GetComponentInChildren<Animator>(true);
    var avatar = firstAnimator != null ? firstAnimator.avatar : null;
    var baseController = firstAnimator != null ? firstAnimator.runtimeAnimatorController as AnimatorController : null;

    if (!EditorUtility.DisplayDialog("Torito",
          $"Se configurara la embestida en {prefabs.Count} prefab(s): {string.Join(", ", prefabs.Select(p => p.name))}.\n\n" +
          "- Las animaciones del torito se importaran como Humanoid con su avatar (si no lo estaban).\n" +
          "- Se creara Torito.controller con los estados Carga y Embestida.\n" +
          "- Los toritos usaran ese controlador y el componente ChargeAttack.", "Configurar", "Cancelar"))
    {
      return;
    }

    var chargeClip = PrepareClip(ChargeClipFile, avatar);
    var dashClip = PrepareClip(DashClipFile, avatar);

    var controller = GetOrCreateController(baseController);

    if (controller == null)
    {
      EditorUtility.DisplayDialog("Torito", "No pude crear el controlador (el torito no tiene Animator con controlador).", "OK");
      return;
    }

    SetupController(controller, chargeClip, dashClip);

    foreach (var prefab in prefabs)
    {
      SetupPrefab(prefab, controller);
    }

    AssetDatabase.SaveAssets();
    Selection.activeObject = controller;

    var msg = "Embestida configurada.";
    if (chargeClip == null) msg += $"\n\nNo encontre el clip de {ChargeClipFile}: asignalo a mano al estado 'Carga'.";
    if (dashClip == null) msg += $"\n\nNo encontre el clip de {DashClipFile}: el estado 'Embestida' quedo vacio hasta tener la animacion.";
    EditorUtility.DisplayDialog("Torito", msg, "OK");
  }

  // ---------- Prefabs ----------

  private static List<GameObject> FindToritoPrefabs()
  {
    var list = new List<GameObject>();

    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
    {
      var path = AssetDatabase.GUIDToAssetPath(guid);
      var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);

      if (go != null && go.name.ToUpperInvariant().Contains("TORITO") && go.GetComponent<Enemy>() != null)
      {
        list.Add(go);
      }
    }

    return list;
  }

  private static void SetupPrefab(GameObject prefab, AnimatorController controller)
  {
    var path = AssetDatabase.GetAssetPath(prefab);
    var root = PrefabUtility.LoadPrefabContents(path);

    try
    {
      var animator = root.GetComponentInChildren<Animator>(true);

      if (animator != null)
      {
        animator.runtimeAnimatorController = controller;
      }

      if (root.GetComponent<ChargeAttack>() == null)
      {
        root.AddComponent<ChargeAttack>();
      }

      var enemy = root.GetComponent<Enemy>();

      if (enemy != null)
      {
        enemy.meleeAttack = false;
      }

      PrefabUtility.SaveAsPrefabAsset(root, path);
    }
    finally
    {
      PrefabUtility.UnloadPrefabContents(root);
    }
  }

  // ---------- Clips ----------

  private static string FindModelPath(string fileName)
  {
    foreach (var guid in AssetDatabase.FindAssets(fileName, new[] { "Assets" }))
    {
      var path = AssetDatabase.GUIDToAssetPath(guid);

      if (Path.GetFileNameWithoutExtension(path) == fileName && path.ToLowerInvariant().EndsWith(".fbx"))
      {
        return path;
      }
    }

    return null;
  }

  private static AnimationClip PrepareClip(string fileName, Avatar avatar)
  {
    var path = FindModelPath(fileName);

    if (path == null)
    {
      return null;
    }

    var importer = AssetImporter.GetAtPath(path) as ModelImporter;

    if (importer != null)
    {
      var changed = false;

      if (avatar != null && avatar.isHuman && importer.animationType != ModelImporterAnimationType.Human)
      {
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = avatar;
        changed = true;
      }

      var clips = importer.clipAnimations;

      if (clips == null || clips.Length == 0)
      {
        clips = importer.defaultClipAnimations;
      }

      foreach (var clip in clips)
      {
        if (!clip.loopTime)
        {
          clip.loopTime = true;
          changed = true;
        }
      }

      if (changed)
      {
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
      }
    }

    return AssetDatabase.LoadAllAssetsAtPath(path)
      .OfType<AnimationClip>()
      .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
  }

  // ---------- Controlador ----------

  private static AnimatorController GetOrCreateController(AnimatorController baseController)
  {
    var folder = baseController != null
      ? Path.GetDirectoryName(AssetDatabase.GetAssetPath(baseController)).Replace('\\', '/')
      : "Assets/Characters/Enemys/EnemysAnimations";
    var path = $"{folder}/{ControllerName}";

    var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);

    if (existing != null)
    {
      return existing;
    }

    if (baseController != null && baseController.name != Path.GetFileNameWithoutExtension(ControllerName))
    {
      AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(baseController), path);
      return AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
    }

    return baseController;
  }

  private static void SetupController(AnimatorController controller, AnimationClip chargeClip, AnimationClip dashClip)
  {
    AddBool(controller, "Charge");
    AddBool(controller, "Dash");

    var sm = controller.layers[0].stateMachine;
    var idle = sm.defaultState;

    var carga = FindState(sm, "Carga");

    if (carga == null)
    {
      carga = sm.AddState("Carga", new Vector3(300f, -120f, 0f));

      var toCarga = sm.AddAnyStateTransition(carga);
      toCarga.hasExitTime = false;
      toCarga.duration = 0.15f;
      toCarga.canTransitionToSelf = false;
      toCarga.AddCondition(AnimatorConditionMode.If, 0f, "Charge");
      toCarga.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dash");

      if (idle != null)
      {
        var back = carga.AddTransition(idle);
        back.hasExitTime = false;
        back.duration = 0.2f;
        back.AddCondition(AnimatorConditionMode.IfNot, 0f, "Charge");
        back.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dash");
      }
    }

    var embestida = FindState(sm, "Embestida");

    if (embestida == null)
    {
      embestida = sm.AddState("Embestida", new Vector3(560f, -120f, 0f));

      var toDash = carga.AddTransition(embestida);
      toDash.hasExitTime = false;
      toDash.duration = 0.08f;
      toDash.AddCondition(AnimatorConditionMode.If, 0f, "Dash");

      if (idle != null)
      {
        var back = embestida.AddTransition(idle);
        back.hasExitTime = false;
        back.duration = 0.2f;
        back.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dash");
      }
    }

    if (chargeClip != null) carga.motion = chargeClip;
    if (dashClip != null) embestida.motion = dashClip;

    EditorUtility.SetDirty(controller);
  }

  private static void AddBool(AnimatorController controller, string name)
  {
    if (controller.parameters.All(p => p.name != name))
    {
      controller.AddParameter(name, AnimatorControllerParameterType.Bool);
    }
  }

  private static AnimatorState FindState(AnimatorStateMachine sm, string name)
  {
    foreach (var child in sm.states)
    {
      if (child.state.name == name)
      {
        return child.state;
      }
    }

    return null;
  }
}
