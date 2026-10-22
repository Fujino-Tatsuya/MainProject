using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 튜토리얼 존3 스피너봇 연습 모드(할 일 15 · S9). 튜토리얼에 놓는 <see cref="SpinnerBot"/> <b>한 개체</b>의 루트에 붙인다.
///
/// 🔴 붙은 개체에만 적용된다 — SpinnerBot 은 <see cref="ISpinnerBotOverride"/> 구현체가 없으면 원래대로 돈다.
///    MonsterBase · MonsterCounterWindow 는 고치지 않았다. 바꾼 것과 이유: Docs/tech/handoff-tutorial-spinner.md.
///
/// 모드(<see cref="SpinnerPracticeMode"/>, 규칙은 <see cref="SpinnerPracticeModeModel"/>):
/// <list type="bullet">
/// <item><b>연습</b> — 체력 잠금 · 스핀만 · 창 간격 <see cref="windowInterval"/> · 돌진 피해 0(밀쳐내기만) · 스폰 반경 클램프.
///   v8 연계 연습(<see cref="SpinnerComboModel"/>): 간파 성공 → 기절 → <b>취약 유지</b>(시간 종료 없음 · FSM 정지 · 슈퍼 아머) →
///   취약 중 우클릭 간파 적중(<see cref="ISpinnerBotOverride.NotifyInterruptHit"/>)이 공격자 반대로 밀친다 → Wall 레이어 콜라이더 충돌
///   (공용 <see cref="UnitWallImpacts"/> 이벤트 — 할 일 32, 안내 화살표 지점은 <see cref="SetPracticeWall"/>) =
///   연계 1회 → 회복 → <b>그 자리에서</b> 목표 전이면 다시 준비·간파, 3회째면 완료(사용자 변경 — 원위치 복귀 없음).
///   23호 취약 밀치기와 같은 방식(공격자 반대 고정 거리)이지만 코드는 따로다(23호 것은 TwentyThreeBoss 전용).</item>
/// <item><b>전환 대기</b> — <see cref="realModeDelay"/> 초 FSM 정지(<see cref="MonsterBase.SetServerLogicSuspended"/>, 체력 잠금 유지).</item>
/// <item><b>본 모드</b> — 주입을 전부 끈다 = 원래 스피너봇.</item>
/// </list>
/// 휴면은 여기 없다 — 모든 몬스터 공통 <see cref="MonsterBase.SetDormantServer"/>(FSM 정지 + 무적·넉백/CC 무시)를
/// 디렉터(<c>TutorialMonsterZones</c> / <c>TutorialDirector.SetMonsterDormantServer</c>)가 건다. 휴면 중에는 전환 대기 타이머가 흐르지 않는다.
/// S9 순서: <c>SetMonsterDormantServer(spinner, false)</c> → <see cref="EnterPractice"/> → <see cref="EnterRealMode"/>.
/// 집계는 새로 세지 않는다 — 간파 성공은 SpinnerBot 이 원래대로 <see cref="CombatStatsEvents.ServerCounterSucceeded"/> 를 낸다.
/// 여기 이벤트는 그 통보와 <see cref="MonsterCounterWindow.IsOpen"/> 관찰로 만든 튜토리얼용 파생 신호다.
///
/// ⚠️ NetworkBehaviour 다 — 런타임 AddComponent 금지(NGO 인덱스가 갈린다). 씬 배치 개체나 프리팹 Variant 에 미리 붙인다.
/// ⚠️ 간파 슬로우모션(<see cref="IInterruptSlowMotionTarget"/>)은 일부러 구현하지 않는다(사양 5).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpinnerBot))]
public sealed class TutorialSpinnerPractice : NetworkBehaviour, ISpinnerBotOverride
{
    [Header("연습 모드")]
    [SerializeField, Min(0f)]
    [Tooltip("간파 창 사이 간격(초) — 창이 열린 시각부터 다음 창을 열 수 있는 시각까지. 원래 확률 40%·쿨 6초 대신 쓴다.")]
    float windowInterval = 9f;

