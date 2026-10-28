using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace NuiGrab
{
  [RequireComponent(typeof(Rigidbody))]
  public class Breakable : MonoBehaviour
  {
    [SerializeField, Min(0.1f)] private float _breakSpeed = 5f;
    [SerializeField] private bool _breakOnFloor = false;
    [SerializeField, Range(2, 4)] private int _fragmentGrid = 2;
    [SerializeField, Min(0.5f)] private float _fragmentLifetime = 4f;
    [SerializeField] private GameObject _containedPickup;
    [SerializeField] private GameObject[] _fragmentPrefabsOverride;

    [Tooltip("Los pedazos no chocan con el jugador ni con los enemigos (no los levantan al pisarlos). Siguen cayendo y rebotando en el suelo.")]
    [SerializeField] private bool _fragmentsIgnoreCharacters = true;

    [Header("Textura de los fragmentos")]
    [Tooltip("0 = cada pedazo usa un color plano tomado del punto de la malla " +
             "original mas cercano (ideal para atlas de paleta). 1 = UV completas " +
             "de la superficie. Valores bajos evitan que se cuele otra zona del atlas.")]
    [SerializeField, Range(0f, 1f)] private float _fragmentUvBlend = 0.3f;

        [Header("Sonidos")]
        public AudioClip clip;

    [Header("Solo con espada (puertas, barricadas...)")]
    [Tooltip("Solo se rompe con la espada del jugador: cajas, objetos lanzados o la mano no le hacen nada. El objeto queda fijo (Rigidbody kinematico).")]
    [SerializeField] private bool _onlySword;

    [Tooltip("Espadazos necesarios para romperlo.")]
    [SerializeField, Min(1)] private int _swordHits = 1;

    [Tooltip("Sonido de cada espadazo que todavia no lo rompe (vacio = ninguno).")]
    [SerializeField] private AudioClip _swordHitClip;

    [Tooltip("Temblor de camara en cada espadazo que no lo rompe.")]
    [SerializeField, Range(0f, 1f)] private float _swordHitShake = 0.15f;

    [Tooltip("Cuanto se sacude el objeto (m) al recibir un espadazo.")]
    [SerializeField, Min(0f)] private float _swordHitWobble = 0.06f;

    [Header("Daño a enemigos")]
    [Tooltip("Daño que hace este objeto al golpear a un enemigo (lanzado o estrellado mientras se sostiene con la mano). 0 = no hace daño.")]
    [SerializeField, Min(0)] private int _enemyDamage = 20;

    [Tooltip("Velocidad minima (m/s) del golpe para hacer daño. Evita dañar al rozar o al apoyar el objeto en el enemigo.")]
    [SerializeField, Min(0f)] private float _minDamageSpeed = 3f;

    [Tooltip("El objeto se rompe al golpear a un enemigo (aunque no llegue a 'Break Speed').")]
    [SerializeField] private bool _breakOnEnemyHit = true;

    [Tooltip("Fuerza de la reaccion del enemigo (empuje, temblor, aturdimiento). 1 = como un espadazo normal.")]
    [SerializeField, Range(0f, 3f)] private float _hitIntensity = 1f;

    [Tooltip("Temblor de camara al golpear.")]
    [SerializeField, Range(0f, 1f)] private float _hitCameraShake = 0.25f;

    // Evita varios golpes al mismo enemigo en un mismo choque (varios colliders / frames seguidos).
    private readonly Dictionary<UnityEngine.Object, float> _lastHitTime = new Dictionary<UnityEngine.Object, float>();
    private const float HitCooldown = 0.4f;


    /// <summary>Se dispara justo antes de destruir el objeto.</summary>
    public event Action<Breakable> OnBroken;

    public bool IsBroken => _broken;

    /// <summary>True si solo se puede romper con la espada.</summary>
    public bool OnlySword => _onlySword;
    public float BreakSpeed => _breakSpeed;
    public bool BreaksOnFloor => _breakOnFloor;

    private Rigidbody _rb;
    private HandGrabManager _grabManager;
    private bool _broken;
    private int _swordHitCount;
    private Coroutine _wobbleRoutine;
    private Vector3 _restPosition;

    private void Awake()
    {
      _rb = GetComponent<Rigidbody>();

      if (_onlySword)
      {
        // Puertas y barricadas: fijas, nada las empuja.
        _rb.isKinematic = true;
      }

      _restPosition = transform.position;
    }

    /// <summary>
    /// Golpe de la espada del jugador. Si el objeto es "Solo con espada" cuenta
    /// los golpes hasta romperse; si no, se rompe directamente.
    /// </summary>
    public void HitWithSword(Vector3 point, Vector3 direction)
    {
      if (_broken)
      {
        return;
      }

      var push = direction.sqrMagnitude > 0.0001f ? direction.normalized * 6f : Vector3.zero;

      if (!_onlySword || ++_swordHitCount >= _swordHits)
      {
        Break(point, push);
        return;
      }

      if (_swordHitShake > 0f)
      {
        TopDownCamera.AddShake(_swordHitShake);
      }

      if (_swordHitClip != null && AudioManager.Instance != null)
      {
        AudioManager.Instance.playClip(_swordHitClip);
      }

      if (_swordHitWobble > 0f)
      {
        if (_wobbleRoutine != null)
        {
          StopCoroutine(_wobbleRoutine);
          transform.position = _restPosition;
        }

        _wobbleRoutine = StartCoroutine(Wobble(push.sqrMagnitude > 0f ? push.normalized : transform.forward));
      }
    }

    private System.Collections.IEnumerator Wobble(Vector3 direction)
    {
      _restPosition = transform.position;
      const float duration = 0.25f;
      var t = 0f;

      while (t < duration)
      {
        t += Time.deltaTime;
        var k = 1f - t / duration;
        var offset = Mathf.Sin(t * 70f) * _swordHitWobble * k;
        transform.position = _restPosition + direction * offset;
        yield return null;
      }

      transform.position = _restPosition;
      _wobbleRoutine = null;
    }

    private void OnCollisionEnter(Collision collision)
    {
      // Solo con espada: los choques (cajas, objetos lanzados) no lo rompen.
      if (_broken || _onlySword)
      {
        return;
      }

      if (IsPlayerCollision(collision))
      {
        return;
      }

      var speed = ImpactSpeed(collision, out var floorHit);

      // Golpe a un enemigo: daño (y rotura si esta configurado).
      if (collision.contactCount > 0)
      {
        var velocity = _rb.isKinematic && _grabManager != null ? _grabManager.HeldVelocity : -collision.relativeVelocity;

        if (velocity.sqrMagnitude < speed * speed)
        {
          velocity = velocity.normalized * speed;
        }

        if (TryDamageEnemy(collision.collider, collision.GetContact(0).point, velocity))
        {
          return;
        }
      }

      if (floorHit && !_breakOnFloor)
      {
        return;
      }

      if (speed >= _breakSpeed)
      {
        Break(collision);
      }
    }

    /// <summary>
    /// True si algun contacto de la colision pertenece al cuerpo del jugador.
    /// </summary>
    private bool IsPlayerCollision(Collision collision)
    {
      for (int i = 0; i < collision.contactCount; i++)
      {
        var other = collision.GetContact(i).otherCollider;
        if (other != null && other.GetComponentInParent<global::Player>() != null)
        {
          return true;
        }
      }

      return false;
    }

    /// <summary>
    /// Si 'other' es un enemigo (IDamageable que no es el jugador) y el golpe es
    /// lo bastante rapido, le hace daño, lo hace reaccionar y (opcional) rompe
    /// el objeto. Lo usan el choque fisico (objeto lanzado) y HandGrabManager
    /// (objeto estrellado mientras se sostiene). Devuelve true si golpeo.
    /// </summary>
    public bool TryDamageEnemy(Collider other, Vector3 point, Vector3 velocity)
    {
      if (_broken || _onlySword || _enemyDamage <= 0 || other == null || velocity.magnitude < _minDamageSpeed)
      {
        return false;
      }

      if (other.GetComponentInParent<global::Player>() != null)
      {
        return false;
      }

      var damageable = other.GetComponentInParent<IDamageable>();
      var key = damageable as UnityEngine.Object;

      if (damageable == null || key == null)
      {
        return false;
      }

      if (_lastHitTime.TryGetValue(key, out var last) && Time.time - last < HitCooldown)
      {
        return true;
      }

      _lastHitTime[key] = Time.time;
      damageable.TakeDamage(_enemyDamage);

      var reaction = other.GetComponentInParent<HitReaction>();
      if (reaction != null && _hitIntensity > 0f)
      {
        reaction.React(point, velocity, transform, _hitIntensity);
      }

      if (_hitCameraShake > 0f)
      {
        TopDownCamera.AddShake(_hitCameraShake);
      }

      if (_breakOnEnemyHit)
      {
        Break(point, velocity);
      }

      return true;
    }

    /// <summary>Rompe el objeto explicitamente (enemigos, armas, trampas...).</summary>
    public void Break(Collision cause = null)
    {
        var impactPoint = cause != null && cause.contactCount > 0
            ? cause.GetContact(0).point
            : transform.position;
        var impactVelocity = cause != null ? cause.relativeVelocity : Vector3.zero;

        Break(impactPoint, impactVelocity);
    }

    /// <summary>
    /// Rompe el objeto con datos de impacto sinteticos. Lo usa HandGrabManager
    /// cuando el objeto agarrado (kinematico) es frenado contra una superficie:
    /// el barrido del agarre evita el contacto fisico, asi que la colision se
    /// notifica a mano con la velocidad estimada del golpe.
    /// </summary>
    public void Break(Vector3 impactPoint, Vector3 impactVelocity)
    {
        if (_broken)
        {
            return;
        }

        _broken = true;

      // El objeto agarrado que se rompe libera la mano antes de desaparecer.
      if (_grabManager == null)
      {
        _grabManager = FindAnyObjectByType<HandGrabManager>();
      }

      if (_grabManager != null && _grabManager.IsHoldingBody(_rb))
      {
        _grabManager.ForceRelease();
      }

      SpawnFragments(impactPoint, impactVelocity);

      if (_containedPickup != null)
      {
        Instantiate(_containedPickup, transform.position + Vector3.up * 0.25f, Quaternion.identity);
      }
      if (AudioManager.Instance != null)
      {
        AudioManager.Instance.playClip(clip);
      }
      OnBroken?.Invoke(this);
      Destroy(gameObject);
    }

    /// <summary>
    /// Velocidad efectiva del impacto y si alguno de los contactos es contra el
    /// suelo (normal apuntando hacia arriba).
    /// </summary>
    private float ImpactSpeed(Collision collision, out bool floorHit)
    {
      floorHit = false;

      for (int i = 0; i < collision.contactCount; i++)
      {
        if (collision.GetContact(i).normal.y > 0.6f)
        {
          floorHit = true;
          break;
        }
      }

      // Defensa extra: las piezas del puzzle nunca se rompen.
      if (GetComponent<PuzzlePiece>() != null)
      {
        return 0f;
      }

      if (_rb.isKinematic)
      {
        // Agarrado por la mano: la fisica reporta ~0 en kinematicos, asi que se
        // usa la velocidad estimada del seguimiento manual.
        if (_grabManager == null)
        {
          _grabManager = FindAnyObjectByType<HandGrabManager>();
        }

        return _grabManager != null && _grabManager.IsHoldingBody(_rb)
            ? _grabManager.HeldVelocity.magnitude
            : 0f;
      }

      return collision.relativeVelocity.magnitude;
    }

    private void SpawnFragments(Vector3 impactPoint, Vector3 impactVelocity)
    {
      var renderer = GetComponent<Renderer>();

      if (renderer == null)
      {
        return;
      }

      // Desactiva los colliders del original: evita empujones raros durante el
      // frame restante antes de que Destroy haga efecto.
      foreach (var collider in GetComponentsInChildren<Collider>())
      {
        collider.enabled = false;
      }

      var spawnedColliders = new List<Collider>();
      var totalMass = SourceMass();
      var material = renderer.sharedMaterial;

      if (_fragmentPrefabsOverride != null && _fragmentPrefabsOverride.Length > 0)
      {
        // Ruta para modelos importados: prefabs de pedazos hechos a mano.
        var perFragmentMass = totalMass / Mathf.Max(1, CountValid(_fragmentPrefabsOverride));

        foreach (var prefab in _fragmentPrefabsOverride)
        {
          if (prefab == null)
          {
            continue;
          }

          var fragment = Instantiate(prefab, transform.position, transform.rotation);
          CollectAndLaunch(fragment, perFragmentMass, impactPoint, impactVelocity, spawnedColliders);
        }
      }
      else
      {
        // Ruta procedural: divide la malla original en una rejilla NxNxN de
        // cubos. Las UV de cada cubo se muestrean de la malla original para que
        // pinte la misma zona del atlas que la superficie de la que salio; un
        // cubo primitivo de Unity trae UV 0..1 y con un atlas compartido acaba
        // mostrando las texturas de otros modelos.
        var grid = Mathf.Max(2, _fragmentGrid);
        var meshFilter = GetComponent<MeshFilter>();
        var sourceMesh = meshFilter != null ? meshFilter.sharedMesh : null;
        var sampler = new MeshUvSampler(sourceMesh);

        if (!sampler.IsValid)
        {
          Debug.LogWarning(
              $"[Breakable] '{name}': no se pudieron leer las UV de la malla " +
              "original, los fragmentos usaran UV planas. Activa 'Read/Write " +
              "Enabled' en los ajustes de importacion del modelo.",
              this);
        }

        // Rejilla en espacio local de la malla: asi funciona tambien con el
        // objeto rotado, a diferencia del AABB en mundo del renderer.
        var localBounds = sourceMesh != null
            ? sourceMesh.bounds
            : new Bounds(transform.InverseTransformPoint(renderer.bounds.center), Vector3.one);

        var cellSize = localBounds.size / grid;
        var lossyScale = transform.lossyScale;
        var fill = 0.88f;
        var worldSize = new Vector3(
            Mathf.Abs(cellSize.x * lossyScale.x) * fill,
            Mathf.Abs(cellSize.y * lossyScale.y) * fill,
            Mathf.Abs(cellSize.z * lossyScale.z) * fill);

        var perFragmentMass = totalMass / (grid * grid * grid);

        for (int x = 0; x < grid; x++)
        {
          for (int y = 0; y < grid; y++)
          {
            for (int z = 0; z < grid; z++)
            {
              var localCenter = localBounds.min + new Vector3(
                  (x + 0.5f) * cellSize.x,
                  (y + 0.5f) * cellSize.y,
                  (z + 0.5f) * cellSize.z);

              var fragment = new GameObject(name + "_frag");
              fragment.transform.SetPositionAndRotation(
                  transform.TransformPoint(localCenter), transform.rotation);

              var mesh = FragmentMeshFactory.CreateFragmentCube(
                  worldSize, fragment.transform, transform, sampler, _fragmentUvBlend);

              fragment.AddComponent<MeshFilter>().sharedMesh = mesh;

              var meshRenderer = fragment.AddComponent<MeshRenderer>();
              meshRenderer.sharedMaterial = material;
              meshRenderer.shadowCastingMode = renderer.shadowCastingMode;

              fragment.AddComponent<BoxCollider>().size = worldSize;

              CollectAndLaunch(fragment, perFragmentMass, impactPoint, impactVelocity, spawnedColliders);

              // La malla se genera en runtime: el pedazo la libera al morir.
              var fragmentComponent = fragment.GetComponent<Fragment>();
              if (fragmentComponent != null)
              {
                fragmentComponent.SetOwnedMesh(mesh);
              }
            }
          }
        }
      }

      // Los pedazos nacen solapados entre si: ignora sus colisiones mutuas
      // durante toda su vida corta para evitar estallidos artificiales.
      for (int i = 0; i < spawnedColliders.Count; i++)
      {
        for (int j = i + 1; j < spawnedColliders.Count; j++)
        {
          Physics.IgnoreCollision(spawnedColliders[i], spawnedColliders[j]);
        }
      }

      if (_fragmentsIgnoreCharacters)
      {
        IgnoreCharacters(spawnedColliders);
      }
    }

    /// <summary>
    /// Los pedazos atraviesan al jugador y a los enemigos: sus capsulas se
    /// subian encima de los pedazos y los levantaban.
    /// </summary>
    private static void IgnoreCharacters(List<Collider> fragments)
    {
      var characters = new List<Collider>();

      var player = FindAnyObjectByType<global::Player>();
      if (player != null)
      {
        AddSolid(player.GetComponentsInChildren<Collider>(true), characters);
      }

      foreach (var enemy in FindObjectsByType<Enemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
      {
        AddSolid(enemy.GetComponentsInChildren<Collider>(true), characters);
      }

      foreach (var fragment in fragments)
      {
        if (fragment == null) continue;

        foreach (var character in characters)
        {
          Physics.IgnoreCollision(fragment, character);
        }
      }
    }

    private static void AddSolid(Collider[] colliders, List<Collider> into)
    {
      foreach (var c in colliders)
      {
        if (c != null && !c.isTrigger)
        {
          into.Add(c);
        }
      }
    }

    private void CollectAndLaunch(
        GameObject fragment,
        float mass,
        Vector3 impactPoint,
        Vector3 impactVelocity,
        List<Collider> spawnedColliders)
    {
      // Los fragmentos viven en capa normal: la mano no puede agarrarlos.
      fragment.layer = 0;

      var body = fragment.GetComponent<Rigidbody>();
      if (body == null)
      {
        body = fragment.AddComponent<Rigidbody>();
      }

      body.mass = Mathf.Max(mass, 0.01f);
      body.interpolation = RigidbodyInterpolation.Interpolate;

      var direction = fragment.transform.position - impactPoint;
      direction = direction.sqrMagnitude > 0.0001f
          ? direction.normalized
          : UnityEngine.Random.onUnitSphere;

      // Hereda parte del impulso del golpe y anade una explosion radial corta
      // con sesgo hacia arriba para que los pedazos "salten".
      body.linearVelocity = impactVelocity * 0.45f
          + direction * UnityEngine.Random.Range(1.5f, 3f)
          + Vector3.up * UnityEngine.Random.Range(0.25f, 0.75f);
      body.angularVelocity = new Vector3(
          UnityEngine.Random.Range(-4f, 4f),
          UnityEngine.Random.Range(-4f, 4f),
          UnityEngine.Random.Range(-4f, 4f));

      var fragmentComponent = fragment.GetComponent<Fragment>();
      if (fragmentComponent == null)
      {
        fragmentComponent = fragment.AddComponent<Fragment>();
      }

      fragmentComponent.Init(_fragmentLifetime);

      foreach (var collider in fragment.GetComponentsInChildren<Collider>())
      {
        spawnedColliders.Add(collider);
      }
    }

    private int CountValid(GameObject[] array)
    {
      var count = 0;

      foreach (var item in array)
      {
        if (item != null)
        {
          count++;
        }
      }

      return count;
    }

    private float SourceMass()
    {
      return _rb != null && _rb.mass > 0f ? _rb.mass : 1f;
    }
  }
}
