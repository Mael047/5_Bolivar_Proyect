// BossFx.cs
// Efectos sencillos para la pelea del jefe, hechos por codigo (sin prefabs):
//   - Burst: estallido de particulas (tierra, golpes, energia).
//   - Wisps: humo/energia que sale sin parar hasta que se apaga.
//   - Shatter: rompe un objeto en pedazos que salen volando (la mesa).
// Las particulas usan el material que se le pase (en la pelea: el brillo de
// los escupitajos, aditivo con textura suave).

using UnityEngine;

public static class BossFx
{
  /// <summary>Estallido de particulas hacia arriba (o en todas direcciones).</summary>
  public static ParticleSystem Burst(Vector3 position, Color color, int count, float speed, float size, float life, Material material, bool sphere = false, float gravity = 0.6f)
  {
    if (material == null || count <= 0)
    {
      return null;
    }

    var go = new GameObject("FX_Estallido");
    go.transform.SetPositionAndRotation(position, Quaternion.Euler(-90f, 0f, 0f));
    var ps = go.AddComponent<ParticleSystem>();
    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

    var main = ps.main;
    main.duration = 0.3f;
    main.loop = false;
    main.playOnAwake = false;
    main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
    main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
    main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
    main.startColor = color;
    main.gravityModifier = gravity;
    main.simulationSpace = ParticleSystemSimulationSpace.World;
    main.maxParticles = count;

    var emission = ps.emission;
    emission.rateOverTime = 0f;
    emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

    var shape = ps.shape;
    shape.shapeType = sphere ? ParticleSystemShapeType.Sphere : ParticleSystemShapeType.Cone;
    shape.angle = 40f;
    shape.radius = 0.25f;

    FadeOut(ps);

    var renderer = go.GetComponent<ParticleSystemRenderer>();
    renderer.sharedMaterial = material;
    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

    ps.Play();
    Object.Destroy(go, main.duration + life + 0.2f);
    return ps;
  }

  /// <summary>Humo/energia continuo pegado a un objeto. Se para con Stop().</summary>
  public static ParticleSystem Wisps(Transform parent, Vector3 localOffset, Color color, float radius, Material material)
  {
    if (material == null)
    {
      return null;
    }

    var go = new GameObject("FX_Humo");
    go.transform.SetParent(parent, false);
    go.transform.localPosition = localOffset;
    go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
    var ps = go.AddComponent<ParticleSystem>();
    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

    var main = ps.main;
    main.loop = true;
    main.playOnAwake = false;
    main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
    main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
    main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
    main.startColor = color;
    main.simulationSpace = ParticleSystemSimulationSpace.World;
    main.maxParticles = 60;

    var emission = ps.emission;
    emission.rateOverTime = 30f;

    var shape = ps.shape;
    shape.shapeType = ParticleSystemShapeType.Circle;
    shape.radius = radius;

    FadeOut(ps);

    var renderer = go.GetComponent<ParticleSystemRenderer>();
    renderer.sharedMaterial = material;
    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

    ps.Play();
    return ps;
  }

  private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
  private static MaterialPropertyBlock s_block;

  /// <summary>Destello: enciende la emision de los renderers y la apaga en "duration" segundos.</summary>
  public static void Flash(MonoBehaviour host, Renderer[] renderers, Color color, float duration)
  {
    if (host != null && host.isActiveAndEnabled && renderers != null)
    {
      host.StartCoroutine(FlashRoutine(renderers, color, duration));
    }
  }

  private static System.Collections.IEnumerator FlashRoutine(Renderer[] renderers, Color color, float duration)
  {
    s_block ??= new MaterialPropertyBlock();
    var t = 0f;

    while (t < duration)
    {
      t += Time.deltaTime;
      var k = 1f - Mathf.Clamp01(t / duration);
      SetEmission(renderers, color * (k * k));
      yield return null;
    }

    SetEmission(renderers, Color.black);
  }

  private static void SetEmission(Renderer[] renderers, Color color)
  {
    foreach (var r in renderers)
    {
      if (r == null || r is ParticleSystemRenderer) continue;
      r.GetPropertyBlock(s_block);
      s_block.SetColor(EmissionId, color);
      r.SetPropertyBlock(s_block);
    }
  }

  private static void FadeOut(ParticleSystem ps)
  {
    var col = ps.colorOverLifetime;
    col.enabled = true;
    var gradient = new Gradient();
    gradient.SetKeys(
      new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
    col.color = gradient;

    var sizeOverLife = ps.sizeOverLifetime;
    sizeOverLife.enabled = true;
    sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
  }

  /// <summary>
  /// Rompe un objeto: lo oculta y suelta pedazos (cubos con su material) que
  /// salen volando desde <paramref name="origin"/>. Los pedazos solo chocan con
  /// el suelo y el escenario, y desaparecen solos.
  /// </summary>
  public static void Shatter(GameObject target, Vector3 origin, float force)
  {
    if (target == null || !target.activeInHierarchy)
    {
      return;
    }

    var layer = NuiGrab.Breakable.FragmentLayer;

    foreach (var renderer in target.GetComponentsInChildren<MeshRenderer>())
    {
      if (!renderer.enabled) continue;

      var b = renderer.bounds;
      var volume = b.size.x * b.size.y * b.size.z;
      var pieces = Mathf.Clamp(Mathf.RoundToInt(volume * 40f), 4, 14);

      for (var i = 0; i < pieces; i++)
      {
        var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shard.name = "Pedazo";
        shard.layer = layer;
        var size = new Vector3(Random.Range(0.15f, 0.35f) * b.size.x, Random.Range(0.15f, 0.35f) * b.size.y, Random.Range(0.15f, 0.35f) * b.size.z);
        size = Vector3.Max(size, Vector3.one * 0.05f);
        shard.transform.localScale = size;
        shard.transform.position = new Vector3(
          Random.Range(b.min.x, b.max.x), Random.Range(b.min.y, b.max.y), Random.Range(b.min.z, b.max.z));
        shard.transform.rotation = Random.rotation;

        var shardRenderer = shard.GetComponent<MeshRenderer>();
        shardRenderer.sharedMaterial = renderer.sharedMaterial;

        var body = shard.AddComponent<Rigidbody>();
        body.mass = 0.3f;
        body.AddExplosionForce(force, origin, Mathf.Max(3f, b.extents.magnitude * 3f), 1.2f, ForceMode.VelocityChange);
        body.angularVelocity = Random.insideUnitSphere * 10f;

        shard.AddComponent<Shard>().Life = Random.Range(2.5f, 4f);
      }
    }

    target.SetActive(false);
  }

  /// <summary>Pedazo que se encoge y desaparece.</summary>
  private class Shard : MonoBehaviour
  {
    public float Life = 3f;
    private float _age;
    private Vector3 _scale;

    private void Start() => _scale = transform.localScale;

    private void Update()
    {
      _age += Time.deltaTime;
      var shrink = Mathf.Clamp01((Life - _age) / 0.8f);
      transform.localScale = _scale * shrink;

      if (_age >= Life)
      {
        Destroy(gameObject);
      }
    }
  }
}
