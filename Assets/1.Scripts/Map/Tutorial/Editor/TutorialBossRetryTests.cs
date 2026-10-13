using NUnit.Framework;

/// <summary>전멸 리스폰(할 일 30 (2)) 튜토리얼 쪽 — 보스 재도전 되감기·뒤 배리어 마스크 재계산. 원본: Docs/tech/party-lives-respawn.md §3.5·§3.6.</summary>
public sealed class TutorialBossRetryTests
{
    static TutorialFlowModel ArrivedModel()
    {
        var m = new TutorialFlowModel();
        m.AddPlayer(1);
        for (int g = 0; g < TutorialFlowModel.GateCount; g++) Assert.IsTrue(m.TryOpenGate((TutorialGate)g));
        m.SetStep(TutorialStep.Finished);
        return m;
    }

    [Test]
    public void Rewind_FinishedToBossEnter_ClosesOnlyG11()
    {
        TutorialFlowModel m = ArrivedModel();
        TutorialStep from = TutorialStep.None, to = TutorialStep.None;
        m.StepChanged += (a, b) => { from = a; to = b; };

        Assert.IsTrue(m.RewindForBossRetry());
        Assert.AreEqual(TutorialStep.BossEnter, m.Step);
        Assert.AreEqual(TutorialStep.Finished, from);
        Assert.AreEqual(TutorialStep.BossEnter, to);
        Assert.IsFalse(m.IsGateOpen(TutorialGate.G11_BossRoom));
        for (int g = 0; g <= (int)TutorialGate.G10_BossEnterAndPad; g++)
            Assert.IsTrue(m.IsGateOpen((TutorialGate)g), $"{(TutorialGate)g} 는 열린 채여야 한다");
    }

    [Test]
    public void Rewind_LifeChargeResumesOnlyAfterReArrival()
    {
        TutorialFlowModel m = ArrivedModel();
        Assert.IsTrue(TutorialDeathRulesModel.IsLifeChargedAt(m.OpenedGateMask));

        m.RewindForBossRetry();
        Assert.IsFalse(TutorialDeathRulesModel.IsLifeChargedAt(m.OpenedGateMask), "리스폰 뒤 보스방 재도착 전 사망은 차감 없음");

        Assert.IsTrue(m.TryOpenGate(TutorialGate.G11_BossRoom));
        Assert.IsTrue(m.SetStep(TutorialStep.Finished));
        Assert.IsTrue(TutorialDeathRulesModel.IsLifeChargedAt(m.OpenedGateMask), "보스방 재도착 뒤 사망은 다시 차감");
    }

    [Test]
    public void Rewind_G10Closed_Refused()
    {
        var m = new TutorialFlowModel();
        m.AddPlayer(1);
        for (int g = 0; g < (int)TutorialGate.G10_BossEnterAndPad; g++) m.TryOpenGate((TutorialGate)g);
        m.SetStep(TutorialStep.Finished);   // 만료 강제 이동으로 G10 전에 도착한 경우

        Assert.IsFalse(m.RewindForBossRetry());
        Assert.AreEqual(TutorialStep.Finished, m.Step);
    }

    [Test]
    public void Rewind_Twice_SecondIsNoChange()
    {
        TutorialFlowModel m = ArrivedModel();
        Assert.IsTrue(m.RewindForBossRetry());
        Assert.IsFalse(m.RewindForBossRetry());
    }

    [Test]
    public void BackBarrierMask_Corridor4Frontmost_DropsG10KeepsG9()
    {
        int mask = TutorialBackBarrierRules.MaskWithFrontmost(TutorialArea.Corridor4);
        Assert.AreEqual(TutorialArea.Corridor4, TutorialBackBarrierRules.FrontmostPassedArea(mask));
        Assert.IsFalse(TutorialBackBarrierRules.Has(mask, TutorialGate.G10_BossEnterAndPad));
        Assert.IsTrue(TutorialBackBarrierRules.Has(mask, TutorialGate.G9_Corridor4Entry));
        Assert.IsTrue(TutorialBackBarrierRules.Has(mask, TutorialGate.G1_Corridor1Entry));
        Assert.IsFalse(TutorialBackBarrierRules.IsAreaBehind(TutorialArea.Corridor4, mask));
        Assert.IsFalse(TutorialBackBarrierRules.IsAreaBehind(TutorialArea.BossEnter, mask), "앞길(보스 진입 존)은 뒤가 아니다");
        Assert.IsTrue(TutorialBackBarrierRules.IsAreaBehind(TutorialArea.Zone4, mask));
    }

    [Test]
    public void BackBarrierMask_LocalCollidersAfterRewind_NoG10()
    {
        // G10 을 지났던 사람(전체 비트) → 리스폰 마스크. 되감기 뒤(튜토리얼 진행 중) 로컬 뒤 배리어에 G10 이 없어야 앞길이 열린다.
        int allOpen = (1 << TutorialFlowModel.GateCount) - 1 & ~(1 << (int)TutorialGate.G11_BossRoom);
        int before = TutorialBackBarrierRules.AllBarrierMask;
        Assert.IsTrue(TutorialBackBarrierRules.Has(TutorialBackBarrierRules.LocalColliderMask(before, allOpen, true), TutorialGate.G10_BossEnterAndPad));

        int after = TutorialBackBarrierRules.MaskWithFrontmost(TutorialArea.Corridor4);
        int local = TutorialBackBarrierRules.LocalColliderMask(after, allOpen, true);
        Assert.IsFalse(TutorialBackBarrierRules.Has(local, TutorialGate.G10_BossEnterAndPad));
        Assert.IsTrue(TutorialBackBarrierRules.Has(local, TutorialGate.G9_Corridor4Entry));
    }

    [Test]
    public void BackBarrierMask_RevivePointAfterRespawn_StaysCorridor4()
    {
        // 리스폰 뒤 G11 전 사망 — 부활 지점 구간이 복도4(패드 존 아님)부터, 복도4 아래로는 안 내려간다.
        int opened = (1 << ((int)TutorialGate.G10_BossEnterAndPad + 1)) - 1;
        int passed = TutorialBackBarrierRules.MaskWithFrontmost(TutorialArea.Corridor4);
        var order = TutorialRevivePointRules.AreaOrder(TutorialStep.BossEnter, opened, passed);
        CollectionAssert.AreEqual(new[] { TutorialArea.Corridor4 }, order);
    }

    [Test]
    public void BackBarrierMask_Zone1_IsEmpty()
    {
        Assert.AreEqual(0, TutorialBackBarrierRules.MaskWithFrontmost(TutorialArea.Zone1));
    }
}
