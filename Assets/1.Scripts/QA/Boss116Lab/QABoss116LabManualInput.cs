#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 할 일 116-C5 — 대쉬 간파 예측 랩 수동 모드의 사람 입력 관측(읽기만, 게임 동작 불변). 디렉터(<see cref="QABoss116LabDirector"/>)가 수동 모드일 때만
/// 자기 오브젝트에 붙인다(씬에는 놓지 않는다).
/// <list type="bullet">
/// <item>입력 = 내 플레이어 <c>PlayerInput</c> 의 "Interrupt"(우클릭)·"Dash" 액션 원시 눌림(<c>WasPressedThisFrame</c>) — 게임 입력 리더와 같은 액션이다.</item>
/// <item>실행 순서를 플레이어보다 앞에 둔다 — 누른 프레임의 게이트 값(<see cref="QABoss116LabProbe.Snapshot"/>)이 게임이 그 입력을 처리하기 전 값이다.</item>
/// <item>판정은 순수 <see cref="QABoss116ManualTracker"/>, 줄은 <see cref="QABoss116LabManual.FormatAttempt"/> → 디렉터 판 버퍼(<c>dashint</c>).</item>
/// <item>대시를 누른 프레임부터 다음 프레임까지 게임 대시 거부 로그(<c>[Dash] 시작 불가</c>)를 잡는다(<see cref="QABoss116LabProbe.BeginCapture"/>).</item>
/// </list>
/// </summary>
[DefaultExecutionOrder(-500)]
[DisallowMultipleComponent]
public sealed class QABoss116LabManualInput : MonoBehaviour
{
    private const string Tag = QABoss116Lab.Tag;

    private QABoss116LabDirector _director;
    private readonly QABoss116ManualTracker _tracker = new QABoss116ManualTracker();
    private readonly List<string> _gateFirst = new List<string>(), _gateSecond = new List<string>();
    private Player _me;
    private InputAction _rmb, _dash;
    private bool _capturing;
    private string _dashLog = "";
    private QABoss116ManualFrame _last;
    private bool _hasLast;
    private int _attempts;

    public int Attempts => _attempts;

    public void Init(QABoss116LabDirector director) => _director = director;

    private void Update()
    {
        if (_capturing)
        {
            string log = QABoss116LabProbe.EndCapture();
            _capturing = false;
            if (log.Length > 0)
                _dashLog = log;
        }

        TwentyThreeBoss boss = _director != null ? _director.Boss : null;
        Player me = Player.LocalPlayer;
        if (_director == null || !_director.Started || boss == null || me == null)
            return;
        if (me != _me)
            Bind(me);
        if (_rmb == null && _dash == null)
            return;

        QABoss116ManualFrame f = Frame(me, boss);
        QABoss116ManualAttempt done = _tracker.Step(f);
        if (done != null)
            Emit(done, me);
        if (_tracker.TookFirst)
        {
            _gateFirst.Clear();
            _gateSecond.Clear();
            _dashLog = "";
            QABoss116LabProbe.Snapshot(me, boss, _gateFirst);
        }
        if (_tracker.TookSecond)
        {
            _gateSecond.Clear();
            QABoss116LabProbe.Snapshot(me, boss, _gateSecond);
        }
        if (f.Dash && !_capturing)
        {
            QABoss116LabProbe.BeginCapture();
            _capturing = true;
        }
        _last = f;
        _hasLast = true;
    }

    /// <summary>Play 정지 — 결과를 지켜보던 시도를 지금 값으로 끝낸다(디렉터 OnDestroy 가 부른다).</summary>
    public void Flush()
    {
        if (_capturing)
        {
            QABoss116LabProbe.EndCapture();
            _capturing = false;
        }
        if (!_hasLast)
            return;
        QABoss116ManualAttempt done = _tracker.Flush(_last);
        if (done != null)
            Emit(done, _me);
    }

    private void OnDisable()
    {
        if (_capturing)
        {
            QABoss116LabProbe.EndCapture();
            _capturing = false;
        }
    }

    private void Bind(Player me)
    {
        _me = me;
        PlayerInput input = me.GetComponent<PlayerInput>();
        _rmb = input != null && input.actions != null ? input.actions.FindAction("Interrupt") : null;
        _dash = input != null && input.actions != null ? input.actions.FindAction("Dash") : null;
        Debug.Log($"{Tag} event=manual_input player={me.name} rmb={(_rmb != null ? "ok" : "missing")} dash={(_dash != null ? "ok" : "missing")}");
    }

    private QABoss116ManualFrame Frame(Player me, TwentyThreeBoss boss)
    {
        PlayerStateController sc = me.GetComponent<PlayerStateController>();
        PlayerDashState ds = sc != null ? sc.ActiveDash : null;
        PlayerSkillController skills = me.GetComponent<PlayerSkillController>();
        PlayerDashController dash = me.GetComponent<PlayerDashController>();
        PlayerSkillBase active = skills != null ? skills.ActiveSkill : null;
        bool inDash = me.CurrentState == PlayerActionState.Dash && ds != null;
        return new QABoss116ManualFrame
        {
            T = Time.time,
            ServerT = ServerNow(),
            Rmb = _rmb != null && _rmb.WasPressedThisFrame(),
            Dash = _dash != null && _dash.WasPressedThisFrame(),
            InDash = inDash,
            DashElapsed = inDash ? ds.Elapsed : 0f,
            DashRemaining = inDash ? ds.Remaining : 0f,
            InInterrupt = me.CurrentState == PlayerActionState.Skill && active is PlayerInterruptSkillBase,
            Attached = ds != null && ds.AttachedSkill is PlayerInterruptSkillBase,
            Charge = dash != null ? dash.PredictedCharge : 0,
            CounterOpen = boss.CounterWindowOpen,
            State = me.CurrentState + (active != null ? ":" + active.GetType().Name : ""),
        };
    }

    private void Emit(QABoss116ManualAttempt a, Player me)
    {
        _attempts++;
        NetworkManager nm = NetworkManager.Singleton;
        int clientId = nm != null && nm.IsListening ? (int)nm.LocalClientId : 0;
        List<string> gate = a.GateAtFirst || _gateSecond.Count == 0 ? _gateFirst : _gateSecond;
        string endState = me != null ? me.CurrentState.ToString() : "";
        string line = QABoss116LabManual.FormatAttempt(a, _director.Character, clientId, _director.CounterWindows, endState, _dashLog, gate);
        _director.WriteManual(line);
        Debug.Log($"{Tag} event=manual_attempt n={_attempts} order={QABoss116DashPlan.Token(a.Order)} outcome={a.Outcome} " +
                  $"gapS={(a.GapS >= 0f ? a.GapS.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) : "na")} " +
                  $"counterOpen={(a.CounterOpenFirst ? "in" : "out")} cause={QABoss116Lab.Cause(a.Outcome, ParseKv(line))}");
    }

    private static Dictionary<string, string> ParseKv(string line) =>
        QABoss103PartFile.TryParse(line, out _, out Dictionary<string, string> kv) ? kv : null;

    private static double ServerNow()
    {
        NetworkManager nm = NetworkManager.Singleton;
        return nm != null && nm.IsListening ? nm.ServerTime.Time : -1.0;
    }
}
#endif
