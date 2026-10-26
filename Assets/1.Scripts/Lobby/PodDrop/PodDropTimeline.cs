using System;
using System.Collections.Generic;
using EuniTween;
using UnityEngine;

/// <summary>
/// 로비 포드 탑승·강하 연출 단계(시간 순서, 할 일 100). t = 0 은 출격 집결 확정. <see cref="PodDropLobbyTimeline.PhaseAt"/> 가 경과 시간으로 고른다.
/// 카메라 줌은 단계와 겹쳐 흐르므로 단계가 아니다 — <see cref="PodDropLobbyTimeline.CameraDistanceAt"/> 로 따로 본다.
/// </summary>
public enum PodDropLobbyPhase
{
    LetterboxIn,    // t=0 — 서버가 입력 잠금. 위·아래 레터박스가 들어오고 UI 가 같이 숨는다. 카메라는 멀리서 줌인 시작. 같이 짧은 암전 — 가장 깊을 때 각자 걷기 시작 지점으로 순간이동
    Walk,           // 걷기 시작 지점 → 계단 → 문턱까지 일정 속도로 자동 보행(슬롯 순서대로 간격을 두고 출발) → 좌석으로 미끄러짐. 레터박스·암전이 둘 다 끝난 뒤. 가장 늦게 앉는 사람 기준
    HatchClose,     // 해치 닫힘(선택 — 길이 0 이면 건너뛴다, 기본 0)
    Descend,        // 포드가 갱도 아래로 내려간다
    FadeOut,        // 화면이 검게 페이드 아웃
    Done,           // 완전 암전 — 서버가 맵 로딩을 시작한다. 레터박스는 검은 화면 아래에서 걷힌다
}

/// <summary>
/// 맵 포드 착지 연출 단계(시간 순서, 할 일 100). t = 0 은 로딩 화면이 걷힌 순간 — 레터박스는 이미 덮인 채 시작한다.
/// </summary>
public enum PodDropMapPhase
{
    Hold,           // 로딩이 걷힌 뒤 대기. 포드는 위에 떠 있고 플레이어는 착지점에 숨김·잠금
    Fall,           // 포드 낙하
    Settle,         // 착지 순간(땅 갈라짐 이펙트·카메라 흔들림) → 디졸브 전 여운
    Dissolve,       // 포드 디졸브 — 시작 순간 플레이어가 보인다
    LetterboxOut,   // 레터박스가 걷힌다
    Released,       // 해제 — UI·조작·튜토리얼(S0) 시작. 끝이 없다
}

/// <summary>
/// 로비 단계 수치 — 인스펙터에서 단계 순서대로 묶는다(t = 0 은 출격 집결 확정). 길이는 초, 거리는 m.
/// 카메라 거리는 플레이어 카메라 FollowOffset 배율(1 = 평소 거리) — <see cref="CameraTargetSwitcher"/> 가 오프셋에 배율을 곱하는 방식과 같다.
/// </summary>
[Serializable]
public struct PodDropLobbyTimings
{
    [Header("레터박스 · 카메라")]
    [Tooltip("t=0(출격 확정) → 위·아래 레터박스가 다 들어옴(초). UI 는 레터박스와 같이 숨는다. 이것과 암전이 둘 다 끝나는 순간 첫 사람이 걷기 시작")]
    [Min(0f)] public float letterboxInSeconds;
    [Tooltip("t=0 부터 짧은 암전 전체 길이(초) — 1/3 검게, 1/3 유지, 1/3 밝게. 한가운데(가장 깊을 때) 각자 걷기 시작 지점(WalkStart)으로 순간이동. 0 = 암전 없이 t=0 에 순간이동")]
    [Min(0f)] public float teleportBlackoutSeconds;
    [Tooltip("줌 시작(t=0) 카메라 거리 — 평소 거리 배율(1 = 평소). 클수록 멀리서 시작")]
    [Min(0.1f)] public float cameraStartDistance;
    [Tooltip("줌 끝 카메라 거리 — 평소 거리 배율(1 = 평소)")]
    [Min(0.1f)] public float cameraEndDistance;
    [Tooltip("t=0 → 줌 끝(초). 끝난 뒤로는 끝 거리 유지. 0 = 처음부터 끝 거리")]
    [Min(0f)] public float cameraZoomSeconds;
    [Tooltip("줌 이징(진행도 → 시작→끝 거리 비율)")]
    public Ease cameraZoomEase;

