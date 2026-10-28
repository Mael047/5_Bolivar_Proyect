// EnemyEncounter.cs
// Zona de combate: cuando todos los enemigos de la lista son derrotados,
// suelta sus recompensas (llaves, hojas de vida, lo que quieras) con su
// animacion de salir y flotar.
//
// Recompensas: lista en "Rewards". Cada una tiene su prefab, cuantas salen,
// probabilidad, y donde aparecen (donde murio el ultimo enemigo, en el centro
// o en uno o varios puntos de aparicion).
//
// Opcional:
//   - Empezar al entrar el jugador en el radio (si no, vigila desde el inicio).
//   - Emboscada: los enemigos aparecen al entrar.
//   - Barreras: objetos que se activan al empezar (encierran al jugador) y se
//     quitan al ganar.
//
// Uso: crea un objeto vacio en el centro de la zona, añade este componente,
// arrastra los enemigos a "Enemies" y añade recompensas en "Rewards".
// Para puntos de aparicion: crea objetos vacios (hijos de la zona, por
// ejemplo) y arrastralos a "Spawn Points" de la recompensa.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Random = UnityEngine.Random;

[DisallowMultipleComponent]
public class EnemyEncounter : MonoBehaviour
{
  public enum DropPlace
  {
    [Tooltip("Donde murio el ultimo enemigo.")]
    UltimoEnemigo,
    [Tooltip("En los puntos de 'Spawn Points'.")]
    PuntosDeAparicion,
    [Tooltip("En el centro de la zona (este objeto).")]
    CentroDeLaZona
  }

  public enum PointMode
  {
    [Tooltip("Cada copia va al siguiente punto de la lista (1, 2, 3, 1...).")]
    EnOrden,
    [Tooltip("Cada copia va a un punto al azar.")]
    AlAzar,
    [Tooltip("Sale 'Amount' en CADA punto.")]
    EnTodosLosPuntos
  }

  [Serializable]
  public class Reward
  {
    [Tooltip("Prefab que se suelta (llave, hoja de vida...). Ponle FloatingPickup para que salga flotando.")]
    public GameObject prefab;
    [Min(1), Tooltip("Cuantas copias.")]
    public int amount = 1;
    [Range(0f, 1f), Tooltip("Probabilidad de que salga (1 = siempre).")]
    public float chance = 1f;
    public DropPlace dropAt = DropPlace.UltimoEnemigo;
    [Tooltip("Puntos de aparicion (para 'Puntos De Aparicion').")]
    public Transform[] spawnPoints;
    public PointMode pointMode = PointMode.EnOrden;
    [Tooltip("Si no esta vacio, cambia el tipo de llave (KeyPickup).")]
    public string overrideKeyId = "";
  }

  [Header("Enemigos")]
  [SerializeField, Tooltip("Enemigos a derrotar (su componente Health). Vacio = los enemigos hijos de este objeto.")]
  private List<Health> _enemies = new List<Health>();

  [Header("Inicio")]
  [SerializeField, Min(0f), Tooltip("El combate empieza cuando el jugador entra en este radio. 0 = desde el inicio.")]
  private float _startRadius = 8f;
  [SerializeField, Tooltip("Emboscada: los enemigos estan ocultos y aparecen al empezar.")]
  private bool _spawnEnemiesOnStart;
  [SerializeField, Tooltip("Se activan al empezar y se desactivan al ganar (rejas, muros de espinas...).")]
  private GameObject[] _barriers;

  [Header("Recompensas")]
  [SerializeField] private List<Reward> _rewards = new List<Reward>();
  [SerializeField, Min(0f), Tooltip("Espera antes de soltarlas (para ver la muerte del enemigo).")]
  private float _dropDelay = 0.8f;
  [SerializeField, Min(0f), Tooltip("Tiempo entre una recompensa y la siguiente (salen en cascada).")]
  private float _dropInterval = 0.15f;
  [SerializeField, Min(0f), Tooltip("Si salen varias en el mismo sitio, se reparten en este radio para no encimarse.")]
  private float _scatterRadius = 0.9f;
  [SerializeField] private AudioClip _clearedSound;

