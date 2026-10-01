using NUnit.Framework;
using UnityEngine;

// 기획: Docs/design/boss/boss-electric-floor.md §4·§5·§11 · PLAN-boss-electric-drone.md S1.
public sealed class BossElectricFloorPatternTests
{
    const int Trials = 500;

    // ── 타일 마스크 개수 (§4.1 · §5.3) ──────────────────────────

    [Test]
    public void Normal_CrossIs13Tiles_AndAvoidsLastLines()
    {
        var p = new BossElectricFloorPatterns(new System.Random(1));
        int prevR = -1, prevC = -1;
        for (int i = 0; i < Trials; i++)
        {
            ulong m = p.NextNormal();
            Assert.That(BossTileGrid.Count(m), Is.EqualTo(13), "교차 칸은 한 번만 센다");
            Assert.That(p.LastRow, Is.Not.EqualTo(prevR), "직전 첫 번째 축 줄 제외");
            Assert.That(p.LastCol, Is.Not.EqualTo(prevC), "직전 두 번째 축 줄 제외");
            prevR = p.LastRow;
            prevC = p.LastCol;
        }
    }

    [Test]
    public void B_StageMasks()
    {
        Assert.That(BossTileGrid.Count(BossElectricFloorPatterns.BInner), Is.EqualTo(25));
        Assert.That(BossTileGrid.Count(BossElectricFloorPatterns.BOddRows), Is.EqualTo(28));
        Assert.That(BossTileGrid.Count(BossElectricFloorPatterns.BOddCols), Is.EqualTo(28));
        // 바깥 테두리는 안전
        for (int i = 0; i < BossTileGrid.Size; i++)
        {
            Assert.That(BossTileGrid.Has(BossElectricFloorPatterns.BInner, 0, i), Is.False);
            Assert.That(BossTileGrid.Has(BossElectricFloorPatterns.BInner, 6, i), Is.False);
            Assert.That(BossTileGrid.Has(BossElectricFloorPatterns.BInner, i, 0), Is.False);
            Assert.That(BossTileGrid.Has(BossElectricFloorPatterns.BInner, i, 6), Is.False);
        }
    }

    [Test]
    public void A_PresetsAre4x4_FromEachCorner()
    {
        foreach (ulong a in BossElectricFloorPatterns.APresets)
            Assert.That(BossTileGrid.Count(a), Is.EqualTo(16));
        Assert.That(BossTileGrid.Has(BossElectricFloorPatterns.APresets[0], 0, 0), Is.True);   // A-1 (1,1)
        Assert.That(BossTileGrid.Has(BossElectricFloorPatterns.APresets[1], 6, 0), Is.True);   // A-2 (7,1)
        Assert.That(BossTileGrid.Has(BossElectricFloorPatterns.APresets[2], 0, 6), Is.True);   // A-3 (1,7)
        Assert.That(BossTileGrid.Has(BossElectricFloorPatterns.APresets[3], 6, 6), Is.True);   // A-4 (7,7)
    }

    // ── A: 서로 다른 2개 · 직전 조합 제외 · 겹침 1회 (§5.2) ────

    [Test]
    public void A_TwoDistinctPresets_NeverRepeatsLastCombo_OverlapCountedOnce()
    {
        var p = new BossElectricFloorPatterns(new System.Random(2));
        ulong prev = 0;
        for (int i = 0; i < Trials; i++)
        {
            ulong m = p.NextA();
            Assert.That(m, Is.Not.EqualTo(prev), "직전 A 와 완전히 같은 조합 금지");
            int n = BossTileGrid.Count(m);
            // 인접 두 개 = 16+16−4 = 28, 대각 두 개 = 16+16−1 = 31
            Assert.That(n == 28 || n == 31, Is.True, $"칸 수 {n}");
            prev = m;
        }
    }

    // ── 그룹 교대 · B 방향 교대 · 리셋 (§5.1 · §5.3 · §5.4) ────

    [Test]
    public void Groups_Alternate_AndResetAllowsEitherStart()
    {
        bool sawA = false, sawB = false;
        for (int seed = 0; seed < 40; seed++)
        {
            var p = new BossElectricFloorPatterns(new System.Random(seed));
            var g0 = p.NextGroup();
            if (g0 == BossElectricFloorPatterns.ChargeGroup.A) sawA = true; else sawB = true;
            for (int i = 0; i < 6; i++)
            {
                var g = p.NextGroup();
                Assert.That(g, Is.Not.EqualTo(g0), "같은 그룹 연속 금지");
                g0 = g;
            }
        }
        Assert.That(sawA && sawB, Is.True, "첫 그룹은 A·B 둘 다 나와야 한다(50%)");
    }

    [Test]
    public void BStage2_AlternatesDirection_ResetOnChargeEnd()
    {
        var p = new BossElectricFloorPatterns(new System.Random(3));
        p.NextBStage2(out bool first);
        for (int i = 0; i < 10; i++)
        {
            p.NextBStage2(out bool rows);
            Assert.That(rows, Is.Not.EqualTo(first));
            first = rows;
        }
        // 리셋 뒤에는 직전 방향 기록이 없다 — 여러 시드에서 양쪽 다 나온다.
        bool sawRows = false, sawCols = false;
        for (int seed = 0; seed < 40; seed++)
        {
            var q = new BossElectricFloorPatterns(new System.Random(seed));
            q.NextBStage2(out _);
            q.ResetCharge();
            q.NextBStage2(out bool r);
            if (r) sawRows = true; else sawCols = true;
        }
        Assert.That(sawRows && sawCols, Is.True);
    }

    [Test]
    public void ResetCharge_KeepsNormalLineHistory()
    {
        var p = new BossElectricFloorPatterns(new System.Random(4));
        p.NextNormal();
        int r = p.LastRow, c = p.LastCol;
        p.ResetCharge();
        Assert.That(p.LastRow, Is.EqualTo(r), "과충전·기믹 종료는 일반 장판 직전 줄 기록을 지우지 않는다(§8)");
        Assert.That(p.LastCol, Is.EqualTo(c));
        p.ResetAll();
        Assert.That(p.LastRow, Is.EqualTo(-1));
    }

    // ── 그리드: 방 로컬 · 회전 (PLAN §2) ───────────────────────

    [Test]
    public void Grid_FootPositionMapsToTile_InRotatedRoom()
    {
        // 28×28, 칸 4m, 방이 90° 돌아 배치됨.
        Matrix4x4 frame = Matrix4x4.TRS(new Vector3(100f, 0f, 50f), Quaternion.Euler(0f, 90f, 0f), Vector3.one);
        var g = new BossTileGrid(frame, -14f, 14f, -14f, 14f, 0f);
        Assert.That(g.TileSize, Is.EqualTo(4f).Within(1e-4));

        for (int r = 0; r < BossTileGrid.Size; r++)
            for (int c = 0; c < BossTileGrid.Size; c++)
            {
                Vector3 center = g.TileCenter(r, c);
                Assert.That(g.TryGetTile(center, out int rr, out int cc), Is.True);
                Assert.That((rr, cc), Is.EqualTo((r, c)));
            }

        // 방 로컬 (−14+2, +14−2) = 타일 (0,0) 중심 → 월드
        Vector3 w = frame.MultiplyPoint3x4(new Vector3(-12f, 0f, 12f));
        Assert.That(g.TryGetTile(w, out int r0, out int c0) && r0 == 0 && c0 == 0, Is.True);
        // 방 밖
        Assert.That(g.TryGetTile(frame.MultiplyPoint3x4(new Vector3(15f, 0f, 0f)), out _, out _), Is.False);
    }
}
