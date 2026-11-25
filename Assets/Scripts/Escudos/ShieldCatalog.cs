// ShieldCatalog.cs
// Lista de los escudos del puzzle: id (el mismo "Piece Id" de cada escudo),
// nombre para mostrar e icono para la interfaz. Lo usan el HUD, el menu de
// los postes y los mensajes. Asset: Assets/UI/Escudos/CatalogoEscudos.
// El orden de la lista es el orden de las casillas del HUD.

using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Re:Quinta/Catalogo de escudos", fileName = "CatalogoEscudos")]
public class ShieldCatalog : ScriptableObject
{
  [Serializable]
  public class Entry
  {
    [Tooltip("Igual que el Piece Id del escudo y el Accepted Piece Id del poste.")]
    public string id;
    [Tooltip("Nombre que se muestra (Perú, Bolivia...).")]
    public string displayName;
    public Sprite icon;
  }

  [SerializeField] private List<Entry> _entries = new List<Entry>();

  [SerializeField, Tooltip("Casilla vacia del HUD (silueta).")]
  private Sprite _emptyIcon;

  public IReadOnlyList<Entry> Entries => _entries;
  public Sprite EmptyIcon => _emptyIcon;

  public Entry Find(string id)
  {
    if (string.IsNullOrEmpty(id)) return null;
    return _entries.Find(e => e != null && string.Equals(e.id, id, StringComparison.OrdinalIgnoreCase));
  }

  public string NameOf(string id)
  {
    var e = Find(id);
    return e != null && !string.IsNullOrEmpty(e.displayName) ? e.displayName : id;
  }

  public Sprite IconOf(string id)
  {
    var e = Find(id);
    return e != null ? e.icon : null;
  }

  /// <summary>Posicion en la lista (para ordenar); los desconocidos al final.</summary>
  public int OrderOf(string id)
  {
    var i = _entries.FindIndex(e => e != null && string.Equals(e.id, id, StringComparison.OrdinalIgnoreCase));
    return i < 0 ? int.MaxValue : i;
  }
}