    [Header("보행 (걷기 시작 지점 → 문턱 → 좌석)")]
    [Tooltip("걷는 속도(m/s) — 전원 같다. 각자 보행 시간 = 자기 걷기 시작 지점 → 공용 경로점 → 문턱 길이 ÷ 이 값. 경로를 바꿔도 속도는 그대로다(플레이어 평소 이동 5 m/s)")]
    [Min(0.1f)] public float walkSpeed;
    [Tooltip("앞사람이 걷기 시작한 순간 → 다음 사람이 걷기 시작(초). 탑승 다리 폭이 0.8m 라 한 줄로 들어간다")]
    [Min(0f)] public float walkInterval;
    [Tooltip("문턱에 닿은 순간 → 좌석까지 미끄러져 들어가 숨는 시간(초). 포드 실내가 낮아 충돌 없이 보간한다. 문턱이 없으면 마지막 경로점 → 좌석")]
    [Min(0f)] public float seatSlideSeconds;

    [Header("해치 · 강하")]
    [Tooltip("마지막 사람이 포드 안에 도착한 순간 → 해치가 다 닫힘(초). 0 = 해치 연출 없음(단계 건너뜀)")]
    [Min(0f)] public float hatchCloseSeconds;
    [Tooltip("포드가 갱도 아래로 내려가는 시간(초)")]
    [Min(0f)] public float descendSeconds;
    [Tooltip("포드가 내려가는 깊이(m) — 발사대 기준 아래로")]
    [Min(0f)] public float descendDepth;
    [Tooltip("강하 이징(진행도 → 내려간 비율). 기본 InCubic = 처음 천천히, 점점 빠르게")]
    public Ease descendEase;

    [Header("페이드 아웃 → 맵 로딩")]
    [Tooltip("강하가 끝난 순간 → 화면이 완전히 검게 됨(초). 끝나는 순간 맵 로딩 시작")]
    [Min(0f)] public float fadeOutSeconds;

    [Header("화면 UI")]
    [Tooltip("연출 도중 새로 뜬 화면 UI 를 다시 찾아 숨기는 간격(초). 0 = 매 프레임")]
    [DataTableIgnore, Min(0f)] public float hudRescanSeconds;

    public static PodDropLobbyTimings Default => new PodDropLobbyTimings
    {
        letterboxInSeconds = 0.5f,
        teleportBlackoutSeconds = 0.4f,
        cameraStartDistance = 1.8f,
        cameraEndDistance = 1.15f,
        cameraZoomSeconds = 6f,
        cameraZoomEase = Ease.InOutSine,
        walkSpeed = 4.5f,
        walkInterval = 0.4f,
        seatSlideSeconds = 0.4f,
        hatchCloseSeconds = 0f,
        descendSeconds = 2f,
        descendDepth = 10f,
        descendEase = Ease.InCubic,
        fadeOutSeconds = 0.6f,
        hudRescanSeconds = 0.5f,
    };
}

/// <summary>
/// 맵 단계 수치 — 인스펙터에서 단계 순서대로 묶는다(t = 0 은 로딩 화면이 걷힌 순간). 길이는 초, 거리는 m.
/// 이펙트 크기·흔들림은 시간표가 쓰지 않는다 — 착지 시각(<see cref="PodDropMapTimeline.ImpactAt"/>)에 맵 연출이 읽는다.
/// </summary>
[Serializable]
public struct PodDropMapTimings
{
    [Header("대기 · 카메라 (레터박스는 덮인 채 시작)")]
    [Tooltip("로딩 화면이 걷힌 순간 → 포드 낙하 시작(초)")]
    [Min(0f)] public float holdSeconds;
    [Tooltip("줌 시작(t=0) 카메라 거리 — 평소 거리 배율(1 = 평소). 클수록 멀리서 시작")]
    [Min(0.1f)] public float cameraStartDistance;
    [Tooltip("줌 끝 카메라 거리 — 평소 거리 배율. 1 이면 해제 때 평소 카메라와 이어진다")]
    [Min(0.1f)] public float cameraEndDistance;
    [Tooltip("t=0 → 줌 끝(초). 해제 시각보다 짧게 두면 해제 때 카메라가 튀지 않는다")]
    [Min(0f)] public float cameraZoomSeconds;
    [Tooltip("줌 이징(진행도 → 시작→끝 거리 비율)")]
    public Ease cameraZoomEase;

