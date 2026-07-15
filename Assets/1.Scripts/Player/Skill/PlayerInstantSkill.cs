using UnityEngine;

/// <summary>
/// 프레스 즉발형 스킬 타입 (E 수호자의 의지, 우클릭 단죄의 방패).
/// 시작 즉시 Active로 전환하고, 애니메이션 End 이벤트로 종료한다.
/// 판정(Hit) 처리는 구체 스킬이 OnAnimationEvent를 override 해서 구현한다 (base 호출 유지).
/// </summary>
public abstract class PlayerInstantSkill : PlayerSkillBase
{
    public override void OnServerStart(Vector3 direction, Unit target)
    {
        State = SkillState.Active;
    }

    public override void OnAnimationEvent(SkillAnimationEventType eventType)
    {
        if (eventType == SkillAnimationEventType.End)
            EndSelf(SkillEndReason.Completed);
    }
}
