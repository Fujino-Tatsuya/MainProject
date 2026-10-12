using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 개인 뒤 배리어 순수 규칙(<see cref="TutorialBackBarrierRules"/>) — 너머 구간, 지남 판정(앞면·여유·너머 구간),
/// 마스크 갱신(열린 게이트만·유지), 로컬 콜라이더 목록(열린 것만·진행 중만), 켜기/끄기 차이.
/// 원본: Docs/tech/tutorial-director.md §7. 스테이지는 <see cref="TutorialBarrierRulesTests"/> 와 같은 임의 좌표.
/// </summary>
public sealed class TutorialBackBarrierRulesTests
{
    const float Clear = TutorialBackBarrierRules.DefaultClearance;

    static int Mask(params TutorialGate[] gates)
    {
        int m = 0;
        foreach (TutorialGate g in gates) m |= 1 << (int)g;
        return m;
    }

    // 존1 x 0~10 · 복도1 x 10~20(+x) · 존2 x 20~30 · 복도2 z 5~15(+z) · 존3 z 15~25 · 복도3 x 10~20(−x) · 존4 x 0~10 · 복도4 x −10~0 · 보스 진입
    static TutorialAreaTracker NewStage()
    {
        var t = new TutorialAreaTracker();
        t.SetArea(TutorialArea.Zone1, TutorialRect.FromMinMax(0f, -5f, 10f, 5f));
        t.SetArea(TutorialArea.Corridor1, TutorialRect.FromMinMax(10f, -2f, 20f, 2f));
        t.SetArea(TutorialArea.Zone2, TutorialRect.FromMinMax(20f, -5f, 30f, 5f));
        t.SetArea(TutorialArea.Corridor2, TutorialRect.FromMinMax(23f, 5f, 27f, 15f));
        t.SetArea(TutorialArea.Zone3, TutorialRect.FromMinMax(20f, 15f, 30f, 25f));
        t.SetArea(TutorialArea.Corridor3, TutorialRect.FromMinMax(10f, 18f, 20f, 22f));
        t.SetArea(TutorialArea.Zone4, TutorialRect.FromMinMax(0f, 15f, 10f, 25f));
        t.SetArea(TutorialArea.Corridor4, TutorialRect.FromMinMax(-10f, 18f, 0f, 22f));
        t.SetArea(TutorialArea.BossEnter, TutorialRect.FromMinMax(-20f, 15f, -10f, 25f));
        return t;
    }

    static List<TutorialBarrierSpec> Specs(TutorialAreaTracker t) =>
        TutorialBarrierRules.BuildSpecs(t, TutorialBarrierRules.DefaultThickness, TutorialBarrierRules.DefaultSideOverlap, out _);

    static TutorialBarrierSpec Find(List<TutorialBarrierSpec> specs, TutorialGate g)
    {
        foreach (TutorialBarrierSpec s in specs) if (s.Gate == g) return s;
        Assert.Fail($"{g} 배리어 없음");
        return default;
    }

    [Test]
    public void 너머_구간_입구는_복도_출구는_다음_존()
    {
        List<TutorialBarrierSpec> s = Specs(NewStage());
        Assert.AreEqual(TutorialArea.Corridor1, TutorialBackBarrierRules.AreaBeyond(Find(s, TutorialGate.G1_Corridor1Entry)));
        Assert.AreEqual(TutorialArea.Zone2, TutorialBackBarrierRules.AreaBeyond(Find(s, TutorialGate.G2_Zone2Entry)));
        Assert.AreEqual(TutorialArea.Corridor3, TutorialBackBarrierRules.AreaBeyond(Find(s, TutorialGate.G6_Corridor3Entry)));
        Assert.AreEqual(TutorialArea.BossEnter, TutorialBackBarrierRules.AreaBeyond(Find(s, TutorialGate.G10_BossEnterAndPad)));
    }

    [Test]
    public void 스펙에_진행_방향이_실린다()
    {
        List<TutorialBarrierSpec> s = Specs(NewStage());
        TutorialBarrierSpec g1 = Find(s, TutorialGate.G1_Corridor1Entry);
        Assert.IsTrue(g1.AlongX); Assert.AreEqual(1f, g1.Forward);
        TutorialBarrierSpec g3 = Find(s, TutorialGate.G3_Corridor2Entry);
        Assert.IsFalse(g3.AlongX); Assert.AreEqual(1f, g3.Forward);
        TutorialBarrierSpec g6 = Find(s, TutorialGate.G6_Corridor3Entry);
        Assert.IsTrue(g6.AlongX); Assert.AreEqual(-1f, g6.Forward, "복도3 은 −x 로 나아간다");
    }

