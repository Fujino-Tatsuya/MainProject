using UnityEngine;

public class PlayerAnimationEventRelay : MonoBehaviour
{
    private Player player;

    private void Awake()
    {
        player = GetComponentInParent<Player>();
    }

    public void EndDefaultAttack()
    {
        if (player == null)
            player = GetComponentInParent<Player>();

        player?.EndDefaultAttack();
    }

    public void EndInterrupt()
    {
        if (player == null)
            player = GetComponentInParent<Player>();

        player?.EndInterrupt();
    }
}
