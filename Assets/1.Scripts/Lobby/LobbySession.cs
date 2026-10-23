using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 로비 상태의 <b>소유자</b>(3.LobbyScene 씬 상주 NetworkObject, 서버 권한 — PLAN-48 §4.6·§4.7, C8).
/// 규칙은 <see cref="LobbyReadyRules"/> 가 갖고, 여기는 NGO 복제·RPC 검증만 한다.
/// <list type="bullet">
/// <item>복제: 자리 목록 <see cref="LobbyPlayerEntry"/>(NetworkList) + 카운트다운 종료 서버 시각(NetworkVariable). 둘 다 서버만 쓴다.</item>
/// <item>시드: 스폰 때 접속자마다 <see cref="ServerCharacterSelectionStore"/> 의 선택으로 자리를 만들고, 없는 사람만 0번 — 판 뒤 로비 재진입에 캐릭터가 유지된다.</item>
/// <item>접속·퇴장: 서버 콜백으로 자리를 더하고 뺀다.</item>
/// <item>게임 시작·접속 승인은 여기 없다(할 일 86 T2b): 출격 집결 확정 → <see cref="LobbyTrainingDirector"/> 가 StartGameLoading 1회·승인 제공자를 맡는다.</item>
/// <item>캐릭터 잠금: 요청마다 <see cref="LobbyTrainingDirector.CharacterChangeBlockFor"/>(훈련 명단 안·출격 카운트다운·출격 단계)로도 거부한다.</item>
/// </list>
/// ⚠ 준비 토글(<see cref="RequestToggleReady"/>)·<see cref="IsCountdownActive"/> 는 qa/LobbyDemo 하네스 호환으로만 남겼다 — 씬에 Ready 오브젝트가 없어
/// 서버 거리 재검증에서 늘 거부되고, 준비 카운트다운은 더 이상 게임을 시작하지 않는다(PLAN-86 §7-2 Ready 패드 = 출격 집결).
/// 캐릭터 요청은 서버가 캐릭터 선택 오브젝트(<see cref="LobbyInteractable"/>)와의 거리를 다시 본다(C12).
/// 캐릭터 교체 = <see cref="LobbyPlayerSpawner.TrySwapCharacter"/>(디스폰 → 같은 자리 새 Variant, C12).
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbySession : NetworkBehaviour
{
    private const string Tag = "[LobbySession]";
    public const int DefaultCharacterId = 0;

    [SerializeField] private LobbyRules rules;
    [SerializeField] private CharacterRoster characterRoster;
    [Tooltip("캐릭터 교체(디스폰 → 같은 자리 새 Variant)를 맡는 같은 씬의 스포너.")]
    [SerializeField] private LobbyPlayerSpawner playerSpawner;

    private readonly NetworkList<LobbyPlayerEntry> entries = new NetworkList<LobbyPlayerEntry>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 준비 카운트다운이 끝나는 서버 시각(NetworkManager.ServerTime.Time, 초). 0 이하 = 카운트다운 없음. 서버만 쓴다(qa 호환용, 게임 시작과 무관).
    private readonly NetworkVariable<double> countdownEndServerTime = new NetworkVariable<double>(
        LobbyReadyRules.NoCountdown, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 서버 전용
    private LobbyReadyRules _rules;
    private int _loggedCountdownSecond = -1;

    /// <summary>스폰된 로비 세션(없으면 null).</summary>
    public static LobbySession Active { get; private set; }

    /// <summary>[전 피어] 자리·카운트다운이 바뀌었다(스폰·디스폰 포함) — UI 갱신용.</summary>
    public static event Action StateChanged;

    /// <summary>[요청한 클라만] 내 캐릭터 교체 요청을 서버가 거부했다 — 피드백 UI 를 붙일 자리(지금은 로그만).</summary>
    public static event Action<LobbyCharacterChangeReject> CharacterRequestRejected;

    public int EntryCount => IsSpawned ? entries.Count : 0;

    public LobbyPlayerEntry GetEntry(int index) => entries[index];

    public double CountdownEndServerTime => countdownEndServerTime.Value;

    /// <summary>[전 피어] 준비 카운트다운 중 — qa 호환용. 출격 집결 카운트다운은 <see cref="LobbyTrainingDirector.IsSortieCountdownActive"/>.</summary>
    public bool IsCountdownActive => IsSpawned && countdownEndServerTime.Value > LobbyReadyRules.NoCountdown;

    /// <summary>[전 피어] 남은 카운트다운 초(없으면 0).</summary>
    public double CountdownRemainingSeconds => IsSpawned && NetworkManager != null
        ? LobbyReadyRules.RemainingSeconds(countdownEndServerTime.Value, NetworkManager.ServerTime.Time)
        : 0d;

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

    /// <summary>[로컬] 내 준비 토글 요청 — qa/LobbyDemo 하네스 호환용. 씬에 Ready 오브젝트가 없어 서버가 거리 검증에서 거부한다.</summary>
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
    }

    // 씬 상주 오브젝트라 오너가 없다 — 누구나 호출하고 보낸 사람은 SenderClientId 로 가린다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ToggleReadyServerRpc(RpcParams p = default)
    {
        if (_rules == null)
            return;

        // 준비 오브젝트 거리 재검증(C10) — 클라 프롬프트 판정만 믿으면 어디서든 준비할 수 있다.
        var clientId = p.Receive.SenderClientId;
        if (!IsNearInteractable(LobbyInteractableKind.Ready, clientId, "준비 토글"))
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

    // 승인 순서: 거리(C12) → 훈련 진행기(출격 단계·출격 카운트다운·훈련 명단 잠금) → 규칙(준비·카운트다운·교체 중·쿨다운·로스터) → 프리팹 교체(디스폰 → 같은 자리 새 Variant) → 저장소·자리 반영.
    // 교체가 실패하면 자리·저장소를 바꾸지 않는다. 거부는 보낸 사람에게만 알린다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestCharacterServerRpc(int characterId, RpcParams p = default)
    {
        if (_rules == null)
            return;

        var clientId = p.Receive.SenderClientId;
        if (!IsNearInteractable(LobbyInteractableKind.CharacterSelect, clientId, "캐릭터 요청"))
        {
            RejectCharacter(clientId, characterId, LobbyCharacterChangeReject.TooFar);
            return;
        }

        // 늦은 참가자(훈련·브리핑 중 접속 — 훈련 명단 밖)는 바꿀 수 있다. 같은 캐릭터 재요청도 여기서 거부된다(잠금 피드백 우선).
        var training = LobbyTrainingDirector.Active;
        var trainingReject = training != null ? training.CharacterChangeBlockFor(clientId) : LobbyCharacterChangeReject.None;
        if (trainingReject != LobbyCharacterChangeReject.None)
        {
            RejectCharacter(clientId, characterId, trainingReject);
            return;
        }

        var now = NetworkManager.ServerTime.Time;
        var reject = _rules.CanChangeCharacter(clientId, now, SwapCooldownSeconds);
        if (reject == LobbyCharacterChangeReject.None && !IsCharacterAvailable(characterId))
            reject = LobbyCharacterChangeReject.UnavailableCharacter;
        if (reject != LobbyCharacterChangeReject.None)
        {
            RejectCharacter(clientId, characterId, reject);
            return;
        }

        if (_rules.TryGetSeat(clientId, out var seat) && seat.CharacterId == characterId)
            return;   // 같은 캐릭터 — 교체도 쿨다운도 없다

        if (playerSpawner == null)
        {
            Debug.LogError($"{Tag} LobbyPlayerSpawner 가 비어 캐릭터를 교체하지 못했다.", this);
            RejectCharacter(clientId, characterId, LobbyCharacterChangeReject.SwapFailed);
            return;
        }

        _rules.BeginSwap(clientId);
        if (!playerSpawner.TrySwapCharacter(clientId, characterId, out var failure))
        {
            _rules.EndSwap(clientId);
            Debug.LogWarning($"{Tag} client {clientId} → {characterId} 교체 실패: {failure}", this);
            RejectCharacter(clientId, characterId, LobbyCharacterChangeReject.SwapFailed);
            return;
        }

        _rules.CommitCharacter(clientId, characterId, now);
        ServerCharacterSelectionStore.Set(clientId, characterId);   // playerId 기억(재접속 유지)도 여기서 갱신된다
        SyncReplicatedState();
        StartCoroutine(EndSwapNextFrame(clientId));
        Debug.Log($"[SceneFlow] {Tag} client {clientId} character={characterId} (교체 완료)");
    }

    // 옛 오브젝트는 이 프레임 끝에 파괴된다 — 그때까지 같은 사람의 다음 교체를 SwapInProgress 로 막는다.
    private IEnumerator EndSwapNextFrame(ulong clientId)
    {
        yield return null;
        _rules?.EndSwap(clientId);
    }

    private void RejectCharacter(ulong clientId, int characterId, LobbyCharacterChangeReject reason)
    {
        Debug.LogWarning($"{Tag} 캐릭터 요청 거부 client {clientId} → {characterId}: {reason}", this);
        CharacterRequestRejectedClientRpc(reason, RpcTarget.Single(clientId, RpcTargetUse.Temp));
    }

    // 거부 피드백 — 보낸 사람만. 지금은 로그 + 이벤트뿐(패널은 이미 닫혀 있다, character-select-panel.md "붙이는 법" 5).
    [Rpc(SendTo.SpecifiedInParams)]
    private void CharacterRequestRejectedClientRpc(LobbyCharacterChangeReject reason, RpcParams p = default)
    {
        Debug.LogWarning($"{Tag} 내 캐릭터 교체 요청이 거부됐다: {reason}");
        CharacterRequestRejected?.Invoke(reason);
    }

    // [서버] 보낸 사람 PlayerObject 가 그 종류 오브젝트(LobbyInteractable) 반경 + 여유 안인가 — 평면 거리. 밖이면 경고하고 거부한다.
    private bool IsNearInteractable(LobbyInteractableKind kind, ulong clientId, string action)
    {
        var player = NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) ? client.PlayerObject : null;
        if (player == null)
        {
            Debug.LogWarning($"{Tag} {action} 거부 client {clientId} — PlayerObject 없음(로비 스폰 전).", this);
            return false;
        }

        if (LobbyInteractable.AnyWithinServerReach(kind, player.transform.position, out var distance))
            return true;

        Debug.LogWarning(distance < float.MaxValue
            ? $"{Tag} {action} 거부 client {clientId} — {kind} 오브젝트까지 {distance:F2}m(반경 + 여유 {LobbyInteractable.ServerReachTolerance}m 밖). 서버가 본 위치 {player.transform.position}."
            : $"{Tag} {action} 거부 client {clientId} — 씬에 {kind} 오브젝트(LobbyInteractable)가 없다.", this);
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

    // 초가 바뀔 때마다 전 피어에 로그 한 줄 + StateChanged(준비 카운트다운 — qa 호환, 화면 표시는 없다).
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
