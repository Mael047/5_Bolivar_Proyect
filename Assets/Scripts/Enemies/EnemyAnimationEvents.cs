using UnityEngine;

/// <summary>
/// Suelta los AnimationEvents de los clips reutilizados del Player
/// (Attack1.fbx dispara AE_StartAttack / AE_EndAttack) en GameObjects de
/// enemigos, que no tienen hitbox de espada. El EndAttack reinicia el ciclo
/// de ataque del Enemy.
/// </summary>
public class EnemyAnimationEvents : MonoBehaviour
{
    private Enemy enemy;

    private void Awake()
    {
        enemy = GetComponent<Enemy>();
    }

    public void AE_StartAttack()
    {
    }

    public void AE_EndAttack()
    {
        if (enemy != null) enemy.Final_Ani();
    }

    public void AE_StartInvincibility()
    {
    }

    public void AE_EndInvincibility()
    {
    }
}