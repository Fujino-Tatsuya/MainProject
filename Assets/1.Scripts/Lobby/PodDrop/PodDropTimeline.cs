using System;
using System.Collections.Generic;
using EuniTween;
using UnityEngine;

/// <summary>
/// 로비 단계 수치 — 인스펙터에서 단계 순서대로 묶는다(t = 0 은 출격 집결 확정). 길이는 초, 거리는 m.
/// 카메라 거리는 플레이어 카메라 FollowOffset 배율(1 = 평소 거리) — <see cref="CameraTargetSwitcher"/> 가 오프셋에 배율을 곱하는 방식과 같다.
/// 발사대 클립은 1배속으로 끝까지 재생하고 단계 전환은 클립 애니 이벤트(<see cref="PodAnimEventRelay"/>)가 한다(할 일 106) — 클립 시각·길이 칸은 없다.
/// </summary>
[Serializable]
public struct PodDropLobbyTimings
{
    [Header("레터박스 · 카메라")]
    [Tooltip("t=0(출격 확정) → 위·아래 레터박스가 다 들어옴(초). UI 는 레터박스와 같이 숨는다. 이것과 암전이 둘 다 끝나야(그리고 해치가 열려야) 첫 사람이 걷기 시작")]
    [Min(0f)] public float letterboxInSeconds;
    [Tooltip("레터박스 들어오기 이징(진행도 → 덮인 비율). 기본 Linear = 일정한 속도")]
    public Ease letterboxInEase;
    [Tooltip("시작 암전 — t=0 부터 화면이 다 검어지기까지(초). 0 = 곧바로 검게(유지가 있으면)")]
    [Min(0f)] public float blackoutFadeInSeconds;
    [Tooltip("시작 암전 — 다 검은 채 유지(초). 이 구간 한가운데 각자 걷기 시작 지점(WalkStart)으로 순간이동하고 해치 클립이 시작한다. 세 구간이 모두 0 = 암전 없이 t=0 에 순간이동")]
    [Min(0f)] public float blackoutHoldSeconds;
    [Tooltip("시작 암전 — 유지가 끝난 뒤 다 밝아지기까지(초)")]
    [Min(0f)] public float blackoutFadeOutSeconds;
    [Tooltip("줌 시작 카메라 거리 — 평소 거리 배율(1 = 평소). 클수록 멀리서 시작. 줌 시작 지연 동안 이 거리로 멈춰 있다")]
    [Min(0.1f)] public float cameraStartDistance;
    [Tooltip("줌 끝 카메라 거리 — 평소 거리 배율(1 = 평소)")]
    [Min(0.1f)] public float cameraEndDistance;
    [Tooltip("t=0 → 줌 시작(초). 그동안 시작 거리로 멈춰 있다. 0 = t=0 에 바로 줌")]
    [Min(0f)] public float cameraZoomDelaySeconds;
    [Tooltip("줌 시작 → 줌 끝(초). 끝난 뒤로는 끝 거리 유지. 0 = 줌 시작 순간 끝 거리")]
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

    [Header("발사대 애니 (Animator 상태 이름 — 1배속, 클립 이벤트가 다음 단계를 연다)")]
    [Tooltip("해치 상태 이름(컨트롤러 Base Layer). 시작 암전 한가운데(순간이동 순간)부터 1배속으로 재생하고, 클립 이벤트(PodClipEvent — 기본 103프레임)에서 그 자세로 멈춘 뒤 탑승한다. " +
             "이벤트 위치는 FBX 임포트 창 Animation 탭에서 옮긴다. 비우면 해치 애니 없이 곧바로 탑승")]
    public string hatchState;
    [Tooltip("발사 상태 이름(컨트롤러 Base Layer) — 해치 닫힘·갱도 문 열림·포드 하강이 한 클립. 가장 늦게 앉는 순간 처음부터 1배속으로 재생하고, " +
             "끝 프레임 클립 이벤트(PodClipEvent)부터 끝 대기 → 끝 페이드 아웃(검게) 뒤 로딩 화면으로 넘어간다(할 일 121). 비우면 앉자마자 끝 대기부터")]
    public string launchState;
    [Tooltip("발사 클립 가속 1단계 배율 — 발사 클립의 가속 1단계 이벤트(PodClipEvent 정수 인자 1, 기본 213프레임)부터 2단계 이벤트까지 이 배율로 재생한다(1 = 가속 없음). " +
             "전환 프레임은 FBX 임포트 창 Animation 탭에서 이벤트를 옮겨 튜닝한다. 호스트 이벤트 시각을 서버가 복제해 클라도 같은 시점에 가속한다")]
    [Min(0.01f)] public float launchFastSpeed;
    [Tooltip("발사 클립 가속 2단계 배율 — 발사 클립의 가속 2단계 이벤트(PodClipEvent 정수 인자 2, 기본 553프레임)부터 이 배율로 끝까지 재생한다(1단계보다 작으면 끝으로 가며 감속). " +
             "전환 프레임은 FBX 임포트 창 Animation 탭에서 이벤트를 옮겨 튜닝한다. 호스트 이벤트 시각을 서버가 복제해 클라도 같은 시점에 바꾼다")]
    [Min(0.01f)] public float launchFastSpeed2;

