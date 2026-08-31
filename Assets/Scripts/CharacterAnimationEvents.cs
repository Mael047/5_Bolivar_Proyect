using UnityEngine;

public class CharacterAnimationEvents : MonoBehaviour
{
    private Player player;

    private void Awake()
    {
        // Busca el script Player en el objeto padre
        player = GetComponentInParent<Player>();
    }

    public void AE_StartAttack()
    {
        if (player != null) player.AE_StartAttack();
    }

    public void AE_EndAttack()
    {
        if (player != null) player.AE_EndAttack();
    }
}