#if UNITY_EDITOR
using NUnit.Framework;

/// <summary>할 일 169 — 10분 시나리오 출격 집결 자리·인원(<see cref="QASortieGather.TrySlot"/>).</summary>
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
}
#endif
