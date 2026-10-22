using System;

/// <summary>
/// 간파 성공 쿨 감소(할 일 26)의 순수 규칙. 시전 하나의 판정 동안 성공을 모았다가 판정이 끝날 때 한 번만 감소량을 낸다.
/// 감소량 = 전체 쿨타임 × 비율(0~1). 남은 쿨에서의 차감·0 클램프는 <see cref="PlayerSkillCooldownLedger.Reduce"/> 가 한다.
/// </summary>
public sealed class InterruptSuccessCooldownModel
{
    /// <summary>공용 규칙(<see cref="PlayerGameRuleData"/>)이 없을 때 쓰는 기본 비율.</summary>
    public const float DefaultReductionRatio = 0.8f;

    private bool succeeded;
    private bool grantsReduction;
    private bool claimed;

    /// <summary>이번 시전에서 성공이 한 건이라도 있었는가(감소를 주지 않는 대상의 성공 포함 — 성공 이벤트는 이 값을 따른다).</summary>
    public bool HasSucceeded => succeeded;

    /// <summary>시전 시작 — 이전 시전의 기록을 비운다.</summary>
    public void BeginCast()
    {
        succeeded = false;
        grantsReduction = false;
        claimed = false;
    }

    /// <summary>
    /// 카운터 창 소비 성공 한 건. 같은 시전에서 여러 번 와도 감소는 한 번이다.
    /// <paramref name="grantsCooldown"/> = false 면 성공만 남기고 감소는 주지 않는다(훈련 상대 — <see cref="GrantsCooldown"/>).
    /// 같은 시전에서 감소를 주는 대상의 성공이 하나라도 있으면 감소한다.
    /// </summary>
    public void NotifySuccess(bool grantsCooldown = true)
    {
        succeeded = true;
        grantsReduction |= grantsCooldown;
    }

    /// <summary>
    /// 이 대상의 간파 성공이 시전자 쿨 감소를 주는가 — <see cref="IInterruptSuccessCooldownTarget"/> 를 구현하지 않은 대상(23호·중간보스·허수아비)은 true.
    /// </summary>
    public static bool GrantsCooldown(object target) =>
        !(target is IInterruptSuccessCooldownTarget opt) || opt.ServerGrantsInterruptSuccessCooldown;

    /// <summary>
    /// 이번 시전의 감소량을 한 번만 가져간다. 성공이 없거나(감소를 주는 대상의 성공이 없거나) 이미 가져갔거나 감소량이 0이면 false.
    /// </summary>
    public bool TryClaimReduction(float fullCooldown, float ratio, out float seconds)
    {
        seconds = 0f;
        if (!succeeded || !grantsReduction || claimed)
            return false;

        claimed = true;
        seconds = ComputeReduction(fullCooldown, ratio);
        return seconds > 0f;
    }

    /// <summary>전체 쿨타임 × 비율. 음수 쿨은 0, 비율은 0~1 로 자른다.</summary>
    public static float ComputeReduction(float fullCooldown, float ratio)
    {
        if (float.IsNaN(fullCooldown) || float.IsNaN(ratio))
            return 0f;

        return Math.Max(0f, fullCooldown) * Math.Min(1f, Math.Max(0f, ratio));
    }
}
