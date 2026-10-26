#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 할 일 103 C3 — 전투 봇의 예고 회피(패턴 피격 확률·반응 지연)와 간파(간파 성공률). 공격 쪽(C2)은 <c>QABoss103CombatBot.cs</c>.
/// <para><b>회피</b>: <see cref="QABoss103ThreatSensor"/> 가 본 위험 중 내가 범위 안(또는 대상)인 것을 위험 1건마다 1회 굴린다(<see cref="QABoss103ThreatLedger"/>).
/// 반응 지연(프로필 ms, 실시간) 뒤 회피 굴림이면: 팔라딘은 E(수호자의 의지) 쿨이 돌았고 막을 수 있는 위험이면 위험 출처를 향해 방어
/// (홀드 설정이면 남은 위험 + 0.4초 유지, 데이터 최대 유지 시간 상한), 아니면 대시(<see cref="PlayerDashController.TryBeginPredictedDash"/> — 서버 무적 토큰).
/// 대시 방향 = 위험 밖(콘 = 옆, 점프·드론 = 중심 반대, 장판 = 가장 가까운 예고 밖 칸, 예고 없는 공격 = 보스 반대), 앞 바닥이 없으면 ±45°·±90°·반대 순으로 돌린다.
/// 대시 방향 설정(<see cref="UserInputConfig.DashDirection"/>)은 이 프로세스에서만 조준 방향(Cursor)으로 바꿔 두고(PlayerPrefs 저장 없음) 끝나면 되돌린다.
/// 회피 뒤에는 그 위험이 끝날 때까지 다가가지 않고(물러나기·제자리 공격은 함), 끝나면 C2 거리 유지·공격으로 돌아간다.</para>
/// <para><b>간파</b>: <see cref="TwentyThreeBoss.CounterWindowOpen"/>(복제) 상승 엣지 = 기회(돌진·착지·레이드 릴레이·연쇄 간파·북진 압박 창이 모두 이 값을 쓴다).
/// 그 순간 사거리 안이고 우클릭 쿨이면 굴린다 — 성공 = 열림 + 반응 지연(추정 닫힘 80ms 전으로 당김), 실패 = 관측 닫힘 + 150ms.
/// 누르기 전 보스를 향한다. 정면 밖이어도 그대로 누르고 <c>front=false</c> 로 남긴다. 기회 밖에서는 우클릭을 쓰지 않는다.</para>
/// <para>로그: <c>[QA103] event=threat</c>(위험 1건 1줄) · <c>event=counter_try</c> / <c>counter_skip</c>(창 1개 1줄). 결과(실제 피격·간파 성공)는 C4 가 서버 로그로 집계한다.</para>
/// </summary>
public sealed partial class QABoss103CombatBot
{
    private const BindingFlags AnyInst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly FieldInfo DashModeField = typeof(UserInputConfig).GetField("dashDirectionMode", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo BossDataField = typeof(TwentyThreeBoss).GetField("_boss", AnyInst);

    /// <summary>대시 시작 뒤 이동·공격 입력을 멈추는 시간(초).</summary>
    private const double DashLockSeconds = 0.4;
    /// <summary>회피 뒤 위험이 끝나고도 다가가지 않는 여유(초).</summary>
    private const double AvoidTailSeconds = 0.3;
    /// <summary>대시 방향 바닥 검사 후보(도) — 원래 방향부터.</summary>
    private static readonly float[] EscapeTurns = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };
    /// <summary>사거리 앵커를 못 읽을 때 간파 사거리(보스 표면까지, m).</summary>
    private const double CounterReachFallback = 2.5;

    private sealed class PendingReaction
    {
        public QABoss103Threat Threat;
        public bool Dodge;
        public double ActAt;
    }

    private readonly QABoss103ThreatSensor _sensor = new QABoss103ThreatSensor();
    private readonly QABoss103ThreatLedger _ledger = new QABoss103ThreatLedger();
    private readonly List<PendingReaction> _reactions = new List<PendingReaction>();
    private System.Random _dodgeRng, _counterRng;

    private static bool _dashModeForced;
    private static DashDirectionMode _dashModeSaved;

    private double _dashLockUntil = -1.0;
    private double _avoidUntil = -1.0;
    private bool _guardHeld;
    private double _guardReleaseAt;

    private int _threatsSeen, _dashes, _guards, _noCharges, _blocks, _dashRejects, _guardRejects, _late;