    [SerializeField, Min(0f)]
    [Tooltip("스폰 지점(또는 SetPracticeAnchor) 중심 이동 반경(m). 밖으로 밀리거나 돌진해 나가지 못한다. 연습·전환 대기에만 건다.")]
    float practiceRadius = 6f;

    [SerializeField, Min(0f)]
    [Tooltip("간파를 놓친 회전 돌진이 플레이어를 밀쳐내는 반경(m, 몸 중심 수평). 한 스핀에 한 사람당 1회.")]
    float pushRadius = 2.5f;

    [SerializeField, Min(0f)]
    [Tooltip("밀쳐내기 세기(Unit.Knockback). 0 이면 밀지 않는다(피해는 어차피 0).")]
    float pushStrength = 5f;

    [Header("본 모드 전환")]
    [SerializeField, Min(0f)]
    [Tooltip("EnterRealMode 후 원래 스피너봇으로 돌아가기 전 정지 시간(초).")]
    float realModeDelay = 2f;

    [Header("연계 연습 (v8 §3.5 — 간파 → 취약 밀치기 → 벽 충돌 → 회복)")]
    [SerializeField, Min(1)]
    [Tooltip("본 전투 전 연계 성공 목표(파티 합계).")]
    int comboTarget = SpinnerComboModel.DefaultTarget;

    [SerializeField, Min(0.1f)]
    [Tooltip("우클릭 밀치기 한 번의 거리(m) — 23호 취약 밀치기(BossDataSO 3m)와 같은 출발점. 연습 위치는 1~2회로 벽에 닿는 거리.")]
    float comboPushDistance = 3f;

    [SerializeField, Min(0.05f)]
    [Tooltip("밀치기 한 번에 걸리는 시간(초).")]
    float comboPushSeconds = 0.35f;

    [SerializeField, Min(0f)]
    [Tooltip("벽 충돌로 보는 몸 가장자리 ↔ Wall 레이어 콜라이더 거리(m) — 공용 벽 충돌 판정(UnitWallImpacts)에 넘긴다.")]
    float comboContactSlack = UnitWallImpacts.DefaultContactSlack;

    [SerializeField, Min(0f)]
    [Tooltip("밀치기가 NavMesh 가장자리에 막혔을 때 벽 충돌로 보는 거리(m) — NavMesh 는 벽에서 에이전트 반경만큼 안쪽에서 끝난다.")]
    float comboBlockedContactSlack = UnitWallImpacts.DefaultBlockedSlack;

    [SerializeField, Min(0f)]
    [Tooltip("벽 충돌 뒤 회복 시간(초) — 끝나면 그 자리에서 다시 준비·간파(3회째면 완료).")]
    float comboRecoverSeconds = 0.6f;

    [SerializeField, Min(0f)]
    [Tooltip("회복 끝 뒤 다음 간파 창까지 최소 대기(초) — 밀치기에 쓴 우클릭이 다시 준비될 틈. 원래 창 간격(9초)이 더 늦으면 그쪽.")]
    float comboReadyDelay = 4f;

