using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 로비 포드 탑승·강하 연출(할 일 100 C2, 3.LobbyScene — <see cref="LobbyTrainingDirector"/> 와 같은 씬 상주 NetworkObject 에 붙는다).
/// 출격 집결 확정 → <see cref="LobbyTrainingDirector"/> 가 맵 로딩 대신 <see cref="TryBeginServer"/> 에 넘긴다 → 시간표 끝(완전 암전)에 그 콜백으로 로딩.
/// <list type="bullet">
/// <item>복제: 시작 시각(서버시간)과 참가 명단(플레이어 NetworkObjectId, 보행 슬롯 순서)만. 모든 피어가 같은 명단 인원으로
/// <see cref="PodDropLobbyTimeline"/> 을 만들어 같은 시각으로 재생한다(보스 엘리베이터 89 와 같은 방식). RPC 없음.</item>
/// <item>서버: 시작 순간 참가자 연출 잠금(<see cref="PlayerEncounterLock.BeginCinematicServer"/> — 입력·피해·낙사·사망 기록 차단),
/// 슬롯마다 포드 안 도착 시각에 숨김(<see cref="PlayerEncounterLock.SetCinematicHiddenServer"/>), <see cref="PodDropLobbyTimeline.LoadAt"/> 에 콜백.
/// 연출 중 끊긴 사람은 건너뛰고 계속한다(명단은 그대로 둔다 — 인원이 바뀌면 시간표가 바뀐다). 잠금·숨김은 로딩이 플레이어를 디스폰할 때 같이 사라진다.</item>
/// <item>오너: 시작 직후 짧은 암전이 가장 깊을 때(<see cref="PodDropLobbyTimeline.TeleportAt"/>) 자기 걷기 시작 지점(WalkStart, 슬롯 순서)으로
/// 순간이동한다(보스 엘리베이터 TeleportOwnerClientRpc 의 오너 처리와 같은 순서). 자기 슬롯 보행 구간 동안 그 자리 → 공용 경로점(walkPath, 리스트 순서)을
/// 평소 걷기 변위(Walk 애니)로 걷고, 문턱 → 좌석은 충돌 없이 보간한다(포드 실내 1.42m &lt; 캡슐 1.79m). 도착 뒤 좌석에 붙잡아 둔다.</item>
/// <item>전 피어(로컬 표시): 레터박스·UI 숨김·암전(순간이동 암전 + 끝 페이드, <see cref="PodDropScreenOverlay"/>), 카메라 임시 추적(포드 카메라 지점) + 거리 배율 줌인,
/// 포드 하강(런타임 transform 만), 연출 동안 포드 콜라이더 끔.</item>
/// </list>
/// 수치는 <see cref="PodDropSettings"/>, 위치는 씬 Transform(LobbyTraining/PodLaunch) 이 원본이다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyPodLaunch : NetworkBehaviour
{
    private const string Tag = "[LobbyPod]";

    [Tooltip("포드 강하 수치(시간표). 비우면 연출 없이 곧바로 맵 로딩.")]
    [SerializeField] private PodDropSettings settings;

    [Header("씬 지점")]
    [Tooltip("연출 동안 카메라가 따라가는 지점(포드 쪽). 비우면 카메라는 플레이어를 따라간 채 줌만 한다.")]
    [SerializeField] private Transform cameraFocus;

    [Tooltip("걷기 시작 지점 — 슬롯 순서(P1, P2, P3). 시작 암전 한가운데 각자 여기로 순간이동한 뒤 걷는다. 사람이 더 많으면 돌려 쓴다. 비우면 연출 없이 곧바로 맵 로딩.")]
    [SerializeField] private Transform[] walkStarts = Array.Empty<Transform>();

    [Tooltip("충돌을 켠 채 걷는 공용 경로점 — 리스트 순서대로 걷는다(이름은 표시용). 각자 걷기 시작 지점에서 첫 점으로 합류한다. " +
             "빈 칸은 건너뛴다. 비면 걷기 시작 지점에서 곧바로 문턱으로 보간한다. 씬 뷰 편집은 이 컴포넌트 인스펙터의 버튼으로.")]
    [FormerlySerializedAs("walkWaypoints")]
    [SerializeField] private List<Transform> walkPath = new List<Transform>();

    [Tooltip("문턱 — 마지막 경로점부터 여기를 거쳐 좌석까지 충돌 없이 보간한다(포드 실내 높이 1.42m 라 캡슐이 막힌다). 비우면 마지막 경로점에서 곧바로 좌석으로.")]
    [SerializeField] private Transform threshold;

    [Tooltip("포드 안 좌석(숨김 지점) — 슬롯 순서(P1, P2, P3). 사람이 더 많으면 돌려 쓴다.")]
    [SerializeField] private Transform[] seats = Array.Empty<Transform>();

    [Header("포드")]
    [Tooltip("내려갈 포드 루트(PF_DropPod_v015). 연출 동안 런타임 위치만 바꾸고(씬 저장 없음) 아래 콜라이더를 끈다. 비우면 하강 없음.")]
    [SerializeField] private Transform pod;

    [Header("화면")]
    [Tooltip("레터박스 위·아래 각각 화면 높이 비율(보스 등장 연출 기본 0.04 와 같은 단위).")]
    [SerializeField, Range(0f, 0.2f)] private float letterboxHeight = 0.04f;

    // 연출 시작 시각(서버시간). 0 = 연출 없음. 서버 write / 모두 read.
    private readonly NetworkVariable<double> _startAt = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 참가자 플레이어 NetworkObjectId — 보행 슬롯 순서. 시작 시각보다 먼저 쓴다. 서버 write / 모두 read.
    private readonly NetworkList<ulong> _walkers = new NetworkList<ulong>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private static double ServerNow =>
        NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : 0d;

    // 서버 전용
    private Coroutine _sequence;
    private bool _loadIssued;
    private readonly List<ulong> _lockedClientIds = new List<ulong>();

    // 전 피어(로컬 표시)
    private double _seenStart;
    private PodDropLobbyTimeline _timeline;
    private PodDropScreenOverlay _overlay;
    private CameraTargetSwitcher _heldCamera;
    private Vector3 _podOrigin;
    private bool _podMoved;
    private readonly List<Collider> _disabledColliders = new List<Collider>();

    // 오너 보행
    private struct OwnerWalk
    {
        public bool Active;
        public double Start;          // 어느 연출의 보행인가 — 시작 시각이 바뀌면 버린다
        public Vector3 WalkStart;     // 걷기 시작 지점(순간이동 목적지)
        public double TeleportAt;     // 서버시간 — 암전이 가장 깊을 때
        public bool Teleported;
        public bool Started;
        public Vector3[] Ground;      // 충돌을 켠 채 걷는 경유 지점(수평만 쓴다 — 높이는 지면)
        public int GroundIndex;
        public Vector3[] Interior;    // 충돌 없이 보간 — 문턱 … 좌석(마지막)
        public double WalkStartAt;    // 서버시간
        public double GroundEndAt;    // 출발 때 길이 비율로 정한다
        public double WalkEndAt;
        public bool InInterior;
        public Vector3 InteriorFrom;
        public bool Arrived;
    }

    private OwnerWalk _walk;

    /// <summary>[전 피어] 연출 중인가(시작 시각이 복제돼 있다).</summary>
    public bool IsRunning => IsSpawned && _startAt.Value > 0d;

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            if (_sequence != null) StopCoroutine(_sequence);
            _sequence = null;
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
    /// [서버] 포드 연출 시작 — 완전 암전 시각에 <paramref name="onDone"/>(맵 로딩)을 부른다. 설정·좌석·참가자가 없으면 false(호출자가 곧바로 로딩).
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
        string missing = PodLaunchRules.MissingSetup(settings != null, CountAssigned(seats), CountAssigned(walkStarts), participants.Count);
        if (missing != null)
        {
            Debug.LogWarning($"{Tag} {missing} — 포드 연출 없이 곧바로 맵 로딩.", this);
            return false;
        }

        PodDropLobbyTimeline timeline = settings.CreateLobbyTimeline(participants.Count);
        double start = NetworkManager.ServerTime.Time;

        // 명단을 시작 시각보다 먼저 쓴다 — 피어가 시작 시각을 보는 순간 인원(시간표)도 맞아 있게.
        _walkers.Clear();
        foreach (ulong clientId in participants)
            _walkers.Add(NetworkManager.ConnectedClients[clientId].PlayerObject.NetworkObjectId);
        _startAt.Value = start;
        _loadIssued = false;
        LockParticipants(participants);

        Debug.Log($"[SceneFlow] {Tag} 포드 연출 시작 — 참가자 {participants.Count}명 [{string.Join(",", participants)}], " +
                  $"순간이동 +{timeline.TeleportAt:0.##}s(암전 {timeline.TeleportBlackoutSeconds:0.##}s), 보행 +{timeline.WalkAt:0.##}s(간격 {timeline.WalkInterval:0.##}·{timeline.WalkSeconds:0.##}s), 강하 +{timeline.DescendAt:0.##}s, " +
                  $"페이드 +{timeline.FadeOutAt:0.##}s, 로딩 +{timeline.LoadAt:0.##}s");

        _sequence = StartCoroutine(Sequence(participants, timeline, start, onDone));
        return true;
    }

    private IEnumerator Sequence(List<ulong> participants, PodDropLobbyTimeline timeline, double start, Action onDone)
    {
        // 슬롯 순서 = 도착 순서 — 포드 안에 도착한 사람부터 숨긴다.
        for (int slot = 0; slot < participants.Count; slot++)
        {
            while (NetworkManager.ServerTime.Time < start + timeline.WalkEndAt(slot))
                yield return null;
            HideParticipant(participants[slot]);
        }

        while (NetworkManager.ServerTime.Time < start + timeline.LoadAt)
            yield return null;

        _sequence = null;
        _loadIssued = true;
        Debug.Log($"[SceneFlow] {Tag} 완전 암전 — 맵 로딩 시작");
        onDone?.Invoke();
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

        // 명단이 시작 시각보다 늦게 왔으면 인원에 맞춰 다시 짠다(같은 시각부터 이어 그린다).
        if (_timeline == null || _timeline.WalkerCount != _walkers.Count)
            _timeline = settings.CreateLobbyTimeline(_walkers.Count);

        double elapsed = ServerNow - start;
        UpdatePod(elapsed);
        UpdateCamera(elapsed);

        if (_overlay == null) _overlay = gameObject.AddComponent<PodDropScreenOverlay>();
        // 레터박스는 완전 암전(LoadAt) 뒤 0 — 검은 화면 아래에서 걷힌다. UI 는 암전 전까지 새로 뜬 것도 끄고, 그 뒤엔 끈 것만 유지(로딩 씬 캔버스는 건드리지 않는다).
        _overlay.Apply(_timeline.LetterboxAlphaAt(elapsed) * letterboxHeight, _timeline.ScreenFadeAlphaAt(elapsed), elapsed < _timeline.LoadAt);
    }

    private void UpdatePod(double elapsed)
    {
        if (pod == null)
            return;

        if (!_podMoved)
        {
            _podMoved = true;
            _podOrigin = pod.position;
            // 연출 동안 포드 콜라이더를 끈다 — 내려가는 몸체에 플레이어(숨김)가 걸리지 않게, 문틀·천장에 막히지 않게.
            foreach (Collider c in pod.GetComponentsInChildren<Collider>())
            {
                if (!c.enabled) continue;
                c.enabled = false;
                _disabledColliders.Add(c);
            }
        }

        pod.position = _podOrigin + Vector3.up * _timeline.PodOffsetAt(elapsed);
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

        if (_podMoved)
        {
            _podMoved = false;
            if (pod != null) pod.position = _podOrigin;
        }

        foreach (Collider c in _disabledColliders)
        {
            if (c != null) c.enabled = true;
        }
        _disabledColliders.Clear();
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
            if (slot < 0)
                return; // 참가자가 아니다

            Transform seat = seats.Length > 0 ? seats[PodLaunchRules.PointIndex(slot, seats.Length)] : null;
            Vector3[] starts = Positions(walkStarts);
            if (seat == null || starts.Length == 0)
                return;

            PodLaunchRules.WalkRoute route = BuildRoute(seat);
            if (route.SkippedCount > 0)
                Debug.LogWarning($"{Tag} 걷기 경로(walkPath)에 빈 칸 {route.SkippedCount}개 — 건너뛰고 걷는다.", this);

            _walk = new OwnerWalk
            {
                Active = true,
                Start = start,
                WalkStart = starts[PodLaunchRules.PointIndex(slot, starts.Length)],
                TeleportAt = start + _timeline.TeleportAt,
                Ground = route.Ground,
                Interior = route.Interior,
                WalkStartAt = start + _timeline.WalkStartAt(slot),
                WalkEndAt = start + _timeline.WalkEndAt(slot),
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
            Vector3 firstTarget = _walk.Ground.Length > 0 ? _walk.Ground[0] : _walk.Interior[0];
            TeleportOwner(po, motor, _walk.WalkStart, firstTarget - _walk.WalkStart);
        }

        double walkStartAt = _walk.WalkStartAt;
        if (now < walkStartAt)
            return;

        Player player = po.GetComponent<Player>();
        Vector3 position = motor.SimulationState.Position;
        if (!_walk.Started)
        {
            _walk.Started = true;

            // 걷는 속도가 일정하게 — 걷기 시작 지점에서 잰 길이 비율로 보행 시간을 앞(충돌)·뒤(보간) 구간에 나눈다.
            Vector3 groundEnd = _walk.Ground.Length > 0 ? _walk.Ground[_walk.Ground.Length - 1] : position;
            float share = PodLaunchRules.GroundShare(
                BossElevatorTimeline.PathLength(position, _walk.Ground),
                PodLaunchRules.PathLength3D(groundEnd, _walk.Interior));
            _walk.GroundEndAt = walkStartAt + (_walk.WalkEndAt - walkStartAt) * share;

            Face(po, (_walk.Ground.Length > 0 ? _walk.Ground[0] : _walk.Interior[0]) - position);
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

        if (now < _walk.WalkEndAt)
        {
            if (!_walk.InInterior)
            {
                _walk.InInterior = true;
                _walk.InteriorFrom = position;
                Vector3 toEntrance = _walk.Interior[0] - position;
                if (_walk.Ground.Length > 0 && BossElevatorTimeline.WalkNeedsSnap(now, _walk.GroundEndAt, _walk.Ground[_walk.Ground.Length - 1] - position))
                    Debug.LogWarning($"{Tag} 보행이 입구에 못 닿았다(남은 수평 {Vector3.ProjectOnPlane(_walk.Ground[_walk.Ground.Length - 1] - position, Vector3.up).magnitude:0.##}m) — " +
                                     $"{position} 에서 충돌 없이 포드 안으로 보간한다.", this);
                Face(po, toEntrance);
            }

            float t = (float)((now - _walk.GroundEndAt) / Math.Max(1e-4, _walk.WalkEndAt - _walk.GroundEndAt));
            motor.SetPoseTarget(PodLaunchRules.PointAlong(_walk.InteriorFrom, _walk.Interior, t), po.transform.rotation);
            if (player != null) player.SetAnimatorMoving(true);
            return;
        }

        if (!_walk.Arrived)
        {
            _walk.Arrived = true;
            if (player != null) player.SetAnimatorMoving(false);
        }

        motor.SetPoseTarget(_walk.Interior[_walk.Interior.Length - 1], po.transform.rotation);
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

    // 공용 경로점(리스트 순서) → 문턱 → 이 좌석. 이름·개수와 무관하다.
    private PodLaunchRules.WalkRoute BuildRoute(Transform seat)
    {
        var path = new List<Vector3?>(walkPath.Count);
        foreach (Transform point in walkPath)
            path.Add(point != null ? point.position : (Vector3?)null);

        return PodLaunchRules.BuildRoute(path, threshold != null ? threshold.position : (Vector3?)null, seat.position);
    }

    // 비어 있는 슬롯은 건너뛴다.
    private static Vector3[] Positions(Transform[] points)
    {
        var list = new List<Vector3>(points.Length);
        foreach (Transform point in points)
        {
            if (point != null) list.Add(point.position);
        }

        return list.ToArray();
    }

#if UNITY_EDITOR
    // 씬 뷰 경로 편집 도구(LobbyPodLaunchEditor) 읽기 전용 — 쓰기는 SerializedObject 로(Undo).
    public IReadOnlyList<Transform> EditorWalkStarts => walkStarts;
    public IReadOnlyList<Transform> EditorWalkPath => walkPath;
    public Transform EditorThreshold => threshold;
    public IReadOnlyList<Transform> EditorSeats => seats;
    public Transform EditorPod => pod;

    public PodLaunchRules.WalkRoute EditorBuildRoute(Transform seat) => BuildRoute(seat);
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
