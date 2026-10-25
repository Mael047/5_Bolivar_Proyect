using UnityEngine;

public class Health : MonoBehaviour, IDamageable
{
    [SerializeField] private int maxHealth = 100;
    private int currentHealth;

    [Tooltip("Segundos que tarda en destruirse tras morir. Mayor que 0 permite ver/jugar la animacion de muerte antes del Destroy.")]
    [SerializeField] private float destroyDelay = 0f;

    /// <summary>Se dispara justo antes de destruir el objeto (o de programar el destroy).</summary>
    public event System.Action Died;

    /// <summary>Se dispara cada vez que cambia la vida: (vida actual, vida maxima, cambio). Cambio negativo = daño.</summary>
    public event System.Action<int, int, int> Changed;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsDead => currentHealth <= 0;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(int damageAmount)
    {
        if (IsDead)
        {
            return;
        }

        currentHealth = Mathf.Max(0, currentHealth - damageAmount);
        Debug.Log($"{gameObject.name} recibió {damageAmount} de daño. Vida restante: {currentHealth}/{maxHealth}");
        Changed?.Invoke(currentHealth, maxHealth, -damageAmount);

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    /// <summary>Recupera vida sin pasar del maximo (pociones, puntos de guardado...).</summary>
    public void Heal(int amount)
    {
        if (IsDead || amount <= 0)
        {
            return;
        }

        var before = currentHealth;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);

        if (currentHealth != before)
        {
            Changed?.Invoke(currentHealth, maxHealth, currentHealth - before);
        }
    }

    private void Die()
    {
        Debug.Log($"{gameObject.name} ha sido derrotado.");
        Died?.Invoke();

        if (destroyDelay > 0f)
        {
            Invoke(nameof(DestroyDelayed), destroyDelay);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void DestroyDelayed()
    {
        if (this != null)
        {
            Destroy(gameObject);
        }
    }
}