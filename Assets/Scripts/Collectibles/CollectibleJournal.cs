// CollectibleJournal.cs
// Registro de los coleccionables recogidos en la partida, en el orden en que
// se recogieron. Lo llena Collectible al examinarse por primera vez (o al
// cargar uno ya recogido con "Remember Between Sessions") y lo lee la pantalla
// de Estado del menu de pausa para mostrarlos y volver a abrirlos.
// Se vacia al cargar una escena (Nueva partida / Salir al menu).

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Collectibles
{
  public static class CollectibleJournal
  {
    private static readonly List<Collectible> s_entries = new List<Collectible>();

    /// <summary>Coleccionables recogidos, en orden.</summary>
    public static IReadOnlyList<Collectible> Entries
    {
      get
      {
        s_entries.RemoveAll(c => c == null);
        return s_entries;
      }
    }

    /// <summary>Se dispara al añadir un coleccionable nuevo.</summary>
    public static event Action Changed;

    public static void Add(Collectible collectible)
    {
      if (collectible == null)
      {
        return;
      }

      foreach (var c in s_entries)
      {
        if (c != null && c.Id == collectible.Id)
        {
          return;
        }
      }

      s_entries.Add(collectible);
      Changed?.Invoke();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
      s_entries.Clear();
      Changed = null;
      SceneManager.sceneLoaded -= OnSceneLoaded;
      SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
      if (mode == LoadSceneMode.Single)
      {
        s_entries.RemoveAll(c => c == null || c.gameObject.scene != scene);
        Changed?.Invoke();
      }
    }
  }
}
