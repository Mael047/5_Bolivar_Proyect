using UnityEngine;

[RequireComponent(typeof(Animator))]
public class EnemyAnimator : MonoBehaviour
{
    private const string IsMovingParam = "isMoving";
    private const string AttackParam = "Attack";
    private const string HitParam = "Hit";
    private const string DeadParam = "Dead";

    [SerializeField] private Animator animator;
    [SerializeField] private float deathRollDuration = 0.7f;

    private Enemy enemy;
    private HitReaction hitReaction;
    private Health health;
    private Collider[] colliders;
    private Rigidbody rb;

    private bool wasAttacking;
    private bool wasStunned;
    private bool isDead;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        enemy = GetComponent<Enemy>();
        hitReaction = GetComponent<HitReaction>();
        health = GetComponent<Health>();
        colliders = GetComponentsInChildren<Collider>(true);
        rb = GetComponent<Rigidbody>();

        if (animator != null)
        {
            animator.applyRootMotion = false;
        }
    }

    private void OnEnable()
    {
        if (health != null) health.Died += OnDied;
    }

    private void OnDisable()
    {
        if (health != null) health.Died -= OnDied;
    }

    private void Update()
    {
        if (animator == null || isDead) return;

        if (enemy != null)
        {
            animator.SetBool(IsMovingParam, enemy.IsMovingNow);

            var attacking = enemy.attack;
            if (attacking && !wasAttacking)
            {
                animator.SetTrigger(AttackParam);
            }
            wasAttacking = attacking;
        }

        var stunned = hitReaction != null && hitReaction.IsStunned;
        if (stunned && !wasStunned)
        {
            animator.SetTrigger(HitParam);
        }
        wasStunned = stunned;
    }

    private void OnDied()
    {
        if (isDead) return;
        isDead = true;

        if (animator != null) animator.SetBool(DeadParam, true);

        foreach (var c in colliders)
        {
            if (c != null) c.enabled = false;
        }
        if (rb != null) rb.isKinematic = true;
        if (enemy != null) enemy.enabled = false;

        StartCoroutine(PlayDeathRoll());
    }

    private System.Collections.IEnumerator PlayDeathRoll()
    {
        var start = transform.rotation;
        var target = start * Quaternion.Euler(-90f, 0f, 0f);
        var elapsed = 0f;

        while (elapsed < deathRollDuration)
        {
            elapsed += Time.deltaTime;
            var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / deathRollDuration));
            transform.rotation = Quaternion.Slerp(start, target, k);
            yield return null;
        }

        transform.rotation = target;
    }
}