using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 맵 포드 착지 연출(할 일 100 C3, 4.MapScene — 씬 상주 NetworkObject <c>Management/MapNetworkSync</c> 에 붙는다).
/// 로비 포드 연출을 거쳐 온 진입(<see cref="NetworkLoadingFlowController.EntryKind"/> = <see cref="MapEntryKind.Lobby"/>)일 때만 켜진다.
/// Dev Boot·판 재시작(Alt+0)·재합류·로비 폴백 로딩은 지금과 같다(낙하 스폰·즉시 조작·S0).
/// <list type="bullet">
/// <item>복제: 연출 켜짐(맵 씬 스폰 때 서버가 정함) · 착지점 Pose · 시작 시각(서버시간, 로딩 화면이 걷힌 순간 <see cref="GameManager.OnMainGameReady"/>)
/// · 펴기 끝·열기 끝 시각(서버시간 — 호스트 포드의 클립 이벤트, 할 일 106). 모든 피어가 이 값으로 <see cref="PodDropMapTimeline"/> 을 같이 계산한다. RPC 없음.</item>
/// <item>서버: 플레이어를 착지점 좌석에 낙하 없이 스폰(<see cref="TryGetSeatPoseServer"/> — 로딩 흐름이 부른다) → 스폰되는 대로 연출 잠금
/// (<see cref="PlayerEncounterLock.BeginCinematicServer"/> — 입력·피해·낙사·사망 기록 차단) + 숨김 → 디졸브 시작에 보이기 → 해제 시각에 잠금 해제.
/// 단계는 호스트 포드 Animator 의 클립 이벤트(<see cref="PodAnimEventRelay"/>)로 확정한다 — 날개·다리 펴기 둘 다 끝 → 착륙, 해치 열기·날개 접기 둘 다 끝 → 디졸브.
/// 이벤트가 빠진 클립은 끝까지 돈 것으로 대신하고 경고한다. 연출 중 끊긴 사람은 건너뛰고 남은 인원으로 계속. 착지점을 못 찾거나 시작 시각이 오지 않으면 즉시 해제(폴백, 경고 로그).</item>
/// <item>전 피어(로컬 표시): 레터박스(로딩이 걷힌 뒤 천천히 들어옴)·UI 숨김(<see cref="PodDropScreenOverlay"/>, 매 프레임 다시 찾음),
/// 카메라 거리 배율 줌, 포드(<c>PF_DropPod_PodOnly_Combined_v001</c> 런타임 생성, 콜라이더 끔) 빠른 낙하 → 정지 높이(기본 5m)에서 날개·다리 펴기
/// → 천천히 착륙(이펙트·흔들림) → 해치 열기·날개 접기 → 디졸브(<see cref="PodDissolve"/>) → 포드 제거. 클립은 1배속 재생(<see cref="PodAnimatorDriver"/>) —
/// 클라 포드는 재생만 하고 자기 이벤트로 단계를 넘기지 않는다.</item>
/// <item>튜토리얼: <see cref="IsArrivalDoneLocal"/> 이 참이 된 뒤에 존1 S0(착지 타이머·자막·목표)이 시작한다(<see cref="TutorialZone1Flow"/>).</item>
/// </list>
/// 수치·좌석 오프셋은 <see cref="PodDropSettings"/>(맵 단계) 가 원본이다 — 착지점은 플레이어 스폰 슬롯(<see cref="MapGenerator.GetRoleSlot"/>) 기준.
/// </summary>
[DisallowMultipleComponent]
public sealed class MapPodArrival : NetworkBehaviour
{
    private const string Tag = "[MapPod]";

    [Tooltip("포드 강하 수치(맵 단계 시간표·좌석 오프셋). 비우면 연출 없음(지금처럼 낙하 스폰).")]
    [SerializeField] private PodDropSettings settings;

    [Header("포드")]
    [Tooltip("착지할 포드 원본(PF_DropPod_PodOnly_Combined_v001 — 루트 Animator). 피어마다 로컬로 만들고 연출 끝에 지운다. 비우면 연출 없음.")]
    [SerializeField] private GameObject podPrefab;

    [Tooltip("디졸브 머티리얼 템플릿(M_Dissolve_Template — DissolveFx, _Cutoff). 비우면 디졸브 대신 포드를 숨긴다.")]
    [SerializeField] private Material dissolveTemplate;

