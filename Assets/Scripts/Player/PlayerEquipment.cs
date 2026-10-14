// PlayerEquipment.cs
// Equipamiento del jugador: espada y escudo. Al empezar el juego no tiene
// ninguno (salvo que se marque "Start With..." para pruebas); se consiguen
// recogiendo un Collectible con "Equipment" = Espada / Escudo, que muestra su
// inspeccion como cualquier coleccionable y despues se equipa.
//
//   - Sin espada: no puede atacar y la espada no se ve.
//   - Sin escudo: no puede cubrirse (el lock-on con L1 sigue funcionando) y
//     el escudo no se ve.
//
// Si el Player no tiene este componente, todo funciona como antes (tiene ambos).

using System;
using Collectibles;
using UnityEngine;

public enum EquipmentSlot
{
  Ninguno,
  Espada,
  Escudo
}

[DisallowMultipleComponent]
public class PlayerEquipment : MonoBehaviour
{
  [Header("Para pruebas")]
  [SerializeField, Tooltip("Empezar con la espada (para probar el combate sin recogerla).")]
  private bool _startWithSword;

  [SerializeField, Tooltip("Empezar con el escudo.")]
  private bool _startWithShield;

  [Header("Partes visibles")]
  [SerializeField, Tooltip("Objetos que se ven solo con la espada. Vacio = se busca 'Espada' dentro del jugador.")]
  private GameObject[] _swordVisuals;

  [SerializeField, Tooltip("Objetos que se ven solo con el escudo. Vacio = se busca 'escudo' dentro del jugador.")]
  private GameObject[] _shieldVisuals;

  private bool _hasSword;
  private bool _hasShield;
  private Collectible _swordItem;
  private Collectible _shieldItem;

  public static PlayerEquipment Instance { get; private set; }

  /// <summary>Se dispara al equipar algo.</summary>
  public event Action Changed;

  public bool HasSword => _hasSword;
  public bool HasShield => _hasShield;

  private void Awake()
  {
    Instance = this;
    _hasSword |= _startWithSword;
    _hasShield |= _startWithShield;

    if (_swordVisuals == null || _swordVisuals.Length == 0)
    {
      _swordVisuals = FindVisual("Espada");
    }

    if (_shieldVisuals == null || _shieldVisuals.Length == 0)
    {
      _shieldVisuals = FindVisual("escudo");
    }

    Apply();
  }

  private void OnDestroy()
  {
    if (Instance == this)
    {
      Instance = null;
    }
  }

  public bool Has(EquipmentSlot slot)
  {
    switch (slot)
    {
      case EquipmentSlot.Espada: return _hasSword;
      case EquipmentSlot.Escudo: return _hasShield;
      default: return false;
    }
  }

  /// <summary>Coleccionable con el que se consiguio (para volver a examinarlo). Null si no se tiene.</summary>
  public Collectible ItemIn(EquipmentSlot slot)
  {
    switch (slot)
    {
      case EquipmentSlot.Espada: return _swordItem;
      case EquipmentSlot.Escudo: return _shieldItem;
      default: return null;
    }
  }

  public void Equip(EquipmentSlot slot, Collectible item)
  {
    switch (slot)
    {
      case EquipmentSlot.Espada:
        _hasSword = true;
        _swordItem = item;
        break;
      case EquipmentSlot.Escudo:
        _hasShield = true;
        _shieldItem = item;
        break;
      default:
        return;
    }

    Apply();
    Changed?.Invoke();
  }

  private void Apply()
  {
    SetActive(_swordVisuals, _hasSword);
    SetActive(_shieldVisuals, _hasShield);
  }

  private static void SetActive(GameObject[] objects, bool active)
  {
    if (objects == null)
    {
      return;
    }

    foreach (var go in objects)
    {
      if (go != null && go.activeSelf != active)
      {
        go.SetActive(active);
      }
    }
  }

  private GameObject[] FindVisual(string objectName)
  {
    foreach (var t in GetComponentsInChildren<Transform>(true))
    {
      if (string.Equals(t.name, objectName, StringComparison.OrdinalIgnoreCase))
      {
        return new[] { t.gameObject };
      }
    }

    return new GameObject[0];
  }

  [ContextMenu("Probar: dar espada")]
  private void TestSword() => Equip(EquipmentSlot.Espada, null);

  [ContextMenu("Probar: dar escudo")]
  private void TestShield() => Equip(EquipmentSlot.Escudo, null);
}
