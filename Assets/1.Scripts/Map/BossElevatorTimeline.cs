using System;
using EuniTween;
using UnityEngine;

/// <summary>보스 엘리베이터 연출 단계(시간 순서). <see cref="BossElevatorTimeline.PhaseAt"/> 가 경과 시간으로 고른다.</summary>
public enum BossElevatorPhase
{
    FrontFadeOut,   // t=0 — 카운트다운 끝, 화면이 검게 페이드 아웃. 엘리베이터는 위에서 대기
    FrontBlackout,  // 완전 암전 — 서버가 참가자를 문 앞(FrontPoints)으로 옮긴다
    FrontFadeIn,    // 문 앞에서 화면이 밝아진다
    Focus,          // 카메라가 엘리베이터에 포커스 + 입력 잠금, 하강 대기
    Descending,     // 위 → 착지 (OutQuint)
    DoorOpening,    // 문 Open 클립
    WalkingIn,      // 문 열린 채 — 참가자가 슬롯 순서(P1→P2→P3)대로 칸 안으로 걸어 들어간다
    DoorClosing,    // 문 Close 클립
    Ascending,      // 착지 → 위로 계속 (속도를 InCirc 로 올린 뒤 유지, 끝 높이 없음). 참가자는 숨김
    FadingOut,      // 상승은 계속, 화면이 검게 페이드 아웃
    Blackout,       // 완전 암전 — 서버가 보스룸 이동, 리그는 대기 자리로 되돌린다
}

/// <summary>엘리베이터 문 포즈 — <see cref="BossElevatorTimeline.DoorPoseAt"/> 결과.</summary>
public enum BossElevatorDoorState
{
    Closed,
    Opening,
    Open,
    Closing,
}

/// <summary>
/// 보스 엘리베이터 연출 수치. 문 열림/닫힘 시간이 0 이하면 문 클립 길이를, 높이가 0 이하면 칸 대기 높이를 쓴다(<see cref="BossElevatorRig"/> 가 채운다).
/// </summary>
[Serializable]
public struct BossElevatorTimingSettings
{
    [DataTableIgnore]
    [Tooltip("착지 위치 기준 하강 시작 높이(m). 0 이하 = 자동(프리팹에 놓인 칸 대기 높이 − 착지 높이)")]
    public float height;

    [Tooltip("카운트다운 끝 → 화면이 검게 되는 시간(초). 끝나는 순간 참가자가 문 앞으로 옮겨진다")]
    [Min(0f)] public float frontFadeOutDuration;
    [Tooltip("문 앞으로 옮긴 뒤 완전 암전을 유지하는 시간(초) — 순간이동 보간을 가린다")]
    [Min(0f)] public float frontBlackHold;
    [Tooltip("문 앞에서 화면이 밝아지는 시간(초). 끝나는 순간 카메라가 엘리베이터로 가고 입력이 잠긴다")]
    [Min(0f)] public float frontFadeInDuration;

    [Tooltip("카메라 포커스(입력 잠금) 후 하강 시작까지(초)")]
    [Min(0f)] public float waitBeforeDescend;
    [Tooltip("하강 시간(초) — OutQuint")]
    [Min(0f)] public float descendDuration;
    [Tooltip("문 열림 시간(초). 0 이하 = Open 클립 길이")]
    public float doorOpenDuration;

    [Tooltip("문이 다 열린 뒤 첫 사람(P1)이 걷기 시작할 때까지(초)")]
    [Min(0f)] public float walkStartDelay;
    [Tooltip("다음 사람이 걷기 시작하는 간격(초) — P1 → P2 → P3")]
    [Min(0f)] public float walkInterval;
    [Tooltip("걷는 속도(m/s). 한 사람의 보행 시간 = 가장 먼 문 앞 지점 → 칸 안 지점 거리 ÷ 이 값")]
    [Min(0.1f)] public float walkSpeed;
    [Tooltip("마지막 사람이 칸 안에 도착한 뒤 문 닫힘 시작까지(초)")]
    [Min(0f)] public float closeDelayAfterWalk;
    [Tooltip("문 닫힘 시간(초). 0 이하 = Close 클립 길이")]
    public float doorCloseDuration;

