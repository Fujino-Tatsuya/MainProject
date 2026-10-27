using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 로비 포드 탑승·강하 연출(할 일 100 C2, 3.LobbyScene — <see cref="LobbyTrainingDirector"/> 와 같은 씬 상주 NetworkObject 에 붙는다).
/// 출격 집결 확정 → <see cref="LobbyTrainingDirector"/> 가 맵 로딩 대신 <see cref="TryBeginServer"/> 에 넘긴다 → 발사 클립 끝 이벤트에 그 콜백으로 곧바로 로딩(페이드 없음).
/// <list type="bullet">
/// <item>복제: 시작 시각(서버시간), 참가 명단(플레이어 NetworkObjectId, 보행 슬롯 순서), 슬롯별 보행 시간(서버가 경로 길이 ÷ 걷는 속도로 계산),
/// 해치 열림 시각(서버시간 — 호스트 발사대의 해치 클립 이벤트, 할 일 106), 발사 클립 가속 시각(호스트 가속 이벤트 — 그 뒤 발사 클립 배율 재생, 106 추가 2). 모든 피어가 이 값으로 <see cref="PodDropLobbyTimeline"/> 을 같이 계산한다. RPC 없음.</item>
/// <item>서버: 시작 순간 참가자 연출 잠금(<see cref="PlayerEncounterLock.BeginCinematicServer"/> — 입력·피해·낙사·사망 기록 차단),
/// 호스트 발사대 Animator 의 클립 이벤트(<see cref="PodAnimEventRelay"/>)로 단계를 확정한다 — 해치 이벤트 → 해치 열림 시각 복제(탑승 시작),
/// 슬롯마다 포드 안 도착 시각에 숨김(<see cref="PlayerEncounterLock.SetCinematicHiddenServer"/>), 발사 클립 끝 이벤트 → 콜백(로딩).
/// 이벤트가 빠진 클립은 끝까지 돈 것으로 대신하고 경고한다. 연출 중 끊긴 사람은 건너뛰고 계속한다. 잠금·숨김은 로딩이 플레이어를 디스폰할 때 같이 사라진다.</item>
/// <item>오너: 시작 직후 짧은 암전의 유지 구간 한가운데(<see cref="PodDropLobbyTimeline.TeleportAt"/>) 자기 걷기 시작 지점(WalkStart, 슬롯 순서)으로
/// 순간이동한다(보스 엘리베이터 TeleportOwnerClientRpc 의 오너 처리와 같은 순서). 해치가 열리면(복제) 자기 슬롯 보행 구간 동안 그 자리 → 개인 경로점(슬롯 entryPath) → 공용 경로점(walkPath, 슬롯 합류 번호부터 리스트 순서)을
/// 평소 걷기 변위(Walk 애니)로 걷고 → 문턱까지 같은 속도로, 문턱 → 좌석은 미끄러짐 시간 동안 충돌 없이 보간한다(포드 실내 1.42m &lt; 캡슐 1.79m). 도착 뒤 좌석에 붙잡아 둔다.</item>
/// <item>전 피어(로컬 표시): 레터박스·UI 숨김·시작 암전(<see cref="PodDropScreenOverlay"/>), 카메라 임시 추적(포드 카메라 지점) + 거리 배율 줌인,
/// 발사대 Animator 1배속 재생(<see cref="PodAnimatorDriver"/>) — 순간이동 순간 해치 클립, 해치 이벤트 프레임에서 멈춤, 가장 늦게 앉는 순간 발사 클립(해치 닫힘·갱도 문·포드 하강) 처음부터 끝까지.
/// 클라 Animator 는 재생만 한다 — 자기 이벤트로는 해치 자세를 멈출 뿐 단계를 넘기지 않는다.</item>
/// </list>
/// 수치(레터박스 높이·걷는 속도 포함)는 <see cref="PodDropSettings"/>, 위치는 씬 Transform(LobbyTraining/PodLaunch) 이 원본이다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyPodLaunch : NetworkBehaviour, ISerializationCallbackReceiver
{
    private const string Tag = "[LobbyPod]";

    [Tooltip("포드 강하 수치(시간표). 비우면 연출 없이 곧바로 맵 로딩.")]
    [SerializeField] private PodDropSettings settings;

    [Header("씬 지점")]
    [Tooltip("연출 동안 카메라가 따라가는 지점(포드 쪽). 비우면 카메라는 플레이어를 따라간 채 줌만 한다.")]
    [SerializeField] private Transform cameraFocus;

    /// <summary>한 사람 몫 걷기 시작 — 시작 지점 + 개인 경로 + 공용 경로 합류 번호(할 일 100 C2e).</summary>
    [Serializable]
    public sealed class WalkSlot
    {
        [Tooltip("걷기 시작 지점 — 시작 암전 한가운데 여기로 순간이동한 뒤 걷는다.")]
        public Transform start;

        [Tooltip("개인 경로점 — 시작 지점에서 공용 경로에 합류하기 전까지 이 순서로 걷는다(충돌 켬). 빈 칸은 건너뛴다. 비우면 곧바로 합류점으로.")]
        public List<Transform> entryPath = new List<Transform>();

        [Tooltip("합류할 공용 경로점 칸 번호(0부터 — 0 = #1). 공용 경로점 칸 수와 같으면 공용 경로 없이 문턱으로. 범위 밖이면 맞추고 경고한다.")]
        [Min(0)] public int joinIndex;
    }

    [Tooltip("걷기 시작 슬롯 — 슬롯 순서(P1, P2, P3). 사람마다 시작 지점 → 개인 경로점 → 공용 경로점(합류 번호부터) → 문턱 → 좌석으로 걷는다. " +
             "사람이 더 많으면 돌려 쓴다(시작 지점이 빈 슬롯은 뺀다). 시작 지점이 하나도 없으면 연출 없이 곧바로 맵 로딩.")]
    [SerializeField] private WalkSlot[] walkSlots = Array.Empty<WalkSlot>();

    // C2e 이전 씬의 걷기 시작 지점 배열 — 읽은 직후 walkSlots 로 옮기고 비운다(OnAfterDeserialize). 새로 쓰지 않는다.
    [FormerlySerializedAs("walkStarts")]
    [SerializeField, HideInInspector] private Transform[] legacyWalkStarts = Array.Empty<Transform>();

    [Tooltip("충돌을 켠 채 걷는 공용 경로점 — 리스트 순서대로 걷는다(이름은 표시용). 각자 개인 경로 끝에서 슬롯의 합류 번호 칸으로 합류한다. " +
             "빈 칸은 건너뛴다. 비면 걷기 시작 지점(개인 경로)에서 곧바로 문턱으로 보간한다. 씬 뷰 편집은 이 컴포넌트 인스펙터의 버튼으로.")]
    [FormerlySerializedAs("walkWaypoints")]
    [SerializeField] private List<Transform> walkPath = new List<Transform>();

    [Tooltip("문턱 — 마지막 경로점부터 여기를 거쳐 좌석까지 충돌 없이 보간한다(포드 실내 높이 1.42m 라 캡슐이 막힌다). 비우면 마지막 경로점에서 곧바로 좌석으로.")]
    [SerializeField] private Transform threshold;

    [Tooltip("포드 안 좌석(숨김 지점) — 슬롯 순서(P1, P2, P3). 사람이 더 많으면 돌려 쓴다.")]
    [SerializeField] private Transform[] seats = Array.Empty<Transform>();

    [Header("발사대")]
    [Tooltip("발사대 Animator(PF_DropPod_LaunchStation_Combined_v001 루트). 연출 동안 해치·발사 클립을 1배속으로 재생하고 클립 이벤트로 단계를 넘긴다(포드 하강도 클립이 한다). 비우면 애니 없이 곧바로 넘어간다.")]
    [SerializeField] private Animator launcher;

    // 연출 시작 시각(서버시간). 0 = 연출 없음. 서버 write / 모두 read.
    private readonly NetworkVariable<double> _startAt = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 참가자 플레이어 NetworkObjectId — 보행 슬롯 순서. 시작 시각보다 먼저 쓴다. 서버 write / 모두 read.
    private readonly NetworkList<ulong> _walkers = new NetworkList<ulong>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 슬롯별 보행 시간(초, 걷기 시작 지점 → 문턱) — 서버가 경로 길이 ÷ 걷는 속도로 계산한 값. 명단과 같은 순서·같은 때(시작 시각보다 먼저) 쓴다.
    // 피어가 각자 경로를 재지 않고 이 값으로 시간표를 만든다 — 서버 숨김 시각과 오너 도착 시각이 늘 같다. 서버 write / 모두 read.
    private readonly NetworkList<float> _walkSeconds = new NetworkList<float>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 해치 열림 시각(서버시간) — 호스트 발사대의 해치 클립 이벤트를 서버가 확정한 순간. 0 = 아직. 탑승(보행)은 이 뒤에 시작한다. 서버 write / 모두 read.
    private readonly NetworkVariable<double> _hatchOpenedAt = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 발사 클립 가속 시각(서버시간) — 호스트 발사대의 가속 클립 이벤트(PodClipEvent 종류 1, 기본 294프레임)를 서버가 확정한 순간. 0 = 아직.
    // 클라는 자기 이벤트로 곧바로 가속하고, 이벤트를 놓쳤거나 늦게 본 피어는 이 시각으로 클립 위치를 계산해 따라잡는다(할 일 106 추가 2). 서버 write / 모두 read.
    private readonly NetworkVariable<double> _launchFastAt = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private static double ServerNow =>
        NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : 0d;

    // 서버 전용 — 연출 한 번의 진행 상태(null = 연출 없음)
    private sealed class ServerRun
    {
        public List<ulong> Participants;
        public PodDropLobbyTimeline Timeline;
        public double Start;
        public Action OnDone;
        public bool[] Hidden;
        public PodClipGate HatchGate;
        public PodClipGate LaunchGate;
    }

    private ServerRun _run;
    private bool _loadIssued;
    private readonly List<ulong> _lockedClientIds = new List<ulong>();

    // 전 피어(로컬 표시)
    private double _seenStart;
    private PodDropLobbyTimeline _timeline;
    private PodDropScreenOverlay _overlay;
    private CameraTargetSwitcher _heldCamera;
    private PodAnimatorDriver _launcherDriver;
    private bool _hatchHeldOnBoard;
    private bool _launchFast;

    // 오너 보행
    private struct OwnerWalk
    {
        public bool Active;
        public double Start;          // 어느 연출의 보행인가 — 시작 시각이 바뀌면 버린다
        public int Slot;
        public float GroundShare;     // 보행 시간 중 충돌을 켠 앞 구간 비율(길이 비율)
        public Vector3 WalkStart;     // 걷기 시작 지점(순간이동 목적지)
        public double TeleportAt;     // 서버시간 — 암전 유지 구간 한가운데
        public bool Teleported;
        public bool Timed;            // 해치 열림(복제)을 받아 아래 보행 시각을 채웠다
        public bool Started;
        public Vector3[] Ground;      // 충돌을 켠 채 걷는 경유 지점(수평만 쓴다 — 높이는 지면)
        public int GroundIndex;
        public Vector3[] Approach;     // 충돌 없이 걷는 마지막 구간 — 문턱(없으면 비었다)
        public Vector3 Seat;
        public double WalkStartAt;    // 서버시간
        public double GroundEndAt;    // 보행 시간을 길이 비율로 나눈 앞 구간 끝
        public double ArriveAt;       // 문턱 도착 — 여기부터 좌석으로 미끄러진다
        public double WalkEndAt;      // 좌석 도착
        public bool InApproach;
        public Vector3 ApproachFrom;
        public bool InSlide;
        public Vector3 SlideFrom;
        public bool Arrived;
    }

    private OwnerWalk _walk;

    /// <summary>[전 피어] 연출 중인가(시작 시각이 복제돼 있다).</summary>
    public bool IsRunning => IsSpawned && _startAt.Value > 0d;

    void ISerializationCallbackReceiver.OnBeforeSerialize() { }

    // C2e 이전 씬(walkStarts 배열)을 슬롯으로 옮긴다 — 개인 경로 없음·합류 0 = 지금까지와 같은 동작. 씬을 저장하면 새 형식만 남는다.
    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
        if (legacyWalkStarts == null || legacyWalkStarts.Length == 0)
            return;

        if (walkSlots == null || walkSlots.Length == 0)
        {
            walkSlots = new WalkSlot[legacyWalkStarts.Length];
            for (int i = 0; i < walkSlots.Length; i++)
                walkSlots[i] = new WalkSlot { start = legacyWalkStarts[i] };
        }

        legacyWalkStarts = Array.Empty<Transform>();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            _run = null;
            // 로딩 전에 끝났다(씬 언로드·호스트 셧다운) — 건 잠금·숨김을 푼다. 로딩을 넘긴 뒤면 플레이어 디스폰이 함께 정리한다.
            if (!_loadIssued) ReleaseLocks("디스폰(로딩 전)");
        }

        ClearLocal();
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        ClearLocal();
        base.OnDestroy();
    }

    // ---------------- 서버 ----------------

    /// <summary>
    /// [서버] 포드 연출 시작 — 발사 클립 끝 이벤트에 <paramref name="onDone"/>(맵 로딩)을 부른다. 설정·좌석·참가자가 없으면 false(호출자가 곧바로 로딩).
    /// 참가자 기록·시작 시각 복제·연출 잠금은 이 호출 안(같은 프레임)에서 끝낸다.
    /// </summary>
    public bool TryBeginServer(Action onDone)
    {
        if (!IsServer || !IsSpawned)
            return false;

        if (_startAt.Value > 0d)
        {
            Debug.LogWarning($"{Tag} 이미 연출 중 — 중복 시작 무시.", this);
            return true;
        }

        List<ulong> participants = CollectParticipants();
        List<WalkSlot> slots = ActiveSlots();
        string missing = PodLaunchRules.MissingSetup(settings != null, CountAssigned(seats), slots.Count, participants.Count);
        if (missing != null)
        {
            Debug.LogWarning($"{Tag} {missing} — 포드 연출 없이 곧바로 맵 로딩.", this);
            return false;
        }

        for (int i = 0; i < slots.Count; i++)
            WarnRoute(slots[i], BuildRoute(slots[i], Vector3.zero));

        float[] walkSeconds = WalkSecondsPerSlot(participants.Count, settings);
        PodDropLobbyTimeline timeline = settings.CreateLobbyTimeline(walkSeconds);
        double start = NetworkManager.ServerTime.Time;

        // 명단·보행 시간을 시작 시각보다 먼저 쓴다 — 피어가 시작 시각을 보는 순간 시간표도 맞아 있게.
        _walkers.Clear();
        foreach (ulong clientId in participants)
            _walkers.Add(NetworkManager.ConnectedClients[clientId].PlayerObject.NetworkObjectId);
        _walkSeconds.Clear();
        foreach (float seconds in walkSeconds)
            _walkSeconds.Add(seconds);
        _hatchOpenedAt.Value = 0d;
        _launchFastAt.Value = 0d;
        _startAt.Value = start;
        _loadIssued = false;
        LockParticipants(participants);

        PodDropLobbyTimings lobby = settings.Lobby;
        _run = new ServerRun
        {
            Participants = participants,
            Timeline = timeline,
            Start = start,
            OnDone = onDone,
            Hidden = new bool[participants.Count],
            HatchGate = new PodClipGate(new[] { lobby.hatchState }),
            LaunchGate = new PodClipGate(new[] { lobby.launchState }),
        };

        Debug.Log($"[SceneFlow] {Tag} 포드 연출 시작 — 참가자 {participants.Count}명 [{string.Join(",", participants)}], " +
                  $"순간이동·해치 클립 +{timeline.TeleportAt:0.##}s(암전 {timeline.BlackoutFadeInSeconds:0.##}/{timeline.BlackoutHoldSeconds:0.##}/{timeline.BlackoutFadeOutSeconds:0.##}s), " +
                  $"탑승은 해치 이벤트 뒤(빨라도 +{timeline.WalkReadyAt:0.##}s, 간격 {timeline.WalkInterval:0.##}s, " +
                  $"속도 {lobby.walkSpeed:0.##}m/s, 각자 [{string.Join(",", Array.ConvertAll(walkSeconds, s => s.ToString("0.##")))}]s + 미끄러짐 {timeline.SeatSlideSeconds:0.##}s → 다 앉기 +{timeline.SeatedSeconds:0.##}s), " +
                  $"발사 클립 '{lobby.launchState}' 끝 이벤트에 로딩");
        return true;
    }

    // 클립 이벤트(호스트 발사대)는 애니메이터 갱신 중에 오므로 같은 프레임 LateUpdate 에서 단계를 넘긴다 — 이벤트가 빠진 클립의 '끝까지 돌았다' 예비 판정보다 이벤트가 먼저 적힌다.
    private void LateUpdate()
    {
        if (IsServer && IsSpawned && _run != null && !_loadIssued)
            UpdateServerRun(_run);
    }

    private void UpdateServerRun(ServerRun run)
    {
        PodDropLobbyTimeline timeline = run.Timeline;
        double elapsed = ServerNow - run.Start;

        // 1) 해치 열림 — 해치 클립 이벤트(또는 이벤트 없이 끝까지 돎). 발사대가 없으면 해치 클립 시작 순간 곧바로.
        if (_hatchOpenedAt.Value <= 0d)
        {
            if (elapsed < timeline.HatchAt || !GateOpen(run.HatchGate, "해치"))
                return;

            _hatchOpenedAt.Value = ServerNow;
            Debug.Log($"[SceneFlow] {Tag} 해치 열림 +{elapsed:0.##}s — 탑승 +{timeline.BoardAt(elapsed):0.##}s 부터");
        }

        double board = timeline.BoardAt(PodDropTimeline.EventElapsed(_hatchOpenedAt.Value, run.Start));

        // 2) 좌석에 도착한 사람부터 숨긴다 — 경로 길이가 달라 슬롯 순서와 도착 순서가 다를 수 있다.
        for (int slot = 0; slot < run.Participants.Count; slot++)
        {
            if (run.Hidden[slot] || elapsed < timeline.WalkEndAt(slot, board)) continue;
            run.Hidden[slot] = true;
            HideParticipant(run.Participants[slot]);
        }

        // 3) 발사 — 가장 늦게 앉은 순간 클립 시작(전 피어 UpdateLauncher), 끝 이벤트에 곧바로 로딩.
        if (elapsed < timeline.LaunchAt(board) || !GateOpen(run.LaunchGate, "발사"))
            return;

        _run = null;
        _loadIssued = true;
        Debug.Log($"[SceneFlow] {Tag} 발사 클립 끝 +{elapsed:0.##}s — 맵 로딩 시작(페이드 없음)");
        run.OnDone?.Invoke();
    }

    // 발사대가 없으면 애니 없이 곧바로 연다. 이벤트 없이 끝까지 돈 클립으로 연 경우 경고(사용자가 FBX 클립 이벤트를 지웠다).
    private bool GateOpen(PodClipGate gate, string label)
    {
        if (launcher == null || _launcherDriver == null)
            return launcher == null;

        if (!gate.IsOpen(_launcherDriver.IsFinished, out List<string> fallbacks))
            return false;

        if (fallbacks != null)
            Debug.LogWarning($"{Tag} {label} 클립 [{string.Join(",", fallbacks)}] 에 '{PodAnimEventRelay.FunctionName}' 이벤트가 없어 클립 끝에서 넘어간다 — " +
                             "FBX 임포트 창(Animation 탭) 또는 Tools/Lobby/포드 애니 이벤트 메뉴로 넣을 것.", this);
        return true;
    }

    // [전 피어] 발사대 클립 이벤트. 해치 이벤트는 모든 피어에서 그 자세로 멈추게 한다(표시). 단계 확정은 서버만.
    // 가속 이벤트는 모든 피어에서 곧바로 가속하고(표시), 서버는 그 시각을 복제한다 — 단계 문(해치·발사)에는 넣지 않는다.
    private void HandleLauncherClipEvent(string clipName, int kind)
    {
        if (settings == null)
            return;

        PodDropLobbyTimings lobby = settings.Lobby;
        if (kind == PodAnimEventRelay.KindSpeedUp)
        {
            if (clipName != lobby.launchState || !_launcherDriver.HasStarted(lobby.launchState) || _launchFast)
                return;

            _launchFast = true;
            _launcherDriver.SetSpeed(lobby.launchFastSpeed);
            if (IsServer && _run != null && _launchFastAt.Value <= 0d)
            {
                _launchFastAt.Value = ServerNow;
                Debug.Log($"[SceneFlow] {Tag} 발사 클립 가속 ×{lobby.launchFastSpeed:0.##} +{ServerNow - _run.Start:0.##}s");
            }
            return;
        }

        if (clipName == lobby.hatchState && !_launcherDriver.HasStarted(lobby.launchState))
            _launcherDriver.Hold();

        if (IsServer && _run != null)
        {
            _run.HatchGate.Notify(clipName);
            _run.LaunchGate.Notify(clipName);
        }
    }

    /// <summary>참가자 clientId(오름차순) — 플레이어 오브젝트가 있는 접속자 전원.</summary>
    private List<ulong> CollectParticipants()
    {
        var ids = new List<ulong>();
        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
                ids.Add(client.ClientId);
        }

        PodLaunchRules.OrderParticipants(ids);
        return ids;
    }

    private void LockParticipants(List<ulong> participants)
    {
        _lockedClientIds.Clear();
        foreach (ulong clientId in participants)
        {
            PlayerEncounterLock encounterLock = FindLock(clientId);
            if (encounterLock == null || !encounterLock.BeginCinematicServer())
                continue;

            // 🔴 걸려 있던 낙하 복구(안전지점으로 되돌리는 지연 코루틴)를 끊는다 — 안 끊으면 걷기 시작 지점으로 옮긴 뒤 옛 안전지점으로 끌려간다(엘리베이터와 같다).
            encounterLock.GetComponent<PlayerFallRecovery>()?.CancelRecoveryServer();

            _lockedClientIds.Add(clientId);
        }
    }

    private void HideParticipant(ulong clientId)
    {
        // 연출 중 끊긴 사람은 건너뛴다(남은 인원으로 계속).
        PlayerEncounterLock encounterLock = FindLock(clientId);
        if (encounterLock == null)
        {
            Debug.Log($"[SceneFlow] {Tag} client {clientId} 없음(연출 중 퇴장) — 숨김 건너뜀");
            return;
        }

        encounterLock.SetCinematicHiddenServer(true);
    }

    private void ReleaseLocks(string reason)
    {
        if (_lockedClientIds.Count == 0)
            return;

        foreach (ulong clientId in _lockedClientIds)
        {
            PlayerEncounterLock encounterLock = FindLock(clientId);
            if (encounterLock == null || !encounterLock.IsSpawned)
                continue;

            encounterLock.SetCinematicHiddenServer(false);
            encounterLock.EndCinematicServer();
        }

        Debug.Log($"[SceneFlow] {Tag} 연출 잠금 해제({reason}) — [{string.Join(",", _lockedClientIds)}]");
        _lockedClientIds.Clear();
    }

    private PlayerEncounterLock FindLock(ulong clientId)
    {
        NetworkManager nm = NetworkManager;
        if (nm == null || !nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null)
            return null;

        return client.PlayerObject.GetComponent<PlayerEncounterLock>();
    }

    // ---------------- 전 피어(로컬 표시) ----------------

    private void Update()
    {
        if (!IsSpawned)
            return;

        double start = _startAt.Value;
        if (start != _seenStart)
        {
            ClearLocal();
            _seenStart = start;
        }

        if (start <= 0d)
            return;

        if (settings == null)
            return;

        // 보행 시간 목록이 시작 시각보다 늦게 왔으면 그에 맞춰 다시 짠다(같은 시각부터 이어 그린다).
        if (_timeline == null || _timeline.WalkerCount != _walkSeconds.Count)
            _timeline = settings.CreateLobbyTimeline(ReplicatedWalkSeconds());

        double elapsed = ServerNow - start;
        UpdateLauncher(elapsed, _timeline.BoardAt(PodDropTimeline.EventElapsed(_hatchOpenedAt.Value, start)),
            PodDropTimeline.EventElapsed(_launchFastAt.Value, start));
        UpdateCamera(elapsed);

        if (_overlay == null) _overlay = gameObject.AddComponent<PodDropScreenOverlay>();
        _overlay.HudRescanInterval = settings.Lobby.hudRescanSeconds;
        // 끝 암전 없음 — 발사 클립 끝에서 곧바로 로딩 화면(정렬 1000, 숨김 예외)이 이 화면(500) 위에 뜨고, 레터박스는 로비 씬과 함께 사라진다.
        _overlay.Apply(_timeline.LetterboxAlphaAt(elapsed) * settings.LetterboxHeight, _timeline.BlackoutAlphaAt(elapsed), true);
    }

    // Base Layer(0) 에 전신 클립을 이어 재생한다 — 해치 클립을 마스크 레이어(Hatches)에 두면 그 자세가 발사 클립의 해치 닫힘을 덮는다.
    // 해치 클립은 순간이동 순간 처음부터, 이벤트 프레임에서 멈춤(이벤트 또는 서버 해치 열림 복제 중 먼저 온 것). 발사 클립은 가장 늦게 앉는 순간 처음부터 끝까지.
    // 발사 클립은 가속 이벤트(기본 294프레임)부터 배율로 돈다 — 각 피어는 자기 이벤트로 곧바로 가속하고, 이벤트를 놓친 피어는 서버 가속 시각(복제)을 받는 순간 그 위치로 옮겨 가속한다.
    // 늦게 처음 본 피어는 1배속 구간 + 가속 구간을 반영한 위치에서 시작한다(<see cref="PodDropLobbyTimeline.LaunchClipSecondsAt"/>).
    // 발사대 프리팹엔 콜라이더가 없다(바닥은 로비 레이아웃 PodLaunch_FloorCollider — 걷는 길이라 끄지 않는다).
    private void UpdateLauncher(double elapsed, double board, double fastAt)
    {
        if (launcher == null || elapsed < _timeline.HatchAt)
            return;

        if (_launcherDriver == null)
        {
            _launcherDriver = new PodAnimatorDriver(launcher);
            _launcherDriver.ClipEvent += HandleLauncherClipEvent;
        }

        PodDropLobbyTimings lobby = settings.Lobby;
        double launchAt = _timeline.LaunchAt(board);
        if (elapsed >= launchAt)
        {
            float clipSeconds = PodDropLobbyTimeline.LaunchClipSecondsAt(elapsed, launchAt, fastAt, lobby.launchFastSpeed);
            if (!_launcherDriver.HasStarted(lobby.launchState))
            {
                _launcherDriver.Play(lobby.launchState, 0, clipSeconds);
                if (elapsed >= fastAt)
                {
                    _launchFast = true;
                    _launcherDriver.SetSpeed(lobby.launchFastSpeed);
                }
            }
            else if (!_launchFast && elapsed >= fastAt)
            {
                // 자기 가속 이벤트를 놓친 피어 — 서버 가속 시각 기준 위치로 옮겨 가속한다.
                _launchFast = true;
                _launcherDriver.Seek(lobby.launchState, clipSeconds);
                _launcherDriver.SetSpeed(lobby.launchFastSpeed);
            }
            return;
        }

        if (!_launcherDriver.HasStarted(lobby.hatchState))
        {
            _launcherDriver.Play(lobby.hatchState, 0, (float)(elapsed - _timeline.HatchAt));
            // 해치가 이미 열린 뒤 처음 본 피어 — 따라잡은 자세로 멈춘다(지난 이벤트는 뜨지 않는다).
            if (_hatchOpenedAt.Value > 0d) _launcherDriver.Hold();
        }
        else if (_hatchOpenedAt.Value > 0d && !_hatchHeldOnBoard)
        {
            // 자기 이벤트를 놓친 클라(늦게 시작) — 서버 해치 열림을 받은 순간 그 자세로 멈춘다.
            _hatchHeldOnBoard = true;
            _launcherDriver.Hold();
        }
    }

    private void UpdateCamera(double elapsed)
    {
        CameraTargetSwitcher cameraSwitcher = CameraTargetSwitcher.Active;
        if (cameraSwitcher == null)
            return; // 리그가 늦게 붙을 수 있다 — 다음 프레임에 다시

        if (_heldCamera == null)
        {
            _heldCamera = cameraSwitcher;
            if (cameraFocus != null)
                cameraSwitcher.SetTemporaryFollow(cameraFocus);
        }

        cameraSwitcher.SetCinematicDistanceMultiplier(_timeline.CameraDistanceAt(elapsed));
    }

    // 표시·포드·카메라·오너 보행을 원래대로. 멱등.
    private void ClearLocal()
    {
        StopOwnerWalk();
        _timeline = null;

        if (_overlay != null) _overlay.Clear();

        if (_heldCamera != null)
        {
            _heldCamera.SetCinematicDistanceMultiplier(1f);
            if (cameraFocus != null) _heldCamera.ClearTemporaryFollow();
            _heldCamera = null;
        }

        _launcherDriver?.Release();
        _hatchHeldOnBoard = false;
        _launchFast = false;
    }

    // ---------------- 오너 보행 ----------------

    // 평소 걷기와 같은 Motor 채널(AddGroundedDisplacement: 스윕 충돌·계단 오르기·중력)로 웨이포인트를 따라 걷고(Walk 애니 = IsMoving),
    // 문턱부터는 포즈로 보간해 충돌 없이 좌석까지 들어간다. 도착 뒤로는 좌석을 포즈로 붙잡는다(숨김·하강 동안 낙하·밀림 없음).
    // 위치는 오너 NetworkTransform·IsMoving 은 NetworkAnimator(오너)로 복제된다. 연출 잠금(Cinematic)은 조작 입력만 막고 이 변위는 막지 않는다.
    private void FixedUpdate()
    {
        if (!IsSpawned || _timeline == null)
            return;

        double start = _startAt.Value;
        if (start <= 0d)
            return;

        NetworkManager nm = NetworkManager.Singleton;
        NetworkObject po = nm != null ? nm.LocalClient?.PlayerObject : null;
        if (po == null || !po.TryGetComponent(out PlayerMotor motor))
            return;

        if (!_walk.Active || _walk.Start != start)
        {
            StopOwnerWalk();
            int slot = _walkers.IndexOf(po.NetworkObjectId); // 복제 명단 순서 = 보행 슬롯
            if (slot < 0 || slot >= _timeline.WalkerCount)
                return; // 참가자가 아니다(또는 보행 시간 목록이 아직 안 왔다 — 다음 틱에 다시)

            Transform seat = seats.Length > 0 ? seats[PodLaunchRules.PointIndex(slot, seats.Length)] : null;
            List<WalkSlot> slots = ActiveSlots();
            if (seat == null || slots.Count == 0)
                return;

            WalkSlot walkSlot = slots[PodLaunchRules.PointIndex(slot, slots.Count)];
            PodLaunchRules.WalkRoute route = BuildRoute(walkSlot, seat.position);
            WarnRoute(walkSlot, route);

            // 서버가 정한 보행 시간(복제)을 길이 비율로 앞(충돌 켬)·뒤(→ 문턱, 충돌 끔) 구간에 나눈다 — 걷는 속도가 일정하게.
            Vector3 walkStart = walkSlot.start.position;
            float groundLength = PodLaunchRules.GroundLength(walkStart, route);

            _walk = new OwnerWalk
            {
                Active = true,
                Start = start,
                Slot = slot,
                GroundShare = PodLaunchRules.GroundShare(groundLength, PodLaunchRules.WalkLength(walkStart, route) - groundLength),
                WalkStart = walkStart,
                TeleportAt = start + _timeline.TeleportAt,
                Ground = route.Ground,
                Approach = route.HasThreshold ? new[] { route.Interior[0] } : Array.Empty<Vector3>(),
                Seat = route.Interior[route.Interior.Length - 1],
            };
        }

        double now = ServerNow;
        if (now < _walk.TeleportAt)
            return;

        if (!_walk.Teleported)
        {
            _walk.Teleported = true;
            // 한 줄로 걷는 참가자끼리 밀어내기로 튀지 않게(걷기 시작 지점을 돌려 쓰면 겹쳐 선다) — 순간이동부터 보행·붙잡기 동안만. 벽·계단엔 그대로 막힌다.
            motor.PassThroughUnitsOverride = true;
            Vector3 firstTarget = _walk.Ground.Length > 0 ? _walk.Ground[0] : _walk.Approach.Length > 0 ? _walk.Approach[0] : _walk.Seat;
            TeleportOwner(po, motor, _walk.WalkStart, firstTarget - _walk.WalkStart);
        }

        // 해치가 열려야(서버 확정·복제) 보행 시각이 정해진다 — 그때까지 걷기 시작 지점에 서 있다.
        if (!_walk.Timed)
        {
            if (_hatchOpenedAt.Value <= 0d)
                return;

            int walkSlotIndex = _walk.Slot;
            double board = _timeline.BoardAt(PodDropTimeline.EventElapsed(_hatchOpenedAt.Value, start));
            _walk.Timed = true;
            _walk.WalkStartAt = start + _timeline.WalkStartAt(walkSlotIndex, board);
            _walk.GroundEndAt = _walk.WalkStartAt + _timeline.WalkSecondsOf(walkSlotIndex) * _walk.GroundShare;
            _walk.ArriveAt = start + _timeline.WalkArriveAt(walkSlotIndex, board);
            _walk.WalkEndAt = start + _timeline.WalkEndAt(walkSlotIndex, board);
        }

        if (now < _walk.WalkStartAt)
            return;

        Player player = po.GetComponent<Player>();
        Vector3 position = motor.SimulationState.Position;
        if (!_walk.Started)
        {
            _walk.Started = true;
            Face(po, (_walk.Ground.Length > 0 ? _walk.Ground[0] : _walk.Approach.Length > 0 ? _walk.Approach[0] : _walk.Seat) - position);
        }

        if (now < _walk.GroundEndAt && _walk.Ground.Length > 0)
        {
            int segment = _walk.GroundIndex;
            motor.AddGroundedDisplacement(
                BossElevatorTimeline.WalkStepAlongPath(position, _walk.Ground, ref _walk.GroundIndex, _walk.GroundEndAt - now, Time.fixedDeltaTime));
            // 경유 지점을 지나 꺾이면 다음 구간을 바라본다.
            if (_walk.GroundIndex != segment)
                Face(po, _walk.Ground[_walk.GroundIndex] - _walk.Ground[_walk.GroundIndex - 1]);
            if (player != null) player.SetAnimatorMoving(true);
            return;
        }

        // 마지막 경로점 → 문턱 — 충돌 없이 같은 속도로 보간(문턱이 없으면 길이 0 이라 건너뛴다).
        if (now < _walk.ArriveAt && _walk.Approach.Length > 0)
        {
            if (!_walk.InApproach)
            {
                _walk.InApproach = true;
                _walk.ApproachFrom = position;
                if (_walk.Ground.Length > 0 && BossElevatorTimeline.WalkNeedsSnap(now, _walk.GroundEndAt, _walk.Ground[_walk.Ground.Length - 1] - position))
                    Debug.LogWarning($"{Tag} 보행이 입구에 못 닿았다(남은 수평 {Vector3.ProjectOnPlane(_walk.Ground[_walk.Ground.Length - 1] - position, Vector3.up).magnitude:0.##}m) — " +
                                     $"{position} 에서 충돌 없이 포드 안으로 보간한다.", this);
                Face(po, _walk.Approach[0] - position);
            }

            float t = (float)((now - _walk.GroundEndAt) / Math.Max(1e-4, _walk.ArriveAt - _walk.GroundEndAt));
            motor.SetPoseTarget(PodLaunchRules.PointAlong(_walk.ApproachFrom, _walk.Approach, t), po.transform.rotation);
            if (player != null) player.SetAnimatorMoving(true);
            return;
        }

        // 문턱 → 좌석 — 정해진 미끄러짐 시간 동안 충돌 없이 보간.
        if (now < _walk.WalkEndAt)
        {
            if (!_walk.InSlide)
            {
                _walk.InSlide = true;
                _walk.SlideFrom = position;
                Face(po, _walk.Seat - position);
            }

            float t = (float)((now - _walk.ArriveAt) / Math.Max(1e-4, _walk.WalkEndAt - _walk.ArriveAt));
            motor.SetPoseTarget(Vector3.Lerp(_walk.SlideFrom, _walk.Seat, Mathf.Clamp01(t)), po.transform.rotation);
            if (player != null) player.SetAnimatorMoving(true);
            return;
        }

        if (!_walk.Arrived)
        {
            _walk.Arrived = true;
            if (player != null) player.SetAnimatorMoving(false);
        }

        motor.SetPoseTarget(_walk.Seat, po.transform.rotation);
    }

    /// <summary>오너 보행을 멈춘다(순간이동한 뒤였으면 Walk 애니·유닛 통과도 끈다). 멱등.</summary>
    private void StopOwnerWalk()
    {
        if (_walk.Active && _walk.Teleported)
        {
            NetworkManager nm = NetworkManager.Singleton;
            NetworkObject po = nm != null ? nm.LocalClient?.PlayerObject : null;
            if (po != null)
            {
                if (po.TryGetComponent(out Player player)) player.SetAnimatorMoving(false);
                if (po.TryGetComponent(out PlayerMotor motor)) motor.PassThroughUnitsOverride = false;
            }
        }

        _walk = default;
    }

    // 보스 엘리베이터 TeleportOwnerClientRpc 의 오너 처리와 같은 순서 — 대시 상태 종료 → 넉백 제거 → Motor 순간이동(다음 시뮬레이션 기준점까지)
    // → Armature 방향 → 오너 NetworkTransform 순간이동(다른 피어가 보간으로 미끄러지지 않게). 이유는 그쪽 주석 참고.
    private static void TeleportOwner(NetworkObject po, PlayerMotor motor, Vector3 destination, Vector3 facing)
    {
        if (po.TryGetComponent(out PlayerStateController stateController))
            stateController.EndDash();

        motor.ClearKnockbackVelocity();
        motor.TeleportAuthoritative(destination);
        Face(po, facing);

        if (po.TryGetComponent(out NetworkTransform netTransform))
            netTransform.Teleport(destination, po.transform.rotation, po.transform.localScale);
    }

    private static void Face(NetworkObject po, Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude > 1e-4f && po.TryGetComponent(out PlayerMovement movement))
            movement.RotateImmediately(direction);
    }

    // 이 슬롯의 개인 경로점 → 공용 경로점(합류 번호부터, 리스트 순서) → 문턱 → 이 좌석. 이름·개수와 무관하다.
    private PodLaunchRules.WalkRoute BuildRoute(WalkSlot slot, Vector3 seat) =>
        PodLaunchRules.BuildRoute(Positions(slot.entryPath), Positions(walkPath), slot.joinIndex,
            threshold != null ? threshold.position : (Vector3?)null, seat);

    private void WarnRoute(WalkSlot slot, PodLaunchRules.WalkRoute route)
    {
        if (route.SkippedCount > 0)
            Debug.LogWarning($"{Tag} '{slot.start.name}' 걷기 경로(개인·공용 경로점)에 빈 칸 {route.SkippedCount}개 — 건너뛰고 걷는다.", this);
        if (route.JoinClamped)
            Debug.LogWarning($"{Tag} '{slot.start.name}' 합류 번호 {slot.joinIndex} 가 범위(0~{walkPath.Count}) 밖 — {route.JoinIndex} 로 맞춰 걷는다.", this);
    }

    /// <summary>
    /// 슬롯별 보행 시간(초) = 자기 걷기 시작 지점 → 개인 경로점 → 공용 경로점(합류 이후) → 문턱 길이 ÷ 걷는 속도. 서버가 시작 때 한 번 계산해 복제한다(좌석은 길이에 들지 않는다).
    /// 걷기 시작 지점이 없으면 전원 0.
    /// </summary>
    private float[] WalkSecondsPerSlot(int count, PodDropSettings source)
    {
        var result = new float[Mathf.Max(0, count)];
        List<WalkSlot> slots = ActiveSlots();
        if (slots.Count == 0 || source == null)
            return result;

        for (int slot = 0; slot < result.Length; slot++)
        {
            WalkSlot walkSlot = slots[PodLaunchRules.PointIndex(slot, slots.Count)];
            result[slot] = source.WalkSecondsFor(PodLaunchRules.WalkLength(walkSlot.start.position, BuildRoute(walkSlot, Vector3.zero)));
        }
        return result;
    }

    // 걷기 시작 지점이 있는 슬롯만(순서 그대로) — 보행 슬롯은 이 목록을 돌려 쓴다.
    private List<WalkSlot> ActiveSlots()
    {
        var list = new List<WalkSlot>(walkSlots.Length);
        foreach (WalkSlot slot in walkSlots)
        {
            if (slot != null && slot.start != null) list.Add(slot);
        }

        return list;
    }

    private float[] ReplicatedWalkSeconds()
    {
        var result = new float[_walkSeconds.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = _walkSeconds[i];
        return result;
    }

    // 빈 칸은 null 로 남긴다(건너뛰기·빈 칸 수는 PodLaunchRules 가).
    private static List<Vector3?> Positions(List<Transform> points)
    {
        var list = new List<Vector3?>(points != null ? points.Count : 0);
        if (points != null)
        {
            foreach (Transform point in points)
                list.Add(point != null ? point.position : (Vector3?)null);
        }

        return list;
    }

#if UNITY_EDITOR
    // 씬 뷰 경로 편집 도구(LobbyPodLaunchEditor) 읽기 전용 — 쓰기는 SerializedObject 로(Undo).
    public IReadOnlyList<WalkSlot> EditorWalkSlots => walkSlots;
    /// <summary>걷기 시작 지점이 있는 슬롯(보행 슬롯 순서 — 표시 번호 "시작 N" 이 이 순서).</summary>
    public List<WalkSlot> EditorActiveSlots() => ActiveSlots();
    public IReadOnlyList<Transform> EditorWalkPath => walkPath;
    public Transform EditorThreshold => threshold;
    public IReadOnlyList<Transform> EditorSeats => seats;
    public Transform EditorLauncher => launcher != null ? launcher.transform : null;
    public PodDropSettings EditorSettings => settings;

    public PodLaunchRules.WalkRoute EditorBuildRoute(WalkSlot slot, Vector3 seat) => BuildRoute(slot, seat);

    /// <summary>시각표 표시용 — 서버가 연출 시작 때 복제하는 것과 같은 계산(<paramref name="source"/> 의 걷는 속도로).</summary>
    public float[] EditorWalkSecondsPerSlot(int count, PodDropSettings source) => WalkSecondsPerSlot(count, source);
#endif

    private static int CountAssigned(Transform[] points)
    {
        int n = 0;
        foreach (Transform point in points)
        {
            if (point != null) n++;
        }

        return n;
    }
}
