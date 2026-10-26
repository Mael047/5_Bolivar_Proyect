// MapArea.cs
// Caja que define que parte del mundo cubre el mapa grande. Se ve como un
// recuadro amarillo en la escena.
//
// Muevela / cambia su tamaño para encuadrar el nivel y luego hornea
// (boton en el Inspector o Tools > Mapa > Hornear mapa).
// Si ya tienes una capa pintada a mano, NO la muevas: tu dibujo dejaria de
// coincidir con el mapa.

using UnityEngine;

[DisallowMultipleComponent]
public class MapArea : MonoBehaviour
{
  [Tooltip("Tamaño de la zona (X = ancho, Y = alto para medir el relieve, Z = largo).")]
  public Vector3 size = new Vector3(200f, 60f, 200f);

  [Tooltip("Datos donde se guarda el mapa horneado.")]
  public WorldMapData data;

  public Bounds Bounds => new Bounds(transform.position, size);

  private void OnDrawGizmos()
  {
    Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
    Gizmos.DrawWireCube(transform.position, size);

    // Flecha hacia el norte (+Z = arriba del mapa)
    var top = transform.position + Vector3.up * (size.y * 0.5f);
    var north = top + Vector3.forward * (size.z * 0.5f);
    Gizmos.DrawLine(top, north);
    Gizmos.DrawLine(north, north + new Vector3(-0.04f, 0f, -0.06f) * size.z);
    Gizmos.DrawLine(north, north + new Vector3(0.04f, 0f, -0.06f) * size.z);
  }

  private void OnDrawGizmosSelected()
  {
    Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.08f);
    Gizmos.DrawCube(transform.position, size);
  }
}
