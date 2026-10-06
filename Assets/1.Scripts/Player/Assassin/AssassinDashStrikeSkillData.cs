using UnityEngine;

/// <summary>
/// 일반 Q 관통 돌진 설계값(character_assassin.md §7.2). 피해 계수 ×1.5·쿨 5초는 베이스 필드.
/// 쿨타임은 돌진 시작(= 서버 승인) 시점이라 <c>commitCooldownManually</c> = false.
/// 변신 Q 는 <see cref="AssassinTransformedDashStrikeSkillData"/>(쿨 3초 + 적중 차감).
/// </summary>
[CreateAssetMenu(fileName = "AssassinDashStrikeSkillData", menuName = "Player/Assassin/Dash Strike Skill Data (일반 Q)")]
[DataTableSheet("Assassin", Order = 2)]
public class AssassinDashStrikeSkillData : PlayerSkillData
{
    [Header("단계 (10-06 은희) — 준비 → 돌진 → 종료")]
    [Tooltip("준비(초) — 승인 후 제자리에서 웅크리는 시간(애니 미정). 이 동안 이동·판정 없음.")]
    [SerializeField, Min(0f)] private float prepareDuration = 0.1f;
    [Tooltip("종료(초) — 도착 후 제자리에서 머무는 시간. 궤적 이펙트가 늦게 따라와 속도감을 준다.")]
    [SerializeField, Min(0f)] private float endDuration = 0.1f;

    [Header("돌진")]
    [SerializeField, Min(0f)] private float dashDistance = 4f;
    [SerializeField, Min(0.1f)] private float dashSpeed = 20f;
    [Tooltip("경로 판정 폭(m). 매 틱 이동 구간을 이 폭으로 훑는다")]
    [SerializeField, Min(0f)] private float pathWidth = 1.2f;
    [SerializeField, Min(0.1f), DataTableIgnore] private float pathHeight = 2f;
    [SerializeField, Min(1), DataTableIgnore] private int maxHitResults = 16;

    public float PrepareDuration => prepareDuration;
    public float EndDuration => endDuration;
    public float DashDistance => dashDistance;
    public float DashSpeed => dashSpeed;
    public float DashDuration => dashSpeed > 0f ? dashDistance / dashSpeed : 0f;
    public float PathWidth => pathWidth;
    public float PathHeight => pathHeight;
    public int MaxHitResults => maxHitResults;

    /// <summary>사용당 최초 유효 적중에서 이 스킬 쿨타임을 줄이는 초. 일반 Q = 0(보상 없음).</summary>
    public virtual float HitCooldownReduction => 0f;
}
