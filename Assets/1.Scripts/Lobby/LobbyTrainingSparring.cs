using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 간파 훈련 T31~T40·T50 허수아비(할 일 86 T5c → 157 ⑦·⑧ — PLAN §5 ⑦·⑧, 명세 §5 T30~T50·§6) — <see cref="LobbyTrainingDirector"/> 와 같은 오브젝트(씬 상주 NetworkObject)에 붙는다.
/// 연결 규칙은 <see cref="LobbySparringLink"/>, 단계 판정은 훈련 상대(<see cref="TrainingSparringBot"/>·<see cref="TrainingSparringModel"/>),
/// T32·T34 "새 시전" 판정은 <see cref="LobbyCastWindow"/>. 여기는 스폰·구독·짝짓기 입력·보고만 한다.
/// <list type="bullet">
/// <item>시작 = 진행기 T31(Counter) 이 시작되는 순간(T30 집결 확정 — 집결 대기 중엔 스폰하지 않는다): 상대를 <see cref="LobbyTrainingSparringArena.SpawnPoint"/> 에 스폰하고
/// C 위치·지정 벽을 <see cref="TrainingSparringBot.ServerConfigureArena"/> 로 넣은 뒤 같은 평가에서 T31 시작 알림까지 준다. 이미 있으면 재사용.</item>
/// <item>시작 알림: T31~T40 이 시작될 때마다 <see cref="TrainingSparringBot.ServerNotifyFlowStageStarted"/> 1회(앞으로만) — 그 단계 revision 을 기억한다.
/// T32·T34 알림이면 판정 창을 열고 그 순간 접속자별 시전 기준선(Player 인스턴스, <see cref="Player.ServerCastSeq"/>)을 잡는다.</item>
/// <item>짝짓기 입력: 접속자 Player 의 <see cref="Player.ServerAttackLanded"/>(대상에 이 상대가 있을 때)·상대의 CC·벽·넉백 끝을 프레임 번호와 함께 창에 넣고,
/// 다음 프레임 이후 <see cref="LobbyCastWindow.Resolve"/> → 상대에 인정(<see cref="TrainingSparringBot.ServerAcceptCrowdControl"/>·<see cref="TrainingSparringBot.ServerAcceptWallImpact"/>)
/// 또는 T34 재시도(<see cref="TrainingSparringBot.ServerRestoreAfterMiss"/>). Player 구독은 교체(새 인스턴스)를 따라 매 프레임 맞춘다.</item>
/// <item>T33: 상대가 CC 반응이 끝난 뒤 위치 복귀를 실행하면(<see cref="TrainingSparringBot.ServerResetExecuted"/>) 플레이어 쪽 <see cref="TrainingResetOptions.PracticeReset"/> 회차당 1회(Q10 A).</item>
/// <item>완료 보고: 상대 <see cref="TrainingSparringBot.ServerStageCompleted"/> → 다음 Update 에 <see cref="LobbyTrainingDirector.ServerReportActivityCompleted"/>(Activity, 알림 때 revision) 1회 —
/// 피해·사망 판정 도중 단계를 넘기지 않는다. 진행기 단계와 어긋난 보고·중복·지난 revision 은 무시(경고).</item>
/// <item>단계 안 사건 무전(TRAIN_03_COUNTER/WALL/COMBAT)은 끈다(Q6 A — 명세 §3.3 중복 대사 금지, 문구 키 보존).</item>
/// <item>정리: 간파 훈련이 아닌 단계로 넘어가거나(T50 등) 진행기 디스폰 때 살아 있는 상대 디스폰·회차 끝. T40 에서 처치된 상대는 사망 연출 뒤 스스로 디스폰한다.</item>
/// <item>T50 자유 연습(⑧): T50 동안 허수아비 1개(<see cref="LobbyRules"/> 의 TrainingDummy 계열 프리팹 — 사망 없음, 자리 = A/B 위치). 출구 집결 확정(→ L30)의
/// 단계 변경에서 정리 — 진행기가 그 뒤에 종료 텔레포트한다. 체력 하한은 행 trainingProfile(T50 = Training — 출구 확정(→ L30) 때 풀림, ⑨)대로 진행기가 맞춘다.</item>
/// </list>
/// 제압 중 쿨타임 가속은 보류(할 일 91 GrantServer 로 나중 연결, Q13 A — 훈련이 임의 버프를 만들지 않는다) — 여기엔 없다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyTrainingSparring : NetworkBehaviour
{
    private const string Tag = "[LobbyTraining/Train03]";

    [Tooltip("훈련 상대 프리팹(TrainingSparringBot.prefab) — NetworkPrefabs 에 등록돼 있어야 한다.")]
    [SerializeField] private GameObject sparringPrefab;

    [Tooltip("A/B·C 위치와 지정 벽(LobbyTraining/Train03Arena) — 임시 좌표, 최종 위치는 사용자.")]
    [SerializeField] private LobbyTrainingSparringArena arena;

    // 서버
    private LobbyTrainingDirector _director;
    private readonly LobbySparringLink _link = new LobbySparringLink();
    private readonly LobbyCastWindow _castWindow = new LobbyCastWindow();
    private readonly Queue<LobbyTutorialActivity> _pendingReports = new Queue<LobbyTutorialActivity>();
    private readonly Dictionary<ulong, PlayerHook> _playerHooks = new Dictionary<ulong, PlayerHook>();
    private readonly List<ulong> _staleHooks = new List<ulong>();
    private readonly List<SkillTooltipTag> _sourceTags = new List<SkillTooltipTag>();
    private int _noticeRevision;
    private TrainingSparringBot _bot;
    private NetworkObject _dummy;
    private bool _warnedNoDummyPrefab;

    // 구독한 Player 인스턴스와 그 핸들러 — 교체(새 인스턴스)·연결 종료 때 풀어 준다.
    private readonly struct PlayerHook
    {
        public PlayerHook(Player player, Action<PlayerAttackLanded> handler) => (Player, Handler) = (player, handler);
        public Player Player { get; }
        public Action<PlayerAttackLanded> Handler { get; }
    }

    /// <summary>스폰된 간파 훈련 연결(없으면 null) — 서버 이벤트를 구독할 곳.</summary>
    public static LobbyTrainingSparring Active { get; private set; }

    /// <summary>[서버] 지금 회차의 훈련 상대(없으면 null).</summary>
    public TrainingSparringBot ServerBot => _bot;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Active = this;

        if (!IsServer)
            return;

        _director = GetComponent<LobbyTrainingDirector>();
        if (_director == null)
        {
            Debug.LogError($"{Tag} 같은 오브젝트에 LobbyTrainingDirector 가 없다 — 간파 훈련이 동작하지 않는다.", this);
            return;
        }

        if (sparringPrefab == null || sparringPrefab.GetComponent<TrainingSparringBot>() == null)
            Debug.LogWarning($"{Tag} 훈련 상대 프리팹(TrainingSparringBot)이 비었다 — 간파 훈련이 끝나지 않는다.", this);
        if (arena == null || arena.SpawnPoint == null)
            Debug.LogWarning($"{Tag} 배치(Train03Arena·A/B 위치)가 비었다 — 진행기 위치에 스폰한다.", this);
        else if (arena.DesignatedWalls == null || arena.DesignatedWalls.Length == 0)
            Debug.LogWarning($"{Tag} 지정 벽이 비었다 — T34 벽 목표를 채울 수 없어 간파 훈련이 T34 에서 멈춘다.", this);

        _director.ServerFlowChanged += EvaluateServer;

        // 진행기가 먼저 스폰돼 첫 알림을 놓쳤을 수 있다 — 지금 상태로 한 번 맞춘다.
        EvaluateServer();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            if (_director != null)
                _director.ServerFlowChanged -= EvaluateServer;

            if (_link.IsRoundActive || _bot != null)
                EndServer("진행기 디스폰");
            DespawnDummyServer("진행기 디스폰");
            UnhookAllPlayers();
            _director = null;
        }

        if (Active == this)
            Active = null;
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        UnhookAllPlayers();
        if (Active == this)
            Active = null;
        base.OnDestroy();
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer || !_link.IsRoundActive)
            return;

        SyncPlayerHooks();
        ResolveCastWindow();

        // 피해·사망 콜백 안에서 단계를 넘기지 않는다 — Update 에서 보고한다(보고가 간파 훈련 이탈이면 이 안에서 정리될 수 있다).
        while (_link.IsRoundActive && _pendingReports.Count > 0)
            ReportServer(_pendingReports.Dequeue());
    }

    // ─── 서버: 시작·알림·정리 ────────────────────────────────────────────

    private void EvaluateServer()
    {
        if (_director == null)
            return;

        var activity = _director.CurrentActivity;
        if (LobbySparringLink.BotPresent(activity))
        {
            if (!_link.IsRoundActive)
                BeginServer();
            if (_bot == null)
                return;

            if (_link.TryTakeFlowNotice(activity, out var notice))
            {
                _noticeRevision = _director.Revision;
                BeginCastWindow(notice);
                var accepted = _bot.ServerNotifyFlowStageStarted(notice);
                Debug.Log($"[SceneFlow] {Tag} 시작 알림 {notice} rev {_noticeRevision} → 상대 {(accepted ? "수락" : "확인만")} (상대 단계 {_bot.SparringStage}, 판정 창 {_castWindow.Mode})");
            }
        }
        else if (_link.IsRoundActive || _bot != null)
        {
            EndServer($"단계 이탈 → {_director.DescribeStep()}");
        }

        // T50 자유 연습 허수아비 — 훈련 상대 정리 뒤에 스폰, T50 을 떠나면(출구 집결 확정 → L30) 종료 텔레포트 전에 정리.
        if (LobbySparringLink.FreePracticeDummyPresent(activity))
        {
            if (_dummy == null || !_dummy.IsSpawned)
                _dummy = SpawnDummyServer();
        }
        else if (_dummy != null)
        {
            DespawnDummyServer($"단계 이탈 → {_director.DescribeStep()}");
        }
    }

    // T50 허수아비 1개 — 기존 TrainingDummy 계열(사망 없음·피해 숫자·회복). 자리 = Train03Arena A/B 위치(씬 무수정), 없으면 진행기 위치.
    private NetworkObject SpawnDummyServer()
    {
        var prefab = _director.FreePracticeDummyPrefab;
        if (prefab == null || prefab.GetComponent<NetworkObject>() == null)
        {
            if (!_warnedNoDummyPrefab)
            {
                _warnedNoDummyPrefab = true;
                Debug.LogWarning($"{Tag} 자유 연습 허수아비 프리팹(LobbyRules.freePracticeDummyPrefab)이 비었거나 NetworkObject 가 없다 — 허수아비 없이 출구 집결만 한다.", this);
            }

            return null;
        }

        var point = arena != null ? arena.SpawnPoint : null;
        var position = point != null ? point.position : transform.position;
        var rotation = point != null ? Quaternion.Euler(0f, point.eulerAngles.y, 0f) : Quaternion.identity;
        var go = Instantiate(prefab, position, rotation);
        SceneManager.MoveGameObjectToScene(go, gameObject.scene);
        go.name = $"{prefab.name}_FreePractice";
        var no = go.GetComponent<NetworkObject>();
        no.Spawn(true);
        Debug.Log($"[SceneFlow] {Tag} T50 자유 연습 허수아비 스폰 — {go.name} @ {position}");
        return no;
    }

    private void DespawnDummyServer(string reason)
    {
        if (_dummy == null)
            return;

        var despawned = _dummy.IsSpawned;
        if (despawned)
            _dummy.Despawn(true);
        _dummy = null;
        Debug.Log($"[SceneFlow] {Tag} T50 자유 연습 허수아비 정리({reason}) — 디스폰 {despawned}");
    }

    private void BeginServer()
    {
        _link.BeginRound();
        _castWindow.Reset();
        _pendingReports.Clear();

        if (_bot == null || !_bot.IsSpawned)
            _bot = SpawnBotServer();
        else
            Debug.Log($"[SceneFlow] {Tag} 훈련 상대 재사용 — {_bot.name}");

        if (_bot == null)
        {
            Debug.LogError($"{Tag} 훈련 상대를 스폰하지 못했다 — 간파 훈련을 끝낼 수 없다(프리팹·NetworkPrefabs 확인).", this);
            return;
        }

        _bot.ServerConfigureArena(arena != null ? arena.ResetPoint : null, arena != null ? arena.DesignatedWalls : null);
        Subscribe(_bot);
        SyncPlayerHooks();
        Debug.Log($"[SceneFlow] {Tag} 간파 훈련 회차 {_link.Round} 시작 — 상대 {_bot.name} @ {_bot.transform.position}, " +
                  $"C 위치 {(arena != null && arena.ResetPoint != null ? arena.ResetPoint.position.ToString() : "(없음 — 제자리)")}, " +
                  $"지정 벽 {(arena != null && arena.DesignatedWalls != null ? arena.DesignatedWalls.Length : 0)}개");
    }

    private TrainingSparringBot SpawnBotServer()
    {
        if (sparringPrefab == null)
            return null;

        var point = arena != null ? arena.SpawnPoint : null;
        var position = point != null ? point.position : transform.position;
        var rotation = point != null ? Quaternion.Euler(0f, point.eulerAngles.y, 0f) : Quaternion.identity;
        var go = Instantiate(sparringPrefab, position, rotation);
        // 활성 씬이 로비가 아닐 때도 로비 씬과 같이 정리되게 진행기의 씬으로 옮긴다.
        SceneManager.MoveGameObjectToScene(go, gameObject.scene);
        go.name = $"{sparringPrefab.name}_Train03";
        var no = go.GetComponent<NetworkObject>();
        var bot = go.GetComponent<TrainingSparringBot>();
        if (no == null || bot == null)
        {
            Debug.LogError($"{Tag} 프리팹 '{sparringPrefab.name}' 에 NetworkObject/TrainingSparringBot 이 없다.", this);
            Destroy(go);
            return null;
        }

        no.Spawn(true);
        return bot;
    }

    private void EndServer(string reason)
    {
        var round = _link.Round;
        var reported = _link.LastReported;
        _link.EndRound();
        _castWindow.Reset();
        _pendingReports.Clear();
        UnhookAllPlayers();

        var despawned = false;
        if (_bot != null)
        {
            Unsubscribe(_bot);
            // 살아 있는 상대만 — T40 에서 처치된 상대는 사망 연출 뒤 스스로 디스폰한다.
            if (_bot.IsSpawned && _bot.CurrentHealth > 0)
            {
                _bot.NetworkObject.Despawn(true);
                despawned = true;
            }

            _bot = null;
        }

        Debug.Log($"[SceneFlow] {Tag} 간파 훈련 정리({reason}) — 회차 {round}, 마지막 보고 {reported}, 상대 디스폰 {despawned}");
    }

    // ─── 서버: "새 시전" 판정 창 ─────────────────────────────────────────

    // T32·T34 알림 순간 — 판정 창을 열고 접속자별 기준선(Player 인스턴스·지금 시전 seq)을 잡는다. 진입 때 없던 사람·교체로 생긴 새 인스턴스는 기준선 0.
    private void BeginCastWindow(LobbyTutorialActivity notice)
    {
        var mode = LobbySparringLink.CastWindowModeFor(notice);
        if (mode == LobbyCastWindowMode.None)
        {
            _castWindow.Close();
            return;
        }

        _castWindow.Begin(mode, Time.frameCount);
        var nm = NetworkManager;
        if (nm == null)
            return;

        var baselines = 0;
        foreach (var clientId in nm.ConnectedClientsIds)
        {
            if (!_director.TryGetPlayer(clientId, out var player) || player == null)
                continue;
            _castWindow.SetBaseline(clientId, player.GetInstanceID(), player.ServerCastSeq);
            baselines++;
        }

        Debug.Log($"[SceneFlow] {Tag} 판정 창 {mode} 시작 — 프레임 {Time.frameCount}, 기준선 {baselines}명");
    }

    private void ResolveCastWindow()
    {
        var result = _castWindow.Resolve(Time.frameCount);
        if (!result.Any || _bot == null)
            return;

        if (result.CrowdControlAccepted && _bot.ServerAcceptCrowdControl(result.EventFrame))
            Debug.Log($"[SceneFlow] {Tag} T32 CC 인정 — client {result.ClientId} 시전 {result.CastId}(프레임 {result.EventFrame})");

        if (result.WallAccepted && _bot.ServerAcceptWallImpact(result.EventFrame))
            Debug.Log($"[SceneFlow] {Tag} T34 지정 벽 충돌 인정(프레임 {result.EventFrame})");
        else if (result.RetryRequested)
            _bot.ServerRestoreAfterMiss();
    }

    // 접속자 Player 의 적중 통지를 구독한다 — 캐릭터 교체(같은 clientId, 새 Player)·연결 종료를 따라 매 프레임 맞춘다.
    private void SyncPlayerHooks()
    {
        var nm = NetworkManager;
        if (nm == null || _director == null)
            return;

        _staleHooks.Clear();
        foreach (var pair in _playerHooks)
        {
            if (pair.Value.Player == null || !nm.ConnectedClients.ContainsKey(pair.Key)
                || !_director.TryGetPlayer(pair.Key, out var current) || current != pair.Value.Player)
                _staleHooks.Add(pair.Key);
        }

        foreach (var clientId in _staleHooks)
        {
            var hook = _playerHooks[clientId];
            if (hook.Player != null)
                hook.Player.ServerAttackLanded -= hook.Handler;
            _playerHooks.Remove(clientId);
        }

        foreach (var clientId in nm.ConnectedClientsIds)
        {
            if (_playerHooks.ContainsKey(clientId) || !_director.TryGetPlayer(clientId, out var player) || player == null)
                continue;

            var id = clientId;
            var instanceKey = player.GetInstanceID();
            Action<PlayerAttackLanded> handler = landed => HandlePlayerAttackLanded(id, instanceKey, landed);
            player.ServerAttackLanded += handler;
            _playerHooks[clientId] = new PlayerHook(player, handler);
        }
    }

    private void UnhookAllPlayers()
    {
        foreach (var pair in _playerHooks)
            if (pair.Value.Player != null)
                pair.Value.Player.ServerAttackLanded -= pair.Value.Handler;
        _playerHooks.Clear();
    }

    // 🔴 Targets 는 발행자 버퍼 — 이 호출 안에서만 읽는다.
    private void HandlePlayerAttackLanded(ulong clientId, int instanceKey, PlayerAttackLanded landed)
    {
        if (_bot == null || !_link.IsRoundActive || landed.Targets == null)
            return;

        var hitBot = false;
        for (var i = 0; i < landed.Targets.Count; i++)
        {
            if (ReferenceEquals(landed.Targets[i], _bot))
            {
                hitBot = true;
                break;
            }
        }

        if (!hitBot)
            return;

        _castWindow.RecordLanded(Time.frameCount, clientId, instanceKey, landed.CastId, PassesTagRule(landed.Source));
    }

    // 지금 행의 RequiredSkillTags — 표 플래그(enforceRequiredSkillTags, 기본 꺼짐 Q12a)일 때만 검사. 출처가 스킬이 아니면 태그 없음.
    private bool PassesTagRule(UnityEngine.Object source)
    {
        var table = _director != null ? _director.Table : null;
        var enforce = table != null && table.EnforceRequiredSkillTags;
        if (!enforce)
            return true;

        var step = _director.CurrentStep;
        _sourceTags.Clear();
        if (source is PlayerSkillBase skill && skill.Data != null)
            SkillTooltipTags.TryParse(skill.Data.Tooltip.TagIds, _sourceTags, out _);
        return LobbyCastWindow.PassesTagRule(true, step != null ? step.requiredSkillTags : null, _sourceTags);
    }

    // ─── 서버: 상대 이벤트 ──────────────────────────────────────────────

    private void Subscribe(TrainingSparringBot bot)
    {
        bot.ServerStageCompleted += HandleBotStageCompleted;
        bot.ServerResetExecuted += HandleBotResetExecuted;
        bot.ServerCrowdControlApplied += HandleBotCrowdControlApplied;
        bot.ServerWallImpact += HandleBotWallImpact;
        bot.ServerKnockbackEnded += HandleBotKnockbackEnded;
    }

    private void Unsubscribe(TrainingSparringBot bot)
    {
        bot.ServerStageCompleted -= HandleBotStageCompleted;
        bot.ServerResetExecuted -= HandleBotResetExecuted;
        bot.ServerCrowdControlApplied -= HandleBotCrowdControlApplied;
        bot.ServerWallImpact -= HandleBotWallImpact;
        bot.ServerKnockbackEnded -= HandleBotKnockbackEnded;
    }

    private void HandleBotStageCompleted(LobbyTutorialActivity completed) => _pendingReports.Enqueue(completed);

    private void HandleBotCrowdControlApplied(int episode, TrainingSparringCrowdControl kinds, bool applied) =>
        _castWindow.RecordCrowdControl(Time.frameCount, episode, kinds, applied);

    private void HandleBotWallImpact(int episode, bool designated) => _castWindow.RecordWallImpact(Time.frameCount, episode, designated);

    private void HandleBotKnockbackEnded(int episode) => _castWindow.RecordKnockbackEnded(Time.frameCount, episode);

    // 상대가 T33 위치 복귀를 마쳤다(C 위치·체력, 취약·칸 보존) — 플레이어 쪽 체력·쿨타임·대시 1회(Q10 A). 분노 재지급 없음. T33 완료 보고는 상대 모델이 낸다.
    private void HandleBotResetExecuted()
    {
        if (_director == null || !_link.TryConsumePracticeReset())
        {
            Debug.LogWarning($"{Tag} T33 플레이어 리셋 중복 — 무시(회차 {_link.Round}).", this);
            return;
        }

        _director.ServerResetTrainingRoster(TrainingResetOptions.PracticeReset);
        Debug.Log($"[SceneFlow] {Tag} T33 위치 복귀 — 상대 C 위치 + 훈련 명단 리셋(PracticeReset)");
    }

    private void ReportServer(LobbyTutorialActivity completed)
    {
        if (_director == null)
            return;

        if (!_link.TryAcceptReport(completed, _director.CurrentActivity))
        {
            Debug.LogWarning($"{Tag} 상대 단계 완료 {completed} 무시 — 진행기 {_director.DescribeStep()}, 마지막 보고 {_link.LastReported}.", this);
            return;
        }

        if (_director.ServerReportActivityCompleted(completed, _noticeRevision))
            Debug.Log($"[SceneFlow] {Tag} 상대 단계 완료 {completed} → 진행기 보고");
    }
}
