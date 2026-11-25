// BossSetup.cs
// Tools > Jefe > Preparar jefe y tentaculos
//
// Los FBX de Assets/Characters/Boss son de dos tipos:
//   - Idle_stand / appear / chargeAttack / damage / defeat: el CUERPO del
//     jefe (malla pCube5 + rig). En esos archivos tambien vienen 3
//     tentaculos puestos en fila al lado, pero quietos (no los anima).
//   - *_tentacle: un TENTACULO suelto con su propio rig y su animacion.
// Por eso las animaciones de tentaculo no movian los tentaculos del cuerpo:
// son rigs distintos. Jefe y tentaculo son dos personajes separados.
//
// Esta herramienta:
//   1. Ajusta la importacion: sin camaras ni luces, Generic, nombres claros
//      y "Loop" en las animaciones de reposo.
//   2. Crea los Animator Controller:
//        Jefe_Cuerpo.controller   (Quieto, Aparecer, AtaqueCargado, Danio, Derrota)
//        Jefe_Tentaculo.controller (Quieto, Aparecer, Ataque1-3, Cansado, Derrota)
//   3. Crea los prefabs Jefe.prefab (solo el cuerpo) y Tentaculo.prefab.
//
// Ojo con el rig: las mallas estan pesadas al esqueleto simple de Blender
// ("metarig" en el cuerpo, "Armature" en el tentaculo), pero las animaciones
// mueven el rig de Rigify ("rig" / "RIG-Armature"). Por eso la malla no se
// movia (y al quitar el metarig, desaparecia). La herramienta engancha cada
// hueso de la malla a su hueso DEF- del rig animado (DEF-spine, DEF-Bone...)
// y guarda la malla corregida en Boss/Meshes.
//
// Si un controller ya existe, no lo toca (borralo para recrearlo). Los
// prefabs se rehacen siempre (las escenas que los usan se mantienen).

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class BossSetup
{
  private const string Folder = "Assets/Characters/Boss";

  // archivo -> (nombre del clip, loop)
  private static readonly Dictionary<string, (string clip, bool loop)> Clips = new Dictionary<string, (string, bool)>
  {
    { "Idle_stand", ("Jefe_Quieto", true) },
    { "Idle_appear", ("Jefe_Aparecer", false) },
    { "Idle_chargeAttack", ("Jefe_AtaqueCargado", false) },
    { "Idle_damage", ("Jefe_Danio", false) },
    { "Idle_defeat", ("Jefe_Derrota", false) },
    { "Idle_static_tentacle", ("Tentaculo_Quieto", true) },
    { "Idle_appear_tentacle", ("Tentaculo_Aparecer", false) },
    { "Idle_attack1_tentacle", ("Tentaculo_Ataque1", false) },
    { "Idle_attack2_tentacle", ("Tentaculo_Ataque2", false) },
    { "Idle_attack3_tentacle", ("Tentaculo_Ataque3", false) },
    { "Idle_tired_tentacle", ("Tentaculo_Cansado", true) },
    { "Idle_defeat_tentacle", ("Tentaculo_Derrota", false) },
  };

  [MenuItem("Tools/Jefe/Preparar jefe y tentaculos")]
  private static void Run()
  {
    var log = new System.Text.StringBuilder();

    // 1. Importacion
    foreach (var pair in Clips)
    {
      var path = Folder + "/" + pair.Key + ".fbx";
      var importer = AssetImporter.GetAtPath(path) as ModelImporter;

      if (importer == null)
      {
        log.AppendLine("Falta " + path);
        continue;
      }

      importer.importCameras = false;
      importer.importLights = false;
      importer.animationType = ModelImporterAnimationType.Generic;
      importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
      importer.importAnimation = true;

      var clips = importer.defaultClipAnimations;

      foreach (var c in clips)
      {
        c.name = pair.Value.clip;
        c.loopTime = pair.Value.loop;
      }

      importer.clipAnimations = clips;
      importer.SaveAndReimport();
    }

    // 2. Controllers
    var body = Controller(Folder + "/Jefe_Cuerpo.controller", "Jefe_Quieto",
      new[] { ("Aparecer", "Jefe_Aparecer"), ("AtaqueCargado", "Jefe_AtaqueCargado"), ("Danio", "Jefe_Danio") },
      ("Derrota", "Jefe_Derrota"), null, log);

    var tentacle = Controller(Folder + "/Jefe_Tentaculo.controller", "Tentaculo_Quieto",
      new[] { ("Aparecer", "Tentaculo_Aparecer"), ("Ataque1", "Tentaculo_Ataque1"), ("Ataque2", "Tentaculo_Ataque2"), ("Ataque3", "Tentaculo_Ataque3") },
      ("Derrota", "Tentaculo_Derrota"), ("Cansado", "Tentaculo_Cansado"), log);

    // 3. Prefabs
    Prefab(Folder + "/Idle_stand.fbx", Folder + "/Jefe.prefab", "Jefe", body, true, log);
    Prefab(Folder + "/Idle_static_tentacle.fbx", Folder + "/Tentaculo.prefab", "Tentaculo", tentacle, false, log);

    AssetDatabase.SaveAssets();
    EditorUtility.DisplayDialog("Jefe", log.ToString(), "OK");
  }

  // ---------- Animator Controller ----------

  private static AnimatorController Controller(string path, string idleClip, (string trigger, string clip)[] actions,
    (string trigger, string clip) defeat, (string boolName, string clip)? tired, System.Text.StringBuilder log)
  {
    var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);

    if (existing != null)
    {
      log.AppendLine("Ya existia " + Path.GetFileName(path) + " (no se toco).");
      return existing;
    }

    var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
    var sm = ctrl.layers[0].stateMachine;

    var idle = sm.AddState("Quieto", new Vector3(300, 0));
    idle.motion = FindClip(idleClip);
    sm.defaultState = idle;

    var y = 80;

    foreach (var (trigger, clip) in actions)
    {
      ctrl.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
      var state = sm.AddState(trigger, new Vector3(600, y));
      state.motion = FindClip(clip);
      y += 70;

      var go = sm.AddAnyStateTransition(state);
      go.AddCondition(AnimatorConditionMode.If, 0f, trigger);
      go.hasExitTime = false;
      go.duration = 0.1f;
      go.canTransitionToSelf = false;

      var back = state.AddTransition(idle);
      back.hasExitTime = true;
      back.exitTime = 0.95f;
      back.duration = 0.15f;
    }

    if (tired.HasValue)
    {
      var (boolName, clip) = tired.Value;
      ctrl.AddParameter(boolName, AnimatorControllerParameterType.Bool);
      var state = sm.AddState(boolName, new Vector3(300, -120));
      state.motion = FindClip(clip);

      var into = idle.AddTransition(state);
      into.AddCondition(AnimatorConditionMode.If, 0f, boolName);
      into.hasExitTime = false;
      into.duration = 0.2f;

      var outOf = state.AddTransition(idle);
      outOf.AddCondition(AnimatorConditionMode.IfNot, 0f, boolName);
      outOf.hasExitTime = false;
      outOf.duration = 0.2f;
    }

    // Derrota: desde cualquier estado y se queda ahi.
    ctrl.AddParameter(defeat.trigger, AnimatorControllerParameterType.Trigger);
    var dead = sm.AddState(defeat.trigger, new Vector3(600, y + 40));
    dead.motion = FindClip(defeat.clip);
    var toDead = sm.AddAnyStateTransition(dead);
    toDead.AddCondition(AnimatorConditionMode.If, 0f, defeat.trigger);
    toDead.hasExitTime = false;
    toDead.duration = 0.1f;
    toDead.canTransitionToSelf = false;

    EditorUtility.SetDirty(ctrl);
    log.AppendLine("Creado " + Path.GetFileName(path));
    return ctrl;
  }

  private static AnimationClip FindClip(string clipName)
  {
    foreach (var pair in Clips)
    {
      if (pair.Value.clip != clipName) continue;

      return AssetDatabase.LoadAllAssetsAtPath(Folder + "/" + pair.Key + ".fbx")
        .OfType<AnimationClip>()
        .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }

    return null;
  }

  // ---------- Prefabs ----------

  private static void Prefab(string modelPath, string prefabPath, string name, RuntimeAnimatorController controller, bool isBody, System.Text.StringBuilder log)
  {
    var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

    if (model == null)
    {
      log.AppendLine("Falta " + modelPath);
      return;
    }

    var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
    PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
    go.name = name;

    // La malla al rig animado (antes de quitar el esqueleto simple).
    var animatedRig = go.transform.Find(isBody ? "rig" : "RIG-Armature");

    if (animatedRig == null)
    {
      log.AppendLine("No encontre el rig animado en " + Path.GetFileName(modelPath));
    }
    else
    {
      foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
      {
        if (smr.name.StartsWith("WGT")) continue;
      // En el archivo del cuerpo, los tentaculos de muestra se quitan: no hace falta engancharlos.
      if (isBody && smr.name.StartsWith("raiz_tentaculo")) continue;
        var moved = RebindToRig(smr, animatedRig, name, log);
        log.AppendLine("  " + smr.name + ": " + moved + " huesos enganchados al rig animado");
      }
    }

    // Quitar lo que no es del personaje.
    foreach (Transform child in go.transform.Cast<Transform>().ToList())
    {
      var n = child.name;
      var remove = child.GetComponent<Camera>() != null || child.GetComponent<Light>() != null
                   || n == "Camera" || n == "Light" || n == "Sun" || n.StartsWith("WGT");

      if (isBody)
      {
        // Los tentaculos de muestra (y sus rigs) y el metarig de Rigify.
        remove |= n.StartsWith("raiz_tentaculo") || n.StartsWith("RIG-Armature") || n.StartsWith("Armature") || n == "metarig";
      }
      else
      {
        // El "Armature" simple no mueve la malla (la mueve RIG-Armature).
        remove |= n == "Armature";
      }

      if (remove)
      {
        Object.DestroyImmediate(child.gameObject);
      }
    }

    var animator = go.GetComponent<Animator>();
    if (animator == null) animator = go.AddComponent<Animator>();
    animator.runtimeAnimatorController = controller;
    animator.applyRootMotion = false;
    animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

    PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
    Object.DestroyImmediate(go);
    log.AppendLine("Prefab listo: " + Path.GetFileName(prefabPath));
  }

  /// <summary>
  /// Cambia cada hueso de la malla (del esqueleto simple) por su hueso DEF-
  /// del rig animado, ajustando las bindposes para que la malla se vea igual.
  /// La malla corregida se guarda como asset. Devuelve cuantos huesos cambio.
  /// </summary>
  private static int RebindToRig(SkinnedMeshRenderer smr, Transform rig, string owner, System.Text.StringBuilder log)
  {
    var mesh = smr.sharedMesh;
    var bones = smr.bones;

    if (mesh == null || bones == null || bones.Length == 0)
    {
      return 0;
    }

    var oldBind = mesh.bindposes;
    var newBones = new Transform[bones.Length];
    var newBind = new Matrix4x4[oldBind.Length];
    var moved = 0;

    for (var i = 0; i < bones.Length; i++)
    {
      var bone = bones[i];
      newBones[i] = bone;
      if (i < oldBind.Length) newBind[i] = oldBind[i];

      if (bone == null || bone.IsChildOf(rig))
      {
        continue;
      }

      // Su DEF- en el rig; si no tiene (p. ej. heel), el DEF- de su padre.
      Transform target = null;

      for (var b = bone; b != null && target == null; b = b.parent)
      {
        target = FindDeep(rig, "DEF-" + b.name) ?? FindDeep(rig, b.name);
      }

      if (target == null)
      {
        log.AppendLine("    sin pareja en el rig: " + bone.name);
        continue;
      }

      newBones[i] = target;

      if (i < oldBind.Length)
      {
        // Misma forma en la pose actual: bind' = def^-1 * hueso * bind
        newBind[i] = target.worldToLocalMatrix * bone.localToWorldMatrix * oldBind[i];
      }

      moved++;
    }

    if (moved == 0)
    {
      return 0;
    }

    var folder = Folder + "/Meshes";
    if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Folder, "Meshes");

    var path = folder + "/" + owner + "_" + smr.name + "_rig.asset";
    var copy = Object.Instantiate(mesh);
    copy.name = owner + "_" + smr.name + "_rig";
    copy.bindposes = newBind;

    var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

    if (existing != null)
    {
      EditorUtility.CopySerialized(copy, existing);
      Object.DestroyImmediate(copy);
      copy = existing;
    }
    else
    {
      AssetDatabase.CreateAsset(copy, path);
    }

    smr.sharedMesh = copy;
    smr.bones = newBones;

    if (smr.rootBone == null || !smr.rootBone.IsChildOf(rig))
    {
      var rootIndex = System.Array.IndexOf(bones, smr.rootBone);
      smr.rootBone = rootIndex >= 0 ? newBones[rootIndex] : newBones.FirstOrDefault(b => b != null);
    }

    // Calcula su caja con los huesos (si no, se podria ocultar al moverse).
    smr.updateWhenOffscreen = true;
    return moved;
  }

  private static Transform FindDeep(Transform root, string name)
  {
    foreach (var t in root.GetComponentsInChildren<Transform>(true))
    {
      if (t.name == name) return t;
    }

    return null;
  }
}
