using UnityEngine;

public class PlayerAnimationEventRelay : MonoBehaviour
{
    private Player player;
    private DefaultAttackController defaultAttack;

    private void Awake()
    {
        player = GetComponentInParent<Player>();
        defaultAttack = GetComponentInParent<DefaultAttackController>();
    }

    public void EndDefaultAttack()
    {
        HandleDefaultAttackEvent((int)DefaultAttackAnimationEventType.End);
    }

    public void HitDefaultAttack()
    {
        HandleDefaultAttackEvent((int)DefaultAttackAnimationEventType.Hit);
    }

    public void HandleDefaultAttackEvent(int eventType)
    {
        if (defaultAttack == null)
            defaultAttack = GetComponentInParent<DefaultAttackController>();

        defaultAttack?.HandleAnimationEvent((DefaultAttackAnimationEventType)eventType);
    }

    public void EndInterrupt()
    {
        if (player == null)
            player = GetComponentInParent<Player>();

        player?.EndInterrupt();
    }
}
