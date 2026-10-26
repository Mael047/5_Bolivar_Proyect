// WorldMapData.cs
// Datos del mapa grande: la imagen horneada del nivel, la zona del mundo que
// cubre y todos los ajustes de estilo del horneado.
//
// Se crea solo (Assets/UI/Mapa/WorldMapData) al hornear por primera vez.
// Flujo cuando cambias el nivel:
//   Tools > Mapa > Hornear mapa  -> vuelve a fotografiar el nivel y reescribe
//   las imagenes en el mismo archivo, asi nada pierde la referencia.
//   Tools > Mapa > Reaplicar estilo -> solo recalcula el dibujo (rapido) con
//   los ajustes de abajo, sin volver a fotografiar.
//
// Capa pintada a mano (opcional): pinta encima de Mapa_Base.png en tu
// programa de dibujo y guarda SOLO tus trazos en un PNG transparente del
// mismo tamaño; arrastralo a "Capa pintada". Al re-hornear tu capa se
// conserva y sigue alineada mientras no muevas el MapArea.

using UnityEngine;

[CreateAssetMenu(menuName = "Re:Quinta/Datos del mapa", fileName = "WorldMapData")]
public class WorldMapData : ScriptableObject
{
  [Header("Resultado del horneado (lo escribe la herramienta)")]
  [Tooltip("Imagen estilizada del nivel.")]
  public Texture2D baseMap;
  [Tooltip("Esquina suroeste del area en el mundo (X, Z).")]
  public Vector2 worldMin;
  [Tooltip("Tamaño del area en el mundo (X, Z).")]
  public Vector2 worldSize;
  public string bakedAt;

  [Header("Capa pintada a mano (opcional)")]
  [Tooltip("PNG transparente del mismo tamaño que el mapa base, con tus dibujos encima.")]
  public Texture2D overlay;

  [Header("Horneado: fotografia del nivel")]
  [Range(256, 4096), Tooltip("Resolucion del lado mayor del mapa.")]
  public int resolution = 2048;
  [Tooltip("Capas que salen en la fotografia (quita jugador, enemigos, UI).")]
  public LayerMask renderLayers = ~((1 << 1) | (1 << 3) | (1 << 5) | (1 << 10) | (1 << 11));
  [Tooltip("Incluir pasto y flores del terreno.")]
  public bool includeGrass;
  [Tooltip("Sombras del sol en la fotografia (puede verse borroso en niveles grandes).")]
  public bool shadows;
  public Color backgroundColor = new Color(0.62f, 0.78f, 0.86f, 1f);

  [Header("Horneado: relieve (para contornos y sombreado)")]
  [Range(128, 2048), Tooltip("Resolucion de la medicion de alturas (lado mayor).")]
  public int heightResolution = 1024;
  [Tooltip("Capas que cuentan como suelo/obstaculo al medir alturas.")]
  public LayerMask heightLayers = ~((1 << 1) | (1 << 2) | (1 << 3) | (1 << 5) | (1 << 8) | (1 << 10) | (1 << 11));

  [Header("Estilo: color")]
  [Range(0, 4), Tooltip("Suavizado de la foto (quita ruido de texturas).")]
  public int blurRadius = 1;
  [Range(0, 16), Tooltip("Niveles de color (0 = sin efecto, 4-8 = aspecto ilustrado).")]
  public int posterizeLevels = 7;
  [Range(0f, 2f)] public float saturation = 0.9f;
  [Range(0f, 2f)] public float brightness = 1.08f;

  [Header("Estilo: papel")]
  public Color paperColor = new Color(0.96f, 0.89f, 0.72f, 1f);
  [Range(0f, 1f), Tooltip("Cuanto se tiñe del color del papel.")]
  public float paperAmount = 0.4f;
  [Range(0f, 0.3f), Tooltip("Textura/manchas del papel.")]
  public float paperNoise = 0.08f;
  [Range(0f, 1f), Tooltip("Bordes quemados/envejecidos.")]
  public float edgeBurn = 0.35f;
  public Color edgeBurnColor = new Color(0.45f, 0.3f, 0.15f, 1f);

  [Header("Estilo: tinta y relieve")]
  public Color inkColor = new Color(0.22f, 0.15f, 0.09f, 1f);
  [Range(0f, 1f), Tooltip("Contorno de muros, arboles, rocas (cambios bruscos de altura).")]
  public float outlineStrength = 0.85f;
  [Min(0.05f), Tooltip("Diferencia de altura (m) que se considera un borde.")]
  public float outlineThreshold = 0.6f;
  [Range(0, 3), Tooltip("Grosor extra del contorno.")]
  public int outlineWidth = 1;
  [Range(0f, 1f), Tooltip("Sombreado del relieve (luz desde el noroeste).")]
  public float hillshade = 0.45f;
  [Min(0f), Tooltip("Separacion (m) entre curvas de nivel. 0 = sin curvas.")]
  public float contourInterval = 2f;
  [Range(0f, 1f)] public float contourStrength = 0.25f;

  [HideInInspector] public float heightMin;
  [HideInInspector] public float heightMax;
  [HideInInspector] public int heightWidth;
  [HideInInspector] public int heightHeight;

  public bool IsBaked => baseMap != null && worldSize.x > 0f && worldSize.y > 0f;

  /// <summary>Posicion del mundo -> coordenada 0..1 del mapa (0,0 = suroeste).</summary>
  public Vector2 WorldToUV(Vector3 position)
  {
    return new Vector2(
      (position.x - worldMin.x) / Mathf.Max(0.001f, worldSize.x),
      (position.z - worldMin.y) / Mathf.Max(0.001f, worldSize.y));
  }

  /// <summary>Ancho / alto del mapa.</summary>
  public float Aspect => worldSize.y > 0f ? worldSize.x / worldSize.y : 1f;
}
