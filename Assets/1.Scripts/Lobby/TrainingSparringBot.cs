using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// [서버 권한] TRAIN_03 훈련 상대(할 일 86 T5b — PLAN-86 §3.5, 명세 v0.2 §6·§9). SpinnerBot 외형·애니로 23호 CC 규칙을 따른다.
/// 단계 판정은 순수 <see cref="TrainingSparringModel"/>(서버 소유), 게이지·벽·처치 방지 판정은 <see cref="TrainingSparringRules"/>.
/// <list type="bullet">
/// <item>23호 규칙(TwentyThreeBoss 를 읽고 같은 규칙을 여기 구현 — 그 파일은 안 건드린다): 간파 창 중 정면 간파 = 성공 → 칸 감소 + 그로기 + 취약(그로기 시작부터).
/// 취약 중 간파 스킬 = 방향 무관 넉백, 취약 중 팔라딘 Q(넉백+경직) = 밀림 + 남은 취약까지 경직. 취약·제압 중 간파 창 없음.
/// 제압 = 그로기 breakDuration, 받는 플레이어 피해 ×suppressDamageMultiplier, 끝나면 게이지 가득. 값은 전부 <see cref="counterRules"/>(23호 데이터).</item>
/// <item>보스처럼 자동 피격 반응 없음(<see cref="AutoHitReactions"/> = false) — 일반 몬스터 경직·그로기 누적·넉백을 쓰지 않는다.</item>
/// <item>벽: 넉백·밀림 틱마다 <see cref="UnitWallImpacts.ServerProbe"/>(넉백 1회 = 에피소드 1개) — 결과 콜라이더가 <see cref="designatedWalls"/> 에 있을 때만 유효.</item>
/// <item>공격: 간파 창(<see cref="MonsterCounterWindow"/> 저작값) 동안 실전과 같은 간파 예고 → 채찍(클립 OnAttackHit 이 판정). 이동 없이 제자리에서 돌아본다(앵커 = 배치 위치).
/// T31 은 예고·창을 <see cref="trainingCounterWindowSeconds"/> 까지 늘린다(PLAN Q11 A — 신호를 보고 입력해도 성공할 길이, 새 연출 없음).</item>
/// <item>훈련 보정(할 일 157 ⑦): T31~T33 칸 전이 중지, T31 간파 성공 ~ T34 취약 무기한 유지(매 틱 연장 — 23호·공용 코드 무수정, PLAN Q17)·자발 행동 없음,
/// T33 위치 복귀(취약·칸 보존, 체력만 복구), T34 빗나간 넉백 위치 복구, 처치 방지(체력 하한 1), T40 마무리 체력. 실전 전역 규칙·데이터는 건드리지 않는다.</item>
/// </list>
/// 진행기 연결(단계 알림·완료 보고·플레이어 리셋 호출)과 "새 시전" 판정(<see cref="LobbyCastWindow"/>)은 <see cref="LobbyTrainingSparring"/>. 이 상대는 서버 C# 이벤트만 낸다.
/// </summary>
[DataTableSheet("Lobby", Order = 0)]
public sealed class TrainingSparringBot : MonsterBase, IInterruptSlowMotionTarget, IInterruptWindowReplica, IOverheadHealthBarRule, IInterruptCueSink
{
    const string Tag = "[TrainingSparring]";

    [Header("23호 CC 규칙 (전투 데이터 — 값을 여기 따로 두지 않는다)")]
    [Tooltip("23호가 쓰는 보스 데이터(TwentyThree.prefab = No23_A). groggyDuration·vulnerable*·wallRebound*·counterGaugeStep·environmentGaugeStep·" +
             "breakDuration·suppressDamageMultiplier·counterFrontAngle·counterRequiresFront 를 읽는다.")]
    [SerializeField] BossDataSO counterRules;

    [Header("훈련 데이터")]
    [Tooltip("T31 간파 창 최소 길이(초) — 저작 창(MonsterCounterWindow)이 이보다 짧으면 예고·창을 이 길이로 늘린다(PLAN Q11 A). 0 = 늘리지 않음. 훈련 상대 전용, 실전 무관.")]
    [SerializeField, Min(0f), DataTableIgnore] float trainingCounterWindowSeconds = 2f;

    [Tooltip("D 마무리 체력 — 제압 중 피해 확인 + 제압 종료 뒤 체력을 이 값으로 낮추고 처치 방지를 푼다(지금 체력이 더 낮으면 그대로). 명세 §9.6: 훈련 데이터, 숫자 미고정 — 임시값.")]
    [SerializeField, Min(1)] int finishHealth = 60;

    [Header("배치 (씬 — T5c)")]
    [Tooltip("유효 충돌로 인정하는 지정 벽(Wall 레이어 콜라이더). 비면 어떤 벽도 인정하지 않는다(장식 벽 불인정).")]
    [SerializeField] Collider[] designatedWalls = Array.Empty<Collider>();
    [Tooltip("B→C 초기화 때 옮길 C 위치·방향. 비면 제자리.")]
    [SerializeField] Transform resetPoint;

    [Header("애니·연출")]
    [Tooltip("간파 창이 끝나면 재생할 채찍 상태(컨트롤러 일치). 히트·종료는 그 클립의 OnAttackHit·OnAttackEnd 이벤트.")]
    [SerializeField] string whipState = "Attack Whip R Start";
    [Tooltip("간파 성공 섬광(SpinnerBot 프리팹의 InterruptFlashVFX). 비면 연출만 빠진다.")]
    [SerializeField] EffectSocketPlayer interruptFlash;
    [Tooltip("칸 UI(머리 위 중간보스 바) 체력 게이지 색.")]
    [SerializeField] Color barFillColor = new Color(0.9f, 0.2f, 0.2f, 1f);

