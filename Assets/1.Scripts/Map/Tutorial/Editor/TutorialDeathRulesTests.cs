using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 튜토리얼 사망 규칙 순수 로직(<see cref="TutorialDeathRulesModel"/>·<see cref="TutorialRevivePointRules"/>) —
/// 차감 여부(단계 기준), 3초 대기·Soul 대기, 이동 시작 시 대기 취소·즉시 그 자리 부활, 연출 잠금 보류·재시도, 부활 지점 선택.
/// 원본: Docs/design/tutorial-scenario.md §2.5·§4, Docs/tech/tutorial-director.md §8. 좌표는 테스트용 임의값.
/// </summary>
public sealed class TutorialDeathRulesTests
{
    const ulong A = 1, B = 2;

    static int Mask(params TutorialGate[] gates)
    {
        int m = 0;
        foreach (TutorialGate g in gates) m |= 1 << (int)g;
        return m;
    }

    static int MaskUpTo(TutorialGate last)
    {
        int m = 0;
        for (int i = 0; i <= (int)last; i++) m |= 1 << i;
        return m;
    }

    static TutorialReviveVerdict Eval(TutorialDeathRulesModel m, ulong id, double now, PlayerLifeState s, bool locked,
                                      out TutorialReviveOrder order)
        => m.Evaluate(id, now, s, locked, out order, out _);

    // ─── 차감 여부 · 대기 ────────────────────────────────────────────────

    [Test]
    public void G8전_사망은_3초뒤_차감없이_안전지점_부활()
    {
        var m = new TutorialDeathRulesModel();
        m.RecordDeath(A, 10d, timerStarted: false);

        Assert.That(m.IsPenaltyFree(A), Is.True, "G8 전 = 목숨 수와 무관하게 Soul");
        Assert.That(Eval(m, A, 12.9d, PlayerLifeState.Soul, false, out _), Is.EqualTo(TutorialReviveVerdict.Waiting));
        Assert.That(Eval(m, A, 13d, PlayerLifeState.Soul, false, out TutorialReviveOrder o), Is.EqualTo(TutorialReviveVerdict.Revive));
        Assert.That(o.ConsumeLife, Is.False);
        Assert.That(o.InPlace, Is.False);
        Assert.That(m.HasPending(A), Is.False, "부활하면 기록이 지워진다");
    }

    [Test]
    public void G8뒤_사망은_목숨1개_차감()
    {
        var m = new TutorialDeathRulesModel();
        m.RecordDeath(A, 0d, timerStarted: true);

        Assert.That(m.IsPenaltyFree(A), Is.False, "G8 뒤 = 기존 목적지 규칙(목숨 0 → 완전 사망)");
        Assert.That(Eval(m, A, 3d, PlayerLifeState.Soul, false, out TutorialReviveOrder o), Is.EqualTo(TutorialReviveVerdict.Revive));
        Assert.That(o.ConsumeLife, Is.True);
    }

    [Test]
    public void 차감여부는_사망시점_단계로_고정된다()
    {
        // 대기 중에 G8 이 열려도 G8 전 사망은 차감 없음 — 위치는 아예 입력이 아니다(단계 기준).
        var m = new TutorialDeathRulesModel();
        m.RecordDeath(A, 0d, timerStarted: false);
        Assert.That(Eval(m, A, 3d, PlayerLifeState.Soul, false, out TutorialReviveOrder o), Is.EqualTo(TutorialReviveVerdict.Revive));
        Assert.That(o.ConsumeLife, Is.False);
    }

    [Test]
    public void 시각이_지나도_사망연출중이면_Soul을_기다린다()
    {
        var m = new TutorialDeathRulesModel();
        m.RecordDeath(A, 0d, false);
        Assert.That(Eval(m, A, 5d, PlayerLifeState.DeadPresentation, false, out _), Is.EqualTo(TutorialReviveVerdict.WaitingForSoul));
        Assert.That(m.HasPending(A), Is.True);
        Assert.That(Eval(m, A, 5.1d, PlayerLifeState.Soul, false, out _), Is.EqualTo(TutorialReviveVerdict.Revive));
    }