    [Header("낙하 · 착지")]
    [Tooltip("착지점 기준 낙하 시작 높이(m)")]
    [Min(0f)] public float fallHeight;
    [Tooltip("낙하 시작 → 착지(초)")]
    [Min(0f)] public float fallSeconds;
    [Tooltip("낙하 이징(진행도 → 떨어진 비율). 기본 InQuad = 점점 빠르게")]
    public Ease fallEase;
    [Tooltip("착지 땅 갈라짐 이펙트 크기 배율(23호 등장 착지 FX_Ground_Crash 기본 1.5)")]
    [Min(0f)] public float impactEffectScale;
    [Tooltip("착지 카메라 흔들림 세기(0 = 없음)")]
    [Min(0f)] public float shakeAmplitude;
    [Tooltip("착지 카메라 흔들림 시간(초)")]
    [Min(0f)] public float shakeDuration;
    [Tooltip("착지 → 디졸브 시작(초)")]
    [Min(0f)] public float settleSeconds;

    [Header("디졸브 → 레터박스 아웃 → 해제")]
    [Tooltip("포드가 디졸브로 사라지는 시간(초). 시작 순간 플레이어가 보인다")]
    [Min(0f)] public float dissolveSeconds;
    [Tooltip("디졸브가 끝난 순간 → 레터박스가 다 걷힘(초). 끝나는 순간 UI·조작·튜토리얼(S0) 해제")]
    [Min(0f)] public float letterboxOutSeconds;

    [Header("화면 UI · 안전장치")]
    [Tooltip("연출 도중 새로 뜬 화면 UI 를 다시 찾아 숨기는 간격(초). 0 = 매 프레임 — 로딩 화면이 걷히는 프레임에 HUD 가 한 프레임도 보이지 않게(연출 몇 초 동안만)")]
    [DataTableIgnore, Min(0f)] public float hudRescanSeconds;
    [Tooltip("연출을 켠 뒤 로딩 화면이 걷히기(시작)를 기다리는 최대 시간(초, 실시간). 넘으면 잠금·숨김에 갇히지 않게 연출 없이 해제한다. " +
             "로딩 최소 표시(5s) + Ready(2.5s) + 느린 클라 로드보다 넉넉하게")]
    [DataTableIgnore, Min(1f)] public float startTimeoutSeconds;

    // 위치는 시간표가 쓰지 않는다 — 맵 연출(MapPodArrival)이 플레이어 스폰 슬롯(MapGenerator PlayerSpawn) 기준으로 읽는다.
    [Header("착지점 · 좌석 (플레이어 스폰 슬롯 기준 로컬, m)")]
    [Tooltip("포드 착지점 = 스폰 슬롯 + 이 오프셋(슬롯 회전 기준 로컬). 플레이어 좌석은 이 착지점 기준이다")]
    public Vector3 podLandingOffset;
    [Tooltip("플레이어 좌석(숨긴 채 스폰되는 자리) — 포드 착지점 기준 로컬 오프셋, 스폰 순서(P1, P2, P3). 사람이 더 많으면 돌려 쓴다. 비우면 전원 착지점")]
    public Vector3[] seatOffsets;
    [Tooltip("착지점 지면 찾기 — 착지점 위 이 높이(m)에서 아래로 같은 길이만큼 레이를 쏴 맞은 높이를 지면으로 쓴다. 0 = 슬롯 높이 그대로")]
    [Min(0f)] public float groundProbeHeight;

