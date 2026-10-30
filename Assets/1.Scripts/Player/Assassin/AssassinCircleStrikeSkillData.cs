using UnityEngine;

/// <summary>
/// 변신 E 원형 5타 설계값(character_assassin.md §9.3). 타당 계수 ×2.8·쿨 12초·조준 고정 거리 1.5m 는 베이스 필드 —
/// 원 중심 거리 = <c>castRange</c>, 원 반경(효과 반경 2m) = <c>hitArea</c> 첫 원 radius(할 일 153 — 베이스 <c>aoeRadius</c> 는 이 SO 에선 미사용). 쿨타임은 확정(= 공격 시작, 서버 승인) 시점이라
/// <c>commitCooldownManually</c> = false — 조준만 하고 취소하면 쿨이 없다.
/// </summary>
[CreateAssetMenu(fileName = "AssassinCircleStrikeSkillData", menuName = "Player/Assassin/Circle Strike Skill Data (변신 E)")]
[DataTableSheet("Assassin", Order = 4)]
public sealed class AssassinCircleStrikeSkillData : PlayerSkillData
{
    [Header("원형 연타")]
    [Tooltip("동일 피해 타격 수. 클립 Hit 이벤트마다 1타, End 가 먼저 오면 남은 타를 마저 친다")]
    [SerializeField, Min(1)] private int hitCount = 5;
    [Tooltip("판정 영역(할 일 153) — 기준점 = 원 중심(지면 조준점, 높이 = 시전자 발) + 시전자 → 중심 방향(Point). " +
             "첫 원 radius = 효과 반경(조준 원·원 베기 VFX 도 이 값 — 베이스 aoeRadius 대신), 높이 = 발 기준. 칼날의 영역은 반경(길이) 배율.")]
    [SerializeField] private AttackShapeSet hitArea = new AttackShapeSet(AttackShapeOrigin.Point, StrikeCircle());
    [SerializeField, Min(1), DataTableIgnore] private int maxHitResults = 32;

    public int HitCount => hitCount;
    public AttackShapeSet HitArea => hitArea;
    /// <summary>효과 반경 = 판정 도형 첫 원 radius(원이 아니면 0) — 조준 원(<c>PlayerSkillTargeting.GroundMarkerRadius</c>)도 이 값을 읽는다.</summary>
    public override float AoeRadius => StrikeShape != null ? StrikeShape.Radius : 0f;
    public float AreaRadius => AoeRadius;
    public float CenterDistance => CastRange;
    public int MaxHitResults => maxHitResults;

    private AttackCircleShape StrikeShape => hitArea != null && !hitArea.IsEmpty ? hitArea.Shapes[0] as AttackCircleShape : null;

    /// <summary>타격 판정 영역 — 칼날의 영역 반경 배율 <paramref name="radiusMultiplier"/> 를 길이 축으로 바른 복사본(1 이면 원본 그대로, SO 불변).</summary>
    public AttackShapeSet StrikeArea(float radiusMultiplier) =>
        radiusMultiplier == 1f ? hitArea : hitArea.Scaled(radiusMultiplier, 1f);

    /// <summary>타격 기준 포즈 — 기준점 종류가 Point 면 원 중심, CasterAim 이면 시전자 발밑. 정면 = 시전자 → 원 중심(바닥 평면, 같으면 +z).</summary>
    public AttackShapePose StrikePose(Vector3 casterPosition, Vector3 areaCenter)
    {
        Vector3 forward = areaCenter - casterPosition;
        return hitArea.ResolvePose(casterPosition, forward, areaCenter, forward);
    }

    /// <summary>
    /// 기본 판정 도형(할 일 153) — 예전 원기둥(<c>SkillHitVolume.CircleColumn</c>: 반경 aoeRadius 2 · 높이 areaHeight 3 을 발 높이부터 위로)을 옮긴 값.
    /// 높이 = 발 기준 0~3.
    /// </summary>
    public static AttackCircleShape StrikeCircle() =>
        new AttackCircleShape(2f) { MinHeight = 0f, MaxHeight = 3f };
}
