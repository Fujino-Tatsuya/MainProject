using UnityEngine;

/// <summary>
/// 스킬 피격 판정 영역 한 개 — 서버 판정과 오너 미리보기(스킬 범위 외곽선, 할 일 58)가 <b>같은 값</b>을 쓴다.
/// 서버는 이 값으로 겹침을 돌려 피해를 주고, 미리보기는 같은 값으로 겹침을 돌려 맞을 적을 칠한다 — 기하가 갈라질 곳이 없다.
///
/// 모양 둘:
/// <list type="bullet">
/// <item><see cref="Box"/>·<see cref="Beam"/> — 회전 직육면체(거너 Q 관통 레이저 미리보기 = 판정 도형의 넓은 질의 상자, <c>GunnerChargeLaserSkill.FireVolume</c>).</item>
/// <item><see cref="CircleColumn"/> — 발 높이 원기둥. 겹침은 외접 상자로 하고 콜라이더 AABB 가 원에 걸리는지 <see cref="Accepts"/> 로 거른다
///   (어쌔신 변신 E 원형 5타, <see cref="AssassinCircleStrikeRules.OverlapsCircle"/>).</item>
/// </list>
/// <see cref="Mask"/> 는 그 스킬의 피격 레이어(데이터 hittableLayers) 그대로다. 미리보기는 여기에 적 마스크를 교집합한다.
/// </summary>
public readonly struct SkillHitVolume
{
    SkillHitVolume(Vector3 center, Vector3 halfExtents, Quaternion rotation, bool isCircle, Vector3 circleCenter, float radius, int mask)
    {
        Center = center;
        HalfExtents = halfExtents;
        Rotation = rotation;
        IsCircle = isCircle;
        CircleCenter = circleCenter;
        Radius = radius;
        Mask = mask;
    }

    /// <summary>겹침 상자 중심(월드).</summary>
    public Vector3 Center { get; }
    public Vector3 HalfExtents { get; }
    public Quaternion Rotation { get; }
    /// <summary>원기둥이면 상자 겹침 뒤 원 필터(<see cref="Accepts"/>)가 붙는다.</summary>
    public bool IsCircle { get; }
    /// <summary>원기둥의 원 중심(발 높이). 상자 모양이면 의미 없음.</summary>
    public Vector3 CircleCenter { get; }
    public float Radius { get; }
    public int Mask { get; }

    /// <summary>
    /// 굵은 직선 상자 — <paramref name="origin"/> 에서 <paramref name="direction"/> 으로 <paramref name="length"/> 만큼,
    /// 폭 <paramref name="width"/>·높이 <paramref name="height"/>. 회전은 <c>LookRotation(direction)</c>(방향 정규화는 호출자 몫).
    /// </summary>
    public static SkillHitVolume Beam(Vector3 origin, Vector3 direction, float length, float width, float height, int mask)
    {
        Vector3 center = origin + direction * (length * 0.5f);
        var halfExtents = new Vector3(width * 0.5f, height * 0.5f, length * 0.5f);
        return new SkillHitVolume(center, halfExtents, Quaternion.LookRotation(direction), false, Vector3.zero, 0f, mask);
    }

    /// <summary>회전 상자 그대로(판정 도형의 넓은 질의 상자 — <see cref="AttackShape.GetBroadBox"/>).</summary>
    public static SkillHitVolume Box(Vector3 center, Vector3 halfExtents, Quaternion rotation, int mask) =>
        new SkillHitVolume(center, halfExtents, rotation, false, Vector3.zero, 0f, mask);

    /// <summary>발 높이 <paramref name="center"/> 에서 위로 <paramref name="height"/> 인 반경 <paramref name="radius"/> 원기둥.</summary>
    public static SkillHitVolume CircleColumn(Vector3 center, float radius, float height, int mask)
    {
        Vector3 boxCenter = center + Vector3.up * (height * 0.5f);
        var halfExtents = new Vector3(radius, height * 0.5f, radius);
        return new SkillHitVolume(boxCenter, halfExtents, Quaternion.identity, true, center, radius, mask);
    }

    /// <summary>NonAlloc 상자 겹침 1회. 마스크는 호출자가 고른다(서버 = <see cref="Mask"/>, 미리보기 = 교집합).</summary>
    public int Overlap(Collider[] results, int mask, QueryTriggerInteraction triggers)
    {
        return Physics.OverlapBoxNonAlloc(Center, HalfExtents, results, Rotation, mask, triggers);
    }

    /// <summary>겹침에 걸린 콜라이더(월드 AABB)가 실제 영역 안인가. 상자 모양은 겹침 그대로 전부 안이다.</summary>
    public bool Accepts(Bounds colliderBounds)
    {
        return !IsCircle || AssassinCircleStrikeRules.OverlapsCircle(CircleCenter, Radius, colliderBounds);
    }
}

/// <summary>
/// [오너 로컬] 범위 표시(조준 지점 원)가 떠 있는 지점 지정 스킬이 "그 지점에 시전하면 실제로 맞힐 영역"을 내놓는다.
/// <see cref="PlayerSkillTargeting"/> 이 GroundPoint 조준 중 매 프레임 묻는다. 서버 판정과 같은 함수로 만들어야 한다.
/// </summary>
public interface ISkillGroundHitPreview
{
    /// <param name="casterPosition">시전자 위치(발).</param>
    /// <param name="aimPoint">시전될 지점 — 조준이 확정 때 넘길 값 그대로(고정 거리 투영 후).</param>
    bool TryGetGroundHitVolume(Vector3 casterPosition, Vector3 aimPoint, out SkillHitVolume volume);
}