    // ── 복제(늦게 붙은 클라 포함) ──
    readonly NetworkVariable<TrainingSparringStage> _stage = new NetworkVariable<TrainingSparringStage>(
        TrainingSparringStage.Idle, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<float> _gauge = new NetworkVariable<float>(
        BossCounterProgress.GaugeMax, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<bool> _segmentUi = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<bool> _vulnerableVisual = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<bool> _suppressedVisual = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // 제압이 끝나는 서버 시각(NetworkManager.ServerTime) — D 남은 시간 표시. 제압 아니면 0.
    readonly NetworkVariable<double> _suppressEndsAt = new NetworkVariable<double>(
        0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // D 마무리 안내 — 모델 IsFinishing ∧ 제압 아님. 스폰·초기화·사망·제압 중엔 false(SyncModel 이 모델에서 다시 만든다).
    readonly NetworkVariable<bool> _finishPrompt = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── 서버 런타임 ──
    TrainingSparringModel _model;
    MonsterCounterWindow _counter;
    readonly Collider[] _detectBuffer = new Collider[16];
    readonly WallImpactEpisode _wallEpisode = new WallImpactEpisode();
    Unit _wallPusher;

    float _windupLeft;                 // > 0 = 간파 창(예고) 중 — 채찍 보류
    float _counterTickScale = 1f;      // 훈련 연장 창: 저작 창 ÷ 예고 길이(창이 예고 끝에 닫히게 창 시간만 느리게 흘린다)
    float _vulnerableUntil = -1f;      // Time.time, < 0 = 취약 아님
    bool _environmentConsumed;         // 이번 취약에서 벽을 이미 썼나(23호 §6.1 최초 1회)
    bool _suppressed;
    bool _vulnerableStunGroggy;        // 지금 그로기가 취약 중 기절(경직)인가
    bool _kbActive, _kbRebound;
    Vector3 _kbDir;
    float _kbLeft, _kbSpeed;
    int _kbFrame = -1;
    int _kbEpisode;                    // 넉백 에피소드 번호(1부터) — 벽·끝 사건을 그 넉백을 연 시전과 잇는다
    float _pendingConcussion;          // 밀림이 끝나면 걸 기절(특성 뇌진탕)
    bool _ccStun;                      // 기절(경직) 반응 중
    bool _resetting;
    int _interruptSuccessCount;

    /// <summary>[서버] 간파 성공(창 소비). 인자 = 공격자 clientId.</summary>
    public event Action<ulong> ServerCounterSucceeded;
    /// <summary>[서버] 취약 중 CC 를 받았다 — (넉백 에피소드(밀림 없으면 0), 종류, 실제 적용). 시전 짝짓기는 <see cref="LobbyCastWindow"/>.</summary>
    public event Action<int, TrainingSparringCrowdControl, bool> ServerCrowdControlApplied;
    /// <summary>[서버] 넉백 에피소드가 벽에 닿았다 — (에피소드, 지정 벽인가). 넉백 1회 = 최대 1회.</summary>
    public event Action<int, bool> ServerWallImpact;
    /// <summary>[서버] 넉백 에피소드가 끝났다(반동 포함) — 에피소드.</summary>
    public event Action<int> ServerKnockbackEnded;
    /// <summary>[서버] 칸 감소 — 다음 게이지(0~100).</summary>
    public event Action<float> ServerGaugeReduced;
    public event Action ServerSuppressStarted;
    public event Action ServerSuppressEnded;
    /// <summary>[서버] 제압 중 플레이어 유효 피해(받아들여진 피해 &gt; 0).</summary>
    public event Action ServerSuppressDamaged;
    /// <summary>[서버] 상대 사망. 인자 = 모델이 완료로 인정했는가(false = 처치 방지 누락).</summary>
    public event Action<bool> ServerSparringDied;
    /// <summary>[서버] 모델 단계 완료(T31 Counter·T32 VulnerableCc·T34 Wall·T40 SuppressKill, 완료마다 1회) — 진행기 보고는 LobbyTrainingSparring.</summary>
    public event Action<LobbyTutorialActivity> ServerStageCompleted;
    /// <summary>[서버] T33 위치 복귀 실행 완료 — 진행기가 플레이어 쪽 리셋(<see cref="TrainingResetOptions.PracticeReset"/>)을 이어 건다(Q10 A).</summary>
    public event Action ServerResetExecuted;

    /// <summary>[모든 피어] 상대 단계(복제).</summary>
    public TrainingSparringStage SparringStage => _stage.Value;
    /// <summary>[모든 피어] 간파 게이지(0~100, 복제).</summary>
    public float CounterGauge => _gauge.Value;
    /// <summary>[모든 피어] 칸 UI 표시(복제 — 모델 ShowsSegmentUi).</summary>
    public bool ShowsSegmentUi => _segmentUi.Value;
    public bool IsVulnerableVisual => _vulnerableVisual.Value;
    public bool IsSuppressedVisual => _suppressedVisual.Value;
    /// <summary>[모든 피어] D 마무리 안내 표시(복제) — 바 이름이 칸 대신 '마무리하세요'.</summary>
    public bool ShowsFinishPrompt => _finishPrompt.Value;
    /// <summary>[모든 피어] 총 칸(23호 데이터 — 게이지 최대 / counterGaugeStep).</summary>
    public int TotalSegments => TrainingSparringSegmentCheck.Evaluate(BossCounterProgress.GaugeMax, CounterStep).TotalSegments;
    /// <summary>[모든 피어] 남은 칸.</summary>
    public int RemainingSegments => TrainingSparringRules.SegmentsRemaining(_gauge.Value, CounterStep);
    /// <summary>[모든 피어] 남은 제압 시간(초). 제압 아니면 0.</summary>
    public double SuppressRemainingSeconds =>
        _suppressedVisual.Value && NetworkManager != null ? Math.Max(0d, _suppressEndsAt.Value - NetworkManager.ServerTime.Time) : 0d;

    /// <summary>[서버] 상태기계(테스트·진행기 조회용). 클라에서는 null.</summary>
    public TrainingSparringModel ServerModel => _model;
    public BossDataSO CounterRules => counterRules;
    public IReadOnlyList<Collider> DesignatedWalls => designatedWalls;

    // 23호 규칙과 같은 데이터 — 비어 있으면 0(로그는 스폰 때 1회).
    float CounterStep => counterRules != null ? counterRules.counterGaugeStep : 0f;
    float EnvironmentStep => counterRules != null ? counterRules.environmentGaugeStep : 0f;
    float GroggySeconds => counterRules != null ? counterRules.groggyDuration : 0f;
    float BreakSeconds => counterRules != null ? Mathf.Max(0.05f, counterRules.breakDuration) : 0.05f;
    float VulnerableSeconds => counterRules != null ? Mathf.Max(0f, counterRules.vulnerableDuration) : 0f;
    float KnockbackDistance => counterRules != null ? Mathf.Max(0f, counterRules.vulnerableKnockbackDistance) : 0f;
    float KnockbackTime => counterRules != null ? Mathf.Max(0.01f, counterRules.vulnerableKnockbackTime) : 0.01f;
    float ReboundDistance => counterRules != null ? Mathf.Max(0f, counterRules.wallReboundDistance) : 0f;
    float ReboundTime => counterRules != null ? Mathf.Max(0.01f, counterRules.wallReboundTime) : 0.01f;
    float SuppressDamageMultiplier => counterRules != null ? Mathf.Max(0f, counterRules.suppressDamageMultiplier) : 1f;

    MonsterCounterWindow Counter => _counter != null ? _counter : (_counter = GetComponent<MonsterCounterWindow>());

    bool IsVulnerable => _vulnerableUntil >= 0f && !_suppressed;
    bool CounterBlockedByState => IsVulnerable || _suppressed;
    bool AllowsGauge => _model != null && _model.AllowsGaugeTransition;
    bool HoldsVulnerable => _model != null && _model.HoldsVulnerable;
    bool CrowdControlActive => _kbActive || _ccStun;

    // 보스 규칙 — 자동 경직·그로기 누적·넉백 없음. 공격 중 회전 잠금(예고와 판정이 어긋나지 않게 — SpinnerBot 과 같다).
    protected override bool AutoHitReactions => false;
    protected override bool FaceTargetWhileAttacking => false;

    #region 수명
    /// <summary>[모든 피어] 스폰된 훈련 상대(없으면 null) — 로비 튜토리얼 파사드(<see cref="LobbyTutorialState.TrainingTarget"/>)가 읽는다.</summary>
    public static TrainingSparringBot Current { get; private set; }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Current = this;
        _vulnerableVisual.OnValueChanged += OnVulnerableVisualChanged;
        ApplyVulnerableTint(_vulnerableVisual.Value);

        if (!IsServer) return;

        if (counterRules == null)
            Debug.LogError($"{Tag} {name}: 23호 데이터(counterRules)가 비어 있다 — 칸·그로기·취약·제압 값이 0 이 된다. 프리팹에 No23_A 를 물릴 것.", this);

        TrainingSparringSegmentCheck segments = TrainingSparringSegmentCheck.Evaluate(BossCounterProgress.GaugeMax, CounterStep);
        if (!segments.IsValid)
            Debug.LogError($"{Tag} {name}: {segments.Error}", this);

        _model = new TrainingSparringModel(segments);
        _gauge.Value = BossCounterProgress.GaugeMax;
        SyncModel();
    }

    public override void OnNetworkDespawn()
    {
        _vulnerableVisual.OnValueChanged -= OnVulnerableVisualChanged;
        if (Current == this)
            Current = null;
        base.OnNetworkDespawn();
    }

    /// <summary>[서버] 씬 배치 주입(스폰한 프리팹은 씬 콜라이더를 직렬화로 못 가리킨다). null 이면 그 항목은 그대로.</summary>
    public void ServerConfigureArena(Transform cPoint, Collider[] walls)
    {
        if (!IsServer) return;
        if (cPoint != null) resetPoint = cPoint;
        if (walls != null) designatedWalls = walls;
    }

    /// <summary>
    /// [서버] 진행기 단계 시작 알림(T31~T40 Activity). 모델이 받아들이면 true. T33 ResetPosition 이면 CC 반응이 끝나는 대로 위치 복귀를 실행한다
    /// (재접속·중복 알림은 모델이 1회로 막는다). 이 프레임 이하 사건은 이 단계 완료로 세지 않는다.
    /// </summary>
    public bool ServerNotifyFlowStageStarted(LobbyTutorialActivity flowStage)
    {
        if (!IsServer || _model == null) return false;
        bool accepted = _model.OnFlowStageStarted(flowStage, Time.frameCount);
        if (!accepted)
            Edit.Log($"{Tag} {name}: 단계 알림 {flowStage} 무시(상대 단계 {_model.Stage})", this);
        TryRunReset();
        SyncModel();
        return accepted;
    }

    /// <summary>[서버] T32 — 새 시전 CC 인정(<see cref="LobbyCastWindow"/> 통과, 적용 프레임). 모델이 완료로 받으면 true.</summary>
    public bool ServerAcceptCrowdControl(int eventFrame)
    {
        if (!IsServer || _model == null) return false;
        bool accepted = _model.OnCrowdControlAccepted(eventFrame);
        SyncModel();
        return accepted;
    }

    /// <summary>[서버] T34 — 새 시전 넉백의 지정 벽 충돌 인정. 모델이 완료로 받으면 true.</summary>
    public bool ServerAcceptWallImpact(int eventFrame)
    {
        if (!IsServer || _model == null) return false;
        bool accepted = _model.OnWallAccepted(eventFrame);
        SyncModel();
        return accepted;
    }

    /// <summary>
    /// [서버] T34 — 넉백이 지정 벽 인정 없이 끝났다 → C 위치로 복구하고 재시도(취약·칸·체력 그대로). 완료 뒤·새 넉백 중이면 무시(false).
    /// </summary>
    public bool ServerRestoreAfterMiss()
    {
        if (!IsServer || _model == null || !_model.NeedsRetryAfterMiss || _kbActive || State == MonsterState.Dead) return false;
        RepositionToResetPoint();
        Debug.Log($"{Tag} T34 지정 벽 빗나감 — 위치 복구 후 재시도({(resetPoint != null ? resetPoint.name : "제자리")})", this);
        return true;
    }
    #endregion

    #region 서버 틱 — 넉백·취약 시간·제자리 판단
    protected override bool OnServerPreTick(float dt)
    {
        if (_model == null) return false;

        // 🔴 넉백을 먼저 민다 — 같은 틱의 벽 도달과 취약 만료가 겹치면 벽이 먼저다(23호 §6.5).
        bool pushing = _kbActive;
        if (_kbActive) TickKnockback(dt);

        if (_vulnerableUntil >= 0f)
        {
            if (HoldsVulnerable) _vulnerableUntil = Time.time + VulnerableSeconds;   // T31 성공 ~ T34 무기한 유지 — 만료 연장(Q17)
            else if (Time.time >= _vulnerableUntil) EndVulnerable("시간 종료");
        }
        if (HoldsVulnerable && !IsVulnerable && !_suppressed && State != MonsterState.Dead)
            StartVulnerable("훈련 취약 유지");

        TryRunReset();
        SyncModel();

        // 밀리는 동안엔 행동 결정을 멈춘다. 그로기면 그로기 타이머는 흘러야 하므로 FSM 을 돌린다(23호와 같다).
        if (pushing && State != MonsterState.Groggy && State != MonsterState.Dead) return true;

        // 대기·추격은 이 상대가 직접 판단한다(제자리 회전 → 사거리 안이면 공격). base 추격·리쉬 복귀(체력 회복)는 쓰지 않는다.
        if (State == MonsterState.Idle || State == MonsterState.Chase)
        {
            TickStandAndAttack();
            return true;
        }
        return false;
    }

    // 이동 없음 — 배치 위치(넉백으로 밀린 곳)에서 돌아보고 사거리 안이면 친다. T31 성공 뒤~T34·시작 전·완료는 가만히 있는다.
    void TickStandAndAttack()
    {
        HoldAgent();
        if (!_model.AllowsSelfAction || data == null)
        {
            if (State != MonsterState.Idle) DecideNextAfterAction();
            return;
        }

        Transform target = Target;
        if (!MonsterTargeting.IsAttackable(target)) target = FindNearestPlayer();
        if (target != null) AdoptTarget(target);
        if (Target == null) return;

        FaceTarget();
        float dist = Vector3.Distance(transform.position, Target.position);
        bool attackBlocked = status != null && status.BlocksAttack;
        if (dist <= data.attackRange && !attackBlocked && CooldownReady())
            StartAttack();
    }

    Transform FindNearestPlayer()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, data.detectionRadius, _detectBuffer, playerMask,
                                                  QueryTriggerInteraction.Collide);
        Transform nearest = null;
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider c = _detectBuffer[i];
            if (c == null || !MonsterTargeting.IsAttackable(c)) continue;
            Player player = c.GetComponentInParent<Player>();
            Transform root = player != null ? player.transform : c.transform.root;
            if (!MonsterPerceptionPolicy.WithinHeight(transform.position.y, root.position.y, data.detectionHeightTolerance)) continue;
            float sqr = (root.position - transform.position).sqrMagnitude;
            if (sqr < best) { best = sqr; nearest = root; }
        }
        return nearest;
    }

    void HoldAgent()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
    }
    #endregion

    #region 공격 — 간파 창(예고) → 채찍
    protected override void StartAttack()
    {
        base.StartAttack();   // StopAgent + FaceTarget + (데이터)슈퍼아머 + Attack

        float window = Counter != null ? Counter.WindowDuration : 0f;
        if (window <= 0f)
        {
            PlayWhipRpc();
            return;
        }

        // T31 훈련 창 연장(Q11 A) — 예고를 늘리고, 저작 창은 그 예고 끝에 닫히도록 느리게 흘린다(공용 MonsterCounterWindow 무수정).
        float windup = TrainingSparringRules.TrainingWindup(window, trainingCounterWindowSeconds, _model != null && _model.ExtendsCounterWindow);
        _counterTickScale = window / windup;

        // 창 길이만큼 공격 안전망·슈퍼아머를 늘린다(애니를 보류해도 타이머는 준다 — SpinnerBot 과 같은 규약).
        _windupLeft = windup;
        _stateTimer += windup;
        if (data.hasSuperArmorWhileAttacking && status != null)
            status.ApplyStatus(StatusEffectType.SuperArmor, AttackTime(data.attackDuration) + windup);

        // 취약·제압 중엔 간파 창이 없다(23호 §8) — 예고만 빨강으로.
        bool counterOpen = !CounterBlockedByState;
        if (meleeAttack != null)
            ShowHitboxTelegraph(meleeAttack.ColliderInfo, windup,
                                palette: counterOpen ? BossAttackConeTelegraph.Palette.Interrupt : BossAttackConeTelegraph.Palette.Attack);
        if (counterOpen)
        {
            Counter.Open();
            if (Counter.IsOpen) ServerSetCounterWindow(true, windup);
        }
    }

    protected override void HandleAttack(float dt)
    {
        if (_windupLeft > 0f)
        {
            _windupLeft -= dt;
            _stateTimer -= dt;
            HoldAgent();
            Counter?.TickAndDetectExpiry(dt * _counterTickScale);
            if (_windupLeft <= 0f)
            {
                CloseCounterWindow();
                HideAttackTelegraph();
                PlayWhipRpc();   // 히트·종료 = 클립 OnAttackHit·OnAttackEnd
            }
            return;
        }

        base.HandleAttack(dt);
    }

    void CloseCounterWindow()
    {
        _windupLeft = 0f;
        _counterTickScale = 1f;
        if (Counter != null && Counter.IsOpen) Counter.Close();
        ServerSetCounterWindow(false);
    }

    // 진행 중 공격을 끊는다(간파 성공·넉백·초기화). 상태 전이는 호출측.
    void CancelAttack()
    {
        CloseCounterWindow();
        meleeAttack?.EndHitWindow();
        HideAttackTelegraph();
        status?.RemoveStatus(StatusEffectType.SuperArmor);
    }

    // 공격 애니는 RPC 가 맡는다 — Attack 상태 기본 매핑(트리거)은 건너뛴다.
    protected override void PlayStateAnimation(MonsterState s)
    {
        if (s == MonsterState.Attack) return;
        base.PlayStateAnimation(s);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void PlayWhipRpc() => SafeCrossFade(whipState);
    #endregion

    #region 피격 — 23호 간파·취약·제압 규칙
    public override bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)
    {
        // 초기화 중 새 피해·CC 판정 제한(명세 §9.4).
        if (IsServer && _resetting) return false;

        // 🔴 조건은 base 호출 전에 스냅샷한다 — base 가 상태를 바꿀 수 있다(23호와 같다).
        bool fromPlayer = IsFromPlayer(hitContext);
        bool interrupt = attackInfo.isInterruptAttack;
        bool counter = IsServer && interrupt && _windupLeft > 0f && Counter != null && Counter.IsOpen
                       && !CounterBlockedByState && IsCounterFromFront(hitContext);
        // 취약 중 CC(할 일 157 ⑦ — 넉백/기절 태그 스킬 전반을 받는다): 간파 스킬 = 고정 거리 밀림, 넉백 세기 > 0 = 밀림(+ 경직),
        // 넉백 없이 경직만 = 기절. 23호 QualifiesPushHit 와 같은 조건(경직 > 0 조건 없음). T33 위치 복귀 중엔 새 CC 반응 없음.
        bool ccOpen = IsServer && IsVulnerable && (_model == null || _model.AcceptsCrowdControl);
        bool vulnerableHit = ccOpen && interrupt;
        bool pushHit = ccOpen && BossCrowdControlRules.QualifiesPushHit(true, interrupt, fromPlayer, attackInfo.knockbackStrength);
        bool stunHit = ccOpen && !interrupt && fromPlayer && attackInfo.knockbackStrength <= 0f && attackInfo.staggerDuration > 0f;

        if (IsServer && _suppressed && fromPlayer)
            attackInfo.damage = Mathf.Max(0, Mathf.RoundToInt(attackInfo.damage * SuppressDamageMultiplier));

        bool resolved = base.ReceiveAttack(attackInfo, hitContext);
        if (!IsServer || !resolved || State == MonsterState.Dead)
        {
            SyncModel();
            return resolved;
        }

        if (_suppressed && fromPlayer && attackInfo.damage > 0)
        {
            _model.OnSuppressDamage();
            ServerSuppressDamaged?.Invoke();
        }

        if (counter && Counter.TryConsumeInterrupt())
        {
            _interruptSuccessCount++;   // 🔴 슬로우 모션 성공 판별 — 이 ReceiveAttack 안에서 올린다(플레이어가 전후 비교)
            ulong attacker = GetAttackerClientId(hitContext);
            CombatStatsEvents.RaiseServerCounterSucceeded(this, attacker);   // 결과 화면 통계 + 플레이어 성공 장부
            EnterCounterSuccess(attacker);
            PlayInterruptFlashRpc();
        }
        else if (vulnerableHit)
        {
            bool pushed = StartKnockback(hitContext, attackInfo);
            ReportCrowdControl(TrainingSparringCrowdControl.Knockback, pushed);
        }
        else if (pushHit)
        {
            bool pushed = StartKnockback(hitContext, attackInfo);
            bool stunned = pushed && TryStartStun(attackInfo.staggerDuration, "밀림 + 경직");
            if (pushed && attackInfo.concussionDuration > 0f)
                _pendingConcussion = Mathf.Max(_pendingConcussion, attackInfo.concussionDuration);   // 밀림이 끝나면 기절(뇌진탕)
            ReportCrowdControl(TrainingSparringCrowdControl.Knockback | (stunned ? TrainingSparringCrowdControl.Stun : 0), pushed);
        }
        else if (stunHit)
        {
            ReportCrowdControl(TrainingSparringCrowdControl.Stun, TryStartStun(attackInfo.staggerDuration, "기절"));
        }
        else if (IsServer && !ccOpen && IsVulnerable && fromPlayer && (interrupt || attackInfo.knockbackStrength > 0f || attackInfo.staggerDuration > 0f))
        {
            // T33 위치 복귀 중 — 반응 없이 버린다(면역과 같다 — 인정 안 됨).
            ReportCrowdControl(attackInfo.knockbackStrength > 0f || interrupt ? TrainingSparringCrowdControl.Knockback : TrainingSparringCrowdControl.Stun, false);
        }

        SyncModel();
        return resolved;
    }

    // 취약 중 기절(경직) — 그로기로 건다. 이미 그로기(간파 성공·제압·다른 경직)면 걸지 않는다(false = 실제 적용 아님).
    bool TryStartStun(float staggerDuration, string why)
    {
        float stun = TrainingSparringRules.PushStaggerSeconds(staggerDuration, _vulnerableUntil - Time.time, HoldsVulnerable);
        if (stun <= 0f || State == MonsterState.Groggy || State == MonsterState.Dead)
            return false;

        ForceGroggy(stun);
        _vulnerableStunGroggy = true;
        _ccStun = true;
        Debug.Log($"{Tag} 취약 중 {why} {stun:0.##}초", this);
        return true;
    }

    // CC 사건 — 넉백이면 지금 에피소드(이번 적중이 연 것), 기절만이면 0. 거부된 CC(같은 틱 중복·이미 그로기·위치 복귀 중)는 applied=false.
    void ReportCrowdControl(TrainingSparringCrowdControl kinds, bool applied)
    {
        int episode = (kinds & TrainingSparringCrowdControl.Knockback) != 0 && applied ? _kbEpisode : 0;
        ServerCrowdControlApplied?.Invoke(episode, kinds, applied);
    }

    bool IsCounterFromFront(AttackHitContext hitContext)
    {
        // 무기 자식이 아니라 공격자 루트로 잰다(23호 — 무기 길이만큼 편향 방지).
        Vector3 origin = hitContext.sourceTransform != null ? hitContext.sourceTransform.root.position : hitContext.sourcePosition;
        return TrainingSparringRules.IsFromFront(transform.forward, origin - transform.position,
            counterRules != null ? counterRules.counterFrontAngle : 0f, counterRules == null || counterRules.counterRequiresFront);
    }

    // 간파 성공 — 칸 감소(T34·T40) + 그로기 + 취약, 0 이면 제압. T31 은 그로기 + 취약만 — 이 순간 T31 완료, 취약은 T34 끝까지 유지된다.
    void EnterCounterSuccess(ulong attacker)
    {
        CancelAttack();
        _model.OnCounterSucceeded();
        ServerCounterSucceeded?.Invoke(attacker);

        TrainingSparringGaugeOutcome o = TrainingSparringRules.ResolveCounter(_gauge.Value, CounterStep, GroggySeconds, BreakSeconds, AllowsGauge);
        if (o.Reduced)
        {
            _gauge.Value = o.NextGauge;
            ServerGaugeReduced?.Invoke(o.NextGauge);
        }

        if (o.IsSuppress)
        {
            EnterSuppress("간파 성공");
            return;
        }

        ForceGroggy(o.Duration);
        StartVulnerable("간파 성공");
        Debug.Log($"{Tag} 간파 성공 — 게이지 {_gauge.Value:0} · 그로기 {o.Duration:0.##}초 · 단계 {_model.Stage}", this);
    }

    void StartVulnerable(string why)
    {
        _vulnerableUntil = Time.time + VulnerableSeconds;
        _environmentConsumed = false;
        _vulnerableVisual.Value = true;
        Debug.Log($"{Tag} 취약 진입({why}) — {VulnerableSeconds:0.#}초", this);
    }

    void EndVulnerable(string why, bool releaseStun = true)
    {
        if (_vulnerableStunGroggy && releaseStun && State == MonsterState.Groggy && !_suppressed)
        {
            Debug.Log($"{Tag} 취약 종료({why}) — 경직도 해제", this);
            DecideNextAfterAction();   // 상태 훅이 경직 플래그를 정리한다
        }
        if (_vulnerableUntil < 0f) return;
        _vulnerableUntil = -1f;
        _vulnerableVisual.Value = false;
        Debug.Log($"{Tag} 취약 종료 — {why}", this);
    }

    void EnterSuppress(string reason)
    {
        if (State == MonsterState.Dead) return;
        CancelAttack();
        _gauge.Value = 0f;
        EndVulnerable("제압 진입", releaseStun: false);   // Q 경직은 제압 그로기가 이어받는다(23호)
        if (_vulnerableStunGroggy) { _vulnerableStunGroggy = false; _ccStun = false; }

        _suppressed = true;
        _suppressedVisual.Value = true;
        _suppressEndsAt.Value = NetworkManager != null ? NetworkManager.ServerTime.Time + BreakSeconds : 0d;
        ForceGroggy(BreakSeconds);
        _model.OnSuppressStarted();
        ServerSuppressStarted?.Invoke();
        Debug.Log($"{Tag} 제압 진입({reason}) — {BreakSeconds:0.#}초 · 받는 플레이어 피해 ×{SuppressDamageMultiplier:0.##} · 단계 {_model.Stage}", this);
    }

    // 그로기를 벗어나면 제압 종료 — 게이지 가득(사망이면 복원 없음). D 마무리 체력 요청이 있으면 지금 적용한다.
    void ExitSuppress(MonsterState next)
    {
        _suppressed = false;
        _suppressedVisual.Value = false;
        _suppressEndsAt.Value = 0d;
        if (next == MonsterState.Dead) return;

        _gauge.Value = BossCounterProgress.GaugeMax;
        _model.OnSuppressEnded();
        ServerSuppressEnded?.Invoke();
        if (_model.TryConsumeFinishHealthRequest())
        {
            int hp = TrainingSparringRules.FinishHealth(CurrentHealth, FinalMaxHp, finishHealth);
            SetCurrentHealthOnServer(hp);
            Debug.Log($"{Tag} 마무리 — 체력 {hp}(훈련 데이터 {finishHealth}) · 처치 방지 해제", this);
        }
        else
        {
            Debug.Log($"{Tag} 제압 종료 — 게이지 가득 · 단계 {_model.Stage}", this);
        }
    }
    #endregion

    #region 넉백·벽 (취약 넉백 / 밀림)
    // 새 넉백 에피소드를 연다. 같은 틱 두 번째 적중은 피해만(23호 §5.3) — false(실제 적용 아님).
    bool StartKnockback(AttackHitContext ctx, AttackInfo info)
    {
        if (_kbFrame == Time.frameCount) return false;
        _kbFrame = Time.frameCount;

        if (State == MonsterState.Attack)
        {
            CancelAttack();
            DecideNextAfterAction();
        }

        Vector3 from = ctx.sourceTransform != null ? ctx.sourceTransform.position : ctx.sourcePosition;
        Vector3 dir = transform.position - from;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = info.knockbackDirection;
            if (dir.sqrMagnitude < 0.0001f && ctx.sourceTransform != null) dir = ctx.sourceTransform.forward;
            dir.y = 0f;
        }
        if (dir.sqrMagnitude < 0.0001f) dir = -transform.forward;
        dir.Normalize();

        _kbActive = true;
        _kbRebound = false;
        _kbDir = dir;
        _kbLeft = KnockbackDistance;
        _kbSpeed = KnockbackDistance / KnockbackTime;
        _wallEpisode.Begin();   // 넉백 1회 = 벽 충돌 1회(재적중이면 새 에피소드 — 23호도 새 방향으로 처음부터)
        _wallPusher = ctx.sourceUnit != null ? ctx.sourceUnit
            : ctx.sourceTransform != null ? ctx.sourceTransform.GetComponentInParent<Unit>() : null;
        if (agent != null && agent.enabled && agent.isOnNavMesh) { agent.ResetPath(); agent.velocity = Vector3.zero; }

        _kbEpisode++;
        _pendingConcussion = 0f;
        Debug.Log($"{Tag} 넉백 #{_kbEpisode} — {KnockbackDistance:0.#}m / {KnockbackTime:0.##}초", this);
        return true;
    }

    void TickKnockback(float dt)
    {
        float step = Mathf.Min(_kbLeft, _kbSpeed * dt);
        Vector3 before = transform.position;
        Vector3 delta = _kbDir * step;
        if (agent != null && agent.enabled && agent.isOnNavMesh) agent.Move(delta);   // NavMesh 밖으로는 안 밀린다
        else transform.position += delta;

        if (!_kbRebound)
        {
            Vector3 moved = transform.position - before;
            moved.y = 0f;
            if (UnitWallImpacts.ServerProbe(this, _wallEpisode, _kbDir, step, moved.magnitude, _kbSpeed, _wallPusher, out UnitWallImpact impact)
                && OnWallImpact(impact))
                return;   // 반동으로 바뀌었다 — 이번 틱 남은 거리 소비 없음
        }

        _kbLeft -= step;
        if (_kbLeft <= 0.0001f) FinishKnockback();
    }

    // 반동으로 넘어갔으면 true. 지정 벽이 아니면(장식 벽) 사건만 알리고 아무것도 하지 않는다 — 에피소드는 이미 썼다(넉백 1회 = 벽 1회).
    bool OnWallImpact(UnitWallImpact impact)
    {
        if (!TrainingSparringRules.IsDesignatedWall(impact.Wall, designatedWalls))
        {
            Edit.Log($"{Tag} 지정 벽 아님 '{(impact.Wall != null ? impact.Wall.name : "?")}' — 불인정", this);
            ServerWallImpact?.Invoke(_kbEpisode, false);
            return false;
        }

        ServerWallImpact?.Invoke(_kbEpisode, true);
        if (!IsVulnerable || _environmentConsumed)
        {
            Debug.Log($"{Tag} 지정 벽 충돌 — 취약 아님 또는 이번 취약에서 이미 씀(칸 변화 없음)", this);
            return false;
        }

        _environmentConsumed = true;
        TrainingSparringGaugeOutcome o = TrainingSparringRules.ResolveWall(_gauge.Value, EnvironmentStep, AllowsGauge);
        if (o.Reduced)
        {
            _gauge.Value = o.NextGauge;
            ServerGaugeReduced?.Invoke(o.NextGauge);
        }
        // T34 완료는 이 충돌 자체(LobbyCastWindow 가 시전을 확인한 뒤 ServerAcceptWallImpact) — 칸 감소는 조건 아님.
        Debug.Log($"{Tag} 지정 벽 유효 충돌 — 칸 감소 {o.Reduced} · 게이지 {_gauge.Value:0} · 단계 {_model.Stage}", this);

        if (!HoldsVulnerable) EndVulnerable("벽 충돌");
        if (o.IsSuppress) EnterSuppress("벽 충돌");

        // 방 안쪽으로 반동(23호 §6.2). 제압으로 넘어갔어도 반동 이동은 마친다.
        _kbRebound = true;
        _kbDir = -_kbDir;
        _kbLeft = ReboundDistance;
        _kbSpeed = ReboundDistance / ReboundTime;
        return true;
    }

    // 벽에 부딪힌 넉백만 끝날 때 그로기·취약을 함께 끝낸다(23호 FinishVulnerableKnockback, 훈련 유지 중엔 안 끝냄). 벽 아님 = 각자 타이머대로.
    void FinishKnockback()
    {
        bool hitWall = _kbRebound;
        _kbActive = false;
        _kbRebound = false;
        _wallEpisode.End();
        _wallPusher = null;

        if (hitWall && !_suppressed && State != MonsterState.Dead && !HoldsVulnerable)
        {
            EndVulnerable("벽 반동 종료");
            if (State == MonsterState.Groggy) DecideNextAfterAction();
        }

        // 특성 뇌진탕 — 밀림이 끝나면 기절(23호 ConsumeConcussionAfterPush 와 같은 순서). 취약이 끝났으면 걸지 않는다.
        if (_pendingConcussion > 0f && IsVulnerable && (_model == null || _model.AcceptsCrowdControl))
            TryStartStun(_pendingConcussion, "뇌진탕 기절");
        _pendingConcussion = 0f;

        ServerKnockbackEnded?.Invoke(_kbEpisode);
    }

    // 보스 규칙 — Unit.Knockback(방향, 세기) 임펄스로는 밀리지 않는다(밀림은 위 취약 규칙만).
    protected override void OnKnockback(Vector3 direction, float strength) { }
    #endregion

    #region 상태 전이·사망·처치 방지
    protected override void OnMonsterStateChanged(MonsterState previous, MonsterState next)
    {
        base.OnMonsterStateChanged(previous, next);
        if (!IsServer || _model == null) return;

        if (previous == MonsterState.Attack && next != MonsterState.Attack)
            CloseCounterWindow();

        if (previous == MonsterState.Groggy && next != MonsterState.Groggy)
        {
            if (_suppressed) ExitSuppress(next);
            if (_vulnerableStunGroggy)
            {
                _vulnerableStunGroggy = false;
                _ccStun = false;
            }
        }

        if (next == MonsterState.Dead)
        {
            EndVulnerable("사망", releaseStun: false);
            _kbActive = false;
            _ccStun = false;
        }

        SyncModel();
    }

    protected override void OnDeath()
    {
        if (_model == null) return;
        bool completed = _model.OnDied();
        if (!completed)
            Debug.LogWarning($"{Tag} {name}: 처치 방지 중 사망 — 단계 {_model.Stage}(완료로 인정하지 않는다)", this);
        ServerSparringDied?.Invoke(completed);
        SyncModel();
    }

    // 처치 방지 — T31~T40 동안 체력 1 미만으로 내려가지 않는다(Unit.ApplyHealthDamage 의 모든 경로). T40 마무리 뒤 해제.
    protected override int HealthFloor => TrainingSparringRules.HealthFloor(_model != null && _model.PreventsDeath);

    // 초기화 중엔 공격 외 직접·비율 피해도 막는다.
    protected override bool CanApplyHealthDamage(int damage) => !_resetting && base.CanApplyHealthDamage(damage);
    #endregion

    #region T33 위치 복귀 · T34 재시도 위치 복구
    // T33(명세 §5 T33·T34): 앞 CC 반응(밀림·기절)이 끝나면 상대만 C 위치로. 취약 유지·칸(게이지) 보존·체력만 복구, 잔여 CC·속도 정리.
    // 플레이어는 옮기지 않는다(쿨·대시 리셋은 진행기 — Q10 A). 투사체는 여기서 지우지 않는다 — T34 시전 기준선이 이전 시전을 거른다.
    void TryRunReset()
    {
        if (_model == null || !_model.TryConsumeResetRequest(CrowdControlActive)) return;

        _resetting = true;
        try
        {
            _kbActive = _kbRebound = false;
            _wallEpisode.End();
            _wallPusher = null;
            _pendingConcussion = 0f;
            _ccStun = false;
            _vulnerableStunGroggy = false;
            CancelAttack();
            status?.ClearAll();   // 속도·잔여 상태이상(취약은 상대 자체 플래그라 남는다)

            RepositionToResetPoint();
            DecideNextAfterAction();

            Revive();                     // 체력만 복구
            _environmentConsumed = false; // T34 지정 벽이 칸을 실제 규칙대로 깎게(T32 중 벽 접촉으로 써 버렸을 수 있다)
            if (!IsVulnerable && !_suppressed && State != MonsterState.Dead)
                StartVulnerable("T33 위치 복귀 — 취약 유지");
            _model.OnResetExecuted();
            Debug.Log($"{Tag} T33 위치 복귀 — 위치 {(resetPoint != null ? resetPoint.name : "제자리")} · 체력 가득 · 칸 {RemainingSegments}/{TotalSegments} 보존 · 취약 유지 · 단계 {_model.Stage}", this);
        }
        finally
        {
            _resetting = false;
        }

        ServerResetExecuted?.Invoke();
    }

    // C 위치·방향으로 순간 이동하고 앵커를 옮긴다(플레이어는 옮기지 않는다). 위치가 비면 제자리.
    void RepositionToResetPoint()
    {
        if (agent != null && !agent.enabled) agent.enabled = true;
        if (resetPoint == null) return;

        Vector3 p = resetPoint.position;
        if (NavMesh.SamplePosition(p, out NavMeshHit hit, 1f, NavMesh.AllAreas)) p = hit.position;
        if (agent != null && agent.enabled) agent.Warp(p);
        else transform.position = p;
        transform.rotation = Quaternion.Euler(0f, resetPoint.eulerAngles.y, 0f);
        SetSpawnAnchor(p);
    }
    #endregion

    #region 모델 출력 → 복제·이벤트
    void SyncModel()
    {
        if (!IsServer || _model == null) return;

        while (_model.TryConsumeStageReport(out LobbyTutorialActivity completed))
        {
            Debug.Log($"{Tag} 단계 완료 — {completed}", this);
            ServerStageCompleted?.Invoke(completed);
        }

        if (_stage.Value != _model.Stage) _stage.Value = _model.Stage;
        if (_segmentUi.Value != _model.ShowsSegmentUi) _segmentUi.Value = _model.ShowsSegmentUi;
        bool finishPrompt = _model.IsFinishing && !_suppressed;
        if (_finishPrompt.Value != finishPrompt) _finishPrompt.Value = finishPrompt;
    }
    #endregion

    #region 간파 슬로우·연출
    // 실전 규칙 유지 — 중간보스·23호처럼 간파 슬로우를 건다(창 열림 ∧ 취약·제압 아님).
    bool IInterruptSlowMotionTarget.ServerIsInterruptible =>
        IsServer && _windupLeft > 0f && Counter != null && Counter.IsOpen && !CounterBlockedByState;

    int IInterruptSlowMotionTarget.ServerInterruptSuccessCount => _interruptSuccessCount;

    // 할 일 132 — 오너 클라 간파 시도 예측이 읽는 판정 창 복제값(MonsterBase). 준비 남은 시간(_windupLeft)·취약은 서버만 본다.
    bool IInterruptWindowReplica.ReplicatedWindowOpen => CounterJudgeWindowReplica;

    // 할 일 132 정정1 — 예측 대상 각도. IsCounterFromFront 와 같은 값(규칙 없음 = 정면 0°, 정면 판정 끔 = 180).
    float IInterruptWindowReplica.InterruptFrontAngle =>
        counterRules == null ? 0f : !counterRules.counterRequiresFront ? BeaverLobby.Player.Dash.InterruptTargetGeometry.NoFrontRule : counterRules.counterFrontAngle;

    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    void PlayInterruptFlashRpc()
    {
        // 간파 연출 디렉터 E1 칸(할 일 118, 피어마다 자기 값) — 켜져 있으면 디렉터가 아래 PlayCue 를 부른다. 섬광만 끈다.
        InterruptProductionDirector.PlayCue(InterruptProductionCue.TargetFlash,
            InterruptProductionEvent.LocalCue(InterruptProductionPhase.Success), this);
    }

    void IInterruptCueSink.PlayCue(InterruptProductionCue cue, in InterruptProductionEvent e)
    {
        if (cue == InterruptProductionCue.TargetFlash && interruptFlash != null)
            interruptFlash.PlayOnceOnSurface(EffectSurfaceCollider);
    }

    void OnVulnerableVisualChanged(bool previous, bool next) => ApplyVulnerableTint(next);

    // 임시 표시(23호와 같은 코드 틴트).
    void ApplyVulnerableTint(bool on)
    {
        HitFlash flash = GetComponent<HitFlash>();
        if (flash == null) return;
        if (on && counterRules != null) flash.SetBaseTint(counterRules.vulnerableTint);
        else flash.ClearBaseTint();
    }

    static bool IsFromPlayer(AttackHitContext ctx) =>
        ctx.sourceUnit is Player ||
        (ctx.sourceTransform != null && ctx.sourceTransform.GetComponentInParent<Player>() != null);
    #endregion

    #region 칸 UI — 머리 위 중간보스 바(MonsterOverheadHealthBarRule 이 이 규칙을 보고 바만 붙인다)
    string _label;
    int _labelKey = int.MinValue;

    bool IOverheadHealthBarRule.ShouldShow => _segmentUi.Value && CurrentHealth > 0;
    Color IOverheadHealthBarRule.FillColor => barFillColor;

    // 캐시 키: 칸 = 0 이상, 제압 안내 = -1 - 남은 초(-1 이하), 마무리 = 아래 고정값(초기값 int.MinValue 와도 다름).
    const int FinishLabelKey = int.MinValue + 1;

    // 바 이름 자리 = 칸 "n/m", D 제압 중엔 '제압된 상대를 공격하세요' + 남은 초, D 마무리 중엔 '마무리하세요'(칸 없음 — 임시, 명세 §9.6). 바뀔 때만 다시 만든다.
    string IOverheadHealthBarRule.Label
    {
        get
        {
            bool finisherSuppress = _stage.Value == TrainingSparringStage.SuppressKill && _suppressedVisual.Value;
            bool finishing = !finisherSuppress && _finishPrompt.Value;
            int key = finisherSuppress ? -1 - LobbyUiStrings.CountdownDisplaySeconds(SuppressRemainingSeconds)
                : finishing ? FinishLabelKey
                : RemainingSegments * 1000 + TotalSegments;
            if (key != _labelKey || _label == null)
            {
                _labelKey = key;
                _label = finisherSuppress ? LobbyUiStrings.FormatTrain03SuppressAttack(SuppressRemainingSeconds)
                    : finishing ? LobbyUiStrings.Train03Finish.Text
                    : LobbyUiStrings.FormatTrain03Segments(RemainingSegments, TotalSegments);
            }
            return _label;
        }
    }
    #endregion
}
