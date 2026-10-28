// KeyRing.cs
// Llavero del jugador: cuantas llaves lleva de cada tipo.
//
// Tipo de llave = un texto (Key Id). Vacio = "llave pequeña" generica, que
// abre cualquier puerta generica (como en Zelda). Con un Id (ej. "Jardin")
// solo abre las puertas que pidan ese mismo Id.
//
// Se vacia al cargar la escena de nuevo (nueva partida / salir al menu).

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class KeyRing
{
  private static readonly Dictionary<string, int> s_keys = new Dictionary<string, int>();

  /// <summary>Se dispara al ganar o gastar una llave: (tipo, cantidad actual).</summary>
  public static event Action<string, int> Changed;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void Init()
  {
    s_keys.Clear();
    Changed = null;
    SceneManager.sceneLoaded -= OnSceneLoaded;
    SceneManager.sceneLoaded += OnSceneLoaded;
  }

  private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
  {
    if (mode == LoadSceneMode.Single)
    {
      s_keys.Clear();
    }
  }

  private static string Normalize(string id) => string.IsNullOrWhiteSpace(id) ? "" : id.Trim();

  public static int Count(string id = "")
  {
    return s_keys.TryGetValue(Normalize(id), out var n) ? n : 0;
  }

  public static bool Has(string id = "") => Count(id) > 0;

  public static void Add(string id = "", int amount = 1)
  {
    id = Normalize(id);
    var n = Count(id) + Mathf.Max(1, amount);
    s_keys[id] = n;
    Changed?.Invoke(id, n);
  }

  /// <summary>Gasta una llave. Devuelve false si no habia.</summary>
  public static bool Use(string id = "")
  {
    id = Normalize(id);
    var n = Count(id);

    if (n <= 0)
    {
      return false;
    }

    s_keys[id] = n - 1;
    Changed?.Invoke(id, n - 1);
    return true;
  }
}
