using NUnit.Framework;

/// <summary>
/// 커서 대상 판정 널널하게(할 일 265) — <see cref="CursorUnitPick.Resolve{T}"/> 순수 규칙.
/// 직접 맞음 우선 · 반경 안 고를 수 있는 것 중 가장 가까운 것 · 반경 밖 없음 · 반경 0 = 예전(직접 결과 그대로).
/// </summary>
public sealed class CursorUnitPickLenientTests
{
    static CursorPickCandidate<string> C(string item, float distance, bool selectable = true)
        => new CursorPickCandidate<string>(item, distance, selectable);

    [Test]
    public void 직접_맞으면_반경_안_더_가까운_후보가_있어도_직접_대상()
    {
        var nearby = new[] { C("near", 0.1f) };
        CursorPickResult r = CursorUnitPick.Resolve(CursorPickResult.ValidTarget, "direct", nearby, 1f, out string picked);
        Assert.AreEqual(CursorPickResult.ValidTarget, r);
        Assert.AreEqual("direct", picked);
    }

    [Test]
    public void 빗나가면_반경_안_가장_가까운_것()
    {
        var nearby = new[] { C("far", 0.8f), C("near", 0.3f), C("mid", 0.5f) };
        CursorPickResult r = CursorUnitPick.Resolve(CursorPickResult.NoHit, null, nearby, 1f, out string picked);
        Assert.AreEqual(CursorPickResult.ValidTarget, r);
        Assert.AreEqual("near", picked);
    }

    [Test]
    public void 고를_수_없는_후보는_더_가까워도_건너뜀()
    {
        var nearby = new[] { C("dead", 0.1f, selectable: false), C("alive", 0.6f) };
        CursorUnitPick.Resolve(CursorPickResult.NoHit, null, nearby, 1f, out string picked);
        Assert.AreEqual("alive", picked);
    }

    [Test]
    public void 반경_경계는_포함()
    {
        var nearby = new[] { C("edge", 1f) };
        Assert.AreEqual(CursorPickResult.ValidTarget,
            CursorUnitPick.Resolve(CursorPickResult.NoHit, null, nearby, 1f, out string picked));
        Assert.AreEqual("edge", picked);
    }

    [Test]
    public void 반경_밖이면_없음_직접_결과_그대로()
    {
        var nearby = new[] { C("out", 1.01f) };
        Assert.AreEqual(CursorPickResult.NoHit,
            CursorUnitPick.Resolve(CursorPickResult.NoHit, null, nearby, 1f, out string picked));
        Assert.IsNull(picked);

        Assert.AreEqual(CursorPickResult.InvalidTarget,
            CursorUnitPick.Resolve(CursorPickResult.InvalidTarget, null, nearby, 1f, out picked));
        Assert.IsNull(picked);
    }

    [Test]
    public void 직접_맞은_게_무효_대상이어도_반경_안_유효_후보면_그것()
    {
        var nearby = new[] { C("enemy", 0.4f) };
        Assert.AreEqual(CursorPickResult.ValidTarget,
            CursorUnitPick.Resolve(CursorPickResult.InvalidTarget, null, nearby, 1f, out string picked));
        Assert.AreEqual("enemy", picked);
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    public void 반경_0_이하면_예전_동작(float radius)
    {
        var nearby = new[] { C("near", 0f) };
        Assert.AreEqual(CursorPickResult.NoHit,
            CursorUnitPick.Resolve(CursorPickResult.NoHit, null, nearby, radius, out string picked));
        Assert.IsNull(picked);

        Assert.AreEqual(CursorPickResult.ValidTarget,
            CursorUnitPick.Resolve(CursorPickResult.ValidTarget, "direct", nearby, radius, out picked));
        Assert.AreEqual("direct", picked);
    }

    [Test]
    public void 후보가_없으면_직접_결과()
    {
        Assert.AreEqual(CursorPickResult.NoHit,
            CursorUnitPick.Resolve(CursorPickResult.NoHit, null, new CursorPickCandidate<string>[0], 1f, out string picked));
        Assert.IsNull(picked);
    }
}
