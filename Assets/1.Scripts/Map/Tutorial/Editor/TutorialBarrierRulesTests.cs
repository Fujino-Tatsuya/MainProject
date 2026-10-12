using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 튜토리얼 배리어 순수 규칙(<see cref="TutorialBarrierRules"/>) — 게이트 매핑, 배리어 모양(복도 양 끝·안쪽·가로 겹침),
/// 구간 진입 가능 판정, 추락 복구 후보 허용 판정. 원본: Docs/design/tutorial-scenario.md §2.2·§4, Docs/tech/tutorial-director.md §7.
/// 좌표는 테스트용 임의값.
/// </summary>
public sealed class TutorialBarrierRulesTests
{
    static int Mask(params TutorialGate[] gates)
    {
        int m = 0;
        foreach (TutorialGate g in gates) m |= 1 << (int)g;
        return m;
    }

    // 존1 x 0~10 · 복도1 x 10~20(z −2~2) · 존2 x 20~30 · 복도2 z 5~15(x 23~27, z 축) · 존3 x 20~30 z 15~25
    // · 복도3 x 10~20 · 존4 x 0~10 · 복도4 x −10~0 · 보스 진입 x −20~−10 — 복도3·4 는 −x 방향(방향 반전도 본다).
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

    static TutorialBarrierSpec Find(List<TutorialBarrierSpec> specs, TutorialGate g)
    {
        foreach (TutorialBarrierSpec s in specs) if (s.Gate == g) return s;
        Assert.Fail($"{g} 배리어 없음");
        return default;
    }

    // ─── 매핑 ────────────────────────────────────────────────────────────

    [Test]
    public void 게이트_매핑_복도_입구출구()
    {
        Assert.AreEqual(TutorialGate.G1_Corridor1Entry, TutorialBarrierRules.GateOf(1, true));
        Assert.AreEqual(TutorialGate.G2_Zone2Entry, TutorialBarrierRules.GateOf(1, false));
        Assert.AreEqual(TutorialGate.G3_Corridor2Entry, TutorialBarrierRules.GateOf(2, true));
        Assert.AreEqual(TutorialGate.G4_Zone3Entry, TutorialBarrierRules.GateOf(2, false));
        Assert.AreEqual(TutorialGate.G6_Corridor3Entry, TutorialBarrierRules.GateOf(3, true));
        Assert.AreEqual(TutorialGate.G7_Zone4Entry, TutorialBarrierRules.GateOf(3, false));
        Assert.AreEqual(TutorialGate.G9_Corridor4Entry, TutorialBarrierRules.GateOf(4, true));
        Assert.AreEqual(TutorialGate.G10_BossEnterAndPad, TutorialBarrierRules.GateOf(4, false));
    }

    [Test]
    public void G5_G8_G11_은_배리어가_없다()
    {
        Assert.IsFalse(TutorialBarrierRules.HasBarrier(TutorialGate.G5_SpinnerRealMode));
        Assert.IsFalse(TutorialBarrierRules.HasBarrier(TutorialGate.G8_TimerStart));
        Assert.IsFalse(TutorialBarrierRules.HasBarrier(TutorialGate.G11_BossRoom));
        Assert.AreEqual(0, TutorialCorridorRules.CorridorOfExitGate(TutorialGate.G11_BossRoom), "복도 출구 기본값(G10)에 흡수되지 않는다");
        Assert.IsTrue(TutorialBarrierRules.HasBarrier(TutorialGate.G1_Corridor1Entry));
        Assert.IsTrue(TutorialBarrierRules.HasBarrier(TutorialGate.G10_BossEnterAndPad));
    }

