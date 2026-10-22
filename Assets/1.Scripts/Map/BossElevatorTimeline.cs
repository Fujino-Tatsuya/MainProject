using System;
using System.Collections.Generic;
using EuniTween;
using UnityEngine;

/// <summary>
/// 보스 엘리베이터 연출 단계(시간 순서) — 칸·문 기준. <see cref="BossElevatorTimeline.PhaseAt"/> 가 경과 시간으로 고른다.
/// 문 앞 이동의 암전 유지·페이드 인은 하강과 겹치므로 단계가 아니다 — <see cref="BossElevatorTimeline.FadeAlphaAt"/>·
/// <see cref="BossElevatorTimeline.FrontFadeInAt"/>·<see cref="BossElevatorTimeline.FrontFadeInEndAt"/> 로 따로 본다.
/// </summary>
public enum BossElevatorPhase
{
    FrontFadeOut,   // t=0 — 카운트다운 끝, 서버가 입력 잠금. 화면이 검게 페이드 아웃. 엘리베이터는 위에서 대기
    WaitDescend,    // 완전 암전 — 서버가 참가자를 문 앞(FrontPoints)으로 옮기고 카메라가 칸으로 넘어간다. 하강 대기(기본 0초)
    Descending,     // 위 → 착지 (descendEase, 기본 OutQuint). 암전 중에 시작 — 화면이 밝아질 때 이미 내려오는 중
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
/// 보스 엘리베이터 연출 수치 — 인스펙터에서 단계 순서대로 묶는다(t = 0 은 카운트다운 끝).
/// 문 열림/닫힘 시간이 0 이하면 문 클립 길이를, 높이가 0 이하면 칸 대기 높이를 쓴다(<see cref="BossElevatorRig"/> 가 채운다).
/// 보스룸 도착 뒤 페이드 인·페이드 색·도착 ACK 타임아웃은 리그 없는 경로와 함께 쓰는 값이라 <see cref="BossTeleportManager"/> 인스펙터에 있다.
/// </summary>
[Serializable]
public struct BossElevatorTimingSettings
{
    [Header("암전 · 문 앞 이동 (t=0 = 카운트다운 끝)")]
    [Tooltip("t=0(카운트다운 끝) → 화면이 완전히 검게 되는 시간(초). 끝나는 순간 = 문 앞 이동: 참가자가 문 앞으로 옮겨지고 카메라가 엘리베이터로 간다")]
    [Min(0f)] public float frontFadeOutDuration;
    [Tooltip("문 앞 이동 뒤 완전 암전을 유지하는 시간(초) — 순간이동 보간을 가린다. 끝나는 순간 화면이 밝아지기 시작")]
    [Min(0f)] public float frontBlackHold;
    [Tooltip("암전 유지가 끝난 뒤 화면이 밝아지는 시간(초). 끝나는 순간 서버가 보행을 지시한다(실제 걷기 시작은 보행 묶음 기준)")]
    [Min(0f)] public float frontFadeInDuration;

    [Header("하강")]
    [DataTableIgnore]
    [Tooltip("착지 위치 기준 하강 시작 높이(m). 0 이하 = 자동(프리팹에 놓인 칸 대기 높이 − 착지 높이)")]
    public float height;
    [Tooltip("문 앞 이동(완전 암전·카메라 전환) → 하강 시작(초). 0 = 그 순간 바로. 암전 유지보다 길면 화면이 밝아질 때 칸이 아직 멈춰 있다")]
    [Min(0f)] public float descendDelay;
    [Tooltip("하강 시작 → 착지(초). 평균 하강 속도 = 높이 ÷ 이 값. 착지 순간 문이 열리기 시작한다")]
    [Min(0f)] public float descendDuration;
    [Tooltip("하강 이징(진행도 → 내려온 비율). 기본 OutQuint = 처음 빠르고 착지 직전 느리게. Back·Elastic·Bounce 는 착지 높이 아래로 넘친다")]
    public Ease descendEase;

    [Header("문 열림")]
    [Tooltip("착지 → 문이 다 열림(초). 0 이하 = Open 클립 길이")]
    public float doorOpenDuration;

