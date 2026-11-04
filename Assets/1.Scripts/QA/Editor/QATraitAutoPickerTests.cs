#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>10분 세션 특성 자동 선택(<see cref="QATraitAutoPicker.PickSeeded"/>) — 제시 안에서만, 같은 시드·차수면 같은 카드, 없으면 None.</summary>
public sealed class QATraitAutoPickerTests
{
    [Test]
    public void 제시가_없으면_None()
    {
        Assert.AreEqual(TraitIdHash.None, QATraitAutoPicker.PickSeeded(1, 1, null));
        Assert.AreEqual(TraitIdHash.None, QATraitAutoPicker.PickSeeded(1, 1, new List<int>()));
    }

    [Test]
    public void 고른_카드는_항상_제시_안에_있고_결정적이다()
    {
        var offers = new List<int> { -12345, 678, 91011 };   // 특성 해시는 음수도 정상
        for (int seed = -50; seed < 50; seed++)
            for (int nth = 1; nth <= 4; nth++)
            {
                int pick = QATraitAutoPicker.PickSeeded(seed, nth, offers);
                CollectionAssert.Contains(offers, pick);
                Assert.AreEqual(pick, QATraitAutoPicker.PickSeeded(seed, nth, offers));
            }
    }

    [Test]
    public void 시드가_달라지면_세_장이_모두_나온다()
    {
        var offers = new List<int> { 1, 2, 3 };
        var seen = new HashSet<int>();
        for (int seed = 0; seed < 64; seed++)
            seen.Add(QATraitAutoPicker.PickSeeded(seed, 1, offers));
        Assert.AreEqual(3, seen.Count);
    }
}
#endif
