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
    // ⚠️ 미사용 2칸(PLAN-anim-event-timing P2-3) — 준비 끝 = 클립 Custom0, 종료 = 클립 End 이벤트(돌진 클립에서 옮겨 튜닝).
    //    GameData.xlsx Assassin 시트에 행이 있어 지우면 테이블 모드가 막힌다 — 다음 병합 Export 때 행부터 정리하고 지운다.
#pragma warning disable CS0414
    [Header("단계 (미사용 — 클립 Custom0·End 이벤트)")]
    [Tooltip("⚠️ 미사용 — 준비 끝(돌진 출발)은 돌진 클립의 Custom0 이벤트.")]
    [SerializeField, Min(0f)] private float prepareDuration = 0.1f;
    [Tooltip("⚠️ 미사용 — 종료는 돌진 클립의 End 이벤트(경로 끝 뒤에 둔다). 이벤트가 안 오면 maxActiveDuration 안전망.")]
    [SerializeField, Min(0f)] private float endDuration = 0.1f;
#pragma warning restore CS0414

    [Header("돌진")]
    [SerializeField, Min(0f)] private float dashDistance = 4f;
    [SerializeField, Min(0.1f)] private float dashSpeed = 20f;
    [Tooltip("경로 판정 폭(m). 매 틱 이동 구간을 이 폭으로 훑는다")]
    [SerializeField, Min(0f)] private float pathWidth = 1.2f;
    [SerializeField, Min(0.1f), DataTableIgnore] private float pathHeight = 2f;
    [SerializeField, Min(1), DataTableIgnore] private int maxHitResults = 16;

    public float DashDistance => dashDistance;
    public float DashSpeed => dashSpeed;
    public float DashDuration => dashSpeed > 0f ? dashDistance / dashSpeed : 0f;
    public float PathWidth => pathWidth;
    public float PathHeight => pathHeight;
    public int MaxHitResults => maxHitResults;

    /// <summary>사용당 최초 유효 적중에서 이 스킬 쿨타임을 줄이는 초. 일반 Q = 0(보상 없음).</summary>
    public virtual float HitCooldownReduction => 0f;
}
