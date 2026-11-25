// RenderOptimizer.cs
// Optimizacion de rendimiento alrededor de la camara. Va en la Main Camera
// (Tools > Rendimiento > Optimizar escena abierta lo pone solo).
//
// 1. Terreno: distancias de arboles, pasto/detalles y precision del Terrain
//    pensadas para la camara cenital (por defecto los arboles se dibujaban a
//    5000 m). Se aplican al empezar; no cambian la escena guardada. Las
//    distancias de arboles solo se acortan (respeta los arboles convertidos
//    en objetos por CameraOccluderHider).
//
// 2. Activacion por distancia: los objetos con DistanceCullable (enemigos,
//    particulas, animaciones de decorado) se "pausan" cuando estan lejos del
//    jugador Y fuera de la vista: dejan de dibujarse, animarse, sonar y
//    pensar (IA). Vuelven ANTES de que se vean: cuentan como visibles con un
//    margen extra alrededor de la pantalla, y hay un anillo de margen para
//    que no se enciendan y apaguen en el borde. Unity ya dibuja solo lo que
//    esta en pantalla; esto ahorra la CPU (scripts, animaciones, sombras).
//    Usa CullingGroup de Unity y revisa unas pocas veces por segundo.

using System.Collections.Generic;
using UnityEngine;

namespace Rendimiento
{
  [DisallowMultipleComponent]
  public class RenderOptimizer : MonoBehaviour
  {
    [Header("Terreno")]
    [SerializeField, Tooltip("Aplicar estos valores a todos los Terrain al empezar.")]
    private bool _applyTerrain = true;

    [SerializeField, Min(1f), Tooltip("Distancia maxima a la que se dibujan arboles del Terrain.")]
    private float _treeDistance = 120f;

    [SerializeField, Min(1f), Tooltip("A partir de esta distancia los arboles se dibujan como billboard (imagen plana, mucho mas barato).")]
    private float _treeBillboardDistance = 40f;

    [SerializeField, Range(0f, 250f), Tooltip("Distancia maxima del pasto y detalles del Terrain.")]
    private float _detailDistance = 50f;

    [SerializeField, Range(0f, 1f), Tooltip("Densidad del pasto y detalles (1 = todo).")]
    private float _detailDensity = 1f;

    [SerializeField, Range(1f, 200f), Tooltip("Error de pixel del relieve: mas alto = menos triangulos lejos.")]
    private float _heightmapPixelError = 8f;

    [SerializeField, Min(1f), Tooltip("Distancia a la que el suelo del Terrain usa su textura simplificada.")]
    private float _basemapDistance = 80f;

    [Header("Activacion por distancia")]
    [SerializeField, Tooltip("Centro de la zona activa. Vacio = el jugador (o esta camara si no hay jugador).")]
    private Transform _focus;

    [SerializeField, Min(1f), Tooltip("Todo lo que este a menos de esta distancia del jugador queda activo aunque no se vea.")]
    private float _activeRadius = 30f;

    [SerializeField, Min(0f), Tooltip("Anillo extra: entre el radio y radio + margen se mantiene como esta (evita parpadeos).")]
    private float _margin = 10f;

    [SerializeField, Min(0f), Tooltip("Metros extra alrededor de cada objeto para considerarlo visible: se activa un poco antes de entrar en pantalla.")]
    private float _visibilityPadding = 6f;

    [SerializeField, Range(0.05f, 1f), Tooltip("Cada cuanto se revisa (segundos).")]
    private float _checkInterval = 0.2f;

    [SerializeField, Tooltip("Pausar tambien lo que esta lejos aunque se vea en pantalla (mas ahorro, puede notarse).")]
    private bool _pauseVisibleFar;

    public static RenderOptimizer Instance { get; private set; }

    private static readonly List<DistanceCullable> s_pending = new List<DistanceCullable>();

    private readonly List<DistanceCullable> _items = new List<DistanceCullable>();
    private BoundingSphere[] _spheres = new BoundingSphere[64];
    private CullingGroup _group;
    private Camera _camera;
    private float _nextCheck;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
      Instance = null;
      s_pending.Clear();
    }

    private void Awake()
    {
      if (Instance != null && Instance != this)
      {
        Debug.LogWarning("[RenderOptimizer] Hay dos en la escena; se usa el primero.", this);
        enabled = false;
        return;
      }

      Instance = this;
      _camera = GetComponent<Camera>();

      if (_camera == null)
      {
        _camera = Camera.main;
      }
    }

    private void OnEnable()
    {
      if (Instance != this)
      {
        return;
      }

      _group = new CullingGroup { targetCamera = _camera };
      _group.SetBoundingSpheres(_spheres);
      _group.SetBoundingSphereCount(0);
      _group.SetDistanceReferencePoint(_focus != null ? _focus : transform);
      UpdateBands();

      foreach (var item in s_pending)
      {
        if (item != null) Add(item);
      }

      s_pending.Clear();
    }

    private void OnDisable()
    {
      if (_group != null)
      {
        _group.Dispose();
        _group = null;
      }

      // Todo vuelve a la normalidad.
      foreach (var item in _items)
      {
        if (item != null)
        {
          item.SetPaused(false);
          item.Index = -1;
          s_pending.Add(item);
        }
      }

      _items.Clear();
    }

