using UnityEngine;

public class PlayerAnimationEventRelay : MonoBehaviour
{
    private Player player;
    private IPlayerBasicAttack defaultAttack;
    private PlayerSkillController skillController;

    private void Awake()
    {
        player = GetComponentInParent<Player>();
        defaultAttack = GetComponentInParent<IPlayerBasicAttack>();
        skillController = GetComponentInParent<PlayerSkillController>();
    }

    public void EndDefaultAttack()
    {
        HandleDefaultAttackEvent(DefaultAttackAnimationEventType.End);
    }

    public void HitDefaultAttack()
    {
        HandleDefaultAttackEvent(DefaultAttackAnimationEventType.Hit);
    }

    // 평타 클립의 애니메이션 이벤트 수신. 파라미터가 enum 이라 Animation 창 이벤트 인스펙터에 이름 드롭다운이 뜬다.
    // 클립에는 intParameter(0~3)로 저장된다 — enum 값을 바꾸면 기존 클립 이벤트가 다른 의미가 되므로 값은 고정. 오버로드 금지(인스펙터 목록에서 빠진다).
    public void HandleDefaultAttackEvent(DefaultAttackAnimationEventType eventType)
    {
        if (defaultAttack == null)
            defaultAttack = GetComponentInParent<IPlayerBasicAttack>();

        defaultAttack?.HandleAnimationEvent(eventType);
    }

    public void EndInterrupt()
    {
        if (player == null)
            player = GetComponentInParent<Player>();

        player?.EndInterrupt();
    }

    // 스킬 클립의 애니메이션 이벤트 수신. 평타와 같이 enum 드롭다운 + intParameter(0~3) 저장.
    public void HandleSkillEvent(SkillAnimationEventType eventType)
    {
        if (skillController == null)
            skillController = GetComponentInParent<PlayerSkillController>();

        skillController?.HandleAnimationEvent(eventType);
    }
}