    [Header("착지")]
    [Tooltip("착지 순간 땅 갈라짐 이펙트(23호 등장 착지 FX_Ground_Crash_Entry). 비우면 이펙트 없음.")]
    [SerializeField] private EffectEntry impactEffect;

    // 이번 진입에 연출이 켜졌는가. 서버 write / 모두 read.
    private readonly NetworkVariable<bool> _armed = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 연출 시작 시각(서버시간) = 로딩 화면이 걷힌 순간. 0 = 아직.
    private readonly NetworkVariable<double> _startAt = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 포드 착지점(지면). 클라가 맵 생성을 기다리지 않게 서버가 정한 값을 복제한다.
    private readonly NetworkVariable<bool> _landingReady = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Vector3> _landingPosition = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Quaternion> _landingRotation = new NetworkVariable<Quaternion>(
        Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 날개·다리 펴기가 둘 다 끝난 시각(서버시간, 호스트 포드 클립 이벤트) — 착륙 시작. 0 = 아직.
    private readonly NetworkVariable<double> _deployedAt = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 해치 열기·날개 접기가 둘 다 끝난 시각(서버시간, 호스트 포드 클립 이벤트) — 디졸브 시작. 0 = 아직.
    private readonly NetworkVariable<double> _openedAt = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>지금 맵 씬의 포드 착지 연출(없으면 null). 로딩 흐름(좌석 스폰)·튜토리얼(S0 지연)이 읽는다.</summary>
    public static MapPodArrival Active { get; private set; }

    /// <summary>
    /// [전 피어] 도착 연출이 끝났는가 — 튜토리얼 S0 시작 조건. 연출이 없는 맵·꺼진 진입(Dev Boot·재시작)은 즉시 참.
    /// </summary>
    public static bool IsArrivalDoneLocal => Active == null || Active.ArrivalDoneLocal;

    private static double ServerNow =>
        NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : 0d;

    // 서버 전용
    private PodDropMapTimeline _serverTimeline;
    private bool _landingResolvedServer;
    private Pose _landingServer;
    private bool _readySubscribed;
    private float _armedAtRealtime;
    private bool _shownServer;
    private bool _releasedServer;
    private PodClipGate _deployGate;
    private PodClipGate _openGate;
    private readonly HashSet<ulong> _lockedPlayerObjectIds = new HashSet<ulong>();
    private readonly List<PlayerEncounterLock> _locks = new List<PlayerEncounterLock>();

    // 전 피어(로컬 표시)
    private PodDropMapTimeline _timeline;
    private bool _localActive;
    private bool _localFinished;
    private PodDropScreenOverlay _overlay;
    private CameraTargetSwitcher _heldCamera;
    private GameObject _pod;
    private bool _podGone;
    private PodDissolve _dissolve;
    private PodAnimatorDriver _podAnimator;
    private bool _impactPlayed;

    private bool ArrivalDoneLocal
    {
        get
        {
            if (_localFinished) return true;
            if (settings == null) return true; // 시간표를 모르면 막지 않는다(서버도 설정 없이 켜지 않는다)
            return PodArrivalRules.IsArrivalDone(IsSpawned, _armed.Value, _startAt.Value, _openedAt.Value, ServerNow, LocalTimeline.ReleaseAfterOpenSeconds);
        }
    }

    private PodDropMapTimeline LocalTimeline => _timeline ??= settings.CreateMapTimeline();

    private void Awake()
    {
        Active = this;
    }

    public override void OnDestroy()
    {
        UnsubscribeReady();
        ClearLocal();
        if (Active == this) Active = null;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
            ArmServer();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            UnsubscribeReady();
            // 연출 도중 씬이 내려간다(재시작·호스트 종료) — 건 잠금·숨김을 푼다. 플레이어가 같이 디스폰되면 건너뛴다.
            if (_armed.Value && !_releasedServer) ReleaseAllServer("디스폰");
        }

        ClearLocal();
        base.OnNetworkDespawn();
    }

    // ---------------- 서버 ----------------

