using UnityEngine;

public class Health : MonoBehaviour, IDamageable
{
    [SerializeField] private int maxHealth = 100;
    private int currentHealth;

    [Tooltip("Segundos que tarda en destruirse tras morir. Mayor que 0 permite ver/jugar la animacion de muerte antes del Destroy.")]
    [SerializeField] private float destroyDelay = 0f;

    /// <summary>Se dispara justo antes de destruir el objeto (o de programar el destroy).</summary>
    public event System.Action Died;

    public int CurrentHealth => currentHealth;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(int damageAmount)
    {
        currentHealth -= damageAmount;
        Debug.Log($"{gameObject.name} recibió {damageAmount} de daño. Vida restante: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0)
        {
            Die();
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