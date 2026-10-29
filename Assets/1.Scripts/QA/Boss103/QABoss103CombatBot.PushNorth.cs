#if UNITY_EDITOR
using System.Reflection;
using BeaverLobby.Player.Dash;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 할 일 140-2 — 3페이즈 북진 무력화 간파 창(마지막 송전탑 파괴 → <c>pushNorthStunTime</c> 초). 규칙은 <see cref="QABoss103PushNorthCounter"/>, 간파 기본 흐름은
/// <c>QABoss103CombatBot.Evasion.cs</c>. 보스·탑은 읽기만 한다(클라 = 탑 몸 콜라이더, 호스트 = 탑 상태·보스 기믹 값).
/// <list type="bullet">
/// <item>활성 탑이 모두 사라지면(창이 열리기 전이라도) <see cref="QABoss103PushNorthCounter.LinkSeconds"/> 동안 보스 정면 자리로 걷는다.</item>
/// <item>그 뒤 열린 창 = 북진 창: 닫힘·마감·포기 거리를 무력화 시간에 맞추고, 반응 지연 동안에도 걷는다. 실패 굴림은 창 안에서 보스 반대쪽을 겨눠 누른다.</item>
/// <item>마감까지 걸어서 못 닿고 대시 한 번 거리 안이면 대쉬 간파(대시 → 창 안 <see cref="PlayerSkillController.TryUseDashInterrupt"/>).</item>
/// <item>창마다 1줄 <c>[QA103] event=pn_counter</c> — 야간 QA events.tsv 기록 전용.</item>
/// </list>
/// </summary>
public sealed partial class QABoss103CombatBot
{
    private static readonly FieldInfo RaidGimmickField = typeof(TwentyThreeBoss).GetField("_raidGimmick", AnyInst);
    private static readonly FieldInfo DashDataField = typeof(PlayerDashController).GetField("dashData", AnyInst);

    // 활성 탑 소멸 관측
    private bool _pylonLiveWas;
    private double _pylonsClearedAt = -1.0;

    // 이번 북진 창
    private bool _pnWindow, _pnRolled, _pnTried, _pnCast, _pnDash, _pnDashGaveUp;
    private string _pnReason;
    private double _pnCloseAt, _pnOpenGameT, _pnOpenServerT, _pnReach, _pnSlack, _pnJudgeAt;
    private double _pnPressAt = -1.0, _pnDashAt = -1.0;
    private float _pnOpenDist, _pnOpenSurf;
    private Vector3 _pnDashDir;
    private int _pnWindows, _pnTries, _pnDashTries;
    // 할 일 140-4 — 누름 자리에서 이동 입력 0 인 틱 수·시작 시각(누를 때 멈춤 확인)
    private int _pnStillTicks;
    private double _pnStillSince = -1.0;

    /// <summary>매 틱(간파 판정 전) — 직전 틱에 활성 탑이 있었는데 지금 하나도 없으면 소멸 시각을 남긴다.</summary>
    private void ObservePylonClear(double now)
    {
        bool live = false;
        foreach (PylonView v in _pylonViews)
        {
            BossChargingPylon p = v.Pylon;
            if (p == null || !p.IsSpawned)
                continue;
            live |= p.IsServer ? p.IsLive : v.Body != null && v.Body.enabled;
        }
        if (_pylonLiveWas && !live)
            _pylonsClearedAt = now;
        _pylonLiveWas = live;
    }

    /// <summary>호스트만 — 보스 기믹이 북진이 아니라고 확실히 안다(클라는 서버 전용 값이라 모른다 → false).</summary>
    private bool HostSaysNotPushNorth()
    {
        if (_boss == null || !_boss.IsServer || RaidGimmickField == null)
            return false;
        object v = RaidGimmickField.GetValue(_boss);
        return v == null || v.ToString() != "PushNorth";
    }

    /// <summary>① 활성 탑이 막 다 사라졌고 창은 아직 — 보스 쪽으로 미리 걷는다.</summary>
    private bool PnPreApproach(double now) =>
        !_windowWas && _pylonsClearedAt >= 0.0 && now - _pylonsClearedAt <= QABoss103PushNorthCounter.LinkSeconds && !HostSaysNotPushNorth();

    private double PnStunSeconds() =>
        BossDataField != null && BossDataField.GetValue(_boss) is BossDataSO data ? data.pushNorthStunTime : QABoss103PushNorthCounter.DefaultStunSeconds;

