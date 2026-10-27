using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 게이트 보류(<see cref="MapSceneFlowModel.HoldGate"/>) — 4구역 소개 연출이 끝나야 G7 을 연다(PLAN-zone4-intro-cinematic D2).
/// 보류 중 열 조건(전원 완료·교착 방지)이 차면 열지 않고 사유를 기억 → 풀 때 그 사유로 연다. 직접 열기(디버그·강제)는 막지 않는다.
/// </summary>
public sealed class MapSceneGateHoldTests
{
    const double Deadlock = 60d;
    const ulong A = 0, B = 1;

    static MapSceneFlowModel G7Turn(params ulong[] players)
    {
        var m = new MapSceneFlowModel(Deadlock);
        foreach (ulong id in players) m.AddPlayer(id);
        for (var g = MapSceneGate.G1_Corridor1Entry; g < MapSceneGate.G7_Zone4Entry; g++)
            Assert.IsTrue(m.TryOpenGate(g), g.ToString());
        return m;
    }

    [Test]
    public void 보류_중_전원_완료면_열지_않고_준비됨_한_번()
    {
        MapSceneFlowModel m = G7Turn(A, B);
        Assert.IsTrue(m.HoldGate(MapSceneGate.G7_Zone4Entry));
        var ready = new List<(MapSceneGate, MapSceneGateOpenReason)>();
        m.GateReadyWhileHeld += (g, r) => ready.Add((g, r));

        m.ReportCheck(A, MapSceneCheck.TraitCorridor3);
        m.ReportCheck(B, MapSceneCheck.TraitCorridor3);
        m.Evaluate(1d);
        m.Evaluate(2d);

        Assert.IsFalse(m.IsGateOpen(MapSceneGate.G7_Zone4Entry));
        Assert.IsTrue(m.IsGateReadyHeld(MapSceneGate.G7_Zone4Entry));
        Assert.AreEqual(1, ready.Count);
        Assert.AreEqual((MapSceneGate.G7_Zone4Entry, MapSceneGateOpenReason.AllDone), ready[0]);
    }

    [Test]
    public void 풀면_기억한_사유로_연다_교착_방지면_Timeout()
    {
        MapSceneFlowModel m = G7Turn(A, B);
        m.HoldGate(MapSceneGate.G7_Zone4Entry);
        var opened = new List<(MapSceneGate, MapSceneGateOpenReason)>();
        m.GateOpened += (g, r) => opened.Add((g, r));

        m.ReportCheck(A, MapSceneCheck.TraitCorridor3);   // B 는 안 고름
        m.Evaluate(0d);
        m.Evaluate(Deadlock);
        Assert.IsTrue(m.IsGateReadyHeld(MapSceneGate.G7_Zone4Entry));
        Assert.IsEmpty(opened);

        Assert.IsTrue(m.ReleaseHeldGate(MapSceneGate.G7_Zone4Entry));
        Assert.IsTrue(m.IsGateOpen(MapSceneGate.G7_Zone4Entry));
        Assert.AreEqual((MapSceneGate.G7_Zone4Entry, MapSceneGateOpenReason.Timeout), opened[0]);
        Assert.AreNotEqual(0, m.TimeoutGateMask & (1 << (int)MapSceneGate.G7_Zone4Entry));
    }

    [Test]
    public void 조건_전에_풀면_보류만_풀리고_다음_평가가_연다()
    {
        MapSceneFlowModel m = G7Turn(A);
        m.HoldGate(MapSceneGate.G7_Zone4Entry);
        Assert.IsFalse(m.ReleaseHeldGate(MapSceneGate.G7_Zone4Entry));
        Assert.IsFalse(m.IsGateHeld(MapSceneGate.G7_Zone4Entry));

        m.ReportCheck(A, MapSceneCheck.TraitCorridor3);
        m.Evaluate(0d);
        Assert.IsTrue(m.IsGateOpen(MapSceneGate.G7_Zone4Entry));
    }

    [Test]
    public void 직접_열기는_보류와_무관하게_열고_보류를_지운다()
    {
        MapSceneFlowModel m = G7Turn(A);
        m.HoldGate(MapSceneGate.G7_Zone4Entry);
        m.ReportCheck(A, MapSceneCheck.TraitCorridor3);
        m.Evaluate(0d);

        Assert.IsTrue(m.TryOpenGate(MapSceneGate.G7_Zone4Entry));
        Assert.IsFalse(m.IsGateHeld(MapSceneGate.G7_Zone4Entry));
        Assert.IsFalse(m.IsGateReadyHeld(MapSceneGate.G7_Zone4Entry));
        Assert.IsFalse(m.ReleaseHeldGate(MapSceneGate.G7_Zone4Entry));   // 이미 열림 — 두 번 열지 않는다
    }

    [Test]
    public void 보류_중이면_뒤_게이트도_열리지_않는다()
    {
        MapSceneFlowModel m = G7Turn(A);
        m.HoldGate(MapSceneGate.G7_Zone4Entry);
        m.RequestGate(MapSceneGate.G9_Corridor4Entry);   // 존4 클리어가 먼저 와도
        m.ReportCheck(A, MapSceneCheck.TraitCorridor3);
        m.Evaluate(0d);
        Assert.IsFalse(m.IsGateOpen(MapSceneGate.G8_TimerStart));
        Assert.IsFalse(m.IsGateOpen(MapSceneGate.G9_Corridor4Entry));
    }

    [Test]
    public void 준비된_보류_게이트는_동료_대기로_보이지_않는다()
    {
        MapSceneFlowModel m = G7Turn(A);
        m.HoldGate(MapSceneGate.G7_Zone4Entry);
        Assert.IsTrue(m.TryGetPendingPersonalGate(out MapSceneGate pending, out _, out _, out _));
        Assert.AreEqual(MapSceneGate.G7_Zone4Entry, pending);

        m.ReportCheck(A, MapSceneCheck.TraitCorridor3);
        m.Evaluate(0d);
        Assert.IsFalse(m.TryGetPendingPersonalGate(out _, out _, out _, out _));
    }
}