    [Header("발사 끝 → 로딩 (서버가 확정한 발사 클립 끝 시각 기준)")]
    [Tooltip("발사 클립 끝 이벤트 → 끝 페이드 아웃 시작(초). 그동안 마지막 자세 그대로 보인다. 0 = 끝 이벤트 순간 곧바로 페이드")]
    [Min(0f)] public float launchEndWaitSeconds;
    [Tooltip("끝 페이드 아웃 — 화면이 다 검어지기까지(초, 선형 — 시작 암전과 같은 검은 화면). 다 검어진 순간 서버가 로딩을 건다(로딩 화면이 검은 화면 위에 뜬다). " +
             "대기·페이드 둘 다 0 = 끝 이벤트 순간 곧바로 로딩(페이드 없음)")]
    [Min(0f)] public float launchEndFadeSeconds;

    [Header("화면 UI")]
    [Tooltip("연출 도중 새로 뜬 화면 UI 를 다시 찾아 숨기는 간격(초). 0 = 매 프레임")]
    [DataTableIgnore, Min(0f)] public float hudRescanSeconds;

    public static PodDropLobbyTimings Default => new PodDropLobbyTimings
    {
        letterboxInSeconds = 0.5f,
        letterboxInEase = Ease.Linear,
        // 짧은 암전 0.4초를 셋으로(C2 까지의 1/3 고정 비율과 같다).
        blackoutFadeInSeconds = 0.4f / 3f,
        blackoutHoldSeconds = 0.4f / 3f,
        blackoutFadeOutSeconds = 0.4f / 3f,
        cameraStartDistance = 1.8f,
        cameraEndDistance = 1.15f,
        cameraZoomDelaySeconds = 0f,
        cameraZoomSeconds = 6f,
        cameraZoomEase = Ease.InOutSine,
        walkSpeed = 4.5f,
        walkInterval = 0.4f,
        seatSlideSeconds = 0.4f,
        hatchState = "DP_HatchLobbyOpenClose",
        launchState = "DP_LaunchCycle",
        launchFastSpeed = 6f,
        launchFastSpeed2 = 1f,
        launchEndWaitSeconds = 1f,
        launchEndFadeSeconds = 1f,
        hudRescanSeconds = 0.5f,
    };
}

/// <summary>
/// 맵 단계 수치 — 인스펙터에서 단계 순서대로 묶는다(t = 0 은 로딩 화면이 걷힌 순간). 길이는 초, 거리는 m.
/// 착지 애니는 1배속으로 끝까지 재생하고 다음 단계는 클립 끝 애니 이벤트(<see cref="PodAnimEventRelay"/>)가 연다(할 일 106) — 클립 길이·구간 칸은 없다.
/// 이펙트 크기·흔들림·위치는 시간표가 쓰지 않는다 — 맵 연출(<see cref="MapPodArrival"/>)이 읽는다.
/// </summary>
[Serializable]
public struct PodDropMapTimings
{
    [Header("레터박스 · 카메라 (레터박스는 진입 때 이미 덮여 있다)")]
    [Tooltip("로딩 화면이 걷힌 순간 → 포드 낙하 시작(초)")]
    [Min(0f)] public float holdSeconds;
    [Tooltip("로딩 화면이 걷힌 순간 검은 화면에서 밝아지기까지(초). 다른 단계와 겹쳐 흐른다(시각을 밀지 않는다). " +
             "0 = 검은 화면 없이 곧바로 맵이 보인다(로딩 화면 자체는 페이드 없이 한 번에 사라진다)")]
    [Min(0f)] public float fadeInSeconds;
    [Tooltip("도착 페이드인 이징(진행도 → 밝아진 비율). 기본 Linear = 일정한 속도")]
    public Ease fadeInEase;
    [Tooltip("줌 시작 카메라 거리 — 평소 거리 배율(1 = 평소). 1 보다 크면 약간 줌 아웃한 채 시작. 줌 시작 지연 동안 이 거리로 멈춰 있다")]
    [Min(0.1f)] public float cameraStartDistance;
    [Tooltip("줌 끝 카메라 거리 — 평소 거리 배율. 1 이면 해제 때 평소 카메라와 이어진다")]
    [Min(0.1f)] public float cameraEndDistance;
    [Tooltip("t=0 → 줌 시작(초). 그동안 시작 거리로 멈춰 있다. 0 = t=0 에 바로 줌")]
    [Min(0f)] public float cameraZoomDelaySeconds;
    [Tooltip("줌 시작 → 줌 끝(초)")]
    [Min(0f)] public float cameraZoomSeconds;
    [Tooltip("줌 이징(진행도 → 시작→끝 거리 비율)")]
    public Ease cameraZoomEase;
    [Tooltip("카메라 시작 지점 높이 — 착지점 위 (날개·다리 연출 높이 + 이 값) m 를 보고 시작한다. 급감속이 시작되면 포드를 따라가고, 착지 뒤 조작이 돌아오는 순간 플레이어로 넘어간다")]
    public float cameraStartHeightOffset;

