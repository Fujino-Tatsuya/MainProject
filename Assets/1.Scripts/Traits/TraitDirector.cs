using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>[서버] 특성 1장 획득 통지.</summary>
public readonly struct TraitChosen
{
    public readonly ulong ClientId;
    /// <summary>특성 ID 의 고정 해시(<see cref="TraitIdHash"/>).</summary>
    public readonly int TraitHash;
    public readonly TraitDefinition Trait;
    /// <summary>true = 교착 방지·타이머 만료로 서버가 대신 골랐다. false = 플레이어가 직접 골랐다.</summary>
    public readonly bool Auto;
    /// <summary>복도 라운드 번호. 0 = 재선택(개인 라운드, 할 일 30) 획득 — 복도 라운드가 아니다.</summary>
    public readonly int Round;

    public TraitChosen(ulong clientId, int traitHash, TraitDefinition trait, bool auto, int round)
    {
        ClientId = clientId;
        TraitHash = traitHash;
        Trait = trait;
        Auto = auto;
        Round = round;
    }
}

/// <summary>
/// 튜토리얼 복도 특성 선택의 <b>상태 소유자</b>(씬 상주 NetworkObject, 서버 권한 — 할 일 15 T1).
/// 규칙은 <see cref="TraitSelectionModel"/> 이 갖고, 여기는 NGO 동기화·RPC 검증·효과 적용만 한다.
///
/// 동기화: 플레이어별 "제시 후보"와 "획득 특성"을 행 단위로 <see cref="NetworkList{T}"/> 에 복제한다
/// (<see cref="ZoneBridgeGateManager"/> 와 같은 씬 매니저 + NetworkList 패턴). 클라는 자기 clientId 행만 읽으면 된다.
/// 제시 행은 아직 안 고른 동안만 존재한다 — 고르면 사라지고 획득 행이 생긴다.
/// 특성 키(할 일 125) = 정의 ID 의 고정 해시(<see cref="TraitIdHash"/>) — 풀 배열 순서와 무관하다.
/// 후보 = 캐릭터·차수별 고정 3개(<see cref="TraitCatalog"/>, 차수 = 획득 수 + 1), 자동 선택 = 첫 번째. 캐릭터를 모르거나 정의가 없으면 0장(Empty).
/// 접속 때 서버 풀의 목록 버전(<see cref="TraitCatalogIndex.Version"/>)을 복제해 클라가 자기 값과 비교한다 — 다르면 오류 로그.
///
/// 효과: 배율 특성 = 여기서 걸지 않는다 — 각 피어의 <see cref="PlayerTraitModifiers"/> 가 이 디렉터의 획득 행을 읽어 같은 순수 함수로 계산한다
/// (할 일 125 2/5, 상태이상 아님 → 보스방 디버프 정리·연출 잠금과 무관, 1 미만 페널티 유지). 획득 행이 바뀌면 원복·재적용도 자동이다.
/// 이벤트형 효과(할 일 125 3/5 — 쿨 차감·보호막·밀치기·파생 공격·다음 공격 강화) = 그 플레이어 허브(<see cref="PlayerTraitEffects"/>)에
/// <see cref="TraitEffectBinder"/> 로 걸고, 되돌리기 장부(<see cref="TraitEffectLedger{THandle}"/>)에 특성 해시 단위로 기록한다 —
/// 재선택은 전부 되돌린 뒤 다시 고른 것만 걸고, 재합류는 획득 목록과 맞추며, '획득 1회' 동작은 두 경우 모두 다시 하지 않는다.
/// 선택 창 무적(T2): 제시받고 아직 안 고른 플레이어에게 <see cref="InvulnerabilityCause.TraitSelection"/> 토큰을 건다.
/// 제시 순간 플레이어 오브젝트가 없었으면 그 플레이어가 스폰될 때(<see cref="PlayerLifeCycleController.ServerLifeStateChanged"/>) 아직 미선택이면 다시 건다(T3).
/// 특성 초기화(할 일 30 3단계): <see cref="PartyWipeRespawner.PartyWipeRespawned"/> → 리스폰한 각자에게 팝업 행(<see cref="EntryKind.ResetPrompt"/>) →
/// 응답 RPC 를 서버가 <see cref="TraitResetModel"/> 로 검증 → [예] = 그 플레이어 효과만 원복 + 혼자만의 라운드 4번(<see cref="TraitSelectionModel.StartPersonalRound"/>).
/// 팝업·재선택 중인 사람이 있으면 <see cref="IsTraitResetInProgress"/> 가 보스 진입 패드를 잠근다(D3). 원본: Docs/tech/party-lives-respawn.md §4.
/// 판 도중 재합류(할 일 48 C14 R4c): 퇴장 때 상태를 playerId 키로 <see cref="RejoinMemory"/> 에 보관 → <see cref="MatchRoster.PlayerRejoined"/> 때
/// 새 clientId 로 획득 목록·라운드 참가·팝업/재선택을 되돌리고 효과를 다시 건다(<see cref="TraitRejoin"/>).
/// 이 컴포넌트를 씬에 두지 않으면 아무 일도 하지 않는다(튜토리얼 외 영향 없음).
/// </summary>
[DisallowMultipleComponent]
public sealed class TraitDirector : NetworkBehaviour
{
    private const string Tag = "[Trait]";