    [Test]
    public void 다른경로로_살아났거나_완전사망이면_기록을_버린다()
    {
        var m = new TutorialDeathRulesModel();
        m.RecordDeath(A, 0d, true);
        m.RecordDeath(B, 0d, true);
        Assert.That(Eval(m, A, 1d, PlayerLifeState.Alive, false, out _), Is.EqualTo(TutorialReviveVerdict.Dropped));
        Assert.That(Eval(m, B, 1d, PlayerLifeState.PermanentDead, false, out _), Is.EqualTo(TutorialReviveVerdict.Dropped));
        Assert.That(m.PendingCount, Is.Zero);
        Assert.That(Eval(m, A, 4d, PlayerLifeState.Soul, false, out _), Is.EqualTo(TutorialReviveVerdict.None));
    }

    [Test]
    public void 이탈하면_기록을_지운다()
    {
        var m = new TutorialDeathRulesModel();
        m.RecordDeath(A, 0d, false);
        Assert.That(m.Forget(A), Is.True);
        Assert.That(m.IsPenaltyFree(A), Is.False);
        Assert.That(m.Forget(A), Is.False);
    }

    // ─── 보스방 이동 ─────────────────────────────────────────────────────

    [Test]
    public void 이동_시작하면_대기부활을_취소하고_즉시_그자리_부활()
    {
        var m = new TutorialDeathRulesModel();
        m.RecordDeath(A, 0d, timerStarted: false);
        m.RecordDeath(B, 0d, timerStarted: true);

        Assert.That(m.BeginMove(), Is.EqualTo(2));
        Assert.That(m.BeginMove(), Is.Zero, "이미 진행 중이면 다시 바꾸지 않는다");

        // 3초 전이어도 Soul 이면 바로.
        Assert.That(Eval(m, A, 1.6d, PlayerLifeState.Soul, false, out TutorialReviveOrder a), Is.EqualTo(TutorialReviveVerdict.Revive));
        Assert.That(a.InPlace, Is.True);
        Assert.That(a.ConsumeLife, Is.False, "G8 전 사망이면 이동 중 즉시 부활도 차감 없음");

        // 아직 사망 연출이면 Soul 을 기다린다(대기 3초는 무시).
        Assert.That(Eval(m, B, 0.5d, PlayerLifeState.DeadPresentation, false, out _), Is.EqualTo(TutorialReviveVerdict.WaitingForSoul));
        Assert.That(Eval(m, B, 1.6d, PlayerLifeState.Soul, false, out TutorialReviveOrder b), Is.EqualTo(TutorialReviveVerdict.Revive));
        Assert.That(b.InPlace, Is.True);
        Assert.That(b.ConsumeLife, Is.True);
    }

    [Test]
    public void 이동중_사망은_바로_그자리_이동끝나도_되돌리지_않는다()
    {
        var m = new TutorialDeathRulesModel();
        m.BeginMove();
        m.RecordDeath(A, 0d, true);
        m.EndMove();
        m.RecordDeath(B, 0d, true);

        Assert.That(Eval(m, A, 0.1d, PlayerLifeState.Soul, false, out TutorialReviveOrder a), Is.EqualTo(TutorialReviveVerdict.Revive));
        Assert.That(a.InPlace, Is.True, "옛 구간 지점으로 끌려가면 안 된다");
        Assert.That(Eval(m, B, 0.1d, PlayerLifeState.Soul, false, out _), Is.EqualTo(TutorialReviveVerdict.Waiting), "이동이 끝난 뒤 사망은 다시 3초");
    }

    [Test]
    public void 튜토리얼_종료시_남은_대기는_그자리로()
    {
        var m = new TutorialDeathRulesModel();
        m.RecordDeath(A, 0d, false);
        Assert.That(m.ConvertAllToInPlace(), Is.EqualTo(1));
        Assert.That(m.ConvertAllToInPlace(), Is.Zero);
        Assert.That(Eval(m, A, 0.1d, PlayerLifeState.Soul, false, out TutorialReviveOrder o), Is.EqualTo(TutorialReviveVerdict.Revive));
        Assert.That(o.InPlace, Is.True);
    }

