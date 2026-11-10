using NUnit.Framework;
using UnityEngine;

/// <summary>할 일 253 — 획득 특성 벌집 배치(<see cref="TraitIconHoneycomb"/>): 2열 지그재그, 오른쪽 열 반 칸 위, 아래에서 위로.</summary>
public class TraitIconHoneycombTests
{
    private static readonly Vector2 Cell = new Vector2(64f, 56f);
    private const float Gap = 4f;

    [Test]
    public void 첫_칸은_상자_왼쪽_아래()
    {
        Assert.That(TraitIconHoneycomb.CellCenter(0, Cell, Gap), Is.EqualTo(new Vector2(32f, 28f)));
    }

    [Test]
    public void 둘째는_오른쪽_열_반_칸_위()
    {
        // x = 32 + 64×0.75 + 4 = 84, y = 28 + (56+4)/2 = 58
        Assert.That(TraitIconHoneycomb.CellCenter(1, Cell, Gap), Is.EqualTo(new Vector2(84f, 58f)));
    }

    [Test]
    public void 같은_열은_한_칸씩_위로()
    {
        Assert.That(TraitIconHoneycomb.CellCenter(2, Cell, Gap), Is.EqualTo(new Vector2(32f, 88f)));  // 3 = 1 바로 위
        Assert.That(TraitIconHoneycomb.CellCenter(3, Cell, Gap), Is.EqualTo(new Vector2(84f, 118f))); // 4 = 2 바로 위
        Assert.That(TraitIconHoneycomb.CellCenter(4, Cell, Gap), Is.EqualTo(new Vector2(32f, 148f))); // 5 = 3 바로 위
    }

    [Test]
    public void 오른쪽_열은_왼쪽_두_칸_사이_높이()
    {
        for (int row = 0; row < 4; row++)
        {
            float left = TraitIconHoneycomb.CellCenter(row * 2, Cell, Gap).y;
            float leftAbove = TraitIconHoneycomb.CellCenter(row * 2 + 2, Cell, Gap).y;
            float right = TraitIconHoneycomb.CellCenter(row * 2 + 1, Cell, Gap).y;
            Assert.That(right, Is.EqualTo((left + leftAbove) * 0.5f).Within(1e-4f));
        }
    }
}
