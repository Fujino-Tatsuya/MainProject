using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 튜토리얼 진행 규칙(<see cref="TutorialFlowModel"/>) — 전원 판정·교착 방지·이탈/완전 사망 재판정·게이트 순서·중복 보고.
/// 원본: Docs/design/tutorial-scenario.md §2.1·§4. 교착 방지 길이는 튜닝값이라 테스트에선 임의값(Deadlock)을 쓴다.
/// </summary>
public sealed class TutorialFlowModelTests
{
    const double Deadlock = 60d;
    const ulong A = 0, B = 1, C = 2;

    static TutorialFlowModel NewModel(params ulong[] players)
    {
        var m = new TutorialFlowModel(Deadlock);
        foreach (ulong id in players) m.AddPlayer(id);
        return m;
    }

    // v8 G1 = 이동·대시·패시브 + 기본 공격·Q·E·R·우클릭 각 1회.
    static readonly TutorialCheck[] Zone1Checks =
    {
        TutorialCheck.Move, TutorialCheck.Dash, TutorialCheck.PassiveTooltip, TutorialCheck.BasicAttack,
        TutorialCheck.SkillQ, TutorialCheck.SkillE, TutorialCheck.SkillR, TutorialCheck.SkillInterrupt,
    };

    static void CompleteZone1(TutorialFlowModel m, ulong id)
    {
        foreach (TutorialCheck c in Zone1Checks) m.ReportCheck(id, c);
    }

    // 전원 존1 완료 → G1 열림(AllDone). 그다음 차례 = G2(복도1 특성).
    static void OpenG1(TutorialFlowModel m, double now, params ulong[] ids)
    {
        foreach (ulong id in ids) CompleteZone1(m, id);
        m.Evaluate(now);
        Assert.IsTrue(m.IsGateOpen(TutorialGate.G1_Corridor1Entry));
    }

    static List<(TutorialGate, TutorialGateOpenReason)> Record(TutorialFlowModel m)
    {
        var log = new List<(TutorialGate, TutorialGateOpenReason)>();
        m.GateOpened += (g, r) => log.Add((g, r));
        return log;
    }

    // ─── 전원 판정 ──────────────────────────────────────────────────────

    [Test]
    public void 전원_완료하면_G1_이_열린다()
    {
        var m = NewModel(A, B);
        var log = Record(m);
        CompleteZone1(m, A);
        CompleteZone1(m, B);
        m.Evaluate(1d);

        Assert.IsTrue(m.IsGateOpen(TutorialGate.G1_Corridor1Entry));
        CollectionAssert.AreEqual(new[] { (TutorialGate.G1_Corridor1Entry, TutorialGateOpenReason.AllDone) }, log);
    }

    [Test]
    public void 한_명이라도_미완료면_열리지_않는다()
    {
        var m = NewModel(A, B);
        CompleteZone1(m, A);
        m.Evaluate(1d);
        Assert.IsFalse(m.IsGateOpen(TutorialGate.G1_Corridor1Entry));
    }

    [Test]
    public void 존1_여덟_항목_중_하나라도_빠지면_미완료다()
    {
        foreach (TutorialCheck missing in Zone1Checks)
        {
            var m = NewModel(A);
            foreach (TutorialCheck c in Zone1Checks)
                if (c != missing) m.ReportCheck(A, c);
            m.Evaluate(0d);
            Assert.IsFalse(m.IsGateOpen(TutorialGate.G1_Corridor1Entry), $"{missing} 미완료");

            m.ReportCheck(A, missing);
            m.Evaluate(0d);
            Assert.IsTrue(m.IsGateOpen(TutorialGate.G1_Corridor1Entry), $"{missing} 완료 뒤");
        }
    }

    [Test]
    public void 공격_스킬_다섯_종은_G1_조건이다()
    {
        int g1 = TutorialFlowModel.RequiredMaskOf(TutorialGate.G1_Corridor1Entry);
        foreach (TutorialCheck c in new[] { TutorialCheck.BasicAttack, TutorialCheck.SkillQ, TutorialCheck.SkillE,
                                             TutorialCheck.SkillR, TutorialCheck.SkillInterrupt })
            Assert.AreNotEqual(0, g1 & (1 << (int)c), c.ToString());
        Assert.AreEqual(0, g1 & (1 << (int)TutorialCheck.Corridor1CooldownReset), "쿨 초기화 기록은 게이트 조건 아님");
        Assert.AreEqual(0, TutorialFlowModel.RequiredMaskOf(TutorialGate.G3_Corridor2Entry), "G3 은 존 장부(S4)가 연다");
    }