    private void OnDestroy()
    {
      if (Instance == this)
      {
        Instance = null;
      }
    }

    private void OnValidate()
    {
      _treeBillboardDistance = Mathf.Min(_treeBillboardDistance, _treeDistance);

      if (_group != null)
      {
        UpdateBands();
      }
    }

    private void Start()
    {
      if (_applyTerrain)
      {
        ApplyTerrain();
      }

      if (_focus == null)
      {
        var player = FindAnyObjectByType<global::Player>();
        _focus = player != null ? player.transform : transform;
      }

      if (_group != null)
      {
        _group.SetDistanceReferencePoint(_focus);
      }
    }

    /// <summary>Aplica las distancias del terreno a todos los Terrain activos.</summary>
    public void ApplyTerrain()
    {
      foreach (var terrain in Terrain.activeTerrains)
      {
        if (terrain == null) continue;

        // Solo se acortan las distancias de arboles, nunca se alargan: si
        // CameraOccluderHider ya convirtio los arboles en objetos (para
        // transparentarlos) el Terrain esta en 0 y debe seguir asi, o se
        // dibujarian dos arboles en cada sitio.
        if (terrain.treeDistance > 0f)
        {
          terrain.treeDistance = Mathf.Min(terrain.treeDistance, _treeDistance);
          terrain.treeBillboardDistance = Mathf.Min(terrain.treeBillboardDistance, _treeBillboardDistance);
        }

        terrain.detailObjectDistance = _detailDistance;
        terrain.detailObjectDensity = _detailDensity;
        terrain.heightmapPixelError = _heightmapPixelError;
        terrain.basemapDistance = _basemapDistance;
      }
    }

    // ---------- Registro ----------

    internal static void Register(DistanceCullable item)
    {
      if (Instance != null && Instance._group != null)
      {
        Instance.Add(item);
      }
      else if (!s_pending.Contains(item))
      {
        s_pending.Add(item);
      }
    }

    internal static void Unregister(DistanceCullable item)
    {
      s_pending.Remove(item);

      if (Instance != null)
      {
        Instance.Remove(item);
      }
    }

    private void Add(DistanceCullable item)
    {
      if (item.Index >= 0)
      {
        return;
      }

      if (_items.Count >= _spheres.Length)
      {
        System.Array.Resize(ref _spheres, _spheres.Length * 2);
        _group.SetBoundingSpheres(_spheres);
      }

      item.Index = _items.Count;
      _items.Add(item);
      _spheres[item.Index] = item.GetSphere(_visibilityPadding);
      _group.SetBoundingSphereCount(_items.Count);
    }

    private void Remove(DistanceCullable item)
    {
      var index = item.Index;

      if (index < 0 || index >= _items.Count || _items[index] != item)
      {
        return;
      }

      // Se cambia por el ultimo para no dejar huecos.
      var last = _items.Count - 1;

      if (index != last)
      {
        var moved = _items[last];
        _items[index] = moved;
        _spheres[index] = _spheres[last];
        moved.Index = index;
      }

      _items.RemoveAt(last);
      item.Index = -1;

      if (_group != null)
      {
        _group.SetBoundingSphereCount(_items.Count);
      }
    }

    private void UpdateBands()
    {
      // Banda 0: dentro del radio. Banda 1: anillo de margen. Banda 2: lejos.
      _group.SetBoundingDistances(new[] { _activeRadius, _activeRadius + _margin });
    }

    // ---------- Revision ----------

    private void Update()
    {
      if (_group == null || Time.unscaledTime < _nextCheck)
      {
        return;
      }

      _nextCheck = Time.unscaledTime + _checkInterval;

      for (var i = _items.Count - 1; i >= 0; i--)
      {
        var item = _items[i];

        if (item == null)
        {
          // Destruido sin pasar por OnDisable (raro): se limpia.
          var last = _items.Count - 1;
          _items[i] = _items[last];
          _spheres[i] = _spheres[last];
          if (_items[i] != null) _items[i].Index = i;
          _items.RemoveAt(last);
          _group.SetBoundingSphereCount(_items.Count);
          continue;
        }

        // Los que se mueven (enemigos) actualizan su esfera.
        if (item.Moves)
        {
          _spheres[i] = item.GetSphere(_visibilityPadding);
        }

        var band = _group.GetDistance(i);
        var visible = _group.IsVisible(i);

        if (band == 0 || (visible && !_pauseVisibleFar) || item.KeepActive)
        {
          item.SetPaused(false);
        }
        else if (band >= 2 && (!visible || _pauseVisibleFar))
        {
          item.SetPaused(true);
        }
        // Banda 1 (anillo): se queda como esta.
      }
    }

    private void OnDrawGizmosSelected()
    {
      var center = _focus != null ? _focus.position : transform.position;
      Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.8f);
      DrawCircle(center, _activeRadius);
      Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
      DrawCircle(center, _activeRadius + _margin);
    }

    private static void DrawCircle(Vector3 center, float radius)
    {
      var prev = center + new Vector3(radius, 0f, 0f);

      for (var i = 1; i <= 48; i++)
      {
        var a = i / 48f * Mathf.PI * 2f;
        var next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
        Gizmos.DrawLine(prev, next);
        prev = next;
      }
    }
  }
}