    [Test]
    public void 앞면에서_여유만큼_넘어가야_지남()
    {
        TutorialAreaTracker t = NewStage();
        TutorialBarrierSpec g1 = Find(Specs(t), TutorialGate.G1_Corridor1Entry);   // 박스 x 10~10.4, 앞면 10.4
        Assert.AreEqual(0.6f, TutorialBackBarrierRules.DistancePastFace(g1, 11f, 0f), 1e-4f);
        Assert.Less(TutorialBackBarrierRules.DistancePastFace(g1, 5f, 0f), 0f, "존1 쪽은 음수");

        Assert.IsFalse(TutorialBackBarrierRules.HasPassed(t, g1, 10.2f, 0f, Clear), "배리어 박스 위");
        Assert.IsFalse(TutorialBackBarrierRules.HasPassed(t, g1, 10.4f + Clear - 0.01f, 0f, Clear), "여유 미만");
        Assert.IsTrue(TutorialBackBarrierRules.HasPassed(t, g1, 10.4f + Clear + 0.01f, 0f, Clear));
        Assert.IsTrue(TutorialBackBarrierRules.HasPassed(t, g1, 19f, 0f, Clear), "복도 깊이 들어가도 지남");
    }

    [Test]
    public void 너머_구간_밖이면_지나지_않음()
    {
        TutorialAreaTracker t = NewStage();
        TutorialBarrierSpec g1 = Find(Specs(t), TutorialGate.G1_Corridor1Entry);
        Assert.IsFalse(TutorialBackBarrierRules.HasPassed(t, g1, 25f, 0f, Clear), "존2 는 G1 너머 구간(복도1)이 아니다");
        Assert.IsFalse(TutorialBackBarrierRules.HasPassed(t, g1, 15f, 4f, Clear), "복도 옆(벽 밖)");
        Assert.IsFalse(TutorialBackBarrierRules.HasPassed(null, g1, 15f, 0f, Clear));
    }

    [Test]
    public void 출구_배리어는_다음_존_안쪽에서_지남()
    {
        TutorialAreaTracker t = NewStage();
        TutorialBarrierSpec g2 = Find(Specs(t), TutorialGate.G2_Zone2Entry);   // 박스 x 19.6~20, 앞면 20
        Assert.IsFalse(TutorialBackBarrierRules.HasPassed(t, g2, 19f, 0f, Clear), "아직 복도");
        Assert.IsFalse(TutorialBackBarrierRules.HasPassed(t, g2, 20.5f, 0f, Clear), "여유 미만");
        Assert.IsTrue(TutorialBackBarrierRules.HasPassed(t, g2, 21f, 0f, Clear));
    }

    [Test]
    public void 역방향_복도도_진행_방향으로_판정()
    {
        TutorialAreaTracker t = NewStage();
        TutorialBarrierSpec g6 = Find(Specs(t), TutorialGate.G6_Corridor3Entry);   // 박스 x 19.6~20, 앞면 19.6(−x)
        Assert.IsFalse(TutorialBackBarrierRules.HasPassed(t, g6, 19.2f, 20f, Clear));
        Assert.IsTrue(TutorialBackBarrierRules.HasPassed(t, g6, 18.5f, 20f, Clear));
        TutorialBarrierSpec g7 = Find(Specs(t), TutorialGate.G7_Zone4Entry);       // 앞면 x 10, 존4 = x 0~10
        Assert.IsTrue(TutorialBackBarrierRules.HasPassed(t, g7, 9f, 20f, Clear));
        Assert.IsFalse(TutorialBackBarrierRules.HasPassed(t, g7, 11f, 20f, Clear));
    }

    [Test]
    public void 마스크는_열린_게이트만_켜고_유지한다()
    {
        TutorialAreaTracker t = NewStage();
        List<TutorialBarrierSpec> s = Specs(t);

        int m = TutorialBackBarrierRules.UpdatePassedMask(0, t, s, 15f, 0f, 0, Clear, out int newly);
        Assert.AreEqual(0, m, "닫힌 G1 은 지남으로 보지 않는다");
        Assert.AreEqual(0, newly);

        int open = Mask(TutorialGate.G1_Corridor1Entry);
        m = TutorialBackBarrierRules.UpdatePassedMask(0, t, s, 15f, 0f, open, Clear, out newly);
        Assert.AreEqual(open, m);
        Assert.AreEqual(open, newly);

        // 존1 로 되돌아가도(텔레포트 등) 비트는 유지 — 이번엔 새로 켜진 것 없음
        m = TutorialBackBarrierRules.UpdatePassedMask(m, t, s, 5f, 0f, open, Clear, out newly);
        Assert.AreEqual(open, m);
        Assert.AreEqual(0, newly);

        open |= Mask(TutorialGate.G2_Zone2Entry);
        m = TutorialBackBarrierRules.UpdatePassedMask(m, t, s, 25f, 0f, open, Clear, out newly);
        Assert.AreEqual(open, m);
        Assert.AreEqual(Mask(TutorialGate.G2_Zone2Entry), newly);
    }