    [Test]
    public void 체크는_한_번_켜지면_되돌리지_않는다()
    {
        var m = NewModel(A);
        Assert.IsTrue(m.ReportCheck(A, TutorialCheck.SkillR));
        Assert.IsFalse(m.ReportCheck(A, TutorialCheck.SkillR), "중복");
        m.SetPermanentDead(A, true);
        m.SetPermanentDead(A, false);
        m.TryGetPlayer(A, out TutorialPlayerProgress p);
        Assert.IsTrue(p.Has(TutorialCheck.SkillR), "사망·부활로 지워지지 않는다");
    }

    // ─── 교착 방지 ──────────────────────────────────────────────────────

    [Test]
    public void G1_은_교착_방지로_열리지_않는다()
    {
        var m = NewModel(A, B);
        CompleteZone1(m, A);
        m.Evaluate(10d);
        m.Evaluate(10d + Deadlock * 100d);
        Assert.IsFalse(m.IsGateOpen(TutorialGate.G1_Corridor1Entry), "v8 — 시간 경과로 G1 을 열지 않는다");
        Assert.IsFalse(TutorialFlowModel.HasDeadlockTimeout(TutorialGate.G1_Corridor1Entry));
        Assert.IsTrue(TutorialFlowModel.HasDeadlockTimeout(TutorialGate.G2_Zone2Entry));

        Assert.IsTrue(m.TryGetPendingPersonalGate(out TutorialGate g, out int done, out int total, out double at));
        Assert.AreEqual((TutorialGate.G1_Corridor1Entry, 1, 2, -1d), (g, done, total, at), "대기 표시엔 자동 진행 시각이 없다");
    }

    [Test]
    public void 교착_방지는_첫_완료자_기준_정확히_경계에서_열린다()
    {
        var m = NewModel(A, B);
        var log = Record(m);
        OpenG1(m, 0d, A, B);
        m.ReportCheck(A, TutorialCheck.TraitCorridor1);
        m.Evaluate(10d);   // 첫 완료자 시각 = 10

        m.Evaluate(10d + Deadlock - 0.001d);
        Assert.IsFalse(m.IsGateOpen(TutorialGate.G2_Zone2Entry), "경계 직전");

        m.Evaluate(10d + Deadlock);
        Assert.IsTrue(m.IsGateOpen(TutorialGate.G2_Zone2Entry), "정확히 경계");
        Assert.AreEqual((TutorialGate.G2_Zone2Entry, TutorialGateOpenReason.Timeout), log[log.Count - 1]);
        Assert.AreNotEqual(0, m.TimeoutGateMask & (1 << (int)TutorialGate.G2_Zone2Entry));
    }

    [Test]
    public void 두_번째_완료자는_교착_방지_시계를_다시_시작하지_않는다()
    {
        var m = NewModel(A, B, C);
        OpenG1(m, 0d, A, B, C);
        m.ReportCheck(A, TutorialCheck.TraitCorridor1);
        m.Evaluate(10d);
        m.ReportCheck(B, TutorialCheck.TraitCorridor1);
        m.Evaluate(40d);

        m.Evaluate(10d + Deadlock);
        Assert.IsTrue(m.IsGateOpen(TutorialGate.G2_Zone2Entry));
    }

    [Test]
    public void 완료자가_없으면_아무리_지나도_열리지_않는다()
    {
        var m = NewModel(A, B);
        m.Evaluate(0d);
        m.Evaluate(Deadlock * 100d);
        Assert.IsFalse(m.IsGateOpen(TutorialGate.G1_Corridor1Entry));
    }

    [Test]
    public void 첫_완료자가_이탈해도_교착_방지_시계는_계속된다()
    {
        var m = NewModel(A, B);
        OpenG1(m, 0d, A, B);
        m.ReportCheck(A, TutorialCheck.TraitCorridor1);
        m.Evaluate(10d);
        m.MarkDisconnected(A);
        m.Evaluate(20d);
        Assert.IsFalse(m.IsGateOpen(TutorialGate.G2_Zone2Entry), "B 미완료");

        m.Evaluate(10d + Deadlock);
        Assert.IsTrue(m.IsGateOpen(TutorialGate.G2_Zone2Entry));
    }