    private void ArmServer()
    {
        var flow = NetworkManager.GetComponent<NetworkLoadingFlowController>();
        MapEntryKind entryKind = flow != null ? flow.EntryKind : MapEntryKind.Direct;
        GameManager gameManager = GameManager.Instance;
        string skip = PodArrivalRules.SkipReason(entryKind, settings != null, podPrefab != null,
            gameManager != null && !gameManager.IsMainGameReady);
        if (skip != null)
        {
            if (entryKind == MapEntryKind.Lobby)
                Debug.LogWarning($"{Tag} {skip} — 맵 포드 착지 연출 없이 지금처럼(낙하 스폰·즉시 조작) 진행.", this);
            else
                Debug.Log($"[SceneFlow] {Tag} 연출 없음 — {skip}");
            return;
        }

        _serverTimeline = settings.CreateMapTimeline();
        _deployGate = new PodClipGate(settings.Map.DeployStates);
        _openGate = new PodClipGate(settings.Map.OpenStates);
        // 이벤트만 구독한다(SubscribeMainGameReady 는 이미 준비면 즉시 부른다 — 로딩 화면 아래에서 시작하면 안 된다).
        gameManager.OnMainGameReady += HandleMainGameReadyServer;
        _readySubscribed = true;
        _armedAtRealtime = Time.realtimeSinceStartup;
        _armed.Value = true;
        TryResolveLandingServer(null);

        Debug.Log($"[SceneFlow] {Tag} 연출 켜짐(진입 {entryKind}) — 로딩이 걷히면 시작. 낙하 +{_serverTimeline.FallAt:0.##}s, " +
                  $"정지 높이 {_serverTimeline.HoverHeight:0.##}m +{_serverTimeline.HoverAt:0.##}s 에 [{string.Join(",", _deployGate.States)}] → 끝 이벤트 → 착륙 {_serverTimeline.LandSeconds:0.##}s " +
                  $"→ [{string.Join(",", _openGate.States)}] → 끝 이벤트 → 디졸브·해제 +{_serverTimeline.ReleaseAfterOpenSeconds:0.##}s");
    }

    private void UnsubscribeReady()
    {
        if (!_readySubscribed) return;
        _readySubscribed = false;
        if (GameManager.Instance != null) GameManager.Instance.OnMainGameReady -= HandleMainGameReadyServer;
    }

    /// <summary>
    /// [서버] 로딩 흐름이 플레이어를 스폰할 자리 — 연출이 켜져 있으면 포드 착지점 좌석(낙하 높이 없음)을 주고 true.
    /// 연출이 꺼져 있으면 false(호출자는 지금처럼 슬롯 + spawn_Offset 낙하 스폰).
    /// </summary>
    public bool TryGetSeatPoseServer(int spawnIndex, Transform spawnSlot, out Pose pose)
    {
        pose = default;
        if (!IsServer || !IsSpawned || !_armed.Value || _releasedServer || settings == null)
            return false;

        if (!_landingResolvedServer && !TryResolveLandingServer(spawnSlot))
            return false;

        pose = PodArrivalRules.SeatPose(_landingServer, spawnIndex, settings.Map.seatOffsets);
        return true;
    }

    // 착지점 = 플레이어 스폰 슬롯 + 오프셋, 높이는 지면(레이). 맵 생성 전이면 false — 다음 프레임에 다시.
    private bool TryResolveLandingServer(Transform spawnSlot)
    {
        if (_landingResolvedServer) return true;

        if (spawnSlot == null)
        {
            MapGenerator generator = FindFirstObjectByType<MapGenerator>();
            ZoneSlot slot = generator != null ? generator.GetRoleSlot(ZoneRole.PlayerSpawn) : null;
            spawnSlot = slot != null ? slot.transform : null;
        }

        if (spawnSlot == null)
            return false;

        PodDropMapTimings map = settings.Map;
        Pose landing = PodArrivalRules.LandingPose(spawnSlot.position, spawnSlot.rotation, map.podLandingOffset);
        float probe = map.groundProbeHeight;
        if (probe > 0f && Physics.Raycast(landing.position + Vector3.up * probe, Vector3.down, out RaycastHit hit, probe * 2f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            landing.position.y = hit.point.y;
        }

        _landingServer = landing;
        _landingResolvedServer = true;
        _landingPosition.Value = landing.position;
        _landingRotation.Value = landing.rotation;
        _landingReady.Value = true;
        Debug.Log($"[SceneFlow] {Tag} 착지점 {landing.position} (슬롯 {spawnSlot.name} {spawnSlot.position})");
        return true;
    }

    private void HandleMainGameReadyServer()
    {
        if (!IsSpawned || !_armed.Value || _startAt.Value > 0d || _releasedServer)
            return;

        if (!TryResolveLandingServer(null))
        {
            AbortServer("착지점(PlayerSpawn 슬롯)을 못 찾았다");
            return;
        }

        _startAt.Value = ServerNow;
        Debug.Log($"[SceneFlow] {Tag} 로딩 걷힘 — 포드 착지 연출 시작, 잠근 플레이어 {_locks.Count}명");
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned || !_armed.Value || _releasedServer)
            return;

        TryResolveLandingServer(null);

        double start = _startAt.Value;
        if (start <= 0d)
        {
            float timeout = settings.Map.startTimeoutSeconds;
            if (Time.realtimeSinceStartup - _armedAtRealtime > timeout)
            {
                AbortServer($"로딩 걷힘(시작)이 {timeout:0}초 안에 오지 않았다");
                return;
            }

            LockPlayersServer(hide: true);
            return;
        }

        double elapsed = ServerNow - start;
        double opened = PodDropTimeline.EventElapsed(_openedAt.Value, start);
        if (!_shownServer && elapsed >= _serverTimeline.DissolveAt(opened))
        {
            _shownServer = true;
            foreach (PlayerEncounterLock encounterLock in _locks)
            {
                if (encounterLock != null && encounterLock.IsSpawned) encounterLock.SetCinematicHiddenServer(false);
            }
        }

        LockPlayersServer(hide: !_shownServer);

        if (_serverTimeline.IsReleasedAt(elapsed, opened))
            ReleaseAllServer("연출 끝");
    }

