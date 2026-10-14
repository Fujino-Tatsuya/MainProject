using System;
using EuniTween;
using UnityEngine;

/// <summary>보스 엘리베이터 연출 단계(시간 순서). <see cref="BossElevatorTimeline.PhaseAt"/> 가 경과 시간으로 고른다.</summary>
public enum BossElevatorPhase
{
    TeleportFront,  // t=0 — 플레이어가 문 앞으로 순간이동, 엘리베이터는 위에서 대기
    Descending,     // 위 → 착지 (OutQuint)
    DoorOpening,    // 문 Open 클립
    Vanished,       // 플레이어 사라짐 — 문 열린 채 대기
    DoorClosing,    // 문 Close 클립
    Ascending,      // 착지 → 위로 계속 (속도를 InCirc 로 올린 뒤 유지, 끝 높이 없음)
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
/// 보스 엘리베이터 연출 수치. 문 열림/닫힘 시간이 0 이하면 문 클립 길이를 쓴다(<see cref="BossElevatorRig"/> 가 채운다).
/// </summary>
[Serializable]
public struct BossElevatorTimingSettings
{
    [Tooltip("착지 위치 기준 하강 시작 높이(m)")]
    [Min(0f)] public float height;
    [Tooltip("순간이동 후 하강 시작까지(초)")]
    [Min(0f)] public float waitBeforeDescend;
    [Tooltip("하강 시간(초) — OutQuint")]
    [Min(0f)] public float descendDuration;
    [Tooltip("문 열림 시간(초). 0 이하 = Open 클립 길이")]
    public float doorOpenDuration;
    [Tooltip("사라진 뒤 문 닫힘 시작까지(초)")]
    [Min(0f)] public float closeDelayAfterVanish;
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
        height = 12f,
        waitBeforeDescend = 0.5f,
        descendDuration = 1.2f,
        doorOpenDuration = 0f,
        closeDelayAfterVanish = 1.5f,
        doorCloseDuration = 0f,
        ascendDuration = 1.5f,
        ascendSpeed = 24f,
        fadeOutDelay = 0f,
        fadeOutDuration = 1.5f,
    };
}

/// <summary>
/// 보스 엘리베이터 연출의 순수 시간표 — 단계 시각, 경과 시간 → 단계·높이·문 포즈·페이드 알파. 네트워크·씬을 모른다.
/// 경과 시간은 시작 시각 기준이라 늦게 합류한 피어도 같은 값을 얻는다(음수 = 시작 전, 첫 단계로 취급).
/// 높이는 착지 위치 기준 오프셋: 시작 전·하강 전 = <see cref="Height"/>, 문 단계 = 0, 상승부터는 상한 없이 커진다
/// (속도 = <see cref="AscendSpeed"/> × InCirc(가속 진행도), 가속 뒤 등속 — <see cref="AscendSpeedAt"/>).
/// 마지막 단계 <see cref="BossElevatorPhase.Blackout"/> 은 끝이 없다 — 그 뒤 처리는 호출자가 한다.
/// </summary>
public sealed class BossElevatorTimeline
{
    // ∫₀¹ InCirc(u) du = 1 - π/4 — 가속 구간 이동 거리 = 속도 × 가속 시간 × 이 값.
    const float InCircArea = 1f - Mathf.PI / 4f;

    public float Height { get; }
    public float DescendDuration { get; }
    public float DoorOpenDuration { get; }
    public float DoorCloseDuration { get; }
    public float AscendAccelDuration { get; }
    public float AscendSpeed { get; }
    public float FadeOutDuration { get; }

    public float DescendStartAt { get; }
    public float DoorOpenAt { get; }
    public float VanishAt { get; }
    public float DoorCloseAt { get; }
    public float AscendStartAt { get; }
    public float FadeOutStartAt { get; }
    public float BlackoutAt { get; }

    /// <param name="clipOpenSeconds">문 Open 클립 길이 — 설정의 문 열림 시간이 0 이하일 때 쓴다.</param>
    /// <param name="clipCloseSeconds">문 Close 클립 길이 — 설정의 문 닫힘 시간이 0 이하일 때 쓴다.</param>
    public BossElevatorTimeline(BossElevatorTimingSettings settings, float clipOpenSeconds, float clipCloseSeconds)
    {
        Height = Mathf.Max(0f, settings.height);
        DescendDuration = Mathf.Max(0f, settings.descendDuration);
        DoorOpenDuration = Mathf.Max(0f, settings.doorOpenDuration > 0f ? settings.doorOpenDuration : clipOpenSeconds);
        DoorCloseDuration = Mathf.Max(0f, settings.doorCloseDuration > 0f ? settings.doorCloseDuration : clipCloseSeconds);
        AscendAccelDuration = Mathf.Max(0f, settings.ascendDuration);
        AscendSpeed = Mathf.Max(0f, settings.ascendSpeed);
        FadeOutDuration = Mathf.Max(0f, settings.fadeOutDuration);

        DescendStartAt = Mathf.Max(0f, settings.waitBeforeDescend);
        DoorOpenAt = DescendStartAt + DescendDuration;
        VanishAt = DoorOpenAt + DoorOpenDuration;
        DoorCloseAt = VanishAt + Mathf.Max(0f, settings.closeDelayAfterVanish);
        AscendStartAt = DoorCloseAt + DoorCloseDuration;
        FadeOutStartAt = AscendStartAt + Mathf.Max(0f, settings.fadeOutDelay);
        BlackoutAt = FadeOutStartAt + FadeOutDuration;
    }

    /// <summary>단계가 시작되는 경과 시각(초).</summary>
    public float StartOf(BossElevatorPhase phase)
    {
        switch (phase)
        {
            case BossElevatorPhase.TeleportFront: return 0f;
            case BossElevatorPhase.Descending: return DescendStartAt;
            case BossElevatorPhase.DoorOpening: return DoorOpenAt;
            case BossElevatorPhase.Vanished: return VanishAt;
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
        if (elapsed >= VanishAt) return BossElevatorPhase.Vanished;
        if (elapsed >= DoorOpenAt) return BossElevatorPhase.DoorOpening;
        if (elapsed >= DescendStartAt) return BossElevatorPhase.Descending;
        return BossElevatorPhase.TeleportFront;
    }

    /// <summary>경과 시간의 엘리베이터 높이(착지 위치 기준 오프셋, m). 상승 시작 뒤로는 상한이 없다.</summary>
    public float HeightAt(double elapsed)
    {
        switch (PhaseAt(elapsed))
        {
            case BossElevatorPhase.TeleportFront:
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

    /// <summary>경과 시간의 검은 화면 알파(0 = 투명, 1 = 완전 암전). 페이드 아웃 구간은 선형.</summary>
    public float FadeAlphaAt(double elapsed)
    {
        if (elapsed >= BlackoutAt) return 1f;
        if (elapsed < FadeOutStartAt) return 0f;
        return Progress(elapsed, FadeOutStartAt, FadeOutDuration);
    }

    /// <summary>경과 시간의 문 포즈와 그 클립 안 정규화 시간(0~1). Closed/Open 은 1 고정.</summary>
    public BossElevatorDoorState DoorPoseAt(double elapsed, out float normalizedTime)
    {
        switch (PhaseAt(elapsed))
        {
            case BossElevatorPhase.DoorOpening:
                normalizedTime = Progress(elapsed, DoorOpenAt, DoorOpenDuration);
                return BossElevatorDoorState.Opening;
            case BossElevatorPhase.Vanished:
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
        if (duration <= 0f) return 1f;
        return Mathf.Clamp01((float)((elapsed - start) / duration));
    }
}