    public static PodDropMapTimings Default => new PodDropMapTimings
    {
        holdSeconds = 0.3f,
        cameraStartDistance = 1.8f,
        cameraEndDistance = 1f,
        cameraZoomSeconds = 2.6f,
        cameraZoomEase = Ease.InOutSine,
        fallHeight = 12f,
        fallSeconds = 0.6f,
        fallEase = Ease.InQuad,
        impactEffectScale = 1.5f,
        shakeAmplitude = 0.6f,
        shakeDuration = 0.35f,
        settleSeconds = 0.4f,
        dissolveSeconds = 1f,
        letterboxOutSeconds = 0.5f,
        hudRescanSeconds = 0f,
        startTimeoutSeconds = 120f,
        podLandingOffset = Vector3.zero,
        // 포드 바닥 약 2.35 × 2.69m 안 — 디졸브로 포드가 걷히면 그 자리에 서 있다.
        seatOffsets = new[] { new Vector3(-0.6f, 0f, 0.45f), new Vector3(0.6f, 0f, 0.45f), new Vector3(0f, 0f, -0.55f) },
        groundProbeHeight = 2f,
    };
}

/// <summary>
/// 로비 포드 연출의 순수 시간표 — 단계 시각, 경과 시간 → 단계·진행률·레터박스 알파·카메라 거리·슬롯별 보행·해치·포드 하강·페이드 알파.
/// 네트워크·씬을 모른다. 경과 시간은 시작 시각(출격 확정) 기준이라 늦게 본 피어도 같은 값을 얻는다(음수 = 시작 전, 첫 단계로 취급).
/// 보행 시간은 슬롯마다 다르다(경로 길이 ÷ 걷는 속도 — 호출자가 계산해 넣는다). 웨이포인트를 따라가는 실제 이동은 호출자 몫이다(<see cref="BossElevatorTimeline.WalkStepAlongPath"/> 처럼 남은 시간에 맞춰 걷는다).
/// 마지막 단계 <see cref="PodDropLobbyPhase.Done"/> 은 끝이 없다 — 그 뒤 처리(맵 로딩)는 호출자가 한다.
/// </summary>
public sealed class PodDropLobbyTimeline
{
    public float LetterboxInSeconds { get; }
    /// <summary>순간이동 암전 전체 길이(초, t=0 시작) — 검게 · 유지 · 밝게가 1/3 씩.</summary>
    public float TeleportBlackoutSeconds { get; }
    public float CameraStartDistance { get; }
    public float CameraEndDistance { get; }
    public float CameraZoomSeconds { get; }
    public Ease CameraZoomEase { get; }
    public float HatchCloseSeconds { get; }
    public float DescendSeconds { get; }
    public float DescendDepth { get; }
    public Ease DescendEase { get; }
    public float FadeOutSeconds { get; }

    /// <summary>걸어 들어가는 사람 수(0 = 아무도 안 걷고 보행 단계 길이 0).</summary>
    public int WalkerCount => _walkSeconds.Length;
    /// <summary>다음 사람이 걷기 시작하는 간격(초).</summary>
    public float WalkInterval { get; }
    /// <summary>문턱 → 좌석 미끄러짐 시간(초) — 전원 같다.</summary>
    public float SeatSlideSeconds { get; }

    // 슬롯별 보행 시간(초, 걷기 시작 지점 → 문턱) — 경로 길이 ÷ 걷는 속도(<see cref="WalkSecondsFor"/>).
    private readonly float[] _walkSeconds;

    /// <summary>암전이 가장 깊은 시각(암전 한가운데) — 각 오너가 자기 걷기 시작 지점으로 순간이동한다. 암전 길이 0 이면 0.</summary>
    public float TeleportAt => TeleportBlackoutSeconds * 0.5f;
    /// <summary>레터박스가 다 들어오고 암전도 다 걷힌 시각 = 보행 단계 시작(첫 사람 출발).</summary>
    public float WalkAt { get; }
    /// <summary>가장 늦게 좌석에 도착한 사람의 도착 시각 = 해치 닫힘 시작.</summary>
    public float HatchCloseAt { get; }
    public float DescendAt { get; }
    public float FadeOutAt { get; }
    /// <summary>완전 암전 시각 — 서버가 맵 로딩을 시작한다. 로비 연출 전체 길이.</summary>
    public float LoadAt { get; }