    // 간파
    private readonly QABoss103WindowEstimate _window = new QABoss103WindowEstimate();
    private bool _windowWas;
    private double _windowOpenAt = -1.0;
    private int _counterWindows, _counterTries, _counterSkips;
    private bool _counterPending, _counterHit, _counterWaitClose;
    private double _counterPressAt, _counterPlanMs, _counterEstMs;

    private void InitEvasion(int runSeed)
    {
        _dodgeRng = new System.Random(QABoss103Bot.Seed(runSeed, _slot, QABoss103Bot.Stream.Dodge));
        _counterRng = new System.Random(QABoss103Bot.Seed(runSeed, _slot, QABoss103Bot.Stream.Counter));
        string dash = ForceCursorDash();
        Debug.Log($"{Tag} event=bot_evasion character={_character} slot={_slot} dashMode={dash} guardHold={(UserInputConfig.GuardSkillHold ? "true" : "false")} " +
                  $"sensorMissing={_sensor.Missing} frontField={(BossDataField != null ? "ok" : "missing")}");
    }

    /// <summary>대시 방향 = 조준(이 프로세스 메모리만 — 설정 저장 안 함). 봇은 이동 입력 층을 우회해 '이동 방향'이 없고 정면은 공격 중 잠긴다.</summary>
    private static string ForceCursorDash()
    {
        DashDirectionMode current = UserInputConfig.DashDirection;   // 로드 보장
        if (DashModeField == null)
            return "facing_fallback";
        if (!_dashModeForced)
        {
            _dashModeSaved = current;
            _dashModeForced = true;
        }
        DashModeField.SetValue(null, DashDirectionMode.Cursor);
        return "cursor";
    }

    private static void RestoreDashMode()
    {
        if (!_dashModeForced || DashModeField == null)
            return;
        DashModeField.SetValue(null, _dashModeSaved);
        _dashModeForced = false;
    }

    /// <summary>대시 중·방어 유지 중 — C2 이동·공격을 멈춘다.</summary>
    private bool EvasionHoldsBot(double now) => now < _dashLockUntil || _guardHeld;

    /// <summary>회피한 위험이 아직 끝나지 않았다 — 다가가지 않는다.</summary>
    private bool AvoidingApproach(double now) => now < _avoidUntil;

    // ── 회피 ───────────────────────────────────────────────────

    private void TickEvasion(Player me, Vector3 myPos, float surface, double now)
    {
        _sensor.Bind(_boss);
        _sensor.Tick(me, now);

        IReadOnlyList<QABoss103Threat> threats = _sensor.Threats;
        for (int i = 0; i < threats.Count; i++)
        {
            QABoss103Threat t = threats[i];
            if (!t.Active || _ledger.HasRolled(t.Id) || !_sensor.Contains(t, myPos, surface))
                continue;
            if (!_ledger.TryRoll(t.Id, _dodgeRng, _profile.PatternHitChance, out bool dodge))
                continue;
            _threatsSeen++;
            _reactions.Add(new PendingReaction { Threat = t, Dodge = dodge, ActAt = now + _profile.ReactionMs / 1000.0 });
        }

        for (int i = 0; i < _reactions.Count; i++)
        {
            PendingReaction r = _reactions[i];
            if (now < r.ActAt)
                continue;
            _reactions.RemoveAt(i--);
            React(me, r, now);
        }

        if (_guardHeld && now >= _guardReleaseAt)
            ReleaseGuard();
    }

    private void React(Player me, PendingReaction r, double now)
    {
        QABoss103Threat t = r.Threat;
        string note = "none";
        QABoss103DodgeAction action = QABoss103DodgeAction.None;
        string ok = "na";

        if (r.Dodge)
        {
            if (!t.Active && t.Remaining(now) <= 0.0)
            {
                note = "expired";   // 반응 지연보다 짧은 예고 — 회피 굴림이었지만 이미 끝났다
                _late++;
            }
            else if (t.Kind == QABoss103ThreatKind.Dash && _counterPending && _counterHit)
            {
                note = "counter_first";   // 돌진은 간파 창을 연다 — 간파 성공 굴림이면 피하지 않고 누른다
            }
            else
            {
                action = Dodge(me, t, now, out bool done, out note);
                ok = action == QABoss103DodgeAction.Dash || action == QABoss103DodgeAction.Guard ? (done ? "true" : "false") : "na";
            }
        }

        double lagMs = (now - t.DetectedAt) * 1000.0;
        Debug.Log($"{Tag} event=threat pattern={QABoss103Evasion.Token(t.Kind)} id={t.Id} attack={QABoss103Plan.Token(t.Attack)} bot={_character} slot={_slot} " +
                  $"tier={_profile.Tier} roll={(r.Dodge ? "dodge" : "take")} action={QABoss103Evasion.Token(action)} ok={ok} " +
                  $"reactMs={F0(_profile.ReactionMs)} actMs={F0(lagMs)} leadMs={(double.IsNaN(t.LeadSeconds) ? "na" : F0(t.LeadSeconds * 1000.0))} " +
                  $"targetedMe={(t.TargetedMe ? "true" : "false")} serverT={F3(t.ServerT)} note={note}");
        PartThreat(t, r.Dodge, action, ok, note);
    }