    // ─── 연출 잠금 ───────────────────────────────────────────────────────

    [Test]
    public void 연출잠금중엔_보류하고_첫번만_알리고_풀리면_부활()
    {
        var m = new TutorialDeathRulesModel();
        m.RecordDeath(A, 0d, true);

        Assert.That(m.Evaluate(A, 3d, PlayerLifeState.Soul, true, out _, out bool first1), Is.EqualTo(TutorialReviveVerdict.BlockedByCinematic));
        Assert.That(first1, Is.True);
        Assert.That(m.Evaluate(A, 4d, PlayerLifeState.DeadPresentation, true, out _, out bool first2), Is.EqualTo(TutorialReviveVerdict.BlockedByCinematic));
        Assert.That(first2, Is.False, "로그는 한 번만");
        Assert.That(m.HasPending(A), Is.True, "조용히 버리지 않는다");

        Assert.That(Eval(m, A, 5d, PlayerLifeState.Soul, false, out TutorialReviveOrder o), Is.EqualTo(TutorialReviveVerdict.Revive));
        Assert.That(o.WasBlocked, Is.True);
        Assert.That(o.ConsumeLife, Is.True);
    }

    [Test]
    public void 대기시각_전에는_잠금이어도_Waiting()
    {
        var m = new TutorialDeathRulesModel();
        m.RecordDeath(A, 0d, false);
        Assert.That(m.Evaluate(A, 1d, PlayerLifeState.Soul, true, out _, out bool first), Is.EqualTo(TutorialReviveVerdict.Waiting));
        Assert.That(first, Is.False);
    }

    // ─── 부활 지점 ───────────────────────────────────────────────────────

    [Test]
    public void 지금_단계_구간은_열린_배리어로_들어갈_수_있는_가장_앞_구간_보스진입_제외()
    {
        Assert.That(TutorialRevivePointRules.ResolveStageArea(0), Is.EqualTo(TutorialArea.Zone1));
        Assert.That(TutorialRevivePointRules.ResolveStageArea(Mask(TutorialGate.G1_Corridor1Entry)), Is.EqualTo(TutorialArea.Corridor1));
        Assert.That(TutorialRevivePointRules.ResolveStageArea(MaskUpTo(TutorialGate.G2_Zone2Entry)), Is.EqualTo(TutorialArea.Zone2));
        Assert.That(TutorialRevivePointRules.ResolveStageArea(MaskUpTo(TutorialGate.G8_TimerStart)), Is.EqualTo(TutorialArea.Zone4));
        Assert.That(TutorialRevivePointRules.ResolveStageArea(MaskUpTo(TutorialGate.G10_BossEnterAndPad)), Is.EqualTo(TutorialArea.Corridor4),
            "보스 진입 존(패드)에서는 부활시키지 않는다");
    }

    [Test]
    public void 찾는_순서는_지금_구간부터_거꾸로()
    {
        List<TutorialArea> order = TutorialRevivePointRules.AreaOrder(MaskUpTo(TutorialGate.G2_Zone2Entry));
        Assert.That(order, Is.EqualTo(new[] { TutorialArea.Zone2, TutorialArea.Corridor1, TutorialArea.Zone1 }));
    }

    [Test]
    public void 격자는_가장자리_안쪽에만_깔린다()
    {
        var pts = new List<(float x, float z)>();
        TutorialRevivePointRules.SampleGrid(TutorialRect.FromMinMax(0f, 0f, 10f, 4f), 2f, 1f, pts);
        Assert.That(pts.Count, Is.EqualTo(5 * 2));
        foreach ((float x, float z) in pts)
        {
            Assert.That(x, Is.InRange(1f, 9f));
            Assert.That(z, Is.InRange(1f, 3f));
        }

        pts.Clear();
        TutorialRevivePointRules.SampleGrid(TutorialRect.FromMinMax(0f, 0f, 1f, 1f), 2f, 1f, pts);
        Assert.That(pts.Count, Is.EqualTo(1), "여유가 없으면 가운데 한 점");
        Assert.That(pts[0].x, Is.EqualTo(0.5f).Within(1e-4));
    }

