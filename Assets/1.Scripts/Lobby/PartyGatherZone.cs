using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 파티 집결 구역(할 일 86 PLAN §3.1·§7) — 바닥 사각형(BoxCollider) 안에 참여자 전원이 유지 시간 동안 모이면 서버가 확정을 1회 낸다.
/// 규칙은 <see cref="PartyGatherRules"/> 가 갖고, 여기는 위치·상태 수집, NGO 복제, 확정 이벤트만 한다.
/// <list type="bullet">
/// <item>위치: <see cref="BossEnterTrigger"/> 처럼 플레이어 본체(transform.position)가 박스 안인지 본다. 트리거 Enter/Exit 대신 서버가 매 프레임
/// 참여자 PlayerObject 를 직접 검사한다(콜라이더 여럿·사망·디스폰으로 Exit 가 안 오는 경우를 따로 정리할 필요가 없다). 박스 회전 반영.</item>
/// <item>상태: 쓰러짐·사망 = <see cref="PlayerLifeCycleController.State"/> ≠ Alive 또는 체력 0 이하. 진행 중 행동 = <see cref="Player.CurrentState"/> 가 Idle/Move 아님.</item>
/// <item>참여자: 기본 = 접속자 전원(접속·퇴장 자동 반영). <see cref="ServerSetParticipants"/> 로 외부(로비 훈련 진행기)가 명단을 넣으면 그 뒤로 접속은 자동 추가하지 않는다.
/// 연결 종료 확정(OnClientDisconnectCallback)은 어느 쪽이든 명단에서 뺀다.</item>
/// <item>복제: 활성·집결 n/m·카운트다운 종료 서버 시각·확정 여부. 모두 서버만 쓴다. 표시는 <see cref="PartyGatherStatusView"/>.</item>
/// <item>카운트다운 중에도 입력은 막지 않는다(사용자 결정 2026-10-22). 카운트다운 여부는 <see cref="IsCountdownActive"/> — 출격 구역 것을 접속 승인·캐릭터 잠금이 본다.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class PartyGatherZone : NetworkBehaviour
{
    private const string Tag = "[PartyGatherZone]";

    [Tooltip("전원 집결을 연속으로 유지해야 하는 시간(초, 서버 ServerTime 기준). PLAN-86 §7 = 전부 5초.")]
    [Min(0f)]
    [SerializeField] private float holdSeconds = (float)PartyGatherRules.DefaultHoldSeconds;

    [Tooltip("스폰 때 바로 판정을 켤지. 끄면 진행기가 ServerSetActive(true) 로 켠다(목표 전 다음 구역 비활성).")]
    [SerializeField] private bool activeOnSpawn;

    private readonly NetworkVariable<bool> isActive = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> isConfirmed = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> gatheredCount = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> participantCount = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 카운트다운이 끝나는 서버 시각(NetworkManager.ServerTime.Time, 초). 0 이하 = 카운트다운 없음.
    private readonly NetworkVariable<double> countdownEndServerTime = new NetworkVariable<double>(
        PartyGatherRules.NoCountdown, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private static readonly List<PartyGatherZone> SpawnedZones = new List<PartyGatherZone>();

    private BoxCollider _box;

    // 서버 전용
    private PartyGatherRules _rules;
    private bool _explicitParticipants;

    /// <summary>스폰된 집결 구역(활성·비활성 모두).</summary>
    public static IReadOnlyList<PartyGatherZone> Spawned => SpawnedZones;

    /// <summary>[서버] 전원 집결을 유지 시간만큼 채워 확정됐다 — 구역마다 1회(<see cref="ServerResetConfirmation"/> 전까지).</summary>
    public event Action<PartyGatherZone> ServerConfirmed;

    public bool IsActive => IsSpawned && isActive.Value;
    public bool IsConfirmed => IsSpawned && isConfirmed.Value;
    public int GatheredCount => IsSpawned ? gatheredCount.Value : 0;
    public int ParticipantCount => IsSpawned ? participantCount.Value : 0;
    public double CountdownEndServerTime => IsSpawned ? countdownEndServerTime.Value : PartyGatherRules.NoCountdown;
    public float HoldSeconds => holdSeconds;

    /// <summary>[전 피어] 카운트다운 중.</summary>
    public bool IsCountdownActive => IsActive && !IsConfirmed && countdownEndServerTime.Value > PartyGatherRules.NoCountdown;

    /// <summary>[전 피어] 남은 카운트다운 초(없으면 0).</summary>
    public double CountdownRemainingSeconds => IsSpawned && NetworkManager != null
        ? PartyGatherRules.RemainingSeconds(countdownEndServerTime.Value, NetworkManager.ServerTime.Time)
        : 0d;

    /// <summary>[서버] 현재 참여자 명단(서버가 아니면 빈 목록).</summary>
    public IReadOnlyList<ulong> ServerParticipants => _rules != null ? _rules.Participants : Array.Empty<ulong>();

    private void Awake()
    {
        _box = GetComponent<BoxCollider>();
        // 판정은 위치 검사라 물리 충돌이 필요 없다 — 막는 벽이 되지 않게 트리거로 둔다.
        _box.isTrigger = true;
    }

    private void Reset()
    {
        var box = GetComponent<BoxCollider>();
        if (box != null)
            box.isTrigger = true;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!SpawnedZones.Contains(this))
            SpawnedZones.Add(this);

        if (!IsServer)
            return;

        _rules = new PartyGatherRules(holdSeconds);
        _explicitParticipants = false;
        _rules.SetParticipants(NetworkManager.ConnectedClientsIds);
        _rules.SetActive(activeOnSpawn);
        NetworkManager.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        SyncReplicatedState();
    }

    public override void OnNetworkDespawn()
    {
        SpawnedZones.Remove(this);

        if (IsServer)
        {
            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            _rules = null;
        }

        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        SpawnedZones.Remove(this);
        base.OnDestroy();
    }

    /// <summary>[서버] 판정을 켜고 끈다. 끄면 카운트다운 0·표시 숨김. 확정 래치는 그대로.</summary>
    public void ServerSetActive(bool active)
    {
        if (!RequireServer(nameof(ServerSetActive)))
            return;

        _rules.SetActive(active);
        SyncReplicatedState();
        Debug.Log($"[SceneFlow] {Tag} {name} active={active}");
    }

    /// <summary>[서버] 유지 시간을 바꾼다(로비 훈련 진행기가 LobbyRules 값·영역 안 확인 0초를 넣는다). 이미 시작한 카운트다운 종료 시각은 그대로.</summary>
    public void ServerSetHoldSeconds(float seconds)
    {
        if (!RequireServer(nameof(ServerSetHoldSeconds)))
            return;

        holdSeconds = Mathf.Max(0f, seconds);
        _rules.HoldSeconds = holdSeconds;
    }

    /// <summary>[서버] 참여자 명단을 외부에서 정한다(로비 훈련 진행기). 이후 새 접속은 자동 추가하지 않는다. 구성이 바뀌면 카운트다운 0.</summary>
    public void ServerSetParticipants(IEnumerable<ulong> clientIds)
    {
        if (!RequireServer(nameof(ServerSetParticipants)))
            return;

        _explicitParticipants = true;
        if (_rules.SetParticipants(clientIds))
            SyncReplicatedState();
    }

    /// <summary>[서버] 명단을 기본값(현재 접속자 전원, 접속·퇴장 자동 반영)으로 되돌린다.</summary>
    public void ServerUseConnectedClients()
    {
        if (!RequireServer(nameof(ServerUseConnectedClients)))
            return;

        _explicitParticipants = false;
        if (_rules.SetParticipants(NetworkManager.ConnectedClientsIds))
            SyncReplicatedState();
    }

    /// <summary>[서버] 참여자 한 명 추가(늦은 참가자 등). 인원 변화라 카운트다운 0.</summary>
    public bool ServerAddParticipant(ulong clientId)
    {
        if (!RequireServer(nameof(ServerAddParticipant)) || !_rules.AddParticipant(clientId))
            return false;

        SyncReplicatedState();
        return true;
    }

    /// <summary>[서버] 참여자 한 명 제거. 인원 변화라 카운트다운 0.</summary>
    public bool ServerRemoveParticipant(ulong clientId)
    {
        if (!RequireServer(nameof(ServerRemoveParticipant)) || !_rules.RemoveParticipant(clientId))
            return false;

        SyncReplicatedState();
        return true;
    }

    /// <summary>[서버] 확정 래치를 풀어 같은 구역을 다시 쓰게 한다(판 끝 로비 복귀 등).</summary>
    public void ServerResetConfirmation()
    {
        if (!RequireServer(nameof(ServerResetConfirmation)))
            return;

        _rules.ResetConfirmation();
        SyncReplicatedState();
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer || _rules == null || !_rules.IsActive || _rules.IsConfirmed)
            return;

        _rules.HoldSeconds = holdSeconds;   // 인스펙터 튜닝 즉시 반영(시작한 카운트다운 종료 시각은 그대로)
        var participants = _rules.Participants;
        for (var i = 0; i < participants.Count; i++)
            _rules.SetMemberState(participants[i], ReadMemberState(participants[i]));

        var confirmed = _rules.Tick(NetworkManager.ServerTime.Time);
        SyncReplicatedState();
        if (!confirmed)
            return;

        Debug.Log($"[SceneFlow] {Tag} {name} 집결 확정 ({_rules.ParticipantCount}명, 유지 {holdSeconds}초)");
        ServerConfirmed?.Invoke(this);
    }

    // [서버] PlayerObject 가 없으면(스폰 전·교체 중) 밖으로 본다.
    private PartyGatherMemberState ReadMemberState(ulong clientId)
    {
        var playerObject = NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) ? client.PlayerObject : null;
        var player = playerObject != null ? playerObject.GetComponent<Player>() : null;
        if (player == null)
            return default;

        var lifeCycle = player.GetComponent<PlayerLifeCycleController>();
        var incapacitated = player.CurrentHealth <= 0 || (lifeCycle != null && lifeCycle.State != PlayerLifeState.Alive);
        var state = player.CurrentState;
        var busy = state != PlayerActionState.Idle && state != PlayerActionState.Move;
        return new PartyGatherMemberState(ContainsPoint(player.transform.position), incapacitated, busy);
    }

    /// <summary>박스 중심 월드 좌표 — 표시 대상 고르기(가까운 구역)용.</summary>
    public Vector3 WorldCenter
    {
        get
        {
            var box = _box != null ? _box : GetComponent<BoxCollider>();
            return box.transform.TransformPoint(box.center);
        }
    }

    /// <summary>월드 좌표가 박스(회전 포함) 안인가.</summary>
    public bool ContainsPoint(Vector3 worldPosition)
    {
        var box = _box != null ? _box : GetComponent<BoxCollider>();
        var local = box.transform.InverseTransformPoint(worldPosition) - box.center;
        var half = box.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (_rules == null || _explicitParticipants || !_rules.AddParticipant(clientId))
            return;

        SyncReplicatedState();
        Debug.Log($"[SceneFlow] {Tag} {name} 접속 client {clientId} — 참여자 {_rules.ParticipantCount}명, 카운트다운 초기화");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (_rules == null || !_rules.RemoveParticipant(clientId))
            return;

        SyncReplicatedState();
        Debug.Log($"[SceneFlow] {Tag} {name} 퇴장 client {clientId} — 참여자 {_rules.ParticipantCount}명, 카운트다운 초기화");
    }

    // 서버 규칙 상태 → 복제. 같은 값은 건드리지 않아 변경 이벤트를 줄인다.
    private void SyncReplicatedState()
    {
        if (isActive.Value != _rules.IsActive)
            isActive.Value = _rules.IsActive;
        if (isConfirmed.Value != _rules.IsConfirmed)
            isConfirmed.Value = _rules.IsConfirmed;
        if (gatheredCount.Value != _rules.GatheredCount)
            gatheredCount.Value = _rules.GatheredCount;
        if (participantCount.Value != _rules.ParticipantCount)
            participantCount.Value = _rules.ParticipantCount;
        if (countdownEndServerTime.Value != _rules.CountdownEndTime)
            countdownEndServerTime.Value = _rules.CountdownEndTime;
    }

    private bool RequireServer(string action)
    {
        if (IsServer && _rules != null)
            return true;

        Debug.LogWarning($"{Tag} {name} {action} 무시 — 서버가 아니거나 아직 스폰 전.", this);
        return false;
    }

    private void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null)
            return;

        // 에디트 모드 = 스폰 때 활성 여부, Play 중 = 복제된 활성 여부. 활성 초록 / 비활성 회색 / 확정 파랑.
        var active = Application.isPlaying && IsSpawned ? isActive.Value : activeOnSpawn;
        var color = Application.isPlaying && IsSpawned && isConfirmed.Value
            ? new Color(0.3f, 0.6f, 1f)
            : active ? new Color(0.2f, 1f, 0.3f) : new Color(0.6f, 0.6f, 0.6f);

        var previous = Gizmos.matrix;
        Gizmos.matrix = box.transform.localToWorldMatrix;
        Gizmos.color = new Color(color.r, color.g, color.b, 0.15f);
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = color;
        Gizmos.DrawWireCube(box.center, box.size);
        Gizmos.matrix = previous;
    }
}