    [Tooltip("상승 가속 시간(초) — 속도를 InCirc 로 0 → 상승 속도까지 올린다. 0 = 처음부터 상승 속도")]
    [Min(0f)] public float ascendDuration;
    [Tooltip("가속이 끝난 뒤 유지하는 상승 속도(m/s) — 끝 높이 없이 계속 올라간다")]
    [Min(0f)] public float ascendSpeed;
    [Tooltip("상승 시작 후 화면 페이드 아웃 시작까지(초)")]
    [Min(0f)] public float fadeOutDelay;
    [Tooltip("검은 화면 페이드 아웃 시간(초) — 끝나는 순간이 Blackout")]
    [Min(0f)] public float fadeOutDuration;

    public static BossElevatorTimingSettings Default => new BossElevatorTimingSettings
    {
        height = 0f,
        frontFadeOutDuration = 0.4f,
        frontBlackHold = 0.2f,
        frontFadeInDuration = 0.5f,
        waitBeforeDescend = 0.5f,
        descendDuration = 2.1f,
        doorOpenDuration = 0f,
        walkStartDelay = 0.2f,
        walkInterval = 0.6f,
        walkSpeed = 3f,
        closeDelayAfterWalk = 0.5f,
        doorCloseDuration = 0f,
        ascendDuration = 2.6f,
        ascendSpeed = 14f,
        fadeOutDelay = 1f,
        fadeOutDuration = 1.6f,
    };

    /// <summary>
    /// 하강 시작 높이를 확정한 사본. <see cref="height"/> 가 0 이하면 <paramref name="idleAboveLanded"/>(칸 대기 높이 − 착지 높이)를 쓴다 —
    /// 하강이 칸이 놓인 자리에서 시작해 재생 시작·<see cref="BossElevatorRig.ResetToIdle"/> 에서 순간이동하지 않는다.
    /// </summary>
    public BossElevatorTimingSettings WithResolvedHeight(float idleAboveLanded)
    {
        BossElevatorTimingSettings s = this;
        if (s.height <= 0f) s.height = Mathf.Max(0f, idleAboveLanded);
        return s;
    }
}

/// <summary>
/// 보스 엘리베이터 연출의 순수 시간표 — 단계 시각, 경과 시간 → 단계·높이·문 포즈·페이드 알파·슬롯별 보행. 네트워크·씬을 모른다.
/// 경과 시간은 시작 시각(카운트다운 끝) 기준이라 늦게 합류한 피어도 같은 값을 얻는다(음수 = 시작 전, 첫 단계로 취급).
/// 높이는 착지 위치 기준 오프셋: 시작 전·하강 전 = <see cref="Height"/>, 문·보행 단계 = 0, 상승부터는 상한 없이 커진다
/// (속도 = <see cref="AscendSpeed"/> × InCirc(가속 진행도), 가속 뒤 등속 — <see cref="AscendSpeedAt"/>).
/// 화면 페이드는 두 번이다: 문 앞 이동(페이드 아웃 → 암전 → 페이드 인, 끊김 없이 한 번)과 상승 뒤 Blackout.
/// 마지막 단계 <see cref="BossElevatorPhase.Blackout"/> 은 끝이 없다 — 그 뒤 처리는 호출자가 한다.
/// </summary>
public sealed class BossElevatorTimeline
{
    // ∫₀¹ InCirc(u) du = 1 - π/4 — 가속 구간 이동 거리 = 속도 × 가속 시간 × 이 값.
    const float InCircArea = 1f - Mathf.PI / 4f;

    public float Height { get; }
    public float FrontFadeOutDuration { get; }
    public float FrontFadeInDuration { get; }
    public float DescendDuration { get; }
    public float DoorOpenDuration { get; }
    public float DoorCloseDuration { get; }
    public float AscendAccelDuration { get; }
    public float AscendSpeed { get; }
    public float FadeOutDuration { get; }

