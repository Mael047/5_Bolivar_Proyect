// CorruptionZone.cs
// Zona corrupta: un cubo (escala = tamaño de la zona) con el material
// ZonaCorrupta. Lo que queda dentro se ve distorsionado y morado; la nutria,
// los enemigos, las cajas, los letreros y los objetos con CorruptionExempt
// se ven normales.
//
// Cuando se derrotan los enemigos obligatorios de la zona, la corrupcion se
// desvanece y la zona queda limpia (se guarda con la partida).
//
// Enemigos obligatorios (el primero que tenga algo):
//   1. "Encounter": un combate (EnemyEncounter). Se limpia al ganarlo.
//   2. "Required Enemies": lista de enemigos (su Health).
//   3. Vacio: todos los enemigos que esten dentro del cubo al empezar.
//
// Crear: Tools > Zona corrupta > Crear zona corrupta (o GameObject > Zona corrupta).

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[RequireComponent(typeof(MeshRenderer))]
public class CorruptionZone : MonoBehaviour
{
  private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
  private const int PlayerLayer = 3;
  private const int EnemyLayer = 10;
  private const string CleanLayerName = "SinCorrupcion";

  /// <summary>Capa de lo que no se corrompe (letreros, objetos marcados). Sin nombre: la 13.</summary>
  public static int CleanLayer
  {
    get
    {
      var layer = LayerMask.NameToLayer(CleanLayerName);
      return layer >= 0 ? layer : 13;
    }
  }

  [Header("Enemigos obligatorios")]
  [SerializeField, Tooltip("Combate de la zona. Si esta, la zona se limpia al ganarlo.")]
  private EnemyEncounter _encounter;
  [SerializeField, Tooltip("Enemigos que hay que derrotar (si no hay combate). Vacio = los que esten dentro de la zona al empezar.")]
  private List<Health> _requiredEnemies = new List<Health>();

  [Header("Limpieza")]
  [SerializeField, Min(0f), Tooltip("Segundos que tarda en desaparecer la corrupcion.")]
  private float _clearDuration = 2.5f;
  [SerializeField, Min(0f), Tooltip("Espera tras derrotar al ultimo enemigo.")]
  private float _clearDelay = 0.6f;
  [SerializeField] private AudioClip _clearSound;
  [SerializeField, Range(0f, 1f)] private float _volume = 0.9f;

  [Header("Eventos")]
  public UnityEvent OnCleared = new UnityEvent();

  /// <summary>Se avisa al quedar limpia (lo usa el guardado).</summary>
  public event Action<CorruptionZone> Cleared;

  private MeshRenderer _renderer;
  private MaterialPropertyBlock _block;
  private bool _cleared;
  private bool _clearing;
  private float _nextCharacterScan;

  public bool IsCleared => _cleared;

  private void Awake()
  {
    _renderer = GetComponent<MeshRenderer>();
    _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    _renderer.receiveShadows = false;
    _block = new MaterialPropertyBlock();

    // Ignore Raycast: ni el desvanecido de paredes, ni el mapa, ni los rayos lo toman por un objeto.
    gameObject.layer = 2;

    // El cubo solo se ve: no choca con nada.
    foreach (var col in GetComponents<Collider>())
    {
      col.enabled = false;
    }

    SetIntensity(1f);
  }