    [Header("보행 (P1 → P2 → P3)")]
    [Tooltip("문이 다 열린 순간 → 첫 사람(P1)이 걷기 시작(초)")]
    [Min(0f)] public float walkStartDelay;
    [Tooltip("앞사람이 걷기 시작한 순간 → 다음 사람이 걷기 시작(초)")]
    [Min(0f)] public float walkInterval;
    [Tooltip("걷는 속도(m/s). 한 사람의 보행 시간 = 가장 먼 문 앞 지점 → 칸 안 지점 거리 ÷ 이 값(전원 같은 시간). 칸 안 지점 간격은 리그의 _insideSpread")]
    [Min(0.1f)] public float walkSpeed;

    [Header("문 닫힘")]
    [Tooltip("마지막 사람이 칸 안에 도착한 순간 → 문 닫힘 시작(초)")]
    [Min(0f)] public float closeDelayAfterWalk;
    [Tooltip("문 닫힘 시작 → 다 닫힘(초). 0 이하 = Close 클립 길이. 다 닫힌 순간 참가자를 숨기고 상승을 시작한다")]
    public float doorCloseDuration;

    [Header("상승")]
    [Tooltip("상승 가속 시간(초) — 문이 다 닫힌 순간부터 속도를 InCirc 로 0 → 상승 속도까지 올린다. 0 = 처음부터 상승 속도")]
    [Min(0f)] public float ascendDuration;
    [Tooltip("가속이 끝난 뒤 유지하는 상승 속도(m/s) — 끝 높이 없이 계속 올라간다")]
    [Min(0f)] public float ascendSpeed;

    [Header("페이드 아웃 → 보스룸 이동")]
    [Tooltip("상승 시작(문이 다 닫힌 순간) → 화면 페이드 아웃 시작(초)")]
    [Min(0f)] public float fadeOutDelay;
    [Tooltip("검은 화면 페이드 아웃 시간(초) — 끝나는 순간 완전 암전(Blackout): 서버가 보스룸으로 옮기고 리그는 대기 자리로. 그 뒤 페이드 인은 BossTeleportManager 의 fadeInSeconds")]
    [Min(0f)] public float fadeOutDuration;

