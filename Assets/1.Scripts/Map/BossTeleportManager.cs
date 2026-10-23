using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// 보스룸 이동 관리자 (PLAN §6 개정). 씬 상주 NetworkObject — 이 오브젝트의 위치가 텔레포트 지점이다.
//
// 흐름(서버 권한):
//  1) 파티 집결(할 일 89) — 서버 Update 가 참가자(접속 중 + PermanentDead 아님)마다 발판(BossEnterTrigger) 안·생명·행동 상태를 읽어
//     BossElevatorGather(= 할 일 86 PartyGatherRules)로 판정한다. 전원이 살아서 발판에 모이면 유지 시간(BossElevatorRig 데이터, 기본 5초) 카운트,
//     한 명이라도 나가거나 쓰러지거나 인원이 바뀌면 0 — 다시 모이면 처음부터. 발판 잠김(튜토리얼)·강제 카운트다운·연출·도착 대기 중엔 판정하지 않는다.
//     제한시간 만료 강제 이동(ForceStartEncounter)은 따로 — 집결 상태로 취소되지 않는다.
//  2) 만료 시각(서버시간)·집결 n/m 을 NetworkVariable로 복제 → 전 피어가 같은 숫자 표시.
//  3) 집결 확정(대기 없이 — 입력 잠금 같은 프레임) 또는 강제 카운트다운 만료 시 보스 엘리베이터 연출(할 일 39·46 — Docs/design/boss-entrance-elevator.md). 시작 시각(서버시간)과 참가자 순서(슬롯 = clientId
//     오름차순, 플레이어 NetworkObjectId 목록)를 복제해 전 피어가 같은 시간표로 BossElevatorRig·화면 페이드·실루엣을 그린다(늦은 합류도 같은 그림).
//     서버는 시간표대로: 시작(카운트다운 끝) = 입력 잠금 → 페이드 아웃 끝(암전) = 참가자를 FrontPoints 로 순간이동(같은 순간 각 피어의
//     참가자 카메라가 엘리베이터로) → 페이드 인 끝 = 오너에게 보행 지시(목표·시작 시각·시간) → (하강·문 열림·슬롯 순서 보행은 각 피어/오너가
//     시간표로) → 상승 시작(문 닫힘 끝) = 숨김 → Blackout(완전 암전) = 4). 시작 뒤엔 취소가 없다 — 발판 이탈은 집결 카운트다운만 취소한다. 리그가 없는 구성이면 곧바로 4).
//  4) 서버가 참가자 전원을 PlayerArrivalPoints 로 텔레포트 → 도착 ACK → AlivePlayersArrived → 입력 잠금·숨김 해제.
//     리그 없는 구성에서 이동하는 본인 화면은 이동 직전 암전 → 이동 → 밝아짐(로컬 연출). 엘리베이터면 상승 중 카메라가 칸을 따라가고
//     화면이 시간표대로 검게 페이드 아웃, 도착 RPC 까지 암전을 유지한 뒤 기존 페이드 인으로 이어진다.
//
// 진입 패드 크기/표시 색/페이드는 전부 이 컴포넌트 인스펙터에서 튜닝한다(팀장 확정) —
// 범위 표시(BossEnterZoneVisual)는 런타임 부착이라 씬에서 직접 만질 수 없기 때문.
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class BossTeleportManager : NetworkBehaviour
{
    [Header("산개")]
    [Tooltip("도착 지점을 못 찾았을 때만 쓰는 폴백 산개 반경.")]
    [SerializeField, Min(0f)] private float scatterRadius = 2f;

    [Header("도착 지점 (bossroom/PlayerArrivalPoints)")]
    [Tooltip("비어 있으면 씬에서 'PlayerArrivalPoints' 이름으로 찾는다.")]
    [SerializeField] private Transform arrivalPointsRoot;
    [Tooltip("비어 있으면 위 루트의 자식으로 채운다. ClientId 오름차순으로 이 순서에 배정된다.")]
    [SerializeField] private Transform[] arrivalPoints;

    [Header("도착 ACK")]
    [Tooltip("서버 전용. 보스룸 이동(엘리베이터면 Blackout 순간) 뒤 이 시간(초) 안에 전원 ACK가 오지 않으면 전투를 강행하지 않고 실패로 복구한다.")]
    [SerializeField, Min(0.5f)] private float arrivalAckTimeoutSeconds = 5f;

    [Header("진입 패드 (존 중앙 트리거+테두리 크기, m)")]
    [SerializeField] private Vector2 enterPadSize = new Vector2(6f, 6f);

    [Header("범위 표시 색")]
    [SerializeField] private Color idleColor = new Color(0.25f, 0.8f, 1f, 0.9f);
    [SerializeField] private Color activeColor = new Color(0.35f, 1f, 0.4f, 1f);

    [Header("이동 페이드 (본인 화면만 — 엘리베이터 연출 중 페이드 시간은 BossElevatorRig 시간표)")]
    [Tooltip("리그 없는 구성에서만 — 카운트다운 끝 직전 화면이 검게 되는 시간(초). 엘리베이터 연출이면 리그의 frontFadeOutDuration 을 쓴다.")]
    [SerializeField, Min(0.05f)] private float fadeOutSeconds = 0.3f;
    [Tooltip("보스룸 도착 RPC 를 받은 순간 → 화면이 다 밝아지는 시간(초). 엘리베이터 연출의 마지막 페이드 인도 이 값이다(리그 없는 경로와 공유).")]
    [SerializeField, Min(0.05f)] private float fadeInSeconds = 0.5f;
    [Tooltip("페이드 색 — 리그 없는 경로·엘리베이터 시간표 페이드(문 앞 이동·상승 뒤 암전)·도착 페이드 인 공통.")]
    [SerializeField] private Color fadeColor = Color.black;

    // 텔레포트 만료 시각(서버시간). 0 = 비활성. 서버 write / 모두 read.
    private readonly NetworkVariable<double> _teleportAt = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 존 점유 여부(범위 표시 색 전환용 복제 — 발판 위 집결 인정 1명 이상). 서버 write / 모두 read.
    private readonly NetworkVariable<bool> _occupied = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 집결 n/m(할 일 89). 판정이 꺼져 있으면 0/0. 서버 write / 모두 read.
    private readonly NetworkVariable<int> _gatheredCount = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _gatherTotal = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 보스 엘리베이터 연출 시작 시각(서버시간). 0 = 연출 없음. 서버 write / 모두 read — 각 피어가 이 시각으로 리그를 재생한다.
    // 도착 완료·도착 무효·재도전 리셋에서 0 으로 돌아간다.
    private readonly NetworkVariable<double> _elevatorStartAt = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 엘리베이터 참가자 플레이어 NetworkObjectId — 슬롯(P1, P2, P3) 순서. 서버 write / 모두 read.
    // 각 피어가 인원(시간표 보행 길이)·슬롯별 실루엣 끄기·"내가 참가자인가"(페이드·카메라)를 여기서 읽는다. 시작 시각과 함께 채우고 비운다.
    private readonly NetworkList<ulong> _elevatorWalkers = new NetworkList<ulong>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private static double ServerNow =>
        NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : 0d;
    private static readonly Func<double> ServerClock = () => ServerNow;

    /// <summary>카운트다운을 누가 시작했나. 집결이 깨지면 <see cref="CountdownCause.Gather"/> 만 취소된다.</summary>
    private enum CountdownCause { None, Gather, Forced }

    private CountdownCause _countdownCause;
    // 강제 카운트다운 대기(TeleportAfter) 또는 엘리베이터 시간표(ElevatorSequence) 코루틴. 집결 카운트다운은 코루틴이 없다(Update 판정).
    private Coroutine _pending;
    private GUIStyle _countdownStyle;
    private GUIStyle _gatherStyle;

    // 파티 집결 판정(서버 전용, 할 일 89).
    private readonly BossElevatorGather _gather = new BossElevatorGather();
    private readonly List<BossElevatorGatherMember> _gatherMembers = new List<BossElevatorGatherMember>();

    // 로컬 페이드 상태(이동하는 본인 화면 전용 연출).
    private float _fadeAlpha;
    private bool _fadingIn;

    // 도착 ACK 상태(서버 전용).
    private uint _encounterSequence;
    private readonly HashSet<ulong> _awaitingArrival = new HashSet<ulong>();
    private readonly List<ulong> _arrivedClientIds = new List<ulong>();
    private Coroutine _ackTimeout;

    // 엘리베이터 연출 상태(서버 전용). 진행 중엔 패드 이탈로 취소하지 않는다.
    private bool _elevatorRunning;
    // 입력 잠금·숨김을 걸었을 수 있는 참가자(clientId) — EndElevatorServer 가 둘 다 푼다.
    private readonly List<ulong> _elevatorClientIds = new List<ulong>();

    // 엘리베이터 연출 로컬 재생(전 피어).
    private BossElevatorRig _rig;
    private BossElevatorTimeline _localTimeline;
    private double _elevatorSeenStart;   // 이 피어가 마지막으로 반영한 시작 시각(0 = 없음)
    private bool _rigStarted;            // 그 시작 시각으로 리그를 재생했나(존 스폰 전이면 다음 프레임에 다시)
    private bool _elevatorArrivedLocal;  // 이번 연출에서 보스룸 도착 RPC 를 받았나 — 암전 유지·칸 추적을 끝낸다
    private Transform _cameraFollow;     // 지금 카메라 임시 추적 대상(null = 플레이어)
    // 이 피어에서 실루엣을 꺼 둔 참가자 — 연출이 끝나면(도착·취소·재도전·디스폰) 반드시 되켠다.
    private readonly List<PlayerSilhouetteTag> _suppressedSilhouettes = new List<PlayerSilhouetteTag>();

    // 오너 보행(엘리베이터 문 앞 → 칸 안). 서버가 RPC 로 목표·시작 시각·시간을 주고, 오너가 Motor 걷기 변위로 걸어 들어간다
    // (칸 경사판·바닥 콜라이더를 타고 오른다). 도착 시각 뒤로는 그 자리를 Motor 포즈로 붙잡는다.
    private struct OwnerWalk
    {
        public bool Active;
        public double ElevatorStart;  // 어느 연출의 지시인가 — 시작 시각이 다른 값으로 바뀌면 버린다
        public Vector3[] Path;        // 경유 지점들 → 칸 안 지점(마지막) — 수평만 쓴다(높이는 지면)
        public int PathIndex;         // 지금 향하는 점
        public Vector3 Target => Path[Path.Length - 1];
        public double StartAt;        // 서버시간
        public float Duration;
        public bool Started;
        public bool Arrived;          // 도착 시각이 지나 Hold 를 붙잡는 중
        public Vector3 Hold;
    }
    private OwnerWalk _walk;

    /// <summary>서버 전용. 유효 참가자 전원이 도착을 적용했을 때 한 번 발화한다.</summary>
    public event Action<IReadOnlyList<ulong>> AlivePlayersArrived;

    /// <summary>서버 전용. ACK 타임아웃 또는 참가자 전멸로 도착이 무효화됐을 때 발화한다.</summary>
    public event Action ArrivalAborted;

    public static BossTeleportManager Instance { get; private set; }

    /// <summary>[서버] 강제 카운트다운·엘리베이터 연출 또는 도착 대기가 진행 중인지 — 재진입을 막는 데 쓴다. 집결 카운트다운은 <see cref="IsCountdownActive"/> 로만 보인다.</summary>
    public bool IsEncounterBusy => _pending != null || _awaitingArrival.Count > 0;

    /// <summary>발판 위에 살아 있는 참가자가 있는지 (모든 피어에서 유효 — 범위 표시 색만 읽는다).</summary>
    public bool IsOccupied => _occupied.Value;

    /// <summary>카운트다운 진행 중인지 — 집결·강제 공통 (모든 피어에서 유효).</summary>
    public bool IsCountdownActive => _teleportAt.Value > 0d;

    /// <summary>집결 n/m 의 n — 발판 위 + 쓰러짐 아님. 판정이 꺼져 있으면 0 (모든 피어에서 유효).</summary>
    public int GatheredCount => _gatheredCount.Value;

    /// <summary>집결 n/m 의 m — 참가자 수. 판정이 꺼져 있으면 0 (모든 피어에서 유효).</summary>
    public int GatherTotal => _gatherTotal.Value;

    /// <summary>[서버 전용] 엘리베이터 연출이 진행 중인지(읽기 전용) — 판 도중 재합류 스폰 보류(C14 RejoinSpawnGate)가 읽는다.</summary>
    public bool IsElevatorRunning => _elevatorRunning;

    public Vector2 EnterPadSize => enterPadSize;
    public Color IdleColor => idleColor;
    public Color ActiveColor => activeColor;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer)
            return;

        ResolveArrivalPoints();
        NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;

        SetCameraFollow(null);
        RestoreSilhouettes();
        StopOwnerWalk();
        if (_rigStarted && _rig != null) _rig.ResetToIdle();
        _rigStarted = false;
        _elevatorSeenStart = 0d;

        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    /// <summary>도착 지점은 bossroom 프리팹 인스턴스 안에 있으므로 씬에서 이름으로 회수한다.</summary>
    private void ResolveArrivalPoints()
    {
        if (arrivalPoints != null && arrivalPoints.Length > 0)
            return;

        if (arrivalPointsRoot == null)
        {
            GameObject found = GameObject.Find("PlayerArrivalPoints");
            if (found != null)
                arrivalPointsRoot = found.transform;
        }

        if (arrivalPointsRoot == null)
        {
            Edit.LogWarning(
                "[BossTeleport] PlayerArrivalPoints를 찾지 못해 폴백 산개로 이동한다. " +
                "bossroom 프리팹에 도착 지점을 배치하고 인스펙터에 연결할 것.", this);
            return;
        }

        var points = new List<Transform>(arrivalPointsRoot.childCount);
        for (int i = 0; i < arrivalPointsRoot.childCount; i++)
            points.Add(arrivalPointsRoot.GetChild(i));

        arrivalPoints = points.ToArray();
        Edit.Log($"[BossTeleport] 도착 지점 {arrivalPoints.Length}개 회수.", this);
    }

    /// <summary>
    /// 발판 점유 표시(서버 전용) — 범위 표시 색 전환용 복제값만 쓴다. 카운트다운은 시작하지 않는다(집결 판정은 <see cref="UpdateGatherServer"/>).
    /// </summary>
    public void SetOccupied(bool occupied)
    {
        if (!IsServer || _occupied.Value == occupied) return;
        _occupied.Value = occupied;
    }

    /// <summary>
    /// [서버, 매 프레임] 파티 집결 판정(할 일 89) — 참가자마다 발판 안·생명·행동 상태를 넣고 <see cref="BossElevatorGather"/> 로 판정한다.
    /// 집결 카운트다운은 <see cref="_teleportAt"/> 에 그대로 싣는다(강제 카운트다운·연출 중엔 판정이 꺼져 있어 건드리지 않는다).
    /// 확정되면 대기 없이 엘리베이터 연출을 시작한다(입력 잠금 같은 프레임).
    /// </summary>
    private void UpdateGatherServer()
    {
        BossElevatorRig rig = _rig != null ? _rig : _rig = BossElevatorRig.FindActive();
        _gather.HoldSeconds = rig != null ? rig.GatherHoldSeconds : PartyGatherRules.DefaultHoldSeconds;

        _gatherMembers.Clear();
        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null) continue; // 스폰 전·교체 중 — 참가자 아님(CollectParticipants 와 같은 기준)
            _gatherMembers.Add(ReadGatherMember(client.ClientId, playerObject));
        }

        bool padEnabled = TutorialBossPath.IsBossPadEnabled;
        var conditions = new BossElevatorGatherConditions(
            padEnabled,
            _countdownCause == CountdownCause.Forced,
            _pending != null || _elevatorRunning,
            _awaitingArrival.Count > 0);

        bool wasCounting = _gather.IsCountdownActive;
        bool confirmed = _gather.Tick(ServerNow, conditions, _gatherMembers);

        bool judging = _gather.IsActive && !_gather.IsConfirmed;
        SetGatherCounts(judging ? _gather.GatheredCount : 0, judging ? _gather.ParticipantCount : 0);
        SetOccupied(padEnabled && _gather.GatheredCount > 0);

        // 강제 카운트다운·연출이 표시를 쥐고 있지 않을 때만 집결 카운트다운을 복제값에 싣는다.
        if (_countdownCause != CountdownCause.Forced && _pending == null)
        {
            double end = _gather.CountdownEndTime;
            if (_teleportAt.Value != end) _teleportAt.Value = end;
            _countdownCause = _gather.IsCountdownActive ? CountdownCause.Gather : CountdownCause.None;

            if (!wasCounting && _gather.IsCountdownActive)
                Edit.Log($"[BossTeleport] 집결 카운트다운 시작 — 참가자 {_gather.ParticipantCount}명 전원 발판, {_gather.HoldSeconds:0.#}초 뒤 보스 엘리베이터.", this);
            else if (wasCounting && !_gather.IsCountdownActive && !confirmed)
                Edit.Log($"[BossTeleport] 집결 카운트다운 취소 — 집결 {_gather.GatheredCount}/{_gather.ParticipantCount}" +
                         (_gather.IsActive ? "" : " (판정 비활성)") + ".", this);
        }

        if (!confirmed) return;

        Edit.Log($"[BossTeleport] 집결 확정 — 참가자 {_gather.ParticipantCount}명, 보스 엘리베이터 시작.", this);
        _countdownCause = CountdownCause.None;
        StartElevatorSequence();
    }

    /// <summary>[서버] 참가자 한 명의 집결 입력. 생명주기 컴포넌트가 없는 구성은 체력으로 Alive/PermanentDead(<see cref="IsTeleportParticipant"/> 와 같은 기준).</summary>
    private static BossElevatorGatherMember ReadGatherMember(ulong clientId, NetworkObject playerObject)
    {
        Player player = playerObject.GetComponent<Player>();
        Unit unit = player != null ? player : playerObject.GetComponent<Unit>();
        float health = unit != null ? unit.CurrentHealth : 0f;

        var lifeCycle = playerObject.GetComponent<PlayerLifeCycleController>();
        PlayerLifeState lifeState = lifeCycle != null
            ? lifeCycle.State
            : (health > 0f ? PlayerLifeState.Alive : PlayerLifeState.PermanentDead);

        bool onPad = BossEnterTrigger.AnyContains(playerObject.transform.position);
        bool busy = player != null && BossElevatorGather.IsBusy(player.CurrentState);
        return new BossElevatorGatherMember(clientId, true, lifeState, health, onPad, busy);
    }

    private void SetGatherCounts(int gathered, int total)
    {
        if (_gatheredCount.Value != gathered) _gatheredCount.Value = gathered;
        if (_gatherTotal.Value != total) _gatherTotal.Value = total;
    }

    private void BeginForcedCountdown(float seconds)
    {
        _countdownCause = CountdownCause.Forced;
        _teleportAt.Value = NetworkManager.ServerTime.Time + seconds;
        _pending = StartCoroutine(TeleportAfter(_teleportAt.Value));
    }

    private void CancelCountdown()
    {
        if (_pending != null)
        {
            StopCoroutine(_pending);
            _pending = null;
        }

        _countdownCause = CountdownCause.None;
        _teleportAt.Value = 0d;
    }

    /// <summary>
    /// 제한시간 만료로 보스전을 강제 개시한다(서버 전용). <see cref="BossTimerManager"/> 가 부른다.
    ///
    /// 🔴 집결 확정과 **같은 경로**(<see cref="StartElevatorSequence"/>)로 합류한다 — 산개 좌표·도착 ACK·페이드·연출 진입이 전부
    ///    그 안에 있어서, 따로 구현하면 두 경로가 갈린다. 강제 카운트다운은 집결 상태(이탈·사망)로 취소되지 않고,
    ///    진행 중엔 집결 판정이 꺼진다.
    ///
    /// 이미 진행 중(집결 카운트다운·연출·도착 대기)이면 <c>false</c> 를 돌려준다.
    /// 🔴 호출자는 그때 **만료 사실을 버리지 말고 유지**했다가, 그 진행이 취소되면 다시 불러야 한다.
    ///    안 그러면 "만료 직전에 집결했다 흩어지면 제한시간이 사라지는" 구멍이 생긴다.
    /// </summary>
    public bool ForceStartEncounter(float warnSeconds)
    {
        if (!IsServer || !IsSpawned)
            return false;

        // 호출자(BossTimerManager)가 만료 사실을 유지한 채 주기적으로 재시도한다 —
        // 여기서 로그를 남기면 대기 구간 내내 쌓이기만 한다. 조용히 거절한다.
        if (IsEncounterBusy || IsCountdownActive)
            return false;

        BeginForcedCountdown(Mathf.Max(0f, warnSeconds));
        Edit.Log($"[BossTeleport] 🔴 제한시간 만료 — {warnSeconds:0.#}초 후 강제 이동.", this);
        return true;
    }

    /// <summary>강제 카운트다운 대기 → 엘리베이터 연출. 재도전 리셋(<see cref="CancelCountdown"/>)만 멈춘다.</summary>
    private IEnumerator TeleportAfter(double teleportAt)
    {
        // 표시(_teleportAt · ServerTime)와 같은 시계로 기다린다 — WaitForSeconds 는 timeScale(간파 슬로우 등)에 늘어나
        // 숫자가 0 이 된 뒤에도 안 떠나고, 그동안 BossCountdown 구간(낙사·부활 = 보스 진입 지점)도 길어진다.
        while (NetworkManager != null && NetworkManager.ServerTime.Time < teleportAt)
            yield return null;

        _pending = null;
        _countdownCause = CountdownCause.None;

        // 강제 이동이면 목숨이 남은 Soul 을 먼저 되살린다(팀장 확정 2026-09-20) —
        // Soul 만 도착하면 Director 가 참가자를 못 잡아 보스가 아예 안 뜬다.
        ReviveSoulsForForcedStart();

        StartElevatorSequence();
    }

    /// <summary>
    /// [서버] 보스 엘리베이터 연출 시작 — 집결 확정·강제 카운트다운 만료 공통. 참가자 기록·시작 시각 복제·입력 잠금은 이 호출 안(같은 프레임)에서 끝내고,
    /// 시간표 대기는 <see cref="ElevatorSequence"/> 코루틴이 한다. 리그가 없는 구성이면 곧바로 보스룸 이동.
    /// </summary>
    private void StartElevatorSequence()
    {
        BossElevatorRig rig = BossElevatorRig.FindActive();
        List<ulong> participants = CollectParticipants();
        if (rig == null || rig.FrontPoints.Count == 0 || participants.Count == 0)
        {
            // 리그가 없는 구성(테스트 씬 등)·참가자 없음은 예전처럼 곧바로 보스룸 이동(참가자 없음은 그 안에서 취소된다).
            if (participants.Count > 0)
                Edit.LogWarning("[BossTeleport] 보스 엘리베이터(BossElevatorRig·FrontPoints)가 없어 연출 없이 보스룸으로 이동한다.", this);
            TeleportAlivePlayers();
            _teleportAt.Value = 0d; // 표시 종료
            return;
        }

        // 🔴 여기부터 취소 없음 — 발판 이탈·사망은 집결 카운트다운만 취소한다. 재도전 리셋만 이 시퀀스를 멈춘다.
        _elevatorRunning = true;
        BossElevatorTimeline timeline = rig.BuildTimeline(participants.Count);
        double start = NetworkManager.ServerTime.Time;

        // 참가자 순서를 시작 시각보다 먼저 쓴다 — 피어가 시작 시각을 보는 순간 인원(시간표)도 맞아 있게.
        _elevatorClientIds.Clear();
        _elevatorClientIds.AddRange(participants);
        _elevatorWalkers.Clear();
        foreach (ulong clientId in participants)
            _elevatorWalkers.Add(NetworkManager.ConnectedClients[clientId].PlayerObject.NetworkObjectId);
        _elevatorStartAt.Value = start;
        _teleportAt.Value = 0d; // 표시 종료
        // 카운트다운이 끝난 순간부터 입력 잠금(보이는 채로·무적) — 페이드·문 앞 이동 동안에도 이동·공격·스킬·대시가 막힌다.
        // 해제는 EndElevatorServer(도착 완료 = 보스 등장 잠금이 걸린 뒤·도착 무효·재도전) 한 곳이다.
        LockParticipants(participants);
        Edit.Log($"[BossTeleport] 보스 엘리베이터 시작 — 참가자 {participants.Count}명, 문 앞 +{timeline.TeleportFrontAt:0.##}s, " +
                 $"페이드 인 끝 +{timeline.FrontFadeInEndAt:0.##}s, 하강 +{timeline.DescendStartAt:0.##}s, 보행 +{timeline.WalkStartAt(0):0.##}s(간격 {timeline.WalkInterval:0.##}·{timeline.WalkDuration:0.##}s), " +
                 $"상승 +{timeline.AscendStartAt:0.##}s, 암전 +{timeline.BlackoutAt:0.##}s", this);

        // 시간표가 0 초짜리라 코루틴이 동기로 끝나면(끝에서 _pending 을 비우고 _elevatorRunning 을 내린다) 핸들을 남기지 않는다.
        Coroutine sequence = StartCoroutine(ElevatorSequence(participants, rig, timeline, start));
        if (_elevatorRunning) _pending = sequence;
    }

    private IEnumerator ElevatorSequence(List<ulong> participants, BossElevatorRig rig, BossElevatorTimeline timeline, double start)
    {
        // 페이드 아웃 끝(완전 암전) — 문 앞으로.
        while (NetworkManager.ServerTime.Time < start + timeline.TeleportFrontAt)
            yield return null;
        TeleportToElevatorFront(participants, rig);

        // 페이드 인 끝 — 오너에게 보행 지시(카메라는 문 앞 이동 암전 때 이미 엘리베이터로 넘어갔다).
        while (NetworkManager.ServerTime.Time < start + timeline.FrontFadeInEndAt)
            yield return null;
        DirectWalk(participants, rig, timeline, start);

        // 문이 다 닫힌 순간 — 칸 안에서 숨긴다(상승은 칸만 움직인다).
        while (NetworkManager.ServerTime.Time < start + timeline.AscendStartAt)
            yield return null;
        HideParticipants(participants);

        while (NetworkManager.ServerTime.Time < start + timeline.BlackoutAt)
            yield return null;

        // 완전 암전 — 기존 보스룸 이동(도착 ACK 포함). 숨김 해제는 도착 완료·무효 때(EndElevatorServer).
        _pending = null;
        _elevatorRunning = false;
        TeleportAlivePlayers();
    }

    /// <summary>이동 대상 clientId(오름차순) — 슬롯 배정이 결정적이어야 재현·디버깅이 가능하다.</summary>
    private List<ulong> CollectParticipants()
    {
        var ids = new List<ulong>();
        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null || !IsTeleportParticipant(playerObject)) continue;
            ids.Add(client.ClientId);
        }

        ids.Sort();
        return ids;
    }

    /// <summary>[암전 중] 참가자를 엘리베이터 문 앞(FrontPoints, 슬롯 순서)으로 — 보스룸 이동과 같은 오너 권한 경로, 도착 ACK 없음.</summary>
    private void TeleportToElevatorFront(List<ulong> participants, BossElevatorRig rig)
    {
        for (int slot = 0; slot < participants.Count; slot++)
        {
            // 페이드 아웃 동안 끊긴 참가자는 건너뛴다.
            if (!NetworkManager.ConnectedClients.TryGetValue(participants[slot], out NetworkClient client) || client.PlayerObject == null)
                continue;

            // 지점보다 사람이 많으면 돌려 쓴다(현재 최대 3인 = 지점 3개).
            if (!rig.GetFrontPose(slot, out Vector3 position, out Quaternion rotation))
            {
                Edit.LogWarning($"[BossTeleport] 엘리베이터 FrontPoints[{slot % rig.FrontPoints.Count}] 가 비어 있다 — clientId={participants[slot]} 는 제자리.", this);
                continue;
            }

            TeleportParticipant(participants[slot], position, rotation, isArrival: false);
        }
    }

    /// <summary>[연출 시작(카운트다운 끝)] 참가자 입력 잠금(보이는 채로·무적). 풀기는 <see cref="EndElevatorServer"/>.</summary>
    private void LockParticipants(List<ulong> participants)
    {
        foreach (ulong clientId in participants)
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null)
                continue;

            client.PlayerObject.GetComponent<PlayerEncounterLock>()?.SetCinematicInputLockServer(true);
            Edit.Log($"[ElevatorFlow] 서버 입력 잠금 — t={ServerNow:0.###} clientId={clientId}", this);
        }
    }

    /// <summary>
    /// [포커스 시각] 오너에게 보행 지시 — 슬롯 순서대로 시간표의 시작 시각에 경유 지점들을 거쳐 칸 안 지점까지.
    /// 시작 시각·시간을 함께 보내 오너가 서버 시간표에 맞춰 걷는다(출발 자리가 문 앞에서 조금 어긋나 있어도 같은 시각에 도착).
    /// 경로 점은 서버 리그에서 읽어 RPC 로 보낸다(점 몇 개) — 오너가 자기 리그를 찾지 않아도 되고 서버 시간표와 같은 경로가 보장된다.
    /// </summary>
    private void DirectWalk(List<ulong> participants, BossElevatorRig rig, BossElevatorTimeline timeline, double start)
    {
        for (int slot = 0; slot < participants.Count; slot++)
        {
            ulong clientId = participants[slot];
            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null)
                continue;

            ElevatorWalkOwnerClientRpc(rig.GetWalkPath(slot).ToArray(), start, start + timeline.WalkStartAt(slot), timeline.WalkDuration,
                new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } });
        }
    }

    /// <summary>상승 시작(문 닫힘 끝) — 아직 접속해 있는 참가자를 칸 안에서 숨긴다(모델·머리 위 UI·실루엣, 입력 잠금·무적은 유지).</summary>
    private void HideParticipants(List<ulong> participants)
    {
        foreach (ulong clientId in participants)
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null)
                continue;

            client.PlayerObject.GetComponent<PlayerEncounterLock>()?.SetCinematicHiddenServer(true);
        }
    }

    /// <summary>
    /// [서버] 엘리베이터 연출 마무리 — 입력 잠금·숨김(무적 포함) 해제, 시작 시각 0·참가자 목록 비움(전 피어 리그 대기 복귀·카메라·실루엣 복귀,
    /// 오너 보행 중단). 멱등. 도착 완료(보스 등장 잠금이 걸린 뒤)·도착 무효·재도전 리셋에서 부른다.
    /// </summary>
    private void EndElevatorServer(string reason)
    {
        _elevatorRunning = false;

        foreach (ulong clientId in _elevatorClientIds)
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null)
                continue;

            var encounterLock = client.PlayerObject.GetComponent<PlayerEncounterLock>();
            if (encounterLock == null) continue;
            encounterLock.SetCinematicHiddenServer(false);
            encounterLock.SetCinematicInputLockServer(false);
            Edit.Log($"[ElevatorFlow] 서버 입력 잠금 해제 — t={ServerNow:0.###} clientId={clientId} 경로={reason}", this);
        }
        _elevatorClientIds.Clear();

        if (_elevatorStartAt.Value != 0d)
            _elevatorStartAt.Value = 0d;
        if (_elevatorWalkers.Count > 0)
            _elevatorWalkers.Clear();
    }

    /// <summary>이동 대상인가. 완전 사망(PermanentDead)만 제외한다.</summary>
    private static bool IsTeleportParticipant(NetworkObject playerObject)
    {
        // 생명주기 컴포넌트가 없는 구성(테스트 씬 등)에서는 예전 규칙(HP)으로 물러선다.
        var lifeCycle = playerObject.GetComponent<PlayerLifeCycleController>();
        if (lifeCycle == null)
        {
            Unit unit = playerObject.GetComponent<Unit>();
            return unit != null && unit.CurrentHealth > 0;
        }

        return lifeCycle.State != PlayerLifeState.PermanentDead;
    }

    /// <summary>
    /// 제한시간 만료로 끌고 갈 때, 목숨이 남은 Soul 을 **각자 목숨 1개씩 써서** 되살린다.
    /// 목숨이 없어 부활에 실패하면 그대로 Soul 로 남고, 이동은 한다(PermanentDead 가 아니므로).
    /// </summary>
    private void ReviveSoulsForForcedStart()
    {
        if (!IsServer) return;

        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null) continue;

            var lifeCycle = playerObject.GetComponent<PlayerLifeCycleController>();
            if (lifeCycle == null || lifeCycle.State != PlayerLifeState.Soul) continue;

            var revive = playerObject.GetComponent<PlayerReviveController>();
            // 튜토리얼 보스방 도착(G11) 전 사망은 목숨 차감 없음(tutorial-director.md S11) — 사망 규칙과 같은 프레임에 겹쳐도 차감하지 않게.
            TutorialDirector tutorial = TutorialDirector.Instance;
            bool free = tutorial != null && tutorial.IsTutorialActive && !tutorial.IsGateOpen(TutorialGate.G11_BossRoom);
            bool ok = revive != null && revive.TryCompleteReviveOnServer(consumeLife: !free);

            Edit.Log($"[BossTeleport] 강제 개시 부활 — clientId={client.ClientId} 결과={(ok ? "성공" : "실패(목숨 없음)")}", this);
        }
    }

    private void TeleportAlivePlayers()
    {
        if (!IsServer) return;

        // 🔴 이동 대상 = **PermanentDead 가 아닌 전원**(Alive · DeadPresentation · Soul).
        //    2026-09-20 팀장 개정 — 원래는 "생존자만" 이었다. 목숨이 남은 Soul 을 옛 맵에 두고 가면
        //    부활해도 보스전에 합류할 길이 없다. 보스방에서 목숨을 써서 살아나 싸우는 게 의도다.
        //    완전 사망자만 현 위치에 남는다.
        List<ulong> alive = CollectParticipants();

        if (alive.Count == 0)
        {
            Edit.LogWarning("[BossTeleport] 생존자가 없어 이동을 취소한다.", this);
            AbortArrival();
            return;
        }

        _encounterSequence++;
        _awaitingArrival.Clear();
        _arrivedClientIds.Clear();

        // 🔴 순서가 계약이다 — ① 대기 명단 전원 등록 → ② ACK 타임아웃 준비 → ③ 그 다음 RPC 전송.
        //
        //    예전에는 한 루프 안에서 `_awaitingArrival.Add` 직후 바로 RPC 를 보냈다. 그런데 NGO 는
        //    **호스트가 대상인 ClientRpc 를 그 자리에서 동기로 실행**하고(`clientRpcMessage.Handle`),
        //    그 본문이 부르는 `ArrivalAppliedServerRpc` 도 호스트에서 동기로 실행된다.
        //    호스트의 clientId 는 0 이라 `alive.Sort()` 뒤 **항상 slot0** 이므로, 원격을 명단에
        //    넣기도 전에 대기자가 0 이 되어 `CompleteArrival()` 이 **호스트 1명만 든 명단**으로 돌았다.
        //
        //    결과: `BossEncounterDirector` 의 참가자 스냅샷이 호스트 1명이 되어 **원격 플레이어가
        //    등장 연출 중 잠기지 않았고**(이동·공격 가능), 호스트가 연출 중 죽으면 나머지가 멀쩡해도
        //    "참가자 전원 이탈" 로 연출이 중단됐다. 보스는 정상적으로 떠서 증상이 조용했다.
        //
        //    2026-09-20 MPPM 2인 실측으로 확정 — 도착=[0] → 잠금명단=[0] (접속자 2명),
        //    뒤늦게 완성된 [0,1] 은 "이미 진행 중(Descending)이라 도착 신호를 무시" 로 버려졌다.
        for (int slot = 0; slot < alive.Count; slot++)
            _awaitingArrival.Add(alive[slot]);

        if (_ackTimeout != null) StopCoroutine(_ackTimeout);
        _ackTimeout = StartCoroutine(AwaitArrivalAck(_encounterSequence));

        for (int slot = 0; slot < alive.Count; slot++)
        {
            ulong clientId = alive[slot];
            GetArrivalPose(slot, out Vector3 destination, out Quaternion rotation);
            // 보스 등장 시네마틱 — 도착 자리 = 워크인 출발 대열(가로 일자·3인 삼각형, PLAN-boss-encounter-cutscene). 높이는 원래 도착 지점 그대로.
            BossEncounterDirector director = BossEncounterDirector.Instance;
            if (director != null && director.TryGetIntroStartPose(slot, alive.Count, destination.y, out Vector3 introPos, out Quaternion introRot))
            {
                destination = introPos;
                rotation = introRot;
            }
            TeleportParticipant(clientId, destination, rotation, isArrival: true);
            Edit.Log($"[BossTeleport] 슬롯 {slot} 배정 — clientId={clientId}, seq={_encounterSequence}", this);
        }
    }

    /// <summary>
    /// [서버] 참가자 한 명을 오너 권한 경로로 옮긴다. <paramref name="isArrival"/> = 보스룸 도착(오너가 ACK) /
    /// false = 엘리베이터 문 앞(ACK 없음).
    /// </summary>
    private void TeleportParticipant(ulong clientId, Vector3 destination, Quaternion rotation, bool isArrival)
    {
        NetworkObject playerObject = NetworkManager.ConnectedClients[clientId].PlayerObject;

        // 🔴 낙하 복구는 **안전지점으로 되돌리는 지연 코루틴**이다. 취소하지 않고 끌고 가면
        //    도착한 뒤 옛 안전지점으로 다시 끌려간다.
        playerObject.GetComponent<PlayerFallRecovery>()?.CancelRecoveryServer();

        // 서버가 오너가 아닌(클라이언트 소유) NetworkTransform에 Teleport를 호출하면 예외가 발생하여 루프가 중단됨.
        // 따라서 서버(호스트) 본인 것만 여기서 처리하고, 클라이언트는 아래 RPC 내부에서 각자 처리하도록 변경.
        if (playerObject.IsOwner && playerObject.TryGetComponent(out NetworkTransform netTransform))
            netTransform.Teleport(destination, rotation, playerObject.transform.localScale);

        TeleportOwnerClientRpc(destination, rotation, _encounterSequence, isArrival, new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        });
    }

    /// <summary>전원 ACK가 오지 않으면 전투를 강행하지 않고 복구한다.</summary>
    private IEnumerator AwaitArrivalAck(uint sequence)
    {
        yield return new WaitForSeconds(arrivalAckTimeoutSeconds);
        _ackTimeout = null;

        if (sequence != _encounterSequence || _awaitingArrival.Count == 0)
            yield break;

        Edit.LogWarning(
            $"[BossTeleport] 도착 ACK 타임아웃 — {_awaitingArrival.Count}명 미응답. 전투를 시작하지 않는다.", this);
        AbortArrival();
    }

    // 오너 로컬에서도 위치를 강제 — 오너 권한 이동 구성에서 서버 텔레포트가 되돌려지는 것 방지.
    // 도착 직후 페이드인 시작(이동 직전 암전은 로컬에서 만료 시각 기준 선제 진행, 엘리베이터면 상승 중 암전).
    // isArrival = 보스룸 도착(ACK) / false = 엘리베이터 문 앞(ACK 없음).
    [ClientRpc]
    private void TeleportOwnerClientRpc(
        Vector3 destination, Quaternion rotation, uint sequence, bool isArrival, ClientRpcParams rpcParams = default)
    {
        NetworkObject playerObject = NetworkManager.LocalClient?.PlayerObject;
        if (playerObject == null) return;

        // 보스룸 도착 — 칸 안에 세워 두던 포즈 고정을 먼저 끊는다(안 그러면 다음 틱에 칸 자리로 되돌린다).
        if (isArrival) StopOwnerWalk();

        // 🔴 Motor 를 거쳐 옮긴다. 예전에는 Rigidbody 에 직접 썼는데, Motor 구동 중 바디는
        //    **kinematic** 이라 `linearVelocity = 0` 이 아무것도 지우지 않았다
        //    (유니티가 "Setting linear velocity of a kinematic body is not supported." 로 경고까지 찍었다).
        //    그래서 수직 속도·넉백·예약 이동이 그대로 살아남아 **도착 지점에서 미끄러졌다.**
        //    `TeleportAuthoritative` 는 다음 시뮬레이션의 기준점까지 함께 옮긴다.
        // 🔴 대시는 **상태**다 — Motor 필드만 지우면 대시 상태가 다음 FixedUpdate 에서 값을 다시 써서
        //    도착 지점부터 남은 대시를 이어 달린다. 상태머신의 정상 종료 경로로 끊는다.
        //    (여긴 오너다 — 오너 권위 이동이라 여기서 끊는 게 맞다.)
        if (playerObject.TryGetComponent(out PlayerStateController stateController))
            stateController.EndDash();

        // 🔴 바라보는 방향은 루트가 아니라 Armature(= Motor 시뮬레이션의 ArmatureRotation)다. 루트 회전은 다음 틱에
        //    Motor 가 RootRotation 으로 되돌리고 Armature 는 옛 방향 그대로라, 지점의 회전이 화면에 반영되지 않았다(할 일 65).
        //    RotateImmediately 가 y축만 남겨 Armature·Motor 상태를 함께 맞춘다.
        var motor = playerObject.GetComponent<PlayerMotor>();
        if (motor != null)
        {
            motor.ClearKnockbackVelocity();
            motor.TeleportAuthoritative(destination);
            if (playerObject.TryGetComponent(out PlayerMovement movement))
                movement.RotateImmediately(rotation * Vector3.forward);
        }
        else
        {
            if (playerObject.TryGetComponent(out Rigidbody rb))
            {
                rb.position = destination;
                rb.rotation = rotation;
            }
            playerObject.transform.SetPositionAndRotation(destination, rotation);
        }

        // 클라이언트 본인(오너) 권한으로 NetworkTransform 순간이동 처리
        if (playerObject.TryGetComponent(out NetworkTransform netTransform))
        {
            netTransform.Teleport(destination, rotation, playerObject.transform.localScale);
        }

        // 🔴 엘리베이터 문 앞 이동은 시간표 페이드(아웃 → 암전 → 인) 한가운데서 일어난다 — 여기서 따로 암전 → 페이드 인을
        //    걸면 시간표 페이드와 겹쳐 두 번 번쩍인다(할 일 46 이전: 카운트다운 직후 검게 번쩍임). 도착 때만 건다.
        if (!isArrival)
            return;

        _fadeAlpha = 1f;
        _fadingIn = true;

        // 보스룸 도착 — 엘리베이터 암전 유지를 끝내고(기존 페이드 인으로) 카메라를 플레이어로 컷 복귀.
        _elevatorArrivedLocal = true;
        SetCameraFollow(null);

        // 적용 완료를 서버에 통보 — 서버는 전원 ACK 이후에만 다음 단계로 넘어간다.
        ArrivalAppliedServerRpc(sequence);
    }

    /// <summary>
    /// [오너] 엘리베이터 보행 지시 — <paramref name="startAt"/>(서버시간)부터 <paramref name="duration"/> 동안 지금 자리 → <paramref name="path"/>(경유 지점들 → 칸 안 지점)
    /// 를 차례로 Motor 걷기 변위로 등속 보행(Walk 애니 = IsMoving, 칸 콜라이더를 타고 오른다). 도착 뒤에도 그 자리를 붙잡아 둔다(숨김·상승 동안 낙하·밀림 없음). 보스룸 도착·연출 끝에 풀린다.
    /// <paramref name="elevatorStart"/> = 어느 연출의 지시인가(복제 시작 시각과 RPC 의 도착 순서가 바뀌어도 맞춰 본다).
    /// </summary>
    [ClientRpc]
    private void ElevatorWalkOwnerClientRpc(
        Vector3[] path, double elevatorStart, double startAt, float duration, ClientRpcParams rpcParams = default)
    {
        StopOwnerWalk();
        if (path == null || path.Length == 0) return;
        _walk = new OwnerWalk
        {
            Active = true,
            ElevatorStart = elevatorStart,
            Path = path,
            StartAt = startAt,
            Duration = Mathf.Max(0f, duration),
        };
    }

    // 오너 보행 — 평소 걷기와 같은 Motor 채널(AddGroundedDisplacement: 스윕 충돌·StepOffset·경사 투영·중력)로 걸어 칸 경사판·바닥을 타고 오른다.
    // 입력 잠금(시네마틱)은 조작 입력만 막고 이 변위는 막지 않는다. 매 틱 남은 수평 거리 ÷ 남은 시간이라 시간표 도착 시각을 지킨다.
    // 도착 시각 뒤로는 그 자리를 포즈로 붙잡는다(숨김·상승 동안 낙하·밀림 없음). 콜라이더에 막혀 못 들어왔으면 칸 안 지점(칸 바닥 높이)으로 옮겨 붙잡는다.
    // 위치는 오너 NetworkTransform·IsMoving 은 NetworkAnimator(오너)로 복제된다.
    private void FixedUpdate()
    {
        if (!_walk.Active || _elevatorStartAt.Value != _walk.ElevatorStart) return;

        NetworkManager nm = NetworkManager.Singleton;
        NetworkObject po = nm != null ? nm.LocalClient?.PlayerObject : null;
        if (po == null || !po.TryGetComponent(out PlayerMotor motor)) return;

        double now = ServerNow;
        if (now < _walk.StartAt) return;

        Player player = po.GetComponent<Player>();
        if (!_walk.Started)
        {
            _walk.Started = true;

            if (po.TryGetComponent(out PlayerStateController stateController)) stateController.EndDash();
            motor.ClearKnockbackVelocity();
            // 줄 서서 걷는 참가자끼리(칸 안 지점 간격 0.72m < 캡슐 지름 0.76m) 막힘·밀어내기로 튀지 않게 — 보행·붙잡기 동안만. 벽·칸 콜라이더엔 그대로 막힌다.
            motor.PassThroughUnitsOverride = true;

            Vector3 dir = _walk.Path[0] - motor.Position;
            dir.y = 0f;
            if (po.TryGetComponent(out PlayerMovement movement)) movement.RotateImmediately(dir);
        }

        // 시뮬레이션 기준 위치 — 직전 틱에 낸 변위까지 반영된 자리(실행 순서와 무관하게 이중 계산하지 않는다).
        Vector3 position = motor.SimulationState.Position;
        double walkEndAt = _walk.StartAt + _walk.Duration;
        if (!_walk.Arrived && now >= walkEndAt)
        {
            _walk.Arrived = true;
            _walk.Hold = position;
            if (BossElevatorTimeline.WalkNeedsSnap(now, walkEndAt, _walk.Target - position))
            {
                _walk.Hold = CabFloorPoint(_walk.Target, motor);
                Edit.LogWarning($"[ElevatorFlow] 보행이 칸 안 지점에 못 닿았다(남은 수평 {Vector3.ProjectOnPlane(_walk.Target - position, Vector3.up).magnitude:0.##}m) — " +
                                $"{position} → {_walk.Hold} 로 옮긴다.", this);
            }
        }

        if (_walk.Arrived)
        {
            motor.SetPoseTarget(_walk.Hold, po.transform.rotation);
            if (player != null) player.SetAnimatorMoving(false);
            return;
        }

        int segment = _walk.PathIndex;
        motor.AddGroundedDisplacement(
            BossElevatorTimeline.WalkStepAlongPath(position, _walk.Path, ref _walk.PathIndex, walkEndAt - now, Time.fixedDeltaTime));
        // 경유 지점을 지나 꺾이면 다음 구간을 바라본다.
        if (_walk.PathIndex != segment && po.TryGetComponent(out PlayerMovement turning))
            turning.RotateImmediately(_walk.Path[_walk.PathIndex] - _walk.Path[_walk.PathIndex - 1]);
        if (player != null) player.SetAnimatorMoving(true);
    }

    /// <summary>칸 안 지점 수평 자리의 칸 바닥(지면 마스크 첫 면) 높이. 못 찾으면 그 지점 그대로.</summary>
    private static Vector3 CabFloorPoint(Vector3 target, PlayerMotor motor)
    {
        LayerMask groundMask = motor.GameRule != null
            ? motor.GameRule.GetGroundMask(false)
            : LayerMask.GetMask("Default", "Ground", "Env");
        if (Physics.Raycast(target + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 4f, groundMask, QueryTriggerInteraction.Ignore))
            target.y = hit.point.y;
        return target;
    }

    /// <summary>오너 보행을 멈춘다(걷는 중이었으면 Walk 애니·유닛 통과도 끈다). 멱등.</summary>
    private void StopOwnerWalk()
    {
        if (_walk.Active && _walk.Started)
        {
            NetworkManager nm = NetworkManager.Singleton;
            NetworkObject po = nm != null ? nm.LocalClient?.PlayerObject : null;
            Player player = po != null ? po.GetComponent<Player>() : null;
            if (player != null) player.SetAnimatorMoving(false);
            if (po != null && po.TryGetComponent(out PlayerMotor motor)) motor.PassThroughUnitsOverride = false;
        }

        _walk = default;
    }

    /// <summary>도착 적용 ACK. sender가 대기 집합에 있고 sequence가 일치할 때만 수락한다(중복 무시).</summary>
    [ServerRpc(RequireOwnership = false)]
    private void ArrivalAppliedServerRpc(uint sequence, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;

        if (sequence != _encounterSequence || !_awaitingArrival.Remove(sender))
            return;

        _arrivedClientIds.Add(sender);
        Edit.Log($"[BossTeleport] 도착 ACK — clientId={sender}, 남은 {_awaitingArrival.Count}명", this);

        if (_awaitingArrival.Count > 0)
            return;

        CompleteArrival();
    }

    // 한 시퀀스에 도착 완료는 한 번뿐이다. 호스트 동기 ACK 와 원격 ACK 가 겹쳐 두 번 불리면
    // Director 가 두 번째를 "이미 진행 중" 으로 버리면서 **올바른 전체 명단이 사라진다**.
    private uint _completedSequence;

    private void CompleteArrival()
    {
        if (_completedSequence == _encounterSequence)
            return;

        _completedSequence = _encounterSequence;

        if (_ackTimeout != null)
        {
            StopCoroutine(_ackTimeout);
            _ackTimeout = null;
        }

        _arrivedClientIds.Sort();
        Edit.Log($"[BossTeleport] 참가자 {_arrivedClientIds.Count}명 도착 완료 @ {transform.position}", this);
        AlivePlayersArrived?.Invoke(_arrivedClientIds);

        // 숨김 해제는 보스 등장 잠금(AlivePlayersArrived 구독자)이 걸린 뒤 — 입력이 풀리는 틈이 없다.
        EndElevatorServer("도착 완료");
    }

    private void AbortArrival()
    {
        if (_ackTimeout != null)
        {
            StopCoroutine(_ackTimeout);
            _ackTimeout = null;
        }

        _awaitingArrival.Clear();
        _arrivedClientIds.Clear();
        _teleportAt.Value = 0d;
        EndElevatorServer("도착 무효");
        ArrivalAborted?.Invoke();
    }

    /// <summary>
    /// [서버] 전멸 리스폰(할 일 30 (2) — Docs/tech/handoff-boss-encounter-reset.md)용 처음 상태 복귀. <b>이 메서드를 부를 때만</b> 동작한다.
    /// 카운트다운(집결·강제)·엘리베이터 연출·도착 ACK 대기를 취소하고 시퀀스를 올려 늦게 온 ACK 를 버린다. 집결 확정 래치도 푼다 —
    /// 다음에 전원이 발판(<see cref="BossEnterTrigger"/>)에 모이면 다시 집결 카운트다운이 시작된다.
    /// 이벤트(<see cref="AlivePlayersArrived"/>·<see cref="ArrivalAborted"/>)는 내지 않는다.
    /// </summary>
    public void ResetForRetryServer()
    {
        if (!IsServer || !IsSpawned)
            return;

        CancelCountdown();

        if (_ackTimeout != null)
        {
            StopCoroutine(_ackTimeout);
            _ackTimeout = null;
        }

        _awaitingArrival.Clear();
        _arrivedClientIds.Clear();
        _encounterSequence++;
        _occupied.Value = false;
        _gather.ResetConfirmation();
        SetGatherCounts(0, 0);
        EndElevatorServer("재도전 리셋"); // 연출 중이었으면 숨김 해제·전 피어 리그 대기 복귀(CancelCountdown 이 시퀀스 코루틴도 멈췄다)

        Edit.Log("[BossTeleport] 재도전 리셋 — 카운트다운·엘리베이터 연출·도착 대기 취소, 집결 래치 해제(발판 다시 사용 가능).", this);
    }

    /// <summary>대기 중 이탈 처리. 남은 대기자가 0이면 도착한 사람으로 진행하고, 아무도 없으면 취소한다.</summary>
    private void HandleClientDisconnected(ulong clientId)
    {
        if (!IsServer || !_awaitingArrival.Remove(clientId))
            return;

        Edit.Log($"[BossTeleport] 대기 중 이탈 — clientId={clientId}, 남은 {_awaitingArrival.Count}명", this);

        if (_awaitingArrival.Count > 0)
            return;

        if (_arrivedClientIds.Count > 0)
            CompleteArrival();
        else
            AbortArrival();
    }

    private void GetArrivalPose(int slot, out Vector3 position, out Quaternion rotation)
    {
        if (arrivalPoints != null && slot < arrivalPoints.Length && arrivalPoints[slot] != null)
        {
            Transform point = arrivalPoints[slot];
            position = point.position;
            rotation = point.rotation;
            return;
        }

        // 폴백 — 지점이 없거나 인원이 지점보다 많을 때만. 황금각 산개로 겹침을 줄인다.
        rotation = transform.rotation;

        if (slot == 0 || scatterRadius <= 0f)
        {
            position = transform.position;
            return;
        }

        float angle = slot * 137f * Mathf.Deg2Rad;
        position = transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * scatterRadius;
    }

    // 로컬 페이드 진행 — 이동 대상(생존한 본인)만 만료 직전 암전, 취소 시 복구.
    // 엘리베이터 연출 참가자인 본인은 시간표대로(문 앞 이동 페이드, 상승 뒤 암전) 그리고, 보스룸 도착 RPC 까지 암전을 유지한다.
    private void Update()
    {
        if (IsServer && IsSpawned) UpdateGatherServer();

        UpdateElevatorLocal();

        float elevatorAlpha = ElevatorFadeAlpha();
        if (elevatorAlpha > 0f)
        {
            _fadeAlpha = elevatorAlpha;
            _fadingIn = false;
            return;
        }

        if (_fadingIn)
        {
            _fadeAlpha -= Time.deltaTime / fadeInSeconds;
            if (_fadeAlpha <= 0f) { _fadeAlpha = 0f; _fadingIn = false; }
            return;
        }

        // 엘리베이터가 있으면 카운트다운 직전 암전을 하지 않는다 — 카운트다운이 끝나는 순간 시간표 페이드 아웃이 0 부터 시작하므로,
        // 여기서 미리 어둡게 하면 시간표로 넘어가며 한 번 밝아졌다 다시 어두워진다.
        double teleportAt = _teleportAt.Value;
        if (teleportAt > 0d && IsLocalPlayerAlive() && !HasElevatorRig())
        {
            double remain = teleportAt - NetworkManager.Singleton.ServerTime.Time;
            if (remain <= fadeOutSeconds)
                _fadeAlpha = Mathf.Clamp01(1f - (float)remain / fadeOutSeconds);
        }
        else if (_fadeAlpha > 0f)
        {
            // 취소됨 — 어두워지던 화면을 빠르게 복구.
            _fadeAlpha = Mathf.MoveTowards(_fadeAlpha, 0f, Time.deltaTime / fadeOutSeconds);
        }
    }

    /// <summary>
    /// [전 피어] 복제된 시작 시각·참가자 순서로 리그를 재생·복귀시키고, 슬롯이 걷기 시작하면 그 플레이어 실루엣을 끈다(연출 끝에 되켬).
    /// 참가자인 본인이면 문 앞 이동(암전)부터 도착까지 카메라가 칸을 따라간다 — 하강 시작 때 칸이 화면 밖이어도 칸과 함께 내려온다.
    /// 존(리그)은 각 피어가 맵 생성 때 스폰하므로 못 찾으면 다음 프레임에 다시 찾는다 — 늦게 합류해도 같은 시각으로 이어 그린다.
    /// </summary>
    private void UpdateElevatorLocal()
    {
        double start = _elevatorStartAt.Value;
        if (start != _elevatorSeenStart)
        {
            if (_rigStarted && _rig != null) _rig.ResetToIdle();
            _rigStarted = false;
            _elevatorArrivedLocal = false;
            _elevatorSeenStart = start;
            RestoreSilhouettes();
            // 다른 연출(또는 끝)로 바뀌었으면 보행 지시를 버린다. RPC 가 시작 시각 복제보다 먼저 와도 같은 연출이면 유지된다.
            if (_walk.Active && _walk.ElevatorStart != start) StopOwnerWalk();
        }

        if (start <= 0d)
        {
            SetCameraFollow(null);
            return;
        }

        int walkers = _elevatorWalkers.Count;
        if (_rigStarted && _localTimeline.WalkerCount != walkers)
            _rigStarted = false; // 참가자 목록이 시작 시각보다 늦게 왔다 — 인원에 맞춰 다시 짜서 같은 시각부터 이어 그린다

        if (!_rigStarted)
        {
            if (_rig == null) _rig = BossElevatorRig.FindActive();
            if (_rig == null) return;

            _localTimeline = _rig.BuildTimeline(walkers);
            _rig.Play(_localTimeline, start, ServerClock);
            _rigStarted = true;
        }

        double elapsed = ServerNow - start;
        UpdateWalkerSilhouettes(elapsed);

        bool followCab = !_elevatorArrivedLocal && IsLocalElevatorParticipant() && _localTimeline.CameraFollowsCabAt(elapsed);
        SetCameraFollow(followCab ? _rig.CameraTarget : null);
    }

    /// <summary>걷기 시작한 슬롯의 플레이어 실루엣을 끈다 — 연출이 끝날 때까지(뒤이어 숨김). 늦게 합류해도 지난 슬롯을 한꺼번에 끈다.</summary>
    private void UpdateWalkerSilhouettes(double elapsed)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.SpawnManager == null) return;

        for (int slot = 0; slot < _elevatorWalkers.Count; slot++)
        {
            if (elapsed < _localTimeline.WalkStartAt(slot)) break; // 슬롯 순서 = 시작 순서

            if (!nm.SpawnManager.SpawnedObjects.TryGetValue(_elevatorWalkers[slot], out NetworkObject po) || po == null)
                continue;

            PlayerSilhouetteTag silhouette = po.GetComponentInChildren<PlayerSilhouetteTag>(true);
            if (silhouette == null || _suppressedSilhouettes.Contains(silhouette)) continue;

            silhouette.SetSuppressed(true);
            _suppressedSilhouettes.Add(silhouette);
        }
    }

    /// <summary>꺼 둔 실루엣을 전부 되켠다(보는 사람 기준 색은 <see cref="PlayerSilhouetteTag"/> 가 다시 고른다). 멱등.</summary>
    private void RestoreSilhouettes()
    {
        foreach (PlayerSilhouetteTag silhouette in _suppressedSilhouettes)
        {
            if (silhouette != null) silhouette.SetSuppressed(false);
        }
        _suppressedSilhouettes.Clear();
    }

    private float ElevatorFadeAlpha()
    {
        if (!_rigStarted || _elevatorArrivedLocal || _localTimeline == null || !IsLocalElevatorParticipant())
            return 0f;

        return _localTimeline.FadeAlphaAt(ServerNow - _elevatorSeenStart);
    }

    /// <summary>카메라 임시 추적 대상을 바꾼다(null = 플레이어로 컷 복귀).</summary>
    private void SetCameraFollow(Transform target)
    {
        if (_cameraFollow == target) return;

        CameraTargetSwitcher cameraSwitcher = CameraTargetSwitcher.Active;
        if (target != null)
        {
            if (cameraSwitcher == null) return;
            cameraSwitcher.SetTemporaryFollow(target);
        }
        else if (cameraSwitcher != null)
        {
            cameraSwitcher.ClearTemporaryFollow();
        }

        NetworkManager nm = NetworkManager.Singleton;
        Edit.Log($"[ElevatorFlow] 카메라 {(target != null ? $"엘리베이터 추적 → {target.name}" : "플레이어 복귀")} — " +
                 $"t={ServerNow:0.###} clientId={(nm != null ? nm.LocalClientId : ulong.MaxValue)}", this);
        _cameraFollow = target;
    }

    /// <summary>이 피어의 플레이어가 진행 중인 엘리베이터 연출의 참가자인가(복제 목록 기준 — 관전자·완전 사망자는 아니다).</summary>
    private bool IsLocalElevatorParticipant()
    {
        NetworkManager nm = NetworkManager.Singleton;
        NetworkObject po = nm != null ? nm.LocalClient?.PlayerObject : null;
        return po != null && _elevatorWalkers.Contains(po.NetworkObjectId);
    }

    /// <summary>이 피어에 엘리베이터 리그(문 앞 지점 포함)가 있는가 — 서버가 연출 경로를 탈 구성인지.</summary>
    private bool HasElevatorRig()
    {
        if (_rig == null) _rig = BossElevatorRig.FindActive();
        return _rig != null && _rig.FrontPoints.Count > 0;
    }

    private bool IsLocalPlayerAlive()
    {
        NetworkManager nm = NetworkManager.Singleton;
        NetworkObject po = nm != null ? nm.LocalClient?.PlayerObject : null;
        if (po == null) return false;
        Unit unit = po.GetComponent<Unit>();
        return unit != null && unit.CurrentHealth > 0;
    }

    // 임시 표시(전 피어): 페이드 오버레이 + 집결 n/m + 카운트다운 숫자 — UI 시스템 합류 시 교체 전제.
    private void OnGUI()
    {
        if (_fadeAlpha > 0f)
        {
            Color c = fadeColor;
            c.a = _fadeAlpha;
            GUI.color = c;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        DrawGatherCount();

        double teleportAt = _teleportAt.Value;
        if (teleportAt <= 0d || NetworkManager.Singleton == null) return;

        // 상한 없음 — 집결 5초·강제 경고(warnSeconds)도 처음 숫자부터 보인다.
        double remain = teleportAt - NetworkManager.Singleton.ServerTime.Time;
        if (remain <= 0d) return;

        _countdownStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 96,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };
        _countdownStyle.normal.textColor = Color.white;

        int display = Mathf.CeilToInt((float)remain);
        Rect rect = new Rect(0f, Screen.height * 0.25f, Screen.width, 120f);
        GUI.Label(rect, display.ToString(), _countdownStyle);
    }

    // 집결 n/m 한 줄 — 판정 중이고 발판에 누가 있을 때만(맵 내내 0/m 이 떠 있지 않게). 카운트다운 숫자 바로 아래.
    private void DrawGatherCount()
    {
        int total = _gatherTotal.Value;
        int gathered = _gatheredCount.Value;
        if (total <= 0 || (gathered <= 0 && !IsCountdownActive)) return;

        _gatherStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 32,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };
        _gatherStyle.normal.textColor = Color.white;

        Rect rect = new Rect(0f, Screen.height * 0.25f + 120f, Screen.width, 48f);
        GUI.Label(rect, LobbyUiStrings.FormatGatherCount(gathered, total), _gatherStyle);
    }
}