    [Test]
    public void 앞_게이트_전에_미리_끝낸_보고는_차례가_온_시각부터_잰다()
    {
        var m = NewModel(A, B);
        m.ReportCheck(A, TutorialCheck.TraitCorridor1);   // G1 전에 미리(이론상) 완료
        m.Evaluate(0d);

        CompleteZone1(m, A);
        CompleteZone1(m, B);
        m.Evaluate(100d);   // G1 열림 → G2 차례, 이 시각이 첫 완료 기준
        Assert.IsTrue(m.IsGateOpen(TutorialGate.G1_Corridor1Entry));
        Assert.IsFalse(m.IsGateOpen(TutorialGate.G2_Zone2Entry));

        m.Evaluate(100d + Deadlock - 0.001d);
        Assert.IsFalse(m.IsGateOpen(TutorialGate.G2_Zone2Entry));
        m.Evaluate(100d + Deadlock);
        Assert.IsTrue(m.IsGateOpen(TutorialGate.G2_Zone2Entry));
    }

    // ─── 이탈 · 완전 사망 ────────────────────────────────────────────────

    [Test]
    public void 미완료자가_이탈하면_남은_인원_기준으로_즉시_열린다()
    {
        var m = NewModel(A, B);
        var log = Record(m);
        CompleteZone1(m, A);
        m.Evaluate(1d);
        m.MarkDisconnected(B);
        m.Evaluate(2d);

        Assert.IsTrue(m.IsGateOpen(TutorialGate.G1_Corridor1Entry));
        Assert.AreEqual(TutorialGateOpenReason.AllDone, log[0].Item2);
    }

    [Test]
    public void 완전_사망자는_전원에서_빠진다()
    {
        var m = NewModel(A, B);
        CompleteZone1(m, A);
        m.SetPermanentDead(B, true);
        m.Evaluate(1d);

        Assert.IsTrue(m.IsGateOpen(TutorialGate.G1_Corridor1Entry));
        Assert.AreEqual(1, m.ParticipantCount);
    }

    [Test]
    public void 영혼_상태는_전원에_포함된다_완전_사망만_뺀다()
    {
        // 모델은 PermanentDead 만 안다 — Soul·DeadPresentation 은 참가자 그대로(SetPermanentDead 를 부르지 않음).
        var m = NewModel(A, B);
        CompleteZone1(m, A);
        m.Evaluate(1d);
        Assert.IsFalse(m.IsGateOpen(TutorialGate.G1_Corridor1Entry));
        Assert.AreEqual(2, m.ParticipantCount);
    }

    [Test]
    public void 참가자가_0명이면_아무것도_자동으로_열지_않는다()
    {
        var m = NewModel(A);
        CompleteZone1(m, A);   // 완료했지만 판정 전에 이탈
        m.MarkDisconnected(A);
        m.Evaluate(1d);
        m.Evaluate(Deadlock * 10d);

        Assert.AreEqual(0, m.ParticipantCount);
        Assert.AreEqual(0, m.OpenedGateMask);
        Assert.IsFalse(m.TryGetPendingPersonalGate(out _, out _, out int total, out _) && total > 0);
    }

    [Test]
    public void 플레이어가_없는_모델도_수동_게이트는_열_수_있다()
    {
        var m = new TutorialFlowModel(Deadlock);
        Assert.IsTrue(m.TryOpenGate(TutorialGate.G1_Corridor1Entry));
    }

    // ─── 게이트 순서 ────────────────────────────────────────────────────

    [Test]
    public void 앞_게이트가_닫혀_있으면_뒤_게이트를_열_수_없다()
    {
        var m = NewModel(A);
        Assert.IsFalse(m.TryOpenGate(TutorialGate.G3_Corridor2Entry));
        Assert.IsFalse(m.IsGateOpen(TutorialGate.G3_Corridor2Entry));
    }

    [Test]
    public void 개인_게이트는_앞_게이트_전에는_전원_완료여도_열리지_않는다()
    {
        var m = NewModel(A);
        m.ReportCheck(A, TutorialCheck.TraitCorridor1);
        m.Evaluate(1d);
        Assert.IsFalse(m.IsGateOpen(TutorialGate.G2_Zone2Entry));

        CompleteZone1(m, A);
        m.Evaluate(2d);
        Assert.IsTrue(m.IsGateOpen(TutorialGate.G1_Corridor1Entry));
        Assert.IsTrue(m.IsGateOpen(TutorialGate.G2_Zone2Entry), "차례가 오면 같은 판정에서 연달아 열린다");
    }