    /// <param name="walkSeconds">슬롯(보행 순서)별 보행 시간(초, 걷기 시작 지점 → 문턱). 길이 = 걷는 사람 수. 음수·NaN 은 0. 서버가 경로로 계산해 복제한 값을 모든 피어가 같이 쓴다.</param>
    public PodDropLobbyTimeline(PodDropLobbyTimings timings, IReadOnlyList<float> walkSeconds)
    {
        LetterboxInSeconds = Mathf.Max(0f, timings.letterboxInSeconds);
        TeleportBlackoutSeconds = Mathf.Max(0f, timings.teleportBlackoutSeconds);
        CameraStartDistance = timings.cameraStartDistance;
        CameraEndDistance = timings.cameraEndDistance;
        CameraZoomSeconds = Mathf.Max(0f, timings.cameraZoomSeconds);
        CameraZoomEase = timings.cameraZoomEase;
        HatchCloseSeconds = Mathf.Max(0f, timings.hatchCloseSeconds);
        DescendSeconds = Mathf.Max(0f, timings.descendSeconds);
        DescendDepth = Mathf.Max(0f, timings.descendDepth);
        DescendEase = timings.descendEase;
        FadeOutSeconds = Mathf.Max(0f, timings.fadeOutSeconds);

        WalkInterval = Mathf.Max(0f, timings.walkInterval);
        SeatSlideSeconds = Mathf.Max(0f, timings.seatSlideSeconds);
        _walkSeconds = new float[walkSeconds != null ? walkSeconds.Count : 0];
        for (int i = 0; i < _walkSeconds.Length; i++)
            _walkSeconds[i] = walkSeconds[i] > 0f ? walkSeconds[i] : 0f; // NaN 도 0

        // 암전이 레터박스보다 길면 밝아진 뒤에 걷는다 — 걷는 모습이 검은 화면에 묻히지 않게.
        WalkAt = Mathf.Max(LetterboxInSeconds, TeleportBlackoutSeconds);
        // 가장 늦게 도착하는 사람 기준. 아무도 안 걸으면 보행 시간 없이 바로 해치 닫힘.
        HatchCloseAt = WalkAt;
        for (int slot = 0; slot < WalkerCount; slot++)
            HatchCloseAt = Mathf.Max(HatchCloseAt, WalkEndAt(slot));
        DescendAt = HatchCloseAt + HatchCloseSeconds;
        FadeOutAt = DescendAt + DescendSeconds;
        LoadAt = FadeOutAt + FadeOutSeconds;
    }

    /// <summary>슬롯(0 = P1)이 걷기 시작하는 경과 시각(초).</summary>
    public float WalkStartAt(int slot) => WalkAt + Mathf.Max(0, slot) * WalkInterval;

    /// <summary>슬롯의 보행 시간(초, 걷기 시작 지점 → 문턱). 명단 밖 슬롯은 0.</summary>
    public float WalkSecondsOf(int slot) => slot >= 0 && slot < _walkSeconds.Length ? _walkSeconds[slot] : 0f;

    /// <summary>슬롯이 문턱에 닿는 경과 시각(초) — 여기부터 좌석으로 미끄러진다.</summary>
    public float WalkArriveAt(int slot) => WalkStartAt(slot) + WalkSecondsOf(slot);

    /// <summary>슬롯이 좌석에 도착하는 경과 시각(초) — 이 순간 숨긴다.</summary>
    public float WalkEndAt(int slot) => WalkArriveAt(slot) + SeatSlideSeconds;

    /// <summary>슬롯의 보행 진행률(0 = 걷기 시작 지점, 1 = 문턱). 선형 — 걷는 속도가 일정하다.</summary>
    public float WalkProgressAt(int slot, double elapsed) => PodDropTimeline.Progress(elapsed, WalkStartAt(slot), WalkSecondsOf(slot));

    /// <summary>경로 길이(m) ÷ 걷는 속도(m/s) = 보행 시간(초). 길이·속도가 0 이하(또는 NaN)면 0 — 무한·NaN 시간표를 만들지 않는다.</summary>
    public static float WalkSecondsFor(float pathLength, float walkSpeed) =>
        pathLength > 0f && walkSpeed > 0f ? pathLength / walkSpeed : 0f;

