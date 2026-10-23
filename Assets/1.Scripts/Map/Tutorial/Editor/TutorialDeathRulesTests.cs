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
    public void G11전_사망은_3초뒤_차감없이_안전지점_부활()
    {
        var m = new TutorialDeathRulesModel();
        m.RecordDeath(A, 10d, bossRoomReached: false);

        Assert.That(m.IsPenaltyFree(A), Is.True, "G11 전 = 목숨 수와 무관하게 Soul");
        Assert.That(Eval(m, A, 12.9d, PlayerLifeState.Soul, false, out _), Is.EqualTo(TutorialReviveVerdict.Waiting));
        Assert.That(Eval(m, A, 13d, PlayerLifeState.Soul, false, out TutorialReviveOrder o), Is.EqualTo(TutorialReviveVerdict.Revive));
        Assert.That(o.ConsumeLife, Is.False);
        Assert.That(o.InPlace, Is.False);
        Assert.That(m.HasPending(A), Is.False, "부활하면 기록이 지워진다");
    }

    [Test]
    public void G11뒤_사망은_기존규칙_목숨1개_차감()
    {
        int mask = MaskUpTo(TutorialGate.G11_BossRoom);
        Assert.That(TutorialDeathRulesModel.IsLifeChargedAt(mask), Is.True, "G11(보스방 도착) 뒤 = 차감");

        var m = new TutorialDeathRulesModel();
        m.RecordDeath(A, 0d, bossRoomReached: TutorialDeathRulesModel.IsLifeChargedAt(mask));

        Assert.That(m.IsPenaltyFree(A), Is.False, "G11 뒤 = 기존 목적지 규칙(목숨 0 → 완전 사망)");
        Assert.That(Eval(m, A, 3d, PlayerLifeState.Soul, false, out TutorialReviveOrder o), Is.EqualTo(TutorialReviveVerdict.Revive));
        Assert.That(o.ConsumeLife, Is.True);
    }

    [Test]
    public void G8뒤_G11전_사망은_차감없이_Soul_유지()
    {
        // 존4 전투·복도4·패드 대기(G10)·타이머 만료 강제 이동 중 — 타이머가 돌고 있어도 보스방 도착 전이면 차감 없음.
        foreach (TutorialGate last in new[] { TutorialGate.G8_TimerStart, TutorialGate.G9_Corridor4Entry, TutorialGate.G10_BossEnterAndPad })
            Assert.That(TutorialDeathRulesModel.IsLifeChargedAt(MaskUpTo(last)), Is.False, $"{last} 까지 열림 = G11 전");
        Assert.That(TutorialDeathRulesModel.IsLifeChargedAt(0), Is.False, "아무 게이트도 안 열림");

        var m = new TutorialDeathRulesModel();
        m.RecordDeath(A, 0d, bossRoomReached: TutorialDeathRulesModel.IsLifeChargedAt(MaskUpTo(TutorialGate.G10_BossEnterAndPad)));
        Assert.That(m.IsPenaltyFree(A), Is.True, "목숨 수와 무관하게 Soul");

        // 타이머 만료 강제 이동 중 사망도 같다(그 자리 즉시 부활, 차감 없음).
        m.BeginMove();
        m.RecordDeath(B, 0d, bossRoomReached: TutorialDeathRulesModel.IsLifeChargedAt(MaskUpTo(TutorialGate.G8_TimerStart)));
        Assert.That(m.IsPenaltyFree(B), Is.True);
        Assert.That(Eval(m, B, 0.1d, PlayerLifeState.Soul, false, out TutorialReviveOrder b), Is.EqualTo(TutorialReviveVerdict.Revive));
        Assert.That(b.ConsumeLife, Is.False);
        Assert.That(b.InPlace, Is.True);

        Assert.That(Eval(m, A, 3d, PlayerLifeState.Soul, false, out TutorialReviveOrder a), Is.EqualTo(TutorialReviveVerdict.Revive));
        Assert.That(a.ConsumeLife, Is.False);
    }

    [Test]
    public void 차감여부는_사망시점_단계로_고정된다()
    {
        // 대기 중에 G11 이 열려도 G11 전 사망은 차감 없음 — 위치는 아예 입력이 아니다(단계 기준).
        var m = new TutorialDeathRulesModel();
        m.RecordDeath(A, 0d, bossRoomReached: false);
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
    public void 집결_카운트다운_중_사망은_카운트가_취소돼도_보스진입_고정지점_부활()
    {
        // 할 일 89 — 사망 순간 집결이 깨져 카운트다운이 취소되고(EndMove) 그 뒤에 Soul 이 된다.
        var m = new TutorialDeathRulesModel();
        m.BeginMove();
        m.RecordDeath(A, 0d, bossRoomReached: false);
        m.EndMove();

        Assert.That(m.IsMoveInProgress, Is.False);
        Assert.That(Eval(m, A, 0.5d, PlayerLifeState.Soul, false, out TutorialReviveOrder o), Is.EqualTo(TutorialReviveVerdict.Revive));
        Assert.That(o.InPlace, Is.True);
        Assert.That(TutorialDeathRulesModel.RevivesAtBossEnterPoint(o, finished: false), Is.True, "보스방 도착 전 즉시 주문 = 보스 진입 고정 지점");
        Assert.That(TutorialDeathRulesModel.RevivesAtBossEnterPoint(o, finished: true), Is.False, "도착 뒤 = 그 자리(기존)");

        // 이동과 무관한 3초 대기 부활은 고정 지점이 아니다(지금 단계 구간의 안전 지점).
        m.RecordDeath(B, 1d, bossRoomReached: false);
        Assert.That(Eval(m, B, 4d, PlayerLifeState.Soul, false, out TutorialReviveOrder b), Is.EqualTo(TutorialReviveVerdict.Revive));
        Assert.That(TutorialDeathRulesModel.RevivesAtBossEnterPoint(b, finished: false), Is.False);
    }

    [Test]
    public void 이동_시작하면_대기부활을_취소하고_즉시_그자리_부활()
    {
        var m = new TutorialDeathRulesModel();
        m.RecordDeath(A, 0d, bossRoomReached: false);
        m.RecordDeath(B, 0d, bossRoomReached: true);

        Assert.That(m.BeginMove(), Is.EqualTo(2));
        Assert.That(m.BeginMove(), Is.Zero, "이미 진행 중이면 다시 바꾸지 않는다");

        // 3초 전이어도 Soul 이면 바로.
        Assert.That(Eval(m, A, 1.6d, PlayerLifeState.Soul, false, out TutorialReviveOrder a), Is.EqualTo(TutorialReviveVerdict.Revive));
        Assert.That(a.InPlace, Is.True);
        Assert.That(a.ConsumeLife, Is.False, "G11 전 사망이면 이동 중 즉시 부활도 차감 없음");

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
    public void 지금_단계_구간은_디렉터_단계_기준_배리어가_상한()
    {
        // 출구 게이트만 열리고 아직 아무도 다음 존에 안 들어감 — 배리어로는 다음 존이지만 단계는 복도.
        Assert.That(TutorialRevivePointRules.ResolveStageArea(TutorialStep.Corridor1, MaskUpTo(TutorialGate.G2_Zone2Entry)), Is.EqualTo(TutorialArea.Corridor1));
        Assert.That(TutorialRevivePointRules.ResolveStageArea(TutorialStep.Zone2, MaskUpTo(TutorialGate.G2_Zone2Entry)), Is.EqualTo(TutorialArea.Zone2));
        Assert.That(TutorialRevivePointRules.ResolveStageArea(TutorialStep.Corridor2, MaskUpTo(TutorialGate.G4_Zone3Entry)), Is.EqualTo(TutorialArea.Corridor2));
        Assert.That(TutorialRevivePointRules.ResolveStageArea(TutorialStep.Zone3Fight, MaskUpTo(TutorialGate.G5_SpinnerRealMode)), Is.EqualTo(TutorialArea.Zone3));
        Assert.That(TutorialRevivePointRules.ResolveStageArea(TutorialStep.Corridor3, MaskUpTo(TutorialGate.G7_Zone4Entry)), Is.EqualTo(TutorialArea.Corridor3));
        Assert.That(TutorialRevivePointRules.ResolveStageArea(TutorialStep.Zone4Combat, MaskUpTo(TutorialGate.G8_TimerStart)), Is.EqualTo(TutorialArea.Zone4));
        // 입구 게이트와 같이 바뀌는 구간은 두 기준이 같다.
        Assert.That(TutorialRevivePointRules.ResolveStageArea(TutorialStep.Corridor2, MaskUpTo(TutorialGate.G3_Corridor2Entry)), Is.EqualTo(TutorialArea.Corridor2));
        // 보스 진입·종료 = 복도4(패드 존 제외).
        Assert.That(TutorialRevivePointRules.ResolveStageArea(TutorialStep.BossEnter, MaskUpTo(TutorialGate.G10_BossEnterAndPad)), Is.EqualTo(TutorialArea.Corridor4));
        Assert.That(TutorialRevivePointRules.ResolveStageArea(TutorialStep.Finished, MaskUpTo(TutorialGate.G10_BossEnterAndPad)), Is.EqualTo(TutorialArea.Corridor4));
        // 단계가 배리어보다 앞서도(디버그 단계 변경 등) 닫힌 구간으로 넘지 않는다.
        Assert.That(TutorialRevivePointRules.ResolveStageArea(TutorialStep.Zone2, Mask(TutorialGate.G1_Corridor1Entry)), Is.EqualTo(TutorialArea.Corridor1));
        Assert.That(TutorialRevivePointRules.ResolveStageArea(TutorialStep.None, 0), Is.EqualTo(TutorialArea.Zone1));
    }

    [Test]
    public void 단계_기준_선택은_출구만_열린_다음_존에서_되살리지_않는다()
    {
        int mask = MaskUpTo(TutorialGate.G2_Zone2Entry);
        var c = new List<TutorialRevivePoint>
        {
            new TutorialRevivePoint(25f, 0f, TutorialArea.Zone2),
            new TutorialRevivePoint(12f, 0f, TutorialArea.Corridor1),
        };

        Assert.That(TutorialRevivePointRules.TrySelect(c, null, TutorialStep.Corridor1, mask, 25f, 0f, out int i, out bool safe), Is.True);
        Assert.That(i, Is.EqualTo(1), "단계 Corridor1 — 존2 는 아직 지금 단계 구간이 아니다");
        Assert.That(safe, Is.True);

        Assert.That(TutorialRevivePointRules.TrySelect(c, null, TutorialStep.Zone2, mask, 25f, 0f, out i, out _), Is.True);
        Assert.That(i, Is.EqualTo(0), "파티가 존2 에 들어간 뒤엔 존2");

        Assert.That(TutorialRevivePointRules.AreaOrder(TutorialStep.Corridor1, mask),
            Is.EqualTo(new[] { TutorialArea.Corridor1, TutorialArea.Zone1 }));
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

    // ─── 개인 뒤 배리어 예외(15 후속 C2) ──────────────────────────────────

    [Test]
    public void 뒤배리어_없으면_기존_단계_기준과_같다()
    {
        int mask = MaskUpTo(TutorialGate.G2_Zone2Entry);
        Assert.That(TutorialRevivePointRules.AreaOrder(TutorialStep.Corridor1, mask, 0),
            Is.EqualTo(TutorialRevivePointRules.AreaOrder(TutorialStep.Corridor1, mask)));
        Assert.That(TutorialRevivePointRules.ResolveStageArea(TutorialStep.Zone2, mask, 0), Is.EqualTo(TutorialArea.Zone2));
    }

    [Test]
    public void 앞서간_사람은_지나온_최전방_구간_이상에서_부활()
    {
        int mask = MaskUpTo(TutorialGate.G3_Corridor2Entry);   // 파티 단계는 아직 Corridor1
        int passed = Mask(TutorialGate.G1_Corridor1Entry, TutorialGate.G2_Zone2Entry);   // A 혼자 존2 까지
        Assert.That(TutorialRevivePointRules.ResolveStageArea(TutorialStep.Corridor1, mask, passed), Is.EqualTo(TutorialArea.Zone2),
            "단계 구간(복도1)이 뒤 배리어 너머 → 존2 로 올린다");
        Assert.That(TutorialRevivePointRules.AreaOrder(TutorialStep.Corridor1, mask, passed), Is.EqualTo(new[] { TutorialArea.Zone2 }),
            "복도1·존1 은 그 사람의 뒤 배리어 너머 — 찾지 않는다");

        Assert.That(TutorialRevivePointRules.AreaOrder(TutorialStep.Zone3Fight, MaskUpTo(TutorialGate.G4_Zone3Entry), passed),
            Is.EqualTo(new[] { TutorialArea.Zone3, TutorialArea.Corridor2, TutorialArea.Zone2 }), "단계가 더 앞이면 단계 기준 — 최전방 구간에서 멈춘다");
    }

    [Test]
    public void 뒤배리어_너머_후보는_폴백에서도_쓰지_않는다()
    {
        int mask = MaskUpTo(TutorialGate.G2_Zone2Entry);
        int passed = Mask(TutorialGate.G1_Corridor1Entry, TutorialGate.G2_Zone2Entry);
        var c = new List<TutorialRevivePoint>
        {
            new TutorialRevivePoint(12f, 0f, TutorialArea.Corridor1),   // 사망 지점 옆이지만 뒤 배리어 너머
            new TutorialRevivePoint(0f, 0f, TutorialArea.Zone1),
            new TutorialRevivePoint(25f, 0f, TutorialArea.Zone2),       // 몬스터 반경 안
        };
        var threats = new List<TutorialThreat> { new TutorialThreat(25f, 0f, 100f) };

        Assert.That(TutorialRevivePointRules.TrySelect(c, threats, TutorialStep.Corridor1, mask, passed, 12f, 0f, out int i, out bool safe), Is.True);
        Assert.That(i, Is.EqualTo(2), "안전한 곳이 뒤에만 있어도 뒤로 보내지 않는다 — 최전방 구간에서 가장 먼 후보");
        Assert.That(safe, Is.False);

        c.RemoveAt(2);
        Assert.That(TutorialRevivePointRules.TrySelect(c, null, TutorialStep.Corridor1, mask, passed, 12f, 0f, out _, out _), Is.False,
            "최전방 구간 후보가 없으면 실패 → 호출자가 그 자리 부활");

        Assert.That(TutorialRevivePointRules.TrySelect(c, null, TutorialStep.Corridor1, mask, 0, 12f, 0f, out i, out _), Is.True);
        Assert.That(i, Is.EqualTo(0), "뒤에 남은 동료(마스크 0)는 기존대로 복도1");
    }

    [Test]
    public void G10을_지난_사람만_보스진입에서_부활()
    {
        int mask = MaskUpTo(TutorialGate.G10_BossEnterAndPad);
        int passed = MaskUpTo(TutorialGate.G10_BossEnterAndPad) & TutorialBackBarrierRules.AllBarrierMask;
        var c = new List<TutorialRevivePoint>
        {
            new TutorialRevivePoint(-5f, 20f, TutorialArea.Corridor4),
            new TutorialRevivePoint(-15f, 20f, TutorialArea.BossEnter),
        };

        Assert.That(TutorialRevivePointRules.TrySelect(c, null, TutorialStep.BossEnter, mask, passed, -15f, 20f, out int i, out _), Is.True);
        Assert.That(i, Is.EqualTo(1), "복도4 는 그 사람의 G10 뒤 배리어 너머");

        int passedToCorridor4 = passed & ~Mask(TutorialGate.G10_BossEnterAndPad);
        Assert.That(TutorialRevivePointRules.TrySelect(c, null, TutorialStep.BossEnter, mask, passedToCorridor4, -15f, 20f, out i, out _), Is.True);
        Assert.That(i, Is.EqualTo(0), "G10 을 안 지났으면 기존대로 보스 진입 존 제외(복도4)");
    }

    // ─── 보스 진입 패드 제외(15 후속 C3) ─────────────────────────────────

    const float PadMargin = TutorialRevivePointRules.DefaultPadMargin;
    const float Clear = TutorialBackBarrierRules.DefaultClearance;

    static int AllOpen => MaskUpTo(TutorialGate.G10_BossEnterAndPad);
    static int PassedG10 => MaskUpTo(TutorialGate.G10_BossEnterAndPad) & TutorialBackBarrierRules.AllBarrierMask;

    // 보스 진입 존 후보 — 패드 위 · 패드 여유(1m) 안 · 여유 밖
    static List<TutorialRevivePoint> BossEnterCandidates() => new List<TutorialRevivePoint>
    {
        new TutorialRevivePoint(-15f, 20f, TutorialArea.BossEnter),
        new TutorialRevivePoint(-12.5f, 20f, TutorialArea.BossEnter),
        new TutorialRevivePoint(-11.5f, 20f, TutorialArea.BossEnter),
    };

    // TutorialBackBarrierRulesTests 와 같은 스테이지 — 복도4 x −10~0(−x 진행) → 보스 진입 x −20~−10. G10 앞면 x −10.
    static TutorialBarrierSpec G10Spec()
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
        foreach (TutorialBarrierSpec s in TutorialBarrierRules.BuildSpecs(t, TutorialBarrierRules.DefaultThickness,
                                                                         TutorialBarrierRules.DefaultSideOverlap, out _))
            if (s.Gate == TutorialGate.G10_BossEnterAndPad) return s;
        Assert.Fail("G10 배리어 없음");
        return default;
    }

    [Test]
    public void 패드_사각형_여유안_후보는_빼고_밖_후보는_쓴다()
    {
        var pads = new List<TutorialPadArea> { TutorialPadArea.Box(-17f, 18f, -13f, 22f) };   // 4×4, 가장자리 x −13
        Assert.That(TutorialRevivePointRules.TrySelect(BossEnterCandidates(), null, TutorialStep.BossEnter, AllOpen, PassedG10, -15f, 20f,
                                                       pads, PadMargin, out int i, out bool safe), Is.True);
        Assert.That(i, Is.EqualTo(2), "패드 위(−15)·여유 1m 안(−12.5) 제외 — 여유 밖(−11.5)");
        Assert.That(safe, Is.True);
    }

    [Test]
    public void 패드_원_여유안_후보는_빼고_밖_후보는_쓴다()
    {
        var pads = new List<TutorialPadArea> { TutorialPadArea.Circle(-15f, 20f, 2f) };   // 반경 2 + 여유 1 = 3
        Assert.That(TutorialRevivePointRules.TrySelect(BossEnterCandidates(), null, TutorialStep.BossEnter, AllOpen, PassedG10, -15f, 20f,
                                                       pads, PadMargin, out int i, out _), Is.True);
        Assert.That(i, Is.EqualTo(2), "거리 2.5 는 제외, 3.5 는 사용");

        Assert.That(TutorialRevivePointRules.IsOnPad(pads, -15f, 22.9f, PadMargin), Is.True);
        Assert.That(TutorialRevivePointRules.IsOnPad(pads, -15f, 23.1f, PadMargin), Is.False);
    }

    [Test]
    public void 패드_후보는_가장_먼_후보_폴백에서도_쓰지_않는다()
    {
        var pads = new List<TutorialPadArea> { TutorialPadArea.Box(-17f, 18f, -13f, 22f) };
        var threats = new List<TutorialThreat> { new TutorialThreat(-11.5f, 20f, 100f) };   // 전부 위험 — 패드 위가 가장 멀다
        Assert.That(TutorialRevivePointRules.TrySelect(BossEnterCandidates(), threats, TutorialStep.BossEnter, AllOpen, PassedG10, -15f, 20f,
                                                       pads, PadMargin, out int i, out bool safe), Is.True);
        Assert.That(i, Is.EqualTo(2));
        Assert.That(safe, Is.False);
    }

    [Test]
    public void 후보가_전부_패드면_실패하고_폴백은_G10_앞_패드밖_여유밖()
    {
        var pads = new List<TutorialPadArea> { TutorialPadArea.Box(-17f, 17f, -11.9f, 23f) };   // + 여유 1 → x −18~−10.9 (후보 전부 · 1m 지점 x −11 포함)
        Assert.That(TutorialRevivePointRules.TrySelect(BossEnterCandidates(), null, TutorialStep.BossEnter, AllOpen, PassedG10, -15f, 20f,
                                                       pads, PadMargin, out _, out _), Is.False);

        TutorialBarrierSpec g10 = G10Spec();
        var none = new List<(float x, float z)>();
        TutorialRevivePointRules.PadFallbackCandidates(g10, null, PadMargin, TutorialRevivePointRules.DefaultPadFallbackDistance, Clear, none);
        Assert.That(none[0].x, Is.EqualTo(-11f).Within(1e-4), "기본 = G10 앞면(x −10)에서 보스 진입 쪽 1m, 가운데");
        Assert.That(none[0].z, Is.EqualTo(20f).Within(1e-4));

        // 1m 지점(x −11)은 패드 여유 안 → 배리어 쪽 여유 0.75m 지점(x −10.75)
        var pts = new List<(float x, float z)>();
        TutorialRevivePointRules.PadFallbackCandidates(g10, pads, PadMargin, TutorialRevivePointRules.DefaultPadFallbackDistance, Clear, pts);
        Assert.That(pts.Count, Is.GreaterThan(0));
        Assert.That(pts[0].x, Is.EqualTo(-10.75f).Within(1e-4));
        AssertFallbackPoints(g10, pads, pts);

        // 앞 두 지점이 모두 패드 여유 안 → 배리어를 따라 옆으로
        var side = new List<TutorialPadArea> { TutorialPadArea.Box(-14f, 18.6f, -10.5f, 19.6f) };   // + 여유 → x −15~−9.5 · z 17.6~20.6
        pts.Clear();
        TutorialRevivePointRules.PadFallbackCandidates(g10, side, PadMargin, TutorialRevivePointRules.DefaultPadFallbackDistance, Clear, pts);
        Assert.That(pts.Count, Is.GreaterThan(0), "가로 여유(±1.3m) 안에 패드 밖 점이 있다");
        Assert.That(pts[0].x, Is.EqualTo(-11f).Within(1e-4));
        Assert.That(pts[0].z, Is.EqualTo(21f).Within(1e-4), "가운데·±0.5m 는 패드 여유 안 → +1m 옆");
        AssertFallbackPoints(g10, side, pts);
    }

    static void AssertFallbackPoints(TutorialBarrierSpec g10, List<TutorialPadArea> pads, List<(float x, float z)> pts)
    {
        foreach ((float x, float z) in pts)
        {
            Assert.That(TutorialRevivePointRules.IsOnPad(pads, x, z, PadMargin), Is.False, $"({x},{z}) 패드 밖");
            Assert.That(TutorialBackBarrierRules.DistancePastFace(g10, x, z), Is.GreaterThanOrEqualTo(Clear - 1e-4f), $"({x},{z}) 뒤 배리어 여유 밖");
            Assert.That(x, Is.LessThan(-10f), "보스 진입 존 쪽");
        }
    }

    [Test]
    public void 패드_정보가_없으면_기존_선택과_같다()
    {
        List<TutorialRevivePoint> c = BossEnterCandidates();
        var threats = new List<TutorialThreat> { new TutorialThreat(-11.5f, 20f, 1f) };
        Assert.That(TutorialRevivePointRules.TrySelect(c, threats, TutorialStep.BossEnter, AllOpen, PassedG10, -12f, 20f, out int expected, out bool expectedSafe), Is.True);

        foreach (List<TutorialPadArea> pads in new[] { null, new List<TutorialPadArea>() })
        {
            Assert.That(TutorialRevivePointRules.TrySelect(c, threats, TutorialStep.BossEnter, AllOpen, PassedG10, -12f, 20f,
                                                           pads, PadMargin, out int i, out bool safe), Is.True);
            Assert.That(i, Is.EqualTo(expected));
            Assert.That(safe, Is.EqualTo(expectedSafe));
        }
        Assert.That(TutorialRevivePointRules.IsOnPad(null, 0f, 0f, PadMargin), Is.False);
    }
}