    /// <summary>걸어 들어가는 사람 수(0 = 아무도 안 걷고 문이 열린 뒤 바로 닫힘 대기).</summary>
    public int WalkerCount { get; }
    /// <summary>다음 사람이 걷기 시작하는 간격(초).</summary>
    public float WalkInterval { get; }
    /// <summary>한 사람의 보행 시간(초) — 거리 ÷ 속도. 모든 슬롯이 같은 시간에 도착하도록 오너가 이 시간에 맞춰 걷는다.</summary>
    public float WalkDuration { get; }

    /// <summary>문 앞 순간이동 시각 = 페이드 아웃이 끝나 완전히 검은 순간.</summary>
    public float TeleportFrontAt { get; }
    public float FrontFadeInAt { get; }
    /// <summary>카메라 엘리베이터 포커스·입력 잠금 시각 = 문 앞 페이드 인이 끝난 순간.</summary>
    public float FocusAt { get; }
    public float DescendStartAt { get; }
    public float DoorOpenAt { get; }
    /// <summary>문이 다 열린 시각(<see cref="BossElevatorPhase.WalkingIn"/> 시작).</summary>
    public float WalkInAt { get; }
    public float DoorCloseAt { get; }
    public float AscendStartAt { get; }
    public float FadeOutStartAt { get; }
    public float BlackoutAt { get; }

    readonly float _firstWalkAt;

    /// <param name="clipOpenSeconds">문 Open 클립 길이 — 설정의 문 열림 시간이 0 이하일 때 쓴다.</param>
    /// <param name="clipCloseSeconds">문 Close 클립 길이 — 설정의 문 닫힘 시간이 0 이하일 때 쓴다.</param>
    /// <param name="walkerCount">걸어 들어가는 사람 수(참가자 수).</param>
    /// <param name="walkDistance">한 사람이 걷는 거리(m) — 가장 먼 문 앞 지점 → 칸 안 지점.</param>
    public BossElevatorTimeline(BossElevatorTimingSettings settings, float clipOpenSeconds, float clipCloseSeconds,
        int walkerCount = 0, float walkDistance = 0f)
    {
        Height = Mathf.Max(0f, settings.height);
        FrontFadeOutDuration = Mathf.Max(0f, settings.frontFadeOutDuration);
        FrontFadeInDuration = Mathf.Max(0f, settings.frontFadeInDuration);
        DescendDuration = Mathf.Max(0f, settings.descendDuration);
        DoorOpenDuration = Mathf.Max(0f, settings.doorOpenDuration > 0f ? settings.doorOpenDuration : clipOpenSeconds);
        DoorCloseDuration = Mathf.Max(0f, settings.doorCloseDuration > 0f ? settings.doorCloseDuration : clipCloseSeconds);
        AscendAccelDuration = Mathf.Max(0f, settings.ascendDuration);
        AscendSpeed = Mathf.Max(0f, settings.ascendSpeed);
        FadeOutDuration = Mathf.Max(0f, settings.fadeOutDuration);

        WalkerCount = Mathf.Max(0, walkerCount);
        WalkInterval = Mathf.Max(0f, settings.walkInterval);
        float speed = settings.walkSpeed;
        WalkDuration = speed > 0f ? Mathf.Max(0f, walkDistance) / speed : 0f;

        TeleportFrontAt = FrontFadeOutDuration;
        FrontFadeInAt = TeleportFrontAt + Mathf.Max(0f, settings.frontBlackHold);
        FocusAt = FrontFadeInAt + FrontFadeInDuration;
        DescendStartAt = FocusAt + Mathf.Max(0f, settings.waitBeforeDescend);
        DoorOpenAt = DescendStartAt + DescendDuration;
        WalkInAt = DoorOpenAt + DoorOpenDuration;
        _firstWalkAt = WalkInAt + Mathf.Max(0f, settings.walkStartDelay);

        // 아무도 안 걸으면 보행 시간 없이 바로 닫힘 대기.
        float walkEnd = WalkerCount > 0 ? WalkEndAt(WalkerCount - 1) : WalkInAt;
        DoorCloseAt = walkEnd + Mathf.Max(0f, settings.closeDelayAfterWalk);
        AscendStartAt = DoorCloseAt + DoorCloseDuration;
        FadeOutStartAt = AscendStartAt + Mathf.Max(0f, settings.fadeOutDelay);
        BlackoutAt = FadeOutStartAt + FadeOutDuration;
    }