    /// <summary>회피 실행. 방어가 거부되면 대시로 넘어간다.</summary>
    private QABoss103DodgeAction Dodge(Player me, QABoss103Threat t, double now, out bool done, out string note)
    {
        done = false;
        note = "none";
        PlayerSkillController skills = me.GetComponent<PlayerSkillController>();
        PlayerDashController dash = me.GetComponent<PlayerDashController>();
        PlayerStateController sc = me.GetComponent<PlayerStateController>();

        bool guardUsable = _character == QABoss103Character.Paladin && QABoss103Evasion.IsGuardable(t.Kind) && skills != null &&
                           skills.GetSkill(PlayerSkillSlot.Sub) != null && skills.IsCooldownReady(PlayerSkillSlot.Sub) && !skills.IsBusy;
        bool idleMove = me.CurrentState == PlayerActionState.Idle || me.CurrentState == PlayerActionState.Move;
        bool dashAllowed = idleMove || (sc != null && sc.CurrentActionAllowsDashCancel);
        bool hasCharge = dash != null && !dash.IsOutOfCharge;

        QABoss103DodgeAction action = QABoss103Evasion.ChooseAction(true, guardUsable, hasCharge, dashAllowed);
        if (action == QABoss103DodgeAction.Guard)
        {
            if (TryGuard(me, skills, t, now))
            {
                _guards++;
                done = true;
                _avoidUntil = System.Math.Max(_avoidUntil, t.EndsAt + AvoidTailSeconds);
                return action;
            }
            _guardRejects++;
            note = "guard_rejected";
            action = QABoss103Evasion.ChooseAction(true, false, hasCharge, dashAllowed);
        }

        switch (action)
        {
            case QABoss103DodgeAction.Dash:
                Vector3 dir = SafeEscape(me.transform.position, _sensor.EscapeDirection(t, me.transform.position));
                StopHold();
                _driver.ReleaseBasic();
                _driver.SetAim(me.transform.position + dir * 3f);
                PlayerMovement movement = me.GetComponent<PlayerMovement>();
                if (movement != null)
                    movement.RotateToward(dir, 1000f);   // 대시 방향 설정을 못 바꿨을 때(정면 모드) 대비
                done = dash.TryBeginPredictedDash(cancelsAction: !idleMove);
                if (done)
                {
                    _dashes++;
                    _dashLockUntil = now + DashLockSeconds;
                    _avoidUntil = System.Math.Max(_avoidUntil, t.EndsAt + AvoidTailSeconds);
                }
                else
                {
                    _dashRejects++;
                }
                if (note == "none")
                    note = $"dir={F2(dir.x)}/{F2(dir.z)} state={me.CurrentState}";
                break;
            case QABoss103DodgeAction.NoCharge:
                _noCharges++;
                break;
            case QABoss103DodgeAction.Blocked:
                _blocks++;
                if (note == "none")
                    note = "state_" + me.CurrentState;
                break;
        }
        return action;
    }

    private bool TryGuard(Player me, PlayerSkillController skills, QABoss103Threat t, double now)
    {
        StopHold();
        _driver.ReleaseBasic();
        Vector3 src = _sensor.Source(t);
        _driver.SetAim(src);
        bool hold = UserInputConfig.GuardSkillHold;
        if (hold)
            _driver.SetSkillHeld(PlayerSkillSlot.Sub, true);
        if (!skills.ExecuteTargetedSkill(PlayerSkillSlot.Sub, null, src, true))
        {
            if (hold)
                _driver.SetSkillHeld(PlayerSkillSlot.Sub, false);
            return false;
        }
        if (hold)
        {
            float cap = skills.GetSkill(PlayerSkillSlot.Sub).Data is FirstMeleeSubSkillData d ? d.GuardHoldMaxDuration : 0f;
            _guardHeld = true;
            _guardReleaseAt = now + QABoss103Evasion.GuardHoldSeconds(t.Remaining(now), cap);
        }
        return true;
    }

