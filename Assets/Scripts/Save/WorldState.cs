// WorldState.cs
// Se crea solo en cada escena (lo pone GameState). Hace tres cosas:
//   1. Anota en GameState lo que pasa en el mundo: puertas abiertas, llaves y
//      coleccionables recogidos, cajas unicas rotas (las que tienen espada,
//      escudo o llave), puzzles resueltos, combates ganados.
//   2. Al llegar a la escena con estado pendiente (cargar partida, morir,
//      cruzar una puerta) lo aplica: deja todo como estaba, devuelve sus cosas
//      a la nutria, la pone en el banco y crea los restos si murio aqui.
//   3. Recuerda las cajas normales para volver a ponerlas al descansar en un
//      banco (RespawnBreakables).
//
// Los objetos se identifican por su ruta en la jerarquia, asi que no hace
// falta configurar nada; si se renombra o mueve un objeto en el editor, su
// estado guardado se pierde (solo afecta a partidas guardadas antes).

using System.Collections.Generic;
using System.Text;
using Collectibles;
using NuiGrab;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-50)]
public class WorldState : MonoBehaviour
{
  public static WorldState Current { get; private set; }

  private class BoxTemplate
  {
    public Breakable live;
    public GameObject template;
    public Transform parent;
    public Vector3 position;
    public Quaternion rotation;
  }

  private readonly List<BoxTemplate> _boxes = new List<BoxTemplate>();
  private Transform _templateHolder;
  private string _scene;

  private void Awake()
  {
    Current = this;
    _scene = gameObject.scene.name;
  }

  private void OnDestroy()
  {
    if (Current == this) Current = null;
  }

  private void Start()
  {
    var pending = GameState.Pending;
    HookAndApplyWorld();

    if (pending)
    {
      ApplyPlayer();
    }

    SpawnDropIfHere();
  }

  // ---------- Mundo ----------

  private void HookAndApplyWorld()
  {
    var scene = gameObject.scene;

    foreach (var door in FindAll<LockedDoor>(scene))
    {
      var id = IdOf(door, "puerta");
      if (GameState.HasFlag(id)) door.OpenImmediate();
      else door.OnOpened.AddListener(() => GameState.SetFlag(id));
    }

    foreach (var key in FindAll<KeyPickup>(scene))
    {
      var id = IdOf(key, "llave");
      if (GameState.HasFlag(id)) Destroy(key.gameObject);
      else key.OnPickedUp.AddListener(() => GameState.SetFlag(id));
    }

    foreach (var c in FindAll<Collectible>(scene))
    {
      if (c.IsSign) continue;
      // Ya recogido (lo anota GameState al recogerlo), lo lleva la nutria o esta en sus restos.
      var owned = GameState.Session.player.collectibles.Contains(c.Id);
      var inDrop = GameState.Session.hasDrop && GameState.Session.drop.collectibles.Contains(c.Id);

      if (GameState.HasFlag(GameState.CollectedFlag(c)) || owned || inDrop)
      {
        c.HideAsCollected();
      }
    }

    foreach (var puzzle in FindAll<PuzzleManager>(scene))
    {
      var id = IdOf(puzzle, "puzzle");
      if (GameState.HasFlag(id)) puzzle.SolveFromSave();
      else puzzle.Completed += _ => GameState.SetFlag(id);
    }

    foreach (var encounter in FindAll<EnemyEncounter>(scene))
    {
      var id = IdOf(encounter, "combate");
      if (GameState.HasFlag(id)) encounter.MarkClearedFromSave();
      else encounter.OnCleared.AddListener(() => GameState.SetFlag(id));
    }

    _templateHolder = new GameObject("Cajas_Plantillas").transform;
    _templateHolder.SetParent(transform, false);
    _templateHolder.gameObject.SetActive(false);

    foreach (var box in FindAll<Breakable>(scene))
    {
      var id = IdOf(box, "caja");

      if (IsUnique(box))
      {
        // Caja con espada, escudo o llave: no vuelve nunca.
        if (GameState.HasFlag(id)) Destroy(box.gameObject);
        else box.OnBroken += _ => GameState.SetFlag(id);
        continue;
      }

      // Caja normal: se guarda una copia apagada para reponerla en el banco.
      var t = box.transform;
      _boxes.Add(new BoxTemplate
      {
        live = box,
        template = Instantiate(box.gameObject, _templateHolder),
        parent = t.parent,
        position = t.position,
        rotation = t.rotation
      });
    }
  }

  /// <summary>Cajas que no vuelven: las que guardan espada o escudo (y llaves, para no repetirlas).</summary>
  public static bool IsUnique(Breakable box)
  {
    var content = box.ContainedPickup;

    if (content == null)
    {
      return false;
    }

    foreach (var c in content.GetComponentsInChildren<Collectible>(true))
    {
      if (c.Equipment != EquipmentSlot.Ninguno) return true;
    }

    return content.GetComponentInChildren<KeyPickup>(true) != null;
  }

