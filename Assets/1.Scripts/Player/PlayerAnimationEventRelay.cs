using UnityEngine;

public class PlayerAnimationEventRelay : MonoBehaviour
{
    private Player player;
    private DefaultAttackController defaultAttack;
    private PlayerSkillController skillController;

    private void Awake()
    {
        player = GetComponentInParent<Player>();
        defaultAttack = GetComponentInParent<DefaultAttackController>();
        skillController = GetComponentInParent<PlayerSkillController>();
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

    // 스킬 클립의 애니메이션 이벤트 수신 (int = SkillAnimationEventType)
    public void HandleSkillEvent(int eventType)
    {
        if (skillController == null)
            skillController = GetComponentInParent<PlayerSkillController>();

        skillController?.HandleAnimationEvent((SkillAnimationEventType)eventType);
    }
}
