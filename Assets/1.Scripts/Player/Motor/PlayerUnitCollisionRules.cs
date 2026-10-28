using UnityEngine;

/// <summary>
/// 플레이어↔유닛(몬스터·다른 플레이어) 충돌 규칙의 순수 계산. 물리 쿼리·씬 상태에 의존하지 않는다. (할 일 11)
///
/// - 장애물 마스크: 생존 플레이어는 Enemy·Player 에 막히고, Soul 은 Player 를 통과한다.
///   유닛 통과 이동(암살자 Q·거너 우클릭 후폭풍)은 Enemy·Player 를 모두 빼고 벽·지형에만 막힌다.
/// - 겹침 밀어내기: 관통 벡터를 수평으로 모아 프레임당 상한(속도 × dt)까지만 옮긴다 — 순간이동 대신 부드럽게 빠진다.
/// - 일반 대시: 이번 틱 이동이 깎였고 남은 경로를 먼저 막은 것이 유닛이면 대시를 끝낸다(벽은 종전대로 상태 유지).
/// </summary>
public static class PlayerUnitCollisionRules
{
    /// <summary>이보다 짧은 밀어내기는 버린다(접촉 경계의 수치 떨림 방지).</summary>
    public const float MinSeparationDistance = 0.001f;

    /// <summary>대시 이동이 이만큼(m) 이상 깎였을 때만 "막혔다" 로 본다.</summary>
    public const float DashBlockedTolerance = 0.0005f;

    public static int ResolveObstacleMask(
        int baseMask,
        int playerBit,
        int soulBit,
        int enemyBit,
        bool isSoul,
        bool blockOtherPlayers,
        bool passThroughUnits)
    {
        int mask = baseMask & ~playerBit & ~soulBit;
        if (!isSoul && blockOtherPlayers)
            mask |= playerBit;
        if (passThroughUnits)
            mask &= ~(enemyBit | playerBit);
        return mask;
    }

    /// <summary>ComputePenetration 결과 하나(방향·거리)를 수평 성분만 누적한다.</summary>
    public static Vector3 AccumulatePenetration(Vector3 sum, Vector3 direction, float distance)
    {
        if (distance <= 0f)
            return sum;

        Vector3 planar = new Vector3(direction.x, 0f, direction.z);
        return sum + planar * distance;
    }

    /// <summary>
    /// 정적 지형 ComputePenetration 결과 하나를 수평 성분만 누적한다. (할 일 136)
    /// 해소 방향이 걸을 수 있는 면 법선(위쪽)이면 접지 겹침이라 버린다 — 경사에서 옆으로 미끄러지지 않게.
    /// </summary>
    public static Vector3 AccumulateStaticPenetration(Vector3 sum, Vector3 direction, float distance, float maxWalkableAngle)
    {
        if (distance <= 0f || direction.sqrMagnitude <= 1e-8f)
            return sum;
        if (Vector3.Angle(direction, Vector3.up) <= maxWalkableAngle)
            return sum;

        return AccumulatePenetration(sum, direction, distance);
    }

    /// <summary>
    /// 유닛 밀어내기가 벽에 정면으로 막혔을 때 벽을 따라 미끄러질 수평 변위. (할 일 136)
    /// 원래 변위의 접선 성분 부호를 따르고, 정면(접선 0)이면 법선 기준 고정 방향(up × n)을 골라 서버·오너가 같은 답을 낸다.
    /// 길이는 원래 변위 길이 그대로. 법선이 수평 성분이 없거나 변위가 0 이면 0.
    /// </summary>
    public static Vector3 ComputeWallSlideStep(Vector3 step, Vector3 wallNormal)
    {
        Vector3 planarStep = new Vector3(step.x, 0f, step.z);
        Vector3 planarNormal = new Vector3(wallNormal.x, 0f, wallNormal.z);
        float length = planarStep.magnitude;
        if (length < MinSeparationDistance || planarNormal.sqrMagnitude <= 1e-8f)
            return Vector3.zero;

        Vector3 tangent = Vector3.Cross(Vector3.up, planarNormal.normalized);
        float side = Vector3.Dot(planarStep, tangent);
        if (side < -1e-4f)
            tangent = -tangent;
        return tangent * length;
    }

    /// <summary>
    /// 구속 배치 스윕 결과로 시전자 중심에서 놓을 거리를 정한다(할 일 136). 막혔으면 벽 앞 skin 만큼 띄운 지점, 아니면 요청 거리 그대로.
    /// </summary>
    public static float ClampPlacementDistance(float requestedDistance, bool blocked, float hitDistance, float skin)
    {
        float requested = Mathf.Max(0f, requestedDistance);
        if (!blocked)
            return requested;

        return Mathf.Clamp(hitDistance - Mathf.Max(0f, skin), 0f, requested);
    }

    /// <summary>누적 관통 벡터를 이번 틱에 옮길 수평 변위로 바꾼다 — 길이는 maxSpeed × deltaTime 을 넘지 않는다.</summary>
    public static Vector3 ComputeSeparationStep(Vector3 penetrationSum, float maxSpeed, float deltaTime)
    {
        Vector3 planar = new Vector3(penetrationSum.x, 0f, penetrationSum.z);
        float distance = planar.magnitude;
        float maxStep = Mathf.Max(0f, maxSpeed) * Mathf.Max(0f, deltaTime);
        if (distance < MinSeparationDistance || maxStep <= 0f)
            return Vector3.zero;

        return distance <= maxStep ? planar : planar * (maxStep / distance);
    }

    /// <param name="requestedPlanar">이번 틱 요청 수평 이동량(m).</param>
    /// <param name="appliedPlanar">스윕 후 실제 적용된 수평 이동량(m).</param>
    /// <param name="unitBlocksRemaining">남은 경로를 가장 먼저 막는 것이 유닛(Enemy·Player 몸)인가.</param>
    public static bool ShouldEndDashOnUnitBlock(float requestedPlanar, float appliedPlanar, bool unitBlocksRemaining)
    {
        if (!unitBlocksRemaining || requestedPlanar <= DashBlockedTolerance)
            return false;

        return appliedPlanar < requestedPlanar - DashBlockedTolerance;
    }
}
