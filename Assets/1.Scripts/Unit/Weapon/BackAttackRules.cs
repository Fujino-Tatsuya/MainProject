using UnityEngine;

/// <summary>
/// 백어택 판정·적용 규칙(PLAN-assassin A11, character_assassin.md §4.1). 순수 함수 — MonsterBase 피해 진입점과
/// 공격자(적중 연출용 사전 판정)가 같은 식을 쓴다.
///
/// - 배율 1 이하 = 백어택 없음(다른 모든 공격의 기본값).
/// - 강제(어쌔신 변신 중) = 위치 무관 적용. 보스 후방 판정 = 보스(BossDataSO) 에만.
/// - 후방 = 대상 정면 반대 중심 ±<c>backHalfAngle</c>(BossDataSO.backAttackAngle — BossDirectionIndicator 와 같은 값).
/// </summary>
public static class BackAttackRules
{
    /// <summary>이 타격에 백어택 배율을 적용하는가.</summary>
    public static bool Applies(float multiplier, bool forceBackAttack, bool isBoss, bool isBehind) =>
        multiplier > 1f && (forceBackAttack || (isBoss && isBehind));

    /// <summary>
    /// 공격자가 대상 후방에 있는가(수평). 대상 정면과 (공격자 − 대상) 사이 각도 ≥ 180 − 반각.
    /// 완전히 겹치면 후방이 아니다(간파 정면 판정이 겹침을 정면으로 보는 것과 같은 쪽).
    /// </summary>
    public static bool IsBehind(Vector3 targetForward, Vector3 targetPosition, Vector3 attackerPosition, float backHalfAngle)
    {
        Vector3 to = attackerPosition - targetPosition;
        to.y = 0f;
        targetForward.y = 0f;
        if (to.sqrMagnitude < 0.0001f || targetForward.sqrMagnitude < 0.0001f)
            return false;

        float limit = 180f - Mathf.Clamp(backHalfAngle, 0f, 180f);
        return Vector3.Angle(targetForward, to) >= limit;
    }

    /// <summary>배율을 곱한 피해(반올림, 0 이상, int 상한).</summary>
    public static int ApplyMultiplier(int damage, float multiplier)
    {
        double scaled = System.Math.Round((double)damage * multiplier, System.MidpointRounding.AwayFromZero);
        return scaled >= int.MaxValue ? int.MaxValue : Mathf.Max(0, (int)scaled);
    }

    /// <summary>
    /// 각도를 잴 공격자 위치 — 공격자 <b>루트</b> 우선(TwentyThreeBoss.IsCounterFromFront 와 같은 이유:
    /// 무기·히트박스 자식은 이미 대상 쪽으로 뻗어 있어 각도를 편향시킨다). 루트가 없으면 sourcePosition.
    /// </summary>
    public static Vector3 ResolveAttackerPosition(AttackHitContext hitContext) =>
        hitContext.sourceTransform != null ? hitContext.sourceTransform.root.position : hitContext.sourcePosition;
}
