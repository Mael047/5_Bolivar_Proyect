// MapMarker.cs
// Marca un objeto para que aparezca como icono en el minimapa (y mas adelante
// en el mapa grande). Ponlo en cofres, puertas, objetivos, NPCs, etc.
//
// El aspecto (sprite, color, tamaño) sale por defecto de la categoria, que se
// configura una sola vez en el Minimap (lista "Estilos por categoria"). Si
// quieres que este objeto se vea distinto, activa "Personalizar icono".
//
// Los enemigos no necesitan este componente: el Minimap puede marcarlos solo.

using System.Collections.Generic;
using UnityEngine;

public enum MapMarkerCategory
{
  PuntoDeInteres,
  Cofre,
  Puerta,
  Objetivo,
  Enemigo,
  Otro
}

[DisallowMultipleComponent]
public class MapMarker : MonoBehaviour
{
  private static readonly List<MapMarker> s_active = new List<MapMarker>();

  /// <summary>Marcadores activos en la escena.</summary>
  public static IReadOnlyList<MapMarker> Active => s_active;

  [SerializeField] private MapMarkerCategory _category = MapMarkerCategory.PuntoDeInteres;

  [Header("Donde se muestra")]
  [SerializeField] private bool _showOnMinimap = true;
  [SerializeField, Tooltip("Para el mapa grande del menu de pausa.")]
  private bool _showOnMap = true;

  [SerializeField, Tooltip("Punto exacto del icono (vacio = este objeto).")]
  private Transform _anchor;

  [Header("Icono propio (opcional)")]
  [SerializeField, Tooltip("Si esta apagado se usa el estilo de la categoria configurado en el Minimap.")]
  private bool _overrideStyle;
  [SerializeField] private Sprite _icon;
  [SerializeField] private Color _color = Color.white;
  [SerializeField, Min(4f)] private float _size = 22f;
  [SerializeField, Tooltip("Si esta fuera del minimapa, se queda pegado al borde (util para objetivos).")]
  private bool _clampToEdge;
  [SerializeField, Tooltip("El icono gira con el objeto (por ejemplo una flecha).")]
  private bool _rotateWithTarget;

  public MapMarkerCategory Category => _category;
  public bool ShowOnMinimap => _showOnMinimap;
  public bool ShowOnMap => _showOnMap;
  public bool OverrideStyle => _overrideStyle;
  public Sprite Icon => _icon;
  public Color Color => _color;
  public float Size => _size;
  public bool ClampToEdge => _clampToEdge;
  public bool RotateWithTarget => _rotateWithTarget;
  public Vector3 Position => _anchor != null ? _anchor.position : transform.position;
  public float Yaw => (_anchor != null ? _anchor : transform).eulerAngles.y;

  /// <summary>Para crear marcadores desde codigo (ej. enemigos).</summary>
  public void Setup(MapMarkerCategory category, bool showOnMap)
  {
    _category = category;
    _showOnMap = showOnMap;
  }

  /// <summary>Muestra u oculta el marcador (ej. un cofre ya abierto).</summary>
  public void SetVisible(bool minimap, bool map)
  {
    _showOnMinimap = minimap;
    _showOnMap = map;
  }

  private void OnEnable() => s_active.Add(this);

  private void OnDisable() => s_active.Remove(this);
}
