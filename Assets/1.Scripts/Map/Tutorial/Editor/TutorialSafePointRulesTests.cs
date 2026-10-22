using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 튜토리얼 스텝별 고정 안전 지점(할 일 80) 순수 규칙(<see cref="TutorialSafePointRules"/>) —
/// 감시 조건·구간 순서(부활과 같음)·저작 기본 위치·점유 레이어·막힌 지점 주변 빈자리·최후 수단. 문서: Docs/tech/tutorial-director.md §12.
/// </summary>
public sealed class TutorialSafePointRulesTests
{
    // 존1 (0..10) → 복도1 (10..20, 폭 4) → 존2 (20..30). 진행 = +X.
    static TutorialAreaTracker Layout()
    {
        var t = new TutorialAreaTracker();
        t.SetArea(TutorialArea.Zone1, TutorialRect.FromMinMax(0f, -5f, 10f, 5f));
        t.SetArea(TutorialArea.Corridor1, TutorialRect.FromMinMax(10f, -2f, 20f, 2f));
        t.SetArea(TutorialArea.Zone2, TutorialRect.FromMinMax(20f, -5f, 30f, 5f));
        return t;
    }

    static int Mask(params TutorialGate[] gates)
    {
        int m = 0;
        foreach (TutorialGate g in gates) m |= 1 << (int)g;
        return m;
    }

    // ─── 감시 조건 ────────────────────────────────────────────────────────

    [Test]
    public void ShouldWatch_OnlyWhileTutorialRuns()
    {
        Assert.IsTrue(TutorialSafePointRules.ShouldWatch(true, TutorialStep.Zone2, false));
        Assert.IsFalse(TutorialSafePointRules.ShouldWatch(false, TutorialStep.Zone2, false), "튜토리얼 아님");
        Assert.IsFalse(TutorialSafePointRules.ShouldWatch(true, TutorialStep.None, false), "활성 전");
        Assert.IsFalse(TutorialSafePointRules.ShouldWatch(true, TutorialStep.Finished, false), "보스방 도착 뒤 = 일반 규칙");
        Assert.IsFalse(TutorialSafePointRules.ShouldWatch(true, TutorialStep.BossEnter, true), "보스방 이동(카운트다운·도착 대기) 중");
    }

    // ─── 구간 순서 = 자동 부활 ───────────────────────────────────────────

    [Test]
    public void AreaOrder_MatchesRevive_StepAreaDownToFrontmost()
    {
        int mask = Mask(TutorialGate.G1_Corridor1Entry, TutorialGate.G2_Zone2Entry);
        CollectionAssert.AreEqual(new[] { TutorialArea.Zone2, TutorialArea.Corridor1, TutorialArea.Zone1 },
            TutorialSafePointRules.AreaOrder(TutorialStep.Zone2, mask, 0), "단계 = 존2, 뒤 배리어 없음");

        int passed = Mask(TutorialGate.G1_Corridor1Entry);
        CollectionAssert.AreEqual(new[] { TutorialArea.Zone2, TutorialArea.Corridor1 },
            TutorialSafePointRules.AreaOrder(TutorialStep.Zone2, mask, passed), "G1 을 지난 사람은 존1 로 안 돌아간다");

        CollectionAssert.AreEqual(TutorialRevivePointRules.AreaOrder(TutorialStep.Corridor1, mask, passed),
            TutorialSafePointRules.AreaOrder(TutorialStep.Corridor1, mask, passed));
    }

    // ─── 저작 기본 위치 ──────────────────────────────────────────────────

    [Test]
    public void DefaultAnchor_Zone1Center_OthersInsideEntryBarrier()
    {
        TutorialAreaTracker t = Layout();
        var specs = new List<TutorialBarrierSpec>
        {
            new TutorialBarrierSpec(TutorialGate.G1_Corridor1Entry, 1, true, TutorialRect.FromMinMax(10f, -2.3f, 10.4f, 2.3f), true, 1f),
            new TutorialBarrierSpec(TutorialGate.G2_Zone2Entry, 1, false, TutorialRect.FromMinMax(19.6f, -2.3f, 20f, 2.3f), true, 1f),
        };

        Assert.IsTrue(TutorialSafePointRules.TryDefaultAnchor(TutorialArea.Zone1, t, specs, 2.5f, out float x, out float z));
        Assert.AreEqual(5f, x, 1e-4);
        Assert.AreEqual(0f, z, 1e-4);

        Assert.IsTrue(TutorialSafePointRules.TryDefaultAnchor(TutorialArea.Corridor1, t, specs, 1.5f, out x, out z));
        Assert.AreEqual(11.9f, x, 1e-4, "입구 배리어 앞면 10.4 + 1.5");
        Assert.AreEqual(0f, z, 1e-4);

        Assert.IsTrue(TutorialSafePointRules.TryDefaultAnchor(TutorialArea.Zone2, t, specs, 2.5f, out x, out z));
        Assert.AreEqual(22.5f, x, 1e-4, "복도1 출구 배리어 앞면 20 + 2.5");

        Assert.IsFalse(TutorialSafePointRules.TryDefaultAnchor(TutorialArea.Zone3, t, specs, 2.5f, out _, out _), "구간 없음");
    }