    /// <summary>단계가 시작되는 경과 시각(초).</summary>
    public float StartOf(PodDropLobbyPhase phase)
    {
        switch (phase)
        {
            case PodDropLobbyPhase.LetterboxIn: return 0f;
            case PodDropLobbyPhase.Walk: return WalkAt;
            case PodDropLobbyPhase.HatchClose: return HatchCloseAt;
            case PodDropLobbyPhase.Descend: return DescendAt;
            case PodDropLobbyPhase.FadeOut: return FadeOutAt;
            default: return LoadAt;
        }
    }

    /// <summary>단계 길이(초). <see cref="PodDropLobbyPhase.Done"/> 은 끝이 없어 0.</summary>
    public float DurationOf(PodDropLobbyPhase phase) =>
        phase == PodDropLobbyPhase.Done ? 0f : StartOf(phase + 1) - StartOf(phase);

    /// <summary>경과 시간의 단계. 경계 시각은 다음 단계에 속한다(0 길이 단계는 건너뛴다).</summary>
    public PodDropLobbyPhase PhaseAt(double elapsed)
    {
        if (elapsed >= LoadAt) return PodDropLobbyPhase.Done;
        if (elapsed >= FadeOutAt) return PodDropLobbyPhase.FadeOut;
        if (elapsed >= DescendAt) return PodDropLobbyPhase.Descend;
        if (elapsed >= HatchCloseAt) return PodDropLobbyPhase.HatchClose;
        if (elapsed >= WalkAt) return PodDropLobbyPhase.Walk;
        return PodDropLobbyPhase.LetterboxIn;
    }

    /// <summary>경과 시간이 속한 단계 안 진행률(0~1, 선형). <see cref="PodDropLobbyPhase.Done"/> 은 1.</summary>
    public float PhaseProgressAt(double elapsed)
    {
        PodDropLobbyPhase phase = PhaseAt(elapsed);
        if (phase == PodDropLobbyPhase.Done) return 1f;
        return PodDropTimeline.Progress(elapsed, StartOf(phase), DurationOf(phase));
    }

    /// <summary>
    /// 레터박스 알파(0 = 없음, 1 = 다 덮임). 들어오는 구간은 선형, 그 뒤 1 유지.
    /// <see cref="LoadAt"/>(완전 암전)부터 0 — 검은 화면 아래에서 걷혀 로딩 화면을 가리지 않는다. UI 숨김도 같은 값으로 본다(&gt; 0 이면 숨김).
    /// </summary>
    public float LetterboxAlphaAt(double elapsed)
    {
        if (elapsed < 0d || elapsed >= LoadAt) return 0f;
        return PodDropTimeline.Progress(elapsed, 0f, LetterboxInSeconds);
    }

    /// <summary>카메라 거리(평소 거리 배율). t=0 시작 거리 → 줌 끝 끝 거리, 그 뒤 유지.</summary>
    public float CameraDistanceAt(double elapsed) =>
        PodDropTimeline.CameraDistance(elapsed, CameraStartDistance, CameraEndDistance, CameraZoomSeconds, CameraZoomEase);

    /// <summary>해치 닫힘 진행률(0 = 열림, 1 = 닫힘). 해치 연출이 없으면(길이 0) 닫힘 시각부터 1 — 호출자가 해치를 쓰지 않으면 무시한다.</summary>
    public float HatchCloseProgressAt(double elapsed) => PodDropTimeline.Progress(elapsed, HatchCloseAt, HatchCloseSeconds);

    /// <summary>포드 높이 오프셋(m, 발사대 기준). 강하 전 0, 강하 중 0 → −<see cref="DescendDepth"/>, 그 뒤 유지.</summary>
    public float PodOffsetAt(double elapsed) =>
        -DescendDepth * Easing.Evaluate(DescendEase, PodDropTimeline.Progress(elapsed, DescendAt, DescendSeconds));

    /// <summary>검은 화면 알파(0 = 투명, 1 = 완전 암전). 페이드 구간은 선형, <see cref="LoadAt"/> 부터 1.</summary>
    public float FadeAlphaAt(double elapsed) => PodDropTimeline.Progress(elapsed, FadeOutAt, FadeOutSeconds);

