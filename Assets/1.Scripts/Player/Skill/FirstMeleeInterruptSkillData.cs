using UnityEngine;

/// <summary>
/// 우클릭 단죄의 방패 설계값. 수치는 전부 임시값 — 기획 확정 시 에셋에서만 조절한다.
/// 베이스(PlayerSkillData)의 InputType은 Press여야 한다.
/// </summary>
[CreateAssetMenu(menuName = "Combat/First Melee Interrupt Skill Data")]
public class FirstMeleeInterruptSkillData : PlayerSkillData
{
    [Header("판정 타이밍")]
    // 시전 → 판정까지의 선딜(초). 클립에 Hit 애니메이션 이벤트를 심으면 그쪽이 우선한다.
    [SerializeField, Min(0f)] private float hitDelay = 0.15f;
    // 스킬 자체 종료 시각(초). End 애니메이션 이벤트가 있으면 그쪽이 먼저 끝낸다.
    // MaxActiveDuration(강제 종료 안전망)보다 작아야 한다.
    [SerializeField, Min(0.05f)] private float skillDuration = 0.6f;

    [Header("판정")]
    [SerializeField, Min(1)] private int maxHitResults = 8;

    public float HitDelay => hitDelay;
    public float SkillDuration => skillDuration;
    public int MaxHitResults => maxHitResults;
}
