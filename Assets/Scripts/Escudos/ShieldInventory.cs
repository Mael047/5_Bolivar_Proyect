// ShieldInventory.cs
// Escudos del puzzle que lleva la nutria (como el llavero, pero estos pasan
// de una escena a otra y se guardan en el banco: se anotan en GameState).
//
//   escudo:<id>                     -> lo lleva en el inventario
//   escudo_poste:<poste>=<id>       -> esta colocado en ese poste
//   escudo_recogido:<escena>:<id>   -> el del suelo ya se recogio
//
// Nueva partida (GameState.ResetSession) lo deja todo vacio.

using System;
using System.Collections.Generic;
using UnityEngine;

public static class ShieldInventory
{
  private const string HeldPrefix = "escudo:";
  private const string PlacedPrefix = "escudo_poste:";
  private const string PickedPrefix = "escudo_recogido:";

  /// <summary>Cambio en el inventario o en los postes.</summary>
  public static event Action Changed;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void Init()
  {
    Changed = null;
  }

  // ---------- Inventario ----------

  public static bool Has(string id) => !string.IsNullOrEmpty(id) && GameState.HasFlag(HeldPrefix + id);

  /// <summary>Ids de los escudos que lleva (en el orden en que los consiguio).</summary>
  public static List<string> Held()
  {
    var list = new List<string>();

    foreach (var flag in GameState.Session.flags)
    {
      if (flag.StartsWith(HeldPrefix, StringComparison.Ordinal))
      {
        list.Add(flag.Substring(HeldPrefix.Length));
      }
    }

    return list;
  }

  public static void Add(string id)
  {
    if (string.IsNullOrEmpty(id) || Has(id)) return;
    GameState.SetFlag(HeldPrefix + id);
    Changed?.Invoke();
  }

  /// <summary>Saca un escudo del inventario. False si no lo tenia.</summary>
  public static bool Remove(string id)
  {
    if (!Has(id)) return false;
    GameState.ClearFlag(HeldPrefix + id);
    Changed?.Invoke();
    return true;
  }

  // ---------- Postes ----------

  /// <summary>Escudo colocado en un poste (null si esta vacio).</summary>
  public static string GetPlaced(string poleKey)
  {
    var prefix = PlacedPrefix + poleKey + "=";

    foreach (var flag in GameState.Session.flags)
    {
      if (flag.StartsWith(prefix, StringComparison.Ordinal))
      {
        return flag.Substring(prefix.Length);
      }
    }

    return null;
  }

  public static void SetPlaced(string poleKey, string id)
  {
    var current = GetPlaced(poleKey);

    if (current != null)
    {
      GameState.ClearFlag(PlacedPrefix + poleKey + "=" + current);
    }

    if (!string.IsNullOrEmpty(id))
    {
      GameState.SetFlag(PlacedPrefix + poleKey + "=" + id);
    }

    Changed?.Invoke();
  }

  /// <summary>El escudo esta puesto en algun poste.</summary>
  public static bool IsPlaced(string id)
  {
    if (string.IsNullOrEmpty(id)) return false;
    var suffix = "=" + id;

    foreach (var flag in GameState.Session.flags)
    {
      if (flag.StartsWith(PlacedPrefix, StringComparison.Ordinal) && flag.EndsWith(suffix, StringComparison.Ordinal))
      {
        return true;
      }
    }

    return false;
  }

  // ---------- Escudos del suelo ----------

  public static bool WasPicked(string pickupKey) => GameState.HasFlag(PickedPrefix + pickupKey);

  public static void MarkPicked(string pickupKey) => GameState.SetFlag(PickedPrefix + pickupKey);
}
