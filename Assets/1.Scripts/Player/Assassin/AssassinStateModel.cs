using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 어쌔신 상태의 복제 단위. 서버만 쓰고 전 피어가 읽는다 — HUD·VFX 는 이 값과 현재 시각으로 남은 시간을 계산한다.
/// 시간은 <see cref="AssassinState"/> 의 네트워크 시계 도메인(GunnerHeat 와 같음). (character_assassin.md §4.2·§8·§10·§12)
/// </summary>
public struct AssassinStateSnapshot : INetworkSerializable, IEquatable<AssassinStateSnapshot>
{
    public byte stacks;
    public bool enhancedReady;
    public bool transformed;
    public double transformStartTime;
    public double transformEndTime;
    // 2초 이후 R 재입력으로 받은 수동 해제 요청. 만료와 같은 "현재 공격 완료 후 종료" 대기에 들어간다.
    public bool releaseRequested;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref stacks);
        serializer.SerializeValue(ref enhancedReady);
        serializer.SerializeValue(ref transformed);
        serializer.SerializeValue(ref transformStartTime);
        serializer.SerializeValue(ref transformEndTime);
        serializer.SerializeValue(ref releaseRequested);
    }

    public bool Equals(AssassinStateSnapshot other) =>
        stacks == other.stacks &&
        enhancedReady == other.enhancedReady &&
        transformed == other.transformed &&
        transformStartTime == other.transformStartTime &&
        transformEndTime == other.transformEndTime &&
        releaseRequested == other.releaseRequested;
}

/// <summary>스택 상한·변신 지속 표·해제 제한 시간. 값은 <see cref="AssassinStateData"/> 가 준다.</summary>
public readonly struct AssassinStateRules
{
    private readonly float[] transformDurations;

    public int MaxStacks { get; }
    public float ReleaseLockSeconds { get; }

    public AssassinStateRules(int maxStacks, float[] transformDurations, float releaseLockSeconds)
    {
        MaxStacks = Mathf.Max(1, maxStacks);
        this.transformDurations = transformDurations;
        ReleaseLockSeconds = Mathf.Max(0f, releaseLockSeconds);
    }

    public static AssassinStateRules Default => new AssassinStateRules(4, new[] { 4f, 6f, 8f, 10f }, 2f);

    /// <summary>소모한 스택 수(1~)의 변신 지속시간. 표보다 많으면 마지막 값.</summary>
    public float DurationFor(int consumedStacks)
    {
        if (transformDurations == null || transformDurations.Length == 0 || consumedStacks <= 0)
            return 0f;

        int index = Mathf.Clamp(consumedStacks - 1, 0, transformDurations.Length - 1);
        return Mathf.Max(0f, transformDurations[index]);
    }
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
/// 어쌔신 스택·강화·변신 규칙 — 순수 함수(EditMode 테스트 대상). 서버 <see cref="AssassinState"/> 만 상태를 바꾼다.
/// </summary>
public static class AssassinStateModel
{
    // float 시간 누적 오차로 2.0 경계가 1.9999999 로 보이는 것을 허용한다.
    private const double TimeEpsilon = 0.00001;

    /// <summary>강타 적중 보상. 변신 중에는 얻지 못하고 상한을 넘지 않는다(§4.2).</summary>
    public static bool TryGainStack(ref AssassinStateSnapshot state, in AssassinStateRules rules)
    {
        if (state.transformed || state.stacks >= rules.MaxStacks)
            return false;

        state.stacks++;
        return true;
    }

    public static bool CanBeginTransform(in AssassinStateSnapshot state) =>
        !state.transformed && state.stacks >= 1;

    /// <summary>Parry_R 시작 — 스택 전부 소모, 지속 시작, 강화 준비는 제거(§10.1). 스택은 반환하지 않는다.</summary>
    public static bool TryBeginTransform(
        ref AssassinStateSnapshot state, double now, in AssassinStateRules rules, out bool removedEnhancement)
    {
        removedEnhancement = false;
        if (!CanBeginTransform(state))
            return false;

        int consumed = Mathf.Min(state.stacks, rules.MaxStacks);
        removedEnhancement = state.enhancedReady;

        state.stacks = 0;
        state.enhancedReady = false;
        state.transformed = true;
        state.transformStartTime = now;
        state.transformEndTime = now + rules.DurationFor(consumed);
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

    /// <summary>만료 또는 수동 해제로 종료를 기다리는 중. 이 동안 새 공격·다음 묶음을 시작하지 않는다(§10.2).</summary>
    public static bool IsEndPending(in AssassinStateSnapshot state, double now) =>
        state.transformed && (state.releaseRequested || now >= state.transformEndTime);

    /// <summary>종료 대기 중이고 현재 공격(평타 묶음·스킬·간파)이 끝났으면 지금 종료한다(§10.3).</summary>
    public static bool ShouldFinishTransform(in AssassinStateSnapshot state, double now, bool actionInProgress) =>
        IsEndPending(state, now) && !actionInProgress;

    /// <summary>일반 상태로 복귀. 호출자가 슬롯 원복과 R 쿨타임을 처리한다.</summary>
    public static bool TryFinishTransform(ref AssassinStateSnapshot state)
    {
        if (!state.transformed)
            return false;

        state.transformed = false;
        state.transformStartTime = 0.0;
        state.transformEndTime = 0.0;
        state.releaseRequested = false;
        return true;
    }

    /// <summary>
    /// 쓰러짐·사망 — 공격 완료 대기보다 우선한다. 스택 0, 강화 제거, 변신 즉시 종료(§12.2).
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