    [Test]
    public void 로컬_콜라이더는_지난_것_중_열린_것만_진행_중에만()
    {
        int passed = Mask(TutorialGate.G1_Corridor1Entry, TutorialGate.G2_Zone2Entry);
        int opened = Mask(TutorialGate.G1_Corridor1Entry, TutorialGate.G2_Zone2Entry, TutorialGate.G3_Corridor2Entry);
        Assert.AreEqual(passed, TutorialBackBarrierRules.LocalColliderMask(passed, opened, true));
        Assert.AreEqual(Mask(TutorialGate.G1_Corridor1Entry),
                        TutorialBackBarrierRules.LocalColliderMask(passed, Mask(TutorialGate.G1_Corridor1Entry), true),
                        "닫힌 G2 는 일반 배리어가 막는다 — 중복 생성 안 함");
        Assert.AreEqual(0, TutorialBackBarrierRules.LocalColliderMask(passed, opened, false), "Finished·비활성");
        Assert.AreEqual(0, TutorialBackBarrierRules.LocalColliderMask(Mask(TutorialGate.G8_TimerStart), Mask(TutorialGate.G8_TimerStart), true),
                        "배리어 없는 게이트 비트는 무시");
    }

    [Test]
    public void 켜기_끄기_차이()
    {
        int wanted = Mask(TutorialGate.G1_Corridor1Entry, TutorialGate.G2_Zone2Entry);
        int active = Mask(TutorialGate.G1_Corridor1Entry, TutorialGate.G4_Zone3Entry);
        Assert.AreEqual(Mask(TutorialGate.G2_Zone2Entry), TutorialBackBarrierRules.ToEnable(wanted, active));
        Assert.AreEqual(Mask(TutorialGate.G4_Zone3Entry), TutorialBackBarrierRules.ToDisable(wanted, active));
    }

    [Test]
    public void 전체_배리어_마스크는_8개()
    {
        int all = TutorialBackBarrierRules.AllBarrierMask;
        int count = 0;
        for (int g = 0; g < TutorialFlowModel.GateCount; g++)
        {
            bool has = (all & (1 << g)) != 0;
            Assert.AreEqual(TutorialBarrierRules.HasBarrier((TutorialGate)g), has, ((TutorialGate)g).ToString());
            if (has) count++;
        }
        Assert.AreEqual(TutorialBarrierRules.BarrierCount, count);
        Assert.AreEqual("G1,G2", TutorialBackBarrierRules.Describe(Mask(TutorialGate.G1_Corridor1Entry, TutorialGate.G2_Zone2Entry)));
    }

    // ─── 스폰·부활·추락 복구 예외(C2) ─────────────────────────────────────

    [Test]
    public void 지나온_최전방_구간()
    {
        Assert.AreEqual(TutorialArea.Zone1, TutorialBackBarrierRules.FrontmostPassedArea(0));
        Assert.AreEqual(TutorialArea.Corridor1, TutorialBackBarrierRules.FrontmostPassedArea(Mask(TutorialGate.G1_Corridor1Entry)));
        Assert.AreEqual(TutorialArea.Zone2, TutorialBackBarrierRules.FrontmostPassedArea(Mask(TutorialGate.G1_Corridor1Entry, TutorialGate.G2_Zone2Entry)));
        Assert.AreEqual(TutorialArea.Zone4, TutorialBackBarrierRules.FrontmostPassedArea(Mask(TutorialGate.G7_Zone4Entry, TutorialGate.G1_Corridor1Entry)));
        Assert.AreEqual(TutorialArea.BossEnter, TutorialBackBarrierRules.FrontmostPassedArea(Mask(TutorialGate.G10_BossEnterAndPad)));
        Assert.AreEqual(TutorialArea.Zone1, TutorialBackBarrierRules.FrontmostPassedArea(Mask(TutorialGate.G8_TimerStart)), "배리어 없는 게이트 비트는 무시");

        int passed = Mask(TutorialGate.G1_Corridor1Entry);
        Assert.IsTrue(TutorialBackBarrierRules.IsAreaBehind(TutorialArea.Zone1, passed));
        Assert.IsFalse(TutorialBackBarrierRules.IsAreaBehind(TutorialArea.Corridor1, passed));
        Assert.IsFalse(TutorialBackBarrierRules.IsAreaBehind(TutorialArea.Zone2, passed));
        Assert.IsFalse(TutorialBackBarrierRules.IsAreaBehind(TutorialArea.None, passed), "튜토리얼 밖(보스방)");
    }

