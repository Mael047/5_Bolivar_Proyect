// DeathDrop.cs
// Restos de la nutria donde murio: una luz flotante. Al tocarla recupera
// todo lo que llevaba (espada, escudo, llaves, coleccionables).
// Lo crea WorldState solo; no hace falta ponerlo en la escena.
//
// Aspecto: si existe un prefab llamado "Restos_Nutria" dentro de una carpeta
// Resources (Assets/Resources/Restos_Nutria.prefab) se usa ese; si no, una
// esfera brillante con luz.

using Collectibles;
using UnityEngine;

public class DeathDrop : MonoBehaviour
{
  private const float PickupRadius = 1.1f;
  private const string VisualResource = "Restos_Nutria";

  private Transform _visual;
  private Transform _player;
  private Vector3 _base;
  private bool _taken;

  private void Start()
  {
    // Apoyar en el suelo.
    if (Physics.Raycast(transform.position + Vector3.up * 1.5f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore))
    {
      transform.position = hit.point;
    }

    _base = transform.position + Vector3.up * 0.6f;

    var prefab = Resources.Load<GameObject>(VisualResource);
    _visual = prefab != null ? Instantiate(prefab, transform).transform : BuildDefaultVisual();
    _visual.position = _base;
  }

  private Transform BuildDefaultVisual()
  {
    var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
    Destroy(sphere.GetComponent<Collider>());
    sphere.transform.SetParent(transform, false);
    sphere.transform.localScale = Vector3.one * 0.35f;

    var shader = Shader.Find("Universal Render Pipeline/Lit");

    if (shader != null)
    {
      var color = new Color(0.55f, 0.85f, 1f);
      var mat = new Material(shader);
      mat.SetColor("_BaseColor", color);
      mat.EnableKeyword("_EMISSION");
      mat.SetColor("_EmissionColor", color * 3f);
      sphere.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    var light = sphere.AddComponent<Light>();
    light.type = LightType.Point;
    light.color = new Color(0.55f, 0.85f, 1f);
    light.range = 4f;
    light.intensity = 2f;
    return sphere.transform;
  }

  private void Update()
  {
    if (_taken || _visual == null)
    {
      return;
    }

    _visual.position = _base + Vector3.up * (Mathf.Sin(Time.time * 2f) * 0.12f);
    _visual.Rotate(0f, 60f * Time.deltaTime, 0f, Space.World);

    if (_player == null)
    {
      var go = GameObject.FindWithTag("Player");
      _player = go != null ? go.transform : null;
      if (_player == null) return;
    }

    var delta = _player.position - transform.position;
    delta.y = 0f;

    if (delta.sqrMagnitude <= PickupRadius * PickupRadius && Mathf.Abs(_player.position.y - transform.position.y) < 2.5f)
    {
      Take();
    }
  }

  private void Take()
  {
    _taken = true;
    var drop = GameState.TakeDrop();

    // Espada y escudo (con su coleccionable si esta en la escena, para verlo en Estado).
    var equipment = _player.GetComponentInParent<PlayerEquipment>();

    if (equipment != null)
    {
      if (drop.sword) equipment.Equip(EquipmentSlot.Espada, FindEquipmentItem(EquipmentSlot.Espada));
      if (drop.shield) equipment.Equip(EquipmentSlot.Escudo, FindEquipmentItem(EquipmentSlot.Escudo));
    }

    foreach (var k in drop.keys)
    {
      KeyRing.Add(k.id, k.count);
    }

    foreach (var c in FindObjectsByType<Collectible>(FindObjectsInactive.Include, FindObjectsSortMode.None))
    {
      if (drop.collectibles.Contains(c.Id)) CollectibleJournal.Add(c);
    }

    TopDownCamera.AddShake(0.1f);
    Destroy(gameObject);
  }

  private static Collectible FindEquipmentItem(EquipmentSlot slot)
  {
    foreach (var c in FindObjectsByType<Collectible>(FindObjectsInactive.Include, FindObjectsSortMode.None))
    {
      if (c.Equipment == slot) return c;
    }

    return null;
  }
}
