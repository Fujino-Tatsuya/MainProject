using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 개인 뒤 배리어 순수 규칙(<see cref="MapSceneBackBarrierRules"/>) — 너머 구간, 지남 판정(앞면·여유·너머 구간),
/// 마스크 갱신(열린 게이트만·유지), 로컬 콜라이더 목록(열린 것만·진행 중만), 켜기/끄기 차이.
/// 원본: Docs/tech/tutorial-director.md §7. 스테이지는 <see cref="MapSceneBarrierRulesTests"/> 와 같은 임의 좌표.
/// </summary>
public sealed class MapSceneBackBarrierRulesTests
{
    const float Clear = MapSceneBackBarrierRules.DefaultClearance;

    static int Mask(params MapSceneGate[] gates)
    {
        int m = 0;
        foreach (MapSceneGate g in gates) m |= 1 << (int)g;
        return m;
    }

    // 존1 x 0~10 · 복도1 x 10~20(+x) · 존2 x 20~30 · 복도2 z 5~15(+z) · 존3 z 15~25 · 복도3 x 10~20(−x) · 존4 x 0~10 · 복도4 x −10~0 · 보스 진입
    static MapSceneAreaTracker NewStage()
    {
        var t = new MapSceneAreaTracker();
        t.SetArea(MapSceneArea.Zone1, MapSceneRect.FromMinMax(0f, -5f, 10f, 5f));
        t.SetArea(MapSceneArea.Corridor1, MapSceneRect.FromMinMax(10f, -2f, 20f, 2f));
        t.SetArea(MapSceneArea.Zone2, MapSceneRect.FromMinMax(20f, -5f, 30f, 5f));
        t.SetArea(MapSceneArea.Corridor2, MapSceneRect.FromMinMax(23f, 5f, 27f, 15f));
        t.SetArea(MapSceneArea.Zone3, MapSceneRect.FromMinMax(20f, 15f, 30f, 25f));
        t.SetArea(MapSceneArea.Corridor3, MapSceneRect.FromMinMax(10f, 18f, 20f, 22f));
        t.SetArea(MapSceneArea.Zone4, MapSceneRect.FromMinMax(0f, 15f, 10f, 25f));
        t.SetArea(MapSceneArea.Corridor4, MapSceneRect.FromMinMax(-10f, 18f, 0f, 22f));
        t.SetArea(MapSceneArea.BossEnter, MapSceneRect.FromMinMax(-20f, 15f, -10f, 25f));
        return t;
    }

    static List<MapSceneBarrierSpec> Specs(MapSceneAreaTracker t) =>
        MapSceneBarrierRules.BuildSpecs(t, MapSceneBarrierRules.DefaultThickness, MapSceneBarrierRules.DefaultSideOverlap, out _);

    static MapSceneBarrierSpec Find(List<MapSceneBarrierSpec> specs, MapSceneGate g)
    {
        foreach (MapSceneBarrierSpec s in specs) if (s.Gate == g) return s;
        Assert.Fail($"{g} 배리어 없음");
        return default;
    }

    [Test]
    public void 너머_구간_입구는_복도_출구는_다음_존()
    {
        List<MapSceneBarrierSpec> s = Specs(NewStage());
        Assert.AreEqual(MapSceneArea.Corridor1, MapSceneBackBarrierRules.AreaBeyond(Find(s, MapSceneGate.G1_Corridor1Entry)));
        Assert.AreEqual(MapSceneArea.Zone2, MapSceneBackBarrierRules.AreaBeyond(Find(s, MapSceneGate.G2_Zone2Entry)));
        Assert.AreEqual(MapSceneArea.Corridor3, MapSceneBackBarrierRules.AreaBeyond(Find(s, MapSceneGate.G6_Corridor3Entry)));
        Assert.AreEqual(MapSceneArea.BossEnter, MapSceneBackBarrierRules.AreaBeyond(Find(s, MapSceneGate.G10_BossEnterAndPad)));
    }

