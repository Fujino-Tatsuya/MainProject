#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 할 일 116 — 전투 봇 대쉬 간파 규칙(오너 인스턴스). 간파 창 1개마다 <see cref="QABoss116DashPlan.Roll"/>(설정 <c>dashint</c>%, 0 = 끔)로
/// 일반 간파 대신 대쉬 간파를 쓸지·순서·누름 지연을 굴린다. 누를 시점·사거리·정면 조건은 기존 간파 규칙(<c>PressCounter</c>) 그대로이고, 누르는 방식만 바꾼다.
/// <list type="bullet">
/// <item><b>대시 → 우클릭</b>(<c>dash_rmb</c>): 보스 쪽 조준으로 대시(<see cref="PlayerDashController.TryBeginPredictedDash"/> — 회피와 같은 경로) →
///   대시 시작 후 창 × 지연 비율(게임 시각) 뒤 가상 우클릭(<see cref="PlayerInputReader.SetVirtualSkillInput"/> — HUD 칸 클릭과 같은 버퍼 기록) →
///   대시 상태의 창 판정(<c>PlayerDashState.TickDashInterruptInput</c>)이 실제 게임 경로로 처리.</item>
/// <item><b>우클릭 → 대시</b>(<c>rmb_dash</c>): 일반 간파 시전(<c>ExecuteTargetedSkill</c> — 기존과 같음) → 이 피어에서 Skill 상태가 된 뒤 창 × 지연 비율에
///   가상 대시 입력(PlayerInputReader 대시 래치 — Space 와 같은 경로) → 간파 중 대시 전환(<c>PlayerStateController.Tick</c>)이 처리.</item>
/// </list>
/// 결과 1줄 <c>[QA103] event=bot_rule rule=dash_interrupt order=… outcome=…</c> + 판 파일 <c>dashint</c> 줄(→ dash_predict.csv).
/// 예측 로그(<see cref="DashInterruptPredictLog"/>)는 봇이 켜고, 그 줄을 판 파일 <c>dp</c> 로 모은다.
/// </summary>
public sealed partial class QABoss103CombatBot
{
    private static readonly FieldInfo DashLatchField =
        typeof(PlayerInputReader).GetField("dashPressedLatched", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>우클릭 → 대시: 이 피어에서 간파 Skill 상태가 될 때까지 기다리는 상한(실시간 초 — 원격 오너는 승인 왕복 포함).</summary>
    private const double DashIntSkillWaitSeconds = 0.8;
    /// <summary>두 번째 입력 뒤 결과를 지켜보는 상한(게임 초).</summary>
    private const float DashIntObserveSeconds = 1.0f;

    private enum DashIntPhase { Idle, WaitPress, ReleaseRmb, WaitSkill, Observe }

    private System.Random _dashIntRng;
    private int _dashIntPct;
    private QABoss116Order _dashIntPlan = QABoss116Order.None;
    private double _dashIntDelayFrac;

    private DashIntPhase _dashIntPhase = DashIntPhase.Idle;
    private QABoss116Order _dashIntOrder;
    private float _dashIntPressAt, _dashIntObserveUntil, _dashIntDelayS;
    private double _dashIntSkillDeadline, _dashIntServerT;
    private int _dashIntCount;
    private readonly int[] _dashIntOrderCounts = new int[3];
    private bool _dashIntLogHooked;

    // 할 일 116-C4 — 대쉬 간파 예측 랩(QABoss116LabProbe.Active)에서만 쓰는 판단 근거 값. 랩이 아니면 비어 있고 줄도 그대로다.
    private readonly List<string> _labGate = new List<string>();
    private readonly StringBuilder _labSeen = new StringBuilder();
    private string _labSeenLast;
    private double _labFirstT = -1.0;
    private float _labFirstAt = -1f, _labDashStartAt = -1f, _labDashTotal = -1f, _labPressAt = -1f;
    private string _labDashLog = "";

    /// <summary>판 설정에서 대쉬 간파 비율을 읽고, 켜져 있으면 예측 로그를 켜고 판 파일로 모은다.</summary>
    private void InitDashInterrupt(QABoss103Config cfg)
    {
        _dashIntRng = new System.Random(QABoss103Bot.Seed(cfg.RunSeed, _slot, QABoss103Bot.Stream.DashInterrupt));
        _dashIntPct = cfg.DashInterruptPct;
        if (_dashIntPct > 0 && !_dashIntLogHooked)
        {
            DashInterruptPredictLog.Enabled = true;
            DashInterruptPredictLog.LineEmitted += OnDashPredictLine;
            _dashIntLogHooked = true;
        }
        Debug.Log($"{Tag} event=bot_dash_interrupt character={_character} slot={_slot} pct={_dashIntPct} predictLog={(DashInterruptPredictLog.Enabled ? "on" : "off")} " +
                  $"latchField={(DashLatchField != null ? "ok" : "missing")}");
    }

    private void ReleaseDashInterrupt()
    {
        if (!_dashIntLogHooked)
            return;
        DashInterruptPredictLog.LineEmitted -= OnDashPredictLine;
        DashInterruptPredictLog.Enabled = false;
        _dashIntLogHooked = false;
    }

    // 호스트 = 서버 줄(모든 플레이어), 클론 = 원격 오너 줄. 판 파일이 닫힌 뒤 줄은 버린다.
    private void OnDashPredictLine(string line)
    {
        string part = QABoss116DashPredict.ToPartLine(line);
        if (part != null)
            PartWrite(part);
    }

    /// <summary>간파 창이 열릴 때(굴림 통과 뒤) — 이 창을 대쉬 간파로 할지 굴린다.</summary>
    private void RollDashInterrupt()
    {
        _dashIntPlan = _dashIntRng != null ? QABoss116DashPlan.Roll(_dashIntRng, _dashIntPct, out _dashIntDelayFrac) : QABoss116Order.None;
    }

    /// <summary>지금까지 남긴 대쉬 간파 시도 수(<c>dashint</c> 줄 수) — 할 일 116-C4 랩 판 시도 상한.</summary>
    public int DashInterruptAttempts => _dashIntCount;

    /// <summary>대쉬 간파 진행 중 — 이번 틱 다른 이동·공격 입력을 막는다.</summary>
    private bool DashInterruptHoldsBot => _dashIntPhase != DashIntPhase.Idle;

    /// <summary>
    /// <c>PressCounter</c> 대신 대쉬 간파를 시작한다. 시작했으면 true(<paramref name="cast"/> = 첫 입력이 게임에 들어갔나).
    /// 이 창이 대쉬 간파 굴림이 아니거나 대시 충전·창 설정이 없으면 false — 호출자가 일반 간파로 누른다.
    /// </summary>
    private bool TryStartDashInterrupt(Player me, PlayerSkillController skills, Vector3 bossPos, out bool cast)
    {
        cast = false;
        QABoss116Order order = _dashIntPlan;
        _dashIntPlan = QABoss116Order.None;
        PlayerDashController dash = me.GetComponent<PlayerDashController>();
        PlayerInputReader reader = me.GetComponent<PlayerInputReader>();
        if (order == QABoss116Order.None || dash == null || reader == null || skills == null || _dashIntPhase != DashIntPhase.Idle)
            return false;
        LabBegin(me);
        float window = dash.DashInterruptWindow;
        if (!BeaverLobby.Player.Dash.DashInterruptRules.IsEnabled(window) || !dash.IsReady ||
            (order == QABoss116Order.RmbDash && DashLatchField == null))
        {
            LogDashInterrupt(order, !dash.IsReady ? "no_charge" : "unavailable", 0f);
            return false;
        }

        _dashIntOrder = order;
        _dashIntDelayS = (float)(window * _dashIntDelayFrac);
        _driver.SetAim(bossPos);
        PlayerMovement movement = me.GetComponent<PlayerMovement>();
        if (movement != null)
            movement.RotateToward(bossPos - me.transform.position, 1000f);

        if (order == QABoss116Order.DashRmb)
        {
            if (QABoss116LabProbe.Active)
                QABoss116LabProbe.BeginCapture();
            cast = dash.TryBeginPredictedDash(cancelsAction: false);
            if (QABoss116LabProbe.Active)
            {
                _labDashLog = QABoss116LabProbe.EndCapture();
                LabNoteDashStart(me);
            }
            if (!cast)
            {
                LogDashInterrupt(order, "dash_rejected", 0f);
                return true;   // 이 창은 대쉬 간파로 썼다(일반 간파로 다시 누르지 않는다 — 실패 표본)
            }
            _dashIntPressAt = Time.time + _dashIntDelayS;
            _dashIntPhase = DashIntPhase.WaitPress;
            _dashLockUntil = Time.realtimeSinceStartupAsDouble + DashLockSeconds;
            return true;
        }

        cast = skills.ExecuteTargetedSkill(PlayerSkillSlot.Interrupt, _boss, bossPos, true);
        if (!cast)
            return false;   // 일반 간파 시전이 거부됐다 — 기존 재시도 규칙에 맡긴다(다음 틱 다시 굴림 없이 일반 간파)
        _dashIntSkillDeadline = Time.realtimeSinceStartupAsDouble + DashIntSkillWaitSeconds;
        _dashIntPhase = DashIntPhase.WaitSkill;
        return true;
    }

    /// <summary>매 전투 틱 — 대쉬 간파 진행.</summary>
    private void TickDashInterrupt(Player me)
    {
        if (_dashIntPhase == DashIntPhase.Idle)
            return;
        PlayerSkillController skills = me.GetComponent<PlayerSkillController>();
        PlayerInputReader reader = me.GetComponent<PlayerInputReader>();
        PlayerDashController dash = me.GetComponent<PlayerDashController>();
        StopHold();
        _driver.ReleaseBasic();
        _input.SetMove(Vector3.zero);
        if (_boss != null)
            _driver.SetAim(_boss.transform.position);
        if (QABoss116LabProbe.Active)
            LabTick(me, skills);

        switch (_dashIntPhase)
        {
            case DashIntPhase.WaitPress when _dashIntOrder == QABoss116Order.DashRmb:
                if (me.CurrentState != PlayerActionState.Dash)
                {
                    LabResnapshot(me);   // 랩 — 대시가 끝난(끊긴) 순간 상태
                    EndDashInterrupt("dash_ended_before_press");
                    return;
                }
                if (Time.time < _dashIntPressAt)
                    return;
                _dashIntServerT = ServerNow();
                _labPressAt = Time.time;
                reader.SetVirtualSkillInput(PlayerSkillSlot.Interrupt, true);
                _dashIntPhase = DashIntPhase.ReleaseRmb;
                _dashIntObserveUntil = Time.time + DashIntObserveSeconds;
                return;

            case DashIntPhase.ReleaseRmb:
                reader.SetVirtualSkillInput(PlayerSkillSlot.Interrupt, false);
                _dashIntPhase = DashIntPhase.Observe;
                return;

            case DashIntPhase.WaitSkill:
                if (skills != null && skills.ActiveSkill is PlayerInterruptSkillBase && me.CurrentState == PlayerActionState.Skill)
                {
                    _dashIntPressAt = Time.time + _dashIntDelayS;
                    _dashIntPhase = DashIntPhase.WaitPress;
                    return;
                }
                if (Time.realtimeSinceStartupAsDouble > _dashIntSkillDeadline)
                {
                    LabResnapshot(me);   // 랩 — 상한 순간의 쿨·진행 스킬·상태
                    EndDashInterrupt("rmb_not_started");
                }
                return;

            case DashIntPhase.WaitPress: // 우클릭 → 대시
                if (skills == null || !(skills.ActiveSkill is PlayerInterruptSkillBase) || me.CurrentState != PlayerActionState.Skill)
                {
                    EndDashInterrupt("interrupt_ended_before_dash");
                    return;
                }
                if (Time.time < _dashIntPressAt)
                    return;
                if (dash == null || !dash.IsReady)
                {
                    LabResnapshot(me);   // 랩 — 첫 입력 때가 아니라 지금(누르려던 순간) 충전 값
                    EndDashInterrupt("no_charge");
                    return;
                }
                _dashIntServerT = ServerNow();
                _labPressAt = Time.time;
                LabResnapshot(me);   // 랩 — 대시를 누르는 순간의 게이트 값(no_dash 근거)
                DashLatchField.SetValue(reader, true);   // Space 한 번 — 래치·버퍼 기록은 입력 리더 규칙 그대로
                _dashLockUntil = Time.realtimeSinceStartupAsDouble + DashLockSeconds;
                _dashIntPhase = DashIntPhase.Observe;
                _dashIntObserveUntil = Time.time + DashIntObserveSeconds;
                return;

            case DashIntPhase.Observe:
                PlayerDashState dashState = DashStateOf(me);
                bool attached = dashState != null && dashState.AttachedSkill is PlayerInterruptSkillBase;
                if (attached)
                {
                    EndDashInterrupt("attached");
                    return;
                }
                // 대시가 이미 끝나 간파 Skill 로 넘어왔다(짧은 대시·원격 오너 승인 늦음)
                if (me.CurrentState == PlayerActionState.Skill && skills != null && skills.ActiveSkill is PlayerInterruptSkillBase &&
                    _dashIntOrder == QABoss116Order.DashRmb)
                {
                    EndDashInterrupt("skill_after_dash");
                    return;
                }
                if (Time.time >= _dashIntObserveUntil)
                    EndDashInterrupt(_dashIntOrder == QABoss116Order.RmbDash
                        ? (me.CurrentState == PlayerActionState.Dash ? "dash_without_transfer" : "no_dash")
                        : "no_attach");
                return;
        }
    }

    /// <summary>싸움이 멈췄다(연출·사망·단계) — 진행 중이면 끝낸다.</summary>
    private void AbortDashInterrupt(string reason)
    {
        _dashIntPlan = QABoss116Order.None;
        if (_dashIntPhase == DashIntPhase.Idle)
            return;
        if (_dashIntPhase == DashIntPhase.ReleaseRmb && _driver.Player != null)
            _driver.Player.GetComponent<PlayerInputReader>()?.SetVirtualSkillInput(PlayerSkillSlot.Interrupt, false);
        EndDashInterrupt("idle_" + QABoss103Plan.Token(reason));
    }

    private static PlayerDashState DashStateOf(Player me)
    {
        PlayerStateController sc = me.GetComponent<PlayerStateController>();
        return sc != null ? sc.ActiveDash : null;
    }

    private void EndDashInterrupt(string outcome)
    {
        _dashIntPhase = DashIntPhase.Idle;
        LogDashInterrupt(_dashIntOrder, outcome, _dashIntDelayS);
    }

    private void LogDashInterrupt(QABoss116Order order, string outcome, float delayS)
    {
        _dashIntCount++;
        _dashIntOrderCounts[(int)order]++;
        double serverT = _dashIntServerT > 0 ? _dashIntServerT : ServerNow();
        _dashIntServerT = 0;
        NetworkManager nm = NetworkManager.Singleton;
        int clientId = nm != null ? (int)nm.LocalClientId : -1;
        string token = QABoss116DashPlan.Token(order);
        Debug.Log($"{Tag} event=bot_rule character={_character} rule=dash_interrupt n={_dashIntCount} order={token} outcome={outcome} " +
                  $"delayS={delayS.ToString("F3", CultureInfo.InvariantCulture)} window={_counterWindows} clientId={clientId} serverT={F3(serverT)}");
        var kv = new List<string>
        {
            "slot", _slot.ToString(CultureInfo.InvariantCulture),
            "character", _character.ToString(),
            "clientId", clientId.ToString(CultureInfo.InvariantCulture),
            "order", token,
            "outcome", outcome,
            "delayS", delayS.ToString("F3", CultureInfo.InvariantCulture),
            "window", _counterWindows.ToString(CultureInfo.InvariantCulture),
            "serverT", F3(serverT),
        };
        if (QABoss116LabProbe.Active)
            LabAppend(kv, delayS);
        PartWrite(QABoss103PartFile.Format(QABoss116DashPredict.BotKind, kv.ToArray()));
        LabReset();
    }

    // ── 할 일 116-C4 랩 판단 근거 값(QABoss116LabProbe.Active 일 때만) ───────────

    /// <summary>대쉬 간파 첫 입력 직전 — 게이트 값·첫 입력 시각.</summary>
    private void LabBegin(Player me)
    {
        LabReset();
        if (!QABoss116LabProbe.Active)
            return;
        _labFirstT = ServerNow();
        _labFirstAt = Time.time;
        QABoss116LabProbe.Snapshot(me, _boss, _labGate);
        LabSee(me, me.GetComponent<PlayerSkillController>());
    }

    /// <summary>게이트 값을 지금 값으로 다시 잡는다(우클릭 → 대시의 대시 누름 순간).</summary>
    private void LabResnapshot(Player me)
    {
        if (!QABoss116LabProbe.Active)
            return;
        _labGate.Clear();
        QABoss116LabProbe.Snapshot(me, _boss, _labGate);
    }

    /// <summary>대시 상태가 보이면 시작 시각·전체 길이(경과 + 남음)를 적는다(한 번).</summary>
    private void LabNoteDashStart(Player me)
    {
        if (_labDashStartAt >= 0f)
            return;
        PlayerDashState ds = DashStateOf(me);
        if (ds == null)
            return;
        _labDashStartAt = Time.time - ds.Elapsed;
        _labDashTotal = ds.Elapsed + ds.Remaining;
    }

    private void LabTick(Player me, PlayerSkillController skills)
    {
        LabSee(me, skills);
        LabNoteDashStart(me);
    }

    /// <summary>지나간 (상태, 진행 스킬) 순서 — 바뀔 때만 붙인다(rmb_not_started 등 근거).</summary>
    private void LabSee(Player me, PlayerSkillController skills)
    {
        string skill = skills != null && skills.ActiveSkill != null ? skills.ActiveSkill.GetType().Name : "none";
        string now = me.CurrentState + (skill != "none" ? ":" + skill : "") + (skills != null && skills.IsBusy && skill == "none" ? ":busy" : "");
        if (now == _labSeenLast || _labSeen.Length > 160)
            return;
        if (_labSeen.Length > 0)
            _labSeen.Append('>');
        _labSeen.Append(now);
        _labSeenLast = now;
    }

    private void LabAppend(List<string> kv, float plannedS)
    {
        Player me = _driver.Player;
        float now = Time.time;
        float age = _labDashStartAt < 0f ? -1f
            : _labPressAt >= 0f && _dashIntOrder == QABoss116Order.DashRmb ? _labPressAt - _labDashStartAt
            : now - _labDashStartAt;
        QABoss116LabProbe.Add(kv, "firstT", _labFirstT >= 0.0 ? F3(_labFirstT) : "na");
        QABoss116LabProbe.Add(kv, "plannedS", QABoss116LabProbe.F3(plannedS));
        QABoss116LabProbe.Add(kv, "dashAgeS", age >= 0f ? QABoss116LabProbe.F3(age) : "na");
        QABoss116LabProbe.Add(kv, "dashTotalS", _labDashTotal >= 0f ? QABoss116LabProbe.F3(_labDashTotal) : "na");
        QABoss116LabProbe.Add(kv, "waitS", _labFirstAt >= 0f ? QABoss116LabProbe.F3(now - _labFirstAt) : "na");
        QABoss116LabProbe.Add(kv, "endState", me != null ? me.CurrentState.ToString() : "na");
        QABoss116LabProbe.Add(kv, "seen", _labSeen.ToString());
        QABoss116LabProbe.Add(kv, "dashLog", _labDashLog);
        kv.AddRange(_labGate);
    }

    private void LabReset()
    {
        _labGate.Clear();
        _labSeen.Clear();
        _labSeenLast = null;
        _labFirstT = -1.0;
        _labFirstAt = _labDashStartAt = _labDashTotal = _labPressAt = -1f;
        _labDashLog = "";
    }

    private static double ServerNow()
    {
        NetworkManager nm = NetworkManager.Singleton;
        return nm != null && nm.IsListening ? nm.ServerTime.Time : -1.0;
    }

    private string DashInterruptDiag() =>
        $"dashInt={_dashIntCount} dashRmb={_dashIntOrderCounts[(int)QABoss116Order.DashRmb]} rmbDash={_dashIntOrderCounts[(int)QABoss116Order.RmbDash]}";
}
#endif
