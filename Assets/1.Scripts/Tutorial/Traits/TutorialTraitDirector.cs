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
    /// <summary>복도 라운드 번호. 0 = 재선택(개인 라운드, 할 일 30) 획득 — 복도 라운드가 아니다.</summary>
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
/// 선택 창 무적(T2): 제시받고 아직 안 고른 플레이어에게 <see cref="InvulnerabilityCause.TraitSelection"/> 토큰을 건다.
/// 제시 순간 플레이어 오브젝트가 없었으면 그 플레이어가 스폰될 때(<see cref="PlayerLifeCycleController.ServerLifeStateChanged"/>) 아직 미선택이면 다시 건다(T3).
/// 특성 초기화(할 일 30 3단계): <see cref="PartyWipeRespawner.PartyWipeRespawned"/> → 리스폰한 각자에게 팝업 행(<see cref="EntryKind.ResetPrompt"/>) →
/// 응답 RPC 를 서버가 <see cref="TutorialTraitResetModel"/> 로 검증 → [예] = 그 플레이어 효과만 원복 + 혼자만의 라운드 4번(<see cref="TutorialTraitSelectionModel.StartPersonalRound"/>).
/// 팝업·재선택 중인 사람이 있으면 <see cref="IsTraitResetInProgress"/> 가 보스 진입 패드를 잠근다(D3). 원본: Docs/tech/party-lives-respawn.md §4.
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
        /// <summary>특성 초기화 팝업 대기(할 일 30) — <see cref="Entry.TraitIndex"/> 자리에 팝업 세대를 담는다.</summary>
        ResetPrompt = 2,
    }

    /// <summary>복제 행 하나 = (플레이어, 특성 인덱스, 제시/획득, 그 플레이어의 제시 번호).</summary>
    public struct Entry : INetworkSerializable, IEquatable<Entry>
    {
        public ulong ClientId;
        public int TraitIndex;
        public EntryKind Kind;
        /// <summary>제시·획득 행 = 그 플레이어의 <see cref="TutorialTraitSelectionModel.GetOfferSeq"/>(창 판정용). 팝업 행 = 0.</summary>
        public int OfferSeq;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref ClientId);
            s.SerializeValue(ref TraitIndex);
            s.SerializeValue(ref Kind);
            s.SerializeValue(ref OfferSeq);
        }

        public bool Equals(Entry o) => ClientId == o.ClientId && TraitIndex == o.TraitIndex && Kind == o.Kind && OfferSeq == o.OfferSeq;
    }

    // 적용한 효과 하나 — 원복에 필요한 것만. 배율 특성 = 상태이상 타입(출처는 SourceIdOf(index)), 쿨감 특성 = 구독한 핸들러.
    private sealed class AppliedEffect
    {
        public Player Player;
        public StatusEffectType StatType;
        public Action<PlayerAttackLanded> Handler;
    }

    [SerializeField] private TutorialTraitPool pool;

    private readonly NetworkList<Entry> entries = new NetworkList<Entry>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> round = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // TutorialTraitRoundDisplay 플래그(byte) — 현재 라운드의 창 표시 옵션.
    private readonly NetworkVariable<byte> roundDisplay = new NetworkVariable<byte>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 팝업·재선택 중인 사람이 있다(할 일 30 D3) — 보스 진입 패드 잠금·테두리 숨김을 모든 피어가 같은 값으로 읽는다.
    private readonly NetworkVariable<bool> traitResetBusy = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 서버 전용
    private TutorialTraitSelectionModel model;
    private TutorialTraitResetModel resetModel;
    private System.Random random;
    private readonly TutorialTraitEffectLedger<AppliedEffect> appliedEffects = new TutorialTraitEffectLedger<AppliedEffect>();
    private readonly List<TutorialTraitEffectLedger<AppliedEffect>.Record> revertScratch = new List<TutorialTraitEffectLedger<AppliedEffect>.Record>();
    private readonly List<ulong> clientScratch = new List<ulong>();
    private bool roundCompletionRaised = true;

    // 선택 창 무적(InvulnerabilityCause.TraitSelection) 토큰을 건 플레이어 — 모델의 "아직 안 고름"과 맞춘다.
    private readonly HashSet<ulong> shieldedClients = new HashSet<ulong>();
    private readonly List<ulong> shieldScratch = new List<ulong>();
    private readonly List<ulong> shieldAcquireScratch = new List<ulong>();
    private bool shieldRetryRequested;

    public static TutorialTraitDirector Instance { get; private set; }

    public TutorialTraitPool Pool => pool;

    /// <summary>[전 피어] 현재 복도 제시 라운드 번호(0 = 아직 없음). 재선택(개인 라운드)은 올리지 않는다 — 창 판정은 <see cref="GetOfferSeq"/>.</summary>
    public int Round => round.Value;

    /// <summary>[전 피어] 현재 라운드의 창 표시 옵션(상단 설명·아래 한 줄).</summary>
    public TutorialTraitRoundDisplay RoundDisplay => (TutorialTraitRoundDisplay)roundDisplay.Value;

    /// <summary>[전 피어] 누군가 특성 초기화 팝업 대기 또는 재선택 중이다 — 보스 진입 패드가 잠긴다(<see cref="TutorialBossPath.IsBossPadEnabled"/>).</summary>
    public bool IsTraitResetInProgress => IsSpawned && traitResetBusy.Value;

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
        roundDisplay.OnValueChanged += HandleRoundDisplayChanged;

        if (!IsServer)
            return;

        if (pool == null)
            Edit.LogError($"{Tag} 특성 풀이 비었다 — 제시가 동작하지 않는다.", this);

        model = new TutorialTraitSelectionModel(pool != null ? pool.Count : 0, CollectUnusableIndices());
        resetModel = new TutorialTraitResetModel();
        random = new System.Random();
        NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        PlayerLifeCycleController.ServerLifeStateChanged += HandleServerLifeStateChanged;
        PartyWipeRespawner.PartyWipeRespawned += HandlePartyWipeRespawnedServer;
    }

    public override void OnNetworkDespawn()
    {
        entries.OnListChanged -= HandleEntriesChanged;
        round.OnValueChanged -= HandleRoundChanged;
        roundDisplay.OnValueChanged -= HandleRoundDisplayChanged;

        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;

        if (IsServer)
        {
            PlayerLifeCycleController.ServerLifeStateChanged -= HandleServerLifeStateChanged;
            PartyWipeRespawner.PartyWipeRespawned -= HandlePartyWipeRespawnedServer;
            shieldRetryRequested = false;
            ReleaseAllSelectionShields();
        }

        // 쿨감 구독만 푼다 — 배율 상태이상은 디스폰 뒤 플레이어에 남아도 예전처럼 둔다(씬 종료와 함께 사라진다).
        IReadOnlyList<TutorialTraitEffectLedger<AppliedEffect>.Record> applied = appliedEffects.All;
        for (int i = 0; i < applied.Count; i++)
        {
            AppliedEffect effect = applied[i].Handle;
            if (effect.Handler != null && effect.Player != null)
                effect.Player.ServerAttackLanded -= effect.Handler;
        }

        appliedEffects.Clear();
        model = null;
        resetModel = null;
        base.OnNetworkDespawn();
    }

    private bool CanServe => IsSpawned && IsServer && model != null;

    // ── 서버 API ───────────────────────────────────────────────────────────

    /// <summary>
    /// [서버] 접속 중인 전원에게 새 라운드 후보를 제시한다. 이미 얻은 특성은 빠지고, 남은 후보가 적으면 남은 만큼만.
    /// false = 아무에게도 1장도 제시하지 못했다(풀 소진·미스폰) → 호출자는 선택 단계를 건너뛴다.
    /// </summary>
    public bool StartOfferRoundServer() => StartOfferRoundServer(TutorialTraitRoundDisplay.None);

    /// <summary>[서버] 접속 중인 전원에게 제시 — 창 표시 옵션(상단 설명·아래 한 줄)을 같이 정한다. 반환값은 위와 같다.</summary>
    public bool StartOfferRoundServer(TutorialTraitRoundDisplay display)
    {
        if (!CanServe)
            return false;

        clientScratch.Clear();
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            clientScratch.Add(clientId);

        return StartOfferRoundServer(clientScratch, display);
    }

    /// <summary>[서버] 지정한 플레이어들에게 새 라운드 후보를 제시한다. 반환값은 위와 같다.</summary>
    public bool StartOfferRoundServer(IEnumerable<ulong> clientIds) =>
        StartOfferRoundServer(clientIds, TutorialTraitRoundDisplay.None);

    /// <summary>[서버] 지정한 플레이어들에게 제시 + 창 표시 옵션. 반환값은 위와 같다.</summary>
    public bool StartOfferRoundServer(IEnumerable<ulong> clientIds, TutorialTraitRoundDisplay display)
    {
        if (!CanServe)
        {
            Edit.LogWarning($"{Tag} 제시 무시 — 서버에서 스폰된 뒤에만 제시할 수 있다.", this);
            return false;
        }

        int offerCount = pool != null ? pool.OfferCount : 0;
        bool anyOffered = model.StartRound(clientIds, offerCount, random.Next);
        // 옵션을 행보다 먼저 쓴다 — 클라 UI 는 같은 프레임 변경을 모아 한 번에 평가한다.
        roundDisplay.Value = (byte)display;
        round.Value = model.Round;
        roundCompletionRaised = false;
        RebuildEntries();
        SyncSelectionShields();

        Edit.Log($"{Tag} 라운드 {model.Round} 제시 — 1장 이상 받은 플레이어 {(anyOffered ? "있음" : "없음(건너뜀)")}.", this);
        CheckRoundCompleted();
        return anyOffered;
    }

    /// <summary>
    /// [서버] 지금 라운드에 참가자 1명을 더한다 — 복도 중간 지점에 늦게 도착한 사람에게만 제시한다(할 일 15 S12).
    /// 이미 참가한 사람의 제시·선택은 그대로 둔다. 이미 참가자면 <see cref="TutorialTraitJoinResult.AlreadyInRound"/>(변경 없음).
    /// <see cref="TutorialTraitJoinResult.Empty"/> = 0장(고를 것 없음 — 호출자가 바로 다음 단계로).
    /// 라운드가 이미 완료로 알려졌어도 새 미선택자가 생기면 그 사람이 고를 때 <see cref="ServerRoundCompleted"/> 가 다시 난다.
    /// </summary>
    public TutorialTraitJoinResult AddParticipantServer(ulong clientId)
    {
        if (!CanServe)
            return TutorialTraitJoinResult.NoRound;

        TutorialTraitJoinResult result = model.AddParticipant(clientId, random.Next);
        if (result != TutorialTraitJoinResult.Offered && result != TutorialTraitJoinResult.Empty)
            return result;

        if (result == TutorialTraitJoinResult.Offered)
            roundCompletionRaised = false;
        RebuildEntries();
        SyncSelectionShields();

        Edit.Log($"{Tag} 라운드 {model.Round} 참가자 추가 client {clientId} — {result}.", this);
        CheckRoundCompleted();
        return result;
    }

    /// <summary>
    /// [서버, 디버그 구간 스킵 — 할 일 34] 아무에게도 제시하지 않고 새 라운드만 연다(라운드 번호 + 창 표시 옵션).
    /// 참가자는 <see cref="GrantParticipantServer"/> 로 넣는다 — 호출자가 그 사이에 이 라운드 번호를 복도에 묶어 둘 수 있게 둘로 나눴다.
    /// </summary>
    public void StartRoundWithoutOfferServer(TutorialTraitRoundDisplay display)
    {
        if (!CanServe)
            return;

        int offerCount = pool != null ? pool.OfferCount : 0;
        model.StartRound(Array.Empty<ulong>(), offerCount, random.Next);
        roundDisplay.Value = (byte)display;
        round.Value = model.Round;
        roundCompletionRaised = false;
        RebuildEntries();
        SyncSelectionShields();
        Edit.Log($"{Tag} 라운드 {model.Round} 시작 — 제시 없음(구간 스킵).", this);
    }

    /// <summary>
    /// [서버, 디버그 구간 스킵 — 할 일 34] 지금 라운드에 참가자 1명을 <b>제시 없이</b> 넣고 바로 무작위 1장을 획득시킨다
    /// (<see cref="TutorialTraitSelectionModel.AddParticipantWithoutOffer"/>). 제시 행·제시 번호가 생기지 않아 클라 창이 열리지 않는다.
    /// 효과 적용·<see cref="ServerTraitChosen"/>(Auto = true)·라운드 완료 판정은 <see cref="AutoChooseServer"/> 와 같다.
    /// </summary>
    public TutorialTraitJoinResult GrantParticipantServer(ulong clientId)
    {
        if (!CanServe)
            return TutorialTraitJoinResult.NoRound;

        TutorialTraitJoinResult result = model.AddParticipantWithoutOffer(clientId, random.Next, out int traitIndex);
        if (result == TutorialTraitJoinResult.Granted)
        {
            FinishAcquire(clientId, traitIndex, model.Round, auto: true, "자동·제시 없음");
            return result;
        }

        if (result == TutorialTraitJoinResult.Empty)
        {
            RebuildEntries();
            SyncSelectionShields();
            Edit.Log($"{Tag} 라운드 {model.Round} 참가자 추가 client {clientId} — 제시 없이 획득할 후보 없음.", this);
            CheckRoundCompleted();
        }

        return result;
    }

    /// <summary>[서버] 그 플레이어가 지금 라운드 참가자인가(제시 중·고름·0장 모두).</summary>
    public bool IsInRoundServer(ulong clientId) => model != null && model.IsInRound(clientId);

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

    /// <summary>
    /// [전 피어] 그 플레이어의 제시 번호(0 = 제시·획득 행 없음). 그 플레이어에게 새 제시가 들어갈 때만 바뀐다 —
    /// 다른 사람의 재선택·참가 추가로는 변하지 않으므로 선택 창은 이 값으로 열고 닫는다.
    /// </summary>
    public int GetOfferSeq(ulong clientId)
    {
        int seq = 0;
        if (!IsSpawned)
            return seq;

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].ClientId == clientId && entries[i].OfferSeq > seq)
                seq = entries[i].OfferSeq;
        }

        return seq;
    }

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

    /// <summary>[전 피어] 그 플레이어가 특성 초기화 팝업에 답해야 하면 true + 팝업 세대.</summary>
    public bool TryGetResetPrompt(ulong clientId, out int generation)
    {
        generation = 0;
        if (!IsSpawned)
            return false;

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].ClientId == clientId && entries[i].Kind == EntryKind.ResetPrompt)
            {
                generation = entries[i].TraitIndex;
                return true;
            }
        }

        return false;
    }

    /// <summary>[로컬 플레이어] 특성 초기화 팝업 응답(예 = 초기화 후 재선택). 판정은 서버가 한다.</summary>
    public void RequestResetAnswer(int generation, bool reset)
    {
        if (!IsSpawned)
        {
            Edit.LogWarning($"{Tag} 초기화 응답 무시 — 아직 네트워크 스폰 전이다.", this);
            return;
        }

        AnswerResetPromptServerRpc(generation, reset);
    }

    // ── 특성 초기화(할 일 30 3단계) ─────────────────────────────────────────

    // D4: 튜토리얼일 때만 — 절차 맵에는 특성이 없다(이 컴포넌트도 보통 없다).
    private void HandlePartyWipeRespawnedServer(IReadOnlyList<ulong> clientIds)
    {
        if (!CanServe || resetModel == null)
            return;

        TutorialDirector tutorial = TutorialDirector.Instance;
        if (tutorial == null || !tutorial.IsTutorialActive)
        {
            Edit.Log($"{Tag} 전멸 리스폰 — 튜토리얼이 아니라 특성 초기화 팝업 없음.", this);
            return;
        }

        // 다시 전멸 — 지난 팝업은 새 세대로 덮고, 재선택 도중이던 사람의 열린 제시는 거둔다(고른 것은 유지).
        int generation = resetModel.BeginPrompts(clientIds, clientScratch);
        for (int i = 0; i < clientScratch.Count; i++)
            model.CancelOffer(clientScratch[i]);

        RebuildEntries();
        SyncSelectionShields();
        SyncResetBusy();
        Edit.Log($"{Tag} 전멸 리스폰 — 특성 초기화 팝업 세대 {generation}, 대상 {clientIds?.Count ?? 0}명" +
                 $"{(clientScratch.Count > 0 ? $"(재선택 중단 {clientScratch.Count}명)" : "")}.", this);
    }

    // 씬 상주 오브젝트라 오너가 없다 — 누구나 호출하고 보낸 사람은 SenderClientId 로 가린다(자기 팝업에만 답할 수 있다).
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void AnswerResetPromptServerRpc(int generation, bool reset, RpcParams p = default)
    {
        if (resetModel != null)
            CommitResetAnswer(p.Receive.SenderClientId, generation, reset);
    }

    private void CommitResetAnswer(ulong clientId, int generation, bool reset)
    {
        TutorialTraitResetAnswer result = resetModel.Answer(clientId, generation, reset);
        if (result == TutorialTraitResetAnswer.NotPrompted || result == TutorialTraitResetAnswer.StaleGeneration)
        {
            Edit.LogWarning($"{Tag} 초기화 응답 거부: client {clientId} 세대 {generation} — {result}.", this);
            return;
        }

        if (result == TutorialTraitResetAnswer.Reset)
        {
            int reverted = RevertEffects(clientId);
            model.ResetPlayer(clientId);
            bool opened = ContinueReselect(clientId);
            Edit.Log($"{Tag} client {clientId} 특성 초기화 — 효과 {reverted}개 원복, 재선택 {(opened ? $"제시 {model.GetOfferSeq(clientId)}" : "후보 없음(종료)")}.", this);
        }
        else
        {
            Edit.Log($"{Tag} client {clientId} 특성 유지(아니오).", this);
        }

        RebuildEntries();
        SyncSelectionShields();
        SyncResetBusy();
    }

    // 재선택 다음 라운드 — 그 플레이어 혼자 받는다(다른 사람의 제시·선택·라운드 완료와 무관). 0장 라운드는 건너뛴다.
    // 라운드를 열었으면 true, 남은 라운드가 없어 재선택이 끝났으면 false.
    private bool ContinueReselect(ulong clientId)
    {
        int offerCount = pool != null ? pool.OfferCount : 0;
        bool opened = false;
        while (resetModel.TryTakeReselectRound(clientId))
        {
            if (model.StartPersonalRound(clientId, offerCount, random.Next))
            {
                opened = true;
                break;
            }
        }

        // 재선택 창엔 복도 안내 문구(소개·마지막)를 붙이지 않는다. 옵션을 행보다 먼저 쓴다(StartOfferRoundServer 와 같은 순서).
        // 전역 라운드 번호는 그대로 — 창은 그 플레이어의 제시 번호로 열리므로 다른 사람 창이 닫히지 않는다.
        roundDisplay.Value = (byte)TutorialTraitRoundDisplay.None;
        return opened;
    }

    // 그 플레이어에게 이 디렉터가 건 효과만 되돌린다 — 배율 상태이상(출처 = 특성별 id) 제거·쿨감 구독 해제. 되돌린 수.
    private int RevertEffects(ulong clientId)
    {
        int count = appliedEffects.TakeAll(clientId, revertScratch);
        for (int i = 0; i < revertScratch.Count; i++)
        {
            AppliedEffect effect = revertScratch[i].Handle;
            if (effect.Player == null)
                continue;

            if (effect.Handler != null)
                effect.Player.ServerAttackLanded -= effect.Handler;
            else if (effect.Player.StatusEffects != null)
                effect.Player.StatusEffects.Remove(effect.StatType, SourceIdOf(revertScratch[i].TraitIndex));
        }

        revertScratch.Clear();
        return count;
    }

    private void SyncResetBusy()
    {
        bool busy = resetModel != null && resetModel.IsBusy;
        if (IsSpawned && IsServer && traitResetBusy.Value != busy)
            traitResetBusy.Value = busy;
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
        // 재선택(개인 라운드) 획득은 복도 라운드가 아니다 — 0 으로 알려 복도 체크(TutorialCorridorFlow)가 반응하지 않게 한다.
        int chosenRound = model.IsPersonalRound(clientId) ? 0 : model.Round;
        TutorialTraitChoiceResult result = model.TryChoose(clientId, traitIndex);
        if (result != TutorialTraitChoiceResult.Accepted)
        {
            Edit.LogWarning($"{Tag} {(auto ? "자동 " : "")}선택 거부: client {clientId} 특성 {traitIndex} — {result}.", this);
            return false;
        }

        FinishAcquire(clientId, traitIndex, chosenRound, auto, auto ? "자동" : "수동");
        return true;
    }

    // 모델이 획득을 확정한 뒤의 공통 처리 — 효과 적용·재선택 이어가기·행/무적 동기화·획득 통지·라운드 완료 판정.
    // 선택 창 경로(CommitChoice)와 제시 없는 획득(GrantParticipantServer)이 같이 쓴다. how = 로그용.
    private void FinishAcquire(ulong clientId, int traitIndex, int chosenRound, bool auto, string how)
    {
        TutorialTraitDefinition trait = pool != null ? pool.Get(traitIndex) : null;
        ApplyEffect(clientId, traitIndex, trait);

        // 재선택 중이면 다음 라운드를 바로 연다 — 무적 토큰을 풀었다 다시 걸지 않게 행·무적 동기화보다 먼저.
        bool reselecting = resetModel != null && resetModel.IsReselecting(clientId);
        if (reselecting)
            ContinueReselect(clientId);

        RebuildEntries();
        SyncSelectionShields();
        if (reselecting)
            SyncResetBusy();

        Edit.Log($"{Tag} client {clientId} 특성 획득 '{trait?.Id}'({traitIndex}) {how} — 라운드 {chosenRound}" +
                 $"{(reselecting ? $" (재선택, 남은 {resetModel.RoundsLeft(clientId)})" : "")}.", this);
        ServerTraitChosen?.Invoke(new TutorialTraitChosen(clientId, traitIndex, trait, auto, chosenRound));
        CheckRoundCompleted();
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
        resetModel?.Remove(clientId); // 팝업·재선택 대기가 패드 잠금으로 남지 않게
        // 떠난 플레이어 오브젝트는 곧 사라진다 — 원복 없이 장부에서만 뺀다.
        appliedEffects.TakeAll(clientId, revertScratch);
        revertScratch.Clear();

        RebuildEntries();
        SyncSelectionShields();
        SyncResetBusy();
        CheckRoundCompleted();
    }

    // ── 선택 창 무적 ───────────────────────────────────────────────────────

    // 기존 원인별 토큰 경로(PlayerInvulnerability)를 쓴다 — 원인이 다르면 서로의 토큰을 지우지 않으므로
    // 대시·연출·착지 보호 무적과 겹쳐도 어느 한쪽 해제가 다른 쪽을 풀지 않는다.
    private void SyncSelectionShields()
    {
        if (model == null)
            return;

        model.PlanSelectionShields(shieldedClients, shieldScratch, shieldAcquireScratch);
        for (int i = 0; i < shieldScratch.Count; i++)
        {
            SetSelectionShield(shieldScratch[i], false);
            shieldedClients.Remove(shieldScratch[i]);
        }

        for (int i = 0; i < shieldAcquireScratch.Count; i++)
        {
            ulong clientId = shieldAcquireScratch[i];
            if (SetSelectionShield(clientId, true))
                shieldedClients.Add(clientId);
        }
    }

    // 제시 순간 플레이어 오브젝트가 없던(스폰 전·준비 전) 미선택자 재시도. 스폰 초기 통지(s,s)는 그 플레이어의
    // OnNetworkSpawn 안에서 오는데, NGO 는 그 뒤에야 NetworkClient.PlayerObject 를 채운다 — 그래서 바로 걸지 않고 다음 Update 로 미룬다.
    private void HandleServerLifeStateChanged(ulong clientId, PlayerLifeState previous, PlayerLifeState current)
    {
        if (model != null && model.HasPendingChoice(clientId) && !shieldedClients.Contains(clientId))
            shieldRetryRequested = true;
    }

    private void Update()
    {
        if (!shieldRetryRequested)
            return;

        shieldRetryRequested = false;
        SyncSelectionShields();
    }

    private void ReleaseAllSelectionShields()
    {
        foreach (ulong clientId in shieldedClients)
            SetSelectionShield(clientId, false);

        shieldedClients.Clear();
    }

    private bool SetSelectionShield(ulong clientId, bool shielded)
    {
        Player player = FindPlayer(clientId);
        PlayerInvulnerability invulnerability = player != null ? player.GetComponent<PlayerInvulnerability>() : null;
        if (invulnerability == null)
        {
            if (shielded)
                Edit.LogWarning($"{Tag} client {clientId} 의 PlayerInvulnerability 를 못 찾았다 — 선택 중 무적 없음.", this);
            return false;
        }

        // 무기한 토큰 — 선택·자동 획득·이탈·디스폰이 SyncSelectionShields/ReleaseAllSelectionShields 로 해제한다.
        if (shielded)
            invulnerability.AddServerToken(InvulnerabilityCause.TraitSelection, 0.0);
        else
            invulnerability.RemoveServerToken(InvulnerabilityCause.TraitSelection);
        return true;
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
                ApplyStatModifier(clientId, player, StatusEffectType.AttackDamageModifier, trait, traitIndex);
                break;
            case TutorialTraitEffectKind.MoveSpeedMultiplier:
                ApplyStatModifier(clientId, player, StatusEffectType.MoveSpeedModifier, trait, traitIndex);
                break;
            case TutorialTraitEffectKind.CooldownReductionOnHit:
                HookCooldownReduction(clientId, player, trait, traitIndex);
                break;
        }
    }

    private void ApplyStatModifier(ulong clientId, Player player, StatusEffectType type, TutorialTraitDefinition trait, int traitIndex)
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
        else
            appliedEffects.Add(clientId, traitIndex, new AppliedEffect { Player = player, StatType = type });
    }

    private void HookCooldownReduction(ulong clientId, Player player, TutorialTraitDefinition trait, int traitIndex)
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
        appliedEffects.Add(clientId, traitIndex, new AppliedEffect { Player = player, Handler = handler });
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
            // 제시 번호는 획득 행에도 싣는다 — 고른 뒤(제시 행 없음)에도 번호가 남아야 같은 틱 자동 획득 토스트가 뜬다.
            int offerSeq = model.GetOfferSeq(clientId);
            if (model.HasPendingChoice(clientId))
                AddRows(clientId, model.GetOffered(clientId), EntryKind.Offered, offerSeq);
            AddRows(clientId, model.GetAcquired(clientId), EntryKind.Acquired, offerSeq);
            if (resetModel != null && resetModel.TryGetPrompt(clientId, out int generation))
                entries.Add(new Entry { ClientId = clientId, TraitIndex = generation, Kind = EntryKind.ResetPrompt });
        }
    }

    private void AddRows(ulong clientId, IReadOnlyList<int> traitIndices, EntryKind kind, int offerSeq)
    {
        for (int i = 0; i < traitIndices.Count; i++)
            entries.Add(new Entry { ClientId = clientId, TraitIndex = traitIndices[i], Kind = kind, OfferSeq = offerSeq });
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

    private void HandleRoundDisplayChanged(byte previous, byte current) => ClientStateChanged?.Invoke();

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