    /// <summary>
    /// 순간이동 암전 알파 — t=0 부터 1/3 동안 0 → 1, 1/3 동안 1 유지(<see cref="TeleportAt"/> 가 이 안), 1/3 동안 1 → 0, 그 뒤 0. 길이 0 이면 늘 0.
    /// </summary>
    public float BlackoutAlphaAt(double elapsed)
    {
        float third = TeleportBlackoutSeconds / 3f;
        float fadeIn = PodDropTimeline.Progress(elapsed, 0f, third);
        float fadeOut = 1f - PodDropTimeline.Progress(elapsed, third * 2f, third);
        return Mathf.Min(fadeIn, fadeOut);
    }

    /// <summary>화면에 그릴 검은 알파 — 순간이동 암전과 끝 페이드 중 큰 값.</summary>
    public float ScreenFadeAlphaAt(double elapsed) => Mathf.Max(BlackoutAlphaAt(elapsed), FadeAlphaAt(elapsed));
}

/// <summary>
/// 맵 포드 착지 연출의 순수 시간표 — 단계 시각, 경과 시간 → 단계·진행률·레터박스 알파·카메라 거리·포드 높이·디졸브 값.
/// 경과 시간은 로딩 화면이 걷힌 시각 기준(음수 = 시작 전, 첫 단계로 취급 — 레터박스는 덮여 있다).
/// 해제(<see cref="ReleaseAt"/>)는 항상 레터박스 아웃이 끝난 뒤다. 마지막 단계 <see cref="PodDropMapPhase.Released"/> 는 끝이 없다.
/// </summary>
public sealed class PodDropMapTimeline
{
    public float HoldSeconds { get; }
    public float CameraStartDistance { get; }
    public float CameraEndDistance { get; }
    public float CameraZoomSeconds { get; }
    public Ease CameraZoomEase { get; }
    public float FallHeight { get; }
    public float FallSeconds { get; }
    public Ease FallEase { get; }
    public float SettleSeconds { get; }
    public float DissolveSeconds { get; }
    public float LetterboxOutSeconds { get; }

    public float FallAt { get; }
    /// <summary>착지 시각 — 땅 갈라짐 이펙트·카메라 흔들림.</summary>
    public float ImpactAt { get; }
    /// <summary>디졸브 시작 = 플레이어가 보이는 시각.</summary>
    public float DissolveAt { get; }
    public float PlayersVisibleAt => DissolveAt;
    public float LetterboxOutAt { get; }
    /// <summary>레터박스가 다 걷힌 시각 = UI·조작·튜토리얼(S0) 해제. 맵 연출 전체 길이.</summary>
    public float ReleaseAt { get; }

    public PodDropMapTimeline(PodDropMapTimings timings)
    {
        HoldSeconds = Mathf.Max(0f, timings.holdSeconds);
        CameraStartDistance = timings.cameraStartDistance;
        CameraEndDistance = timings.cameraEndDistance;
        CameraZoomSeconds = Mathf.Max(0f, timings.cameraZoomSeconds);
        CameraZoomEase = timings.cameraZoomEase;
        FallHeight = Mathf.Max(0f, timings.fallHeight);
        FallSeconds = Mathf.Max(0f, timings.fallSeconds);
        FallEase = timings.fallEase;
        SettleSeconds = Mathf.Max(0f, timings.settleSeconds);
        DissolveSeconds = Mathf.Max(0f, timings.dissolveSeconds);
        LetterboxOutSeconds = Mathf.Max(0f, timings.letterboxOutSeconds);

        FallAt = HoldSeconds;
        ImpactAt = FallAt + FallSeconds;
        DissolveAt = ImpactAt + SettleSeconds;
        LetterboxOutAt = DissolveAt + DissolveSeconds;
        ReleaseAt = LetterboxOutAt + LetterboxOutSeconds;
    }

    /// <summary>단계가 시작되는 경과 시각(초).</summary>
    public float StartOf(PodDropMapPhase phase)
    {
        switch (phase)
        {
            case PodDropMapPhase.Hold: return 0f;
            case PodDropMapPhase.Fall: return FallAt;
            case PodDropMapPhase.Settle: return ImpactAt;
            case PodDropMapPhase.Dissolve: return DissolveAt;
            case PodDropMapPhase.LetterboxOut: return LetterboxOutAt;
            default: return ReleaseAt;
        }
    }