    [Test]
    public void 안전한_후보중_사망지점에_가까운것_지금구간_우선()
    {
        int mask = MaskUpTo(TutorialGate.G2_Zone2Entry);   // 지금 = 존2
        var c = new List<TutorialRevivePoint>
        {
            new TutorialRevivePoint(0f, 0f, TutorialArea.Zone1),
            new TutorialRevivePoint(20f, 0f, TutorialArea.Zone2),   // 몬스터 바로 옆
            new TutorialRevivePoint(28f, 0f, TutorialArea.Zone2),
            new TutorialRevivePoint(40f, 0f, TutorialArea.Zone2),
        };
        var threats = new List<TutorialThreat> { new TutorialThreat(21f, 0f, 5f) };

        Assert.That(TutorialRevivePointRules.TrySelect(c, threats, mask, 22f, 0f, out int i, out bool safe), Is.True);
        Assert.That(i, Is.EqualTo(2));
        Assert.That(safe, Is.True);
    }

    [Test]
    public void 지금구간이_전부_위험하면_앞구간으로()
    {
        int mask = MaskUpTo(TutorialGate.G2_Zone2Entry);
        var c = new List<TutorialRevivePoint>
        {
            new TutorialRevivePoint(20f, 0f, TutorialArea.Zone2),
            new TutorialRevivePoint(12f, 0f, TutorialArea.Corridor1),
            new TutorialRevivePoint(0f, 0f, TutorialArea.Zone1),
        };
        var threats = new List<TutorialThreat> { new TutorialThreat(20f, 0f, 5f) };

        Assert.That(TutorialRevivePointRules.TrySelect(c, threats, mask, 20f, 0f, out int i, out bool safe), Is.True);
        Assert.That(i, Is.EqualTo(1), "복도1 — 존2 다음 순서");
        Assert.That(safe, Is.True);
    }

    [Test]
    public void 안전한_곳이_없으면_가장_먼_후보()
    {
        int mask = MaskUpTo(TutorialGate.G2_Zone2Entry);
        var c = new List<TutorialRevivePoint>
        {
            new TutorialRevivePoint(0f, 0f, TutorialArea.Zone2),
            new TutorialRevivePoint(6f, 0f, TutorialArea.Zone1),
        };
        var threats = new List<TutorialThreat> { new TutorialThreat(0f, 0f, 100f) };

        Assert.That(TutorialRevivePointRules.TrySelect(c, threats, mask, 0f, 0f, out int i, out bool safe), Is.True);
        Assert.That(i, Is.EqualTo(1));
        Assert.That(safe, Is.False);
    }

    [Test]
    public void 닫힌_구간과_보스진입_후보는_쓰지_않는다()
    {
        int mask = MaskUpTo(TutorialGate.G1_Corridor1Entry);   // 지금 = 복도1, 존2 닫힘
        var c = new List<TutorialRevivePoint>
        {
            new TutorialRevivePoint(25f, 0f, TutorialArea.Zone2),
            new TutorialRevivePoint(-15f, 0f, TutorialArea.BossEnter),
        };
        Assert.That(TutorialRevivePointRules.TrySelect(c, null, mask, 25f, 0f, out _, out _), Is.False);

        c.Add(new TutorialRevivePoint(5f, 0f, TutorialArea.Zone1));
        Assert.That(TutorialRevivePointRules.TrySelect(c, null, mask, 25f, 0f, out int i, out bool safe), Is.True);
        Assert.That(i, Is.EqualTo(2));
        Assert.That(safe, Is.True);

        Assert.That(TutorialRevivePointRules.TrySelect(new List<TutorialRevivePoint>(), null, mask, 0f, 0f, out _, out _), Is.False);
    }
}
