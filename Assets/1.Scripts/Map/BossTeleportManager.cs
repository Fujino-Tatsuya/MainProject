using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// 보스룸 이동 관리자 (PLAN §6 개정). 씬 상주 NetworkObject — 이 오브젝트의 위치가 텔레포트 지점이다.
//
// 흐름(서버 권한):
//  1) BossEnterTrigger가 존 점유(생존 플레이어 유무)를 통지 — 점유 시작 → 카운트다운,
//     전원 이탈/전멸 → 취소·리셋(로아식, 재진입 시 재시작).
//  2) 만료 시각(서버시간)을 NetworkVariable로 복제 → 전 피어가 동일한 3·2·1 표시.
//  3) 만료 시 서버가 "생존" 플레이어 전원을 이 위치 주변으로 산개 텔레포트.
//     이동하는 본인 화면은 이동 직전 암전 → 이동 → 밝아짐(로컬 연출).
//
// 진입 패드 크기/표시 색/페이드는 전부 이 컴포넌트 인스펙터에서 튜닝한다(팀장 확정) —
// 범위 표시(BossEnterZoneVisual)는 런타임 부착이라 씬에서 직접 만질 수 없기 때문.
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class BossTeleportManager : NetworkBehaviour
{
    [Header("카운트다운/산개")]
    [SerializeField, Min(0.5f)] private float countdownSeconds = 3f;
    [Tooltip("도착 지점을 못 찾았을 때만 쓰는 폴백 산개 반경.")]
    [SerializeField, Min(0f)] private float scatterRadius = 2f;

    [Header("도착 지점 (bossroom/PlayerArrivalPoints)")]
    [Tooltip("비어 있으면 씬에서 'PlayerArrivalPoints' 이름으로 찾는다.")]
    [SerializeField] private Transform arrivalPointsRoot;
    [Tooltip("비어 있으면 위 루트의 자식으로 채운다. ClientId 오름차순으로 이 순서에 배정된다.")]
    [SerializeField] private Transform[] arrivalPoints;

    [Header("도착 ACK")]
    [Tooltip("이 시간 안에 전원 ACK가 오지 않으면 전투를 강행하지 않고 실패로 복구한다.")]
    [SerializeField, Min(0.5f)] private float arrivalAckTimeoutSeconds = 5f;

    [Header("진입 패드 (존 중앙 트리거+테두리 크기, m)")]
    [SerializeField] private Vector2 enterPadSize = new Vector2(6f, 6f);

    [Header("범위 표시 색")]
    [SerializeField] private Color idleColor = new Color(0.25f, 0.8f, 1f, 0.9f);
    [SerializeField] private Color activeColor = new Color(0.35f, 1f, 0.4f, 1f);

    [Header("이동 페이드 (본인 화면만)")]
    [SerializeField, Min(0.05f)] private float fadeOutSeconds = 0.3f;
    [SerializeField, Min(0.05f)] private float fadeInSeconds = 0.5f;
    [SerializeField] private Color fadeColor = Color.black;

    // 텔레포트 만료 시각(서버시간). 0 = 비활성. 서버 write / 모두 read.
    private readonly NetworkVariable<double> _teleportAt = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 존 점유 여부(범위 표시 색 전환용 복제). 서버 write / 모두 read.
    private readonly NetworkVariable<bool> _occupied = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>카운트다운을 누가 시작했나. 패드 이탈은 <see cref="CountdownCause.Pad"/> 만 취소한다.</summary>
    private enum CountdownCause { None, Pad, Forced }

    private CountdownCause _countdownCause;
    private Coroutine _pending;
    private GUIStyle _countdownStyle;

    // 로컬 페이드 상태(이동하는 본인 화면 전용 연출).
    private float _fadeAlpha;
    private bool _fadingIn;

    // 도착 ACK 상태(서버 전용).
    private uint _encounterSequence;
    private readonly HashSet<ulong> _awaitingArrival = new HashSet<ulong>();
    private readonly List<ulong> _arrivedClientIds = new List<ulong>();
    private Coroutine _ackTimeout;

    /// <summary>서버 전용. 유효 참가자 전원이 도착을 적용했을 때 한 번 발화한다.</summary>
    public event Action<IReadOnlyList<ulong>> AlivePlayersArrived;

    /// <summary>서버 전용. ACK 타임아웃 또는 참가자 전멸로 도착이 무효화됐을 때 발화한다.</summary>
    public event Action ArrivalAborted;

    public static BossTeleportManager Instance { get; private set; }

    /// <summary>카운트다운 또는 도착 대기가 진행 중인지 — 재진입 카운트다운을 막는 데 쓴다.</summary>
    public bool IsEncounterBusy => _pending != null || _awaitingArrival.Count > 0;

    /// <summary>존 안에 생존 플레이어가 있는지 (모든 피어에서 유효 — 범위 표시가 읽는다).</summary>
    public bool IsOccupied => _occupied.Value;

    /// <summary>카운트다운 진행 중인지 (모든 피어에서 유효).</summary>
    public bool IsCountdownActive => _teleportAt.Value > 0d;

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
    /// 존 점유 상태 통지(서버 전용). BossEnterTrigger가 호출한다.
    /// 점유 시작 → 카운트다운 시작 / 전원 이탈 → 카운트다운 취소(로아식, 팀장 확정).
    /// </summary>
    public void SetOccupied(bool occupied)
    {
        if (!IsServer || _occupied.Value == occupied) return;
        _occupied.Value = occupied;

        if (occupied)
        {
            // 연출/도착 대기 중에는 재진입 카운트다운을 만들지 않는다.
            if (IsEncounterBusy) return;
            BeginCountdown(countdownSeconds, CountdownCause.Pad);
            Edit.Log($"[BossTeleport] 카운트다운 시작 — {countdownSeconds:0}초 후 참가자 전원 보스룸 이동.", this);
        }
        else if (_pending != null)
        {
            // 🔴 강제 이동(제한시간 만료)은 패드 이탈로 취소되지 않는다.
            //    예전에는 `_pending` 하나를 두 경로가 공유해서, 강제 경고 중에 누가 패드에
            //    들어왔다 나가기만 해도 강제 이동이 조용히 사라졌다 — 제한시간이 통째로 우회됐다.
            if (_countdownCause == CountdownCause.Forced)
            {
                Edit.Log("[BossTeleport] 존 이탈 — 그러나 강제 이동 중이라 취소하지 않는다.", this);
                return;
            }

            CancelCountdown();
            Edit.Log("[BossTeleport] 존 이탈 — 카운트다운 취소.", this);
        }
    }

    private void BeginCountdown(float seconds, CountdownCause cause)
    {
        _countdownCause = cause;
        _teleportAt.Value = NetworkManager.ServerTime.Time + seconds;
        _pending = StartCoroutine(TeleportAfter(seconds));
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
    /// 🔴 패드 진입과 **같은 경로**로 합류한다 — 산개 좌표·도착 ACK·페이드·연출 진입이 전부
    ///    그 안에 있어서, 따로 구현하면 두 경로가 갈린다.
    ///
    /// 이미 진행 중(패드 카운트다운·연출·도착 대기)이면 <c>false</c> 를 돌려준다.
    /// 🔴 호출자는 그때 **만료 사실을 버리지 말고 유지**했다가, 그 진행이 취소되면 다시 불러야 한다.
    ///    안 그러면 "만료 직전에 패드를 밟았다 나가면 제한시간이 사라지는" 구멍이 생긴다.
    /// </summary>
    public bool ForceStartEncounter(float warnSeconds)
    {
        if (!IsServer || !IsSpawned)
            return false;

        // 호출자(BossTimerManager)가 만료 사실을 유지한 채 주기적으로 재시도한다 —
        // 여기서 로그를 남기면 대기 구간 내내 쌓이기만 한다. 조용히 거절한다.
        if (IsEncounterBusy || IsCountdownActive)
            return false;

        BeginCountdown(Mathf.Max(0f, warnSeconds), CountdownCause.Forced);
        Edit.Log($"[BossTeleport] 🔴 제한시간 만료 — {warnSeconds:0.#}초 후 강제 이동.", this);
        return true;
    }

    private IEnumerator TeleportAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);

        // 강제 이동이면 목숨이 남은 Soul 을 먼저 되살린다(팀장 확정 2026-09-20) —
        // Soul 만 도착하면 Director 가 참가자를 못 잡아 보스가 아예 안 뜬다.
        bool forced = _countdownCause == CountdownCause.Forced;

        _pending = null;
        _countdownCause = CountdownCause.None;

        if (forced) ReviveSoulsForForcedStart();

        TeleportAlivePlayers();
        _teleportAt.Value = 0d; // 표시 종료
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
            bool ok = revive != null && revive.TryCompleteReviveOnServer();

            Edit.Log($"[BossTeleport] 강제 개시 부활 — clientId={client.ClientId} 결과={(ok ? "성공" : "실패(목숨 없음)")}", this);
        }
    }

    private void TeleportAlivePlayers()
    {
        if (!IsServer) return;

        // ClientId 오름차순 정렬 — 슬롯 배정이 결정적이어야 재현·디버깅이 가능하다.
        var alive = new List<ulong>();
        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null) continue;

            // 🔴 이동 대상 = **PermanentDead 가 아닌 전원**(Alive · DeadPresentation · Soul).
            //    2026-09-20 팀장 개정 — 원래는 "생존자만" 이었다. 목숨이 남은 Soul 을 옛 맵에 두고 가면
            //    부활해도 보스전에 합류할 길이 없다. 보스방에서 목숨을 써서 살아나 싸우는 게 의도다.
            //    완전 사망자만 현 위치에 남는다.
            if (!IsTeleportParticipant(playerObject)) continue;

            alive.Add(client.ClientId);
        }

        alive.Sort();

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
            NetworkObject playerObject = NetworkManager.ConnectedClients[clientId].PlayerObject;
            GetArrivalPose(slot, out Vector3 destination, out Quaternion rotation);

            // 🔴 낙하 복구는 **안전지점으로 되돌리는 지연 코루틴**이다. 취소하지 않고 끌고 가면
            //    보스룸에 도착한 뒤 옛 안전지점으로 다시 끌려간다.
            playerObject.GetComponent<PlayerFallRecovery>()?.CancelRecoveryServer();

            // 서버가 오너가 아닌(클라이언트 소유) NetworkTransform에 Teleport를 호출하면 예외가 발생하여 루프가 중단됨.
            // 따라서 서버(호스트) 본인 것만 여기서 처리하고, 클라이언트는 아래 RPC 내부에서 각자 처리하도록 변경.
            if (playerObject.IsOwner && playerObject.TryGetComponent(out NetworkTransform netTransform))
                netTransform.Teleport(destination, rotation, playerObject.transform.localScale);

            TeleportOwnerClientRpc(destination, rotation, _encounterSequence, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });

            Edit.Log($"[BossTeleport] 슬롯 {slot} 배정 — clientId={clientId}, seq={_encounterSequence}", this);
        }
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
    // 도착 직후 페이드인 시작(이동 직전 암전은 로컬에서 만료 시각 기준 선제 진행).
    [ClientRpc]
    private void TeleportOwnerClientRpc(
        Vector3 destination, Quaternion rotation, uint sequence, ClientRpcParams rpcParams = default)
    {
        NetworkObject playerObject = NetworkManager.LocalClient?.PlayerObject;
        if (playerObject == null) return;

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

        var motor = playerObject.GetComponent<PlayerMotor>();
        if (motor != null)
        {
            motor.ClearKnockbackVelocity();
            motor.TeleportAuthoritative(destination);
            playerObject.transform.rotation = rotation;
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

        _fadeAlpha = 1f;
        _fadingIn = true;

        // 적용 완료를 서버에 통보 — 서버는 전원 ACK 이후에만 다음 단계로 넘어간다.
        ArrivalAppliedServerRpc(sequence);
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
        ArrivalAborted?.Invoke();
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
    private void Update()
    {
        if (_fadingIn)
        {
            _fadeAlpha -= Time.deltaTime / fadeInSeconds;
            if (_fadeAlpha <= 0f) { _fadeAlpha = 0f; _fadingIn = false; }
            return;
        }

        double teleportAt = _teleportAt.Value;
        if (teleportAt > 0d && IsLocalPlayerAlive())
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

    private bool IsLocalPlayerAlive()
    {
        NetworkManager nm = NetworkManager.Singleton;
        NetworkObject po = nm != null ? nm.LocalClient?.PlayerObject : null;
        if (po == null) return false;
        Unit unit = po.GetComponent<Unit>();
        return unit != null && unit.CurrentHealth > 0;
    }

    // 임시 표시(전 피어): 페이드 오버레이 + 카운트다운 숫자 — UI 시스템 합류 시 교체 전제.
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

        double teleportAt = _teleportAt.Value;
        if (teleportAt <= 0d || NetworkManager.Singleton == null) return;

        double remain = teleportAt - NetworkManager.Singleton.ServerTime.Time;
        if (remain <= 0d || remain > countdownSeconds + 0.5d) return;

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
}