    [Header("빠른 낙하 → 급감속 (날개·다리 펴기)")]
    [Tooltip("착지점 기준 낙하 시작 높이(m)")]
    [Min(0f)] public float fallHeight;
    [Tooltip("날개·다리 연출 높이 — 착지점 위(m). 급감속이 끝나는 높이이고, 펴기 끝 이벤트가 아직이면 여기서 기다렸다 착륙한다")]
    [Min(0f)] public float hoverHeight;
    [Tooltip("낙하 시작 → 급감속 시작(초). 빠르게 떨어진다")]
    [Min(0f)] public float fallSeconds;
    [Tooltip("빠른 낙하 이징(진행도 → 떨어진 비율). 기본 Linear = 일정한 빠른 속도(Out 계열이면 급감속 전에 이미 느려진다)")]
    public Ease fallEase;
    [Tooltip("급감속 시작 높이 — 날개·다리 연출 높이 위(m). 여기서 날개·다리 펴기 클립을 1배속으로 재생하며 감속한다")]
    [Min(0f)] public float brakeDistance;
    [Tooltip("급감속 시작 → 날개·다리 연출 높이 도착(초)")]
    [Min(0f)] public float brakeSeconds;
    [Tooltip("급감속 이징(진행도 → 내려온 비율). Out 계열 = 처음 빠르고 끝에서 멈춘다")]
    public Ease brakeEase;
    [Tooltip("급감속 시작 순간 같이 재생하는 날개 펴기 상태(Wings 레이어). 비우면 생략")]
    public string wingDeployState;
    [Tooltip("급감속 시작 순간 같이 재생하는 다리 펴기 상태(Gear 레이어). 비우면 생략. 둘 다 끝 클립 이벤트(PodClipEvent)가 와야(그리고 연출 높이에 닿아야) 착륙한다")]
    public string gearDeployState;

    [Header("착륙 → 해치 열기 · 날개 접기")]
    [Tooltip("날개·다리 연출 높이 → 지면(초). 천천히 내려앉는다")]
    [Min(0f)] public float landSeconds;
    [Tooltip("착륙 이징(진행도 → 내려온 비율)")]
    public Ease landEase;
    [Tooltip("착륙 땅 갈라짐 이펙트 크기 배율(23호 등장 착지 FX_Ground_Crash 기본 1.5)")]
    [Min(0f)] public float impactEffectScale;
    [Tooltip("착륙 카메라 흔들림 세기(0 = 없음)")]
    [Min(0f)] public float shakeAmplitude;
    [Tooltip("착륙 카메라 흔들림 시간(초)")]
    [Min(0f)] public float shakeDuration;
    [Tooltip("착륙 순간 같이 재생하는 해치 열기 상태(Hatches 레이어). 비우면 생략")]
    public string hatchOpenState;
    [Tooltip("착륙 순간 같이 재생하는 날개 접기 상태(Wings 레이어). 비우면 생략. 둘 다 끝 클립 이벤트(PodClipEvent)가 오면 디졸브 대기가 시작된다")]
    public string wingFoldState;

    [Header("디졸브 · 레터박스 아웃 → 해제")]
    [Tooltip("해치 열기·날개 접기 끝 이벤트 → 디졸브 시작(초)")]
    [Min(0f)] public float dissolveDelaySeconds;
    [Tooltip("포드가 디졸브로 사라지는 시간(초). 시작 순간 플레이어가 보이고(실루엣은 아직 안 그림), 끝나는 순간 조작·실루엣이 돌아오고 카메라가 플레이어로 넘어간다")]
    [Min(0f)] public float dissolveSeconds;
    [Tooltip("디졸브 시작 → 레터박스가 다 걷힘(초). 디졸브·레터박스가 둘 다 끝나면 UI·튜토리얼(S0) 해제")]
    [Min(0f)] public float letterboxOutSeconds;
    [Tooltip("레터박스 걷히기 이징(진행도 → 걷힌 비율). 기본 Linear = 일정한 속도")]
    public Ease letterboxOutEase;

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
    [Tooltip("착지한 포드 루트(선체 바닥)의 지면 위 높이(m). 펴진 랜딩 기어 끝이 선체 바닥보다 0.248m 아래라(DP_GearDeploy 끝 실측) 0 이면 기어가 바닥에 묻힌다. 플레이어 좌석 높이는 바꾸지 않는다")]
    public float podGroundClearance;

    public static PodDropMapTimings Default => new PodDropMapTimings
    {
        holdSeconds = 0.3f,
        fadeInSeconds = 0f,
        fadeInEase = Ease.Linear,
        cameraStartDistance = 1.8f,
        cameraEndDistance = 1f,
        cameraZoomDelaySeconds = 0f,
        cameraZoomSeconds = 2.6f,
        cameraZoomEase = Ease.InOutSine,
        cameraStartHeightOffset = 2f,
        fallHeight = 30f,
        hoverHeight = 5f,
        fallSeconds = 0.6f,
        fallEase = Ease.Linear,
        brakeDistance = 3f,
        brakeSeconds = 0.8f,
        brakeEase = Ease.OutCubic,
        wingDeployState = "DP_WingDeploy",
        gearDeployState = "DP_GearDeploy",
        landSeconds = 1.5f,
        landEase = Ease.InOutSine,
        impactEffectScale = 1.5f,
        shakeAmplitude = 0.6f,
        shakeDuration = 0.35f,
        hatchOpenState = "DP_HatchStageOpen",
        wingFoldState = "DP_WingFold",
        dissolveDelaySeconds = 1f,
        dissolveSeconds = 1f,
        letterboxOutSeconds = 0.5f,
        letterboxOutEase = Ease.Linear,
        hudRescanSeconds = 0f,
        startTimeoutSeconds = 120f,
        podLandingOffset = Vector3.zero,
        // 포드 바닥 약 2.35 × 2.69m 안 — 디졸브로 포드가 걷히면 그 자리에 서 있다.
        seatOffsets = new[] { new Vector3(-0.6f, 0f, 0.45f), new Vector3(0.6f, 0f, 0.45f), new Vector3(0f, 0f, -0.55f) },
        groundProbeHeight = 2f,
        // DP_GearDeploy 끝 자세 기어 최저점 −0.248m(포드 루트 기준, PF_DropPod_PodOnly_Combined_v001 Pod 메시 실측) + 여유 0.002.
        podGroundClearance = 0.25f,
    };