    /// <summary>슬롯(0 = P1)이 걷기 시작하는 경과 시각(초).</summary>
    public float WalkStartAt(int slot) => _firstWalkAt + Mathf.Max(0, slot) * WalkInterval;

    /// <summary>슬롯이 칸 안 지점에 도착하는 경과 시각(초).</summary>
    public float WalkEndAt(int slot) => WalkStartAt(slot) + WalkDuration;

    /// <summary>슬롯의 보행 진행도(0 = 문 앞, 1 = 칸 안). 선형 — 걷는 속도가 일정하다.</summary>
    public float WalkProgressAt(int slot, double elapsed) => Progress(elapsed, WalkStartAt(slot), WalkDuration);

    /// <summary>단계가 시작되는 경과 시각(초).</summary>
    public float StartOf(BossElevatorPhase phase)
    {
        switch (phase)
        {
            case BossElevatorPhase.FrontFadeOut: return 0f;
            case BossElevatorPhase.FrontBlackout: return TeleportFrontAt;
            case BossElevatorPhase.FrontFadeIn: return FrontFadeInAt;
            case BossElevatorPhase.Focus: return FocusAt;
            case BossElevatorPhase.Descending: return DescendStartAt;
            case BossElevatorPhase.DoorOpening: return DoorOpenAt;
            case BossElevatorPhase.WalkingIn: return WalkInAt;
            case BossElevatorPhase.DoorClosing: return DoorCloseAt;
            case BossElevatorPhase.Ascending: return AscendStartAt;
            case BossElevatorPhase.FadingOut: return FadeOutStartAt;
            default: return BlackoutAt;
        }
    }

    /// <summary>경과 시간의 단계. 경계 시각은 다음 단계에 속한다(0 길이 단계는 건너뛴다).</summary>
    public BossElevatorPhase PhaseAt(double elapsed)
    {
        if (elapsed >= BlackoutAt) return BossElevatorPhase.Blackout;
        if (elapsed >= FadeOutStartAt) return BossElevatorPhase.FadingOut;
        if (elapsed >= AscendStartAt) return BossElevatorPhase.Ascending;
        if (elapsed >= DoorCloseAt) return BossElevatorPhase.DoorClosing;
        if (elapsed >= WalkInAt) return BossElevatorPhase.WalkingIn;
        if (elapsed >= DoorOpenAt) return BossElevatorPhase.DoorOpening;
        if (elapsed >= DescendStartAt) return BossElevatorPhase.Descending;
        if (elapsed >= FocusAt) return BossElevatorPhase.Focus;
        if (elapsed >= FrontFadeInAt) return BossElevatorPhase.FrontFadeIn;
        if (elapsed >= TeleportFrontAt) return BossElevatorPhase.FrontBlackout;
        return BossElevatorPhase.FrontFadeOut;
    }

    /// <summary>경과 시간의 엘리베이터 높이(착지 위치 기준 오프셋, m). 상승 시작 뒤로는 상한이 없다.</summary>
    public float HeightAt(double elapsed)
    {
        switch (PhaseAt(elapsed))
        {
            case BossElevatorPhase.FrontFadeOut:
            case BossElevatorPhase.FrontBlackout:
            case BossElevatorPhase.FrontFadeIn:
            case BossElevatorPhase.Focus:
                return Height;
            case BossElevatorPhase.Descending:
                return Height * (1f - EaseOutQuint(Progress(elapsed, DescendStartAt, DescendDuration)));
            case BossElevatorPhase.Ascending:
            case BossElevatorPhase.FadingOut:
            case BossElevatorPhase.Blackout:
                return AscentDistance((float)(elapsed - AscendStartAt));
            default:
                return 0f;
        }
    }