    public enum EntryKind : byte
    {
        Offered = 0,
        Acquired = 1,
        /// <summary>특성 초기화 팝업 대기(할 일 30) — <see cref="Entry.TraitHash"/> 자리에 팝업 세대를 담는다.</summary>
        ResetPrompt = 2,
    }

    /// <summary>복제 행 하나 = (플레이어, 특성 ID 해시, 제시/획득, 그 플레이어의 제시 번호).</summary>
    public struct Entry : INetworkSerializable, IEquatable<Entry>
    {
        public ulong ClientId;
        /// <summary>제시·획득 행 = 특성 ID 해시(<see cref="TraitIdHash"/>). 팝업 행 = 팝업 세대.</summary>
        public int TraitHash;
        public EntryKind Kind;
        /// <summary>제시·획득 행 = 그 플레이어의 <see cref="TraitSelectionModel.GetOfferSeq"/>(창 판정용). 팝업 행 = 0.</summary>
        public int OfferSeq;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref ClientId);
            s.SerializeValue(ref TraitHash);
            s.SerializeValue(ref Kind);
            s.SerializeValue(ref OfferSeq);
        }

        public bool Equals(Entry o) => ClientId == o.ClientId && TraitHash == o.TraitHash && Kind == o.Kind && OfferSeq == o.OfferSeq;
    }

    // 적용한 특성 하나 — 원복에 필요한 것만: 이벤트형 효과의 되돌리기 핸들(구독·등록·상태, 할 일 125 3/5).
    // 수치형 기여는 PlayerTraitModifiers 가 획득 행으로 계산하므로 핸들이 없다(그래도 '걸었음' 기록은 남겨 재적용을 막는다).
    private sealed class AppliedEffect
    {
        public readonly List<ITraitEffectBinding> Bindings = new List<ITraitEffectBinding>();

        public void Unbind()
        {
            for (int i = 0; i < Bindings.Count; i++)
                Bindings[i].Unbind();
            Bindings.Clear();
        }
    }

    [SerializeField] private TraitPool pool;

    private readonly NetworkList<Entry> entries = new NetworkList<Entry>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> round = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // TraitRoundDisplay 플래그(byte) — 현재 라운드의 창 표시 옵션.
    private readonly NetworkVariable<byte> roundDisplay = new NetworkVariable<byte>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 팝업·재선택 중인 사람이 있다(할 일 30 D3) — 보스 진입 패드 잠금·테두리 숨김을 모든 피어가 같은 값으로 읽는다.
    private readonly NetworkVariable<bool> traitResetBusy = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 서버 풀의 특성 목록 버전(할 일 125) — 클라가 자기 풀 버전과 비교한다. 0 = 아직 안 씀.
    private readonly NetworkVariable<int> catalogVersion = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private bool versionMismatchLogged;

    // 서버 전용
    private TraitSelectionModel model;
    private TraitResetModel resetModel;
    private System.Random random;
    private readonly TraitEffectLedger<AppliedEffect> appliedEffects = new TraitEffectLedger<AppliedEffect>();
    private readonly List<TraitEffectLedger<AppliedEffect>.Record> revertScratch = new List<TraitEffectLedger<AppliedEffect>.Record>();
    private readonly List<ulong> clientScratch = new List<ulong>();
    private readonly List<TraitOnceKey> onceScratch = new List<TraitOnceKey>();
    private readonly List<int> applyScratch = new List<int>();
    private bool roundCompletionRaised = true;

    // 선택 창 무적(InvulnerabilityCause.TraitSelection) 토큰을 건 플레이어 — 모델의 "아직 안 고름"과 맞춘다.
    private readonly HashSet<ulong> shieldedClients = new HashSet<ulong>();
    private readonly List<ulong> shieldScratch = new List<ulong>();
    private readonly List<ulong> shieldAcquireScratch = new List<ulong>();
    private bool shieldRetryRequested;

    public static TraitDirector Instance { get; private set; }

    private static readonly TraitCatalogIndex EmptyCatalog = new TraitCatalogIndex(Array.Empty<string>());

    public TraitPool Pool => pool;

    /// <summary>[전 피어] 풀 조회표(고정 후보·목록 버전·검증). 풀이 없으면 빈 표.</summary>
    public TraitCatalogIndex Catalog => pool != null ? pool.Catalog : EmptyCatalog;

    /// <summary>[전 피어] 특성 ID 해시의 정의(복제 행의 <see cref="Entry.TraitHash"/>). 없으면 null.</summary>
    public TraitDefinition GetTrait(int traitHash) => pool != null ? pool.GetByHash(traitHash) : null;

    /// <summary>[전 피어] 현재 복도 제시 라운드 번호(0 = 아직 없음). 재선택(개인 라운드)은 올리지 않는다 — 창 판정은 <see cref="GetOfferSeq"/>.</summary>
    public int Round => round.Value;

    /// <summary>[전 피어] 현재 라운드의 창 표시 옵션(상단 설명·아래 한 줄).</summary>
    public TraitRoundDisplay RoundDisplay => (TraitRoundDisplay)roundDisplay.Value;

    /// <summary>[전 피어] 누군가 특성 초기화 팝업 대기 또는 재선택 중이다 — 보스 진입 패드가 잠긴다(<see cref="MapSceneBossPath.IsBossPadEnabled"/>).</summary>
    public bool IsTraitResetInProgress => IsSpawned && traitResetBusy.Value;

    /// <summary>[서버] 특성 1장 획득(수동·자동).</summary>
    public event Action<TraitChosen> ServerTraitChosen;

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
        {
            catalogVersion.OnValueChanged += HandleCatalogVersionChanged;
            CheckCatalogVersion(catalogVersion.Value);
            return;
        }

        if (pool == null)
            Edit.LogError($"{Tag} 특성 풀이 비었다 — 제시가 동작하지 않는다.", this);

        TraitCatalogIndex catalog = Catalog;
        LogCatalogErrors(catalog);
        catalogVersion.Value = catalog.Version;
        Edit.Log($"{Tag} 특성 목록 버전 {catalog.Version} — 제시 가능 정의 {catalog.Count}개(풀 {pool?.Count ?? 0}칸).", this);

        model = new TraitSelectionModel(FillFixedCandidates, catalog.Contains);
        resetModel = new TraitResetModel();
        random = new System.Random();
        NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        PlayerLifeCycleController.ServerLifeStateChanged += HandleServerLifeStateChanged;
        PartyWipeRespawner.PartyWipeRespawned += HandlePartyWipeRespawnedServer;
        MatchRoster.PlayerRejoined += HandlePlayerRejoinedServer;

        // 새 디렉터 = 새 특성 상태(맵 다시 로드) — 이전 디렉터 때 보관한 특성 기록은 되돌릴 대상이 아니다.
        int stale = RejoinMemory.Server.ClearTraits();
        if (stale > 0)
            Edit.Log($"{Tag} 디렉터 스폰 — 이전 특성 재합류 기록 {stale}건을 버린다.", this);
    }

    public override void OnNetworkDespawn()
    {
        entries.OnListChanged -= HandleEntriesChanged;
        round.OnValueChanged -= HandleRoundChanged;
        roundDisplay.OnValueChanged -= HandleRoundDisplayChanged;
        catalogVersion.OnValueChanged -= HandleCatalogVersionChanged;

        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;

        if (IsServer)
        {
            PlayerLifeCycleController.ServerLifeStateChanged -= HandleServerLifeStateChanged;
            PartyWipeRespawner.PartyWipeRespawned -= HandlePartyWipeRespawnedServer;
            MatchRoster.PlayerRejoined -= HandlePlayerRejoinedServer;
            shieldRetryRequested = false;
            ReleaseAllSelectionShields();
        }

        // 이벤트형 효과를 되돌린다. 배율 특성은 PlayerTraitModifiers 가 디렉터를 잃으면 빈 목록(전부 1)으로 돌아간다.
        IReadOnlyList<TraitEffectLedger<AppliedEffect>.Record> applied = appliedEffects.All;
        for (int i = 0; i < applied.Count; i++)
            applied[i].Handle?.Unbind();

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
    public bool StartOfferRoundServer() => StartOfferRoundServer(TraitRoundDisplay.None);

    /// <summary>[서버] 접속 중인 전원에게 제시 — 창 표시 옵션(상단 설명·아래 한 줄)을 같이 정한다. 반환값은 위와 같다.</summary>
    public bool StartOfferRoundServer(TraitRoundDisplay display)
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
        StartOfferRoundServer(clientIds, TraitRoundDisplay.None);

    /// <summary>[서버] 지정한 플레이어들에게 제시 + 창 표시 옵션. 반환값은 위와 같다.</summary>
    public bool StartOfferRoundServer(IEnumerable<ulong> clientIds, TraitRoundDisplay display)
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
    /// 이미 참가한 사람의 제시·선택은 그대로 둔다. 이미 참가자면 <see cref="TraitJoinResult.AlreadyInRound"/>(변경 없음).
    /// <see cref="TraitJoinResult.Empty"/> = 0장(고를 것 없음 — 호출자가 바로 다음 단계로).
    /// 라운드가 이미 완료로 알려졌어도 새 미선택자가 생기면 그 사람이 고를 때 <see cref="ServerRoundCompleted"/> 가 다시 난다.
    /// </summary>
    public TraitJoinResult AddParticipantServer(ulong clientId)
    {
        if (!CanServe)
            return TraitJoinResult.NoRound;

        TraitJoinResult result = model.AddParticipant(clientId, random.Next);
        if (result != TraitJoinResult.Offered && result != TraitJoinResult.Empty)
            return result;

        if (result == TraitJoinResult.Offered)
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
    public void StartRoundWithoutOfferServer(TraitRoundDisplay display)
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
    /// (<see cref="TraitSelectionModel.AddParticipantWithoutOffer"/>). 제시 행·제시 번호가 생기지 않아 클라 창이 열리지 않는다.
    /// 효과 적용·<see cref="ServerTraitChosen"/>(Auto = true)·라운드 완료 판정은 <see cref="AutoChooseServer"/> 와 같다.
    /// </summary>
    public TraitJoinResult GrantParticipantServer(ulong clientId)
    {
        if (!CanServe)
            return TraitJoinResult.NoRound;

        TraitJoinResult result = model.AddParticipantWithoutOffer(clientId, random.Next, out int traitHash);
        if (result == TraitJoinResult.Granted)
        {
            FinishAcquire(clientId, traitHash, model.Round, auto: true, "자동·제시 없음");
            return result;
        }

        if (result == TraitJoinResult.Empty)
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

        int traitHash = model.PickAutoChoice(clientId, random.Next);
        return traitHash >= 0 && CommitChoice(clientId, traitHash, auto: true);
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
    public void RequestChoose(int traitHash)
    {
        if (!IsSpawned)
        {
            Edit.LogWarning($"{Tag} 선택 무시 — 아직 네트워크 스폰 전이다.", this);
            return;
        }

        ChooseTraitServerRpc(traitHash);
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
                generation = entries[i].TraitHash;
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

        MapSceneDirector tutorial = MapSceneDirector.Instance;
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
        TraitResetAnswer result = resetModel.Answer(clientId, generation, reset);
        if (result == TraitResetAnswer.NotPrompted || result == TraitResetAnswer.StaleGeneration)
        {
            Edit.LogWarning($"{Tag} 초기화 응답 거부: client {clientId} 세대 {generation} — {result}.", this);
            return;
        }

        if (result == TraitResetAnswer.Reset)
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
        roundDisplay.Value = (byte)TraitRoundDisplay.None;
        return opened;
    }

    // 그 플레이어에게 이 디렉터가 건 이벤트형 효과만 되돌린다(구독 해제·등록 해제·강화 상태 정리). 배율 특성은 획득 행이 비면
    // PlayerTraitModifiers 가 스스로 1 로 돌아간다. '획득 1회' 표시는 남긴다 — 재선택에서 같은 특성을 다시 골라도 1회 동작을 다시 하지 않는다. 되돌린 수.
    private int RevertEffects(ulong clientId)
    {
        int count = appliedEffects.TakeAll(clientId, revertScratch);
        for (int i = 0; i < revertScratch.Count; i++)
            revertScratch[i].Handle?.Unbind();

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
    private void ChooseTraitServerRpc(int traitHash, RpcParams p = default)
    {
        if (model != null)
            CommitChoice(p.Receive.SenderClientId, traitHash, auto: false);
    }

    // 선택 검증(제시된 후보인가·이미 골랐나)은 모델이 한다. 위반은 무시하고 로그만 남긴다.
    private bool CommitChoice(ulong clientId, int traitHash, bool auto)
    {
        // 재선택(개인 라운드) 획득은 복도 라운드가 아니다 — 0 으로 알려 복도 체크(MapSceneTraitCorridorFlow)가 반응하지 않게 한다.
        int chosenRound = model.IsPersonalRound(clientId) ? 0 : model.Round;
        TraitChoiceResult result = model.TryChoose(clientId, traitHash);
        if (result != TraitChoiceResult.Accepted)
        {
            Edit.LogWarning($"{Tag} {(auto ? "자동 " : "")}선택 거부: client {clientId} 특성 {traitHash} — {result}.", this);
            return false;
        }

        FinishAcquire(clientId, traitHash, chosenRound, auto, auto ? "자동" : "수동");
        return true;
    }

    // 모델이 획득을 확정한 뒤의 공통 처리 — 효과 적용·재선택 이어가기·행/무적 동기화·획득 통지·라운드 완료 판정.
    // 선택 창 경로(CommitChoice)와 제시 없는 획득(GrantParticipantServer)이 같이 쓴다. how = 로그용.
    private void FinishAcquire(ulong clientId, int traitHash, int chosenRound, bool auto, string how)
    {
        TraitDefinition trait = GetTrait(traitHash);
        ApplyEffect(clientId, traitHash, trait);

        // 재선택 중이면 다음 라운드를 바로 연다 — 무적 토큰을 풀었다 다시 걸지 않게 행·무적 동기화보다 먼저.
        bool reselecting = resetModel != null && resetModel.IsReselecting(clientId);
        if (reselecting)
            ContinueReselect(clientId);

        RebuildEntries();
        SyncSelectionShields();
        if (reselecting)
            SyncResetBusy();

        Edit.Log($"{Tag} client {clientId} 특성 획득 '{trait?.Id}'({traitHash}) {how} — 라운드 {chosenRound}" +
                 $"{(reselecting ? $" (재선택, 남은 {resetModel.RoundsLeft(clientId)})" : "")}.", this);
        ServerTraitChosen?.Invoke(new TraitChosen(clientId, traitHash, trait, auto, chosenRound));
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

        SaveForRejoin(clientId);
        model.RemovePlayer(clientId);
        resetModel?.Remove(clientId); // 팝업·재선택 대기가 패드 잠금으로 남지 않게
        // 떠난 플레이어 오브젝트는 곧 사라진다 — 장부에서 빼고 핸들만 푼다(허브 쪽 참조 정리). '획득 1회' 표시는 SaveForRejoin 이 보관했다.
        appliedEffects.TakeAll(clientId, revertScratch);
        for (int i = 0; i < revertScratch.Count; i++)
            revertScratch[i].Handle?.Unbind();
        revertScratch.Clear();
        appliedEffects.ClearOnce(clientId);

        RebuildEntries();
        SyncSelectionShields();
        SyncResetBusy();
        CheckRoundCompleted();
    }

    // ── 판 도중 재합류(할 일 48 C14 R4c) ────────────────────────────────────
    // 퇴장: 모델에서 지우기 직전 상태를 playerId 키로 RejoinMemory 에 넣는다. 모델·리셋 모델만 읽으므로 퇴장 순간 PlayerObject 유무와 무관하다.
    // playerId 는 판 명단의 "마지막 clientId" 로 찾는다 — 퇴장 콜백 순서(명단 묶임 풀기·레지스트리 삭제)와 무관. 명단 밖(데모 흐름 밖)이면 보관하지 않는다.

    private void SaveForRejoin(ulong clientId)
    {
        if (!MatchRoster.Server.IsActive || !MatchRoster.Server.TryGetPlayerIdByLastClient(clientId, out string playerId))
            return;

        appliedEffects.CaptureOnce(clientId, onceScratch);
        TraitRejoinSnapshot snapshot = TraitRejoin.Capture(model, resetModel, clientId, onceScratch);
        onceScratch.Clear();
        RejoinMemory.Server.SaveTraits(playerId, snapshot);
        Edit.Log($"{Tag} client {clientId}({playerId}) 퇴장 — 재합류용 특성 보관 {snapshot} (보관 {RejoinMemory.Server.TraitCount}건).", this);
    }

    // 재스폰 직후(새 PlayerObject 있음): 획득 목록·라운드 참가·팝업/재선택을 되돌리고, 효과 장부에 없는 획득 특성 효과만 건다(멱등).
    // 기록은 꺼내면 지워지므로 Drop(AlreadyHasPlayerObject) 경로에서 같은 사람에게 또 와도 두 번 되돌리지 않는다.
    private void HandlePlayerRejoinedServer(string playerId, ulong oldClientId, ulong newClientId)
    {
        if (!CanServe)
            return;

        if (!RejoinMemory.Server.TryTakeTraits(playerId, out TraitRejoinSnapshot snapshot))
        {
            Edit.Log($"{Tag} 재합류 {playerId} client {oldClientId}→{newClientId} — 보관한 특성 기록 없음.", this);
            return;
        }

        // 팝업·재선택은 전멸 팝업을 띄우는 조건(튜토리얼 진행 중)과 같을 때만 되살린다.
        MapSceneDirector tutorial = MapSceneDirector.Instance;
        bool allowReset = tutorial != null && tutorial.IsTutorialActive;
        TraitRejoinReset reset = TraitRejoin.Restore(model, resetModel, newClientId, snapshot, allowReset);
        if (reset == TraitRejoinReset.ReselectContinue)
            ContinueReselect(newClientId);
        else if (reset == TraitRejoinReset.ReselectOffer)
            roundDisplay.Value = (byte)TraitRoundDisplay.None; // 재선택 창엔 복도 안내 문구를 붙이지 않는다(ContinueReselect 와 같다)

        // '획득 1회' 표시를 새 clientId 로 먼저 옮긴다 — 아래에서 효과를 다시 걸 때 이미 한 1회 동작을 건너뛴다.
        appliedEffects.RestoreOnce(newClientId, snapshot.UsedOnce);
        int applied = ApplyMissingEffects(newClientId);
        RebuildEntries();
        SyncSelectionShields();
        SyncResetBusy();
        CheckRoundCompleted();

        Edit.Log($"{Tag} 재합류 {playerId} client {oldClientId}→{newClientId} — 특성 복원 {snapshot}, 효과 {applied}개 적용, " +
                 $"초기화 {reset}{(snapshot.HadResetState && !allowReset ? "(튜토리얼 아님 — 건너뜀)" : "")}.", this);
    }

    // 획득 목록과 효과 장부를 맞춘다 — 목록에 없는 기록은 되돌리고, 장부에 없는 획득 특성만 효과를 건다
    // (복원 직후 + 스폰 전에 자동 획득돼 효과를 놓친 것까지). 건 수.
    private int ApplyMissingEffects(ulong clientId)
    {
        appliedEffects.Reconcile(clientId, model.GetAcquired(clientId), applyScratch, revertScratch);
        for (int i = 0; i < revertScratch.Count; i++)
            revertScratch[i].Handle?.Unbind();
        revertScratch.Clear();

        int applied = 0;
        for (int i = 0; i < applyScratch.Count; i++)
        {
            int traitHash = applyScratch[i];
            ApplyEffect(clientId, traitHash, GetTrait(traitHash));
            if (appliedEffects.Has(clientId, traitHash))
                applied++;
        }

        applyScratch.Clear();
        return applied;
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

    private void ApplyEffect(ulong clientId, int traitHash, TraitDefinition trait)
    {
        if (trait == null)
        {
            Edit.LogError($"{Tag} 특성 {traitHash} 정의가 없다 — 효과 없이 획득만 기록한다.", this);
            return;
        }

        if (appliedEffects.Has(clientId, traitHash))
            return; // 이미 걸었다(멱등)

        // 수치형 기여(①②④⑦⑩·스탯)는 여기서 걸지 않는다 — 획득 행을 읽는 PlayerTraitModifiers 몫(할 일 125 2/5).
        // 이벤트형(③⑤⑥⑧⑨)은 플레이어 허브에 건다(3/5). 플레이어가 아직 없으면 기록하지 않는다 — 재합류·다음 맞추기(ApplyMissingEffects)가 다시 건다.
        Player player = FindPlayer(clientId);
        if (player == null)
        {
            Edit.LogWarning($"{Tag} client {clientId} 의 Player 를 못 찾았다 — '{trait.Id}' 이벤트 효과를 걸지 못했다(수치 효과는 획득 행으로 적용).", this);
            return;
        }

        var applied = new AppliedEffect();
        int onceRan = TraitEffectBinder.Bind(trait.Effects, PlayerTraitEffects.For(player), clientId, traitHash, appliedEffects, applied.Bindings);
        appliedEffects.Add(clientId, traitHash, applied);
        if (applied.Bindings.Count > 0 || onceRan > 0)
            Edit.Log($"{Tag} client {clientId} '{trait.Id}' 이벤트 효과 {applied.Bindings.Count}개 걸음, 1회 동작 {onceRan}개 실행.", this);
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
                entries.Add(new Entry { ClientId = clientId, TraitHash = generation, Kind = EntryKind.ResetPrompt });
        }
    }

    private void AddRows(ulong clientId, IReadOnlyList<int> traitIndices, EntryKind kind, int offerSeq)
    {
        for (int i = 0; i < traitIndices.Count; i++)
            entries.Add(new Entry { ClientId = clientId, TraitHash = traitIndices[i], Kind = kind, OfferSeq = offerSeq });
    }

    private void CollectRows(ulong clientId, EntryKind kind, List<int> result)
    {
        result.Clear();
        if (!IsSpawned)
            return;

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].ClientId == clientId && entries[i].Kind == kind)
                result.Add(entries[i].TraitHash);
        }
    }

    private void HandleEntriesChanged(NetworkListEvent<Entry> _) => ClientStateChanged?.Invoke();

    private void HandleRoundChanged(int previous, int current) => ClientStateChanged?.Invoke();

    private void HandleRoundDisplayChanged(byte previous, byte current) => ClientStateChanged?.Invoke();

    // ── 조회 ───────────────────────────────────────────────────────────────

    private Player FindPlayer(ulong clientId)
    {
        if (NetworkManager == null || !NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
            return null;

        return client.PlayerObject != null ? client.PlayerObject.GetComponent<Player>() : null;
    }

    // ── 고정 후보(할 일 125) ────────────────────────────────────────────────

    // 모델의 후보 공급자 — 그 플레이어 캐릭터의 tier 차 고정 후보(정의가 등록된 것만, 표 순서).
    private void FillFixedCandidates(ulong clientId, int tier, List<int> result)
    {
        TutorialCharacter character = ResolveCharacterServer(clientId);
        Catalog.GetCandidates(character, tier, result);
        if (character == TutorialCharacter.None)
            Edit.LogWarning($"{Tag} client {clientId} 의 캐릭터를 모른다 — {tier}차 후보 0장.", this);
        else if (result.Count == 0 && tier <= TraitCatalog.TierCount)
            Edit.Log($"{Tag} client {clientId} {character} {tier}차 — 등록된 정의가 없어 후보 0장(건너뜀).", this);
    }

    // 캐릭터 판별 = 기존 방식(스폰된 Player 의 PrefabIdHash → 로스터 역조회, 실패 시 고유 컴포넌트 — TutorialLocalPlayerInfo).
    // 플레이어 오브젝트가 아직 없으면(스폰 전 제시) 서버 캐릭터 선택 저장소 → 로스터 칸의 캐릭터.
    private TutorialCharacter ResolveCharacterServer(ulong clientId)
    {
        Player player = FindPlayer(clientId);
        if (player != null)
            return TutorialLocalPlayerInfo.ResolveCharacter(player);

        NetworkLoadingFlowController flow = NetworkManager != null ? NetworkManager.GetComponent<NetworkLoadingFlowController>() : null;
        CharacterRoster roster = flow != null ? flow.CharacterRoster : null;
        if (roster != null &&
            ServerCharacterSelectionStore.TryGet(clientId, out int characterId) &&
            roster.TryGetAvailableCharacter(characterId, out CharacterRoster.Entry entry) && entry != null)
            return entry.TutorialCharacter;

        return TutorialCharacter.None;
    }

    // ── 특성 목록 버전(할 일 125) ───────────────────────────────────────────

    private void HandleCatalogVersionChanged(int previous, int current) => CheckCatalogVersion(current);

    // 클라: 서버가 쓴 버전과 내 풀 버전을 비교한다 — 다르면 같은 빌드·같은 풀이 아니다(특성 해시가 서로 다른 정의를 가리킬 수 있다).
    private void CheckCatalogVersion(int serverVersion)
    {
        TraitCatalogIndex catalog = Catalog;
        TraitVersionCheck check = TraitCatalog.CompareVersion(serverVersion, catalog.Version);
        if (check != TraitVersionCheck.Mismatch || versionMismatchLogged)
            return;

        versionMismatchLogged = true;
        Edit.LogError($"{Tag} 특성 목록 버전 불일치 — 서버 {serverVersion}, 이 클라 {catalog.Version}. " +
                      "서버와 같은 빌드(같은 TraitPool·정의 ID)로 접속해야 한다. 특성 표시·선택이 어긋날 수 있다.", this);
    }

    private void LogCatalogErrors(TraitCatalogIndex catalog)
    {
        IReadOnlyList<string> errors = catalog.Errors;
        for (int i = 0; i < errors.Count; i++)
            Edit.LogError($"{Tag} 특성 풀 검증 오류: {errors[i]}", this);
    }
}