    /// <summary>단계 길이(초). <see cref="PodDropMapPhase.Released"/> 은 끝이 없어 0.</summary>
    public float DurationOf(PodDropMapPhase phase) =>
        phase == PodDropMapPhase.Released ? 0f : StartOf(phase + 1) - StartOf(phase);

    /// <summary>경과 시간의 단계. 경계 시각은 다음 단계에 속한다(0 길이 단계는 건너뛴다).</summary>
    public PodDropMapPhase PhaseAt(double elapsed)
    {
        if (elapsed >= ReleaseAt) return PodDropMapPhase.Released;
        if (elapsed >= LetterboxOutAt) return PodDropMapPhase.LetterboxOut;
        if (elapsed >= DissolveAt) return PodDropMapPhase.Dissolve;
        if (elapsed >= ImpactAt) return PodDropMapPhase.Settle;
        if (elapsed >= FallAt) return PodDropMapPhase.Fall;
        return PodDropMapPhase.Hold;
    }

    /// <summary>경과 시간이 속한 단계 안 진행률(0~1, 선형). <see cref="PodDropMapPhase.Released"/> 는 1.</summary>
    public float PhaseProgressAt(double elapsed)
    {
        PodDropMapPhase phase = PhaseAt(elapsed);
        if (phase == PodDropMapPhase.Released) return 1f;
        return PodDropTimeline.Progress(elapsed, StartOf(phase), DurationOf(phase));
    }

    /// <summary>해제됐는가 — UI·조작·튜토리얼(S0) 시작. 레터박스가 다 걷힌 뒤에만 true.</summary>
    public bool IsReleasedAt(double elapsed) => elapsed >= ReleaseAt;

    /// <summary>레터박스 알파(0 = 없음, 1 = 다 덮임). 레터박스 아웃 전 1(로딩이 걷힐 때 이미 덮임), 아웃 구간 선형 1 → 0, 그 뒤 0. UI 숨김도 같은 값으로 본다.</summary>
    public float LetterboxAlphaAt(double elapsed) => 1f - PodDropTimeline.Progress(elapsed, LetterboxOutAt, LetterboxOutSeconds);

    /// <summary>카메라 거리(평소 거리 배율). t=0 시작 거리 → 줌 끝 끝 거리, 그 뒤 유지.</summary>
    public float CameraDistanceAt(double elapsed) =>
        PodDropTimeline.CameraDistance(elapsed, CameraStartDistance, CameraEndDistance, CameraZoomSeconds, CameraZoomEase);

    /// <summary>포드 높이(착지점 기준 오프셋, m). 낙하 전 <see cref="FallHeight"/>, 낙하 중 → 0, 착지 뒤 0.</summary>
    public float PodHeightAt(double elapsed) =>
        FallHeight * (1f - Easing.Evaluate(FallEase, PodDropTimeline.Progress(elapsed, FallAt, FallSeconds)));

    /// <summary>디졸브 진행률(0 = 포드 온전, 1 = 다 사라짐). 선형. 셰이더 값 변환(예: DissolveFx `_Cutoff` = 1 − 이 값)은 호출자가 한다.</summary>
    public float DissolveProgressAt(double elapsed) => PodDropTimeline.Progress(elapsed, DissolveAt, DissolveSeconds);
}

/// <summary>로비·맵 포드 시간표가 같이 쓰는 계산.</summary>
public static class PodDropTimeline
{
    /// <summary>구간 진행률(0~1, 선형). 길이 0 이면 시작 시각부터 1.</summary>
    public static float Progress(double elapsed, float start, float duration)
    {
        if (duration <= 0f) return elapsed >= start ? 1f : 0f;
        return Mathf.Clamp01((float)((elapsed - start) / duration));
    }

    /// <summary>t=0 에서 <paramref name="from"/> → <paramref name="seconds"/> 뒤 <paramref name="to"/>(이징), 그 뒤 유지.</summary>
    public static float CameraDistance(double elapsed, float from, float to, float seconds, Ease ease) =>
        Mathf.LerpUnclamped(from, to, Easing.Evaluate(ease, Progress(elapsed, 0f, seconds)));
}