    /// <summary>급감속 시작 순간 같이 재생하는 상태(날개 · 다리 — 비운 칸 포함).</summary>
    public string[] DeployStates => new[] { wingDeployState, gearDeployState };

    /// <summary>착륙 순간 같이 재생하는 상태(해치 · 날개 접기 — 비운 칸 포함).</summary>
    public string[] OpenStates => new[] { hatchOpenState, wingFoldState };
}

/// <summary>
/// 로비 포드 연출의 순수 시간표(할 일 100 C2 → 106) — 경과 시간 → 레터박스 알파·암전·카메라 거리·슬롯별 보행.
/// 네트워크·씬을 모른다. 경과 시간은 시작 시각(출격 확정) 기준이라 늦게 본 피어도 같은 값을 얻는다(음수 = 시작 전).
/// 클립 단계(해치 열림 → 탑승 → 발사 → 끝 대기·페이드 → 로딩)는 시각이 정해져 있지 않다 — 서버가 받은 클립 이벤트 시각(해치 열림, 발사 끝)을 넣어 계산한다.
/// 아직 안 온 이벤트는 <see cref="PodDropTimeline.NotYet"/>(+∞).
/// </summary>
public sealed class PodDropLobbyTimeline
{
    public float LetterboxInSeconds { get; }
    public Ease LetterboxInEase { get; }
    /// <summary>시작 암전 — 검어지기 · 유지 · 밝아지기(초, t=0 부터 이 순서).</summary>
    public float BlackoutFadeInSeconds { get; }
    public float BlackoutHoldSeconds { get; }
    public float BlackoutFadeOutSeconds { get; }
    /// <summary>시작 암전 전체 길이(초) = 세 구간 합.</summary>
    public float BlackoutSeconds => BlackoutFadeInSeconds + BlackoutHoldSeconds + BlackoutFadeOutSeconds;
    public float CameraStartDistance { get; }
    public float CameraEndDistance { get; }
    public float CameraZoomDelaySeconds { get; }
    public float CameraZoomSeconds { get; }
    public Ease CameraZoomEase { get; }

    /// <summary>걸어 들어가는 사람 수(0 = 아무도 안 걷는다).</summary>
    public int WalkerCount => _walkSeconds.Length;
    /// <summary>다음 사람이 걷기 시작하는 간격(초).</summary>
    public float WalkInterval { get; }
    /// <summary>문턱 → 좌석 미끄러짐 시간(초) — 전원 같다.</summary>
    public float SeatSlideSeconds { get; }
    /// <summary>발사 클립 끝 이벤트 → 끝 페이드 아웃 시작(초).</summary>
    public float LaunchEndWaitSeconds { get; }
    /// <summary>끝 페이드 아웃 — 다 검어지기까지(초).</summary>
    public float LaunchEndFadeSeconds { get; }
    /// <summary>발사 클립 끝 이벤트 → 로딩 시작(초) = 끝 대기 + 끝 페이드.</summary>
    public float LoadAfterLaunchEndSeconds => LaunchEndWaitSeconds + LaunchEndFadeSeconds;

    // 슬롯별 보행 시간(초, 걷기 시작 지점 → 문턱) — 경로 길이 ÷ 걷는 속도(<see cref="WalkSecondsFor"/>).
    private readonly float[] _walkSeconds;

    /// <summary>순간이동 시각 = 암전 유지 구간 한가운데 — 각 오너가 자기 걷기 시작 지점으로 순간이동하고, 해치 클립이 처음부터 재생된다. 암전이 없으면 0.</summary>
    public float TeleportAt => BlackoutFadeInSeconds + BlackoutHoldSeconds * 0.5f;
    /// <summary>해치 클립 재생 시작 = 순간이동 순간.</summary>
    public float HatchAt => TeleportAt;
    /// <summary>레터박스가 다 들어오고 암전도 다 걷힌 시각 — 해치가 이보다 일찍 열려도 탑승은 여기부터.</summary>
    public float WalkReadyAt { get; }
    /// <summary>탑승 시작 → 가장 늦게 좌석에 도착(초). 아무도 안 걸으면 0.</summary>
    public float SeatedSeconds { get; }

    /// <param name="walkSeconds">슬롯(보행 순서)별 보행 시간(초, 걷기 시작 지점 → 문턱). 길이 = 걷는 사람 수. 음수·NaN 은 0. 서버가 경로로 계산해 복제한 값을 모든 피어가 같이 쓴다.</param>
    public PodDropLobbyTimeline(PodDropLobbyTimings timings, IReadOnlyList<float> walkSeconds)
    {
        LetterboxInSeconds = Mathf.Max(0f, timings.letterboxInSeconds);
        LetterboxInEase = timings.letterboxInEase;
        BlackoutFadeInSeconds = Mathf.Max(0f, timings.blackoutFadeInSeconds);
        BlackoutHoldSeconds = Mathf.Max(0f, timings.blackoutHoldSeconds);
        BlackoutFadeOutSeconds = Mathf.Max(0f, timings.blackoutFadeOutSeconds);
        CameraStartDistance = timings.cameraStartDistance;
        CameraEndDistance = timings.cameraEndDistance;
        CameraZoomDelaySeconds = Mathf.Max(0f, timings.cameraZoomDelaySeconds);
        CameraZoomSeconds = Mathf.Max(0f, timings.cameraZoomSeconds);
        CameraZoomEase = timings.cameraZoomEase;

        WalkInterval = Mathf.Max(0f, timings.walkInterval);
        SeatSlideSeconds = Mathf.Max(0f, timings.seatSlideSeconds);
        LaunchEndWaitSeconds = Mathf.Max(0f, timings.launchEndWaitSeconds);
        LaunchEndFadeSeconds = Mathf.Max(0f, timings.launchEndFadeSeconds);
        _walkSeconds = new float[walkSeconds != null ? walkSeconds.Count : 0];
        for (int i = 0; i < _walkSeconds.Length; i++)
            _walkSeconds[i] = walkSeconds[i] > 0f ? walkSeconds[i] : 0f; // NaN 도 0

        // 암전이 레터박스보다 길면 밝아진 뒤에 걷는다 — 걷는 모습이 검은 화면에 묻히지 않게.
        WalkReadyAt = Mathf.Max(LetterboxInSeconds, BlackoutSeconds);
        // 가장 늦게 도착하는 사람 기준(경로 길이가 달라 슬롯 순서와 다를 수 있다).
        SeatedSeconds = 0f;
        for (int slot = 0; slot < WalkerCount; slot++)
            SeatedSeconds = Mathf.Max(SeatedSeconds, slot * WalkInterval + _walkSeconds[slot] + SeatSlideSeconds);
    }

