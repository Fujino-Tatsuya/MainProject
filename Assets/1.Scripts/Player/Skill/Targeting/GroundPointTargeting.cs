using UnityEngine;

// GroundPoint 조준에서 사거리 밖 지점을 클릭했을 때의 처리. PlayerSkillData 에 스킬별로 저장한다.
public enum GroundPointOutOfRangeMode
{
    Clamp,        // 사거리 경계로 끌어당겨 바로 시전 (기존 동작)
    AutoApproach  // 클릭 지점을 그대로 저장하고 사거리에 들 때까지 걸어간 뒤 시전
}

// 지점 확정 결과 — 바로 시전할지, 걸어가서 시전할지.
public enum GroundPointConfirmAction
{
    CastNow,
    Approach
}

/// <summary>
/// 지점 지정 조준의 순수 계산(수평 거리 기준, y 무시). 오너 조준·자동 접근·서버 승인이 같은 식을 쓰도록 모았다.
/// MonoBehaviour 에 의존하지 않아 EditMode 로 검증한다(GroundPointTargetingTests).
/// </summary>
public static class GroundPointTargeting
{
    // 자동 이동 시 사거리 경계보다 살짝 안쪽에서 시전한다(서버 위치 오차로 인한 CanUse 거부 방지).
    // 서버 승인은 같은 값만큼 너그럽게 본다.
    public const float RangeBuffer = 0.3f;

    // 사거리는 수평 거리 기준. range 가 0 이하면 항상 밖이다.
    public static bool IsWithinRange(Vector3 origin, Vector3 point, float range)
    {
        if (range <= 0f)
            return false;

        Vector3 flat = point - origin;
        flat.y = 0f;
        return flat.sqrMagnitude <= range * range;
    }

    // 사거리 밖 지점을 경계로 끌어당긴다. 높이는 원래 지점 것을 쓴다.
    public static Vector3 ClampToRange(Vector3 origin, Vector3 point, float range)
    {
        Vector3 flat = point - origin;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0001f)
            return point;

        Vector3 clamped = origin + flat.normalized * range;
        clamped.y = point.y;
        return clamped;
    }

    // 자동 접근이 멈추고 시전하는 거리 — Unit 대상 접근과 같은 식(castRange - RangeBuffer).
    public static bool HasReachedApproachStop(Vector3 origin, Vector3 point, float castRange)
    {
        return IsWithinRange(origin, point, castRange - RangeBuffer);
    }

    // 서버 승인 — 오너가 사거리 안이라 보고 보낸 지점을 위치 오차만큼 너그럽게 받는다.
    public static bool IsApprovableRange(Vector3 origin, Vector3 point, float castRange)
    {
        return IsWithinRange(origin, point, castRange + RangeBuffer);
    }

    /// <summary>
    /// 클릭 지점을 어떻게 처리할지. Clamp = 항상 바로 시전(사거리 밖이면 경계 지점),
    /// AutoApproach = 사거리 안이면 그 지점에 바로 시전, 밖이면 지점을 그대로 두고 접근.
    /// </summary>
    public static GroundPointConfirmAction ResolveConfirm(
        Vector3 origin, Vector3 point, float castRange, GroundPointOutOfRangeMode mode, out Vector3 castPoint)
    {
        if (IsWithinRange(origin, point, castRange))
        {
            castPoint = point;
            return GroundPointConfirmAction.CastNow;
        }

        if (mode == GroundPointOutOfRangeMode.AutoApproach)
        {
            castPoint = point;
            return GroundPointConfirmAction.Approach;
        }

        castPoint = ClampToRange(origin, point, castRange);
        return GroundPointConfirmAction.CastNow;
    }

    // 조준 중 마커 위치 — Clamp 는 시전될 자리(경계)를, AutoApproach 는 마우스 지점을 그대로 보여준다.
    public static Vector3 PreviewMarkerPoint(
        Vector3 origin, Vector3 point, float castRange, GroundPointOutOfRangeMode mode)
    {
        if (mode == GroundPointOutOfRangeMode.AutoApproach || IsWithinRange(origin, point, castRange))
            return point;

        return ClampToRange(origin, point, castRange);
    }

    // HUD 호버 미리보기 마커 — 캐릭터 정면(facing) distance 앞. 높이는 시전자와 같다.
    public static Vector3 HoverMarkerPoint(Vector3 origin, Vector3 facing, float distance)
    {
        facing.y = 0f;
        if (facing.sqrMagnitude < 0.0001f)
            return origin;

        return origin + facing.normalized * Mathf.Max(0f, distance);
    }
}
