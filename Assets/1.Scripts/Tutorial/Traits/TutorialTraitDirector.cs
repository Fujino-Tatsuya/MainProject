using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>[서버] 특성 1장 획득 통지.</summary>
public readonly struct TutorialTraitChosen
{
    public readonly ulong ClientId;
    public readonly int TraitIndex;
    public readonly TutorialTraitDefinition Trait;
    /// <summary>true = 교착 방지·타이머 만료로 서버가 대신 골랐다. false = 플레이어가 직접 골랐다.</summary>
    public readonly bool Auto;
    public readonly int Round;

    public TutorialTraitChosen(ulong clientId, int traitIndex, TutorialTraitDefinition trait, bool auto, int round)
    {
        ClientId = clientId;
        TraitIndex = traitIndex;
        Trait = trait;
        Auto = auto;
        Round = round;
    }
}

/// <summary>
/// 튜토리얼 복도 특성 선택의 <b>상태 소유자</b>(씬 상주 NetworkObject, 서버 권한 — 할 일 15 T1).
/// 규칙은 <see cref="TutorialTraitSelectionModel"/> 이 갖고, 여기는 NGO 동기화·RPC 검증·효과 적용만 한다.
///
/// 동기화: 플레이어별 "제시 후보"와 "획득 특성"을 행 단위로 <see cref="NetworkList{T}"/> 에 복제한다
/// (<see cref="ZoneBridgeGateManager"/> 와 같은 씬 매니저 + NetworkList 패턴). 클라는 자기 clientId 행만 읽으면 된다.
/// 제시 행은 아직 안 고른 동안만 존재한다 — 고르면 사라지고 획득 행이 생긴다.
///
/// 효과: 배율 특성 = 영구(duration 0) 상태이상, 특성마다 다른 출처 id 라 서로 곱해진다.
/// 쿨감 특성 = 그 플레이어의 <see cref="Player.ServerAttackLanded"/> 를 구독한다.
/// 이 컴포넌트를 씬에 두지 않으면 아무 일도 하지 않는다(튜토리얼 외 영향 없음).
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialTraitDirector : NetworkBehaviour
{
    private const string Tag = "[TutorialTrait]";

    // 특성 상태이상의 출처 id 대역 — NetworkObjectId(작은 수)와 겹치지 않게 높은 대역을 쓴다. + 풀 인덱스.
    private const ulong TraitSourceIdBase = 0x5452_4149_5400_0000UL;

    public enum EntryKind : byte
    {
        Offered = 0,
        Acquired = 1,
    }

    /// <summary>복제 행 하나 = (플레이어, 특성 인덱스, 제시/획득).</summary>
    public struct Entry : INetworkSerializable, IEquatable<Entry>
    {
        public ulong ClientId;
        public int TraitIndex;
        public EntryKind Kind;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref ClientId);
            s.SerializeValue(ref TraitIndex);
            s.SerializeValue(ref Kind);
        }

        public bool Equals(Entry o) => ClientId == o.ClientId && TraitIndex == o.TraitIndex && Kind == o.Kind;
    }

    private sealed class CooldownHook
    {
        public Player Player;
        public Action<PlayerAttackLanded> Handler;
    }

    [SerializeField] private TutorialTraitPool pool;

    private readonly NetworkList<Entry> entries = new NetworkList<Entry>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> round = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 서버 전용
    private TutorialTraitSelectionModel model;
    private System.Random random;
    private readonly List<CooldownHook> cooldownHooks = new List<CooldownHook>();
    private readonly List<ulong> clientScratch = new List<ulong>();
    private bool roundCompletionRaised = true;

    public static TutorialTraitDirector Instance { get; private set; }

    public TutorialTraitPool Pool => pool;

    /// <summary>[전 피어] 현재 제시 라운드 번호(0 = 아직 없음).</summary>
    public int Round => round.Value;

    /// <summary>[서버] 특성 1장 획득(수동·자동).</summary>
    public event Action<TutorialTraitChosen> ServerTraitChosen;

    /// <summary>[서버] 현재 라운드 참가자 전원이 골랐다(0장 제시·이탈 포함). 인자 = 라운드 번호.</summary>
    public event Action<int> ServerRoundCompleted;

    /// <summary>[전 피어] 복제 상태(제시·획득·라운드)가 바뀌었다 — UI 갱신용.</summary>
    public event Action ClientStateChanged;

    private void Awake() => Instance = this;

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        entries.OnListChanged += HandleEntriesChanged;
        round.OnValueChanged += HandleRoundChanged;

        if (!IsServer)
            return;

        if (pool == null)
            Edit.LogError($"{Tag} 특성 풀이 비었다 — 제시가 동작하지 않는다.", this);

        model = new TutorialTraitSelectionModel(pool != null ? pool.Count : 0, CollectUnusableIndices());
        random = new System.Random();
        NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        entries.OnListChanged -= HandleEntriesChanged;
        round.OnValueChanged -= HandleRoundChanged;

        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;

        for (int i = 0; i < cooldownHooks.Count; i++)
        {
            if (cooldownHooks[i].Player != null)
                cooldownHooks[i].Player.ServerAttackLanded -= cooldownHooks[i].Handler;
        }

        cooldownHooks.Clear();
        model = null;
        base.OnNetworkDespawn();
    }

    private bool CanServe => IsSpawned && IsServer && model != null;

    // ── 서버 API ───────────────────────────────────────────────────────────

    /// <summary>
    /// [서버] 접속 중인 전원에게 새 라운드 후보를 제시한다. 이미 얻은 특성은 빠지고, 남은 후보가 적으면 남은 만큼만.
    /// false = 아무에게도 1장도 제시하지 못했다(풀 소진·미스폰) → 호출자는 선택 단계를 건너뛴다.
    /// </summary>
    public bool StartOfferRoundServer()
    {
        if (!CanServe)
            return false;

        clientScratch.Clear();
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            clientScratch.Add(clientId);

        return StartOfferRoundServer(clientScratch);
    }

    /// <summary>[서버] 지정한 플레이어들에게 새 라운드 후보를 제시한다. 반환값은 위와 같다.</summary>
    public bool StartOfferRoundServer(IEnumerable<ulong> clientIds)
    {
        if (!CanServe)
        {
            Edit.LogWarning($"{Tag} 제시 무시 — 서버에서 스폰된 뒤에만 제시할 수 있다.", this);
            return false;
        }

        int offerCount = pool != null ? pool.OfferCount : 0;
        bool anyOffered = model.StartRound(clientIds, offerCount, random.Next);
        round.Value = model.Round;
        roundCompletionRaised = false;
        RebuildEntries();

        Edit.Log($"{Tag} 라운드 {model.Round} 제시 — 1장 이상 받은 플레이어 {(anyOffered ? "있음" : "없음(건너뜀)")}.", this);
        CheckRoundCompleted();
        return anyOffered;
    }

    /// <summary>
    /// [서버] 아직 안 고른 플레이어에게 제시된 후보 중 무작위 1장을 대신 고른다(교착 방지 60초·타이머 만료용).
    /// 대상이 아니면(이미 골랐음·제시 없음) false.
    /// </summary>
    public bool AutoChooseServer(ulong clientId)
    {
        if (!CanServe)
            return false;

        int traitIndex = model.PickAutoChoice(clientId, random.Next);
        return traitIndex >= 0 && CommitChoice(clientId, traitIndex, auto: true);
    }

    /// <summary>[서버] 이번 라운드에서 아직 안 고른 전원을 자동 획득 처리한다. 처리한 인원 수를 반환한다.</summary>
    public int AutoChooseAllPendingServer()
    {
        if (!CanServe)
            return 0;

        model.GetPendingClients(clientScratch);
        // CommitChoice 가 이벤트를 부르므로 스냅샷을 복사해 돈다.
        ulong[] pending = clientScratch.ToArray();
        int count = 0;
        for (int i = 0; i < pending.Length; i++)
        {
            if (AutoChooseServer(pending[i]))
                count++;
        }

        return count;
    }

    /// <summary>[서버] 현재 라운드 참가자 전원이 골랐는가. 라운드가 없었으면 true.</summary>
    public bool IsRoundCompleteServer => model == null || model.IsRoundComplete();

    /// <summary>[서버] 그 플레이어가 이번 라운드에서 아직 안 골랐는가.</summary>
    public bool HasPendingChoiceServer(ulong clientId) => model != null && model.HasPendingChoice(clientId);

    // ── 클라 API ───────────────────────────────────────────────────────────

    /// <summary>[로컬 플레이어] 제시된 후보 중 하나를 고른다. 판정은 서버가 한다.</summary>
    public void RequestChoose(int traitIndex)
    {
        if (!IsSpawned)
        {
            Edit.LogWarning($"{Tag} 선택 무시 — 아직 네트워크 스폰 전이다.", this);
            return;
        }

        ChooseTraitServerRpc(traitIndex);
    }

    /// <summary>[전 피어] 그 플레이어에게 지금 제시된 후보(아직 안 골랐을 때만, 제시 순서).</summary>
    public void GetOffered(ulong clientId, List<int> result) => CollectRows(clientId, EntryKind.Offered, result);

    /// <summary>[전 피어] 그 플레이어가 얻은 특성(획득 순서).</summary>
    public void GetAcquired(ulong clientId, List<int> result) => CollectRows(clientId, EntryKind.Acquired, result);

    /// <summary>[전 피어] 그 플레이어가 지금 골라야 하는가(제시 행이 있다).</summary>
    public bool HasPendingChoice(ulong clientId)
    {
        if (!IsSpawned)
            return false;

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].ClientId == clientId && entries[i].Kind == EntryKind.Offered)
                return true;
        }

        return false;
    }

    // ── 선택 ───────────────────────────────────────────────────────────────

    // 씬 상주 오브젝트라 오너가 없다 — 누구나 호출하고 보낸 사람은 SenderClientId 로 가린다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ChooseTraitServerRpc(int traitIndex, RpcParams p = default)
    {
        if (model != null)
            CommitChoice(p.Receive.SenderClientId, traitIndex, auto: false);
    }

    // 선택 검증(제시된 후보인가·이미 골랐나)은 모델이 한다. 위반은 무시하고 로그만 남긴다.
    private bool CommitChoice(ulong clientId, int traitIndex, bool auto)
    {
        TutorialTraitChoiceResult result = model.TryChoose(clientId, traitIndex);
        if (result != TutorialTraitChoiceResult.Accepted)
        {
            Edit.LogWarning($"{Tag} {(auto ? "자동 " : "")}선택 거부: client {clientId} 특성 {traitIndex} — {result}.", this);
            return false;
        }

        TutorialTraitDefinition trait = pool != null ? pool.Get(traitIndex) : null;
        ApplyEffect(clientId, traitIndex, trait);
        RebuildEntries();

        Edit.Log($"{Tag} client {clientId} 특성 획득 '{trait?.Id}'({traitIndex}) {(auto ? "자동" : "수동")} — 라운드 {model.Round}.", this);
        ServerTraitChosen?.Invoke(new TutorialTraitChosen(clientId, traitIndex, trait, auto, model.Round));
        CheckRoundCompleted();
        return true;
    }

    private void CheckRoundCompleted()
    {
        if (roundCompletionRaised || model == null || model.Round == 0 || !model.IsRoundComplete())
            return;

        roundCompletionRaised = true;
        ServerRoundCompleted?.Invoke(model.Round);
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (model == null)
            return;

        model.RemovePlayer(clientId);
        for (int i = cooldownHooks.Count - 1; i >= 0; i--)
        {
            if (cooldownHooks[i].Player == null)
                cooldownHooks.RemoveAt(i);
        }

        RebuildEntries();
        CheckRoundCompleted();
    }

    // ── 효과 ───────────────────────────────────────────────────────────────

    private void ApplyEffect(ulong clientId, int traitIndex, TutorialTraitDefinition trait)
    {
        if (trait == null)
        {
            Edit.LogError($"{Tag} 특성 {traitIndex} 정의가 없다 — 효과 없이 획득만 기록한다.", this);
            return;
        }

        Player player = FindPlayer(clientId);
        if (player == null)
        {
            Edit.LogWarning($"{Tag} client {clientId} 의 Player 를 못 찾았다 — '{trait.Id}' 효과를 적용하지 못했다.", this);
            return;
        }

        switch (trait.EffectKind)
        {
            case TutorialTraitEffectKind.AttackDamageMultiplier:
                ApplyStatModifier(player, StatusEffectType.AttackDamageModifier, trait, traitIndex);
                break;
            case TutorialTraitEffectKind.MoveSpeedMultiplier:
                ApplyStatModifier(player, StatusEffectType.MoveSpeedModifier, trait, traitIndex);
                break;
            case TutorialTraitEffectKind.CooldownReductionOnHit:
                HookCooldownReduction(player, trait);
                break;
        }
    }

    private void ApplyStatModifier(Player player, StatusEffectType type, TutorialTraitDefinition trait, int traitIndex)
    {
        StatusEffectController effects = player.StatusEffects;
        if (effects == null)
        {
            Edit.LogWarning($"{Tag} '{player.name}' 에 StatusEffectController 가 없다 — '{trait.Id}' 미적용.", player);
            return;
        }

        ulong sourceId = SourceIdOf(traitIndex);
        // duration 0 = 영구. 출처가 특성마다 달라 다른 배율 특성과 곱해진다.
        effects.Apply(type, trait.StatMultiplier, 0f, sourceId);
        if (effects.GetStackCount(type, sourceId) == 0)
            Edit.LogWarning($"{Tag} '{trait.Id}' 상태이상이 거부됐다(연출 잠금 중 등) — 효과가 없다.", player);
    }

    private void HookCooldownReduction(Player player, TutorialTraitDefinition trait)
    {
        PlayerSkillController skills = player.GetComponent<PlayerSkillController>();
        if (skills == null)
        {
            Edit.LogWarning($"{Tag} '{player.name}' 에 PlayerSkillController 가 없다 — '{trait.Id}' 미적용.", player);
            return;
        }

        AttackType trigger = trait.TriggerAttackType;
        PlayerSkillSlot slot = trait.CooldownSlot;
        float seconds = trait.Value;
        Action<PlayerAttackLanded> handler = landed =>
        {
            if (landed.AttackType != trigger || skills == null)
                return;

            PlayerSkillBase skill = skills.GetSkill(slot);
            if (skill != null)
                skills.ReduceCooldownServer(skill, seconds);
        };

        player.ServerAttackLanded += handler;
        cooldownHooks.Add(new CooldownHook { Player = player, Handler = handler });
    }

    // ── 동기화 ─────────────────────────────────────────────────────────────

    /// <summary>모델 상태를 복제 행으로 다시 쓴다. 튜토리얼 규모(≤3인 × 수 장)라 전체 재작성으로 충분하다.</summary>
    private void RebuildEntries()
    {
        if (!IsSpawned || !IsServer || model == null)
            return;

        entries.Clear();
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (model.HasPendingChoice(clientId))
                AddRows(clientId, model.GetOffered(clientId), EntryKind.Offered);
            AddRows(clientId, model.GetAcquired(clientId), EntryKind.Acquired);
        }
    }

    private void AddRows(ulong clientId, IReadOnlyList<int> traitIndices, EntryKind kind)
    {
        for (int i = 0; i < traitIndices.Count; i++)
            entries.Add(new Entry { ClientId = clientId, TraitIndex = traitIndices[i], Kind = kind });
    }

    private void CollectRows(ulong clientId, EntryKind kind, List<int> result)
    {
        result.Clear();
        if (!IsSpawned)
            return;

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].ClientId == clientId && entries[i].Kind == kind)
                result.Add(entries[i].TraitIndex);
        }
    }

    private void HandleEntriesChanged(NetworkListEvent<Entry> _) => ClientStateChanged?.Invoke();

    private void HandleRoundChanged(int previous, int current) => ClientStateChanged?.Invoke();

    // ── 조회 ───────────────────────────────────────────────────────────────

    public static ulong SourceIdOf(int traitIndex) => TraitSourceIdBase + (ulong)Mathf.Max(0, traitIndex);

    private Player FindPlayer(ulong clientId)
    {
        if (NetworkManager == null || !NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
            return null;

        return client.PlayerObject != null ? client.PlayerObject.GetComponent<Player>() : null;
    }

    private List<int> CollectUnusableIndices()
    {
        var unusable = new List<int>();
        if (pool == null)
            return unusable;

        for (int i = 0; i < pool.Count; i++)
        {
            if (pool.Get(i) == null)
                unusable.Add(i);
        }

        return unusable;
    }
}