  // Version anterior (una sola recompensa): se pasa sola a la lista.
  [SerializeField, HideInInspector] private GameObject _reward;
  [SerializeField, HideInInspector] private DropPlace _dropAt = DropPlace.UltimoEnemigo;
  [SerializeField, HideInInspector] private Transform _dropPoint;
  [SerializeField, HideInInspector] private string _overrideKeyId = "";

  [Header("Eventos")]
  public UnityEvent OnStarted = new UnityEvent();
  public UnityEvent OnCleared = new UnityEvent();

  private enum State { Esperando, EnCombate, Ganado }

  private State _state = State.Esperando;
  private Transform _player;
  private Vector3 _lastDeathPosition;

  public bool IsCleared => _state == State.Ganado;

  private void MigrateLegacy()
  {
    if (_reward == null)
    {
      return;
    }

    _rewards ??= new List<Reward>();
    _rewards.Add(new Reward
    {
      prefab = _reward,
      dropAt = _dropAt,
      spawnPoints = _dropPoint != null ? new[] { _dropPoint } : Array.Empty<Transform>(),
      overrideKeyId = _overrideKeyId
    });

    _reward = null;
    _dropPoint = null;
    _overrideKeyId = "";
  }

#if UNITY_EDITOR
  private void OnValidate()
  {
    if (_reward != null)
    {
      MigrateLegacy();
      UnityEditor.EditorUtility.SetDirty(this);
    }
  }
#endif

  private void Awake()
  {
    MigrateLegacy();

    if (_enemies.Count == 0)
    {
      foreach (var h in GetComponentsInChildren<Health>(true))
      {
        if (h.GetComponentInParent<Player>() == null)
        {
          _enemies.Add(h);
        }
      }
    }

    _enemies.RemoveAll(h => h == null);
    _lastDeathPosition = transform.position;

    foreach (var h in _enemies)
    {
      h.Died += () => OnEnemyDied(h);

      if (_spawnEnemiesOnStart)
      {
        h.gameObject.SetActive(false);
      }
    }

    SetBarriers(false);
  }

  private void Start()
  {
    if (_startRadius <= 0f)
    {
      Begin();
    }
  }

  private void Update()
  {
    if (_state == State.Esperando)
    {
      if (_player == null)
      {
        var go = GameObject.FindWithTag("Player");
        _player = go != null ? go.transform : null;
      }

      if (_player != null && (_player.position - transform.position).sqrMagnitude <= _startRadius * _startRadius)
      {
        Begin();
      }

      return;
    }

    if (_state == State.EnCombate)
    {
      // Respaldo por si algun enemigo se destruye sin pasar por Health.Died.
      CheckCleared();
    }
  }

  private void Begin()
  {
    if (_state != State.Esperando)
    {
      return;
    }

    _state = State.EnCombate;

    if (_spawnEnemiesOnStart)
    {
      foreach (var h in _enemies)
      {
        if (h != null) h.gameObject.SetActive(true);
      }
    }

    SetBarriers(true);
    OnStarted.Invoke();
    CheckCleared();
  }

  private void OnEnemyDied(Health enemy)
  {
    if (enemy != null)
    {
      _lastDeathPosition = enemy.transform.position;
    }

    // Died se dispara antes de que la vida llegue a destruir el objeto.
    if (_state == State.EnCombate)
    {
      StartCoroutine(CheckNextFrame());
    }
  }

  private IEnumerator CheckNextFrame()
  {
    yield return null;
    CheckCleared();
  }

  private void CheckCleared()
  {
    if (_state != State.EnCombate)
    {
      return;
    }

    foreach (var h in _enemies)
    {
      if (h != null && !h.IsDead)
      {
        return;
      }
    }

    _state = State.Ganado;
    StartCoroutine(Win());
  }

  // ---------- Recompensas ----------