    readonly NetworkVariable<SpinnerPracticeMode> _mode = new NetworkVariable<SpinnerPracticeMode>(
        SpinnerPracticeMode.Practice,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // 연계 단계·유효 벽(표시용 — 안내 UI 가 목표·강조를 고른다). 서버 쓰기.
    readonly NetworkVariable<SpinnerComboPhase> _comboPhase = new NetworkVariable<SpinnerComboPhase>(
        SpinnerComboPhase.Ready, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<Vector3> _wallPoint = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<bool> _hasWall = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    SpinnerBot _bot;
    MonsterCounterWindow _counter;
    NavMeshAgent _agent;

    MonsterStatusEffect _status;

    SpinnerPracticeModeModel _model;
    SpinnerPracticeWindowSchedule _schedule;
    SpinnerComboModel _combo;
    readonly SpinnerPracticeWindowTracker _tracker = new SpinnerPracticeWindowTracker();
    readonly HashSet<Player> _pushedThisSpin = new HashSet<Player>();

    Vector3 _anchor;
    // 할 일 32 — 밀치기 한 번 = 벽 충돌 에피소드 하나(UnitWallImpacts 가 1회 보장). 민 쪽 = 우클릭 시전자.
    readonly WallImpactEpisode _wallEpisode = new WallImpactEpisode();
    Unit _pusher;
    bool _wallImpactThisStep;
    bool? _appliedSuspended;    // 마지막으로 MonsterBase 에 건 FSM 정지 값(null = 아직 안 걺)
    bool _appliedSuperArmor;
    bool _subscribed;

    #region 공개 상태 (전 피어)
    /// <summary>현재 모드. 전 피어에 복제된다.</summary>
    public SpinnerPracticeMode Mode => _mode.Value;

    /// <summary>
    /// 체력 바 회색·잠금 표시용 — 본 모드 전(연습·전환 대기)이면 true. 전 피어에서 읽는다.
    /// 표시는 튜토리얼 안내 UI 몫이다(여기서 UI 를 만들지 않는다).
    /// </summary>
    public bool IsHealthLocked => _mode.Value != SpinnerPracticeMode.Real;

    public SpinnerBot Bot => _bot;

    /// <summary>연계 연습 단계(전 피어 복제) — 준비·간파 / 기절·취약 / 회복 / 완료.</summary>
    public SpinnerComboPhase ComboPhase => _comboPhase.Value;

    /// <summary>유효 벽(연습용 지정 벽) 표시 지점(전 피어). 아직 정하지 않았으면 false.</summary>
    public bool TryGetWallPoint(out Vector3 point)
    {
        point = _wallPoint.Value;
        return _hasWall.Value;
    }
    #endregion

    #region 서버 상태 · 이벤트
    /// <summary>[서버] 연속 놓침 수(성공 시 0). 연습 모드 진입 때도 0 으로 돌아간다.</summary>
    public int ConsecutiveMisses => _tracker.ConsecutiveMisses;

    /// <summary>[서버] 간파 창이 열렸다.</summary>
    public event Action<TutorialSpinnerPractice> ServerWindowOpened;

    /// <summary>[서버] 간파 창이 닫혔다. 두 번째 인자 = 성공으로 닫혔는가.</summary>
    public event Action<TutorialSpinnerPractice, bool> ServerWindowClosed;

    /// <summary>[서버] 성공 없이 닫혔다(놓침). 두 번째 인자 = 연속 놓침 수(이번 포함).</summary>
    public event Action<TutorialSpinnerPractice, int> ServerWindowMissed;

    /// <summary>[서버] 모드가 바뀌었다(전환 대기 → 본 모드 자동 진입 포함).</summary>
    public event Action<TutorialSpinnerPractice, SpinnerPracticeMode> ServerModeChanged;

    /// <summary>
    /// [서버] 연습 중 간파 성공을 연계 시작으로 받아들였다(준비·간파 단계에서만 — 취약·회복 중 성공은 없다). 인자 = 간파한 clientId.
    /// 존3 흐름의 파티 우클릭 쿨 초기화는 여기가 아니라 <see cref="PlayerInterruptSkillBase.ServerInterruptSucceeded"/> 에서 한다.
    /// </summary>
    public event Action<TutorialSpinnerPractice, ulong> ServerComboCounterAccepted;

    /// <summary>[서버] 연계 진행 일(벽 충돌·밀치기 빗나감·다시 준비·완료). 세 번째 인자 = 지금까지 연계 성공 수.</summary>
    public event Action<TutorialSpinnerPractice, SpinnerComboEvent, int> ServerComboEvent;

    /// <summary>[서버] 연계 성공 수(벽 충돌 — 사망·이탈로 줄지 않는다).</summary>
    public int ComboSuccesses => _combo != null ? _combo.Successes : 0;

    /// <summary>연계 목표(인스펙터 — 디렉터 Zone3Interrupts 목표와 같아야 한다).</summary>
    public int ComboTarget => _combo != null ? _combo.Target : comboTarget;
    #endregion

    #region 공개 API (서버)
    /// <summary>
    /// [서버] 연습 모드로 들어간다. 창 스케줄·연속 놓침을 처음으로 돌린다 — 첫 창은 바로 열 수 있다.
    /// 휴면은 풀지 않는다 — 먼저 디렉터가 base 휴면을 푼다(휴면 중이면 경고만 남기고 모드는 바꾼다).
    /// </summary>
    public void EnterPractice()
    {
        if (!RequireServer(nameof(EnterPractice))) return;
        WarnIfDormant(nameof(EnterPractice));
        _model.EnterPractice();
        _schedule.Reset();
        _tracker.ResetCounts();
        ApplyMode();
    }

    /// <summary>[서버] 본 모드 전환 — <see cref="realModeDelay"/> 초 정지 후 원래 스피너봇. 이미 전환 중·본 모드면 무시.</summary>
    public void EnterRealMode()
    {
        if (!RequireServer(nameof(EnterRealMode))) return;
        WarnIfDormant(nameof(EnterRealMode));
        _combo.Finish();   // 교육용 취약 유지·정지·슈퍼 아머를 모두 끈다(v8 Z3-9)
        _model.EnterRealMode();
        ApplyMode();
    }

    /// <summary>
    /// [서버] 안내 화살표가 가리킬 벽 면 위의 점(표시 전용). 존3 흐름이 연습 위치에서 가장 가까운 Wall 레이어 콜라이더 면으로 정한다.
    /// 연계 성공 판정과는 무관하다 — 판정은 어느 Wall 콜라이더든 밀치기 중 벽 충돌 이벤트(할 일 32).
    /// </summary>
    public void SetPracticeWall(Vector3 pointOnWall)
    {
        if (!RequireServer(nameof(SetPracticeWall))) return;
        _wallPoint.Value = pointOnWall;
        _hasWall.Value = true;
    }

    /// <summary>
    /// [서버] 연습 위치(원위치)를 옮기고 지금 그 자리로 보낸다 — 휴면 중(존3 진입 전)에 부른다. NavMesh 위 가장 가까운 점(2m 안)으로 맞춘다.
    /// 못 찾으면 false(위치는 그대로).
    /// </summary>
    public bool RelocateAnchor(Vector3 worldPosition)
    {
        if (!RequireServer(nameof(RelocateAnchor))) return false;
        if (!NavMesh.SamplePosition(worldPosition, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            Debug.LogWarning($"{name}: 연습 위치 {worldPosition} 근처에 NavMesh 가 없다 — 스폰 위치를 원위치로 쓴다.", this);
            return false;
        }
        _anchor = hit.position;
        MoveTo(hit.position, warp: true);
        return true;
    }

    /// <summary>[서버] 이동 반경의 중심을 바꾼다. 기본은 서버 스폰 시 위치.</summary>
    public void SetPracticeAnchor(Vector3 worldPosition)
    {
        if (!RequireServer(nameof(SetPracticeAnchor))) return;
        _anchor = worldPosition;
    }

    /// <summary>[서버] 이동 반경(m)을 바꾼다 — 존3 흐름이 출구 배리어(G6) 위치에서 계산해 넣는다. 0 = 기준점 고정.</summary>
    public void SetPracticeRadius(float radius)
    {
        if (!RequireServer(nameof(SetPracticeRadius))) return;
        practiceRadius = Mathf.Max(0f, radius);
    }

    /// <summary>[서버] 지금 이동 반경 중심(기본 = 서버 스폰 위치).</summary>
    public Vector3 PracticeAnchor => _anchor;

    public float PracticeRadius => practiceRadius;
    #endregion

    void Awake()
    {
        _bot = GetComponent<SpinnerBot>();
        _counter = GetComponent<MonsterCounterWindow>();
        _agent = GetComponent<NavMeshAgent>();
        _status = GetComponent<MonsterStatusEffect>();
        _model = new SpinnerPracticeModeModel(realModeDelay);
        _schedule = new SpinnerPracticeWindowSchedule(windowInterval);
        // 기절 = 원래 간파 그로기(MonsterCounterWindow.groggyDuration)보다 살짝 짧게 — 그로기가 끝나 FSM 이 추격을 고르기 전에 세운다.
        float stun = _counter != null ? Mathf.Max(0f, _counter.GroggyDuration - 0.1f) : 0.9f;
        _combo = new SpinnerComboModel(comboTarget, stun, comboRecoverSeconds, comboPushDistance, comboPushSeconds);
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        _anchor = transform.position;
        _mode.Value = _model.Mode;
        // 🔴 FSM 정지는 여기서 걸지 않는다 — MonsterBase 초기화 순서와 엇갈리지 않게 첫 Update 에서 건다.
        //    휴면은 디렉터가 base 에 건다(이 컴포넌트는 연습 모드로 시작). 연습 공격은 피해 0 이라 휴면 전 틈에도 안전하다.

        CombatStatsEvents.ServerCounterSucceeded += OnServerCounterSucceeded;
        _bot.ServerWallImpacted += OnServerWallImpacted;
        _subscribed = true;

        if (_counter == null)
            Debug.LogWarning($"{name}: MonsterCounterWindow 가 없다 — 연습 모드에서 스피너봇이 공격하지 않는다.", this);
    }

    public override void OnNetworkDespawn()
    {
        Unsubscribe();
    }

    public override void OnDestroy()
    {
        Unsubscribe();
        base.OnDestroy();
    }

    void Unsubscribe()
    {
        if (!_subscribed) return;
        CombatStatsEvents.ServerCounterSucceeded -= OnServerCounterSucceeded;
        if (_bot != null) _bot.ServerWallImpacted -= OnServerWallImpacted;
        _subscribed = false;
    }

    void Update()
    {
        if (!IsServer || !IsSpawned) return;

        // base 휴면 중에는 전환 대기 타이머를 세우고 FSM 정지는 base 에 맡긴다(이중으로 걸지 않는다).
        if (!_bot.IsDormant)
        {
            _model.Tick(Time.deltaTime);
            if (_model.Mode == SpinnerPracticeMode.Practice) TickCombo(Time.deltaTime);
        }
        ApplyMode();

        if (_counter != null) Emit(_tracker.Observe(_counter.IsOpen));
    }

    // ─── 연계 연습(v8 §3.5) — 밀치기 이동은 FSM 을 세운 채 여기서 직접 한다 ───────────────────

    void TickCombo(float dt)
    {
        SpinnerComboEvent events = _combo.Tick(dt);

        if (_combo.IsPushing)
        {
            float step = _combo.PushStep(dt);
            var dir = new Vector3(_combo.PushDirX, 0f, _combo.PushDirZ);
            Vector3 before = transform.position;
            MoveTo(before + dir * step, warp: false);
            Vector3 after = transform.position;
            float moved = new Vector2(after.x - before.x, after.z - before.z).magnitude;

            // 할 일 32 — 공용 벽 충돌 판정(실제 Wall 레이어 콜라이더). 충돌이면 OnServerWallImpacted 가 같은 호출 안에서 표시한다.
            _wallImpactThisStep = false;
            UnitWallImpacts.ServerProbe(_bot, _wallEpisode, dir, step, moved, _combo.PushSpeed, _pusher, out _,
                                        comboContactSlack, comboBlockedContactSlack);
            events |= _combo.ReportPushMoved(step, moved, _wallImpactThisStep);
            if (!_combo.IsPushing) _wallEpisode.End();
        }

        if ((events & SpinnerComboEvent.ReadyAgain) != 0)
        {
            _schedule.DelayUntil(Time.time + comboReadyDelay);
            _tracker.ResetCounts();
        }

        if (events != SpinnerComboEvent.None)
        {
            Debug.Log($"[Tutorial/스피너봇] 연계 {events} — 단계 {_combo.Phase} · 성공 {_combo.Successes}/{_combo.Target}", this);
            ServerComboEvent?.Invoke(this, events, _combo.Successes);
        }
    }

    // 에이전트가 켜져 있고 NavMesh 위면 에이전트로(NavMesh 밖·장애물 카브로는 못 나간다), 아니면 transform 으로.
    void MoveTo(Vector3 target, bool warp)
    {
        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            if (warp) _agent.Warp(target);
            else _agent.Move(target - transform.position);
        }
        else
        {
            transform.position = target;
        }
    }

    // 에이전트·넉백이 이번 프레임 위치를 정한 뒤 범위 밖이면 되돌린다.
    void LateUpdate()
    {
        if (!IsServer || !IsSpawned || !_model.ClampsPosition) return;
        // 연계 취약·회복 중에는 반경을 걸지 않는다 — 벽까지 움직여야 한다(배리어·NavMesh 가 막는다).
        // 다시 준비·간파가 되면 반경이 다시 걸린다 — 벽 앞(연습 위치에서 약 4m)은 보통 반경 안이라 그 자리에 남는다.
        if (_model.Mode == SpinnerPracticeMode.Practice && !_combo.ClampsToPracticeRadius) return;

        Vector3 position = transform.position;
        Vector3 clamped = SpinnerPracticeArea.ClampHorizontal(position, _anchor, practiceRadius);
        if ((clamped - position).sqrMagnitude < 0.000001f) return;

        // 넉백 중에는 에이전트가 꺼져 있다(MonsterBase 가 transform 을 직접 민다) → transform 을 그대로 옮긴다.
        if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.Warp(clamped);
        else transform.position = clamped;
    }

    // 모델 → 복제 모드 · 전환 대기 FSM 정지. 바뀐 것만 건다.
    void ApplyMode()
    {
        if (!IsSpawned) return;

        SpinnerPracticeMode mode = _model.Mode;
        if (_mode.Value != mode)
        {
            _mode.Value = mode;
            ServerModeChanged?.Invoke(this, mode);
        }

        bool practicing = mode == SpinnerPracticeMode.Practice;
        if (_comboPhase.Value != _combo.Phase) _comboPhase.Value = _combo.Phase;

        // 전환 대기 + 연계의 취약 유지·회복 = 스스로 이동·추격·공격하지 않는다(밀치기 이동은 TickCombo 가 직접).
        bool suspend = _model.SuspendsServerLogic || (practicing && _combo.SuspendsFsm);
        if (_appliedSuspended != suspend)
        {
            _bot.SetServerLogicSuspended(suspend);
            _appliedSuspended = suspend;
        }

        // 간파 성공 뒤 회복 끝까지 외부 넉백(Q 넉백 등) 무시 — 우클릭 밀치기만 움직인다. 본 전투 전환 때 푼다.
        bool armor = practicing && _combo.BlocksExternalKnockback;
        if (_status != null && _appliedSuperArmor != armor)
        {
            if (armor) _status.ApplyStatus(StatusEffectType.SuperArmor, 0f);
            else _status.RemoveStatus(StatusEffectType.SuperArmor);
            _appliedSuperArmor = armor;
        }
    }

    void WarnIfDormant(string api)
    {
        if (_bot.IsDormant)
            Debug.LogWarning($"{name}: {api} — 아직 휴면이다. 디렉터 SetMonsterDormantServer(false) 를 먼저 부를 것(휴면 동안은 움직이지 않는다).", this);
    }

    void OnServerCounterSucceeded(Unit target, ulong attackerClientId)
    {
        if (target != _bot) return;
        Emit(_tracker.NotifySucceeded());

        // 연계 시작 — 준비·간파 단계에서만. 이 직후 SpinnerBot 이 원래대로 공격 취소 + 그로기(=기절)에 들어간다.
        if (_model.Mode == SpinnerPracticeMode.Practice && _combo.NotifyCounterSucceeded())
        {
            Debug.Log($"[Tutorial/스피너봇] 간파 성공(client {attackerClientId}) → 취약 — 벽 충돌까지 유지.", this);
            ServerComboCounterAccepted?.Invoke(this, attackerClientId);
        }
    }

    // 할 일 32 — 이 스피너봇의 공용 벽 충돌 이벤트. 연계는 우클릭 밀치기 중인 것만 센다(Q 넉백 등 다른 외부 힘은 무시).
    void OnServerWallImpacted(UnitWallImpact impact)
    {
        if (_model != null && _model.Mode == SpinnerPracticeMode.Practice && _combo.IsPushing)
            _wallImpactThisStep = true;
    }

    // 취약 중 우클릭 간파 적중 = 밀치기(공격자 → 스피너봇 방향). 준비·회복 중에는 무시(외부 밀치기 없음).
    void ISpinnerBotOverride.NotifyInterruptHit(ulong attackerClientId)
    {
        if (_model == null || _model.Mode != SpinnerPracticeMode.Practice || !_combo.IsVulnerable || NetworkManager == null) return;
        if (!NetworkManager.ConnectedClients.TryGetValue(attackerClientId, out NetworkClient client) || client.PlayerObject == null) return;

        Vector3 away = transform.position - client.PlayerObject.transform.position;
        if (!_combo.TryBeginPush(away.x, away.z)) return;

        _wallEpisode.Begin();   // 새 밀치기 = 벽 충돌 1회 다시
        _pusher = client.PlayerObject.GetComponent<Unit>();
        Debug.Log($"[Tutorial/스피너봇] 취약 밀치기(client {attackerClientId}) — 방향 ({_combo.PushDirX:0.##}, {_combo.PushDirZ:0.##}).", this);
    }

    void Emit(SpinnerWindowEvent events)
    {
        if (events == SpinnerWindowEvent.None) return;

        if ((events & SpinnerWindowEvent.Opened) != 0)
            ServerWindowOpened?.Invoke(this);
        if ((events & SpinnerWindowEvent.Succeeded) != 0)
            ServerWindowClosed?.Invoke(this, true);
        if ((events & SpinnerWindowEvent.Missed) != 0)
        {
            ServerWindowClosed?.Invoke(this, false);
            ServerWindowMissed?.Invoke(this, _tracker.ConsecutiveMisses);
        }
    }

    bool RequireServer(string api)
    {
        if (IsServer && _model != null) return true;
        Debug.LogWarning($"{name}: TutorialSpinnerPractice.{api} 는 서버(호스트)에서 스폰 후에만 부른다 — 무시.", this);
        return false;
    }

    #region ISpinnerBotOverride (SpinnerBot 이 서버에서 묻는다)
    bool ISpinnerBotOverride.OverridesAttack => _model != null && _model.OverridesAttack;

    bool ISpinnerBotOverride.BlocksHealthDamage => _model != null && _model.BlocksHealthDamage;

    bool ISpinnerBotOverride.TryBeginSpin()
    {
        if (_model == null || !_model.AllowsSpin || !_combo.AllowsSpin) return false;
        if (!_schedule.TryBegin(Time.time)) return false;

        _pushedThisSpin.Clear();
        return true;
    }

    Vector3 ISpinnerBotOverride.ClampDashTarget(Vector3 target) =>
        SpinnerPracticeArea.ClampSegmentEnd(transform.position, target, _anchor, practiceRadius);

    // 간파를 놓친 돌진 — 피해 없이 반경 안 플레이어를 한 번씩 밀어낸다(서버 권한 Unit.Knockback).
    void ISpinnerBotOverride.TickHarmlessDash()
    {
        if (pushStrength <= 0f || pushRadius <= 0f || NetworkManager == null) return;

        Vector3 center = transform.position;
        float radiusSq = pushRadius * pushRadius;
        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null || !playerObject.TryGetComponent(out Player player)) continue;
            if (player.CurrentHealth <= 0) continue;

            Vector3 away = player.transform.position - center;
            away.y = 0f;
            if (away.sqrMagnitude > radiusSq) continue;
            if (!_pushedThisSpin.Add(player)) continue;

            if (away.sqrMagnitude < 0.0001f)
            {
                away = transform.forward;
                away.y = 0f;
            }
            player.Knockback(away.normalized, pushStrength);
        }
    }
    #endregion
}
