using System;
using System.Collections.Generic;

/// <summary>
/// 어쌔신 Q 관통 돌진의 순수 규칙(character_assassin.md §7). Unity 의존 없이 EditMode 로 검증한다.
/// - 경로 진행: 시작 후 경과 시간 × 속도, 상한(최대 거리 또는 오너 보고 거리)에서 멈춘다.
/// - 오너 보고 거리: 0 ~ 최대 거리로 자르고 NaN·무한은 버린다.
/// </summary>
public static class AssassinDashStrikeRules
{
    /// <summary>시작 후 <paramref name="elapsed"/> 초에 서버가 재구성한 경로의 진행 거리.</summary>
    public static float TraveledAt(float elapsed, float speed, float cap)
    {
        if (elapsed <= 0f || speed <= 0f || cap <= 0f)
            return 0f;

        return Math.Min(elapsed * speed, cap);
    }

    /// <summary>
    /// 오너가 보고한 실제 이동 거리(벽 조기 종료)를 검증한다. 상한은 최대 거리, 하한은 0.
    /// 숫자가 아니면 거부한다 — 호출자는 기존 상한을 유지한다.
    /// </summary>
    public static bool TryValidateReportedDistance(float reported, float maxDistance, out float validated)
    {
        validated = 0f;
        if (float.IsNaN(reported) || float.IsInfinity(reported))
            return false;

        validated = Math.Max(0f, Math.Min(reported, Math.Max(0f, maxDistance)));
        return true;
    }
}

/// <summary>
/// Q 사용 1회의 적중 장부. 같은 대상은 사용당 1회만 피해(§7.1), 변신 Q 쿨 차감은 사용당 최초 유효 적중 1회(§7.3).
/// 키는 Unit 또는 (Unit 이 아닌 상자의) Hurtbox — 호출자가 정한다.
/// </summary>
public sealed class AssassinDashHitLedger
{
    private readonly HashSet<object> damagedTargets = new HashSet<object>();

    public bool CooldownRewardClaimed { get; private set; }
    public int DamagedCount => damagedTargets.Count;

    /// <summary>새 사용 시작 — 장부를 비운다.</summary>
    public void Begin()
    {
        damagedTargets.Clear();
        CooldownRewardClaimed = false;
    }

    /// <summary>이번 사용에서 처음 보는 대상이면 true 를 돌려주고 기록한다.</summary>
    public bool TryRegisterTarget(object target)
    {
        return target != null && damagedTargets.Add(target);
    }

    /// <summary>
    /// 이번 틱에 보상 대상(보스·몹·송전기)이 맞았고 아직 차감하지 않았으면 true(1회만).
    /// 상자 등 보상 제외 대상만 맞으면 소모하지 않는다.
    /// </summary>
    public bool TryClaimCooldownReward(bool landedRewardTarget)
    {
        if (!landedRewardTarget || CooldownRewardClaimed)
            return false;

        CooldownRewardClaimed = true;
        return true;
    }
}