    [Test]
    public void 수동_게이트는_서버가_열_때까지_뒤를_막는다()
    {
        var m = NewModel(A);
        CompleteZone1(m, A);
        m.ReportCheck(A, TutorialCheck.TraitCorridor1);
        m.ReportCheck(A, TutorialCheck.TraitCorridor2);
        m.Evaluate(1d);
        Assert.IsTrue(m.IsGateOpen(TutorialGate.G2_Zone2Entry));
        Assert.IsFalse(m.IsGateOpen(TutorialGate.G4_Zone3Entry), "G3(존2 전멸) 전");

        Assert.IsTrue(m.TryOpenGate(TutorialGate.G3_Corridor2Entry));
        m.Evaluate(2d);
        Assert.IsTrue(m.IsGateOpen(TutorialGate.G4_Zone3Entry));
    }

    [Test]
    public void G11_보스방_도착은_G10_전에는_열리지_않는_수동_게이트다()
    {
        Assert.AreEqual(11, TutorialFlowModel.GateCount, "G11 이 마지막 게이트");
        Assert.IsFalse(TutorialFlowModel.IsPersonalGate(TutorialGate.G11_BossRoom), "개인 체크 게이트 아님");
        Assert.IsFalse(TutorialFlowModel.TryGetGateCounter(TutorialGate.G11_BossRoom, out _), "카운터 게이트 아님");

        var m = NewModel(A);
        for (int i = 0; i < (int)TutorialGate.G10_BossEnterAndPad; i++) Assert.IsTrue(m.TryOpenGate((TutorialGate)i));
        Assert.IsFalse(m.IsGateEligible(TutorialGate.G11_BossRoom));
        Assert.IsFalse(m.TryOpenGate(TutorialGate.G11_BossRoom), "G10 전(타이머 만료 강제 이동 도착) = 거부");

        Assert.IsTrue(m.TryOpenGate(TutorialGate.G10_BossEnterAndPad));
        m.Evaluate(1d);
        Assert.IsFalse(m.IsGateOpen(TutorialGate.G11_BossRoom), "수동 게이트 — Evaluate 가 자동으로 열지 않는다");
        Assert.IsFalse(m.TryGetPendingPersonalGate(out _, out _, out _, out _), "G10 뒤 대기 표시 없음");

        var log = Record(m);
        Assert.IsTrue(m.TryOpenGate(TutorialGate.G11_BossRoom));
        Assert.IsTrue(m.IsGateOpen(TutorialGate.G11_BossRoom));
        CollectionAssert.AreEqual(new[] { (TutorialGate.G11_BossRoom, TutorialGateOpenReason.Manual) }, log);
    }

    [Test]
    public void 연계_합계가_목표에_닿아도_G5_는_복귀_완료_때_흐름이_연다()
    {
        var m = NewModel(A);
        var log = Record(m);
        for (int g = 0; g <= (int)TutorialGate.G4_Zone3Entry; g++) m.TryOpenGate((TutorialGate)g);

        m.AddCounter(TutorialCounter.Zone3Interrupts, 3);
        m.Evaluate(2d);
        Assert.AreEqual(3, m.GetCounter(TutorialCounter.Zone3Interrupts));
        Assert.IsFalse(m.IsGateOpen(TutorialGate.G5_SpinnerRealMode), "v8 — 마지막 원위치 복귀 전");
        Assert.IsFalse(TutorialFlowModel.TryGetGateCounter(TutorialGate.G5_SpinnerRealMode, out _));

        Assert.IsTrue(m.RequestGate(TutorialGate.G5_SpinnerRealMode), "복귀 완료 → 존3 흐름이 연다");
        Assert.AreEqual((TutorialGate.G5_SpinnerRealMode, TutorialGateOpenReason.Manual), log[log.Count - 1]);
    }

    [Test]
    public void 이미_열린_게이트는_다시_열리지_않는다()
    {
        var m = NewModel(A);
        var log = Record(m);
        Assert.IsTrue(m.TryOpenGate(TutorialGate.G1_Corridor1Entry));
        Assert.IsFalse(m.TryOpenGate(TutorialGate.G1_Corridor1Entry));
        Assert.AreEqual(1, log.Count);
    }

