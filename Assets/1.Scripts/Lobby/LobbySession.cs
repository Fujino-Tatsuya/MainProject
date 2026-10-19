using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 로비 상태의 <b>소유자</b>(3.LobbyScene 씬 상주 NetworkObject, 서버 권한 — PLAN-48 §4.6·§4.7, C8).
/// 규칙은 <see cref="LobbyReadyRules"/> 가 갖고, 여기는 NGO 복제·RPC 검증·게임 시작 호출만 한다.
/// <list type="bullet">
/// <item>복제: 자리 목록 <see cref="LobbyPlayerEntry"/>(NetworkList) + 카운트다운 종료 서버 시각(NetworkVariable). 둘 다 서버만 쓴다.</item>
/// <item>시드: 스폰 때 접속자마다 <see cref="ServerCharacterSelectionStore"/> 의 선택으로 자리를 만들고, 없는 사람만 0번 — 판 뒤 로비 재진입에 캐릭터가 유지된다.
/// 새 LobbySession 은 전원 Ready=false 로 시작하므로 판마다 준비가 초기화된다.</item>
/// <item>접속·퇴장: 서버 콜백으로 자리를 더하고 뺀다(인원이 바뀌면 전원 Ready 초기화 + 카운트다운 취소).</item>
/// <item>카운트다운: NetworkManager.ServerTime 기준. 끝나면 래치를 걸고 <see cref="NetworkSessionLauncher.StartGameLoading"/> 을 정확히 1회 부른다.
/// NetworkLoadingFlowController.CanStartFromLobby 는 <see cref="CanStartGame"/>(래치)를 본다.</item>
/// <item>접속 승인: <see cref="NetworkSessionLauncher.LobbyCountdownActiveProvider"/> 를 스폰 때 걸고 디스폰 때 자기 것이면 푼다.</item>
/// </list>
/// 준비 요청은 서버가 준비 오브젝트(<see cref="LobbyInteractable"/>)와의 거리를 다시 본다(C10). 카운트다운 표시는 <see cref="LobbyCountdownView"/>,
/// "준비 n/m" 은 <see cref="LobbyReadyCountLabel"/>. 실제 프리팹 교체 재스폰은 C12 몫이다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbySession : NetworkBehaviour
{
    private const string Tag = "[LobbySession]";
    public const int DefaultCharacterId = 0;

    [SerializeField] private LobbyRules rules;
    [SerializeField] private CharacterRoster characterRoster;

    private readonly NetworkList<LobbyPlayerEntry> entries = new NetworkList<LobbyPlayerEntry>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 카운트다운이 끝나는 서버 시각(NetworkManager.ServerTime.Time, 초). 0 이하 = 카운트다운 없음. 서버만 쓴다.
    // 시작 래치가 걸린 뒤에도 값을 남긴다 — 로비가 내려갈 때까지 "진행 중"으로 보인다.
    private readonly NetworkVariable<double> countdownEndServerTime = new NetworkVariable<double>(
        LobbyReadyRules.NoCountdown, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 서버 전용
    private LobbyReadyRules _rules;
    private Func<bool> _countdownActiveProvider;
    private int _loggedCountdownSecond = -1;

    /// <summary>스폰된 로비 세션(없으면 null).</summary>
    public static LobbySession Active { get; private set; }

    /// <summary>[전 피어] 자리·카운트다운이 바뀌었다(스폰·디스폰 포함) — UI 갱신용.</summary>
    public static event Action StateChanged;

    public int EntryCount => IsSpawned ? entries.Count : 0;

    public LobbyPlayerEntry GetEntry(int index) => entries[index];

    public double CountdownEndServerTime => countdownEndServerTime.Value;

    /// <summary>[전 피어] 카운트다운 중(또는 시작 래치 뒤 로비가 내려가는 중).</summary>
    public bool IsCountdownActive => IsSpawned && countdownEndServerTime.Value > LobbyReadyRules.NoCountdown;

    /// <summary>[전 피어] 남은 카운트다운 초(없으면 0).</summary>
    public double CountdownRemainingSeconds => IsSpawned && NetworkManager != null
        ? LobbyReadyRules.RemainingSeconds(countdownEndServerTime.Value, NetworkManager.ServerTime.Time)
        : 0d;

    /// <summary>[서버] 카운트다운이 끝나 시작 래치가 걸렸다 — 로딩 흐름의 로비 시작 조건.</summary>
    public bool CanStartGame => IsServer && _rules != null && _rules.HasStarted;

    public bool TryGetEntry(ulong clientId, out LobbyPlayerEntry entry)
    {
        if (IsSpawned)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].ClientId == clientId)
                {
                    entry = entries[i];
                    return true;
                }
            }
        }

        entry = default;
        return false;
    }

    /// <summary>[로컬] 내 준비 토글 요청 — 서버가 규칙으로 검증한다.</summary>
    public void RequestToggleReady()
    {
        if (IsSpawned)
            ToggleReadyServerRpc();
    }

    /// <summary>[로컬] 내 캐릭터 변경 요청 — 서버가 규칙·로스터로 검증한다.</summary>
    public void RequestCharacter(int characterId)
    {
        if (IsSpawned)
            RequestCharacterServerRpc(characterId);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Active = this;
        entries.OnListChanged += HandleEntriesChanged;
        countdownEndServerTime.OnValueChanged += HandleCountdownChanged;

        if (IsServer)
        {
            if (rules == null)
                Debug.LogWarning($"{Tag} LobbyRules 가 비었다 — 카운트다운 0초·쿨다운 0초로 동작한다.", this);
            if (characterRoster == null)
                Debug.LogWarning($"{Tag} CharacterRoster 가 비었다 — 캐릭터 요청을 모두 거부한다.", this);

            _rules = new LobbyReadyRules(DemoJoinRule.MaxPlayers);
            foreach (var clientId in NetworkManager.ConnectedClientsIds)
                SeedPlayer(clientId);

            NetworkManager.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            _countdownActiveProvider = () => _rules != null && _rules.IsCountdownActive;
            NetworkSessionLauncher.LobbyCountdownActiveProvider = _countdownActiveProvider;
            SyncReplicatedState();
            Debug.Log($"[SceneFlow] {Tag} 서버 스폰 — 자리 {_rules.Seats.Count}개 시드");
        }

        StateChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        entries.OnListChanged -= HandleEntriesChanged;
        countdownEndServerTime.OnValueChanged -= HandleCountdownChanged;

        if (IsServer)
        {
            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            if (NetworkSessionLauncher.LobbyCountdownActiveProvider == _countdownActiveProvider)
                NetworkSessionLauncher.LobbyCountdownActiveProvider = null;
            _countdownActiveProvider = null;
            _rules = null;
        }

        if (Active == this)
            Active = null;
        _loggedCountdownSecond = -1;
        StateChanged?.Invoke();
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (Active == this)
            Active = null;
        base.OnDestroy();
    }

    private void Update()
    {
        if (!IsSpawned)
            return;

        LogCountdownSecond();

        if (IsServer && _rules != null && _rules.TryConsumeStart(NetworkManager.ServerTime.Time))
            StartGame();
    }

    // 씬 상주 오브젝트라 오너가 없다 — 누구나 호출하고 보낸 사람은 SenderClientId 로 가린다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ToggleReadyServerRpc(RpcParams p = default)
    {
        if (_rules == null)
            return;

        // 준비 오브젝트 거리 재검증(C10) — 클라 프롬프트 판정만 믿으면 어디서든 준비할 수 있다.
        var clientId = p.Receive.SenderClientId;
        if (!IsNearReadyObject(clientId))
            return;

        if (!_rules.ToggleReady(clientId, NetworkManager.ServerTime.Time, CountdownSeconds))
        {
            Debug.LogWarning($"{Tag} 준비 토글 거부 client {clientId} — 자리 없음 또는 시작 래치 뒤.", this);
            return;
        }

        SyncReplicatedState();
        _rules.TryGetSeat(clientId, out var seat);
        Debug.Log($"[SceneFlow] {Tag} client {clientId} ready={seat.Ready} ({_rules.ReadyCount}/{_rules.Seats.Count}) countdown={_rules.IsCountdownActive}");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestCharacterServerRpc(int characterId, RpcParams p = default)
    {
        if (_rules == null)
            return;

        // 캐릭터 선택 오브젝트 거리 재검증 자리 — C10. 실제 프리팹 재스폰 교체는 C12(지금은 저장소·자리만 바꾼다).
        var clientId = p.Receive.SenderClientId;
        var available = IsCharacterAvailable(characterId);
        var reject = _rules.TryChangeCharacter(clientId, characterId, available, NetworkManager.ServerTime.Time, SwapCooldownSeconds);
        if (reject != LobbyCharacterChangeReject.None)
        {
            Debug.LogWarning($"{Tag} 캐릭터 요청 거부 client {clientId} → {characterId}: {reject}", this);
            return;
        }

        ServerCharacterSelectionStore.Set(clientId, characterId);
        SyncReplicatedState();
        Debug.Log($"[SceneFlow] {Tag} client {clientId} character={characterId}");
    }

    // [서버] 보낸 사람 PlayerObject 가 준비 오브젝트(LobbyInteractable Ready) 반경 + 여유 안인가 — 평면 거리. 밖이면 경고하고 거부한다.
    private bool IsNearReadyObject(ulong clientId)
    {
        var player = NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) ? client.PlayerObject : null;
        if (player == null)
        {
            Debug.LogWarning($"{Tag} 준비 토글 거부 client {clientId} — PlayerObject 없음(로비 스폰 전).", this);
            return false;
        }

        if (LobbyInteractable.AnyWithinServerReach(LobbyInteractableKind.Ready, player.transform.position, out var distance))
            return true;

        Debug.LogWarning(distance < float.MaxValue
            ? $"{Tag} 준비 토글 거부 client {clientId} — 준비 오브젝트까지 {distance:F2}m(반경 + 여유 {LobbyInteractable.ServerReachTolerance}m 밖). 서버가 본 위치 {player.transform.position}."
            : $"{Tag} 준비 토글 거부 client {clientId} — 씬에 준비 오브젝트(LobbyInteractable Ready)가 없다.", this);
        return false;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (_rules == null || !SeedPlayer(clientId))
            return;

        SyncReplicatedState();
        Debug.Log($"[SceneFlow] {Tag} 접속 client {clientId} — 자리 {_rules.Seats.Count}개, 전원 Ready 초기화");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (_rules == null || !_rules.RemovePlayer(clientId))
            return;

        SyncReplicatedState();
        Debug.Log($"[SceneFlow] {Tag} 퇴장 client {clientId} — 자리 {_rules.Seats.Count}개, 전원 Ready 초기화");
    }

    // 저장소에 있는(그리고 아직 쓸 수 있는) 캐릭터로, 없으면 0번으로 자리를 만든다. 0번도 저장소에 적어 스폰·playerId 기억과 맞춘다.
    private bool SeedPlayer(ulong clientId)
    {
        var characterId = ServerCharacterSelectionStore.TryGet(clientId, out var stored) && IsCharacterAvailable(stored)
            ? stored
            : DefaultCharacterId;

        if (!_rules.AddPlayer(clientId, characterId))
            return false;

        ServerCharacterSelectionStore.Set(clientId, characterId);
        return true;
    }

    private void StartGame()
    {
        var launcher = NetworkManager.GetComponent<NetworkSessionLauncher>();
        Debug.Log($"[SceneFlow] {Tag} 카운트다운 종료 — 게임 시작 1회 hasLauncher={launcher != null}");
        if (launcher == null)
        {
            Debug.LogError($"{Tag} NetworkSessionLauncher 가 없어 게임을 시작하지 못했다.", this);
            return;
        }

        launcher.StartGameLoading();
    }

    // 서버 규칙 상태 → 복제. 같은 행은 건드리지 않아 변경 이벤트를 줄인다.
    private void SyncReplicatedState()
    {
        var seats = _rules.Seats;
        for (var i = 0; i < seats.Count; i++)
        {
            var entry = new LobbyPlayerEntry(seats[i].ClientId, seats[i].CharacterId, seats[i].Ready);
            if (i < entries.Count)
            {
                if (!entries[i].Equals(entry))
                    entries[i] = entry;
            }
            else
            {
                entries.Add(entry);
            }
        }

        while (entries.Count > seats.Count)
            entries.RemoveAt(entries.Count - 1);

        if (countdownEndServerTime.Value != _rules.CountdownEndTime)
            countdownEndServerTime.Value = _rules.CountdownEndTime;
    }

    // 초가 바뀔 때마다 전 피어에 로그 한 줄 + StateChanged(표시는 LobbyCountdownView 가 매 프레임 직접 읽는다).
    private void LogCountdownSecond()
    {
        if (!IsCountdownActive)
        {
            _loggedCountdownSecond = -1;
            return;
        }

        var second = (int)Math.Ceiling(CountdownRemainingSeconds);
        if (second == _loggedCountdownSecond)
            return;

        _loggedCountdownSecond = second;
        Debug.Log($"[SceneFlow] {Tag} 카운트다운 {second}");
        StateChanged?.Invoke();
    }

    private bool IsCharacterAvailable(int characterId)
    {
        return characterRoster != null && characterRoster.TryGetAvailableCharacter(characterId, out _);
    }

    private double CountdownSeconds => rules != null ? rules.CountdownSeconds : 0d;
    private double SwapCooldownSeconds => rules != null ? rules.CharacterSwapCooldownSeconds : 0d;

    private void HandleEntriesChanged(NetworkListEvent<LobbyPlayerEntry> _) => StateChanged?.Invoke();

    private void HandleCountdownChanged(double previous, double current) => StateChanged?.Invoke();
}
