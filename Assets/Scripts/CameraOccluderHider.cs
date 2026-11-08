// CameraOccluderHider.cs
// Para interiores: lo que queda entre la camara y el jugador (paredes,
// muebles altos) se vuelve transparente poco a poco, y vuelve a aparecer
// suave cuando deja de tapar. Asi la nutria nunca se pierde de vista.
//
// Funciona con materiales URP Lit (los normales del proyecto): mientras tapa,
// el objeto usa una copia transparente de su material y se desvanece hasta
// "Min Alpha". Con otros shaders se oculta dejando solo su sombra.
//
// Va en la camara principal. Solo afecta a objetos con collider en las capas
// de "Occluder Layers" (por defecto Default).

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class CameraOccluderHider : MonoBehaviour
{
  private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
  private static readonly int SurfaceId = Shader.PropertyToID("_Surface");

  [SerializeField, Tooltip("A quien no se debe tapar (vacio = objeto con tag Player).")]
  private Transform _target;
  [SerializeField, Tooltip("Altura del punto del jugador que se comprueba.")]
  private float _targetHeight = 0.8f;
  [SerializeField, Min(0f), Tooltip("Grosor del rayo (cuanto margen se despeja alrededor del jugador).")]
  private float _radius = 0.6f;
  [SerializeField, Tooltip("Capas que se pueden desvanecer.")]
  private LayerMask _occluderLayers = 1;
  [SerializeField, Tooltip("Objetos que nunca se desvanecen (con sus hijos). Los objetos con InteractiveCameraFocus, como la mesa de billar, tampoco se desvanecen nunca.")]
  private Transform[] _neverFade;

  [Header("Desvanecido")]
  [SerializeField, Range(0f, 1f), Tooltip("Opacidad minima de lo que tapa (0 = invisible).")]
  private float _minAlpha = 0.18f;
  [SerializeField, Min(0.05f), Tooltip("Segundos en desvanecerse.")]
  private float _fadeOutTime = 0.25f;
  [SerializeField, Min(0.05f), Tooltip("Segundos en volver a aparecer.")]
  private float _fadeInTime = 0.45f;
  [SerializeField, Min(0f), Tooltip("Segundos que sigue desvanecido tras dejar de tapar (evita parpadeos).")]
  private float _showDelay = 0.2f;
  [SerializeField, Tooltip("Material URP Lit transparente de referencia (lo asigna la herramienta). Hace que la build incluya la version transparente del shader.")]
  private Material _transparentTemplate;

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

  private static readonly Dictionary<Material, Material> s_transparentCopies = new Dictionary<Material, Material>();

  private readonly Dictionary<Renderer, Faded> _faded = new Dictionary<Renderer, Faded>();
  private readonly List<Renderer> _done = new List<Renderer>();
  private readonly RaycastHit[] _hits = new RaycastHit[32];
  private MaterialPropertyBlock _block;

  private void Awake()
  {
    _block = new MaterialPropertyBlock();
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
    }

    var now = Time.unscaledTime;

    // Mientras se mira un objeto desde arriba (billar) no se desvanece nada.
    if (!InteractiveCameraFocus.IsActive)
    {
      var from = transform.position;
      var to = _target.position + Vector3.up * _targetHeight;
      var dir = to - from;
      var distance = dir.magnitude;

      if (distance > 0.01f)
      {
        var count = Physics.SphereCastNonAlloc(from, _radius, dir / distance, _hits, distance, _occluderLayers, QueryTriggerInteraction.Ignore);

        for (var i = 0; i < count; i++)
        {
          var col = _hits[i].collider;

          if (col.transform.IsChildOf(_target) || col.GetComponentInParent<Player>() != null || IsExcluded(col.transform))
          {
            continue;
          }

          foreach (var r in col.GetComponentsInChildren<Renderer>())
          {
            Touch(r, now);
          }
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

  private bool IsExcluded(Transform t)
  {
    if (t.GetComponentInParent<InteractiveCameraFocus>() != null)
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

  private void Touch(Renderer r, float now)
  {
    if (r is ParticleSystemRenderer)
    {
      return;
    }

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
    var allLit = f.original.Length > 0;

    for (var i = 0; i < f.original.Length; i++)
    {
      var m = f.original[i];

      if (m == null || !m.HasProperty(BaseColorId) || !m.HasProperty(SurfaceId))
      {
        allLit = false;
        break;
      }

      f.colors[i] = m.GetColor(BaseColorId);
      copies[i] = TransparentCopy(m);
    }

    if (allLit)
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

  private Material TransparentCopy(Material source)
  {
    if (s_transparentCopies.TryGetValue(source, out var copy) && copy != null)
    {
      return copy;
    }

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
}