    // ─── 중복 보고 · 카운터 · 단계 ─────────────────────────────────────────

    [Test]
    public void 같은_보고는_한_번만_센다()
    {
        var m = NewModel(A);
        Assert.IsTrue(m.ReportCheck(A, TutorialCheck.Dash));
        Assert.IsFalse(m.ReportCheck(A, TutorialCheck.Dash));
    }

    [Test]
    public void 기본_공격은_한_번이면_완료다()
    {
        var m = NewModel(A);
        Assert.IsTrue(m.ReportCheck(A, TutorialCheck.BasicAttack));
        Assert.IsFalse(m.ReportCheck(A, TutorialCheck.BasicAttack), "완료 뒤 보고는 무시");
        m.TryGetPlayer(A, out TutorialPlayerProgress p);
        Assert.IsTrue(p.Has(TutorialCheck.BasicAttack));
    }

    [Test]
    public void 모르는_client_의_보고는_버린다()
    {
        var m = NewModel(A);
        Assert.IsFalse(m.ReportCheck(99, TutorialCheck.Move));
        Assert.IsFalse(m.TryGetPlayer(99, out _));
    }

    [Test]
    public void 이탈한_id_는_다시_등록되지_않는다()
    {
        var m = NewModel(A);
        m.MarkDisconnected(A);
        Assert.IsFalse(m.AddPlayer(A));
        Assert.AreEqual(0, m.ParticipantCount);
    }

    [Test]
    public void 카운터는_목표에서_멈춘다()
    {
        var m = NewModel(A);
        Assert.IsTrue(m.AddCounter(TutorialCounter.Zone4Panels, 3));
        Assert.IsTrue(m.AddCounter(TutorialCounter.Zone4Panels, 5));
        Assert.AreEqual(4, m.GetCounter(TutorialCounter.Zone4Panels));
        Assert.IsFalse(m.AddCounter(TutorialCounter.Zone4Panels));
        Assert.IsFalse(m.AddCounter(TutorialCounter.Zone2Kills, 0));
    }

    [Test]
    public void 단계는_앞으로만_간다()
    {
        var m = NewModel(A);
        var steps = new List<TutorialStep>();
        m.StepChanged += (_, to) => steps.Add(to);

        Assert.IsTrue(m.SetStep(TutorialStep.Zone2));
        Assert.IsFalse(m.SetStep(TutorialStep.Zone2));
        Assert.IsFalse(m.SetStep(TutorialStep.Zone1));
        CollectionAssert.AreEqual(new[] { TutorialStep.Zone2 }, steps);
    }

    // ─── 대기 표시 ──────────────────────────────────────────────────────

    [Test]
    public void 대기_표시는_완료_수_전원_수_자동_진행_시각을_준다()
    {
        var m = NewModel(A, B, C);
        Assert.IsTrue(m.TryGetPendingPersonalGate(out TutorialGate g, out int done, out int total, out double at));
        Assert.AreEqual(TutorialGate.G1_Corridor1Entry, g);
        Assert.AreEqual((0, 3, -1d), (done, total, at));

        CompleteZone1(m, A);
        m.Evaluate(5d);
        m.TryGetPendingPersonalGate(out _, out done, out total, out at);
        Assert.AreEqual((1, 3, -1d), (done, total, at), "G1 은 자동 진행 없음");

        m.SetPermanentDead(C, true);
        m.TryGetPendingPersonalGate(out _, out done, out total, out _);
        Assert.AreEqual((1, 2), (done, total));

        // 특성 게이트(G2)는 자동 진행 시각을 준다.
        CompleteZone1(m, B);
        m.Evaluate(6d);
        m.ReportCheck(A, TutorialCheck.TraitCorridor1);
        m.Evaluate(7d);
        Assert.IsTrue(m.TryGetPendingPersonalGate(out g, out done, out total, out at));
        Assert.AreEqual((TutorialGate.G2_Zone2Entry, 1, 2, 7d + Deadlock), (g, done, total, at));
    }

    [Test]
    public void 수동_게이트_차례에는_대기_표시가_없다()
    {
        var m = NewModel(A);
        m.TryOpenGate(TutorialGate.G1_Corridor1Entry);
        m.TryOpenGate(TutorialGate.G2_Zone2Entry);
        Assert.IsFalse(m.TryGetPendingPersonalGate(out _, out _, out _, out _), "G3 차례(수동)");
    }
}
