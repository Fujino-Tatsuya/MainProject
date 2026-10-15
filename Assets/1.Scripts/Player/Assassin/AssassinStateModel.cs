using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 어쌔신 상태의 복제 단위. 서버만 쓰고 전 피어가 읽는다 — HUD·VFX 는 이 값과 현재 시각으로 남은 시간·게이지를 계산한다.
/// 시간은 <see cref="AssassinState"/> 의 네트워크 시계 도메인(GunnerHeat 와 같음). (character_assassin.md §4.2·§8·§10·§12)
/// </summary>
public struct AssassinStateSnapshot : INetworkSerializable, IEquatable<AssassinStateSnapshot>
{
    // 분노 게이지. 일반 상태 = 현재 값. 변신 중 = 변신 시작 시 연료 — 현재 값은 시작 시각과 초당 감소량으로 계산한다
    // (매 프레임 복제하지 않는다, <see cref="AssassinStateModel.Rage"/>).
    public float rage;
    public bool enhancedReady;
    public bool transformed;
    public double transformStartTime;
    public double transformEndTime;
    // 2초 이후 R 재입력으로 받은 수동 해제 요청. 만료와 같은 "현재 공격 완료 후 종료" 대기에 들어간다.
    public bool releaseRequested;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref rage);
        serializer.SerializeValue(ref enhancedReady);
        serializer.SerializeValue(ref transformed);
        serializer.SerializeValue(ref transformStartTime);
        serializer.SerializeValue(ref transformEndTime);
        serializer.SerializeValue(ref releaseRequested);
    }

    public bool Equals(AssassinStateSnapshot other) =>
        rage == other.rage &&
        enhancedReady == other.enhancedReady &&
        transformed == other.transformed &&
        transformStartTime == other.transformStartTime &&
        transformEndTime == other.transformEndTime &&
        releaseRequested == other.releaseRequested;
}

/// <summary>분노 게이지를 채우는 공격 1회의 종류(§4.2). 일반 상태에서만 충전한다.</summary>
public enum AssassinRageSource
{
    BasicAttack,
    DashStrike,
    EnhancedStrike,
}

/// <summary>분노 게이지 상한·최소 변신량·충전량·변신 중 감소량·해제 제한 시간. 값은 <see cref="AssassinStateData"/> 가 준다.</summary>
public readonly struct AssassinStateRules
{
    // 감소량 0 이면 변신이 끝나지 않는다 — 하한을 둔다.
    private const float MinDecayPerSecond = 0.01f;

    public float MaxRage { get; }
    public float MinTransformRage { get; }
    public float BasicAttackGain { get; }
    public float DashStrikeGain { get; }
    public float EnhancedStrikeGain { get; }
    public float TransformDecayPerSecond { get; }
    public float ReleaseLockSeconds { get; }

    public AssassinStateRules(
        float maxRage, float minTransformRage,
        float basicAttackGain, float dashStrikeGain, float enhancedStrikeGain,
        float transformDecayPerSecond, float releaseLockSeconds)
    {
        MaxRage = Mathf.Max(1f, maxRage);
        MinTransformRage = Mathf.Clamp(minTransformRage, 0f, MaxRage);
        BasicAttackGain = Mathf.Max(0f, basicAttackGain);
        DashStrikeGain = Mathf.Max(0f, dashStrikeGain);
        EnhancedStrikeGain = Mathf.Max(0f, enhancedStrikeGain);
        TransformDecayPerSecond = Mathf.Max(MinDecayPerSecond, transformDecayPerSecond);
        ReleaseLockSeconds = Mathf.Max(0f, releaseLockSeconds);
    }

    public static AssassinStateRules Default => new AssassinStateRules(100f, 40f, 3f, 5f, 15f, 10f, 2f);

    /// <summary>공격 1회의 충전량.</summary>
    public float GainFor(AssassinRageSource source) => source switch
    {
        AssassinRageSource.BasicAttack => BasicAttackGain,
        AssassinRageSource.DashStrike => DashStrikeGain,
        AssassinRageSource.EnhancedStrike => EnhancedStrikeGain,
        _ => 0f,
    };

    /// <summary>게이지 <paramref name="rage"/> 를 연료로 쓴 변신 지속시간(게이지 ÷ 초당 감소량).</summary>
    public float DurationFor(float rage) => Mathf.Max(0f, rage) / TransformDecayPerSecond;
}