    private void ReleaseGuard()
    {
        if (!_guardHeld)
            return;
        _guardHeld = false;
        _driver.SetSkillHeld(PlayerSkillSlot.Sub, false);
    }

    /// <summary>
    /// C7d 끼임 6초 단계 — 진행 방향 기준 뒤 → 왼쪽 → 오른쪽 차례(<see cref="QABoss103StuckEscalation.DashTurnDegrees"/>)로 대시해 빠져나온다.
    /// 충전이 최대 2개 이상이면 회피용 1개는 남긴다(<see cref="QABoss103StuckEscalation.CanSpendDash"/>). 대시했으면 true.
    /// </summary>
    private bool TryStuckDash(Player me, Vector3 myPos, Vector3 moveDir, double realNow)
    {
        PlayerDashController dash = me.GetComponent<PlayerDashController>();
        PlayerStateController sc = me.GetComponent<PlayerStateController>();
        bool idleMove = me.CurrentState == PlayerActionState.Idle || me.CurrentState == PlayerActionState.Move;
        bool allowed = idleMove || (sc != null && sc.CurrentActionAllowsDashCancel);
        string skip = dash == null ? "no_dash"
            : !QABoss103StuckEscalation.CanSpendDash(dash.PredictedCharge, dash.MaxCharge) ? "charge"
            : !allowed ? "state_" + me.CurrentState
            : null;
        int n = _stuckDashes + _stuckDashSkips + 1;
        Vector3 dir = Vector3.zero;
        bool done = false;
        if (skip == null)
        {
            Vector3 fwd = moveDir.sqrMagnitude > 1e-4f ? moveDir.normalized : me.transform.forward;
            fwd.y = 0f;
            dir = SafeEscape(myPos, Quaternion.AngleAxis(-QABoss103StuckEscalation.DashTurnDegrees(n), Vector3.up) * fwd);
            StopHold();
            _driver.ReleaseBasic();
            _driver.SetAim(myPos + dir * 3f);
            PlayerMovement movement = me.GetComponent<PlayerMovement>();
            if (movement != null)
                movement.RotateToward(dir, 1000f);
            done = dash.TryBeginPredictedDash(cancelsAction: !idleMove);
            if (done)
                _dashLockUntil = realNow + DashLockSeconds;
            else
                skip = "rejected";
        }
        if (done)
            _stuckDashes++;
        else
            _stuckDashSkips++;
        Debug.Log($"{Tag} event=bot_stuck_dash character={_character} n={n} ok={(done ? "true" : "false")}" + (skip != null ? $" reason={skip}" : "") +
                  $" dir={F2(dir.x)}/{F2(dir.z)} charges={(dash != null ? dash.PredictedCharge : 0)}/{(dash != null ? dash.MaxCharge : 0)} " +
                  $"stuckS={F2(_escalation.StuckSeconds(Time.timeAsDouble))} pos={Fmt(myPos)}");
        return done;
    }

    private string StuckDiag() => $"stuckDashes={_stuckDashes} stuckDashSkips={_stuckDashSkips} relocateRequests={_escalation.Relocates}";