    [Test]
    public void 잘못된_복도_번호는_예외()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => TutorialBarrierRules.GateOf(0, true));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => TutorialBarrierRules.GateOf(5, false));
    }

    // ─── 모양 ────────────────────────────────────────────────────────────

    [Test]
    public void 배리어_8개_게이트별_하나()
    {
        List<TutorialBarrierSpec> specs = TutorialBarrierRules.BuildSpecs(NewStage(), 0.4f, 0.3f, out string error);
        Assert.IsNull(error);
        Assert.AreEqual(TutorialBarrierRules.BarrierCount, specs.Count);
        var seen = new HashSet<TutorialGate>();
        foreach (TutorialBarrierSpec s in specs) Assert.IsTrue(seen.Add(s.Gate), $"{s.Gate} 중복");
    }

    [Test]
    public void X축_복도_입구는_앞존쪽_끝_안쪽_출구는_다음존쪽_끝_안쪽()
    {
        List<TutorialBarrierSpec> specs = TutorialBarrierRules.BuildSpecs(NewStage(), 0.4f, 0.3f, out _);
        Find(specs, TutorialGate.G1_Corridor1Entry).Rect.GetWorldBounds(out float a0, out float az0, out float a1, out float az1);
        Assert.AreEqual(10f, a0, 1e-4f);
        Assert.AreEqual(10.4f, a1, 1e-4f);
        Assert.AreEqual(-2.3f, az0, 1e-4f);   // 가로 = 복도 너비 + 양옆 겹침
        Assert.AreEqual(2.3f, az1, 1e-4f);

        Find(specs, TutorialGate.G2_Zone2Entry).Rect.GetWorldBounds(out float b0, out _, out float b1, out _);
        Assert.AreEqual(19.6f, b0, 1e-4f);
        Assert.AreEqual(20f, b1, 1e-4f);
    }

    [Test]
    public void Z축_복도도_같은_규칙()
    {
        List<TutorialBarrierSpec> specs = TutorialBarrierRules.BuildSpecs(NewStage(), 0.4f, 0.3f, out _);
        Find(specs, TutorialGate.G3_Corridor2Entry).Rect.GetWorldBounds(out float x0, out float z0, out float x1, out float z1);
        Assert.AreEqual(5f, z0, 1e-4f);
        Assert.AreEqual(5.4f, z1, 1e-4f);
        Assert.AreEqual(22.7f, x0, 1e-4f);
        Assert.AreEqual(27.3f, x1, 1e-4f);
        Find(specs, TutorialGate.G4_Zone3Entry).Rect.GetWorldBounds(out _, out float w0, out _, out float w1);
        Assert.AreEqual(14.6f, w0, 1e-4f);
        Assert.AreEqual(15f, w1, 1e-4f);
    }

    [Test]
    public void 음의_방향_복도는_입구가_큰쪽_끝()
    {
        // 복도3: 존3(x 20~30) → 존4(x 0~10). 입구 = x 20 쪽, 출구 = x 10 쪽(스테이지 180° 회전과 같은 경우)
        List<TutorialBarrierSpec> specs = TutorialBarrierRules.BuildSpecs(NewStage(), 0.4f, 0.3f, out _);
        Find(specs, TutorialGate.G6_Corridor3Entry).Rect.GetWorldBounds(out float a0, out _, out float a1, out _);
        Assert.AreEqual(19.6f, a0, 1e-4f);
        Assert.AreEqual(20f, a1, 1e-4f);
        Find(specs, TutorialGate.G7_Zone4Entry).Rect.GetWorldBounds(out float b0, out _, out float b1, out _);
        Assert.AreEqual(10f, b0, 1e-4f);
        Assert.AreEqual(10.4f, b1, 1e-4f);
    }

    [Test]
    public void 배리어는_존_사각형을_침범하지_않는다()
    {
        TutorialAreaTracker t = NewStage();
        foreach (TutorialBarrierSpec s in TutorialBarrierRules.BuildSpecs(t, 0.4f, 0.3f, out _))
        {
            // 배리어 중심은 복도 안 · 앞/다음 존 안이 아님
            Assert.IsTrue(t.TryGetArea(CorridorOf(s.Corridor), out TutorialRect c));
            Assert.IsTrue(c.Contains(s.Rect.CenterX, s.Rect.CenterZ), s.ToString());
            for (var z = TutorialArea.Zone1; z <= TutorialArea.BossEnter; z++)
                if (TutorialAreaTracker.IsZone(z) && t.TryGetArea(z, out TutorialRect zr))
                    Assert.IsFalse(zr.Contains(s.Rect.CenterX, s.Rect.CenterZ), $"{s} 가 {z} 안");
        }
    }

    [Test]
    public void 복도가_짧으면_두께를_절반으로_줄인다()
    {
        var t = new TutorialAreaTracker();
        t.SetArea(TutorialArea.Zone1, TutorialRect.FromMinMax(0f, -5f, 10f, 5f));
        t.SetArea(TutorialArea.Corridor1, TutorialRect.FromMinMax(10f, -2f, 10.5f, 2f));
        t.SetArea(TutorialArea.Zone2, TutorialRect.FromMinMax(10.5f, -5f, 20f, 5f));
        List<TutorialBarrierSpec> specs = TutorialBarrierRules.BuildSpecs(t, 0.4f, 0f, out string error);
        Assert.AreEqual(2, specs.Count);
        Assert.IsNotNull(error);   // 복도2~4 없음
        Find(specs, TutorialGate.G1_Corridor1Entry).Rect.GetWorldBounds(out float a0, out _, out float a1, out _);
        Assert.AreEqual(0.25f, a1 - a0, 1e-4f);
    }

    [Test]
    public void 구간이_없으면_빈_목록과_오류()
    {
        Assert.AreEqual(0, TutorialBarrierRules.BuildSpecs(null, 0.4f, 0.3f, out string e1).Count);
        Assert.IsNotNull(e1);
        Assert.AreEqual(0, TutorialBarrierRules.BuildSpecs(new TutorialAreaTracker(), 0.4f, 0.3f, out string e2).Count);
        Assert.IsNotNull(e2);
    }

    // ─── 열림 · 진입 가능 ─────────────────────────────────────────────────

    [Test]
    public void 구간_진입_가능은_들어가는_배리어로_정해진다()
    {
        Assert.IsTrue(TutorialBarrierRules.IsAreaReachable(TutorialArea.Zone1, 0));
        Assert.IsFalse(TutorialBarrierRules.IsAreaReachable(TutorialArea.Corridor1, 0));
        int m = Mask(TutorialGate.G1_Corridor1Entry);
        Assert.IsTrue(TutorialBarrierRules.IsAreaReachable(TutorialArea.Corridor1, m));
        Assert.IsFalse(TutorialBarrierRules.IsAreaReachable(TutorialArea.Zone2, m));
        m |= Mask(TutorialGate.G2_Zone2Entry, TutorialGate.G3_Corridor2Entry, TutorialGate.G4_Zone3Entry,
                  TutorialGate.G5_SpinnerRealMode, TutorialGate.G6_Corridor3Entry);
        Assert.IsTrue(TutorialBarrierRules.IsAreaReachable(TutorialArea.Corridor3, m));
        Assert.IsFalse(TutorialBarrierRules.IsAreaReachable(TutorialArea.Zone4, m));
        m |= Mask(TutorialGate.G7_Zone4Entry, TutorialGate.G8_TimerStart, TutorialGate.G9_Corridor4Entry);
        Assert.IsTrue(TutorialBarrierRules.IsAreaReachable(TutorialArea.Corridor4, m));
        Assert.IsFalse(TutorialBarrierRules.IsAreaReachable(TutorialArea.BossEnter, m));
        Assert.IsTrue(TutorialBarrierRules.IsAreaReachable(TutorialArea.BossEnter, m | Mask(TutorialGate.G10_BossEnterAndPad)));
        Assert.IsTrue(TutorialBarrierRules.IsAreaReachable(TutorialArea.None, 0));
    }

    [Test]
    public void 열림_판정은_게이트_비트()
    {
        int m = Mask(TutorialGate.G3_Corridor2Entry);
        Assert.IsTrue(TutorialBarrierRules.IsGateOpen(TutorialGate.G3_Corridor2Entry, m));
        Assert.IsFalse(TutorialBarrierRules.IsGateOpen(TutorialGate.G4_Zone3Entry, m));
    }

    // ─── 추락 복구 후보 ───────────────────────────────────────────────────

    [Test]
    public void 추락후보_닫힌_배리어_너머는_거부()
    {
        TutorialAreaTracker t = NewStage();
        Assert.IsTrue(TutorialBarrierRules.IsPointReachable(t, 9.5f, 0f, 0));      // 존1, G1 앞
        Assert.IsFalse(TutorialBarrierRules.IsPointReachable(t, 11f, 0f, 0));      // 복도1 — G1 닫힘
        Assert.IsFalse(TutorialBarrierRules.IsPointReachable(t, 21f, 0f, 0));      // 존2

        int g1 = Mask(TutorialGate.G1_Corridor1Entry);
        Assert.IsTrue(TutorialBarrierRules.IsPointReachable(t, 19f, 0f, g1));      // 복도1 안 — G2 앞
        Assert.IsFalse(TutorialBarrierRules.IsPointReachable(t, 20.5f, 0f, g1));   // G2 너머 존2
    }

    [Test]
    public void 추락후보_경계선_위는_한쪽만_열려도_허용()
    {
        TutorialAreaTracker t = NewStage();
        Assert.IsTrue(TutorialBarrierRules.IsPointReachable(t, 10f, 0f, 0));   // 존1·복도1 경계 — 존1 쪽 열림
    }

    [Test]
    public void 추락후보_튜토리얼_구간_밖은_허용()
    {
        TutorialAreaTracker t = NewStage();
        Assert.IsTrue(TutorialBarrierRules.IsPointReachable(t, 500f, 500f, 0));   // 보스방 등
        Assert.IsTrue(TutorialBarrierRules.IsPointReachable(null, 11f, 0f, 0));    // 구간 없음
    }

    static TutorialArea CorridorOf(int c) => c switch
    {
        1 => TutorialArea.Corridor1,
        2 => TutorialArea.Corridor2,
        3 => TutorialArea.Corridor3,
        _ => TutorialArea.Corridor4,
    };
}
