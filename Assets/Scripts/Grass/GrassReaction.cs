using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[ExecuteAlways]
public class GrassReaction : MonoBehaviour
{
    private static readonly List<GrassTracker> s_Trackers = new List<GrassTracker>();
    private static GrassReaction s_Instance;

    [Tooltip("Radio global que usan todas las matitas (mismo valor que _Radius del material).")]
    public float radius = 2.0f;

    [Tooltip("Nombre de la propiedad global Vector que el shader lee.")]
    public string trackerPositionProperty = "_TrackerPosition";

    [Tooltip("Nombre de la propiedad global Float activo/inactivo.")]
    public string trackerActiveProperty = "_TrackerActive";

    [Tooltip("Radio global que se sube a _Radius.")]
    public string radiusProperty = "_Radius";

    [Tooltip("Seguir tambien un objeto llamado 'tracker' en la escena (util si no usas GrassTracker).")]
    public bool trackGameObjectNamedTracker = true;

    private Transform m_NamedTracker;

    private void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            if (s_Instance.name == "GrassReaction")
            {
                Object.Destroy(s_Instance.gameObject);
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }
        s_Instance = this;
    }

    private void OnDestroy()
    {
        if (s_Instance == this)
        {
            s_Instance = null;
        }
    }

    private void Update()
    {
        Transform best = FindNearest();
        Vector3 pos = (best != null) ? best.position : Vector3.zero;
        bool active = best != null;

        if (active && trackGameObjectNamedTracker && m_NamedTracker == null)
        {
            m_NamedTracker = GameObject.Find("tracker") != null ? GameObject.Find("tracker").transform : null;
        }

        Shader.SetGlobalVector(trackerPositionProperty, new Vector4(pos.x, pos.y, pos.z, 1.0f));
        Shader.SetGlobalFloat(trackerActiveProperty, active ? 1.0f : 0.0f);
        Shader.SetGlobalFloat(radiusProperty, radius);
    }

    private Transform FindNearest()
    {
        Transform named = null;
        if (trackGameObjectNamedTracker)
        {
            if (m_NamedTracker == null)
            {
                GameObject go = GameObject.Find("tracker");
                m_NamedTracker = go != null ? go.transform : null;
            }
            named = m_NamedTracker;
        }

        Transform best = named;
        float bestSqr = float.MaxValue;
        if (named != null)
        {
            bestSqr = (named.position - transform.position).sqrMagnitude;
        }

        Vector3 refPos = transform.position;
        for (int i = 0; i < s_Trackers.Count; i++)
        {
            if (s_Trackers[i] == null)
            {
                continue;
            }
            Vector3 diff = s_Trackers[i].WorldPosition - refPos;
            float sqr = diff.sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = s_Trackers[i].transform;
            }
        }
        return best;
    }

    public static void Register(GrassTracker tracker)
    {
        if (tracker == null || s_Trackers.Contains(tracker))
        {
            return;
        }
        s_Trackers.Add(tracker);

        if (s_Instance == null)
        {
            if (Application.isPlaying)
            {
                s_Instance = Object.FindObjectOfType<GrassReaction>();
            }
        }
        if (s_Instance == null && Application.isPlaying)
        {
            GameObject go = new GameObject("GrassReaction");
            go.transform.position = Vector3.up * 1000f;
            s_Instance = go.AddComponent<GrassReaction>();
        }
    }

    public static void Unregister(GrassTracker tracker)
    {
        s_Trackers.Remove(tracker);
    }
}