    /// <summary>회피 방향의 앞 바닥이 없으면(낭떠러지·틈) 돌려 본다. 다 막히면 원래 방향.</summary>
    private static Vector3 SafeEscape(Vector3 foot, Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f)
            dir = Vector3.forward;
        dir.Normalize();
        foreach (float turn in EscapeTurns)
        {
            Vector3 d = Quaternion.AngleAxis(turn, Vector3.up) * dir;
            if (IsSafeStep(foot, d))
                return d;
        }
        return dir;
    }

    /// <summary>싸우지 않게 됐다(사망·연출·단계) — 방어를 놓고, 대기 중 반응은 굴림만 남기고 행동 없이 기록한다.</summary>
    private void FlushEvasion(string reason)
    {
        ReleaseGuard();
        foreach (PendingReaction r in _reactions)
        {
            PartThreat(r.Threat, r.Dodge, QABoss103DodgeAction.None, "na", "idle_" + reason);
            Debug.Log($"{Tag} event=threat pattern={QABoss103Evasion.Token(r.Threat.Kind)} id={r.Threat.Id} attack={QABoss103Plan.Token(r.Threat.Attack)} bot={_character} slot={_slot} " +
                      $"tier={_profile.Tier} roll={(r.Dodge ? "dodge" : "take")} action=none ok=na reactMs={F0(_profile.ReactionMs)} actMs=na " +
                      $"leadMs={(double.IsNaN(r.Threat.LeadSeconds) ? "na" : F0(r.Threat.LeadSeconds * 1000.0))} targetedMe={(r.Threat.TargetedMe ? "true" : "false")} " +
                      $"serverT={F3(r.Threat.ServerT)} note=idle_{QABoss103Plan.Token(reason)}");
        }
        _reactions.Clear();
        if (_counterPending)
        {
            _counterPending = false;
            _counterSkips++;
            Debug.Log($"{Tag} event=counter_skip bot={_character} slot={_slot} window={_counterWindows} reason=idle_{QABoss103Plan.Token(reason)}");
        }
        _windowWas = false;
    }

    // ── 간파 ───────────────────────────────────────────────────

    private void TickCounter(Player me, Vector3 myPos, float surface, double now)
    {
        bool open = _boss.CounterWindowOpen;
        if (open && !_windowWas)
            OnCounterOpen(me, myPos, surface, now);
        else if (!open && _windowWas)
            OnCounterClose(now);
        _windowWas = open;

        if (!_counterPending)
            return;
        if (_counterWaitClose)
        {
            if ((now - _windowOpenAt) * 1000.0 > QABoss103Counter.MissGiveUpMs)
            {
                _counterPending = false;
                _counterSkips++;
                Debug.Log($"{Tag} event=counter_skip bot={_character} slot={_slot} window={_counterWindows} reason=no_close");
            }
            return;
        }
        if (now >= _counterPressAt)
            PressCounter(me, myPos, now, open);
    }

    private void OnCounterOpen(Player me, Vector3 myPos, float surface, double now)
    {
        _counterWindows++;
        _windowOpenAt = now;
        _counterPending = false;

        PlayerSkillController skills = me.GetComponent<PlayerSkillController>();
        string skip = null;
        double reach = CounterReach(me, skills);
        if (skills == null || skills.GetSkill(PlayerSkillSlot.Interrupt) == null)
            skip = "no_skill";
        else if (!skills.IsCooldownReady(PlayerSkillSlot.Interrupt))
            skip = "cooldown";
        else if (surface > reach)
            skip = "range";
        if (skip != null)
        {
            _counterSkips++;
            Debug.Log($"{Tag} event=counter_skip bot={_character} slot={_slot} window={_counterWindows} reason={skip} surfM={F2(surface)} reachM={F2(reach)}");
            return;
        }

        _counterHit = QABoss103Counter.RollHit(_counterRng, _profile.InterruptSuccessChance);
        _counterEstMs = _window.EstimateMs();
        _counterPending = true;
        if (_counterHit)
        {
            _counterWaitClose = false;
            _counterPlanMs = QABoss103Counter.HitPressDelayMs(_profile.ReactionMs, _counterEstMs);
            _counterPressAt = now + _counterPlanMs / 1000.0;
        }
        else
        {
            _counterWaitClose = true;   // 닫힘을 본 뒤 +150ms
            _counterPlanMs = double.NaN;
        }
    }

    private void OnCounterClose(double now)
    {
        if (_windowOpenAt >= 0.0)
            _window.Add(now - _windowOpenAt);
        if (_counterPending && _counterWaitClose)
        {
            _counterWaitClose = false;
            _counterPressAt = now + QABoss103Counter.LatePressMs / 1000.0;
            _counterPlanMs = (_counterPressAt - _windowOpenAt) * 1000.0;
        }
    }

    private void PressCounter(Player me, Vector3 myPos, double now, bool openNow)
    {
        _counterPending = false;
        PlayerSkillController skills = me.GetComponent<PlayerSkillController>();
        Vector3 bossPos = _boss.transform.position;
        Vector3 to = myPos - bossPos;
        Vector3 bossFwd = _boss.transform.forward;
        bool requiresFront = true;
        float limit = QABoss103Counter.DefaultFrontAngle;
        if (BossDataField != null && BossDataField.GetValue(_boss) is BossDataSO data)
        {
            requiresFront = data.counterRequiresFront;
            limit = data.counterFrontAngle;
        }
        bool front = !requiresFront || QABoss103Counter.IsFront(bossFwd.x, bossFwd.z, to.x, to.z, limit);
        double frontDeg = QAInterruptLag88Plan.FlatAngleDeg(bossFwd.x, bossFwd.z, to.x, to.z);
        float surface = SurfaceDistance(myPos, bossPos);
        double reach = CounterReach(me, skills);

        StopHold();
        _driver.ReleaseBasic();
        _driver.SetAim(bossPos);
        PlayerMovement movement = me.GetComponent<PlayerMovement>();
        Vector3 face = bossPos - myPos;
        if (movement != null)
            movement.RotateToward(face, 1000f);
        bool ok = skills != null && skills.ExecuteTargetedSkill(PlayerSkillSlot.Interrupt, _boss, bossPos, true);
        _counterTries++;

        NetworkManager nm = NetworkManager.Singleton;
        double serverT = nm != null && nm.IsListening ? nm.ServerTime.Time : -1.0;
        Debug.Log($"{Tag} event=counter_try bot={_character} slot={_slot} tier={_profile.Tier} window={_counterWindows} roll={(_counterHit ? "hit" : "miss")} " +
                  $"pressMs={F0((now - _windowOpenAt) * 1000.0)} planMs={F0(_counterPlanMs)} reactMs={F0(_profile.ReactionMs)} windowEstMs={F0(_counterEstMs)} " +
                  $"windowSamples={_window.Count} openAtPress={(openNow ? "true" : "false")} front={(front ? "true" : "false")} frontDeg={F0(frontDeg)} frontLimit={F0(limit)} " +
                  $"surfM={F2(surface)} reachM={F2(reach)} inRange={(surface <= reach ? "true" : "false")} cast={(ok ? "true" : "false")} serverT={F3(serverT)}");
        PartCounter(_counterHit, ok, serverT);
    }

    /// <summary>
    /// 간파 사거리(보스 표면까지 수평 m) — 우클릭 판정 앵커를 어느 방향으로 돌려도 닿는 최대 거리(<see cref="InterruptOmniRangeModel"/>, 88 진단과 같은 계산).
    /// 앵커를 못 읽으면 <see cref="CounterReachFallback"/>.
    /// </summary>
    private static double CounterReach(Player me, PlayerSkillController skills)
    {
        ColliderInfo anchor = skills != null ? skills.GetSkill(PlayerSkillSlot.Interrupt)?.HitboxAnchor : null;
        if (anchor == null)
            return CounterReachFallback;
        Vector3 pivot = me.transform.position;
        switch (anchor.OverlapCollider)
        {
            case OverlapCollider.Box:
                BoxColliderInfo box = default;
                anchor.GetBoxColliderInfo(ref box);
                return InterruptOmniRangeModel.BoxReach(pivot, box.center, box.halfExtents, box.orientation);
            case OverlapCollider.Sphere:
                SphereColliderInfo sphere = default;
                anchor.GetSphereColliderInfo(ref sphere);
                return InterruptOmniRangeModel.SphereReach(pivot, sphere.center, sphere.radius);
            case OverlapCollider.Capsule:
                CapsuleColliderInfo capsule = default;
                anchor.GetCapsuleColliderInfo(ref capsule);
                return InterruptOmniRangeModel.CapsuleReach(pivot, capsule.point0, capsule.point1, capsule.radius);
        }
        return CounterReachFallback;
    }

    // ── 로그 ───────────────────────────────────────────────────

    private string EvasionDiag() =>
        $"threats={_threatsSeen} dodgeRolls={_ledger.Dodges} takeRolls={_ledger.Takes} dashes={_dashes} dashRejects={_dashRejects} guards={_guards} " +
        $"guardRejects={_guardRejects} noCharge={_noCharges} blocked={_blocks} late={_late} counterWindows={_counterWindows} counterTries={_counterTries} counterSkips={_counterSkips}";

    private static string F0(double v) => double.IsNaN(v) ? "na" : v.ToString("F0", CultureInfo.InvariantCulture);
    private static string F2(double v) => double.IsNaN(v) ? "na" : v.ToString("F2", CultureInfo.InvariantCulture);
    private static string F3(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
}
#endif