    // 호스트 포드의 클립 이벤트로 단계를 확정한다 — 이벤트는 애니메이터 갱신 중에 오므로 LateUpdate(UpdatePod 뒤)에서 본다.
    // 이벤트가 빠진 클립은 끝까지 돈 것으로 대신한다(경고). 포드가 이미 디졸브로 지워졌으면 할 일이 없다.
    private void UpdateStagesServer(PodDropMapTimeline timeline, double elapsed, double deployed)
    {
        if (!IsServer || _releasedServer || _podAnimator == null)
            return;

        if (_deployedAt.Value <= 0d)
        {
            if (elapsed >= timeline.HoverAt && GateOpen(_deployGate, "펴기"))
            {
                _deployedAt.Value = ServerNow;
                Debug.Log($"[SceneFlow] {Tag} 날개·다리 펴기 끝 +{elapsed:0.##}s — 착륙 {timeline.LandSeconds:0.##}s");
            }
            return;
        }

        if (_openedAt.Value <= 0d && elapsed >= timeline.TouchdownAt(deployed) && GateOpen(_openGate, "열기"))
        {
            _openedAt.Value = ServerNow;
            Debug.Log($"[SceneFlow] {Tag} 해치 열기·날개 접기 끝 +{elapsed:0.##}s — 디졸브, 해제 +{timeline.ReleaseAfterOpenSeconds:0.##}s 뒤");
        }
    }

    private bool GateOpen(PodClipGate gate, string label)
    {
        if (gate == null || !gate.IsOpen(_podAnimator.IsFinished, out List<string> fallbacks))
            return false;

        if (fallbacks != null)
            Debug.LogWarning($"{Tag} {label} 클립 [{string.Join(",", fallbacks)}] 에 '{PodAnimEventRelay.FunctionName}' 이벤트가 없어 클립 끝에서 넘어간다 — " +
                             "FBX 임포트 창(Animation 탭) 또는 Tools/Lobby/포드 애니 이벤트 메뉴로 넣을 것.", this);
        return true;
    }

    // [전 피어] 포드 클립 이벤트 — 단계 확정은 서버만(클라 포드는 재생만 한다).
    private void HandlePodClipEvent(string clipName)
    {
        if (!IsServer || _releasedServer)
            return;

        _deployGate?.Notify(clipName);
        _openGate?.Notify(clipName);
    }

