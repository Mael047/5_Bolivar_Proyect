using UnityEngine;
using System.Collections.Generic;

public class MeleeHitbox : MonoBehaviour
{
    [SerializeField] public int damage = 20;
    [SerializeField] private LayerMask targetLayer; // Capa de a quien hace daño

    private List<Collider> alreadyHit = new List<Collider>();
    private Collider hitboxCollider;

    private void Awake()
    {
        hitboxCollider = GetComponent<Collider>();
        if (hitboxCollider != null)
        {
            hitboxCollider.enabled = false; // Desactivado por defecto
        }
    }

    public void EnableHitbox()
    {
        alreadyHit.Clear();
        if (hitboxCollider != null) hitboxCollider.enabled = true;
    }

    public void DisableHitbox()
    {
        if (hitboxCollider != null) hitboxCollider.enabled = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        // Verifica si el objeto golpeado esta en la Layer objetivo
        if (((1 << other.gameObject.layer) & targetLayer) != 0)
        {
            if (!alreadyHit.Contains(other))
            {
                alreadyHit.Add(other);

                // Busca el componente Breakable en el objeto impactado y lo rompe de un golpe
                if (other.TryGetComponent<NuiGrab.Breakable>(out var breakable))
                {
                    breakable.Break(other.ClosestPoint(transform.position), Vector3.zero);
                    return;
                }

                // Busca el componente IDamageable en el objeto impactado
                if (other.TryGetComponent<IDamageable>(out var damageable))
                {
                    damageable.TakeDamage(damage);
                }
            }
        }
    }
}