    [Test]
    public void 스펙에_진행_방향이_실린다()
    {
        List<MapSceneBarrierSpec> s = Specs(NewStage());
        MapSceneBarrierSpec g1 = Find(s, MapSceneGate.G1_Corridor1Entry);
        Assert.IsTrue(g1.AlongX); Assert.AreEqual(1f, g1.Forward);
        MapSceneBarrierSpec g3 = Find(s, MapSceneGate.G3_Corridor2Entry);
        Assert.IsFalse(g3.AlongX); Assert.AreEqual(1f, g3.Forward);
        MapSceneBarrierSpec g6 = Find(s, MapSceneGate.G6_Corridor3Entry);
        Assert.IsTrue(g6.AlongX); Assert.AreEqual(-1f, g6.Forward, "복도3 은 −x 로 나아간다");
    }

    [Test]
    public void 앞면에서_여유만큼_넘어가야_지남()
    {
        MapSceneAreaTracker t = NewStage();
        MapSceneBarrierSpec g1 = Find(Specs(t), MapSceneGate.G1_Corridor1Entry);   // 박스 x 10~10.4, 앞면 10.4
        Assert.AreEqual(0.6f, MapSceneBackBarrierRules.DistancePastFace(g1, 11f, 0f), 1e-4f);
        Assert.Less(MapSceneBackBarrierRules.DistancePastFace(g1, 5f, 0f), 0f, "존1 쪽은 음수");

        Assert.IsFalse(MapSceneBackBarrierRules.HasPassed(t, g1, 10.2f, 0f, Clear), "배리어 박스 위");
        Assert.IsFalse(MapSceneBackBarrierRules.HasPassed(t, g1, 10.4f + Clear - 0.01f, 0f, Clear), "여유 미만");
        Assert.IsTrue(MapSceneBackBarrierRules.HasPassed(t, g1, 10.4f + Clear + 0.01f, 0f, Clear));
        Assert.IsTrue(MapSceneBackBarrierRules.HasPassed(t, g1, 19f, 0f, Clear), "복도 깊이 들어가도 지남");
    }

    [Test]
    public void 너머_구간_밖이면_지나지_않음()
    {
        MapSceneAreaTracker t = NewStage();
        MapSceneBarrierSpec g1 = Find(Specs(t), MapSceneGate.G1_Corridor1Entry);
        Assert.IsFalse(MapSceneBackBarrierRules.HasPassed(t, g1, 25f, 0f, Clear), "존2 는 G1 너머 구간(복도1)이 아니다");
        Assert.IsFalse(MapSceneBackBarrierRules.HasPassed(t, g1, 15f, 4f, Clear), "복도 옆(벽 밖)");
        Assert.IsFalse(MapSceneBackBarrierRules.HasPassed(null, g1, 15f, 0f, Clear));
    }

    [Test]
    public void 출구_배리어는_다음_존_안쪽에서_지남()
    {
        MapSceneAreaTracker t = NewStage();
        MapSceneBarrierSpec g2 = Find(Specs(t), MapSceneGate.G2_Zone2Entry);   // 박스 x 19.6~20, 앞면 20
        Assert.IsFalse(MapSceneBackBarrierRules.HasPassed(t, g2, 19f, 0f, Clear), "아직 복도");
        Assert.IsFalse(MapSceneBackBarrierRules.HasPassed(t, g2, 20.5f, 0f, Clear), "여유 미만");
        Assert.IsTrue(MapSceneBackBarrierRules.HasPassed(t, g2, 21f, 0f, Clear));
    }

    [Test]
    public void 역방향_복도도_진행_방향으로_판정()
    {
        MapSceneAreaTracker t = NewStage();
        MapSceneBarrierSpec g6 = Find(Specs(t), MapSceneGate.G6_Corridor3Entry);   // 박스 x 19.6~20, 앞면 19.6(−x)
        Assert.IsFalse(MapSceneBackBarrierRules.HasPassed(t, g6, 19.2f, 20f, Clear));
        Assert.IsTrue(MapSceneBackBarrierRules.HasPassed(t, g6, 18.5f, 20f, Clear));
        MapSceneBarrierSpec g7 = Find(Specs(t), MapSceneGate.G7_Zone4Entry);       // 앞면 x 10, 존4 = x 0~10
        Assert.IsTrue(MapSceneBackBarrierRules.HasPassed(t, g7, 9f, 20f, Clear));
        Assert.IsFalse(MapSceneBackBarrierRules.HasPassed(t, g7, 11f, 20f, Clear));
    }

