// FlowerPetals.cs
// Suelta petalos cuando el jugador atraviesa flores del Terrain corriendo o
// esquivando. Las flores son "details" del Terrain: no tienen objetos propios,
// asi que se consulta la densidad de detalle bajo el jugador.
// Cuenta como flor cualquier detalle cuyo prefab tenga en el nombre alguna de
// las palabras de 'Flower Keywords'.
// Va en el Player.

using System.Collections.Generic;
using UnityEngine;

public class FlowerPetals : MonoBehaviour
{
  [Header("Cuando")]
  [SerializeField, Min(0f), Tooltip("Velocidad minima (m/s) para soltar petalos (correr o esquivar).")]
  private float _minSpeed = 3.5f;

  [SerializeField, Min(0.02f), Tooltip("Cada cuanto se revisa si hay flores bajo el jugador (s).")]
  private float _checkInterval = 0.12f;

  [SerializeField, Tooltip("Palabras que identifican los detalles del Terrain que son flores.")]
  private string[] _flowerKeywords = { "flower", "flor", "plant" };

  [Header("Petalos")]
  [SerializeField, Min(1)] private int _petalsPerBurst = 5;
  [SerializeField] private Gradient _colors = DefaultColors();
  [SerializeField] private Vector2 _size = new Vector2(0.05f, 0.11f);
  [SerializeField] private Vector2 _lifetime = new Vector2(0.9f, 1.6f);

  [SerializeField, Tooltip("Material de los petalos (opcional). Vacio = uno simple generado.")]
  private Material _material;

  private Rigidbody _rb;
  private ParticleSystem _ps;
  private float _timer;
  private Terrain _terrain;
  private readonly List<int> _flowerLayers = new List<int>();

  private void Awake()
  {
    _rb = GetComponent<Rigidbody>();
    BuildParticles();
  }

  private void Update()
  {
    _timer -= Time.deltaTime;

    if (_timer > 0f || _rb == null)
    {
      return;
    }

    _timer = _checkInterval;

    var velocity = _rb.linearVelocity;
    velocity.y = 0f;

    if (velocity.magnitude < _minSpeed || !OnFlowers(transform.position))
    {
      return;
    }

    _ps.transform.position = transform.position + Vector3.up * 0.25f;
    var emit = new ParticleSystem.EmitParams { applyShapeToPosition = true };

    for (var i = 0; i < _petalsPerBurst; i++)
    {
      emit.startColor = _colors.Evaluate(Random.value);
      emit.velocity = -velocity.normalized * Random.Range(0.3f, 1f) + Vector3.up * Random.Range(1f, 2.2f) + Random.insideUnitSphere * 0.6f;
      _ps.Emit(emit, 1);
    }
  }

  /// <summary>True si hay algun detalle "flor" del Terrain en la posicion.</summary>
  private bool OnFlowers(Vector3 position)
  {
    var terrain = TerrainAt(position);
    if (terrain == null || terrain.terrainData == null) return false;

    if (terrain != _terrain)
    {
      _terrain = terrain;
      CacheFlowerLayers(terrain.terrainData);
    }

    if (_flowerLayers.Count == 0) return false;

    var data = terrain.terrainData;
    var local = position - terrain.transform.position;
    var x = Mathf.Clamp(Mathf.RoundToInt(local.x / data.size.x * data.detailWidth), 0, data.detailWidth - 1);
    var y = Mathf.Clamp(Mathf.RoundToInt(local.z / data.size.z * data.detailHeight), 0, data.detailHeight - 1);

    foreach (var layer in _flowerLayers)
    {
      var map = data.GetDetailLayer(x, y, 1, 1, layer);
      if (map[0, 0] > 0) return true;
    }

    return false;
  }

  private static Terrain TerrainAt(Vector3 position)
  {
    foreach (var t in Terrain.activeTerrains)
    {
      var p = t.transform.position;
      var s = t.terrainData != null ? t.terrainData.size : Vector3.zero;
      if (position.x >= p.x && position.x <= p.x + s.x && position.z >= p.z && position.z <= p.z + s.z) return t;
    }

    return null;
  }

