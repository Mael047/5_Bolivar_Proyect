using UnityEngine;
using System.Collections.Generic;

public class MeleeHitbox : MonoBehaviour
{
    [SerializeField] public int damage = 20;
    [SerializeField] private LayerMask targetLayer; // Capa de a quien hace daño

    [Tooltip("Quien empuña esta hitbox. Vacio = se busca el Rigidbody padre (el jugador). " +
             "Se usa para calcular la direccion del empuje.")]
    [SerializeField] private Transform owner;

    // Un mismo objetivo puede tener varios colliders (cuerpo, ojos...): se
    // registra el objetivo, no el collider, para no pegarle dos veces por swing.
    /// <summary>
    /// Contundencia del golpe actual (la fija Player segun el ataque del combo).
    /// Escala temblor, hit-stop y empuje en el HitReaction del objetivo.
    /// </summary>
    public float ImpactMultiplier { get; set; } = 1f;

    /// <summary>
    /// Sensacion de impacto del lado del jugador para el golpe actual (camara,
    /// camara lenta, vibracion...). La fija Player segun el ataque del combo.
    /// </summary>
    public ImpactSettings Impact { get; set; }

    private bool impactPlayedThisSwing;

    private readonly HashSet<Object> alreadyHit = new HashSet<Object>();
    private Collider hitboxCollider;

    private void Awake()
    {
        hitboxCollider = GetComponent<Collider>();
        if (hitboxCollider != null)
        {
            hitboxCollider.enabled = false; // Desactivado por defecto
        }

        if (owner == null)
        {
            var body = GetComponentInParent<Rigidbody>();
            owner = body != null ? body.transform : transform.root;
        }
    }

    public void EnableHitbox()
    {
        alreadyHit.Clear();
        impactPlayedThisSwing = false;
        if (hitboxCollider != null) hitboxCollider.enabled = true;
    }

    public void DisableHitbox()
    {
        if (hitboxCollider != null) hitboxCollider.enabled = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        // Verifica si el objeto golpeado esta en la Layer objetivo
        if (((1 << other.gameObject.layer) & targetLayer) == 0)
        {
            return;
        }

        // Busca el componente Breakable en el objeto impactado y lo rompe de un golpe
        if (other.TryGetComponent<NuiGrab.Breakable>(out var breakable))
        {
            if (alreadyHit.Add(breakable))
            {
                breakable.Break(other.ClosestPoint(transform.position), Vector3.zero);
            }
            return;
        }

        // Busca el componente IDamageable en el objeto impactado o en sus padres
        var damageable = other.GetComponentInParent<IDamageable>();
        if (damageable == null)
        {
            return;
        }

        var key = damageable as Object;
        if (key != null && !alreadyHit.Add(key))
        {
            return;
        }

        damageable.TakeDamage(damage);

        var hitPoint = other.ClosestPoint(transform.position);
        var origin = owner != null ? owner.position : transform.position;
        var targetTransform = (damageable as Component) != null ? ((Component)damageable).transform : other.transform;
        var direction = targetTransform.position - origin;

        // Reaccion al golpe (empuje, temblor, destello...) si el objetivo la tiene.
        var reaction = other.GetComponentInParent<HitReaction>();
        if (reaction != null)
        {
            reaction.React(hitPoint, direction, owner, ImpactMultiplier);
        }

        // Sensacion del lado del jugador: una sola vez por swing aunque la
        // espada atraviese a varios enemigos.
        if (Impact != null && !impactPlayedThisSwing)
        {
            impactPlayedThisSwing = true;
            ImpactFeedback.Play(Impact, hitPoint, direction);
        }
    }
}