    /// <summary>경과 시간의 상승 속도(m/s). 상승 전 0, 가속 중 <see cref="AscendSpeed"/> × InCirc, 가속 뒤 <see cref="AscendSpeed"/>.</summary>
    public float AscendSpeedAt(double elapsed)
    {
        if (elapsed < AscendStartAt) return 0f;
        if (AscendAccelDuration <= 0f) return AscendSpeed;
        return AscendSpeed * EaseInCirc(Progress(elapsed, AscendStartAt, AscendAccelDuration));
    }

    /// <summary>
    /// 경과 시간의 검은 화면 알파(0 = 투명, 1 = 완전 암전). 페이드 구간은 선형.
    /// 문 앞 이동: 0 → 1(페이드 아웃) → 1(암전 유지) → 0(페이드 인) 이 끊김 없이 한 번. 상승 뒤: 0 → 1 → Blackout 부터 1.
    /// </summary>
    public float FadeAlphaAt(double elapsed)
    {
        if (elapsed < 0d) return 0f;
        if (elapsed >= BlackoutAt) return 1f;
        if (elapsed >= FadeOutStartAt) return Progress(elapsed, FadeOutStartAt, FadeOutDuration);
        if (elapsed >= FocusAt) return 0f;
        if (elapsed >= FrontFadeInAt) return 1f - Progress(elapsed, FrontFadeInAt, FrontFadeInDuration);
        if (elapsed >= TeleportFrontAt) return 1f;
        return Progress(elapsed, 0f, FrontFadeOutDuration);
    }

    /// <summary>경과 시간의 문 포즈와 그 클립 안 정규화 시간(0~1). Closed/Open 은 1 고정.</summary>
    public BossElevatorDoorState DoorPoseAt(double elapsed, out float normalizedTime)
    {
        switch (PhaseAt(elapsed))
        {
            case BossElevatorPhase.DoorOpening:
                normalizedTime = Progress(elapsed, DoorOpenAt, DoorOpenDuration);
                return BossElevatorDoorState.Opening;
            case BossElevatorPhase.WalkingIn:
                normalizedTime = 1f;
                return BossElevatorDoorState.Open;
            case BossElevatorPhase.DoorClosing:
                normalizedTime = Progress(elapsed, DoorCloseAt, DoorCloseDuration);
                return BossElevatorDoorState.Closing;
            default:
                normalizedTime = 1f;
                return BossElevatorDoorState.Closed;
        }
    }

    public static float EaseOutQuint(float t) => Easing.Evaluate(Ease.OutQuint, t);
    public static float EaseInCirc(float t) => Easing.Evaluate(Ease.InCirc, t);

    // 속도 v(s) = V·InCirc(s/A) 의 적분. ∫₀ᵘ (1 - √(1-x²)) dx = u - (u√(1-u²) + asin u) / 2.
    float AscentDistance(float sinceAscend)
    {
        if (sinceAscend <= 0f) return 0f;
        if (AscendAccelDuration <= 0f) return AscendSpeed * sinceAscend;
        if (sinceAscend >= AscendAccelDuration)
            return AscendSpeed * (AscendAccelDuration * InCircArea + (sinceAscend - AscendAccelDuration));

        float u = sinceAscend / AscendAccelDuration;
        float area = u - (u * Mathf.Sqrt(1f - u * u) + Mathf.Asin(u)) * 0.5f;
        return AscendSpeed * AscendAccelDuration * area;
    }

    static float Progress(double elapsed, float start, float duration)
    {
        if (duration <= 0f) return elapsed >= start ? 1f : 0f;
        return Mathf.Clamp01((float)((elapsed - start) / duration));
    }
}