/// <summary>쓰러짐·사망 정리 결과 — 호출자가 쿨타임·슬롯 원복을 이어서 처리한다(§12.2).</summary>
public readonly struct AssassinDownResult
{
    public AssassinDownResult(bool hadEnhancement, bool wasTransformed)
    {
        HadEnhancement = hadEnhancement;
        WasTransformed = wasTransformed;
    }

    /// <summary>강화 준비가 제거됐다 → 일반 E 쿨타임 시작.</summary>
    public bool HadEnhancement { get; }

    /// <summary>변신이 즉시 끝났다 → 슬롯 원복 + R 쿨타임 시작.</summary>
    public bool WasTransformed { get; }
}

/// <summary>
/// 어쌔신 분노 게이지·강화·변신 규칙 — 순수 함수(EditMode 테스트 대상). 서버 <see cref="AssassinState"/> 만 상태를 바꾼다.
/// </summary>
public static class AssassinStateModel
{
    // float 시간 누적 오차로 2.0 경계가 1.9999999 로 보이는 것을 허용한다.
    private const double TimeEpsilon = 0.00001;

    // 3 씩 쌓은 39.99999 가 최소 변신량 40 에 못 미치는 것을 막는다.
    private const float RageEpsilon = 0.0001f;

    /// <summary>
    /// 지금 게이지. 일반 상태 = 저장값(유휴 감소 없음). 변신 중 = 시작 연료 − 경과 × 초당 감소량, 0 아래로 내려가지 않는다(§4.2·§10.2).
    /// </summary>
    public static float Rage(in AssassinStateSnapshot state, double now, in AssassinStateRules rules)
    {
        if (!state.transformed)
            return state.rage;

        double elapsed = Math.Max(0.0, now - state.transformStartTime);
        return (float)Math.Max(0.0, state.rage - elapsed * rules.TransformDecayPerSecond);
    }

    /// <summary>
    /// 유효 대상 적중 공격 1회의 충전(§4.2). 변신 중에는 얻지 못하고, 상한을 넘는 몫은 버린다.
    /// 공격 1회당 1번 부르는 것은 호출자 책임이다(여러 대상이어도 1번).
    /// </summary>
    public static bool TryGainRage(ref AssassinStateSnapshot state, AssassinRageSource source, in AssassinStateRules rules)
    {
        float amount = rules.GainFor(source);
        if (state.transformed || amount <= 0f || state.rage >= rules.MaxRage)
            return false;

        state.rage = Mathf.Min(rules.MaxRage, state.rage + amount);
        return true;
    }

    /// <summary>일반 상태 + 게이지 ≥ 최소 변신량(0 초과). R 쿨·행동 가능은 스킬 컨트롤러가 본다(§10.1).</summary>
    public static bool CanBeginTransform(in AssassinStateSnapshot state, in AssassinStateRules rules) =>
        !state.transformed && state.rage > 0f && state.rage >= rules.MinTransformRage - RageEpsilon;

    /// <summary>Parry_R 시작 — 보유 게이지 전부가 연료, 감소 시작, 강화 준비는 제거(§10.1).</summary>
    public static bool TryBeginTransform(
        ref AssassinStateSnapshot state, double now, in AssassinStateRules rules, out bool removedEnhancement)
    {
        removedEnhancement = false;
        if (!CanBeginTransform(state, rules))
            return false;

        float fuel = Mathf.Min(state.rage, rules.MaxRage);
        removedEnhancement = state.enhancedReady;

        state.rage = fuel;
        state.enhancedReady = false;
        state.transformed = true;
        state.transformStartTime = now;
        state.transformEndTime = now + rules.DurationFor(fuel);
        state.releaseRequested = false;
        return true;
    }

