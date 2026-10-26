using UnityEngine;

/// <summary>
/// 거너 평타 레이저의 위·아래 기울기 규칙(할 일 93). 순수 클래스 — <see cref="GunnerBasicAttack"/> 가 쓴다.
///
/// · 수평 방향은 잠근 몸 방향 그대로다(할 일 27, <see cref="GunnerAttackFacingModel"/>). 여기서는 기울기만 붙인다.
/// · 지면점은 거너 평타 전용 마우스 레이(Default·Ground·Env, 트리거 무시)로 방향을 잠그는 순간 잡는다 — 조준 화살표의
///   Ground 전용 레이가 아니다(맵 바닥 floor_stone 계열이 Default 레이어). 레이가 아무것도 못 맞히면 기울기 0.
/// · 기울기 계산은 그 발을 요청하는 순간의 총구 기준이다(첫 발은 준비가 끝난 발사 자세 총구).
/// · 마우스 지면점과 발 높이의 차가 levelTolerance 이내면 기울기 0 — 평지는 예전과 똑같이 수평이다.
/// · 넘으면 조준점 = 지면점 + 위로 aimHeightAboveGround. 기울기 = (조준점.y − 총구.y) / 총구에서 조준점까지 수평 거리.
/// · 위·아래 각도는 <see cref="MaxPitchDegrees"/> 에서 자른다. 서버도 받은 방향을 <see cref="ClampPitch"/> 로 한 번 더 자른다.
/// </summary>
public static class GunnerLaserPitchRules
{
    /// <summary>위·아래 각도 상한(도).</summary>
    public const float MaxPitchDegrees = 60f;

    // 이보다 가까운 조준점은 기울기를 잴 수 없다고 보고 수평으로 쏜다(m).
    public const float MinHorizontalDistance = 0.01f;

    /// <summary>
    /// 지면 레이가 못 맞혔을 수도 있는 경우 — <paramref name="hasGroundPoint"/> 가 false 면 수평(평면 방향 그대로).
    /// </summary>
    public static Vector3 AimDirection(Vector3 flatDirection, Vector3 origin, float footY, bool hasGroundPoint,
                                       Vector3 groundPoint, float aimHeightAboveGround, float levelTolerance)
    {
        if (!hasGroundPoint)
            return GunnerAttackFacingModel.Flatten(flatDirection, Vector3.forward);

        return AimDirection(flatDirection, origin, footY, groundPoint, aimHeightAboveGround, levelTolerance);
    }

    /// <summary>
    /// 잠근 평면 방향 <paramref name="flatDirection"/> 에 지면 높이에 따른 기울기를 붙인 3D 방향(정규화).
    /// </summary>
    /// <param name="origin">총구 출발점(판정선 시작)</param>
    /// <param name="footY">플레이어 발 높이</param>
    /// <param name="groundPoint">마우스가 가리킨 지면점</param>
    public static Vector3 AimDirection(Vector3 flatDirection, Vector3 origin, float footY, Vector3 groundPoint,
                                       float aimHeightAboveGround, float levelTolerance)
    {
        Vector3 flat = GunnerAttackFacingModel.Flatten(flatDirection, Vector3.forward);
        if (Mathf.Abs(groundPoint.y - footY) <= Mathf.Max(0f, levelTolerance))
            return flat;

        Vector3 toTarget = groundPoint - origin;
        toTarget.y = 0f;
        float horizontal = toTarget.magnitude;
        if (horizontal < MinHorizontalDistance)
            return flat;

        float rise = groundPoint.y + aimHeightAboveGround - origin.y;
        return ClampPitch(flat + Vector3.up * (rise / horizontal), flat);
    }

    /// <summary>
    /// 정규화하고 위·아래 각도를 <see cref="MaxPitchDegrees"/> 로 자른다.
    /// 0 벡터면 <paramref name="fallback"/>(평면으로 눌러) 정면. 수평 성분이 없으면(바로 위·아래) 그 정면으로 상한 각도.
    /// </summary>
    public static Vector3 ClampPitch(Vector3 direction, Vector3 fallback)
    {
        Vector3 flatFallback = GunnerAttackFacingModel.Flatten(fallback, Vector3.forward);
        if (direction.sqrMagnitude < 0.000001f)
            return flatFallback;

        direction.Normalize();
        Vector3 flat = new Vector3(direction.x, 0f, direction.z);
        float flatLength = flat.magnitude;
        float pitch = Mathf.Atan2(direction.y, flatLength) * Mathf.Rad2Deg;
        if (Mathf.Abs(pitch) <= MaxPitchDegrees)
            return direction;

        Vector3 horizontal = flatLength >= 0.0001f ? flat / flatLength : flatFallback;
        float clamped = Mathf.Sign(pitch) * MaxPitchDegrees * Mathf.Deg2Rad;
        return horizontal * Mathf.Cos(clamped) + Vector3.up * Mathf.Sin(clamped);
    }
}
