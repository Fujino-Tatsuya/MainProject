using UnityEngine;

/// <summary>
/// 팔라딘 E(수호자의 의지) 방향 규칙(PLAN-paladin-counter-e C2, Re:C 변경안 §2·§3). 순수 함수 —
/// 방어 성공 판정(<see cref="IsInsideGuardArc"/>)과 반격 부채꼴 판정(<see cref="IsInsideCounterSector"/>)을 둔다.
/// 두 판정 모두 <b>각도는 바닥 평면</b>(y 제거)에서 잰다.
/// </summary>
public static class FirstMeleeGuardRules
{
    // XZ 거리 0.01m — 이보다 가까우면 방향을 정할 수 없다(BackAttackRules·TwentyThreeBoss.IsCounterFromFront 와 같은 값).
    const float PlanarEpsilonSqr = 0.0001f;

    // 경계 포함(<=)이 float 오차로 깨지지 않게 하는 각도 여유(도). Vector3.Angle 은 정확히 60° 인 방향도 60.000004° 로 낸다.
    const float AngleToleranceDeg = 0.001f;

    /// <summary>
    /// 공격 주체가 고정 방어 방향의 전방 방어 범위 안에 있는가(바닥 평면).
    /// 방어 방향과 (공격자 − 플레이어) 사이 각도 ≤ <paramref name="guardAngle"/> × 0.5 — 경계 포함.
    ///
    /// - 방어 방향이 0(평면 기준)이면 false — 정면이 없다.
    /// - 공격자와 XZ 거리 0.01m 미만(겹침)이면 true — 정면으로 본다(TwentyThreeBoss.IsCounterFromFront 선례).
    /// </summary>
    public static bool IsInsideGuardArc(Vector3 guardForward, Vector3 playerPos, Vector3 attackerPos, float guardAngle)
    {
        guardForward.y = 0f;
        if (guardForward.sqrMagnitude < PlanarEpsilonSqr)
            return false;

        Vector3 toAttacker = attackerPos - playerPos;
        toAttacker.y = 0f;
        if (toAttacker.sqrMagnitude < PlanarEpsilonSqr)
            return true;

        float halfAngle = Mathf.Clamp(guardAngle, 0f, 360f) * 0.5f;
        return Vector3.Angle(guardForward, toAttacker) <= halfAngle + AngleToleranceDeg;
    }

    /// <summary>
    /// 반격 부채꼴 안인가 — AssassinConeAttack.IsInsideCone 과 같은 식을 공개 순수 함수로 옮긴 것
    /// (차이는 경계 각도 여유 <see cref="AngleToleranceDeg"/> 하나).
    /// 거리는 대상 콜라이더 표면(<paramref name="closestPoint"/> = <c>Collider.ClosestPoint(origin)</c>)까지,
    /// 각도는 바닥 평면에서 잰다. 표면이 원점과 겹치면(발밑을 포함하는 콜라이더) <paramref name="boundsCenter"/> 방향으로
    /// 각도를 다시 재고, 그래도 겹치면 안으로 본다. 경계 포함. <paramref name="angle"/> 은 <b>전체</b> 각도다.
    ///
    /// 🔴 이 함수는 거리도 평면으로 재지만, 런타임 브로드페이즈는 <c>Physics.OverlapSphereNonAlloc(origin, range)</c>
    /// — <b>3D 구</b>다. 그래서 높이 차가 큰 대상은 이 함수에 오기 전에 빠진다(높이 무관한 것은 각도뿐).
    /// <paramref name="forward"/> 는 평면 방향(y = 0)을 넘긴다.
    /// </summary>
    public static bool IsInsideCounterSector(
        Vector3 origin, Vector3 forward, Vector3 closestPoint, Vector3 boundsCenter, float range, float angle)
    {
        Vector3 toTarget = closestPoint - origin;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude > range * range)
            return false;

        // 발밑을 포함하는 콜라이더는 중심 방향으로 한 번 더 판정한다.
        if (toTarget.sqrMagnitude < PlanarEpsilonSqr)
        {
            toTarget = boundsCenter - origin;
            toTarget.y = 0f;
        }

        float halfAngle = Mathf.Clamp(angle, 0f, 360f) * 0.5f;
        return toTarget.sqrMagnitude < PlanarEpsilonSqr || Vector3.Angle(forward, toTarget) <= halfAngle + AngleToleranceDeg;
    }
}