    // 스폰되는 대로 잠근다(플레이어 오브젝트 단위 — 로비 오브젝트가 남아 있던 프레임과 섞이지 않게). 연출 중 끊긴 사람은 자연히 빠진다.
    private void LockPlayersServer(bool hide)
    {
        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            NetworkObject po = client.PlayerObject;
            if (po == null || !po.IsSpawned || _lockedPlayerObjectIds.Contains(po.NetworkObjectId))
                continue;

            var encounterLock = po.GetComponent<PlayerEncounterLock>();
            if (encounterLock == null || !encounterLock.IsSpawned)
                continue;

            encounterLock.BeginCinematicServer();
            if (hide) encounterLock.SetCinematicHiddenServer(true);
            _lockedPlayerObjectIds.Add(po.NetworkObjectId);
            _locks.Add(encounterLock);
        }
    }

    private void ReleaseAllServer(string reason)
    {
        _releasedServer = true;
        UnsubscribeReady();
        foreach (PlayerEncounterLock encounterLock in _locks)
        {
            if (encounterLock == null || !encounterLock.IsSpawned) continue;
            encounterLock.SetCinematicHiddenServer(false);
            encounterLock.EndCinematicServer();
        }

        Debug.Log($"[SceneFlow] {Tag} 잠금 해제({reason}) — {_locks.Count}명");
        _locks.Clear();
        _lockedPlayerObjectIds.Clear();
    }

    // 검은 화면·잠금에 갇히지 않게 연출을 접는다 — 클라는 켜짐 값이 꺼지면 화면·포드를 걷는다.
    private void AbortServer(string reason)
    {
        Debug.LogWarning($"{Tag} {reason} — 연출 없이 즉시 해제.", this);
        ReleaseAllServer("폴백");
        if (IsSpawned) _armed.Value = false;
    }

    // ---------------- 전 피어(로컬 표시) ----------------

    // LateUpdate — 로딩 화면이 걷히는 프레임에 켜진 HUD 도 그리기 전에 끈다.
    private void LateUpdate()
    {
        if (!IsSpawned)
            return;

        if (!_armed.Value || settings == null)
        {
            if (_localActive) ClearLocal();
            return;
        }

        if (_localFinished)
            return;

        double start = _startAt.Value;
        bool started = start > 0d;
        // 시작 전(로딩 화면 아래) = t0 자세: 레터박스 없음, 포드는 낙하 높이, 카메라는 시작 거리.
        double elapsed = started ? ServerNow - start : 0d;
        double deployed = started ? PodDropTimeline.EventElapsed(_deployedAt.Value, start) : PodDropTimeline.NotYet;
        double opened = started ? PodDropTimeline.EventElapsed(_openedAt.Value, start) : PodDropTimeline.NotYet;
        PodDropMapTimeline timeline = LocalTimeline;

        if (started && timeline.IsReleasedAt(elapsed, opened))
        {
            ClearLocal();
            _localFinished = true;
            Debug.Log($"[SceneFlow] {Tag} 도착 연출 끝 — UI·조작·튜토리얼 시작");
            return;
        }

        _localActive = true;
        UpdateOverlay(timeline, elapsed, opened);
        UpdateCamera(timeline, elapsed);
        UpdatePod(timeline, started, elapsed, deployed, opened);
        if (started)
            UpdateStagesServer(timeline, elapsed, deployed);
    }

    private void UpdateOverlay(PodDropMapTimeline timeline, double elapsed, double opened)
    {
        if (_overlay == null) _overlay = gameObject.AddComponent<PodDropScreenOverlay>();
        // 기본 0 = 매 프레임 — 로딩이 걷히는 프레임에 HUD 가 한 프레임도 보이지 않게(연출 몇 초 동안만).
        _overlay.HudRescanInterval = settings.Map.hudRescanSeconds;
        // 검은 화면 = 도착 페이드인(기본 0 = 없음). 시작 전엔 로딩 화면(정렬 1000) 아래에 깔려 있다가 걷히는 순간부터 밝아진다.
        _overlay.Apply(timeline.LetterboxAlphaAt(elapsed, opened) * settings.LetterboxHeight, timeline.FadeAlphaAt(elapsed), true);
    }

    private void UpdateCamera(PodDropMapTimeline timeline, double elapsed)
    {
        CameraTargetSwitcher cameraSwitcher = CameraTargetSwitcher.Active;
        if (cameraSwitcher == null)
            return; // 리그가 늦게 붙을 수 있다 — 다음 프레임에 다시

        _heldCamera = cameraSwitcher;
        cameraSwitcher.SetCinematicDistanceMultiplier(timeline.CameraDistanceAt(elapsed));
    }

    private void UpdatePod(PodDropMapTimeline timeline, bool started, double elapsed, double deployed, double opened)
    {
        if (_podGone || !_landingReady.Value || podPrefab == null)
            return;

        Vector3 landing = _landingPosition.Value;
        if (_pod == null)
            SpawnPod(landing);

        _pod.transform.SetPositionAndRotation(landing + Vector3.up * timeline.PodHeightAt(elapsed, deployed), _landingRotation.Value);
        UpdatePodAnimation(timeline, started, elapsed, deployed);
        if (!started)
            return;

        double touchdown = timeline.TouchdownAt(deployed);
        double dissolveAt = timeline.DissolveAt(opened);
        if (PodArrivalRules.ShouldPlayImpact(elapsed, touchdown, dissolveAt, _impactPlayed))
            PlayImpact(landing);
        if (elapsed >= touchdown)
            _impactPlayed = true; // 늦게 본 피어는 지나간 착지 효과를 내지 않는다

        if (elapsed < dissolveAt)
            return;

        if (_dissolve == null)
        {
            _dissolve = _pod.AddComponent<PodDissolve>();
            _dissolve.Begin(dissolveTemplate);
        }

        float progress = timeline.DissolveProgressAt(elapsed, opened);
        _dissolve.SetProgress(progress);
        if (progress >= 1f)
        {
            Destroy(_pod);
            _pod = null;
            _dissolve = null;
            _podAnimator = null;
            _podGone = true;
        }
    }

    // 착지 애니는 상태마다 자기 마스크 레이어(Gear·Wings·Hatches)에서 1배속으로 돈다 — 겹쳐 돌아도 서로 덮지 않는다.
    // 정지 높이 전(낙하 중)엔 펴기 클립 처음 자세로 멈춰 떨어진다. 펴기는 정지 높이 도착 순간, 열기·접기는 지면에 닿는 순간 시작(늦게 본 피어는 지난 만큼 건너뛴다).
    // 해치는 지면에 닿기 전까지 열기 클립 0프레임에 고정(닫힌 자세) — Hatches 레이어가 비면 베이스 DP_Idle 의 열린 해치가 보인다. 고정 중 이벤트는 버린다.
    private void UpdatePodAnimation(PodDropMapTimeline timeline, bool started, double elapsed, double deployed)
    {
        if (_podAnimator == null)
        {
            _podAnimator = new PodAnimatorDriver(_pod.GetComponent<Animator>());
            _podAnimator.ClipEvent += HandlePodClipEvent;
        }

        PodDropMapTimings map = settings.Map;
        double touchdown = timeline.TouchdownAt(deployed);
        if (PodArrivalRules.ShouldHoldHatchClosed(started, elapsed, touchdown))
            _podAnimator.HoldAtStart(map.hatchOpenState, -1);

        if (!started || elapsed < timeline.HoverAt)
        {
            _podAnimator.PoseStart(map.wingDeployState, -1);
            _podAnimator.PoseStart(map.gearDeployState, -1);
            return;
        }

        float sinceHover = (float)(elapsed - timeline.HoverAt);
        _podAnimator.Play(map.wingDeployState, -1, sinceHover);
        _podAnimator.Play(map.gearDeployState, -1, sinceHover);

        if (elapsed < touchdown)
            return;

        float sinceTouchdown = (float)(elapsed - touchdown);
        _podAnimator.Play(map.hatchOpenState, -1, sinceTouchdown);
        _podAnimator.Play(map.wingFoldState, -1, sinceTouchdown);
    }

    private void SpawnPod(Vector3 landing)
    {
        _pod = Instantiate(podPrefab, landing, _landingRotation.Value);
        _pod.name = $"{podPrefab.name}(MapPodArrival)";
        // 맵 씬과 함께 내려가게(재시작·종료) — 생성 순간 활성 씬이 로딩 씬일 수 있다.
        SceneManager.MoveGameObjectToScene(_pod, gameObject.scene);
        // 포드 안 좌석에 숨겨 둔 플레이어와 부딪히지 않게 — 연출용 비주얼일 뿐이다.
        foreach (Collider c in _pod.GetComponentsInChildren<Collider>(true))
            c.enabled = false;
    }

    private void PlayImpact(Vector3 landing)
    {
        PodDropMapTimings map = settings.Map;
        if (impactEffect != null && EffectManager.Instance != null)
            EffectManager.Instance.Play(impactEffect, landing, Quaternion.identity, map.impactEffectScale);
        if (map.shakeAmplitude > 0f && CameraFeedback.Instance != null)
            CameraFeedback.Instance.PlayImpactShake(map.shakeAmplitude, map.shakeDuration);
    }

    // 화면·카메라·포드를 원래대로. 멱등.
    private void ClearLocal()
    {
        _localActive = false;
        if (_overlay != null) _overlay.Clear();

        if (_heldCamera != null)
        {
            _heldCamera.SetCinematicDistanceMultiplier(1f);
            _heldCamera = null;
        }

        if (_pod != null)
        {
            Destroy(_pod);
            _pod = null;
        }

        _dissolve = null;
        _podAnimator = null;
    }
}