    [Test]
    public void DefaultAnchor_NegativeForward_GoesTowardMinus()
    {
        var t = new TutorialAreaTracker();
        t.SetArea(TutorialArea.Corridor2, TutorialRect.FromMinMax(-2f, -190f, 2f, -180f));
        var specs = new List<TutorialBarrierSpec>
        {
            // 복도2 입구 — 진행 = −Z (z 큰 쪽에서 작은 쪽으로)
            new TutorialBarrierSpec(TutorialGate.G3_Corridor2Entry, 2, true, TutorialRect.FromMinMax(-2.3f, -180.4f, 2.3f, -180f), false, -1f),
        };
        Assert.IsTrue(TutorialSafePointRules.TryDefaultAnchor(TutorialArea.Corridor2, t, specs, 1.5f, out float x, out float z));
        Assert.AreEqual(0f, x, 1e-4);
        Assert.AreEqual(-181.9f, z, 1e-4, "앞면 −180.4 에서 −Z 로 1.5");
    }

    // ─── 점유 · 막힌 지점 · 최후 수단 ─────────────────────────────────────

    [Test]
    public void BlockingLayers_StaticOnly_MonsterOverlapKeepsPoint()
    {
        foreach (string moving in new[] { "Enemy", "EnemyHurtBox", "Player", "PlayerHurtbox", "Corpse", "Soul", "Weapon", "Projectile", "" })
            Assert.IsFalse(TutorialSafePointRules.IsBlockingLayer(moving), $"{moving} — 움직이는 것은 지점을 무효로 만들지 않는다");
        foreach (string fixedLayer in new[] { "Default", "Ground", "Env", "Wall", "Barrier" })
            Assert.IsTrue(TutorialSafePointRules.IsBlockingLayer(fixedLayer), $"{fixedLayer} — 정적 장애물");
    }

    [Test]
    public void NearestFree_ObstacleOnPoint_PicksClosestRingSpotInsideArea()
    {
        TutorialRect zone1 = TutorialRect.FromMinMax(0f, -5f, 10f, 5f);
        // 지점 (5,0) 위에 반경 1m 기둥.
        System.Func<float, float, bool> free = (x, z) => (x - 5f) * (x - 5f) + z * z > 1f;

        Assert.IsTrue(TutorialSafePointRules.TryFindNearestFree(5f, 0f, zone1, TutorialSafePointRules.DefaultRingRadii, 8, free, out float x, out float z));
        Assert.AreEqual(6.5f, x, 1e-4, "0.75m 링은 전부 막힘 → 1.5m 링 첫 방향(+X)");
        Assert.AreEqual(0f, z, 1e-4);

        // 구간 가장자리 지점 (9.5,0) — +X 쪽 후보는 구간 밖이라 건너뛰고 구간 안 빈자리.
        System.Func<float, float, bool> freeEdge = (px, pz) => (px - 9.5f) * (px - 9.5f) + pz * pz > 1f;
        Assert.IsTrue(TutorialSafePointRules.TryFindNearestFree(9.5f, 0f, zone1, TutorialSafePointRules.DefaultRingRadii, 8, freeEdge, out x, out z));
        Assert.IsTrue(zone1.Contains(x, z), "구간 밖(벽 너머)은 고르지 않는다");
        Assert.AreEqual(9.5f, x, 1e-4);
        Assert.AreEqual(1.5f, z, 1e-4);

        Assert.IsFalse(TutorialSafePointRules.TryFindNearestFree(5f, 0f, zone1, TutorialSafePointRules.DefaultRingRadii, 8, (_, __) => false, out _, out _),
            "전부 막힘");
    }

    [Test]
    public void LastResort_AllBlocked_StaysAtStepPoint_NotStart()
    {
        int mask = Mask(TutorialGate.G1_Corridor1Entry, TutorialGate.G2_Zone2Entry);
        List<TutorialArea> order = TutorialSafePointRules.AreaOrder(TutorialStep.Zone2, mask, 0);

        Assert.IsTrue(TutorialSafePointRules.TryPickLastResort(order, _ => true, out TutorialArea area));
        Assert.AreEqual(TutorialArea.Zone2, area, "지금 단계 구간 — 존1(씬 시작)로 안 간다");

        Assert.IsTrue(TutorialSafePointRules.TryPickLastResort(order, a => a != TutorialArea.Zone2, out area));
        Assert.AreEqual(TutorialArea.Corridor1, area, "지금 구간 지점이 없으면 바로 뒤 구간");

        Assert.IsFalse(TutorialSafePointRules.TryPickLastResort(order, _ => false, out area), "지점 미배치 = 기존 동작");
        Assert.AreEqual(TutorialArea.None, area);
    }
}