    [Test]
    public void 마스크는_열린_게이트만_켜고_유지한다()
    {
        MapSceneAreaTracker t = NewStage();
        List<MapSceneBarrierSpec> s = Specs(t);

        int m = MapSceneBackBarrierRules.UpdatePassedMask(0, t, s, 15f, 0f, 0, Clear, out int newly);
        Assert.AreEqual(0, m, "닫힌 G1 은 지남으로 보지 않는다");
        Assert.AreEqual(0, newly);

        int open = Mask(MapSceneGate.G1_Corridor1Entry);
        m = MapSceneBackBarrierRules.UpdatePassedMask(0, t, s, 15f, 0f, open, Clear, out newly);
        Assert.AreEqual(open, m);
        Assert.AreEqual(open, newly);

        // 존1 로 되돌아가도(텔레포트 등) 비트는 유지 — 이번엔 새로 켜진 것 없음
        m = MapSceneBackBarrierRules.UpdatePassedMask(m, t, s, 5f, 0f, open, Clear, out newly);
        Assert.AreEqual(open, m);
        Assert.AreEqual(0, newly);

        open |= Mask(MapSceneGate.G2_Zone2Entry);
        m = MapSceneBackBarrierRules.UpdatePassedMask(m, t, s, 25f, 0f, open, Clear, out newly);
        Assert.AreEqual(open, m);
        Assert.AreEqual(Mask(MapSceneGate.G2_Zone2Entry), newly);
    }

    [Test]
    public void 로컬_콜라이더는_지난_것_중_열린_것만_진행_중에만()
    {
        int passed = Mask(MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry);
        int opened = Mask(MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry, MapSceneGate.G3_Corridor2Entry);
        Assert.AreEqual(passed, MapSceneBackBarrierRules.LocalColliderMask(passed, opened, true));
        Assert.AreEqual(Mask(MapSceneGate.G1_Corridor1Entry),
                        MapSceneBackBarrierRules.LocalColliderMask(passed, Mask(MapSceneGate.G1_Corridor1Entry), true),
                        "닫힌 G2 는 일반 배리어가 막는다 — 중복 생성 안 함");
        Assert.AreEqual(0, MapSceneBackBarrierRules.LocalColliderMask(passed, opened, false), "Finished·비활성");
        Assert.AreEqual(0, MapSceneBackBarrierRules.LocalColliderMask(Mask(MapSceneGate.G8_TimerStart), Mask(MapSceneGate.G8_TimerStart), true),
                        "배리어 없는 게이트 비트는 무시");
    }

    [Test]
    public void 켜기_끄기_차이()
    {
        int wanted = Mask(MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry);
        int active = Mask(MapSceneGate.G1_Corridor1Entry, MapSceneGate.G4_Zone3Entry);
        Assert.AreEqual(Mask(MapSceneGate.G2_Zone2Entry), MapSceneBackBarrierRules.ToEnable(wanted, active));
        Assert.AreEqual(Mask(MapSceneGate.G4_Zone3Entry), MapSceneBackBarrierRules.ToDisable(wanted, active));
    }

    [Test]
    public void 전체_배리어_마스크는_8개()
    {
        int all = MapSceneBackBarrierRules.AllBarrierMask;
        int count = 0;
        for (int g = 0; g < MapSceneFlowModel.GateCount; g++)
        {
            bool has = (all & (1 << g)) != 0;
            Assert.AreEqual(MapSceneBarrierRules.HasBarrier((MapSceneGate)g), has, ((MapSceneGate)g).ToString());
            if (has) count++;
        }
        Assert.AreEqual(MapSceneBarrierRules.BarrierCount, count);
        Assert.AreEqual("G1,G2", MapSceneBackBarrierRules.Describe(Mask(MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry)));
    }

    // ─── 스폰·부활·추락 복구 예외(C2) ─────────────────────────────────────