  private void Start()
  {
    if (_cleared)
    {
      return;
    }

    if (_encounter != null)
    {
      _encounter.OnCleared.AddListener(BeginClear);
    }
    else if (_requiredEnemies.Count == 0)
    {
      // Todos los enemigos que esten dentro de la zona.
      foreach (var enemy in FindObjectsByType<Enemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
      {
        var health = enemy.GetComponentInParent<Health>();
        if (health == null) health = enemy.GetComponentInChildren<Health>();

        if (health != null && Contains(enemy.transform.position) && !_requiredEnemies.Contains(health))
        {
          _requiredEnemies.Add(health);
        }
      }
    }

    KeepCharactersClean();
  }

  private void Update()
  {
    if (_cleared || _clearing)
    {
      return;
    }

    // Enemigos que aparecen despues (emboscadas): tambien sin corrupcion.
    if (Time.time >= _nextCharacterScan)
    {
      _nextCharacterScan = Time.time + 2f;
      KeepCharactersClean();
    }

    if (_encounter == null && _requiredEnemies.Count > 0 && AllDefeated())
    {
      BeginClear();
    }
  }

  private bool AllDefeated()
  {
    foreach (var h in _requiredEnemies)
    {
      if (h != null && !h.IsDead) return false;
    }

    return true;
  }

  /// <summary>Punto dentro de la zona?</summary>
  public bool Contains(Vector3 worldPoint)
  {
    var p = transform.InverseTransformPoint(worldPoint);
    return Mathf.Abs(p.x) <= 0.5f && Mathf.Abs(p.y) <= 0.5f && Mathf.Abs(p.z) <= 0.5f;
  }

  // ---------- Limpiar ----------

  public void BeginClear()
  {
    if (_cleared || _clearing)
    {
      return;
    }

    StartCoroutine(ClearRoutine());
  }

  private IEnumerator ClearRoutine()
  {
    _clearing = true;
    yield return new WaitForSeconds(_clearDelay);

    if (_clearSound != null)
    {
      AudioSource.PlayClipAtPoint(_clearSound, transform.position, _volume);
    }

    var t = 0f;

    while (t < 1f)
    {
      t = _clearDuration > 0f ? Mathf.Min(1f, t + Time.deltaTime / _clearDuration) : 1f;
      SetIntensity(1f - t * t * (3f - 2f * t));
      yield return null;
    }

    Finish(true);
  }

  /// <summary>Partida guardada: ya estaba limpia.</summary>
  public void ClearImmediate()
  {
    StopAllCoroutines();
    Finish(false);
  }

  private void Finish(bool notify)
  {
    _cleared = true;
    _clearing = false;
    SetIntensity(0f);
    _renderer.enabled = false;

    if (notify)
    {
      OnCleared.Invoke();
      Cleared?.Invoke(this);
    }
  }

  private void SetIntensity(float value)
  {
    if (_renderer == null)
    {
      return;
    }

    _renderer.GetPropertyBlock(_block);
    _block.SetFloat(IntensityId, value);
    _renderer.SetPropertyBlock(_block);
  }

  // ---------- Personajes sin corrupcion ----------

  /// <summary>
  /// La nutria y los enemigos se vuelven a dibujar encima de la zona segun su
  /// capa (Player / Enemy). Sus piezas visibles que esten en otra capa (espada,
  /// escudo...) pasan a la capa de su personaje. Solo las que no tienen
  /// collider, para no cambiar la fisica.
  /// </summary>
  private static void KeepCharactersClean()
  {
    var player = FindAnyObjectByType<Player>();

    if (player != null)
    {
      MoveRenderers(player.transform, PlayerLayer);
    }

    foreach (var enemy in FindObjectsByType<Enemy>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
    {
      MoveRenderers(enemy.transform, EnemyLayer);
    }

    var clean = CleanLayer;

    // Letreros
    foreach (var c in FindObjectsByType<Collectibles.Collectible>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
    {
      if (c.IsSign) MoveRenderers(c.transform, clean);
    }

    // Cajas: el cuerpo ya esta en Grabbable; sus piezas visibles sueltas, a la capa limpia.
    foreach (var box in FindObjectsByType<NuiGrab.Breakable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
    {
      MoveRenderers(box.transform, clean, 8);
    }

    // Cualquier objeto marcado a mano
    foreach (var exempt in FindObjectsByType<CorruptionExempt>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
    {
      MoveRenderers(exempt.transform, clean);
    }
  }

  private static void MoveRenderers(Transform root, int layer, int alsoOk = -1)
  {
    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
    {
      var go = r.gameObject;

      if (go.layer == layer || go.layer == alsoOk || r is ParticleSystemRenderer || go.GetComponent<Collider>() != null)
      {
        continue;
      }

      go.layer = layer;
    }
  }

  // En el editor tambien en Ignore Raycast (asi el mapa horneado no lo dibuja).
  private void OnValidate()
  {
    if (gameObject.layer != 2)
    {
      gameObject.layer = 2;
    }
  }

  private void OnDrawGizmos()
  {
    Gizmos.matrix = transform.localToWorldMatrix;
    Gizmos.color = _cleared ? new Color(0.5f, 0.5f, 0.5f, 0.4f) : new Color(0.7f, 0.2f, 1f, 0.8f);
    Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
  }
}
