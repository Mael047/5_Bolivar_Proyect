// MarimondaSetup.cs
// Deja listo el ataque a distancia de las marimondas:
//   1. Crea el proyectil (Escupitajo_Marimonda) y su salpicadura
//      (Escupitajo_Impacto) con particulas editables, en
//      Assets/Prefabs/Enemys/Proyectiles. Si ya existen, no los toca.
//   2. Importa marimonda_ataque como Humanoid con el avatar de la marimonda.
//   3. Crea Marimonda_Distancia.controller (copia de Enemy.controller) con la
//      animacion de escupir en el estado "Attack".
//   4. En los prefabs MARIMONDA_*: asigna ese controlador, añade RangedAttack
//      con el proyectil, un punto "Boca" en la cabeza, y apaga el ataque
//      cuerpo a cuerpo y el daño por contacto.
// Se puede volver a ejecutar sin duplicar nada.
//
// Menu: Tools > Enemigos > Configurar ataque a distancia de la marimonda
//       Tools > Enemigos > Seleccionar proyectil de la marimonda

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

public static class MarimondaSetup
{
  private const string Folder = "Assets/Prefabs/Enemys/Proyectiles";
  private const string ProjectileName = "Escupitajo_Marimonda";
  private const string ImpactName = "Escupitajo_Impacto";
  private const string AttackClipFile = "marimonda_ataque";
  private const string ControllerName = "Marimonda_Distancia.controller";

  private static readonly Color Goo = new Color(0.62f, 0.25f, 0.85f, 1f);
  private static readonly Color GooLight = new Color(0.85f, 0.6f, 1f, 1f);

  [MenuItem("Tools/Enemigos/Configurar ataque a distancia de la marimonda")]
  private static void Run()
  {
    var prefabs = FindPrefabs("MARIMONDA");

    if (prefabs.Count == 0)
    {
      EditorUtility.DisplayDialog("Marimonda", "No encontre prefabs de marimonda (nombre con 'MARIMONDA' y componente Enemy).", "OK");
      return;
    }

    if (!EditorUtility.DisplayDialog("Marimonda",
          $"Se configurara el ataque a distancia en {prefabs.Count} prefab(s): {string.Join(", ", prefabs.Select(p => p.name))}.\n\n" +
          "- Se crea el proyectil y su salpicadura (si no existen).\n" +
          "- marimonda_ataque se importa como Humanoid.\n" +
          "- Se crea Marimonda_Distancia.controller.\n" +
          "- Las marimondas dejan de atacar cuerpo a cuerpo.", "Configurar", "Cancelar"))
    {
      return;
    }

    var projectile = GetOrBuildProjectile();
    var animator = prefabs[0].GetComponentInChildren<Animator>(true);
    var avatar = animator != null ? animator.avatar : null;
    var baseController = animator != null ? animator.runtimeAnimatorController as AnimatorController : null;

    var clip = PrepareClip(AttackClipFile, avatar);
    var controller = GetOrCreateController(baseController);

    if (controller != null && clip != null)
    {
      var attack = controller.layers[0].stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Attack");

      if (attack != null)
      {
        attack.motion = clip;
        EditorUtility.SetDirty(controller);
      }
    }

    foreach (var prefab in prefabs)
    {
      SetupPrefab(prefab, controller, projectile);
    }

    AssetDatabase.SaveAssets();
    Selection.activeObject = projectile != null ? projectile.gameObject : null;

    var msg = "Ataque a distancia configurado. El proyectil esta seleccionado: edita sus particulas libremente.";
    if (clip == null) msg += $"\n\nNo encontre el clip de {AttackClipFile}: asignalo al estado 'Attack' de {ControllerName}.";
    EditorUtility.DisplayDialog("Marimonda", msg, "OK");
  }

  [MenuItem("Tools/Enemigos/Seleccionar proyectil de la marimonda")]
  private static void SelectProjectile()
  {
    var p = GetOrBuildProjectile();
    Selection.activeObject = p.gameObject;
    EditorGUIUtility.PingObject(p.gameObject);
  }

  // ---------- Prefabs de enemigos ----------

  private static List<GameObject> FindPrefabs(string contains)
  {
    var list = new List<GameObject>();

    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
    {
      var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));

