using NUnit.Framework;

/// <summary>전멸 리스폰(할 일 30 (2)) 튜토리얼 쪽 — 보스 재도전 되감기·뒤 배리어 마스크 재계산. 원본: Docs/tech/party-lives-respawn.md §3.5·§3.6.</summary>
public sealed class MapSceneBossRetryTests
{
    static MapSceneFlowModel ArrivedModel()
    {
        var m = new MapSceneFlowModel();
        m.AddPlayer(1);
        for (int g = 0; g < MapSceneFlowModel.GateCount; g++) Assert.IsTrue(m.TryOpenGate((MapSceneGate)g));
        m.SetStep(MapSceneStep.Finished);
        return m;
    }

    [Test]
    public void Rewind_FinishedToBossEnter_ClosesOnlyG11()
    {
        MapSceneFlowModel m = ArrivedModel();
        MapSceneStep from = MapSceneStep.None, to = MapSceneStep.None;
        m.StepChanged += (a, b) => { from = a; to = b; };

        Assert.IsTrue(m.RewindForBossRetry());
        Assert.AreEqual(MapSceneStep.BossEnter, m.Step);
        Assert.AreEqual(MapSceneStep.Finished, from);
        Assert.AreEqual(MapSceneStep.BossEnter, to);
        Assert.IsFalse(m.IsGateOpen(MapSceneGate.G11_BossRoom));
        for (int g = 0; g <= (int)MapSceneGate.G10_BossEnterAndPad; g++)
            Assert.IsTrue(m.IsGateOpen((MapSceneGate)g), $"{(MapSceneGate)g} 는 열린 채여야 한다");
    }

    [Test]
    public void Rewind_LifeChargeResumesOnlyAfterReArrival()
    {
        MapSceneFlowModel m = ArrivedModel();
        Assert.IsTrue(MapSceneDeathRulesModel.IsLifeChargedAt(m.OpenedGateMask));

        m.RewindForBossRetry();
        Assert.IsFalse(MapSceneDeathRulesModel.IsLifeChargedAt(m.OpenedGateMask), "리스폰 뒤 보스방 재도착 전 사망은 차감 없음");

        Assert.IsTrue(m.TryOpenGate(MapSceneGate.G11_BossRoom));
        Assert.IsTrue(m.SetStep(MapSceneStep.Finished));
        Assert.IsTrue(MapSceneDeathRulesModel.IsLifeChargedAt(m.OpenedGateMask), "보스방 재도착 뒤 사망은 다시 차감");
    }

    [Test]
    public void Rewind_G10Closed_Refused()
    {
        var m = new MapSceneFlowModel();
        m.AddPlayer(1);
        for (int g = 0; g < (int)MapSceneGate.G10_BossEnterAndPad; g++) m.TryOpenGate((MapSceneGate)g);
        m.SetStep(MapSceneStep.Finished);   // 만료 강제 이동으로 G10 전에 도착한 경우

        Assert.IsFalse(m.RewindForBossRetry());
        Assert.AreEqual(MapSceneStep.Finished, m.Step);
    }

    [Test]
    public void Rewind_Twice_SecondIsNoChange()
    {
        MapSceneFlowModel m = ArrivedModel();
        Assert.IsTrue(m.RewindForBossRetry());
        Assert.IsFalse(m.RewindForBossRetry());
    }

    [Test]
    public void BackBarrierMask_Corridor4Frontmost_DropsG10KeepsG9()
    {
        int mask = MapSceneBackBarrierRules.MaskWithFrontmost(MapSceneArea.Corridor4);
        Assert.AreEqual(MapSceneArea.Corridor4, MapSceneBackBarrierRules.FrontmostPassedArea(mask));
        Assert.IsFalse(MapSceneBackBarrierRules.Has(mask, MapSceneGate.G10_BossEnterAndPad));
        Assert.IsTrue(MapSceneBackBarrierRules.Has(mask, MapSceneGate.G9_Corridor4Entry));
        Assert.IsTrue(MapSceneBackBarrierRules.Has(mask, MapSceneGate.G1_Corridor1Entry));
        Assert.IsFalse(MapSceneBackBarrierRules.IsAreaBehind(MapSceneArea.Corridor4, mask));
        Assert.IsFalse(MapSceneBackBarrierRules.IsAreaBehind(MapSceneArea.BossEnter, mask), "앞길(보스 진입 존)은 뒤가 아니다");
        Assert.IsTrue(MapSceneBackBarrierRules.IsAreaBehind(MapSceneArea.Zone4, mask));
    }

    [Test]
    public void BackBarrierMask_LocalCollidersAfterRewind_NoG10()
    {
        // G10 을 지났던 사람(전체 비트) → 리스폰 마스크. 되감기 뒤(튜토리얼 진행 중) 로컬 뒤 배리어에 G10 이 없어야 앞길이 열린다.
        int allOpen = (1 << MapSceneFlowModel.GateCount) - 1 & ~(1 << (int)MapSceneGate.G11_BossRoom);
        int before = MapSceneBackBarrierRules.AllBarrierMask;
        Assert.IsTrue(MapSceneBackBarrierRules.Has(MapSceneBackBarrierRules.LocalColliderMask(before, allOpen, true), MapSceneGate.G10_BossEnterAndPad));

        int after = MapSceneBackBarrierRules.MaskWithFrontmost(MapSceneArea.Corridor4);
        int local = MapSceneBackBarrierRules.LocalColliderMask(after, allOpen, true);
        Assert.IsFalse(MapSceneBackBarrierRules.Has(local, MapSceneGate.G10_BossEnterAndPad));
        Assert.IsTrue(MapSceneBackBarrierRules.Has(local, MapSceneGate.G9_Corridor4Entry));
    }

    [Test]
    public void BackBarrierMask_RevivePointAfterRespawn_StaysCorridor4()
    {
        // 리스폰 뒤 G11 전 사망 — 부활 지점 구간이 복도4(패드 존 아님)부터, 복도4 아래로는 안 내려간다.
        int opened = (1 << ((int)MapSceneGate.G10_BossEnterAndPad + 1)) - 1;
        int passed = MapSceneBackBarrierRules.MaskWithFrontmost(MapSceneArea.Corridor4);
        var order = MapSceneRevivePointRules.AreaOrder(MapSceneStep.BossEnter, opened, passed);
        CollectionAssert.AreEqual(new[] { MapSceneArea.Corridor4 }, order);
    }

    [Test]
    public void BackBarrierMask_Zone1_IsEmpty()
    {
        Assert.AreEqual(0, MapSceneBackBarrierRules.MaskWithFrontmost(MapSceneArea.Zone1));
    }
}