    [Test]
    public void 지나온_최전방_구간()
    {
        Assert.AreEqual(MapSceneArea.Zone1, MapSceneBackBarrierRules.FrontmostPassedArea(0));
        Assert.AreEqual(MapSceneArea.Corridor1, MapSceneBackBarrierRules.FrontmostPassedArea(Mask(MapSceneGate.G1_Corridor1Entry)));
        Assert.AreEqual(MapSceneArea.Zone2, MapSceneBackBarrierRules.FrontmostPassedArea(Mask(MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry)));
        Assert.AreEqual(MapSceneArea.Zone4, MapSceneBackBarrierRules.FrontmostPassedArea(Mask(MapSceneGate.G7_Zone4Entry, MapSceneGate.G1_Corridor1Entry)));
        Assert.AreEqual(MapSceneArea.BossEnter, MapSceneBackBarrierRules.FrontmostPassedArea(Mask(MapSceneGate.G10_BossEnterAndPad)));
        Assert.AreEqual(MapSceneArea.Zone1, MapSceneBackBarrierRules.FrontmostPassedArea(Mask(MapSceneGate.G8_TimerStart)), "배리어 없는 게이트 비트는 무시");

        int passed = Mask(MapSceneGate.G1_Corridor1Entry);
        Assert.IsTrue(MapSceneBackBarrierRules.IsAreaBehind(MapSceneArea.Zone1, passed));
        Assert.IsFalse(MapSceneBackBarrierRules.IsAreaBehind(MapSceneArea.Corridor1, passed));
        Assert.IsFalse(MapSceneBackBarrierRules.IsAreaBehind(MapSceneArea.Zone2, passed));
        Assert.IsFalse(MapSceneBackBarrierRules.IsAreaBehind(MapSceneArea.None, passed), "튜토리얼 밖(보스방)");
    }

    [Test]
    public void 지난_배리어_앞면_여유_안이면_가깝다()
    {
        List<MapSceneBarrierSpec> s = Specs(NewStage());
        int g1 = Mask(MapSceneGate.G1_Corridor1Entry);   // 앞면 x 10.4
        Assert.IsTrue(MapSceneBackBarrierRules.IsNearPassedFace(s, MapSceneArea.Corridor1, 10.9f, 0f, g1, Clear));
        Assert.IsFalse(MapSceneBackBarrierRules.IsNearPassedFace(s, MapSceneArea.Corridor1, 11.5f, 0f, g1, Clear));
        Assert.IsFalse(MapSceneBackBarrierRules.IsNearPassedFace(s, MapSceneArea.Corridor1, 10.9f, 0f, 0, Clear), "안 지났으면 무관");
        Assert.IsFalse(MapSceneBackBarrierRules.IsNearPassedFace(s, MapSceneArea.Zone2, 20.3f, 0f, g1, Clear), "G2 는 안 지남");
        Assert.IsTrue(MapSceneBackBarrierRules.IsNearPassedFace(s, MapSceneArea.Zone2, 20.3f, 0f,
                                                               g1 | Mask(MapSceneGate.G2_Zone2Entry), Clear));
    }

    [Test]
    public void 추락_복구_마스크_0이면_기존_허용과_같다()
    {
        MapSceneAreaTracker t = NewStage();
        List<MapSceneBarrierSpec> s = Specs(t);
        int opened = Mask(MapSceneGate.G1_Corridor1Entry);
        float[][] pts = { new[] { 5f, 0f }, new[] { 15f, 0f }, new[] { 20f, 0f }, new[] { 25f, 0f }, new[] { 100f, 100f } };
        foreach (float[] p in pts)
            Assert.AreEqual(MapSceneBarrierRules.IsPointReachable(t, p[0], p[1], opened),
                            MapSceneBackBarrierRules.IsReturnPointAllowed(t, s, p[0], p[1], opened, 0, Clear), $"({p[0]},{p[1]})");
    }