    /// <summary>일반 E — 이미 준비됐거나 변신 중이면 불가(§8.1).</summary>
    public static bool CanPrepareEnhancement(in AssassinStateSnapshot state) =>
        !state.transformed && !state.enhancedReady;

    public static bool TryPrepareEnhancement(ref AssassinStateSnapshot state)
    {
        if (!CanPrepareEnhancement(state))
            return false;

        state.enhancedReady = true;
        return true;
    }

    /// <summary>강타가 실제로 시작됐다 — 강화를 소모한다. 호출자가 E 쿨타임을 시작한다(§8.2).</summary>
    public static bool TryConsumeEnhancement(ref AssassinStateSnapshot state)
    {
        if (!state.enhancedReady)
            return false;

        state.enhancedReady = false;
        return true;
    }

    public static float Elapsed(in AssassinStateSnapshot state, double now) =>
        state.transformed ? (float)Math.Max(0.0, now - state.transformStartTime) : 0f;

    public static float Remaining(in AssassinStateSnapshot state, double now) =>
        state.transformed ? (float)Math.Max(0.0, state.transformEndTime - now) : 0f;

    /// <summary>변신 시작 후 해제 제한 시간이 지났는가. 지나기 전 입력은 무시하고 예약하지 않는다(§10.2).</summary>
    public static bool CanRequestRelease(in AssassinStateSnapshot state, double now, in AssassinStateRules rules) =>
        state.transformed && !state.releaseRequested &&
        now - state.transformStartTime >= rules.ReleaseLockSeconds - TimeEpsilon;

    public static bool TryRequestRelease(ref AssassinStateSnapshot state, double now, in AssassinStateRules rules)
    {
        if (!CanRequestRelease(state, now, rules))
            return false;

        state.releaseRequested = true;
        return true;
    }

    /// <summary>
    /// 게이지 0(시간 만료) 또는 수동 해제로 종료를 기다리는 중. 이 동안 새 공격·다음 묶음을 시작하지 않는다(§10.2).
    /// 종료 시각 = 시작 + 연료 ÷ 감소량 이라 "지금 ≥ 종료 시각" 이 곧 게이지 0 이다.
    /// </summary>
    public static bool IsEndPending(in AssassinStateSnapshot state, double now) =>
        state.transformed && (state.releaseRequested || now >= state.transformEndTime);

    /// <summary>종료 대기 중이고 현재 공격(평타 묶음·스킬·간파)이 끝났으면 지금 종료한다(§10.3).</summary>
    public static bool ShouldFinishTransform(in AssassinStateSnapshot state, double now, bool actionInProgress) =>
        IsEndPending(state, now) && !actionInProgress;

    /// <summary>
    /// 일반 상태로 복귀 — 지금 남은 게이지를 보존한다(수동 해제는 남은 양, 만료는 0. 종료 대기 중에도 감소는 계속됐다 — §10.2).
    /// 호출자가 슬롯 원복과 R 쿨타임을 처리한다.
    /// </summary>
    public static bool TryFinishTransform(ref AssassinStateSnapshot state, double now, in AssassinStateRules rules)
    {
        if (!state.transformed)
            return false;

        state.rage = Rage(state, now, rules);
        state.transformed = false;
        state.transformStartTime = 0.0;
        state.transformEndTime = 0.0;
        state.releaseRequested = false;
        return true;
    }

    /// <summary>
    /// 쓰러짐·사망 — 공격 완료 대기보다 우선한다. 게이지 0, 강화 제거, 변신 즉시 종료(§12.2).
    /// 변신·강화가 없었다면 결과 플래그가 false 라 새 쿨타임을 만들지 않는다.
    /// </summary>
    public static AssassinDownResult ResetForDown(ref AssassinStateSnapshot state)
    {
        bool hadEnhancement = state.enhancedReady;
        bool wasTransformed = state.transformed;
        state = default;
        return new AssassinDownResult(hadEnhancement, wasTransformed);
    }
}
