using UnityEngine;

/// <summary>
/// 우클릭 단죄의 방패 설계값. 수치는 전부 임시값 — 기획 확정 시 에셋에서만 조절한다.
/// 베이스(PlayerSkillData)의 InputType은 Press여야 한다.
/// </summary>
[CreateAssetMenu(menuName = "Combat/First Melee Interrupt Skill Data")]
[DataTableSheet("Paladin", Order = 4)]
public class FirstMeleeInterruptSkillData : PlayerSkillData, IPlayerInterruptSkillData
{
    [Header("판정 타이밍")]
    // 🔴 지금 안 쓴다 — 판정·종료는 Garen_Parry 의 Hit·End 이벤트가 정한다(FirstMeleeInterruptSkill.TimingFromAnimationEvents,
    //    PLAN-anim-event-timing P1-④). 열 삭제는 Export 정리 때 한다. 시점 튜닝은 클립에서 이벤트를 옮긴다.
    [SerializeField, Min(0f)] private float hitDelay = 0.15f;
    [SerializeField, Min(0.05f)] private float skillDuration = 0.6f;

    [Header("판정")]
    [SerializeField, Min(1), DataTableIgnore] private int maxHitResults = 8;

    public float HitDelay => hitDelay;
    public float SkillDuration => skillDuration;
    public int MaxHitResults => maxHitResults;
}
