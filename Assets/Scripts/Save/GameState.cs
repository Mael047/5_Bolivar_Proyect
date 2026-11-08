// GameState.cs
// Estado de la partida que sobrevive a los cambios de escena y se guarda en
// disco al descansar en un banco.
//
//   - Cosas del mundo ya hechas ("flags"): puertas abiertas, llaves y
//     coleccionables recogidos, cajas unicas rotas, puzzles resueltos,
//     combates ganados. Las anota WorldState en cuanto pasan.
//   - Cosas de la nutria: vida, espada/escudo, llaves sin usar,
//     coleccionables.
//   - Ultimo banco (escena + id) y los "restos" de la ultima muerte.
//
// Al cambiar de escena (puerta, muerte, Cargar partida) queda "pendiente" y
// WorldState lo aplica en la escena nueva.
// Archivo: Application.persistentDataPath/partida.json

using System;
using System.Collections.Generic;
using System.IO;
using Collectibles;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable]
public class KeyCount
{
  public string id;
  public int count;
}

/// <summary>Lo que lleva la nutria (o lo que quedo en sus restos).</summary>
[Serializable]
public class Belongings
{
  public bool sword;
  public bool shield;
  public List<KeyCount> keys = new List<KeyCount>();
  public List<string> collectibles = new List<string>();

  public bool IsEmpty => !sword && !shield && keys.Count == 0 && collectibles.Count == 0;

  public void Merge(Belongings other)
  {
    if (other == null) return;
    sword |= other.sword;
    shield |= other.shield;

    foreach (var k in other.keys)
    {
      var mine = keys.Find(x => x.id == k.id);
      if (mine != null) mine.count += k.count;
      else keys.Add(new KeyCount { id = k.id, count = k.count });
    }

    foreach (var c in other.collectibles)
    {
      if (!collectibles.Contains(c)) collectibles.Add(c);
    }
  }
}

[Serializable]
public class SaveData
{
  public string scene;
  public string benchId;
  public Belongings player = new Belongings();
  public List<string> flags = new List<string>();

  public bool hasDrop;
  public string dropScene;
  public Vector3 dropPosition;
  public Belongings drop = new Belongings();
}

public static class GameState
{
  public enum Spawn { DondeEste, Banco }

  private const string FileName = "partida.json";

  /// <summary>Estado de la partida en curso.</summary>
  public static SaveData Session { get; private set; } = new SaveData();

  /// <summary>Hay estado por aplicar en la proxima escena que se cargue.</summary>
  public static bool Pending { get; private set; }

  /// <summary>Donde aparece la nutria al aplicar el estado pendiente.</summary>
  public static Spawn PendingSpawn { get; private set; }

  /// <summary>La vida se llena al aplicar (muerte, cargar partida).</summary>
  public static bool PendingFullHealth { get; private set; }

  private static int s_pendingHealth = -1;
  private static readonly HashSet<string> s_flags = new HashSet<string>();

