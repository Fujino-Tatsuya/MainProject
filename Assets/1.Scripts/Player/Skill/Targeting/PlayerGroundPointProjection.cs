using UnityEngine;

/// <summary>GroundPoint 고정 거리 조준과 서버 보정을 위한 순수 계산.</summary>
public static class PlayerGroundPointProjection
{
    public const float ServerDistanceTolerance = 0.25f;
    private const float DirectionEpsilonSqr = 0.0001f;

    /// <summary>
    /// 커서의 수평 방향만 사용해 시전자에서 정확히 <paramref name="range"/> 떨어진 지점을 만든다.
    /// 커서가 시전자와 거의 겹치면 마지막 유효 방향(없으면 호출자가 넘긴 forward)을 사용한다.
    /// </summary>
    public static Vector3 ProjectFixedDistance(
        Vector3 origin, Vector3 cursorPoint, float range, Vector3 fallbackDirection)
    {
        Vector3 direction = ResolveHorizontalDirection(cursorPoint - origin, fallbackDirection);
        Vector3 projected = origin + direction * Mathf.Max(0f, range);
        projected.y = cursorPoint.y;
        return projected;
    }

    /// <summary>
    /// 서버 시전자 위치에서 본 제출 지점의 수평 거리가 사거리와 허용치를 넘게 다르면
    /// 제출 방향만 유지한 채 정확한 사거리로 재투영한다.
    /// </summary>
    public static Vector3 ReprojectServerFixedDistance(
        Vector3 serverOrigin,
        Vector3 submittedPoint,
        float range,
        Vector3 fallbackDirection,
        float tolerance = ServerDistanceTolerance)
    {
        float safeRange = Mathf.Max(0f, range);
        Vector3 offset = submittedPoint - serverOrigin;
        offset.y = 0f;

        float horizontalDistance = offset.magnitude;
        if (horizontalDistance > Mathf.Sqrt(DirectionEpsilonSqr) &&
            Mathf.Abs(horizontalDistance - safeRange) <= Mathf.Max(0f, tolerance))
        {
            return submittedPoint;
        }

        return ProjectFixedDistance(serverOrigin, submittedPoint, safeRange, fallbackDirection);
    }

    /// <summary>수평 방향이 유효한지 검사한다. 조준 중 마지막 방향 갱신에 사용한다.</summary>
    public static bool TryGetHorizontalDirection(Vector3 direction, out Vector3 normalized)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < DirectionEpsilonSqr)
        {
            normalized = Vector3.zero;
            return false;
        }

        normalized = direction.normalized;
        return true;
    }

    private static Vector3 ResolveHorizontalDirection(Vector3 direction, Vector3 fallbackDirection)
    {
        if (TryGetHorizontalDirection(direction, out Vector3 normalized))
            return normalized;

        if (TryGetHorizontalDirection(fallbackDirection, out normalized))
            return normalized;

        return Vector3.forward;
    }
}
