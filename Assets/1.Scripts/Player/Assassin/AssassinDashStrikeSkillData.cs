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
    [Tooltip("경로 판정 영역(할 일 153) — 기준점 = 경로 구간 시작 + 돌진 방향(Point). 첫 직사각형 length = 기준 길이(돌진 거리와 같게 둔다 — " +
             "매 틱 구간마다 구간 길이로 맞춘다), width = 경로 폭, 높이 = 구간 시작(발) 높이 기준. 추격의 궤적도 같은 도형(지나온 선분 길이).")]
    [SerializeField] private AttackShapeSet hitArea = new AttackShapeSet(AttackShapeOrigin.Point, PathRect());
    [SerializeField, Min(1), DataTableIgnore] private int maxHitResults = 16;

    public float DashDistance => dashDistance;
    public float DashSpeed => dashSpeed;
    public float DashDuration => dashSpeed > 0f ? dashDistance / dashSpeed : 0f;
    public AttackShapeSet HitArea => hitArea;
    /// <summary>경로 폭 = 판정 도형 첫 직사각형 width(직사각형이 아니면 0).</summary>
    public float PathWidth => PathShape != null ? PathShape.Width : 0f;
    public int MaxHitResults => maxHitResults;

    /// <summary>사용당 최초 유효 적중에서 이 스킬 쿨타임을 줄이는 초. 일반 Q = 0(보상 없음).</summary>
    public virtual float HitCooldownReduction => 0f;

    private AttackRectShape PathShape => hitArea != null && !hitArea.IsEmpty ? hitArea.Shapes[0] as AttackRectShape : null;

    /// <summary>
    /// 경로 구간 하나의 판정 영역 — 도형을 구간 길이 <paramref name="segmentLength"/> 에 맞춘 복사본(길이 배율 = 구간 길이 / 첫 직사각형 length, 폭 그대로).
    /// 서버 경로 판정(매 틱 구간)·추격의 궤적(지나온 선분 전체)·특성 랩 기즈모(0 ~ 돌진 거리)가 같이 쓴다.
    /// </summary>
    public AttackShapeSet PathArea(float segmentLength)
    {
        float full = PathShape != null ? PathShape.Length : 0f;
        return hitArea.Scaled(full > 0f ? Mathf.Max(0f, segmentLength) / full : 0f, 1f);
    }

    /// <summary>구간 기준 포즈 — 기준점 종류가 Point 면 구간 시작 <paramref name="segmentStart"/>, CasterAim 이면 시전자 발밑. 정면 = 돌진 방향.</summary>
    public AttackShapePose PathPose(Vector3 casterPosition, Vector3 segmentStart, Vector3 direction) =>
        hitArea.ResolvePose(casterPosition, direction, segmentStart, direction);

    /// <summary>
    /// 기본 판정 도형(할 일 153) — 예전 경로 상자(<c>AssassinDashStrikeSkill.PathBox</c>: 구간 시작 → 끝, 폭 1.2 · 높이 2 를 발 높이부터 위로)를 옮긴 값.
    /// length 5 = 기준 길이(SO 돌진 거리), 뒤 0, 높이 = 발 기준 0~2.
    /// </summary>
    public static AttackRectShape PathRect() =>
        new AttackRectShape(5f, 1.2f) { MinHeight = 0f, MaxHeight = 2f };
}