    /// <summary>탑승 시작 시각 = 해치 열림 이벤트와 <see cref="WalkReadyAt"/> 중 늦은 쪽. 이벤트 전이면 +∞.</summary>
    public double BoardAt(double hatchOpenedAt) => Math.Max(WalkReadyAt, hatchOpenedAt);

    /// <summary>발사 클립 재생 시작 = 가장 늦게 좌석에 도착한 순간. 탑승 전이면 +∞.</summary>
    public double LaunchAt(double boardAt) => boardAt + SeatedSeconds;

    /// <summary>끝 페이드 아웃 시작 = 발사 클립 끝 이벤트 + 끝 대기. 이벤트 전이면 +∞.</summary>
    public double EndFadeAt(double launchEndedAt) => launchEndedAt + LaunchEndWaitSeconds;

    /// <summary>로딩 시작 = 끝 페이드가 다 검어진 순간(발사 클립 끝 이벤트 + 대기 + 페이드). 서버가 이 시각에 로딩을 건다. 이벤트 전이면 +∞.</summary>
    public double LoadAt(double launchEndedAt) => launchEndedAt + LoadAfterLaunchEndSeconds;

    /// <summary>슬롯(0 = P1)이 걷기 시작하는 경과 시각(초).</summary>
    public double WalkStartAt(int slot, double boardAt) => boardAt + Mathf.Max(0, slot) * WalkInterval;

    /// <summary>슬롯의 보행 시간(초, 걷기 시작 지점 → 문턱). 명단 밖 슬롯은 0.</summary>
    public float WalkSecondsOf(int slot) => slot >= 0 && slot < _walkSeconds.Length ? _walkSeconds[slot] : 0f;

    /// <summary>슬롯이 문턱에 닿는 경과 시각(초) — 여기부터 좌석으로 미끄러진다.</summary>
    public double WalkArriveAt(int slot, double boardAt) => WalkStartAt(slot, boardAt) + WalkSecondsOf(slot);

    /// <summary>슬롯이 좌석에 도착하는 경과 시각(초) — 이 순간 숨긴다.</summary>
    public double WalkEndAt(int slot, double boardAt) => WalkArriveAt(slot, boardAt) + SeatSlideSeconds;

    /// <summary>슬롯의 보행 진행률(0 = 걷기 시작 지점, 1 = 문턱). 선형 — 걷는 속도가 일정하다.</summary>
    public float WalkProgressAt(int slot, double elapsed, double boardAt) =>
        PodDropTimeline.Progress(elapsed, WalkStartAt(slot, boardAt), WalkSecondsOf(slot));

    /// <summary>
    /// 발사 클립 안 재생 위치(초, 1배속 클립 시각) — 발사 시작 <paramref name="launchAt"/> 부터 1배속, 가속 1단계 이벤트 <paramref name="fastAt"/> 부터 <paramref name="fastSpeed"/> 배속(할 일 106 추가 2),
    /// 가속 2단계 이벤트 <paramref name="fast2At"/> 부터 <paramref name="fast2Speed"/> 배속(할 일 121). 늦게 본 피어가 따라잡을 위치.
    /// 이벤트 전(+∞)이면 그 앞 구간 배율 그대로, 발사 전이면 0. 배율 0 이하는 1. 시각이 발사보다 앞이면 발사부터, 2단계가 1단계보다 앞이면 1단계 구간은 없다.
    /// </summary>
    public static float LaunchClipSecondsAt(double elapsed, double launchAt, double fastAt, float fastSpeed,
        double fast2At = PodDropTimeline.NotYet, float fast2Speed = 1f)
    {
        if (elapsed <= launchAt) return 0f;
        if (fastSpeed <= 0f) fastSpeed = 1f;
        if (fast2Speed <= 0f) fast2Speed = 1f;
        double from2 = Math.Max(fast2At, launchAt);
        double from1 = Math.Min(Math.Max(fastAt, launchAt), from2);
        if (elapsed < from1) return (float)(elapsed - launchAt);
        double clip = from1 - launchAt;
        if (elapsed < from2) return (float)(clip + (elapsed - from1) * fastSpeed);
        return (float)(clip + (from2 - from1) * fastSpeed + (elapsed - from2) * fast2Speed);
    }