    public static BossElevatorTimingSettings Default => new BossElevatorTimingSettings
    {
        frontFadeOutDuration = 0.4f,
        frontBlackHold = 0.2f,
        frontFadeInDuration = 0.5f,
        height = 0f,
        descendDelay = 0f,
        descendDuration = 2.5f,
        descendEase = Ease.OutQuint,
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
    public Ease DescendEase { get; }
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
    /// <summary>참가자 카메라가 칸으로 넘어가는 시각 = 문 앞 순간이동과 같은 암전 순간 — 페이드 인 때 이미 칸을 보고 있다.</summary>
    public float CameraFocusAt => TeleportFrontAt;

    /// <summary>
    /// 경과 시간에 참가자 카메라가 칸을 따라가는가 — <see cref="CameraFocusAt"/> 부터 끝없이 true(하강·문 열림·보행·상승 내내 같은 대상).
    /// 그 전은 플레이어. 끝(보스룸 도착)은 시간표 밖이라 호출자가 플레이어로 돌린다.
    /// </summary>
    public bool CameraFollowsCabAt(double elapsed) => elapsed >= CameraFocusAt;
    /// <summary>문 앞 암전 유지가 끝나 화면이 밝아지기 시작하는 시각.</summary>
    public float FrontFadeInAt { get; }
    /// <summary>문 앞 페이드 인이 끝난 시각 — 서버가 보행을 지시한다(칸은 이미 하강 중).</summary>
    public float FrontFadeInEndAt { get; }
    /// <summary>하강 시작 = 문 앞 이동 + <see cref="BossElevatorTimingSettings.descendDelay"/> — 페이드 인과 무관하게 암전 기준.</summary>
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
        DescendEase = settings.descendEase;
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
        FrontFadeInEndAt = FrontFadeInAt + FrontFadeInDuration;
        DescendStartAt = TeleportFrontAt + Mathf.Max(0f, settings.descendDelay);
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

    /// <summary>도착 시각에 칸 안 지점에서 이만큼(수평 m) 안이면 걸어서 도착한 것으로 본다 — 더 멀면 <see cref="WalkNeedsSnap"/>.</summary>
    public const float WalkArriveTolerance = 0.25f;

    /// <summary><paramref name="start"/> → <paramref name="path"/> 의 점들을 차례로 잇는 꺾은선의 수평 길이(m). 점이 없으면 0.</summary>
    public static float PathLength(Vector3 start, IReadOnlyList<Vector3> path)
    {
        float length = 0f;
        Vector3 from = start;
        for (int i = 0; i < path.Count; i++)
        {
            length += Horizontal(path[i] - from).magnitude;
            from = path[i];
        }
        return length;
    }

    /// <summary>
    /// 오너 보행 한 틱의 수평 변위(m, y = 0) — 남은 경로 길이 ÷ 남은 시간 × <paramref name="deltaTime"/> 만큼 경로를 따라간다. 남은 시간이 한 틱 이하면 끝점까지.
    /// 막혀서 뒤처지면 다음 틱 속도가 올라 따라잡는다. 높이는 지면(Motor 의 경사 투영·계단 오르기·중력)에 맡긴다.
    /// 꺾이는 점을 지나면 <paramref name="index"/> 가 다음 점으로 넘어간다(한 틱 안에서 꺾이면 그 틱 변위는 모서리를 살짝 자른다).
    /// </summary>
    /// <param name="position">지금 발 위치.</param>
    /// <param name="path">경유 지점들 → 칸 안 지점(마지막). 수직 성분은 버린다.</param>
    /// <param name="index">지금 향하는 점 — 호출자가 틱마다 들고 다닌다(0 에서 시작).</param>
    /// <param name="timeLeft">도착 시각까지 남은 시간(초).</param>
    public static Vector3 WalkStepAlongPath(Vector3 position, IReadOnlyList<Vector3> path, ref int index, double timeLeft, float deltaTime)
    {
        if (path.Count == 0 || deltaTime <= 0f) return Vector3.zero;
        index = Mathf.Clamp(index, 0, path.Count - 1);

        float remaining = Horizontal(path[index] - position).magnitude;
        for (int i = index + 1; i < path.Count; i++)
            remaining += Horizontal(path[i] - path[i - 1]).magnitude;
        float move = timeLeft <= deltaTime ? remaining : remaining * (float)(deltaTime / timeLeft);

        Vector3 cursor = position;
        while (true)
        {
            Vector3 segment = Horizontal(path[index] - cursor);
            float length = segment.magnitude;
            bool last = index == path.Count - 1;
            if (last || move < length)
            {
                if (length > 0f) cursor += segment * Mathf.Min(1f, move / length);
                break;
            }
            cursor += segment;
            move -= length;
            index++;
        }
        return Horizontal(cursor - position);
    }

    static Vector3 Horizontal(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    /// <summary>
    /// 도착 시각(<paramref name="walkEndAt"/>, 같은 시간 도메인)이 지났는데 칸 안 지점에서 <see cref="WalkArriveTolerance"/> 보다 멀리 있는가 —
    /// 콜라이더에 막혀 못 들어온 경우. 그러면 호출자가 칸 안 지점(그 자리 칸 바닥 높이)으로 옮긴다.
    /// </summary>
    public static bool WalkNeedsSnap(double now, double walkEndAt, Vector3 toTarget)
    {
        toTarget.y = 0f;
        return now >= walkEndAt && toTarget.magnitude > WalkArriveTolerance;
    }

    /// <summary>단계가 시작되는 경과 시각(초).</summary>
    public float StartOf(BossElevatorPhase phase)
    {
        switch (phase)
        {
            case BossElevatorPhase.FrontFadeOut: return 0f;
            case BossElevatorPhase.WaitDescend: return TeleportFrontAt;
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
        if (elapsed >= TeleportFrontAt) return BossElevatorPhase.WaitDescend;
        return BossElevatorPhase.FrontFadeOut;
    }

    /// <summary>경과 시간의 엘리베이터 높이(착지 위치 기준 오프셋, m). 상승 시작 뒤로는 상한이 없다.</summary>
    public float HeightAt(double elapsed)
    {
        switch (PhaseAt(elapsed))
        {
            case BossElevatorPhase.FrontFadeOut:
            case BossElevatorPhase.WaitDescend:
                return Height;
            case BossElevatorPhase.Descending:
                return Height * (1f - Easing.Evaluate(DescendEase, Progress(elapsed, DescendStartAt, DescendDuration)));
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
        if (elapsed >= FrontFadeInEndAt) return 0f;
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
