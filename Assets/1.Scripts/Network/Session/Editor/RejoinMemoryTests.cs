using System;
using NUnit.Framework;

/// <summary>
/// 재합류 보관소(C14 R4c) — playerId 키 보관·꺼내면 지움(두 번 되돌리지 않음)·덮어쓰기·비우기 크기(누적 0).
/// </summary>
public sealed class RejoinMemoryTests
{
    static TutorialTraitRejoinSnapshot Traits(params int[] acquired) =>
        new TutorialTraitRejoinSnapshot(acquired, 0, false, 0, 0, false, 0, Array.Empty<int>());

    [Test]
    public void 보관한_특성은_꺼내면_지워진다()
    {
        var memory = new RejoinMemory();
        Assert.IsTrue(memory.SaveTraits("a", Traits(1, 2)));
        Assert.AreEqual(1, memory.TraitCount);

        Assert.IsTrue(memory.TryTakeTraits("a", out var snapshot));
        CollectionAssert.AreEqual(new[] { 1, 2 }, snapshot.Acquired);
        Assert.AreEqual(0, memory.TraitCount);
        Assert.IsFalse(memory.TryTakeTraits("a", out _), "Drop 경로에서 같은 사람에게 또 와도 두 번 되돌리지 않는다");
    }

    [Test]
    public void 같은_사람이_다시_나가면_마지막_기록으로_덮는다()
    {
        var memory = new RejoinMemory();
        memory.SaveTraits("a", Traits(1));
        memory.SaveTraits("a", Traits(1, 5));

        Assert.AreEqual(1, memory.TraitCount);
        Assert.IsTrue(memory.TryTakeTraits("a", out var snapshot));
        CollectionAssert.AreEqual(new[] { 1, 5 }, snapshot.Acquired);
    }

    [Test]
    public void 빈_playerId_와_빈_기록은_보관하지_않는다()
    {
        var memory = new RejoinMemory();
        Assert.IsFalse(memory.SaveTraits(null, Traits(1)));
        Assert.IsFalse(memory.SaveTraits("", Traits(1)));
        Assert.IsFalse(memory.SaveTraits("a", null));
        Assert.IsFalse(memory.TryTakeTraits(null, out _));
        Assert.AreEqual(0, memory.Count);
    }

    [Test]
    public void 비우면_크기를_돌려주고_0_이_된다()
    {
        var memory = new RejoinMemory();
        memory.SaveTraits("a", Traits(1));
        memory.SaveTraits("b", Traits(2));

        Assert.AreEqual(2, memory.Clear());
        Assert.AreEqual(0, memory.Count);
        Assert.AreEqual(0, memory.Clear(), "두 번째 비우기는 0");

        memory.SaveTraits("c", Traits(3));
        Assert.AreEqual(1, memory.ClearTraits());
        Assert.IsFalse(memory.TryTakeTraits("c", out _));
    }
}