    /// <summary>경로 길이(m) ÷ 걷는 속도(m/s) = 보행 시간(초). 길이·속도가 0 이하(또는 NaN)면 0 — 무한·NaN 시간표를 만들지 않는다.</summary>
    public static float WalkSecondsFor(float pathLength, float walkSpeed) =>
        pathLength > 0f && walkSpeed > 0f ? pathLength / walkSpeed : 0f;

    /// <summary>
    /// 레터박스 알파(0 = 없음, 1 = 다 덮임). 들어오는 구간은 <see cref="LetterboxInEase"/>(기본 선형), 그 뒤 1 유지 — 끝 페이드 아래로 덮인 채 로비 씬과 함께 사라진다.
    /// UI 숨김도 같은 값으로 본다(&gt; 0 이면 숨김).
    /// </summary>
    public float LetterboxAlphaAt(double elapsed)
    {
        if (elapsed < 0d) return 0f;
        return Easing.Evaluate(LetterboxInEase, PodDropTimeline.Progress(elapsed, 0f, LetterboxInSeconds));
    }

    /// <summary>카메라 거리(평소 거리 배율). 줌 시작 지연까지 시작 거리 → 줌 끝 끝 거리, 그 뒤 유지.</summary>
    public float CameraDistanceAt(double elapsed) =>
        PodDropTimeline.CameraDistance(elapsed, CameraStartDistance, CameraEndDistance, CameraZoomDelaySeconds, CameraZoomSeconds, CameraZoomEase);

    /// <summary>
    /// 시작 암전 알파 — t=0 부터 검어지기 동안 0 → 1, 유지 동안 1(<see cref="TeleportAt"/> 가 이 한가운데), 밝아지기 동안 1 → 0, 그 뒤 0(선형).
    /// 세 구간이 모두 0 이면 늘 0. 끝 페이드는 따로(<see cref="EndFadeAlphaAt"/>) — 화면에는 둘 중 큰 값(<see cref="ScreenFadeAlphaAt"/>).
    /// </summary>
    public float BlackoutAlphaAt(double elapsed)
    {
        float fadeIn = PodDropTimeline.Progress(elapsed, 0f, BlackoutFadeInSeconds);
        float fadeOut = 1f - PodDropTimeline.Progress(elapsed, BlackoutFadeInSeconds + BlackoutHoldSeconds, BlackoutFadeOutSeconds);
        return Mathf.Min(fadeIn, fadeOut);
    }

    /// <summary>
    /// 끝 페이드 아웃 알파(할 일 121) — 발사 클립 끝 이벤트(<paramref name="launchEndedAt"/>, 서버 확정·복제) 뒤 대기 동안 0, 페이드 구간 0 → 1(선형), 그 뒤 1 유지
    /// (로딩 화면이 그 위에 뜨고 로비 씬과 함께 사라진다). 이벤트 전(+∞)이면 0. 페이드 0 = 대기 끝 순간 곧바로 1.
    /// </summary>
    public float EndFadeAlphaAt(double elapsed, double launchEndedAt) =>
        PodDropTimeline.Progress(elapsed, EndFadeAt(launchEndedAt), LaunchEndFadeSeconds);

    /// <summary>검은 화면 알파 = 시작 암전·끝 페이드 중 큰 값 — 같은 검은 화면 한 장(<see cref="PodDropScreenOverlay"/>)에 넣는다.</summary>
    public float ScreenFadeAlphaAt(double elapsed, double launchEndedAt) =>
        Mathf.Max(BlackoutAlphaAt(elapsed), EndFadeAlphaAt(elapsed, launchEndedAt));
}

/// <summary>
/// 맵 포드 착지 연출의 순수 시간표(할 일 100 C3 → 106) — 경과 시간 → 레터박스 알파·카메라 거리·포드 높이·디졸브 값.
/// 경과 시간은 로딩 화면이 걷힌 시각 기준(음수 = 시작 전 — 로딩 화면 아래). 레터박스는 처음부터 덮여 있다(106 추가 2).
/// 대기 → 빠른 낙하 → 급감속(날개·다리 펴기 시작) → 연출 높이 도착까지는 시각이 정해져 있고,
/// 그 뒤는 서버가 받은 클립 이벤트 시각(펴기 끝 <c>deployedAt</c>, 열기 끝 <c>openedAt</c>)을 넣어 계산한다. 아직 안 온 이벤트는 <see cref="PodDropTimeline.NotYet"/>(+∞).
/// 열기 끝 → (지연) → 디졸브 시작 = 플레이어 보임·레터박스 아웃 시작 → 디졸브 끝 = 조작·실루엣·카메라 해제(<see cref="ReleaseAt"/>) → 디졸브·레터박스 둘 다 끝 = UI·튜토리얼(<see cref="EndAt"/>).
/// </summary>
public sealed class PodDropMapTimeline
{
    public float HoldSeconds { get; }
    /// <summary>도착 페이드인 길이(초, t=0 시작) — 다른 단계와 겹쳐 흐른다. 0 = 검은 화면 없음.</summary>
    public float FadeInSeconds { get; }
    public Ease FadeInEase { get; }
    public float CameraStartDistance { get; }
    public float CameraEndDistance { get; }
    public float CameraZoomDelaySeconds { get; }
    public float CameraZoomSeconds { get; }
    public Ease CameraZoomEase { get; }
    /// <summary>카메라 시작 지점 = 착지점 위 (<see cref="HoverHeight"/> + 이 값) m.</summary>
    public float CameraStartHeightOffset { get; }
    public float FallHeight { get; }
    /// <summary>날개·다리 연출 높이(m) = 급감속이 끝나는 높이 — 낙하 시작 높이보다 높지 않다.</summary>
    public float HoverHeight { get; }
    /// <summary>급감속 시작 높이(m) = 연출 높이 + 급감속 거리 — 연출 높이 ~ 낙하 시작 높이 안.</summary>
    public float BrakeHeight { get; }
    public float FallSeconds { get; }
    public Ease FallEase { get; }
    public float BrakeSeconds { get; }
    public Ease BrakeEase { get; }
    public float LandSeconds { get; }
    public Ease LandEase { get; }
    public float DissolveDelaySeconds { get; }
    public float DissolveSeconds { get; }
    public float LetterboxOutSeconds { get; }
    public Ease LetterboxOutEase { get; }