  public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);
  public static bool HasSave => File.Exists(SavePath);

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void Init()
  {
    Session = new SaveData();
    s_flags.Clear();
    Pending = false;
    s_pendingHealth = -1;
    Collectible.Collected -= OnCollected;
    Collectible.Collected += OnCollected;
  }

  // ---------- Flags del mundo ----------

  public static bool HasFlag(string id) => !string.IsNullOrEmpty(id) && s_flags.Contains(id);

  public static void SetFlag(string id)
  {
    if (!string.IsNullOrEmpty(id) && s_flags.Add(id))
    {
      Session.flags.Add(id);
    }
  }

  public static string CollectedFlag(Collectible c) => "recogido:" + c.Id;

  private static void OnCollected(Collectible c)
  {
    if (c == null || c.IsSign)
    {
      return;
    }

    SetFlag(CollectedFlag(c));

    if (c.Equipment != EquipmentSlot.Ninguno)
    {
      return; // el equipamiento se guarda como espada/escudo
    }

    if (!Session.player.collectibles.Contains(c.Id))
    {
      Session.player.collectibles.Add(c.Id);
    }
  }

  // ---------- Nueva partida ----------

  /// <summary>Empieza de cero (Nueva partida / volver al menu). No borra el archivo guardado.</summary>
  public static void ResetSession()
  {
    Session = new SaveData();
    s_flags.Clear();
    Pending = false;
    s_pendingHealth = -1;
  }

  // ---------- Capturar a la nutria ----------

  /// <summary>Copia lo que lleva la nutria de la escena actual a la sesion.</summary>
  public static void CapturePlayer()
  {
    var p = Session.player;
    var equipment = PlayerEquipment.Instance != null ? PlayerEquipment.Instance : UnityEngine.Object.FindAnyObjectByType<PlayerEquipment>();

    if (equipment != null)
    {
      p.sword = equipment.HasSword;
      p.shield = equipment.HasShield;
    }

    p.keys.Clear();

    foreach (var pair in KeyRing.All)
    {
      if (pair.Value > 0) p.keys.Add(new KeyCount { id = pair.Key, count = pair.Value });
    }

    var player = UnityEngine.Object.FindAnyObjectByType<Player>();
    Health health = null;

    if (player != null)
    {
      health = player.GetComponentInParent<Health>();
      if (health == null) health = player.GetComponentInChildren<Health>();
    }

    s_pendingHealth = health != null ? health.CurrentHealth : -1;
  }

  // ---------- Transiciones ----------

  /// <summary>Antes de cruzar una puerta a otra escena.</summary>
  public static void PrepareSceneChange()
  {
    CapturePlayer();
    Pending = true;
    PendingSpawn = Spawn.DondeEste;
    PendingFullHealth = false;
  }

  /// <summary>
  /// La nutria murio: todo lo que lleva queda en el punto de muerte (se junta
  /// con unos restos anteriores si no los recogio) y vuelve al ultimo banco.
  /// Devuelve la escena que hay que cargar.
  /// </summary>
  public static string PrepareDeath(Vector3 deathPosition)
  {
    CapturePlayer();

    var drop = new Belongings();
    drop.Merge(Session.player);

    if (Session.hasDrop)
    {
      drop.Merge(Session.drop);
    }

    Session.player = new Belongings();

    if (!drop.IsEmpty)
    {
      Session.hasDrop = true;
      Session.drop = drop;
      Session.dropScene = SceneManager.GetActiveScene().name;
      Session.dropPosition = deathPosition;
    }

    Pending = true;
    PendingFullHealth = true;
    PendingSpawn = string.IsNullOrEmpty(Session.benchId) ? Spawn.DondeEste : Spawn.Banco;

    return PendingSpawn == Spawn.Banco && !string.IsNullOrEmpty(Session.scene)
      ? Session.scene
      : SceneManager.GetActiveScene().name;
  }

  /// <summary>La nutria recogio sus restos.</summary>
  public static Belongings TakeDrop()
  {
    var drop = Session.drop;
    Session.hasDrop = false;
    Session.drop = new Belongings();
    Session.player.Merge(drop);
    return drop;
  }

  // ---------- Disco ----------

  /// <summary>Guarda en el banco: escena, banco, lo que lleva y el mundo.</summary>
  public static void SaveAtBench(string benchId)
  {
    CapturePlayer();
    Session.scene = SceneManager.GetActiveScene().name;
    Session.benchId = benchId;

    try
    {
      File.WriteAllText(SavePath, JsonUtility.ToJson(Session, true));
    }
    catch (Exception e)
    {
      Debug.LogWarning("[GameState] No se pudo guardar la partida: " + e.Message);
    }
  }

  /// <summary>Lee la partida guardada y la deja pendiente. Devuelve la escena a cargar (null si no hay).</summary>
  public static string LoadFromDisk()
  {
    if (!HasSave)
    {
      return null;
    }

    try
    {
      var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));

      if (data == null || string.IsNullOrEmpty(data.scene))
      {
        return null;
      }

      Session = data;
      s_flags.Clear();
      foreach (var f in data.flags) s_flags.Add(f);
      Pending = true;
      PendingSpawn = string.IsNullOrEmpty(data.benchId) ? Spawn.DondeEste : Spawn.Banco;
      PendingFullHealth = true;
      return data.scene;
    }
    catch (Exception e)
    {
      Debug.LogWarning("[GameState] No se pudo leer la partida: " + e.Message);
      return null;
    }
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  private static void HookScenes()
  {
    // Despues de KeyRing / CollectibleJournal (que se vacian al cargar escena).
    SceneManager.sceneLoaded -= OnSceneLoaded;
    SceneManager.sceneLoaded += OnSceneLoaded;
  }

  private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
  {
    if (mode == LoadSceneMode.Single)
    {
      EnsureWorldState(scene);
    }
  }

  // La primera escena al pulsar Play (o al abrir la build) no siempre pasa por
  // sceneLoaded: aqui se asegura su WorldState.
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  private static void EnsureFirstScene()
  {
    EnsureWorldState(SceneManager.GetActiveScene());
  }

  /// <summary>Un WorldState por escena: anota lo que pasa y aplica lo pendiente.</summary>
  private static void EnsureWorldState(Scene scene)
  {
    if (!scene.IsValid() || (WorldState.Current != null && WorldState.Current.gameObject.scene == scene))
    {
      return;
    }

    var go = new GameObject("WorldState");
    SceneManager.MoveGameObjectToScene(go, scene);
    go.AddComponent<WorldState>();
  }

  /// <summary>Lo usa WorldState al aplicar. Devuelve la vida a poner (-1 = no tocar).</summary>
  internal static int ConsumePending(out Spawn spawn, out bool fullHealth)
  {
    spawn = PendingSpawn;
    fullHealth = PendingFullHealth;
    var health = s_pendingHealth;
    Pending = false;
    s_pendingHealth = -1;
    return health;
  }
}