  private IEnumerator Win()
  {
    if (_dropDelay > 0f)
    {
      yield return new WaitForSeconds(_dropDelay);
    }

    SetBarriers(false);

    if (_clearedSound != null)
    {
      AudioSource.PlayClipAtPoint(_clearedSound, transform.position);
    }

    // Primero se decide todo lo que sale y donde; luego se suelta en cascada.
    var drops = new List<(Reward reward, Vector3 position)>();

    foreach (var reward in _rewards)
    {
      if (reward == null || reward.prefab == null || Random.value > reward.chance)
      {
        continue;
      }

      foreach (var position in Positions(reward))
      {
        drops.Add((reward, position));
      }
    }

    Scatter(drops);

    for (var i = 0; i < drops.Count; i++)
    {
      var (reward, position) = drops[i];
      var item = Instantiate(reward.prefab, position + Vector3.up * 0.25f, Quaternion.identity);

      if (!string.IsNullOrWhiteSpace(reward.overrideKeyId) && item.TryGetComponent<KeyPickup>(out var key))
      {
        key.SetKeyId(reward.overrideKeyId);
      }

      if (_dropInterval > 0f && i < drops.Count - 1)
      {
        yield return new WaitForSeconds(_dropInterval);
      }
    }

    OnCleared.Invoke();
  }

  /// <summary>Una posicion por copia segun donde debe salir la recompensa.</summary>
  private IEnumerable<Vector3> Positions(Reward reward)
  {
    var points = ValidPoints(reward.spawnPoints);

    if (reward.dropAt != DropPlace.PuntosDeAparicion || points.Count == 0)
    {
      var center = reward.dropAt == DropPlace.CentroDeLaZona ? transform.position : _lastDeathPosition;

      if (reward.dropAt == DropPlace.PuntosDeAparicion)
      {
        center = transform.position; // sin puntos asignados: el centro
      }

      for (var i = 0; i < reward.amount; i++)
      {
        yield return center;
      }

      yield break;
    }

    switch (reward.pointMode)
    {
      case PointMode.EnTodosLosPuntos:
        foreach (var p in points)
        {
          for (var i = 0; i < reward.amount; i++)
          {
            yield return p.position;
          }
        }
        break;

      case PointMode.AlAzar:
        for (var i = 0; i < reward.amount; i++)
        {
          yield return points[Random.Range(0, points.Count)].position;
        }
        break;

      default:
        for (var i = 0; i < reward.amount; i++)
        {
          yield return points[i % points.Count].position;
        }
        break;
    }
  }

  private static List<Transform> ValidPoints(Transform[] points)
  {
    var list = new List<Transform>();

    if (points != null)
    {
      foreach (var p in points)
      {
        if (p != null) list.Add(p);
      }
    }

    return list;
  }

  /// <summary>Las que caen en el mismo sitio se reparten en circulo.</summary>
  private void Scatter(List<(Reward reward, Vector3 position)> drops)
  {
    if (_scatterRadius <= 0f)
    {
      return;
    }

    var groups = new Dictionary<Vector3, List<int>>();

    for (var i = 0; i < drops.Count; i++)
    {
      var key = drops[i].position;

      if (!groups.TryGetValue(key, out var list))
      {
        groups[key] = list = new List<int>();
      }

      list.Add(i);
    }

    foreach (var pair in groups)
    {
      var list = pair.Value;

      if (list.Count < 2)
      {
        continue;
      }

      var start = Random.value * 360f;

      for (var k = 0; k < list.Count; k++)
      {
        var angle = (start + 360f * k / list.Count) * Mathf.Deg2Rad;
        var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _scatterRadius;
        var index = list[k];
        drops[index] = (drops[index].reward, pair.Key + offset);
      }
    }
  }

  private void SetBarriers(bool active)
  {
    if (_barriers == null)
    {
      return;
    }

    foreach (var b in _barriers)
    {
      if (b != null) b.SetActive(active);
    }
  }

  private void OnDrawGizmos()
  {
    Gizmos.color = new Color(1f, 0.35f, 0.3f, 0.8f);

    if (_startRadius > 0f)
    {
      Gizmos.DrawWireSphere(transform.position, _startRadius);
    }

    foreach (var h in _enemies)
    {
      if (h != null) Gizmos.DrawLine(transform.position, h.transform.position);
    }

    if (_rewards == null)
    {
      return;
    }

    Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);

    foreach (var reward in _rewards)
    {
      if (reward == null || reward.dropAt != DropPlace.PuntosDeAparicion || reward.spawnPoints == null)
      {
        continue;
      }

      foreach (var p in reward.spawnPoints)
      {
        if (p == null) continue;
        Gizmos.DrawWireCube(p.position, Vector3.one * 0.4f);
        Gizmos.DrawLine(transform.position, p.position);
      }
    }
  }
}