    /// <summary>낙하 시작 = 대기 끝.</summary>
    public float FallAt { get; }
    /// <summary>급감속 시작 = 날개·다리 펴기 클립 시작 = 카메라가 포드를 따라가기 시작.</summary>
    public float BrakeAt { get; }
    /// <summary>날개·다리 연출 높이 도착(급감속 끝) — 펴기 끝 이벤트가 이보다 일찍 와도 착륙은 여기부터.</summary>
    public float HoverAt { get; }
    /// <summary>열기 끝 이벤트 → 조작·실루엣·카메라 해제(초) = 디졸브 지연 + 디졸브.</summary>
    public float ReleaseAfterOpenSeconds => DissolveDelaySeconds + DissolveSeconds;
    /// <summary>열기 끝 이벤트 → 연출 끝(초, UI·튜토리얼) = 디졸브 지연 + 디졸브·레터박스 아웃 중 긴 쪽.</summary>
    public float EndAfterOpenSeconds => DissolveDelaySeconds + Mathf.Max(DissolveSeconds, LetterboxOutSeconds);

    public PodDropMapTimeline(PodDropMapTimings timings)
    {
        HoldSeconds = Mathf.Max(0f, timings.holdSeconds);
        FadeInSeconds = Mathf.Max(0f, timings.fadeInSeconds);
        FadeInEase = timings.fadeInEase;
        CameraStartDistance = timings.cameraStartDistance;
        CameraEndDistance = timings.cameraEndDistance;
        CameraZoomDelaySeconds = Mathf.Max(0f, timings.cameraZoomDelaySeconds);
        CameraZoomSeconds = Mathf.Max(0f, timings.cameraZoomSeconds);
        CameraZoomEase = timings.cameraZoomEase;
        CameraStartHeightOffset = timings.cameraStartHeightOffset;
        FallHeight = Mathf.Max(0f, timings.fallHeight);
        HoverHeight = Mathf.Clamp(timings.hoverHeight, 0f, FallHeight);
        BrakeHeight = Mathf.Clamp(HoverHeight + Mathf.Max(0f, timings.brakeDistance), HoverHeight, FallHeight);
        FallSeconds = Mathf.Max(0f, timings.fallSeconds);
        FallEase = timings.fallEase;
        BrakeSeconds = Mathf.Max(0f, timings.brakeSeconds);
        BrakeEase = timings.brakeEase;
        LandSeconds = Mathf.Max(0f, timings.landSeconds);
        LandEase = timings.landEase;
        DissolveDelaySeconds = Mathf.Max(0f, timings.dissolveDelaySeconds);
        DissolveSeconds = Mathf.Max(0f, timings.dissolveSeconds);
        LetterboxOutSeconds = Mathf.Max(0f, timings.letterboxOutSeconds);
        LetterboxOutEase = timings.letterboxOutEase;

        FallAt = HoldSeconds;
        BrakeAt = FallAt + FallSeconds;
        HoverAt = BrakeAt + BrakeSeconds;
    }

    /// <summary>착륙 시작 = 펴기 끝 이벤트와 연출 높이 도착 중 늦은 쪽. 이벤트 전이면 +∞.</summary>
    public double LandStartAt(double deployedAt) => Math.Max(deployedAt, HoverAt);

    /// <summary>지면에 닿는 시각 = 착륙 시작 + 착륙 시간. 착지 이펙트·흔들림, 해치 열기·날개 접기 클립 시작. 이벤트 전이면 +∞.</summary>
    public double TouchdownAt(double deployedAt) => LandStartAt(deployedAt) + LandSeconds;

    /// <summary>디졸브 시작 = 열기 끝 이벤트 + 지연 = 플레이어가 보이고(실루엣은 아직) 레터박스가 걷히기 시작하는 시각.</summary>
    public double DissolveAt(double openedAt) => openedAt + DissolveDelaySeconds;

    /// <summary>레터박스 아웃 시작 = 디졸브 시작.</summary>
    public double LetterboxOutAt(double openedAt) => DissolveAt(openedAt);

    /// <summary>디졸브 끝 = 조작·실루엣 해제, 카메라가 플레이어로 넘어가는 시각.</summary>
    public double ReleaseAt(double openedAt) => openedAt + ReleaseAfterOpenSeconds;

    /// <summary>조작·실루엣·카메라를 돌려줬는가 — 디졸브가 끝난 뒤에만 true.</summary>
    public bool IsReleasedAt(double elapsed, double openedAt) => elapsed >= ReleaseAt(openedAt);

    /// <summary>연출 끝(디졸브·레터박스 아웃 둘 다 끝) = UI·튜토리얼(S0).</summary>
    public double EndAt(double openedAt) => openedAt + EndAfterOpenSeconds;

    public bool IsEndedAt(double elapsed, double openedAt) => elapsed >= EndAt(openedAt);

