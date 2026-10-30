#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

/// <summary>할 일 169 — 10분 시나리오 출격 집결 자리·인원(<see cref="QASortieGather.TrySlot"/>), 2차 막힘 우회(정체·막힌 다리·옆걸음)·설 수 있는 자리 고르기.</summary>
public sealed class QASortieGatherTests
{
    [Test]
    public void 자리는_스폰된_id_중_나보다_작은_수()
    {
        ulong[] ids = { 0, 1, 2 };
        Assert.IsTrue(QASortieGather.TrySlot(0, ids, 3, out int s0, out int c0));
        Assert.IsTrue(QASortieGather.TrySlot(1, ids, 3, out int s1, out _));
        Assert.IsTrue(QASortieGather.TrySlot(2, ids, 3, out int s2, out _));
        Assert.AreEqual(0, s0);
        Assert.AreEqual(1, s1);
        Assert.AreEqual(2, s2);
        Assert.AreEqual(3, c0);
    }

    [Test]
    public void 목록_순서와_id_간격에_무관하다()
    {
        ulong[] ids = { 7, 0, 3 };
        QASortieGather.TrySlot(3, ids, 3, out int slot, out int count);
        Assert.AreEqual(1, slot);
        Assert.AreEqual(3, count);
    }

    [Test]
    public void 인원은_기대_인원과_스폰_인원_중_큰_값()
    {
        ulong[] two = { 0, 1 };
        QASortieGather.TrySlot(1, two, 3, out _, out int moreExpected);
        Assert.AreEqual(3, moreExpected);

        ulong[] four = { 0, 1, 2, 3 };
        QASortieGather.TrySlot(1, four, 3, out _, out int moreSpawned);
        Assert.AreEqual(4, moreSpawned);
    }

    [Test]
    public void 내가_목록에_없으면_false_자리0()
    {
        ulong[] ids = { 0, 1 };
        Assert.IsFalse(QASortieGather.TrySlot(5, ids, 1, out int slot, out int count));
        Assert.AreEqual(0, slot);
        Assert.AreEqual(2, count);
        Assert.IsFalse(QASortieGather.TrySlot(5, null, 0, out _, out int none));
        Assert.AreEqual(1, none);
    }

    [Test]
    public void 멀티면_clientId마다_구역_안_다른_자리()
    {
        ulong[] ids = { 0, 1, 2 };
        var offsets = new System.Collections.Generic.HashSet<float>();
        foreach (ulong id in ids)
        {
            QASortieGather.TrySlot(id, ids, 3, out int slot, out int count);
            float off = QABoss103Plan.PadLineOffset(slot, count, 3f, 1.1f);
            Assert.LessOrEqual(System.Math.Abs(off), 3f * 0.8f + 1e-4f);
            offsets.Add(off);
        }
        Assert.AreEqual(3, offsets.Count);
    }

    // ── 2차: 막힘 우회·자리 고르기 ──────────────────────────────

    [Test]
    public void 정체는_창_안에_최소_진척을_못_내면_true()
    {
        var stall = new QAWalkStall(2f, 0.35f);
        Assert.IsFalse(stall.Tick(new Vector3(0, 0, 0), 0f));
        Assert.IsFalse(stall.Tick(new Vector3(0.1f, 0, 0), 1f));
        Assert.IsFalse(stall.Tick(new Vector3(0.2f, 0, 0), 1.9f));
        Assert.IsTrue(stall.Tick(new Vector3(0.2f, 0, 0), 2.1f));
    }

    [Test]
    public void 정체는_진척이_나면_기준을_옮기고_높이는_무시한다()
    {
        var stall = new QAWalkStall(2f, 0.35f);
        stall.Tick(Vector3.zero, 0f);
        Assert.IsFalse(stall.Tick(new Vector3(0.5f, 0, 0), 1.5f)); // 진척 → 기준 1.5초
        Assert.IsFalse(stall.Tick(new Vector3(0.5f, 1.0f, 0), 3.0f)); // 벽 타기 상승은 진척이 아니다
        Assert.IsTrue(stall.Tick(new Vector3(0.5f, 1.2f, 0), 3.6f));
        stall.Reset();
        Assert.IsFalse(stall.Tick(new Vector3(0.5f, 0, 0), 10f));
    }

