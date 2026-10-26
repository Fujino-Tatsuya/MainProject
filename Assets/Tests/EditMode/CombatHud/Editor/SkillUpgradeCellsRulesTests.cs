using NUnit.Framework;

public sealed class SkillUpgradeCellsRulesTests
{
    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(3, 3)]
    [TestCase(4, 4)]
    [TestCase(9, 4)]
    [TestCase(-2, 0)]
    public void ActiveCellCount_ClampsStageToCellCount(int stage, int expected)
    {
        Assert.That(SkillUpgradeCellsView.ActiveCellCount(stage, 4), Is.EqualTo(expected));
    }

    [Test]
    public void ActiveCellCount_NoCells_IsZero()
    {
        Assert.That(SkillUpgradeCellsView.ActiveCellCount(3, 0), Is.EqualTo(0));
        Assert.That(SkillUpgradeCellsView.ActiveCellCount(3, -1), Is.EqualTo(0));
    }

    [Test]
    public void IsCellOn_FillsFromTheFront()
    {
        bool[] on = new bool[4];
        for (int i = 0; i < 4; i++)
            on[i] = SkillUpgradeCellsView.IsCellOn(i, 2, 4);

        Assert.That(on, Is.EqualTo(new[] { true, true, false, false }));
    }

    [Test]
    public void IsCellOn_OutOfRangeIndex_IsOff()
    {
        Assert.That(SkillUpgradeCellsView.IsCellOn(-1, 4, 4), Is.False);
        Assert.That(SkillUpgradeCellsView.IsCellOn(4, 9, 4), Is.False);
    }
}