  /// <summary>Vuelve a poner las cajas normales rotas (y las movidas en su sitio).</summary>
  public void RespawnBreakables()
  {
    foreach (var b in _boxes)
    {
      if (b.live != null && !b.live.IsBroken)
      {
        // Sigue entera: si se movio, vuelve a su sitio.
        var body = b.live.GetComponent<Rigidbody>();

        if (body != null && !body.isKinematic)
        {
          body.linearVelocity = Vector3.zero;
          body.angularVelocity = Vector3.zero;
        }

        b.live.transform.SetPositionAndRotation(b.position, b.rotation);
        continue;
      }

      var go = Instantiate(b.template, b.position, b.rotation, b.parent);
      go.name = b.template.name.Replace("(Clone)", "").Trim();
      go.SetActive(true);
      b.live = go.GetComponent<Breakable>();
    }
  }

  // ---------- Nutria ----------

  private void ApplyPlayer()
  {
    var arrival = GameState.PendingArrival;
    var health = GameState.ConsumePending(out var spawn, out var fullHealth);
    var player = FindAnyObjectByType<Player>();

    if (player == null)
    {
      return;
    }

    var data = GameState.Session.player;

    // Equipamiento
    var equipment = player.GetComponent<PlayerEquipment>();
    if (equipment != null) equipment.SetEquipped(data.sword, data.shield);

    // Llaves (KeyRing se vacia al cargar la escena)
    KeyRing.Clear();
    foreach (var k in data.keys) KeyRing.Add(k.id, k.count);

    // Coleccionables de esta escena al registro de Estado
    foreach (var c in FindAll<Collectible>(gameObject.scene))
    {
      if (data.collectibles.Contains(c.Id)) CollectibleJournal.Add(c);
    }

    // Vida
    var hp = player.GetComponentInParent<Health>();
    if (hp == null) hp = player.GetComponentInChildren<Health>();

    if (hp != null)
    {
      if (fullHealth) hp.SetHealth(hp.MaxHealth);
      else if (health > 0) hp.SetHealth(health);
    }

    // Posicion: junto al banco
    if (spawn == GameState.Spawn.Banco)
    {
      foreach (var bench in FindAll<SaveBench>(gameObject.scene))
      {
        if (bench.Id == GameState.Session.benchId)
        {
          PlacePlayer(player, bench.SpawnPose);
          break;
        }
      }
    }

    // Posicion: punto de llegada de la puerta (p. ej. la entrada de la casa)
    if (spawn == GameState.Spawn.DondeEste && !string.IsNullOrEmpty(arrival))
    {
      var point = FindAll<Transform>(gameObject.scene).Find(t => t.name == arrival);

      if (point != null)
      {
        PlacePlayer(player, new Pose(point.position, Quaternion.Euler(0f, point.eulerAngles.y, 0f)));
      }
      else
      {
        Debug.LogWarning($"[WorldState] No encontre el punto de llegada '{arrival}' en esta escena.");
      }
    }

    // Lo que traia agarrado aparece delante de ella y se vuelve a agarrar.
    var carried = GameState.TakeCarried();

    if (carried != null)
    {
      SceneManager.MoveGameObjectToScene(carried, gameObject.scene);
      var front = player.transform.position + player.transform.forward * 1f + Vector3.up * 1f;
      carried.transform.position = front;

      var body = carried.GetComponent<Rigidbody>();
      var grab = FindAnyObjectByType<HandGrabManager>();

      if (body != null)
      {
        body.position = front;

        if (grab != null) grab.ForceGrab(body);
        else body.isKinematic = false;
      }
    }

    // La mano y el control vuelven a funcionar (si se venia del menu).
    HandTracker.GameplayEnabled = true;
    var input = player.GetComponent<PlayerInput>();
    if (input != null) input.ActivateInput();
  }

  public static void PlacePlayer(Player player, Pose pose)
  {
    var body = player.GetComponent<Rigidbody>();

    if (body != null)
    {
      body.linearVelocity = Vector3.zero;
      body.position = pose.position;
      body.rotation = pose.rotation;
    }

    player.transform.SetPositionAndRotation(pose.position, pose.rotation);
    Physics.SyncTransforms();

    var cam = Camera.main != null ? Camera.main.GetComponent<TopDownCamera>() : null;
    if (cam != null) cam.SnapToTarget();
  }

  private void SpawnDropIfHere()
  {
    var s = GameState.Session;

    if (!s.hasDrop || s.dropScene != _scene)
    {
      return;
    }

    var go = new GameObject("Restos_Nutria");
    SceneManager.MoveGameObjectToScene(go, gameObject.scene);
    go.transform.position = s.dropPosition;
    go.AddComponent<DeathDrop>();
  }

  // ---------- Utilidades ----------

  private static List<T> FindAll<T>(Scene scene) where T : Component
  {
    var list = new List<T>();

    foreach (var root in scene.GetRootGameObjects())
    {
      list.AddRange(root.GetComponentsInChildren<T>(true));
    }

    return list;
  }

  /// <summary>Id estable: escena + ruta en la jerarquia (con el indice de cada hijo).</summary>
  public static string IdOf(Component c, string kind)
  {
    var sb = new StringBuilder();

    for (var t = c.transform; t != null; t = t.parent)
    {
      sb.Insert(0, "/" + t.name + "#" + t.GetSiblingIndex());
    }

    return kind + ":" + c.gameObject.scene.name + sb;
  }
}