    [Test]
    public void 추락_복구는_그_사람의_뒤_배리어_너머를_거부한다()
    {
        MapSceneAreaTracker t = NewStage();
        List<MapSceneBarrierSpec> s = Specs(t);
        int opened = Mask(MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry, MapSceneGate.G3_Corridor2Entry);
        int passed = Mask(MapSceneGate.G1_Corridor1Entry, MapSceneGate.G2_Zone2Entry);   // 존2 까지 간 사람

        Assert.IsFalse(MapSceneBackBarrierRules.IsReturnPointAllowed(t, s, 5f, 0f, opened, passed, Clear), "존1");
        Assert.IsFalse(MapSceneBackBarrierRules.IsReturnPointAllowed(t, s, 15f, 0f, opened, passed, Clear), "복도1");
        Assert.IsFalse(MapSceneBackBarrierRules.IsReturnPointAllowed(t, s, 20f, 0f, opened, passed, Clear), "복도1·존2 경계 — 앞면 위");
        Assert.IsFalse(MapSceneBackBarrierRules.IsReturnPointAllowed(t, s, 20.3f, 0f, opened, passed, Clear), "존2 지만 G2 앞면 여유 안(끼임)");
        Assert.IsTrue(MapSceneBackBarrierRules.IsReturnPointAllowed(t, s, 21f, 0f, opened, passed, Clear), "존2 여유 밖");
        Assert.IsTrue(MapSceneBackBarrierRules.IsReturnPointAllowed(t, s, 25f, 8f, opened, passed, Clear), "더 앞(복도2, 열림)");
        Assert.IsFalse(MapSceneBackBarrierRules.IsReturnPointAllowed(t, s, 25f, 20f, opened, passed, Clear), "닫힌 존3 은 그대로 거부");
        Assert.IsTrue(MapSceneBackBarrierRules.IsReturnPointAllowed(t, s, 100f, 100f, opened, passed, Clear), "튜토리얼 밖");

        Assert.IsTrue(MapSceneBackBarrierRules.IsReturnPointAllowed(t, s, 15f, 0f, opened, 0, Clear), "뒤에 남은 동료(마스크 0)는 복도1 허용");
        Assert.IsTrue(MapSceneBackBarrierRules.IsReturnPointAllowed(null, s, 5f, 0f, opened, passed, Clear), "구간 없음 — 막지 않는다");
    }

    [Test]
    public void 추락_복구는_보스_진입_패드와_여유_1m_안을_거부한다()
    {
        MapSceneAreaTracker t = NewStage();
        List<MapSceneBarrierSpec> s = Specs(t);
        int opened = 0;
        for (int g = 0; g <= (int)MapSceneGate.G10_BossEnterAndPad; g++) opened |= 1 << g;
        int passed = opened & MapSceneBackBarrierRules.AllBarrierMask;   // G10 을 지난 사람
        var box = new List<MapScenePadArea> { MapScenePadArea.Box(-17f, 18f, -13f, 22f) };
        var circle = new List<MapScenePadArea> { MapScenePadArea.Circle(-15f, 20f, 2f) };

        Assert.IsTrue(MapSceneBackBarrierRules.IsReturnPointAllowed(t, s, -15f, 20f, opened, passed, Clear), "패드 정보 없음 — 기존 허용");
        Assert.IsTrue(MapSceneBackBarrierRules.IsReturnPointAllowed(t, s, -15f, 20f, opened, passed, Clear, new List<MapScenePadArea>()));

        foreach (List<MapScenePadArea> pads in new[] { box, circle })
        {
            Assert.IsFalse(MapSceneBackBarrierRules.IsReturnPointAllowed(t, s, -15f, 20f, opened, passed, Clear, pads), "패드 위");
            Assert.IsFalse(MapSceneBackBarrierRules.IsReturnPointAllowed(t, s, -12.5f, 20f, opened, passed, Clear, pads), "여유 1m 안");
            Assert.IsTrue(MapSceneBackBarrierRules.IsReturnPointAllowed(t, s, -11.5f, 20f, opened, passed, Clear, pads), "여유 밖");
            Assert.IsFalse(MapSceneBackBarrierRules.IsReturnPointAllowed(t, s, -15f, 20f, opened, 0, Clear, pads), "G10 을 안 지난 사람도 패드는 거부");
        }
        Assert.IsTrue(MapSceneBackBarrierRules.IsReturnPointAllowed(t, s, -11.5f, 20f, opened, passed, Clear, box), "패드 밖 규칙은 그대로");
        Assert.IsFalse(MapSceneBackBarrierRules.IsReturnPointAllowed(t, s, -5f, 20f, opened, passed, Clear, box), "복도4 는 여전히 뒤 배리어 너머");
    }
}