    [Test]
    public void 막힌_다리는_근처_출발_같은_도착만_막는다()
    {
        var memory = new QAWalkMemory();
        memory.Block(new Vector3(0, 0, 0), new Vector3(5, 0, 0));
        Assert.IsTrue(memory.IsBlocked(new Vector3(1, 0, 0.5f), new Vector3(5.2f, 1, 0)));  // 비켜 선 자리에서 같은 목표
        Assert.IsFalse(memory.IsBlocked(new Vector3(3, 0, 0), new Vector3(5, 0, 0)));       // 출발이 멀다
        Assert.IsFalse(memory.IsBlocked(new Vector3(0, 0, 0), new Vector3(5, 0, 2)));       // 다른 목표(경유점)
        memory.Clear();
        Assert.AreEqual(0, memory.BlockedCount);
        Assert.IsFalse(memory.IsBlocked(new Vector3(0, 0, 0), new Vector3(5, 0, 0)));
    }

    [Test]
    public void 막힌_다리를_빼면_경유점으로_돌아간다()
    {
        // 0=출발(0,0) 1=경유(0,5) 2=도착(5,0). 직선은 판정상 열렸지만 실제로 막혔다 → 기억 뒤 경유.
        Vector3[] p = { new Vector3(0, 0, 0), new Vector3(0, 0, 5), new Vector3(5, 0, 0) };
        var memory = new QAWalkMemory();
        System.Func<int, int, float> dist = (a, b) => Vector3.Distance(p[a], p[b]);
        CollectionAssert.AreEqual(new[] { 2 }, QABoss103Plan.WalkRoute(3, (a, b) => !memory.IsBlocked(p[a], p[b]), dist));
        memory.Block(p[0], p[2]);
        CollectionAssert.AreEqual(new[] { 1, 2 }, QABoss103Plan.WalkRoute(3, (a, b) => !memory.IsBlocked(p[a], p[b]), dist));
    }

    [Test]
    public void 옆걸음은_좌우를_번갈아_진행_방향에_수직()
    {
        Vector3 travel = new Vector3(0, 0.7f, 2f);
        Vector3 left = QASortieGather.SidestepSide(travel, 0);
        Vector3 right = QASortieGather.SidestepSide(travel, 1);
        Assert.AreEqual(0f, Vector3.Dot(left, new Vector3(0, 0, 1)), 1e-4f);
        Assert.AreEqual(1f, left.magnitude, 1e-4f);
        Assert.AreEqual(0f, left.y, 1e-6f);
        Assert.AreEqual(-left, right);
        Assert.AreEqual(left, QASortieGather.SidestepSide(travel, 2));
        Assert.AreEqual(1f, QASortieGather.SidestepSide(Vector3.zero, 0).magnitude, 1e-4f); // 방향 없음도 단위 벡터
    }

    [Test]
    public void 자리는_설_수_있으면_그대로_막혔으면_가장_가까운_빈_후보()
    {
        // 선호 자리 3개가 z=8 데크 밑(설 수 없음, z>=7). 후보 = z 5 줄(설 수 있음)과 z 8 줄.
        var preferred = new[] { new Vector3(-1, 0, 8), new Vector3(0, 0, 8), new Vector3(1, 0, 8) };
        var candidates = new System.Collections.Generic.List<Vector3>();
        for (int x = -3; x <= 3; x++)
        {
            candidates.Add(new Vector3(x * 0.5f, 0, 5));
            candidates.Add(new Vector3(x * 0.5f, 0, 8));
        }
        System.Func<Vector3, bool> standable = v => v.z < 7f;
        Vector3[] spots = QASortieGather.ChooseSpots(preferred, candidates, standable, 1f);
        Assert.AreEqual(3, spots.Length);
        for (int i = 0; i < 3; i++)
        {
            Assert.Less(spots[i].z, 7f, $"자리 {i} 가 데크 밑");
            for (int j = 0; j < i; j++)
                Assert.GreaterOrEqual(Vector3.Distance(spots[i], spots[j]), 1f - 1e-4f, $"자리 {i}·{j} 겹침");
        }
        Assert.AreEqual(new Vector3(-1, 0, 5), spots[0]);
    }

    [Test]
    public void 자리는_선호_점이_열려_있으면_그대로_후보_없으면_선호_점()
    {
        var preferred = new[] { new Vector3(0, 0, 0), new Vector3(2, 0, 0) };
        Vector3[] open = QASortieGather.ChooseSpots(preferred, new Vector3[0], v => true, 1f);
        CollectionAssert.AreEqual(preferred, open);
        Vector3[] none = QASortieGather.ChooseSpots(preferred, new[] { new Vector3(9, 0, 9) }, v => false, 1f);
        CollectionAssert.AreEqual(preferred, none);
    }
}
#endif
