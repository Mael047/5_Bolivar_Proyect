// CameraOccluderHider.cs
// Lo que tapa a la nutria desde la camara (paredes, casas, arboles, fuentes...)
// se vuelve transparente poco a poco y vuelve a aparecer suave cuando deja de
// tapar. Sirve en el jardin y dentro de la casa.
//
// Como decide si algo tapa:
//   - Lanza varias lineas de vista desde la nutria (pies, pecho, cabeza y
//     los lados) hacia la camara. Con camara ortografica las lineas van
//     paralelas a la vista (como se ve en pantalla), no hacia la posicion de
//     la camara.
//   - Objetos con collider que cubre su forma (casas, paredes, fuentes):
//     se usa el collider exacto.
//   - Objetos sin collider o con collider solo en una parte (arboles: el
//     collider es el tronco, pero tapa la copa): se usa la caja de su malla.
//   - Desde fuera se desvanece el objeto entero (la casa con su techo, el
//     arbol con tronco y copa). Si la nutria esta dentro (interior de la
//     casa), solo la pieza que tapa.
//
// Materiales URP Lit: copia transparente con su color. Otros shaders (arboles
// de Nature, etc.): copia del material de referencia con su textura.
//
// Va en la camara principal. Coste bajo: solo se revisan los objetos cerca de
// la nutria y la lista se prepara al empezar.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class CameraOccluderHider : MonoBehaviour
{
  private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
  private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
  private static readonly int SurfaceId = Shader.PropertyToID("_Surface");

  [SerializeField, Tooltip("A quien no se debe tapar (vacio = objeto con tag Player).")]
  private Transform _target;
  [SerializeField, Tooltip("Altura del pecho del jugador (si no tiene collider).")]
  private float _targetHeight = 0.8f;
  [SerializeField, Min(0f), Tooltip("Margen a los lados de la nutria que tambien se despeja.")]
  private float _radius = 0.4f;
  [SerializeField, Tooltip("Capas que se pueden desvanecer.")]
  private LayerMask _occluderLayers = 1;
  [SerializeField, Tooltip("Objetos que nunca se desvanecen (con sus hijos). Los objetos con InteractiveCameraFocus, como la mesa de billar, tampoco se desvanecen nunca.")]
  private Transform[] _neverFade;

  [Header("Que objetos cuentan")]
  [SerializeField, Min(0f), Tooltip("Altura minima de un objeto para poder tapar (asi se ignoran pasto, flores, piedras).")]
  private float _minHeight = 0.8f;
  [SerializeField, Min(1f), Tooltip("Solo se revisan los objetos a esta distancia de la nutria.")]
  private float _searchRadius = 25f;
  [SerializeField, Range(0.3f, 1f), Tooltip("Para objetos sin collider (copas de arbol): tamaño de la caja usada respecto a la malla. Menos = hay que estar mas detras para que se desvanezca.")]
  private float _boxShrink = 0.85f;
  [SerializeField, Min(1), Tooltip("Piezas maximas que se desvanecen juntas como un solo objeto (casa con techo, arbol con tronco y copa).")]
  private int _maxGroupRenderers = 12;
  [SerializeField, Min(0f), Tooltip("Cada cuantos segundos se vuelve a buscar objetos nuevos (0 = solo al empezar).")]
  private float _rescanInterval = 0f;
  [SerializeField, Tooltip("Arboles pintados en el Terrain: al empezar el juego se cambian por objetos normales (solo en juego, el Terrain no se modifica) para que tambien se desvanezcan.")]
  private bool _fadeTerrainTrees = true;

  [Header("Desvanecido")]
  [SerializeField, Range(0f, 1f), Tooltip("Opacidad minima de lo que tapa (0 = invisible).")]
  private float _minAlpha = 0.18f;
  [SerializeField, Min(0.05f), Tooltip("Segundos en desvanecerse.")]
  private float _fadeOutTime = 0.25f;
  [SerializeField, Min(0.05f), Tooltip("Segundos en volver a aparecer.")]
  private float _fadeInTime = 0.45f;
  [SerializeField, Min(0f), Tooltip("Segundos que sigue desvanecido tras dejar de tapar (evita parpadeos).")]
  private float _showDelay = 0.2f;
  [SerializeField, Tooltip("Material URP Lit transparente de referencia (Assets/Materials/Casa_Transparente_Ref.mat). Se usa para los shaders que no son URP Lit y hace que la build incluya la version transparente.")]
  private Material _transparentTemplate;

  private class Candidate
  {
    public Renderer renderer;
    public Collider[] colliders;
    public bool useBox;
    public Renderer[] group;
    public Bounds groupBounds;
  }

  private class Faded
  {
    public Renderer renderer;
    public Material[] original;
    public Color[] colors;
    public bool transparent;
    public ShadowCastingMode shadowMode;
    public float alpha = 1f;
    public float lastHit;
  }

  private const int RayCount = 5;

  private static readonly Dictionary<Material, Material> s_transparentCopies = new Dictionary<Material, Material>();

  private readonly List<Candidate> _candidates = new List<Candidate>();
  private readonly Dictionary<Renderer, Faded> _faded = new Dictionary<Renderer, Faded>();
  private readonly List<Renderer> _done = new List<Renderer>();
  private readonly Ray[] _rays = new Ray[RayCount];
  private readonly float[] _rayLengths = new float[RayCount];
  private MaterialPropertyBlock _block;
  private Camera _camera;
  private Collider _targetCollider;
  private float _nextRescan;

  private void Awake()
  {
    _block = new MaterialPropertyBlock();
    _camera = GetComponent<Camera>();
  }

  private void Start()
  {
    if (_fadeTerrainTrees)
    {
      ConvertTerrainTrees();
    }

    Rescan();
  }

  /// <summary>
  /// Los arboles del Terrain no son objetos y no se pueden desvanecer uno a
  /// uno. Se crea un objeto por arbol (mismo prefab, posicion, giro y tamaño)
  /// y el Terrain deja de dibujarlos. Sus colliders siguen siendo los del
  /// Terrain, asi que las copias no llevan collider.
  /// </summary>
  private void ConvertTerrainTrees()
  {
    foreach (var terrain in Terrain.activeTerrains)
    {
      var data = terrain != null ? terrain.terrainData : null;

      if (data == null || data.treeInstanceCount == 0 || terrain.treeDistance <= 0f)
      {
        continue;
      }

      var prototypes = data.treePrototypes;
      var holder = new GameObject("Arboles_Terreno (" + terrain.name + ")").transform;
      var origin = terrain.transform.position;
      var size = data.size;
      var created = 0;

      foreach (var tree in data.treeInstances)
      {
        if (tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypes.Length)
        {
          continue;
        }

        var prefab = prototypes[tree.prototypeIndex].prefab;

        if (prefab == null)
        {
          continue;
        }

        var position = origin + Vector3.Scale(tree.position, size);
        var rotation = Quaternion.Euler(0f, tree.rotation * Mathf.Rad2Deg, 0f);
        var go = Instantiate(prefab, position, rotation, holder);
        var s = prefab.transform.localScale;
        go.transform.localScale = new Vector3(s.x * tree.widthScale, s.y * tree.heightScale, s.z * tree.widthScale);

        foreach (var col in go.GetComponentsInChildren<Collider>(true))
        {
          DestroyImmediate(col); // copia creada en juego: se quita ya (antes de buscar objetos)
        }

        created++;
      }

      if (created > 0)
      {
        terrain.treeDistance = 0f; // el Terrain deja de dibujarlos (el pasto sigue igual)
      }
      else
      {
        Destroy(holder.gameObject);
      }
    }
  }

  /// <summary>Vuelve a buscar los objetos que pueden tapar (llamar si se crean objetos grandes en juego).</summary>
  public void Rescan()
  {
    _candidates.Clear();
    var counts = new Dictionary<Transform, int>();

    foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
    {
      if (((1 << r.gameObject.layer) & _occluderLayers.value) == 0 || r.bounds.size.y < _minHeight)
      {
        continue;
      }

      var t = r.transform;

      if (IsExcluded(t) || t.GetComponentInParent<Player>() != null || t.GetComponentInParent<Enemy>() != null)
      {
        continue;
      }

      var c = new Candidate { renderer = r, colliders = FindColliders(t) };

      // Collider que cubre la forma -> exacto; si no (tronco de arbol) -> caja.
      var colliderVolume = 0f;
      foreach (var col in c.colliders) colliderVolume += Volume(col.bounds);
      c.useBox = c.colliders.Length == 0 || colliderVolume < Volume(r.bounds) * 0.4f;

      c.group = FindGroup(t, counts);
      c.groupBounds = r.bounds;
      foreach (var g in c.group) c.groupBounds.Encapsulate(g.bounds);
      _candidates.Add(c);
    }
  }

  private void LateUpdate()
  {
    if (_target == null)
    {
      var player = GameObject.FindWithTag("Player");
      _target = player != null ? player.transform : null;

      if (_target == null)
      {
        return;
      }

      _targetCollider = _target.GetComponent<Collider>();
    }

    if (_camera == null)
    {
      _camera = Camera.main;
    }

    var now = Time.unscaledTime;

    if (_rescanInterval > 0f && now >= _nextRescan)
    {
      _nextRescan = now + _rescanInterval;
      Rescan();
    }

    // Mientras se mira un objeto desde arriba (billar) no se desvanece nada.
    if (!InteractiveCameraFocus.IsActive && _camera != null)
    {
      BuildRays();
      var p = _target.position;
      var range = _searchRadius * _searchRadius;

      foreach (var c in _candidates)
      {
        if (c.renderer == null || !c.renderer.gameObject.activeInHierarchy)
        {
          continue;
        }

        var b = c.renderer.bounds;
        var closest = b.ClosestPoint(p);
        var dx = closest.x - p.x;
        var dz = closest.z - p.z;

        if (dx * dx + dz * dz > range || !Occludes(c, b))
        {
          continue;
        }

        // Desde fuera (jardin): se desvanece el objeto entero (casa con techo).
        // Desde dentro (nutria dentro de la casa): solo la pieza que tapa.
        var gb = c.groupBounds;
        var inside = p.x > gb.min.x && p.x < gb.max.x && p.z > gb.min.z && p.z < gb.max.z;

        if (inside)
        {
          Touch(c.renderer, now);
          continue;
        }

        foreach (var r in c.group)
        {
          if (r != null) Touch(r, now);
        }
      }
    }

    // Animar opacidad y devolver lo que ya no tapa.
    var dt = Time.unscaledDeltaTime;
    _done.Clear();

    foreach (var pair in _faded)
    {
      var f = pair.Value;

      if (f.renderer == null)
      {
        _done.Add(pair.Key);
        continue;
      }

      var covering = !InteractiveCameraFocus.IsActive && now - f.lastHit <= _showDelay;
      var targetAlpha = covering ? _minAlpha : 1f;
      var speed = (1f - _minAlpha) / (covering ? _fadeOutTime : _fadeInTime);
      f.alpha = Mathf.MoveTowards(f.alpha, targetAlpha, speed * dt);
      Apply(f);

      if (!covering && f.alpha >= 1f)
      {
        _done.Add(pair.Key);
      }
    }

    foreach (var r in _done)
    {
      Restore(r);
    }
  }

  // ---------- Lineas de vista ----------

  /// <summary>Lineas de vista desde la nutria (pies, pecho, cabeza, lados) hacia la camara.</summary>
  private void BuildRays()
  {
    Vector3 center;
    float feet;
    float head;

    if (_targetCollider != null && _targetCollider.enabled)
    {
      var b = _targetCollider.bounds;
      center = b.center;
      feet = b.min.y;
      head = b.max.y;
    }
    else
    {
      center = _target.position + Vector3.up * _targetHeight;
      feet = _target.position.y;
      head = _target.position.y + _targetHeight * 1.8f;
    }

    var cam = _camera.transform;
    var side = cam.right * _radius;

    SetRay(0, new Vector3(center.x, feet + 0.15f, center.z), cam);
    SetRay(1, center, cam);
    SetRay(2, new Vector3(center.x, head - 0.05f, center.z), cam);
    SetRay(3, center - side, cam);
    SetRay(4, center + side, cam);
  }

  private void SetRay(int i, Vector3 point, Transform cam)
  {
    Vector3 dir;
    float length;

    if (_camera.orthographic)
    {
      // Vista ortografica: la linea de vista es paralela al "forward" de la camara.
      dir = -cam.forward;
      length = Vector3.Dot(point - cam.position, cam.forward);
    }
    else
    {
      dir = cam.position - point;
      length = dir.magnitude;
      dir = length > 0.001f ? dir / length : Vector3.up;
    }

    const float start = 0.05f;
    _rays[i] = new Ray(point + dir * start, dir);
    _rayLengths[i] = Mathf.Max(0f, length - start);
  }

  private bool Occludes(Candidate c, Bounds b)
  {
    if (c.useBox)
    {
      b.extents *= _boxShrink;
    }

    for (var i = 0; i < RayCount; i++)
    {
      if (!b.IntersectRay(_rays[i], out var d) || d > _rayLengths[i])
      {
        continue;
      }

      if (c.useBox)
      {
        return true;
      }

      foreach (var col in c.colliders)
      {
        if (col != null && col.enabled && col.Raycast(_rays[i], out _, _rayLengths[i]))
        {
          return true;
        }
      }
    }

    return false;
  }

  // ---------- Preparacion ----------

  private static Collider[] FindColliders(Transform t)
  {
    var list = new List<Collider>();

    for (var level = t; level != null && list.Count == 0; level = level == t ? t.parent : null)
    {
      foreach (var col in level.GetComponents<Collider>())
      {
        if (!col.isTrigger) list.Add(col);
      }
    }

    return list.ToArray();
  }

  /// <summary>Sube por los padres mientras sean "un solo objeto" (pocas piezas, sin llegar a la raiz de la escena).</summary>
  private Renderer[] FindGroup(Transform t, Dictionary<Transform, int> counts)
  {
    var root = t;

    while (root.parent != null && root.parent.parent != null)
    {
      var parent = root.parent;

      if (!counts.TryGetValue(parent, out var n))
      {
        n = parent.GetComponentsInChildren<Renderer>(true).Length;
        counts[parent] = n;
      }

      if (n > _maxGroupRenderers || IsExcluded(parent))
      {
        break;
      }

      root = parent;
    }

    var group = new List<Renderer>();

    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
    {
      if (r is ParticleSystemRenderer || IsExcluded(r.transform)) continue;
      group.Add(r);
    }

    return group.ToArray();
  }

  private static float Volume(Bounds b) => Mathf.Max(b.size.x, 0.01f) * Mathf.Max(b.size.y, 0.01f) * Mathf.Max(b.size.z, 0.01f);

  private bool IsExcluded(Transform t)
  {
    if (t.GetComponentInParent<InteractiveCameraFocus>() != null)
    {
      return true;
    }

    if (_target != null && t.IsChildOf(_target))
    {
      return true;
    }

    if (_neverFade != null)
    {
      foreach (var root in _neverFade)
      {
        if (root != null && t.IsChildOf(root)) return true;
      }
    }

    return false;
  }

  // ---------- Desvanecido ----------

  private void Touch(Renderer r, float now)
  {
    if (!_faded.TryGetValue(r, out var f))
    {
      f = Begin(r);
      _faded[r] = f;
    }

    f.lastHit = now;
  }

  private Faded Begin(Renderer r)
  {
    var f = new Faded { renderer = r, original = r.sharedMaterials, shadowMode = r.shadowCastingMode };
    var copies = new Material[f.original.Length];
    f.colors = new Color[f.original.Length];
    var ok = f.original.Length > 0;

    for (var i = 0; i < f.original.Length && ok; i++)
    {
      var copy = TransparentCopy(f.original[i]);

      if (copy == null)
      {
        ok = false;
        break;
      }

      copies[i] = copy;
      f.colors[i] = copy.GetColor(BaseColorId);
    }

    if (ok)
    {
      f.transparent = true;
      r.sharedMaterials = copies;
    }

    return f;
  }

  private void Apply(Faded f)
  {
    if (f.transparent)
    {
      for (var i = 0; i < f.colors.Length; i++)
      {
        f.renderer.GetPropertyBlock(_block, i);
        var c = f.colors[i];
        c.a *= f.alpha;
        _block.SetColor(BaseColorId, c);
        f.renderer.SetPropertyBlock(_block, i);
      }
    }
    else
    {
      // Sin material compatible: se oculta dejando la sombra.
      f.renderer.shadowCastingMode = f.alpha < 0.99f ? ShadowCastingMode.ShadowsOnly : f.shadowMode;
    }
  }

  private void Restore(Renderer r)
  {
    if (_faded.TryGetValue(r, out var f) && r != null)
    {
      if (f.transparent)
      {
        r.sharedMaterials = f.original;

        for (var i = 0; i < f.original.Length; i++)
        {
          r.SetPropertyBlock(null, i);
        }
      }

      r.shadowCastingMode = f.shadowMode;
    }

    _faded.Remove(r);
  }

  /// <summary>Copia transparente del material (cacheada). Null si no se puede.</summary>
  private Material TransparentCopy(Material source)
  {
    if (source == null)
    {
      return null;
    }

    if (s_transparentCopies.TryGetValue(source, out var copy) && copy != null)
    {
      return copy;
    }

    if (source.HasProperty(BaseColorId) && source.HasProperty(SurfaceId))
    {
      // URP Lit (o compatible): mismo material en modo transparente.
      copy = new Material(source) { name = source.name + " (transparente)" };
      copy.SetFloat(SurfaceId, 1f);
      copy.SetFloat("_Blend", 0f);
      copy.SetOverrideTag("RenderType", "Transparent");
      copy.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
      copy.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
      if (copy.HasProperty("_SrcBlendAlpha")) copy.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
      if (copy.HasProperty("_DstBlendAlpha")) copy.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
      copy.SetFloat("_ZWrite", 0f);
      copy.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
      copy.DisableKeyword("_ALPHATEST_ON");
      copy.renderQueue = (int)RenderQueue.Transparent;
      copy.SetShaderPassEnabled("DepthOnly", false);
    }
    else if (_transparentTemplate != null)
    {
      // Otro shader (arboles, etc.): material de referencia con su textura y color.
      copy = new Material(_transparentTemplate) { name = source.name + " (transparente)" };
      var texName = source.HasProperty("_MainTex") ? "_MainTex" : source.HasProperty("_BaseMap") ? "_BaseMap" : null;

      if (texName != null)
      {
        copy.SetTexture(BaseMapId, source.GetTexture(texName));
        copy.SetTextureScale(BaseMapId, source.GetTextureScale(texName));
        copy.SetTextureOffset(BaseMapId, source.GetTextureOffset(texName));
      }

      copy.SetColor(BaseColorId, source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
      copy.SetShaderPassEnabled("DepthOnly", false);
    }
    else
    {
      return null;
    }

    s_transparentCopies[source] = copy;
    return copy;
  }

  private void OnDisable()
  {
    foreach (var r in new List<Renderer>(_faded.Keys))
    {
      Restore(r);
    }
  }

#if UNITY_EDITOR
  // Asigna solo el material de referencia.
  private void OnValidate()
  {
    if (_transparentTemplate == null && !Application.isPlaying)
    {
      _transparentTemplate = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Casa_Transparente_Ref.mat");
    }
  }
#endif
}