    private static double MoveSpeedOf(Player me)
    {
        PlayerMovement m = me != null ? me.GetComponent<PlayerMovement>() : null;
        return m != null ? m.MaxResolvedMoveSpeed : QABoss103PushNorthCounter.FallbackMoveSpeed;
    }

    /// <summary>대쉬 간파로 줄일 수 있는 거리(m) — 충전이 없거나 기능이 꺼졌으면 0.</summary>
    private static double DashInterruptReach(Player me)
    {
        PlayerDashController dash = me != null ? me.GetComponent<PlayerDashController>() : null;
        if (dash == null || dash.IsOutOfCharge || dash.DashInterruptWindow <= 0f)
            return 0.0;
        return DashDataField != null && DashDataField.GetValue(dash) is PlayerDashData d
            ? d.DashSpeed * d.DashDuration
            : QABoss103PushNorthCounter.FallbackDashReach;
    }

    /// <summary>창 열림 때 — 북진 창이면 상태를 잡고 예상 닫힘을 돌려준다(아니면 NaN).</summary>
    private double BeginPnWindow(Player me, Vector3 myPos, float surface, double now)
    {
        _pnWindow = QABoss103PushNorthCounter.IsPushNorthWindow(now, _pylonsClearedAt) && !HostSaysNotPushNorth();
        double clearedAt = _pylonsClearedAt;
        _pylonsClearedAt = -1.0;
        if (!_pnWindow)
            return double.NaN;
        _pnWindows++;
        _pnRolled = _pnTried = _pnCast = _pnDash = _pnDashGaveUp = false;
        _pnReason = null;
        _pnPressAt = _pnDashAt = -1.0;
        _pnJudgeAt = double.NaN;
        _pnStillTicks = 0;
        _pnStillSince = -1.0;
        _pnCloseAt = QABoss103PushNorthCounter.CloseAt(now, clearedAt, PnStunSeconds());
        _pnOpenGameT = Time.timeAsDouble;
        NetworkManager nm = NetworkManager.Singleton;
        _pnOpenServerT = nm != null && nm.IsListening ? nm.ServerTime.Time : double.NaN;
        _pnOpenDist = QAPartyPlacement88.HorizontalDistance(myPos, _boss.transform.position);
        _pnOpenSurf = surface;
        _pnReach = CounterReach(me, me.GetComponent<PlayerSkillController>());
        // 걷기는 창이 열리자마자(반응 지연 없이) 시작한다 — 마감까지 걸어서 + 대쉬 간파 한 번.
        _pnSlack = QABoss103PushNorthCounter.ApproachSlack(QABoss103HitTiming.CounterPressDeadline(_pnCloseAt) - now, 0.0,
                                                          MoveSpeedOf(me), DashInterruptReach(me), CounterApproachSlack);
        return _pnCloseAt;
    }

    /// <summary>누름(또는 대쉬 간파 요청) 1회가 확정됐다.</summary>
    private void NotePnPress(bool cast, bool viaDash, double now)
    {
        if (!_pnWindow)
            return;
        _pnTried = true;
        _pnCast = cast;
        _pnDash = viaDash;
        _pnPressAt = now;
        _pnTries++;
        // 판정 = 누름 + 선딜(대쉬 간파는 max(대시 끝, 평소 Hit) — 기본 대시 0.25초 < 선딜이라 같다).
        _pnJudgeAt = now + QABoss103HitTiming.ParryHitSeconds;
    }