      if (go != null && go.name.ToUpperInvariant().Contains(contains) && go.GetComponent<Enemy>() != null)
      {
        list.Add(go);
      }
    }

    return list;
  }

  private static void SetupPrefab(GameObject prefab, AnimatorController controller, EnemyProjectile projectile)
  {
    var path = AssetDatabase.GetAssetPath(prefab);
    var root = PrefabUtility.LoadPrefabContents(path);

    try
    {
      var animator = root.GetComponentInChildren<Animator>(true);

      if (animator != null && controller != null)
      {
        animator.runtimeAnimatorController = controller;
      }

      var enemy = root.GetComponent<Enemy>();
      if (enemy != null) enemy.meleeAttack = false;

      // Daño por contacto apagado (Enemy lo añade solo si falta: se deja puesto y apagado).
      var contact = root.GetComponent<ContactDamage>();
      if (contact == null) contact = root.AddComponent<ContactDamage>();
      contact.enabled = false;

      var ranged = root.GetComponent<RangedAttack>();
      if (ranged == null) ranged = root.AddComponent<RangedAttack>();
      var so = new SerializedObject(ranged);

      if (so.FindProperty("_projectile").objectReferenceValue == null)
      {
        so.FindProperty("_projectile").objectReferenceValue = projectile;
      }

      if (so.FindProperty("_muzzle").objectReferenceValue == null)
      {
        so.FindProperty("_muzzle").objectReferenceValue = CreateMouth(root, animator);
      }

      so.ApplyModifiedPropertiesWithoutUndo();
      PrefabUtility.SaveAsPrefabAsset(root, path);
    }
    finally
    {
      PrefabUtility.UnloadPrefabContents(root);
    }
  }

  private static Transform CreateMouth(GameObject root, Animator animator)
  {
    var existing = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Boca");

    if (existing != null)
    {
      return existing;
    }

    Transform head = null;

    if (animator != null && animator.isHuman)
    {
      head = animator.GetBoneTransform(HumanBodyBones.Head);
    }

    if (head == null)
    {
      head = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.EndsWith(":Head") || t.name == "Head");
    }

    var mouth = new GameObject("Boca").transform;

    if (head != null)
    {
      mouth.SetParent(head, false);
      mouth.position = head.position + root.transform.forward * 0.25f;
    }
    else
    {
      mouth.SetParent(root.transform, false);
      mouth.localPosition = new Vector3(0f, 1.2f, 0.3f);
    }

    return mouth;
  }

  // ---------- Animacion ----------

  private static AnimationClip PrepareClip(string fileName, Avatar avatar)
  {
    string path = null;

    foreach (var guid in AssetDatabase.FindAssets(fileName, new[] { "Assets" }))
    {
      var p = AssetDatabase.GUIDToAssetPath(guid);

      if (Path.GetFileNameWithoutExtension(p) == fileName && p.ToLowerInvariant().EndsWith(".fbx"))
      {
        path = p;
        break;
      }
    }

    if (path == null)
    {
      return null;
    }

    if (AssetImporter.GetAtPath(path) is ModelImporter importer && avatar != null && avatar.isHuman
        && importer.animationType != ModelImporterAnimationType.Human)
    {
      importer.animationType = ModelImporterAnimationType.Human;
      importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
      importer.sourceAvatar = avatar;
      importer.SaveAndReimport();
    }

    return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
  }

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

    if (baseController == null || baseController.name == Path.GetFileNameWithoutExtension(ControllerName))
    {
      return baseController;
    }

    AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(baseController), path);
    return AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
  }

  // ---------- Proyectil ----------

  private static EnemyProjectile GetOrBuildProjectile()
  {
    var path = $"{Folder}/{ProjectileName}.prefab";
    var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

    if (existing != null && existing.TryGetComponent<EnemyProjectile>(out var p))
    {
      return p;
    }

    EnsureFolder(Folder);
    var texture = SoftTexture();
    var glow = ParticleMaterial("Escupitajo_Brillo", texture, true);
    var goo = ParticleMaterial("Escupitajo_Baba", texture, false);
    var impact = BuildImpact(glow, goo);

    var root = new GameObject(ProjectileName);
    var projectile = root.AddComponent<EnemyProjectile>();

    // Nucleo: bola de baba que tiembla.
    AddSystem(root.transform, "Nucleo", goo, ps =>
    {
      var main = ps.main;
      main.loop = true;
      main.duration = 1f;
      main.startLifetime = 0.15f;
      main.startSpeed = 0f;
      main.startSize = new ParticleSystem.MinMaxCurve(0.32f, 0.42f);
      main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
      main.startColor = Goo;
      main.simulationSpace = ParticleSystemSimulationSpace.Local;
      main.maxParticles = 20;
      var emission = ps.emission;
      emission.rateOverTime = 40f;
      var shape = ps.shape;
      shape.shapeType = ParticleSystemShapeType.Sphere;
      shape.radius = 0.04f;
    });

    // Brillo suave alrededor.
    AddSystem(root.transform, "Brillo", glow, ps =>
    {
      var main = ps.main;
      main.loop = true;
      main.duration = 1f;
      main.startLifetime = 0.2f;
      main.startSpeed = 0f;
      main.startSize = 0.9f;
      main.startColor = new Color(GooLight.r, GooLight.g, GooLight.b, 0.35f);
      main.simulationSpace = ParticleSystemSimulationSpace.Local;
      main.maxParticles = 10;
      var emission = ps.emission;
      emission.rateOverTime = 25f;
      var shape = ps.shape;
      shape.enabled = false;
    });

    // Estela que queda en el aire.
    var trail = AddSystem(root.transform, "Estela", goo, ps =>
    {
      var main = ps.main;
      main.loop = true;
      main.duration = 1f;
      main.startLifetime = 0.35f;
      main.startSpeed = 0f;
      main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.26f);
      main.startColor = Goo;
      main.simulationSpace = ParticleSystemSimulationSpace.World;
      main.maxParticles = 80;
      var emission = ps.emission;
      emission.rateOverTime = 0f;
      emission.rateOverDistance = 22f;
      var shape = ps.shape;
      shape.shapeType = ParticleSystemShapeType.Sphere;
      shape.radius = 0.05f;
      FadeAndShrink(ps);
    });

    // Gotitas que caen.
    var drops = AddSystem(root.transform, "Gotas", goo, ps =>
    {
      var main = ps.main;
      main.loop = true;
      main.duration = 1f;
      main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
      main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
      main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
      main.startColor = Goo;
      main.gravityModifier = 1.2f;
      main.simulationSpace = ParticleSystemSimulationSpace.World;
      main.maxParticles = 40;
      var emission = ps.emission;
      emission.rateOverTime = 16f;
      var shape = ps.shape;
      shape.shapeType = ParticleSystemShapeType.Sphere;
      shape.radius = 0.1f;
      FadeAndShrink(ps);
    });

    var lightGo = new GameObject("Luz");
    lightGo.transform.SetParent(root.transform, false);
    var light = lightGo.AddComponent<Light>();
    light.type = LightType.Point;
    light.color = GooLight;
    light.range = 2f;
    light.intensity = 1.2f;
    light.shadows = LightShadows.None;

    var so = new SerializedObject(projectile);
    so.FindProperty("_impactEffect").objectReferenceValue = impact;
    var detach = so.FindProperty("_detachOnImpact");
    detach.arraySize = 2;
    detach.GetArrayElementAtIndex(0).objectReferenceValue = trail;
    detach.GetArrayElementAtIndex(1).objectReferenceValue = drops;
    so.ApplyModifiedPropertiesWithoutUndo();

    var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
    Object.DestroyImmediate(root);
    return prefab.GetComponent<EnemyProjectile>();
  }

  private static GameObject BuildImpact(Material glow, Material goo)
  {
    var path = $"{Folder}/{ImpactName}.prefab";
    var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

    if (existing != null)
    {
      return existing;
    }

    var root = new GameObject(ImpactName);

    // Sistema principal en la raiz: se destruye solo al terminar.
    var splash = root.AddComponent<ParticleSystem>();
    splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    Configure(splash, goo, ps =>
    {
      var main = ps.main;
      main.loop = false;
      main.duration = 0.6f;
      main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
      main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4.5f);
      main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.15f);
      main.startColor = Goo;
      main.gravityModifier = 2f;
      main.stopAction = ParticleSystemStopAction.Destroy;
      main.maxParticles = 30;
      var emission = ps.emission;
      emission.rateOverTime = 0f;
      emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });
      var shape = ps.shape;
      shape.shapeType = ParticleSystemShapeType.Cone; // sale hacia la normal (+Z)
      shape.angle = 65f;
      shape.radius = 0.05f;
      FadeAndShrink(ps);
    });

    AddSystem(root.transform, "Mancha", glow, ps =>
    {
      var main = ps.main;
      main.loop = false;
      main.duration = 0.4f;
      main.startLifetime = 0.35f;
      main.startSpeed = 0f;
      main.startSize = 0.9f;
      main.startColor = new Color(GooLight.r, GooLight.g, GooLight.b, 0.6f);
      main.maxParticles = 1;
      var emission = ps.emission;
      emission.rateOverTime = 0f;
      emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
      var shape = ps.shape;
      shape.enabled = false;
      var size = ps.sizeOverLifetime;
      size.enabled = true;
      size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1f));
      var color = ps.colorOverLifetime;
      color.enabled = true;
      color.color = FadeGradient(0f);
    });

    var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
    Object.DestroyImmediate(root);
    return prefab;
  }

  private static ParticleSystem AddSystem(Transform parent, string name, Material material, System.Action<ParticleSystem> configure)
  {
    var go = new GameObject(name);
    go.transform.SetParent(parent, false);
    var ps = go.AddComponent<ParticleSystem>();
    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    Configure(ps, material, configure);
    return ps;
  }

  private static void Configure(ParticleSystem ps, Material material, System.Action<ParticleSystem> configure)
  {
    var main = ps.main;
    main.playOnAwake = true;
    configure(ps);

    var renderer = ps.GetComponent<ParticleSystemRenderer>();
    renderer.sharedMaterial = material;
    renderer.renderMode = ParticleSystemRenderMode.Billboard;
    renderer.shadowCastingMode = ShadowCastingMode.Off;
    renderer.receiveShadows = false;
  }

  private static void FadeAndShrink(ParticleSystem ps)
  {
    var color = ps.colorOverLifetime;
    color.enabled = true;
    color.color = FadeGradient(1f);

    var size = ps.sizeOverLifetime;
    size.enabled = true;
    size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f));
  }

  private static Gradient FadeGradient(float startAlpha)
  {
    var g = new Gradient();
    g.SetKeys(
      new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
      new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
    return g;
  }

  // ---------- Materiales y textura ----------

  private static Texture2D SoftTexture()
  {
    var path = $"{Folder}/Particula_Suave.png";
    var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

    if (existing != null)
    {
      return existing;
    }

    const int size = 64;
    var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);

    for (var y = 0; y < size; y++)
    {
      for (var x = 0; x < size; x++)
      {
        var dx = (x + 0.5f) / size * 2f - 1f;
        var dy = (y + 0.5f) / size * 2f - 1f;
        var d = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
        tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, d * 1.4f)));
      }
    }

    File.WriteAllBytes(path, tex.EncodeToPNG());
    Object.DestroyImmediate(tex);
    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

    if (AssetImporter.GetAtPath(path) is TextureImporter importer)
    {
      importer.alphaIsTransparency = true;
      importer.wrapMode = TextureWrapMode.Clamp;
      importer.mipmapEnabled = true;
      importer.SaveAndReimport();
    }

    return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
  }

  private static Material ParticleMaterial(string name, Texture2D texture, bool additive)
  {
    var path = $"{Folder}/{name}.mat";
    var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

    if (existing != null)
    {
      return existing;
    }

    var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
    if (shader == null) shader = Shader.Find("Sprites/Default");
    var mat = new Material(shader) { name = name };

    if (additive)
    {
      NuiGrab.TelekinesisVFX.ConfigureAdditive(mat);
    }
    else
    {
      mat.SetFloat("_Surface", 1f);
      mat.SetFloat("_Blend", 0f);
      mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
      mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
      mat.SetFloat("_ZWrite", 0f);
      mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
      mat.renderQueue = (int)RenderQueue.Transparent;
    }

    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);

    AssetDatabase.CreateAsset(mat, path);
    return mat;
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
}
