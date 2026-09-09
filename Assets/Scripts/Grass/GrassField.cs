using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class GrassField : MonoBehaviour
{
    [Header("Referencias")]
    public Terrain terrain;
    public Mesh bladeMesh;
    public Material material;

    [Header("Generacion")]
    [Tooltip("Muestrear 1 celda de detalle de cada N (4 = 1 de cada 4).")]
    public int samplingStep = 4;

    [Tooltip("Limite de instancias del campo.")]
    public int targetInstances = 40000;

    [Tooltip("Reconstruir automaticamente cuando cambian las capas de detalle.")]
    public bool autoRefresh = true;

    public bool castShadows = true;

    private readonly List<Matrix4x4> m_Matrices = new List<Matrix4x4>();
    private Matrix4x4[] m_Array = new Matrix4x4[0];
    private int m_Stamp = int.MinValue;
    private float m_StampTimer;

    private void OnEnable()
    {
        Rebuild();
    }

    private void OnDisable()
    {
        m_Matrices.Clear();
        m_Array = new Matrix4x4[0];
    }

    private void Update()
    {
        if (autoRefresh && terrain != null && terrain.terrainData != null)
        {
            m_StampTimer -= Time.unscaledDeltaTime;
            if (m_StampTimer <= 0f)
            {
                m_StampTimer = 0.5f;
                int stamp = ComputeStamp();
                if (stamp != m_Stamp)
                {
                    m_Stamp = stamp;
                    Rebuild();
                }
            }
        }
        if (m_Matrices.Count > 0)
        {
            Render();
        }
    }

    [ContextMenu("Rebuild Grass Field")]
    public void Rebuild()
    {
        m_Matrices.Clear();
        if (terrain == null || terrain.terrainData == null || bladeMesh == null || material == null)
        {
            return;
        }

        TerrainData td = terrain.terrainData;
        int w = td.detailWidth;
        int h = td.detailHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        int layers = td.detailPrototypes.Length;
        if (layers <= 0)
        {
            return;
        }

        Vector3 o = terrain.transform.position;
        float scaleX = td.size.x / (float)w;
        float scaleZ = td.size.z / (float)h;
        System.Random rng = new System.Random(1207);
        int budget = Mathf.Max(1, targetInstances);
        int step = Mathf.Max(1, samplingStep);

        for (int l = 0; l < layers && budget > 0; l++)
        {
            int[,] layer = td.GetDetailLayer(0, 0, w, h, l);
            if (layer == null)
            {
                continue;
            }
            for (int y = 0; y < h && budget > 0; y += step)
            {
                for (int x = 0; x < w && budget > 0; x += step)
                {
                    int dens = layer[x, y];
                    if (dens < 1)
                    {
                        continue;
                    }
                    float p = dens / 255f;
                    if ((float)rng.NextDouble() > p)
                    {
                        continue;
                    }
                    float wx = o.x + (x + (float)rng.NextDouble()) * scaleX;
                    float wz = o.z + (y + (float)rng.NextDouble()) * scaleZ;
                    Vector3 sample = new Vector3(wx, 0f, wz);
                    float wy = terrain.SampleHeight(sample) + o.y;
                    if (wy < -1000f)
                    {
                        continue;
                    }
                    float yaw = (float)(rng.NextDouble() * 360.0);
                    float scale = 0.7f + (float)rng.NextDouble() * 0.6f;
                    m_Matrices.Add(Matrix4x4.TRS(
                        new Vector3(wx, wy, wz),
                        Quaternion.Euler(0f, yaw, 0f),
                        new Vector3(scale, scale, scale)));
                    budget--;
                }
            }
        }
        m_Array = m_Matrices.ToArray();
    }

    private int ComputeStamp()
    {
        TerrainData td = terrain.terrainData;
        int n = Mathf.Min(128, td.detailWidth);
        int h = 17;
        for (int l = 0; l < td.detailPrototypes.Length && l < 2; l++)
        {
            int[,] a = td.GetDetailLayer(0, 0, n, n, l);
            if (a == null)
            {
                continue;
            }
            for (int y = 0; y < n; y += 2)
            {
                for (int x = 0; x < n; x += 2)
                {
                    h = h * 31 + a[x, y] + 1;
                }
            }
        }
        return h;
    }

    private void Render()
    {
        int chunk = 1023;
        for (int i = 0; i < m_Matrices.Count; i += chunk)
        {
            int n = Mathf.Min(chunk, m_Matrices.Count - i);
            if (i == 0 && n == m_Matrices.Count)
            {
                Graphics.DrawMeshInstanced(bladeMesh, 0, material, m_Array, n, null,
                    castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off, true,
                    gameObject.layer, null, LightProbeUsage.BlendProbes);
            }
            else
            {
                Matrix4x4[] slice = new Matrix4x4[n];
                System.Array.Copy(m_Array, i, slice, 0, n);
                Graphics.DrawMeshInstanced(bladeMesh, 0, material, slice, n, null,
                    castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off, true,
                    gameObject.layer, null, LightProbeUsage.BlendProbes);
            }
        }
    }
}