    /// <summary>
    /// 레터박스 알파(0 = 없음, 1 = 다 덮임). 진입 때부터(로딩 화면 아래 포함) 1 → 디졸브 시작부터 레터박스 아웃 구간 1 → 0(<see cref="LetterboxOutEase"/>).
    /// UI 숨김은 레터박스와 따로 연출 내내 한다.
    /// </summary>
    public float LetterboxAlphaAt(double elapsed, double openedAt) =>
        1f - Easing.Evaluate(LetterboxOutEase, PodDropTimeline.Progress(elapsed, LetterboxOutAt(openedAt), LetterboxOutSeconds));

    /// <summary>
    /// 도착 페이드인 검은 화면 알파(1 = 완전 암전, 0 = 투명). t=0 전 1(로딩 화면 아래), 페이드인 구간 1 → 0(<see cref="FadeInEase"/>), 그 뒤 0.
    /// 길이 0 이면 늘 0 — 검은 화면 없이 로딩 화면이 걷히자마자 맵이 보인다.
    /// </summary>
    public float FadeAlphaAt(double elapsed) =>
        FadeInSeconds > 0f ? 1f - Easing.Evaluate(FadeInEase, PodDropTimeline.Progress(elapsed, 0f, FadeInSeconds)) : 0f;

    /// <summary>카메라 거리(평소 거리 배율). 줌 시작 지연까지 시작 거리 → 줌 끝 끝 거리, 그 뒤 유지.</summary>
    public float CameraDistanceAt(double elapsed) =>
        PodDropTimeline.CameraDistance(elapsed, CameraStartDistance, CameraEndDistance, CameraZoomDelaySeconds, CameraZoomSeconds, CameraZoomEase);

    /// <summary>
    /// 포드 높이(착지점 기준, m — 착지 여유 높이는 호출자가 더한다). 낙하 전 <see cref="FallHeight"/> → 빠른 낙하(<see cref="FallEase"/>) → <see cref="BrakeHeight"/>
    /// → 급감속(<see cref="BrakeEase"/>) → <see cref="HoverHeight"/>(펴기 끝 이벤트가 아직이면 여기서 기다림) → 착륙(<see cref="LandEase"/>) → 0.
    /// </summary>
    public float PodHeightAt(double elapsed, double deployedAt)
    {
        if (elapsed < BrakeAt)
            return Mathf.LerpUnclamped(FallHeight, BrakeHeight, Easing.Evaluate(FallEase, PodDropTimeline.Progress(elapsed, FallAt, FallSeconds)));
        double landStart = LandStartAt(deployedAt);
        if (elapsed < landStart)
            return Mathf.LerpUnclamped(BrakeHeight, HoverHeight, Easing.Evaluate(BrakeEase, PodDropTimeline.Progress(elapsed, BrakeAt, BrakeSeconds)));
        return HoverHeight * (1f - Easing.Evaluate(LandEase, PodDropTimeline.Progress(elapsed, landStart, LandSeconds)));
    }

    /// <summary>
    /// 카메라가 보는 지점 높이(착지점 기준, m). 급감속 전 = <see cref="HoverHeight"/> + <see cref="CameraStartHeightOffset"/>(고정),
    /// 급감속 시작부터 = 포드 높이(<see cref="PodHeightAt"/> — 포드를 따라간다).
    /// </summary>
    public float CameraFocusHeightAt(double elapsed, double deployedAt) =>
        elapsed < BrakeAt ? HoverHeight + CameraStartHeightOffset : PodHeightAt(elapsed, deployedAt);

    /// <summary>디졸브 진행률(0 = 포드 온전, 1 = 다 사라짐). 선형. 셰이더 값 변환(예: DissolveFx `_Cutoff` = 1 − 이 값)은 호출자가 한다.</summary>
    public float DissolveProgressAt(double elapsed, double openedAt) =>
        PodDropTimeline.Progress(elapsed, DissolveAt(openedAt), DissolveSeconds);
}

/// <summary>로비·맵 포드 시간표가 같이 쓰는 계산.</summary>
public static class PodDropTimeline
{
    /// <summary>아직 오지 않은 클립 이벤트 시각(경과 초) — 그 뒤 시각은 모두 +∞ 라 어떤 경과 시간도 닿지 않는다.</summary>
    public const double NotYet = double.PositiveInfinity;

    /// <summary>복제된 이벤트 서버시각(0 = 아직) → 시작 기준 경과 초(아직이면 <see cref="NotYet"/>).</summary>
    public static double EventElapsed(double eventServerTime, double startServerTime) =>
        eventServerTime > 0d ? eventServerTime - startServerTime : NotYet;

    /// <summary>구간 진행률(0~1, 선형). 길이 0 이면 시작 시각부터 1. 시작이 +∞ 면 0.</summary>
    public static float Progress(double elapsed, double start, float duration)
    {
        if (duration <= 0f) return elapsed >= start ? 1f : 0f;
        if (double.IsPositiveInfinity(start)) return 0f;
        return Mathf.Clamp01((float)((elapsed - start) / duration));
    }

    /// <summary><paramref name="delay"/> 까지 <paramref name="from"/> 유지 → 그로부터 <paramref name="seconds"/> 뒤 <paramref name="to"/>(이징), 그 뒤 유지.</summary>
    public static float CameraDistance(double elapsed, float from, float to, float delay, float seconds, Ease ease) =>
        Mathf.LerpUnclamped(from, to, Easing.Evaluate(ease, Progress(elapsed, delay, seconds)));
}
