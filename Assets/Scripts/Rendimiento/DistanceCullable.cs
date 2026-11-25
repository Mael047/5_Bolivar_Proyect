// DistanceCullable.cs
// Objeto que se pausa cuando esta lejos del jugador y fuera de la vista
// (lo decide RenderOptimizer, en la camara). Al pausarse:
//   - deja de dibujarse (y de proyectar sombra),
//   - Animator y sonidos en bucle se detienen,
//   - las particulas se pausan,
//   - en enemigos, la IA (Enemy, ataques, daño por contacto) se detiene.
// Al volver, todo sigue donde estaba. El objeto NO se desactiva, asi que la
// vida, los encuentros, el guardado y el minimapa siguen funcionando igual.
// Solo se restaura lo que este componente apago.

using System.Collections.Generic;
using UnityEngine;

namespace Rendimiento
{
  [DisallowMultipleComponent]
  public class DistanceCullable : MonoBehaviour
  {
    public enum Kind
    {
      [Tooltip("Decorado: dibujo, animacion, particulas y sonido.")]
      Decorado,
      [Tooltip("Enemigo: lo mismo y ademas su IA.")]
      Enemigo
    }

    [SerializeField, Tooltip("Que se pausa.")]
    private Kind _kind = Kind.Decorado;

    [SerializeField, Tooltip("Se mueve (enemigos): su posicion se actualiza en cada revision.")]
    private bool _moves;

    [SerializeField, Tooltip("Pausar tambien los renderers (dibujo y sombra). Apagalo si otro script los enciende/apaga.")]
    private bool _pauseRenderers = true;

    [SerializeField, Min(0f), Tooltip("Radio del objeto. 0 = se calcula del tamaño de sus renderers.")]
    private float _radius;

    [SerializeField, Tooltip("Scripts extra a pausar (ademas de los de enemigo que se detectan solos).")]
    private List<Behaviour> _extraScripts = new List<Behaviour>();

    /// <summary>Si es true no se pausa nunca (p. ej. mientras persigue al jugador).</summary>
    public bool KeepActive { get; set; }

    public bool Moves => _moves;
    public bool IsPaused { get; private set; }
    internal int Index { get; set; } = -1;

    // Scripts de enemigo que se pausan (por nombre, para no depender de ellos).
    private static readonly HashSet<string> EnemyScripts = new HashSet<string>
    {
      "Enemy", "ChargeAttack", "RangedAttack", "ContactDamage", "EnemyAnimator"
    };

    private readonly List<Renderer> _renderers = new List<Renderer>();
    private readonly List<Behaviour> _behaviours = new List<Behaviour>();
    private readonly List<ParticleSystem> _particles = new List<ParticleSystem>();
    private readonly List<AudioSource> _audio = new List<AudioSource>();

    // Lo que este componente apago, para devolver solo eso.
    private readonly List<Renderer> _offRenderers = new List<Renderer>();
    private readonly List<Behaviour> _offBehaviours = new List<Behaviour>();
    private readonly List<ParticleSystem> _pausedParticles = new List<ParticleSystem>();
    private readonly List<AudioSource> _pausedAudio = new List<AudioSource>();

    private Rigidbody _body;
    private float _autoRadius = 1f;
    private Vector3 _centerOffset;

    private void Awake()
    {
      Collect();
    }

    private void OnEnable()
    {
      RenderOptimizer.Register(this);
    }

    private void OnDisable()
    {
      SetPaused(false);
      RenderOptimizer.Unregister(this);
    }

    /// <summary>Vuelve a buscar renderers, animators, etc. (si se añaden hijos en juego).</summary>
    public void Collect()
    {
      _renderers.Clear();
      _behaviours.Clear();
      _particles.Clear();
      _audio.Clear();

      GetComponentsInChildren(true, _renderers);
      GetComponentsInChildren(true, _particles);
      GetComponentsInChildren(true, _audio);

      // Los renderers de particulas los maneja la pausa de particulas.
      _renderers.RemoveAll(r => r is ParticleSystemRenderer);

      foreach (var animator in GetComponentsInChildren<Animator>(true))
      {
        _behaviours.Add(animator);
      }

      if (_kind == Kind.Enemigo)
      {
        foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
        {
          if (mb != null && mb != this && EnemyScripts.Contains(mb.GetType().Name))
          {
            _behaviours.Add(mb);
          }
        }

        _body = GetComponent<Rigidbody>();
      }

      foreach (var extra in _extraScripts)
      {
        if (extra != null && !_behaviours.Contains(extra)) _behaviours.Add(extra);
      }

      // Tamaño aproximado a partir de lo que se ve.
      var has = false;
      var bounds = new Bounds(transform.position, Vector3.one);

      foreach (var r in _renderers)
      {
        if (r == null) continue;
        if (!has) { bounds = r.bounds; has = true; }
        else bounds.Encapsulate(r.bounds);
      }

      foreach (var ps in _particles)
      {
        var psr = ps.GetComponent<ParticleSystemRenderer>();
        if (psr == null) continue;
        if (!has) { bounds = psr.bounds; has = true; }
        else bounds.Encapsulate(psr.bounds);
      }

      _autoRadius = Mathf.Max(0.5f, bounds.extents.magnitude);
      _centerOffset = bounds.center - transform.position;

      // Particulas: su caja puede ser enorme o cero; se limita.
      _autoRadius = Mathf.Min(_autoRadius, 30f);
    }

    internal BoundingSphere GetSphere(float padding)
    {
      var radius = _radius > 0f ? _radius : _autoRadius;
      return new BoundingSphere(transform.position + _centerOffset, radius + padding);
    }

    public void SetPaused(bool paused)
    {
      if (paused == IsPaused)
      {
        return;
      }

      IsPaused = paused;

      if (paused)
      {
        if (_pauseRenderers)
        {
          foreach (var r in _renderers)
          {
            if (r != null && r.enabled)
            {
              r.enabled = false;
              _offRenderers.Add(r);
            }
          }
        }

        foreach (var b in _behaviours)
        {
          if (b != null && b.enabled)
          {
            b.enabled = false;
            _offBehaviours.Add(b);
          }
        }

        foreach (var ps in _particles)
        {
          if (ps != null && ps.isPlaying)
          {
            ps.Pause(false);
            var psr = ps.GetComponent<ParticleSystemRenderer>();
            if (psr != null) psr.enabled = false;
            _pausedParticles.Add(ps);
          }
        }

        foreach (var a in _audio)
        {
          if (a != null && a.isPlaying && a.loop)
          {
            a.Pause();
            _pausedAudio.Add(a);
          }
        }

        if (_body != null && !_body.isKinematic)
        {
          _body.linearVelocity = Vector3.zero;
          _body.Sleep();
        }
      }
      else
      {
        foreach (var r in _offRenderers) if (r != null) r.enabled = true;
        foreach (var b in _offBehaviours) if (b != null) b.enabled = true;

        foreach (var ps in _pausedParticles)
        {
          if (ps == null) continue;
          var psr = ps.GetComponent<ParticleSystemRenderer>();
          if (psr != null) psr.enabled = true;
          ps.Play(false);
        }

        foreach (var a in _pausedAudio) if (a != null) a.UnPause();

        _offRenderers.Clear();
        _offBehaviours.Clear();
        _pausedParticles.Clear();
        _pausedAudio.Clear();
      }
    }

    private void OnDrawGizmosSelected()
    {
      var radius = _radius > 0f ? _radius : _autoRadius;
      Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.5f);
      Gizmos.DrawWireSphere(Application.isPlaying ? transform.position + _centerOffset : transform.position, radius);
    }
  }
}