    [Test]
    public void 지난_배리어_앞면_여유_안이면_가깝다()
    {
        List<TutorialBarrierSpec> s = Specs(NewStage());
        int g1 = Mask(TutorialGate.G1_Corridor1Entry);   // 앞면 x 10.4
        Assert.IsTrue(TutorialBackBarrierRules.IsNearPassedFace(s, TutorialArea.Corridor1, 10.9f, 0f, g1, Clear));
        Assert.IsFalse(TutorialBackBarrierRules.IsNearPassedFace(s, TutorialArea.Corridor1, 11.5f, 0f, g1, Clear));
        Assert.IsFalse(TutorialBackBarrierRules.IsNearPassedFace(s, TutorialArea.Corridor1, 10.9f, 0f, 0, Clear), "안 지났으면 무관");
        Assert.IsFalse(TutorialBackBarrierRules.IsNearPassedFace(s, TutorialArea.Zone2, 20.3f, 0f, g1, Clear), "G2 는 안 지남");
        Assert.IsTrue(TutorialBackBarrierRules.IsNearPassedFace(s, TutorialArea.Zone2, 20.3f, 0f,
                                                               g1 | Mask(TutorialGate.G2_Zone2Entry), Clear));
    }

    [Test]
    public void 추락_복구_마스크_0이면_기존_허용과_같다()
    {
        TutorialAreaTracker t = NewStage();
        List<TutorialBarrierSpec> s = Specs(t);
        int opened = Mask(TutorialGate.G1_Corridor1Entry);
        float[][] pts = { new[] { 5f, 0f }, new[] { 15f, 0f }, new[] { 20f, 0f }, new[] { 25f, 0f }, new[] { 100f, 100f } };
        foreach (float[] p in pts)
            Assert.AreEqual(TutorialBarrierRules.IsPointReachable(t, p[0], p[1], opened),
                            TutorialBackBarrierRules.IsReturnPointAllowed(t, s, p[0], p[1], opened, 0, Clear), $"({p[0]},{p[1]})");
    }

    [Test]
    public void 추락_복구는_그_사람의_뒤_배리어_너머를_거부한다()
    {
        TutorialAreaTracker t = NewStage();
        List<TutorialBarrierSpec> s = Specs(t);
        int opened = Mask(TutorialGate.G1_Corridor1Entry, TutorialGate.G2_Zone2Entry, TutorialGate.G3_Corridor2Entry);
        int passed = Mask(TutorialGate.G1_Corridor1Entry, TutorialGate.G2_Zone2Entry);   // 존2 까지 간 사람

        Assert.IsFalse(TutorialBackBarrierRules.IsReturnPointAllowed(t, s, 5f, 0f, opened, passed, Clear), "존1");
        Assert.IsFalse(TutorialBackBarrierRules.IsReturnPointAllowed(t, s, 15f, 0f, opened, passed, Clear), "복도1");
        Assert.IsFalse(TutorialBackBarrierRules.IsReturnPointAllowed(t, s, 20f, 0f, opened, passed, Clear), "복도1·존2 경계 — 앞면 위");
        Assert.IsFalse(TutorialBackBarrierRules.IsReturnPointAllowed(t, s, 20.3f, 0f, opened, passed, Clear), "존2 지만 G2 앞면 여유 안(끼임)");
        Assert.IsTrue(TutorialBackBarrierRules.IsReturnPointAllowed(t, s, 21f, 0f, opened, passed, Clear), "존2 여유 밖");
        Assert.IsTrue(TutorialBackBarrierRules.IsReturnPointAllowed(t, s, 25f, 8f, opened, passed, Clear), "더 앞(복도2, 열림)");
        Assert.IsFalse(TutorialBackBarrierRules.IsReturnPointAllowed(t, s, 25f, 20f, opened, passed, Clear), "닫힌 존3 은 그대로 거부");
        Assert.IsTrue(TutorialBackBarrierRules.IsReturnPointAllowed(t, s, 100f, 100f, opened, passed, Clear), "튜토리얼 밖");

        Assert.IsTrue(TutorialBackBarrierRules.IsReturnPointAllowed(t, s, 15f, 0f, opened, 0, Clear), "뒤에 남은 동료(마스크 0)는 복도1 허용");
        Assert.IsTrue(TutorialBackBarrierRules.IsReturnPointAllowed(null, s, 5f, 0f, opened, passed, Clear), "구간 없음 — 막지 않는다");
    }
}
