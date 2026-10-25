using UnityEngine;

public class CharacterAnimationEvents : MonoBehaviour
{
    private Player player;
    private Collider playerCollider;

    private void Awake()
    {
        // Busca el script Player en el objeto padre
        player = GetComponentInParent<Player>();
        playerCollider = GetComponentInParent<Collider>();
    }

    public void AE_StartAttack()
    {
        if (player != null) player.AE_StartAttack();
    }

    public void AE_EndAttack()
    {
        if (player != null) player.AE_EndAttack();
    }

    public void AE_StartInvincibility()
    {
        if (player != null) player.AE_StartInvincibility();
    }

    public void AE_EndInvincibility()
    {
        if (player != null) player.AE_EndInvincibility();
    }

}