#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 할 일 103 C3 — 전투 봇의 예고 회피(패턴 피격 확률·반응 지연)와 간파(간파 성공률). 공격 쪽(C2)은 <c>QABoss103CombatBot.cs</c>.
/// <para><b>회피</b>: <see cref="QABoss103ThreatSensor"/> 가 본 위험 중 내가 범위 안(또는 대상)인 것을 위험 1건마다 1회 굴린다(<see cref="QABoss103ThreatLedger"/>).
/// C7e — 굴림 때 위험별 회피 마감(<see cref="QABoss103HitTiming.DeadlineDelay"/> — 잡기 = 예고 끝, 그 밖 = 첫 피해)을 정한다.
/// 반응 지연(프로필 ms, 실시간)이 마감을 넘기면 expired. 아니면 그 위험 동안 새 평타·스킬을 시작하지 않고(팔라딘 Q 홀드는 놓는다)
/// 범위 밖으로 걷는다(콘 = 옆, 점프·드론·오라 = 중심 반대, 장판 = 가장 가까운 예고 밖 칸, 예고 없는 공격 = 보스 반대).
/// 대시 시각 = 마감 − 무적 절반(<see cref="QABoss103HitTiming.DashStartAt"/>)에 아직 범위 안이면 대시(<see cref="PlayerDashController.TryBeginPredictedDash"/> — 서버 무적 0.25초),
/// 이미 밖이면 <c>walk</c>(충전 아낌 — 충전 1개·재충전 2초). 팔라딘은 E 쿨이 돌았고 막을 수 있으면 걷지 않고 출처를 보고 서서
/// 마감 − 선딜 0.225 − 0.1초에 방어(<see cref="QABoss103HitTiming.GuardStartAt"/>, 홀드 = 첫 피해 + 0.4초까지).
/// 대시 방향 설정(<see cref="UserInputConfig.DashDirection"/>)은 이 프로세스에서만 조준 방향(Cursor)으로 바꿔 두고(PlayerPrefs 저장 없음) 끝나면 되돌린다.
/// 회피 뒤에는 그 위험이 끝날 때까지 다가가지 않는다. 오라(차징)는 켜진 동안 원 밖에 머문다.</para>
/// <para><b>간파</b>: <see cref="TwentyThreeBoss.CounterWindowOpen"/>(복제) 상승 엣지 = 기회. 우클릭 쿨이고 사거리 + 2.5m 안이면 굴린다.
/// C7e — 성공 굴림: 창 동안 새 평타·스킬을 시작하지 않고(홀드 놓음), 반응 지연 뒤부터 마감(추정 닫힘 − 판정 선딜 0.333 − 0.07초)까지
/// 사거리·정면(앞 반원)·Idle/Move 가 맞을 때 누른다 — 안 맞으면 보스 정면 쪽으로 걸어 들어가고, 시전이 거부되면 다음 프레임에 다시 누른다.
/// 추정 닫힘 = 돌진 창이면 돌진 출발(<see cref="QABoss103HitTiming.DashLaunchDelay"/>), 아니면 돌진 아닌 창 관측 최솟값.
/// 실패 굴림 = 관측 닫힘 + 150ms 에 1회.</para>
/// <para>로그: <c>[QA103] event=threat</c>(위험 1건 1줄 — hitMs·deadMs·doMs 추가) · <c>event=counter_try</c> / <c>counter_skip</c>(창 1개 1줄).
/// 결과(실제 피격·간파 성공)는 C4 가 서버 로그로 집계하고 C7e <c>event=bot_fidelity</c> 가 굴림과 잇는다.</para>
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
    /// <summary>C7e — 간파 창이 열릴 때 사거리보다 이만큼(m) 멀어도 굴린다(성공 굴림이면 걸어 들어간다).</summary>
    private const double CounterApproachSlack = 2.5;
    /// <summary>C7e — 간파하러 설 자리 = 보스 표면에서 이만큼(m) 앞.</summary>
    private const float CounterStandOff = 0.5f;

    private sealed class PendingReaction
    {
        public QABoss103Threat Threat;
        public bool Dodge;
        /// <summary>반응 지연 끝(실시간).</summary>
        public double ActAt;
        /// <summary>예상 첫 피해·회피 마감(실시간).</summary>
        public double HitAt, DeadlineAt;
        /// <summary>반응 지연이 지나 회피 중(공격 시작 멈춤·걷기).</summary>
        public bool Engaged;
        /// <summary>팔라딘 — 걷지 않고 서서 방어할 계획.</summary>
        public bool GuardPlan;
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

    private int _threatsSeen, _dashes, _guards, _walks, _noCharges, _blocks, _dashRejects, _guardRejects, _late;
    /// <summary>할 일 119 — 즉사(굴림 없음) 위험 id(같은 위험은 한 번만 센다) · 수 · 예고 판별이 발동과 어긋난 수.</summary>
    private readonly HashSet<string> _lethalSeen = new HashSet<string>();
    private int _lethalNoRoll, _lethalMismatch;

    // 간파
    private readonly QABoss103WindowEstimate _window = new QABoss103WindowEstimate();
    private bool _windowWas;
    private double _windowOpenAt = -1.0;
    private int _counterWindows, _counterTries, _counterSkips, _counterRetries;
    private bool _counterPending, _counterHit, _counterWaitClose, _counterIsDash;
    private double _counterPressAt, _counterDeadline, _counterPlanMs, _counterEstMs;

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

    /// <summary>C7e — '피함' 굴림 위험이 대기·진행 중(끼임 대시가 회피 충전을 먹지 않게).</summary>
    private bool HasDodgePending
    {
        get
        {
            foreach (PendingReaction r in _reactions)
                if (r.Dodge)
                    return true;
            return false;
        }
    }

    // ── 회피 ───────────────────────────────────────────────────

    private void TickEvasion(Player me, Vector3 myPos, float surface, double now)
    {
        _sensor.Bind(_boss);
        _sensor.Tick(me, now);

        CheckLethalFire();
        IReadOnlyList<QABoss103Threat> threats = _sensor.Threats;
        for (int i = 0; i < threats.Count; i++)
        {
            QABoss103Threat t = threats[i];
            if (!t.Active || _ledger.HasRolled(t.Id) || _lethalSeen.Contains(t.Id) || !_sensor.Contains(t, myPos, surface))
                continue;
            if (t.Lethal != QABoss103LethalKind.None)
            {
                NoteLethal(t, now);   // 할 일 119 — 무적 무시 즉사: 굴림·대시 없음(기믹 행동은 그대로)
                continue;
            }
            if (!_ledger.TryRoll(t.Id, _dodgeRng, _profile.PatternHitChance, out bool dodge))
                continue;
            _threatsSeen++;
            double dist = QAPartyPlacement88.HorizontalDistance(myPos, _boss.transform.position);
            _reactions.Add(new PendingReaction
            {
                Threat = t,
                Dodge = dodge,
                ActAt = now + _profile.ReactionMs / 1000.0,
                HitAt = t.DetectedAt + QABoss103HitTiming.HitDelay(t.Kind, t.LeadSeconds, t.GrowSeconds, dist, t.Chained),
                DeadlineAt = t.DetectedAt + QABoss103HitTiming.DeadlineDelay(t.Kind, t.LeadSeconds, t.GrowSeconds, dist, t.Chained),
            });
        }

        for (int i = 0; i < _reactions.Count; i++)
        {
            PendingReaction r = _reactions[i];
            if (now < r.ActAt)
                continue;
            if (!StepReaction(me, myPos, surface, r, now))
                continue;
            _reactions.RemoveAt(i--);
        }

        if (_guardHeld && now >= _guardReleaseAt)
            ReleaseGuard();
    }

    /// <summary>반응 지연이 지난 위험 1건을 한 틱 진행한다. 끝났으면(로그 1줄 남김) true.</summary>
    private bool StepReaction(Player me, Vector3 myPos, float surface, PendingReaction r, double now)
    {
        QABoss103Threat t = r.Threat;
        if (!r.Dodge)
        {
            Report(r, QABoss103DodgeAction.None, "na", "none", now);
            return true;
        }
        if (t.Superseded)
        {
            Report(r, QABoss103DodgeAction.None, "na", "superseded", now);   // 예고 모양(점프 원 등)이 같은 공격을 다시 잡았다
            return true;
        }
        if (!r.Engaged)
        {
            r.Engaged = true;
            if (t.Kind == QABoss103ThreatKind.Dash && _counterPending && _counterHit)
            {
                Report(r, QABoss103DodgeAction.None, "na", "counter_first", now);   // 돌진은 간파 창을 연다 — 간파 성공 굴림이면 피하지 않고 누른다
                return true;
            }
            if (t.Kind != QABoss103ThreatKind.Aura && QABoss103HitTiming.Missed(r.ActAt, r.DeadlineAt))
            {
                _late++;
                Report(r, QABoss103DodgeAction.None, "na", "expired", now);   // 반응 지연이 마감보다 늦다 — 회피 굴림이었지만 이미 늦었다
                return true;
            }
            StopHold();
            _driver.ReleaseBasic();
            PlayerSkillController skills = me.GetComponent<PlayerSkillController>();
            r.GuardPlan = _character == QABoss103Character.Paladin && QABoss103Evasion.IsGuardable(t.Kind) && skills != null &&
                          skills.GetSkill(PlayerSkillSlot.Sub) != null && skills.IsCooldownReady(PlayerSkillSlot.Sub);
        }

        if (t.Kind == QABoss103ThreatKind.Aura)
        {
            // 오라는 켜진 동안 원 밖에 머문다(걷기 = EvadeHolds). 꺼지면 1줄.
            if (t.Active)
                return false;
            Report(r, QABoss103DodgeAction.Walk, "true", "aura_end", now);
            _walks++;
            return true;
        }

        // 방어 계획이면 방어 시각부터, 아니면 대시 시각부터 마감 직전까지 매 틱 시도한다(Q·R 이 끝나 대시·방어가 열리는 순간을 잡는다).
        double dashAt = QABoss103HitTiming.DashStartAt(r.DeadlineAt, r.ActAt);
        double firstAt = r.GuardPlan ? QABoss103HitTiming.GuardStartAt(r.DeadlineAt, r.ActAt) : dashAt;
        if (now < firstAt)
            return false;
        bool final = now >= System.Math.Max(dashAt, r.DeadlineAt - FinalTrySeconds);

        if (!_sensor.Contains(t, myPos, surface, true))
        {
            _walks++;
            _avoidUntil = System.Math.Max(_avoidUntil, System.Math.Max(t.EndsAt, r.HitAt) + AvoidTailSeconds);
            Report(r, QABoss103DodgeAction.Walk, "true", "outside", now);
            return true;
        }
        QABoss103DodgeAction action = Dodge(me, r, now, now >= dashAt, out bool done, out string note);
        if (!done && !final)
            return false;
        CountAction(action, done);
        string ok = action == QABoss103DodgeAction.Dash || action == QABoss103DodgeAction.Guard ? (done ? "true" : "false") : "na";
        Report(r, action, ok, note, now);
        return true;
    }

    /// <summary>
    /// 할 일 119 — 즉사(무적 무시) 위험 1건: 굴리지 않고 '즉사(굴림 없음)'로 1줄·판 파일 <c>lethal</c> 1줄. 같은 위험은 한 번만.
    /// 할 일 105 로 게임이 이 방전에서 무적을 무시하므로(2회차~·④ 는 칸·위치 무관 전원 사망) 대시를 쓰지 않는다. 탑 공격 등 기믹은 C3b 가 그대로 한다.
    /// </summary>
    private void NoteLethal(QABoss103Threat t, double now)
    {
        _lethalSeen.Add(t.Id);
        if (_lethalSeen.Count > QABoss103ThreatLedger.Memory)
            _lethalSeen.Clear();   // 위험 id 는 종류별 회차라 다시 오지 않는다 — 기억 상한만 둔다
        _lethalNoRoll++;
        double hitMs = QABoss103HitTiming.HitDelay(t.Kind, t.LeadSeconds, t.GrowSeconds, double.NaN, t.Chained) * 1000.0;
        Debug.Log($"{Tag} event=threat pattern={QABoss103Evasion.Token(t.Kind)} id={t.Id} attack={QABoss103Plan.Token(t.Attack)} bot={_character} slot={_slot} " +
                  $"tier={_profile.Tier} roll=lethal action=none ok=na lethal={QABoss103Lethal.Token(t.Lethal)} " +
                  $"leadMs={(double.IsNaN(t.LeadSeconds) ? "na" : F0(t.LeadSeconds * 1000.0))} hitMs={F0(hitMs)} serverT={F3(t.ServerT)} note=lethal_no_roll");
        PartWrite(QABoss103PartFile.Format("lethal",
            "slot", _slot.ToString(CultureInfo.InvariantCulture),
            "pattern", QABoss103Evasion.Token(t.Kind),
            "attack", t.Attack,
            "id", t.Id,
            "serverT", F3(t.ServerT),
            "lethal", QABoss103Lethal.Token(t.Lethal),
            "hitMs", F0(hitMs)));
    }

    /// <summary>할 일 119 — 장판 발동마다 예고 때 즉사 판별이 실제 발동(즉사 연출 플래그)과 맞았나(둘 중 하나라도 즉사일 때만 1줄).</summary>
    private void CheckLethalFire()
    {
        if (!_sensor.TakeFloorFire(out QABoss103Threat warned, out ulong mask, out bool firedLethal))
            return;
        QABoss103LethalKind predicted = warned != null ? warned.Lethal : QABoss103LethalKind.None;
        if (predicted == QABoss103LethalKind.None && !firedLethal)
            return;
        bool match = (predicted != QABoss103LethalKind.None) == firedLethal;
        if (!match)
            _lethalMismatch++;
        string line = $"{Tag} event=lethal_check bot={_character} slot={_slot} predicted={QABoss103Lethal.Token(predicted)} firedLethal={(firedLethal ? "true" : "false")} " +
                      $"match={(match ? "true" : "false")} tiles={BossTileGrid.Count(mask)} warnMs={(warned != null && !double.IsNaN(warned.LeadSeconds) ? F0(warned.LeadSeconds * 1000.0) : "na")} id={(warned != null ? warned.Id : "na")}";
        if (match)
            Debug.Log(line);
        else
            Debug.LogWarning(line);
    }

    /// <summary>C7e — 마지막 시도 = 마감 이만큼(초) 전. 대시 무적(0.25)이 마감을 덮을 마지막 틈.</summary>
    private const double FinalTrySeconds = 0.03;

    private void CountAction(QABoss103DodgeAction action, bool done)
    {
        switch (action)
        {
            case QABoss103DodgeAction.Guard: if (done) _guards++; else _guardRejects++; break;
            case QABoss103DodgeAction.Dash: if (done) _dashes++; else _dashRejects++; break;
            case QABoss103DodgeAction.NoCharge: _noCharges++; break;
            case QABoss103DodgeAction.Blocked: _blocks++; break;
        }
    }

    private void Report(PendingReaction r, QABoss103DodgeAction action, string ok, string note, double now)
    {
        QABoss103Threat t = r.Threat;
        double lagMs = (now - t.DetectedAt) * 1000.0;
        // 오라는 켜진 내내 위험이라 '예상 피격' 대신 켜져 있던 시간을 남긴다(충실도 창이 오라 전체를 덮게).
        double hitMs = t.Kind == QABoss103ThreatKind.Aura ? lagMs : (r.HitAt - t.DetectedAt) * 1000.0;
        double deadMs = (r.DeadlineAt - t.DetectedAt) * 1000.0;
        Debug.Log($"{Tag} event=threat pattern={QABoss103Evasion.Token(t.Kind)} id={t.Id} attack={QABoss103Plan.Token(t.Attack)} bot={_character} slot={_slot} " +
                  $"tier={_profile.Tier} roll={(r.Dodge ? "dodge" : "take")} action={QABoss103Evasion.Token(action)} ok={ok} " +
                  $"reactMs={F0(_profile.ReactionMs)} actMs={F0(lagMs)} leadMs={(double.IsNaN(t.LeadSeconds) ? "na" : F0(t.LeadSeconds * 1000.0))} " +
                  $"hitMs={F0(hitMs)} deadMs={F0(deadMs)} doMs={F0(lagMs)} guardPlan={(r.GuardPlan ? "true" : "false")} chained={(t.Chained ? "true" : "false")} " +
                  $"targetedMe={(t.TargetedMe ? "true" : "false")} serverT={F3(t.ServerT)} note={QABoss103Plan.Token(note)}");
        PartThreat(t, r.Dodge, action, ok, note, hitMs);
    }

    /// <summary>
    /// 회피 시도 1회(아직 범위 안). 방어 계획이면 방어, 안 되면(<paramref name="allowDash"/> = 대시 시각이 지났을 때만) 대시.
    /// 실패해도 집계는 하지 않는다 — 호출자가 성공 또는 마지막 시도에서 <see cref="CountAction"/>.
    /// </summary>
    private QABoss103DodgeAction Dodge(Player me, PendingReaction r, double now, bool allowDash, out bool done, out string note)
    {
        QABoss103Threat t = r.Threat;
        done = false;
        note = "none";
        PlayerSkillController skills = me.GetComponent<PlayerSkillController>();
        PlayerDashController dash = me.GetComponent<PlayerDashController>();
        PlayerStateController sc = me.GetComponent<PlayerStateController>();

        bool guardUsable = r.GuardPlan && skills != null && skills.GetSkill(PlayerSkillSlot.Sub) != null && skills.IsCooldownReady(PlayerSkillSlot.Sub) && !skills.IsBusy;
        bool idleMove = me.CurrentState == PlayerActionState.Idle || me.CurrentState == PlayerActionState.Move;
        bool dashAllowed = idleMove || (sc != null && sc.CurrentActionAllowsDashCancel);
        bool hasCharge = dash != null && !dash.IsOutOfCharge;

        QABoss103DodgeAction action = QABoss103Evasion.ChooseAction(true, guardUsable, hasCharge, dashAllowed);
        if (action == QABoss103DodgeAction.Guard)
        {
            if (TryGuard(me, skills, t, System.Math.Max(r.HitAt, r.DeadlineAt), now))
            {
                done = true;
                _avoidUntil = System.Math.Max(_avoidUntil, System.Math.Max(t.EndsAt, r.HitAt) + AvoidTailSeconds);
                return action;
            }
            note = "guard_rejected";
            action = QABoss103Evasion.ChooseAction(true, false, hasCharge, dashAllowed);
        }
        else if (r.GuardPlan)
        {
            note = "guard_unready";
        }
        if (!allowDash)
            return action;   // 방어 시각 ~ 대시 시각 사이 — 방어만 노린다

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
                    _dashLockUntil = now + DashLockSeconds;
                    _avoidUntil = System.Math.Max(_avoidUntil, System.Math.Max(t.EndsAt, r.HitAt) + AvoidTailSeconds);
                }
                if (note == "none")
                    note = $"dir={F2(dir.x)}/{F2(dir.z)} state={me.CurrentState}";
                break;
            case QABoss103DodgeAction.NoCharge:
                if (note == "none" && dash != null)
                    note = $"charges={dash.PredictedCharge}/{dash.MaxCharge}";
                break;
            case QABoss103DodgeAction.Blocked:
                if (note == "none" || note == "guard_unready")
                    note = (note == "guard_unready" ? "guard_unready_" : "") + "state_" + me.CurrentState;
                break;
        }
        return action;
    }

    private bool TryGuard(Player me, PlayerSkillController skills, QABoss103Threat t, double hitAt, double now)
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
            _guardReleaseAt = now + QABoss103Evasion.GuardHoldSeconds(System.Math.Max(t.Remaining(now), hitAt - now), cap);
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
    /// C7e — 회피 진행 중 이동. 반환: <paramref name="hold"/> = 이번 틱 C2 이동·공격을 통째로 막는다(범위 안이라 걷는 중·서서 방어 대기),
    /// <paramref name="noStart"/> = 새 평타·스킬을 시작하지 않는다(진행 중 '피함' 위험이 있다 — 이미 밖이면 제자리 공격은 하던 것만).
    /// </summary>
    private void EvadeMove(Vector3 myPos, float surface, double now, out bool hold, out bool noStart)
    {
        hold = false;
        noStart = false;
        PendingReaction lead = null;
        foreach (PendingReaction r in _reactions)
        {
            if (!r.Dodge || !r.Engaged)
                continue;
            bool inside = _sensor.Contains(r.Threat, myPos, surface, true);
            if (r.Threat.Kind == QABoss103ThreatKind.Aura)
            {
                _avoidUntil = System.Math.Max(_avoidUntil, now + AvoidTailSeconds);
                if (!inside)
                    continue;
            }
            if (!inside && !r.GuardPlan)
            {
                // 이미 밖 — 다가가지만 않으면 하던 자리 공격은 둔다.
                _avoidUntil = System.Math.Max(_avoidUntil, now + AvoidTailSeconds);
                continue;
            }
            noStart = true;
            if (r.GuardPlan)
            {
                // 방어 계획도 걷는다(밖으로 나가면 방어 없이 walk) — 조준만 출처에 둬 방어 방향을 잡는다.
                hold = true;
                _driver.SetAim(_sensor.Source(r.Threat));
            }
            if (inside && (lead == null || r.DeadlineAt < lead.DeadlineAt))
                lead = r;
        }
        if (lead == null)
        {
            if (hold)
                _input.SetMove(Vector3.zero);
            return;
        }
        hold = true;
        _driver.ReleaseBasic();
        Vector3 dir = SafeEscape(myPos, _sensor.EscapeDirection(lead.Threat, myPos));
        _input.SetMove(IsSafeStep(myPos, dir) ? dir : Vector3.zero);
    }

    /// <summary>
    /// C7d 끼임 6초 단계 — 진행 방향 기준 뒤 → 왼쪽 → 오른쪽 차례(<see cref="QABoss103StuckEscalation.DashTurnDegrees"/>)로 대시해 빠져나온다.
    /// 충전이 최대 2개 이상이면 회피용 1개는 남긴다(<see cref="QABoss103StuckEscalation.CanSpendDash"/>). C7e — '피함' 위험이 대기 중이면 쓰지 않는다. 대시했으면 true.
    /// </summary>
    private bool TryStuckDash(Player me, Vector3 myPos, Vector3 moveDir, double realNow)
    {
        PlayerDashController dash = me.GetComponent<PlayerDashController>();
        PlayerStateController sc = me.GetComponent<PlayerStateController>();
        bool idleMove = me.CurrentState == PlayerActionState.Idle || me.CurrentState == PlayerActionState.Move;
        bool allowed = idleMove || (sc != null && sc.CurrentActionAllowsDashCancel);
        string skip = dash == null ? "no_dash"
            : HasDodgePending ? "reserve"
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
            double hitMs = (r.HitAt - r.Threat.DetectedAt) * 1000.0;
            PartThreat(r.Threat, r.Dodge, QABoss103DodgeAction.None, "na", "idle_" + reason, hitMs);
            Debug.Log($"{Tag} event=threat pattern={QABoss103Evasion.Token(r.Threat.Kind)} id={r.Threat.Id} attack={QABoss103Plan.Token(r.Threat.Attack)} bot={_character} slot={_slot} " +
                      $"tier={_profile.Tier} roll={(r.Dodge ? "dodge" : "take")} action=none ok=na reactMs={F0(_profile.ReactionMs)} actMs=na " +
                      $"leadMs={(double.IsNaN(r.Threat.LeadSeconds) ? "na" : F0(r.Threat.LeadSeconds * 1000.0))} hitMs={F0(hitMs)} targetedMe={(r.Threat.TargetedMe ? "true" : "false")} " +
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
        TickDashInterrupt(me);   // 할 일 116 — 진행 중인 대쉬 간파 두 번째 입력·결과
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
        if (now < _counterPressAt)
            return;
        if (!_counterHit)
        {
            PressCounter(me, myPos, now, open, true);
            return;
        }
        // 성공 굴림 — 마감까지 조건(사거리·정면·Idle/Move)이 맞을 때 누르고, 거부되면 다시 누른다. 마감이 지나면 마지막으로 1회.
        bool last = now >= _counterDeadline;
        if (last || CounterReady(me, myPos))
            PressCounter(me, myPos, now, open, last);
    }

    /// <summary>C7e — 지금 눌러도 되나: 시전 가능 상태(Idle/Move·다른 스킬 없음) + 사거리 + 정면.</summary>
    private bool CounterReady(Player me, Vector3 myPos)
    {
        PlayerSkillController skills = me.GetComponent<PlayerSkillController>();
        bool idleMove = me.CurrentState == PlayerActionState.Idle || me.CurrentState == PlayerActionState.Move;
        if (skills == null || skills.IsBusy || !idleMove)
            return false;
        return SurfaceDistance(myPos, _boss.transform.position) <= CounterReach(me, skills) && CounterFront(myPos, out _, out _);
    }

    private bool CounterFront(Vector3 myPos, out double frontDeg, out float limit)
    {
        Vector3 bossPos = _boss.transform.position;
        Vector3 to = myPos - bossPos;
        Vector3 bossFwd = _boss.transform.forward;
        bool requiresFront = true;
        limit = QABoss103Counter.DefaultFrontAngle;
        if (BossDataField != null && BossDataField.GetValue(_boss) is BossDataSO data)
        {
            requiresFront = data.counterRequiresFront;
            limit = data.counterFrontAngle;
        }
        frontDeg = QAInterruptLag88Plan.FlatAngleDeg(bossFwd.x, bossFwd.z, to.x, to.z);
        return !requiresFront || QABoss103Counter.IsFront(bossFwd.x, bossFwd.z, to.x, to.z, limit);
    }

    /// <summary>
    /// C7e — 간파 성공 굴림 대기 중(창 열림 ~ 누름): 새 평타·스킬을 시작하지 않고, 반응 지연 뒤엔 사거리·정면이 안 맞으면 보스 정면 자리로 걷는다.
    /// 이번 틱 C2 이동·공격을 막았으면 true.
    /// </summary>
    private bool CounterHoldsBot(Player me, Vector3 myPos, double now)
    {
        if (DashInterruptHoldsBot)
            return true;   // 할 일 116 — 대쉬 간파 진행 중(입력은 TickDashInterrupt 가 잡고 있다)
        if (!_counterPending || !_counterHit || _counterWaitClose)
            return false;
        StopHold();
        _driver.ReleaseBasic();
        Vector3 bossPos = _boss.transform.position;
        _driver.SetAim(bossPos);
        if (now < _counterPressAt)
        {
            _input.SetMove(Vector3.zero);
            return true;
        }
        PlayerSkillController skills = me.GetComponent<PlayerSkillController>();
        bool inRange = SurfaceDistance(myPos, bossPos) <= CounterReach(me, skills) - 0.2;
        bool front = CounterFront(myPos, out _, out _);
        Vector3 move = Vector3.zero;
        if (!inRange || !front)
        {
            Vector3 fwd = _boss.transform.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 to = myPos - bossPos;
            to.y = 0f;
            // 뒤쪽이면 몸을 뚫지 않게 옆(내 쪽)으로 돌아 들어간다.
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            if (Vector3.Dot(side, to) < 0f)
                side = -side;
            Vector3 spotDir = front ? fwd : (fwd + side).normalized;
            Vector3 spot = bossPos + spotDir * (_bossRadius + CounterStandOff);
            move = spot - myPos;
            move.y = 0f;
            move = move.sqrMagnitude > 0.04f ? move.normalized : Vector3.zero;
        }
        _input.SetMove(move != Vector3.zero && IsSafeStep(myPos, move) ? move : Vector3.zero);
        return true;
    }

    private void OnCounterOpen(Player me, Vector3 myPos, float surface, double now)
    {
        _counterWindows++;
        _windowOpenAt = now;
        _counterPending = false;
        _counterRetries = 0;
        QABoss103Threat dashThreat = ActiveDashThreat(now);
        _counterIsDash = dashThreat != null;

        PlayerSkillController skills = me.GetComponent<PlayerSkillController>();
        string skip = null;
        double reach = CounterReach(me, skills);
        if (skills == null || skills.GetSkill(PlayerSkillSlot.Interrupt) == null)
            skip = "no_skill";
        else if (!skills.IsCooldownReady(PlayerSkillSlot.Interrupt))
            skip = "cooldown";
        else if (surface > reach + CounterApproachSlack)
            skip = "range";
        if (skip != null)
        {
            _counterSkips++;
            Debug.Log($"{Tag} event=counter_skip bot={_character} slot={_slot} window={_counterWindows} reason={skip} surfM={F2(surface)} reachM={F2(reach)}");
            return;
        }

        _counterHit = QABoss103Counter.RollHit(_counterRng, _profile.InterruptSuccessChance);
        RollDashInterrupt();   // 할 일 116 — 이 창을 대쉬 간파로 누를지(따로 굴림, 성공 굴림 순서 불변)
        double closeAt = dashThreat != null
            ? dashThreat.DetectedAt + QABoss103HitTiming.DashLaunchDelay(dashThreat.LeadSeconds, dashThreat.GrowSeconds)
            : now + _window.EstimateMs() / 1000.0;
        _counterEstMs = (closeAt - now) * 1000.0;
        _counterPending = true;
        if (_counterHit)
        {
            _counterWaitClose = false;
            _counterPressAt = now + _profile.ReactionMs / 1000.0;
            _counterDeadline = System.Math.Max(_counterPressAt, QABoss103HitTiming.CounterPressDeadline(closeAt));
            _counterPlanMs = _profile.ReactionMs;
        }
        else
        {
            _counterWaitClose = true;   // 닫힘을 본 뒤 +150ms
            _counterPlanMs = double.NaN;
        }
    }

    /// <summary>방금(0.5초 안) 감지한 돌진 띠 — 그 창은 돌진 출발에 닫힌다.</summary>
    private QABoss103Threat ActiveDashThreat(double now)
    {
        foreach (QABoss103Threat t in _sensor.Threats)
            if (t.Kind == QABoss103ThreatKind.Dash && t.Active && now - t.DetectedAt <= 0.5)
                return t;
        return null;
    }

    private void OnCounterClose(double now)
    {
        if (_windowOpenAt >= 0.0 && !_counterIsDash)
            _window.Add(now - _windowOpenAt);   // 돌진 창은 출발 시각으로 따로 안다 — 돌진 아닌 창만 모은다
        if (_counterPending && _counterWaitClose)
        {
            _counterWaitClose = false;
            _counterPressAt = now + QABoss103Counter.LatePressMs / 1000.0;
            _counterPlanMs = (_counterPressAt - _windowOpenAt) * 1000.0;
        }
    }

    /// <summary>누른다. <paramref name="final"/> = 거부돼도 이번이 마지막(실패 굴림·마감) — 아니면 거부 시 다음 틱 다시.</summary>
    private void PressCounter(Player me, Vector3 myPos, double now, bool openNow, bool final)
    {
        PlayerSkillController skills = me.GetComponent<PlayerSkillController>();
        Vector3 bossPos = _boss.transform.position;
        bool front = CounterFront(myPos, out double frontDeg, out float limit);
        float surface = SurfaceDistance(myPos, bossPos);
        double reach = CounterReach(me, skills);

        StopHold();
        _driver.ReleaseBasic();
        _driver.SetAim(bossPos);
        PlayerMovement movement = me.GetComponent<PlayerMovement>();
        Vector3 face = bossPos - myPos;
        if (movement != null)
            movement.RotateToward(face, 1000f);
        // 할 일 116 — 이 창이 대쉬 간파 굴림이면 그 순서로 누른다(대시 거부도 이 창의 표본 — 일반 간파로 다시 누르지 않는다).
        bool dashPath = TryStartDashInterrupt(me, skills, bossPos, out bool dashCast);
        bool ok = dashPath ? dashCast : skills != null && skills.ExecuteTargetedSkill(PlayerSkillSlot.Interrupt, _boss, bossPos, true);
        if (!ok && !final && !dashPath)
        {
            _counterRetries++;
            return;
        }
        _counterPending = false;
        _counterTries++;

        NetworkManager nm = NetworkManager.Singleton;
        double serverT = nm != null && nm.IsListening ? nm.ServerTime.Time : -1.0;
        Debug.Log($"{Tag} event=counter_try bot={_character} slot={_slot} tier={_profile.Tier} window={_counterWindows} roll={(_counterHit ? "hit" : "miss")} " +
                  $"pressMs={F0((now - _windowOpenAt) * 1000.0)} planMs={F0(_counterPlanMs)} deadlineMs={(_counterHit ? F0((_counterDeadline - _windowOpenAt) * 1000.0) : "na")} " +
                  $"reactMs={F0(_profile.ReactionMs)} windowEstMs={F0(_counterEstMs)} dashWindow={(_counterIsDash ? "true" : "false")} retries={_counterRetries} " +
                  $"windowSamples={_window.Count} openAtPress={(openNow ? "true" : "false")} front={(front ? "true" : "false")} frontDeg={F0(frontDeg)} frontLimit={F0(limit)} " +
                  $"surfM={F2(surface)} reachM={F2(reach)} inRange={(surface <= reach ? "true" : "false")} state={me.CurrentState} cast={(ok ? "true" : "false")} serverT={F3(serverT)}");
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
        $"threats={_threatsSeen} dodgeRolls={_ledger.Dodges} takeRolls={_ledger.Takes} dashes={_dashes} dashRejects={_dashRejects} guards={_guards} walks={_walks} " +
        $"guardRejects={_guardRejects} noCharge={_noCharges} blocked={_blocks} late={_late} lethalNoRoll={_lethalNoRoll} lethalMismatch={_lethalMismatch} " +
        $"counterWindows={_counterWindows} counterTries={_counterTries} counterSkips={_counterSkips} {DashInterruptDiag()}";

    private static string F0(double v) => double.IsNaN(v) ? "na" : v.ToString("F0", CultureInfo.InvariantCulture);
    private static string F2(double v) => double.IsNaN(v) ? "na" : v.ToString("F2", CultureInfo.InvariantCulture);
    private static string F3(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
}
#endif
