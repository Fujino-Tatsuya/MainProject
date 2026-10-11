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
/// <item><b>연습</b> — 체력 잠금 · 스핀만 · 창 간격 <see cref="windowInterval"/> · 돌진 피해 0(밀쳐내기만) · 스폰 반경 클램프.</item>
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

    readonly NetworkVariable<SpinnerPracticeMode> _mode = new NetworkVariable<SpinnerPracticeMode>(
        SpinnerPracticeMode.Practice,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    SpinnerBot _bot;
    MonsterCounterWindow _counter;
    NavMeshAgent _agent;

    SpinnerPracticeModeModel _model;
    SpinnerPracticeWindowSchedule _schedule;
    readonly SpinnerPracticeWindowTracker _tracker = new SpinnerPracticeWindowTracker();
    readonly HashSet<Player> _pushedThisSpin = new HashSet<Player>();

    Vector3 _anchor;
    bool? _appliedSuspended;    // 마지막으로 MonsterBase 에 건 FSM 정지 값(null = 아직 안 걺)
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
        _model.EnterRealMode();
        ApplyMode();
    }

    /// <summary>[서버] 이동 반경의 중심을 바꾼다. 기본은 서버 스폰 시 위치.</summary>
    public void SetPracticeAnchor(Vector3 worldPosition)
    {
        if (!RequireServer(nameof(SetPracticeAnchor))) return;
        _anchor = worldPosition;
    }
    #endregion

    void Awake()
    {
        _bot = GetComponent<SpinnerBot>();
        _counter = GetComponent<MonsterCounterWindow>();
        _agent = GetComponent<NavMeshAgent>();
        _model = new SpinnerPracticeModeModel(realModeDelay);
        _schedule = new SpinnerPracticeWindowSchedule(windowInterval);
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        _anchor = transform.position;
        _mode.Value = _model.Mode;
        // 🔴 FSM 정지는 여기서 걸지 않는다 — MonsterBase 초기화 순서와 엇갈리지 않게 첫 Update 에서 건다.
        //    휴면은 디렉터가 base 에 건다(이 컴포넌트는 연습 모드로 시작). 연습 공격은 피해 0 이라 휴면 전 틈에도 안전하다.

        CombatStatsEvents.ServerCounterSucceeded += OnServerCounterSucceeded;
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
        _subscribed = false;
    }

    void Update()
    {
        if (!IsServer || !IsSpawned) return;

        // base 휴면 중에는 전환 대기 타이머를 세우고 FSM 정지는 base 에 맡긴다(이중으로 걸지 않는다).
        if (!_bot.IsDormant) _model.Tick(Time.deltaTime);
        ApplyMode();

        if (_counter != null) Emit(_tracker.Observe(_counter.IsOpen));
    }

    // 에이전트·넉백이 이번 프레임 위치를 정한 뒤 범위 밖이면 되돌린다.
    void LateUpdate()
    {
        if (!IsServer || !IsSpawned || !_model.ClampsPosition) return;

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

        bool suspend = _model.SuspendsServerLogic;
        if (_appliedSuspended != suspend)
        {
            _bot.SetServerLogicSuspended(suspend);
            _appliedSuspended = suspend;
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
        if (_model == null || !_model.AllowsSpin) return false;
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
