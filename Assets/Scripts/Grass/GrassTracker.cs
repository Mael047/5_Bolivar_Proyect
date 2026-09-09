using UnityEngine;

public class GrassTracker : MonoBehaviour
{
    private void OnEnable()
    {
        GrassReaction.Register(this);
    }

    private void OnDisable()
    {
        GrassReaction.Unregister(this);
    }

    public Vector3 WorldPosition
    {
        get { return transform.position; }
    }
}