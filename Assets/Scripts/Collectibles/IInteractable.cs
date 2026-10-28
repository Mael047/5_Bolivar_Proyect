// IInteractable.cs
// Cualquier cosa con la que el jugador interactua usando el mismo aviso y el
// mismo boton que los coleccionables ("F / X  Examinar"): puertas con llave,
// palancas, NPCs...
// Se registran en InteractableRegistry (OnEnable / OnDisable) y el
// CollectibleInteractor las tiene en cuenta junto a los coleccionables.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Collectibles
{
  public interface IInteractable
  {
    /// <summary>Se puede usar ahora mismo.</summary>
    bool CanInteract { get; }

    /// <summary>True si el jugador esta lo bastante cerca.</summary>
    bool IsInRange(Vector3 playerPosition, out float sqrDistance);

    /// <summary>Punto del mundo donde se coloca el aviso.</summary>
    Vector3 PromptPoint { get; }

    /// <summary>Texto de la accion ("Abrir", "Examinar"...).</summary>
    string PromptText { get; }

    /// <summary>Aviso propio (vacio = el general).</summary>
    CollectiblePromptView CustomPromptView { get; }

    /// <summary>El jugador pulso Interactuar.</summary>
    void Interact(PlayerInput playerInput);
  }

  /// <summary>Interactuables activos que no son coleccionables.</summary>
  public static class InteractableRegistry
  {
    private static readonly List<IInteractable> s_active = new List<IInteractable>();

    public static IReadOnlyList<IInteractable> Active => s_active;

    public static void Register(IInteractable item)
    {
      if (item != null && !s_active.Contains(item))
      {
        s_active.Add(item);
      }

      CollectibleInteractor.EnsureExists();
    }

    public static void Unregister(IInteractable item)
    {
      s_active.Remove(item);
    }
  }
}