  private void CacheFlowerLayers(TerrainData data)
  {
    _flowerLayers.Clear();
    var protos = data.detailPrototypes;

    for (var i = 0; i < protos.Length; i++)
    {
      var protoName = protos[i].prototype != null ? protos[i].prototype.name
        : protos[i].prototypeTexture != null ? protos[i].prototypeTexture.name : "";
      protoName = protoName.ToLowerInvariant();

      foreach (var keyword in _flowerKeywords)
      {
        if (!string.IsNullOrEmpty(keyword) && protoName.Contains(keyword.ToLowerInvariant()))
        {
          _flowerLayers.Add(i);
          break;
        }
      }
    }
  }

  private void BuildParticles()
  {
    var go = new GameObject("FlowerPetals");
    _ps = go.AddComponent<ParticleSystem>();
    _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

    var main = _ps.main;
    main.playOnAwake = false;
    main.loop = false;
    main.simulationSpace = ParticleSystemSimulationSpace.World;
    main.startLifetime = new ParticleSystem.MinMaxCurve(_lifetime.x, _lifetime.y);
    main.startSize = new ParticleSystem.MinMaxCurve(_size.x, _size.y);
    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
    main.gravityModifier = 0.25f;
    main.maxParticles = 300;

    var emission = _ps.emission;
    emission.rateOverTime = 0f;

    var shape = _ps.shape;
    shape.shapeType = ParticleSystemShapeType.Circle;
    shape.radius = 0.35f;
    shape.rotation = new Vector3(90f, 0f, 0f);

    // Caen planeando: frenado del aire, giro y un poco de vaiven.
    var limit = _ps.limitVelocityOverLifetime;
    limit.enabled = true;
    limit.dampen = 0.15f;
    limit.limit = 1.2f;

    var rotation = _ps.rotationOverLifetime;
    rotation.enabled = true;
    rotation.z = new ParticleSystem.MinMaxCurve(-3f, 3f);

    var noise = _ps.noise;
    noise.enabled = true;
    noise.strength = 0.6f;
    noise.frequency = 0.8f;

    var color = _ps.colorOverLifetime;
    color.enabled = true;
    var fade = new Gradient();
    fade.SetKeys(
      new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
    color.color = new ParticleSystem.MinMaxGradient(fade);

    var renderer = go.GetComponent<ParticleSystemRenderer>();
    renderer.renderMode = ParticleSystemRenderMode.Billboard;
    renderer.sharedMaterial = _material != null ? _material : PetalMaterial();
    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
  }

  private static Material PetalMaterial()
  {
    var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
    if (shader == null) shader = Shader.Find("Sprites/Default");
    var mat = new Material(shader) { name = "PetalMaterial" };

    // Transparente con mezcla alfa normal (no aditiva: los petalos no brillan).
    mat.SetFloat("_Surface", 1f);
    mat.SetFloat("_Blend", 0f);
    mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
    mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
    mat.SetFloat("_ZWrite", 0f);
    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

    // Petalo: elipse de bordes suaves.
    const int w = 32, h = 32;
    var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Petal" };
    var px = new Color32[w * h];

    for (var y = 0; y < h; y++)
    {
      for (var x = 0; x < w; x++)
      {
        var u = (x + 0.5f) / w * 2f - 1f;
        var v = (y + 0.5f) / h * 2f - 1f;
        var d = Mathf.Sqrt(u * u / 0.35f + v * v);
        var a = Mathf.Clamp01((1f - d) * 4f);
        px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255));
      }
    }

    tex.SetPixels32(px);
    tex.Apply();
    mat.SetTexture("_BaseMap", tex);
    mat.mainTexture = tex;
    return mat;
  }

  private static Gradient DefaultColors()
  {
    var g = new Gradient();
    g.SetKeys(
      new[]
      {
        new GradientColorKey(new Color(1f, 0.55f, 0.7f), 0f),
        new GradientColorKey(new Color(1f, 0.9f, 0.4f), 0.5f),
        new GradientColorKey(new Color(0.95f, 0.95f, 1f), 1f)
      },
      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
    return g;
  }
}
