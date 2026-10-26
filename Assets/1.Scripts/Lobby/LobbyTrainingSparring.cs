using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// TRAIN_03 간파·CC·벽·제압(할 일 86 T5c — PLAN §3.5, 명세 v0.2 §9) — <see cref="LobbyTrainingDirector"/> 와 같은 오브젝트(씬 상주 NetworkObject)에 붙는다.
/// 연결 규칙은 <see cref="LobbySparringLink"/>, 단계·CC 판정은 훈련 상대(<see cref="TrainingSparringBot"/>·<see cref="TrainingSparringModel"/>). 여기는 스폰·구독·보고만 한다.
/// <list type="bullet">
/// <item>시작 = 진행기 Train03A 가 진행 중이 되는 순간(시작 집결 확정 — 집결 대기 중엔 스폰하지 않는다): 상대를 <see cref="LobbyTrainingSparringArena.SpawnPoint"/>(A/B)에 스폰하고
/// C 위치·지정 벽을 <see cref="TrainingSparringBot.ServerConfigureArena"/> 로 넣은 뒤 같은 평가에서 Train03A 진행 알림까지 준다. 이미 있으면 재사용.</item>
/// <item>진행 알림: Train03A~D 가 진행 중이 될 때마다 <see cref="TrainingSparringBot.ServerNotifyFlowStageStarted"/> 1회(앞으로만).
/// Train03C 알림 = 상대 B→C 초기화(C 위치 재배치) → <see cref="TrainingSparringBot.ServerResetExecuted"/> → 플레이어 리셋(<see cref="TrainingResetOptions.PracticeReset"/>) 회차당 1회.</item>
/// <item>완료 보고: 상대 <see cref="TrainingSparringBot.ServerStageCompleted"/> → 다음 프레임 <see cref="LobbyTrainingDirector.ServerReportStageCompleted"/>(Train03A~D) 1회 —
/// 피해·사망 판정 도중 단계를 넘기지 않는다. 진행기 단계와 어긋난 보고·중복은 무시(경고).</item>
/// <item>무전: <see cref="ServerFirstCounterSucceeded"/>(TRAIN_03_COUNTER)·<see cref="ServerFirstValidWallImpact"/>(TRAIN_03_WALL)·<see cref="ServerFinishStarted"/>(TRAIN_03_COMBAT) — 회차당 1회.
/// 이벤트를 낼 때 여기서 바로 <see cref="LobbyRadio.ServerPlay"/>(LobbyRadioIds) 를 부른다. 단계 진입 대사(START/CC/RESET·WALL_GUIDE/SUPPRESS)는 <see cref="LobbyRadio"/> 쪽.</item>
/// <item>정리: Train03 이 아닌 단계로 넘어가거나(TrainEnd 등) 진행기 디스폰(판 끝 로비 복귀·씬 전환) 때 살아 있는 상대 디스폰·회차 끝.
/// D 에서 처치된 상대는 사망 연출 뒤 스스로 디스폰한다(마지막 처치 연출을 끊지 않는다).</item>
/// </list>
/// 제압 중 쿨타임 가속은 보류(할 일 91 GrantServer 로 나중 연결) — 여기엔 없다. 마무리 체력은 상대 프리팹 finishHealth(임시 60).
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
    private readonly Queue<LobbyTrainingStage> _pendingReports = new Queue<LobbyTrainingStage>();
    private TrainingSparringBot _bot;
    private bool _pendingFirstCounter;
    private bool _warnedNoRadio;

    /// <summary>스폰된 TRAIN_03 연결(없으면 null) — 서버 이벤트를 구독할 곳.</summary>
    public static LobbyTrainingSparring Active { get; private set; }

    /// <summary>[서버] A 에서 첫 간파 성공 — TRAIN_03_COUNTER 무전용. Train03 회차당 1회, 판정 콜백 밖(다음 프레임)에서 낸다.</summary>
    public event Action ServerFirstCounterSucceeded;

    /// <summary>[서버] C 에서 첫 유효 벽 충돌(칸 감소를 동반한 지정 벽 충돌 = 벽 목표 기록) — TRAIN_03_WALL 무전용. Train03 회차당 1회, 판정 콜백 밖에서 낸다.</summary>
    public event Action ServerFirstValidWallImpact;

    /// <summary>[서버] D 에서 첫 마무리 진입(제압 중 유효 피해 + 제압 종료 = 마무리 체력 설정 직후, 상대 모델 IsFinishing) — TRAIN_03_COMBAT 무전용. Train03 회차당 1회, 판정 콜백 밖에서 낸다.</summary>
    public event Action ServerFinishStarted;

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
            Debug.LogError($"{Tag} 같은 오브젝트에 LobbyTrainingDirector 가 없다 — TRAIN_03 이 동작하지 않는다.", this);
            return;
        }

        if (sparringPrefab == null || sparringPrefab.GetComponent<TrainingSparringBot>() == null)
            Debug.LogWarning($"{Tag} 훈련 상대 프리팹(TrainingSparringBot)이 비었다 — TRAIN_03 이 끝나지 않는다.", this);
        if (arena == null || arena.SpawnPoint == null)
            Debug.LogWarning($"{Tag} 배치(Train03Arena·A/B 위치)가 비었다 — 진행기 위치에 스폰한다.", this);
        else if (arena.DesignatedWalls == null || arena.DesignatedWalls.Length == 0)
            Debug.LogWarning($"{Tag} 지정 벽이 비었다 — C 벽 목표를 채울 수 없어 TRAIN_03 이 C 에서 멈춘다.", this);

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
            _director = null;
        }

        if (Active == this)
            Active = null;
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
        if (!IsSpawned || !IsServer || !_link.IsRoundActive)
            return;

        // 무전 이벤트 먼저 — 같은 넉백의 벽 + 마지막 칸이면 벽 무전 뒤 C 완료 보고(명세 §9.5 순서).
        if (_pendingFirstCounter)
        {
            _pendingFirstCounter = false;
            Debug.Log($"[SceneFlow] {Tag} 첫 간파 성공(회차 {_link.Round}) — {nameof(ServerFirstCounterSucceeded)}");
            PlayRadioServer(LobbyRadioIds.Train03Counter);
            ServerFirstCounterSucceeded?.Invoke();
        }

        var model = _bot != null ? _bot.ServerModel : null;
        if (model != null && _link.TryRaiseFirstWall(model.HasRecordedWall))
        {
            Debug.Log($"[SceneFlow] {Tag} 첫 유효 벽 충돌(회차 {_link.Round}) — {nameof(ServerFirstValidWallImpact)}");
            PlayRadioServer(LobbyRadioIds.Train03Wall);
            ServerFirstValidWallImpact?.Invoke();
        }

        // 피해 없이 끝난 제압은 마무리가 아니다(IsFinishing false) — 무전 없음.
        if (model != null && _link.TryRaiseFirstFinish(model.IsFinishing))
        {
            Debug.Log($"[SceneFlow] {Tag} 첫 마무리 진입(회차 {_link.Round}) — {nameof(ServerFinishStarted)}");
            PlayRadioServer(LobbyRadioIds.Train03Combat);
            ServerFinishStarted?.Invoke();
        }

        // 피해·사망 콜백 안에서 단계를 넘기지 않는다 — 다음 프레임에 보고한다(보고가 Train03 이탈이면 이 안에서 정리될 수 있다).
        while (_link.IsRoundActive && _pendingReports.Count > 0)
            ReportServer(_pendingReports.Dequeue());
    }

    // 단계 안 사건 무전 — 회차당 1회는 무전 쪽(oncePerRound 기본값)도 막는다. 무전이 없으면(씬 배치 누락) 경고 한 번만.
    private void PlayRadioServer(string id)
    {
        var radio = LobbyRadio.Active;
        if (radio == null)
        {
            if (!_warnedNoRadio)
            {
                _warnedNoRadio = true;
                Debug.LogWarning($"{Tag} LobbyRadio 가 없다 — {id} 등 TRAIN_03 무전을 건너뛴다.", this);
            }
            return;
        }

        radio.ServerPlay(id);
    }

    // ─── 서버: 시작·알림·정리 ────────────────────────────────────────────

    private void EvaluateServer()
    {
        if (_director == null)
            return;

        var stage = _director.Stage;
        if (LobbySparringLink.BotPresent(stage, _director.Phase))
        {
            if (!_link.IsRoundActive)
                BeginServer();
            if (_bot == null)
                return;

            if (_link.TryTakeFlowNotice(stage, _director.Phase, out var notice))
            {
                var accepted = _bot.ServerNotifyFlowStageStarted(notice);
                Debug.Log($"[SceneFlow] {Tag} 진행 알림 {notice} → 상대 {(accepted ? "수락" : "확인만")} (상대 단계 {_bot.SparringStage})");
            }
        }
        else if (_link.IsRoundActive || _bot != null)
        {
            EndServer($"단계 이탈 → {stage}/{_director.Phase}");
        }
    }

    private void BeginServer()
    {
        _link.BeginRound();
        _pendingReports.Clear();
        _pendingFirstCounter = false;

        if (_bot == null || !_bot.IsSpawned)
            _bot = SpawnBotServer();
        else
            Debug.Log($"[SceneFlow] {Tag} 훈련 상대 재사용 — {_bot.name}");

        if (_bot == null)
        {
            Debug.LogError($"{Tag} 훈련 상대를 스폰하지 못했다 — TRAIN_03 을 끝낼 수 없다(프리팹·NetworkPrefabs 확인).", this);
            return;
        }

        _bot.ServerConfigureArena(arena != null ? arena.ResetPoint : null, arena != null ? arena.DesignatedWalls : null);
        Subscribe(_bot);
        Debug.Log($"[SceneFlow] {Tag} TRAIN_03 회차 {_link.Round} 시작 — 상대 {_bot.name} @ {_bot.transform.position}, " +
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
        _pendingReports.Clear();
        _pendingFirstCounter = false;

        var despawned = false;
        if (_bot != null)
        {
            Unsubscribe(_bot);
            // 살아 있는 상대만 — D 에서 처치된 상대는 사망 연출 뒤 스스로 디스폰한다.
            if (_bot.IsSpawned && _bot.CurrentHealth > 0)
            {
                _bot.NetworkObject.Despawn(true);
                despawned = true;
            }

            _bot = null;
        }

        Debug.Log($"[SceneFlow] {Tag} TRAIN_03 정리({reason}) — 회차 {round}, 마지막 보고 {reported}, 상대 디스폰 {despawned}");
    }

    // ─── 서버: 상대 이벤트 ──────────────────────────────────────────────

    private void Subscribe(TrainingSparringBot bot)
    {
        bot.ServerStageCompleted += HandleBotStageCompleted;
        bot.ServerResetExecuted += HandleBotResetExecuted;
        bot.ServerCounterSucceeded += HandleBotCounterSucceeded;
    }

    private void Unsubscribe(TrainingSparringBot bot)
    {
        bot.ServerStageCompleted -= HandleBotStageCompleted;
        bot.ServerResetExecuted -= HandleBotResetExecuted;
        bot.ServerCounterSucceeded -= HandleBotCounterSucceeded;
    }

    private void HandleBotStageCompleted(LobbyTrainingStage completed) => _pendingReports.Enqueue(completed);

    private void HandleBotCounterSucceeded(ulong attacker)
    {
        // 이벤트 순간 모델은 이미 이 성공을 반영했다 — A 의 성공만 TRAIN_03_COUNTER.
        var model = _bot != null ? _bot.ServerModel : null;
        if (model != null && _link.TryRaiseFirstCounter(model.Stage))
            _pendingFirstCounter = true;
    }

    // 상대가 B→C 초기화를 마쳤다(C 위치 재배치·체력·칸 가득) — 플레이어 쪽 체력·쿨타임·대시 1회. 분노 재지급 없음.
    private void HandleBotResetExecuted()
    {
        if (_director == null || !_link.TryConsumePracticeReset())
        {
            Debug.LogWarning($"{Tag} B→C 초기화 플레이어 리셋 중복 — 무시(회차 {_link.Round}).", this);
            return;
        }

        _director.ServerResetTrainingRoster(TrainingResetOptions.PracticeReset);
        Debug.Log($"[SceneFlow] {Tag} B→C 초기화 — 상대 C 위치 재배치 + 훈련 명단 리셋(PracticeReset)");
    }

    private void ReportServer(LobbyTrainingStage completed)
    {
        if (_director == null)
            return;

        if (!_link.TryAcceptReport(completed, _director.Stage, _director.Phase))
        {
            Debug.LogWarning($"{Tag} 상대 단계 완료 {completed} 무시 — 진행기 {_director.Stage}/{_director.Phase}, 마지막 보고 {_link.LastReported}.", this);
            return;
        }

        if (_director.ServerReportStageCompleted(completed))
            Debug.Log($"[SceneFlow] {Tag} 상대 단계 완료 {completed} → 진행기 보고");
    }
}