    /// <summary>
    /// ② 성공 굴림 대기 중 — 마감까지 걸어서 못 닿고 대시 한 번 거리 안이면 보스 정면 쪽으로 대시하고, 대시 시작 뒤 입력 창 안에 대쉬 간파를 요청한다.
    /// 이번 틱을 이 경로가 썼으면 true(일반 누름을 하지 않는다).
    /// </summary>
    private bool TickPnDash(Player me, Vector3 myPos, double now)
    {
        PlayerDashController dash = me.GetComponent<PlayerDashController>();
        PlayerSkillController skills = me.GetComponent<PlayerSkillController>();
        if (dash == null || skills == null)
            return false;

        if (_pnDashAt >= 0.0)
        {
            double window = dash.DashInterruptWindow + dash.DashInterruptServerTolerance * 0.5;
            if (now - _pnDashAt > window)
            {
                _pnDashAt = -1.0;
                _pnDashGaveUp = true;   // 창을 놓쳤다 — 다시 대시하지 않고 일반 누름으로
                Debug.Log($"{Tag} event=pn_dash_interrupt bot={_character} slot={_slot} ok=false reason=window_passed state={me.CurrentState}");
                return false;
            }
            if (me.CurrentState != PlayerActionState.Dash)
                return true;   // 대시 상태로 넘어가길 기다린다
            if (!skills.TryUseDashInterrupt(_pnDashDir))
                return true;   // 다음 틱 다시(창 안)
            _pnDashAt = -1.0;
            _counterPending = false;
            _counterTries++;
            NetworkManager nm = NetworkManager.Singleton;
            double serverT = nm != null && nm.IsListening ? nm.ServerTime.Time : -1.0;
            Debug.Log($"{Tag} event=counter_try bot={_character} slot={_slot} tier={_profile.Tier} window={_counterWindows} roll=hit " +
                      $"pressMs={F0((now - _windowOpenAt) * 1000.0)} deadlineMs={F0((_counterDeadline - _windowOpenAt) * 1000.0)} dashInterrupt=true pushNorth=true " +
                      $"surfM={F2(SurfaceDistance(myPos, _boss.transform.position))} reachM={F2(CounterReach(me, skills))} cast=true serverT={F3(serverT)}");
            PartCounter(true, true, serverT);
            NotePnPress(true, true, now);
            return true;
        }

        if (_pnDashGaveUp || skills.IsBusy || !(me.CurrentState == PlayerActionState.Idle || me.CurrentState == PlayerActionState.Move))
            return false;
        double reachDash = DashInterruptReach(me);
        double gap = SurfaceDistance(myPos, _boss.transform.position) - CounterReach(me, skills);
        if (!QABoss103PushNorthCounter.UseDashInterrupt(gap, _counterDeadline - now, MoveSpeedOf(me), reachDash))
            return false;
        Vector3 dir = CounterSpot(myPos) - myPos;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f || !IsSafeStep(myPos, dir.normalized))
            return false;
        dir.Normalize();
        StopHold();
        _driver.ReleaseBasic();
        _driver.SetAim(myPos + dir * 3f);
        PlayerMovement movement = me.GetComponent<PlayerMovement>();
        if (movement != null)
            movement.RotateToward(dir, 1000f);
        if (!dash.TryBeginPredictedDash())
        {
            _pnDashGaveUp = true;
            Debug.Log($"{Tag} event=pn_dash_interrupt bot={_character} slot={_slot} ok=false reason=dash_rejected gapM={F2(gap)}");
            return false;
        }
        _pnDashAt = now;
        _pnDashDir = dir;
        _pnDashTries++;
        _dashLockUntil = now + DashLockSeconds;
        Debug.Log($"{Tag} event=pn_dash_interrupt bot={_character} slot={_slot} ok=true step=dash gapM={F2(gap)} toDeadlineMs={F0((_counterDeadline - now) * 1000.0)}");
        return true;
    }

    // ── 할 일 140-4 — 성공 굴림 누름 자리 ───────────────────────

    /// <summary>
    /// 보스 HurtBox 실제 표면까지 수평 거리. 139-4 부터 <see cref="SurfaceDistance"/> 가 같은 실제 표면 거리라 그대로 위임한다
    /// (예전엔 AABB 라 보스가 대각선을 보면 최대 반경 × (√2 − 1) ≈ 0.7m 짧게 봤다).
    /// </summary>
    private float TrueSurfaceDistance(Vector3 myPos) =>
        _boss == null ? float.NaN : SurfaceDistance(myPos, _boss.transform.position);

    /// <summary>북진 누름 자리 — 실제 표면 거리 ≤ 사거리 − 여유 + 정면각 ≤ 서버 한도 − 여유(<see cref="QABoss103PushNorthCounter.InPressSpot"/>).</summary>
    private bool PnInPressSpot(Vector3 myPos, double reach)
    {
        CounterFront(myPos, out double frontDeg, out float limit);
        bool requiresFront = !(BossDataField != null && BossDataField.GetValue(_boss) is BossDataSO data) || data.counterRequiresFront;
        return QABoss103PushNorthCounter.InPressSpot(TrueSurfaceDistance(myPos), reach, frontDeg, limit, requiresFront);
    }

    /// <summary>북진 누름 자리로 걸어갈 표면 거리 — 기본 <see cref="CounterStandOff"/>, 누름 거리가 짧으면 그 절반.</summary>
    private static float PnStandOff(double reach) =>
        Mathf.Min(CounterStandOff, (float)(QABoss103PushNorthCounter.PressDistance(reach) * 0.5));

    /// <summary>누름 자리 대기 틱 — 이동 입력이 0 이면 멈춤 누적, 움직이면 초기화.</summary>
    private void NotePnStill(bool still, double now)
    {
        if (!still)
        {
            _pnStillTicks = 0;
            _pnStillSince = -1.0;
            return;
        }
        _pnStillTicks++;
        if (_pnStillSince < 0.0)
            _pnStillSince = now;
    }

    /// <summary>북진 성공 굴림 — 지금 누를까(<see cref="QABoss103PushNorthCounter.ShouldPress"/>). 시전 가능 상태가 아니면 마감 전엔 기다린다.</summary>
    private bool PnShouldPress(Player me, Vector3 myPos, double now)
    {
        PlayerSkillController skills = me.GetComponent<PlayerSkillController>();
        bool castable = skills != null && !skills.IsBusy &&
                        (me.CurrentState == PlayerActionState.Idle || me.CurrentState == PlayerActionState.Move);
        bool spot = castable && PnInPressSpot(myPos, CounterReach(me, skills));
        bool settled = QABoss103PushNorthCounter.IsSettled(_pnStillTicks, _pnStillSince >= 0.0 ? now - _pnStillSince : 0.0);
        return QABoss103PushNorthCounter.ShouldPress(now, _counterDeadline, spot, settled, castable && CounterReady(me, myPos));
    }

    /// <summary>북진 성공 굴림을 눌렀고(대쉬 간파 아님) 판정 + 잠깐 전이다 — 멈춰 있는다.</summary>
    private bool PnHoldsAfterPress(double now) =>
        _pnWindow && _pnTried && _pnCast && !_pnDash && _counterHit && !double.IsNaN(_pnJudgeAt) &&
        now < _pnJudgeAt + QABoss103PushNorthCounter.PostPressHoldSeconds;

    /// <summary>counter_try 북진 꼬리 — 누름 거리·멈춘 틱·시간.</summary>
    private string PnPressDiag(double reach, double now) =>
        $"pressM={F2(QABoss103PushNorthCounter.PressDistance(reach))} stillTicks={_pnStillTicks} " +
        $"stillMs={(_pnStillSince >= 0.0 ? F0((now - _pnStillSince) * 1000.0) : "na")}";

    /// <summary>④ 창 1개 결과 줄. <paramref name="closedAt"/> = 관측 닫힘(NaN = 못 봄 — 싸움에서 빠짐).</summary>
    private void EndPnWindow(double closedAt, string idleReason)
    {
        if (!_pnWindow)
            return;
        _pnWindow = false;
        _pnDashAt = -1.0;
        string reason = _pnReason
                        ?? (idleReason != null && !_pnTried ? "idle_" + QABoss103Plan.Token(idleReason)
                            : _pnTried ? (_counterHit ? "pressed" : "miss_roll_whiff") : "no_press");
        bool missRoll = _pnTried && !_counterHit;
        string result = QABoss103PushNorthCounter.Outcome(_pnTried, _pnCast, missRoll, _pnJudgeAt, closedAt, _pnCloseAt);
        string roll = !_pnRolled ? "na" : _counterHit ? "hit" : "miss";
        double openAt = _windowOpenAt;
        Debug.Log(QABoss103PushNorthCounter.FormatLine(Tag, _character.ToString(), _slot, _profile.Tier.ToString(), _pnOpenGameT, _pnOpenServerT,
            _pnCloseAt - openAt, _pnOpenDist, _pnOpenSurf, _pnReach, _pnSlack, roll, _pnTried, _pnDash, result, reason,
            _pnPressAt >= 0.0 ? (_pnPressAt - openAt) * 1000.0 : double.NaN, double.IsNaN(closedAt) ? double.NaN : (closedAt - openAt) * 1000.0));
    }

    private string PushNorthDiag() => $"pnWindows={_pnWindows} pnTries={_pnTries} pnDashTries={_pnDashTries}";
}
#endif
