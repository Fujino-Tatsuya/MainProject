using NUnit.Framework;

namespace VeyTrace.UI.Tests
{
    public sealed class BossSuppressCellLayoutTests
    {
        const float Tolerance = 1e-4f;

        static int FilledCount(int total, int remaining)
        {
            int count = 0;
            for (int i = 0; i < total; i++)
                if (BossSuppressCellLayout.IsFilled(i, remaining)) count++;
            return count;
        }

        [TestCase(3, 3)]
        [TestCase(2, 2)]
        [TestCase(0, 0)]
        public void ThreeCells_FillVisibleCount_EqualsRemaining(int remaining, int expected)
        {
            Assert.That(FilledCount(3, remaining), Is.EqualTo(expected));
        }

        [Test]
        public void ThreeCells_TwoRemaining_RightmostIsEmpty()
        {
            Assert.That(BossSuppressCellLayout.IsFilled(0, 2), Is.True);
            Assert.That(BossSuppressCellLayout.IsFilled(1, 2), Is.True);
            Assert.That(BossSuppressCellLayout.IsFilled(2, 2), Is.False);
        }

        [Test]
        public void ThreeCells_CentersAreSymmetricAroundRowCenter()
        {
            // 칸 96·간격 8 → 줄 폭 304, 중심 -104 / 0 / +104
            Assert.That(BossSuppressCellLayout.RowWidth(3, 96f, 8f), Is.EqualTo(304f).Within(Tolerance));
            Assert.That(BossSuppressCellLayout.CellCenterX(0, 3, 96f, 8f), Is.EqualTo(-104f).Within(Tolerance));
            Assert.That(BossSuppressCellLayout.CellCenterX(1, 3, 96f, 8f), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(BossSuppressCellLayout.CellCenterX(2, 3, 96f, 8f), Is.EqualTo(104f).Within(Tolerance));
        }

        [Test]
        public void SingleCell_SitsAtRowCenter()
        {
            Assert.That(BossSuppressCellLayout.CellCenterX(0, 1, 96f, 8f), Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void ZeroCells_RowIsEmpty()
        {
            Assert.That(BossSuppressCellLayout.RowWidth(0, 96f, 8f), Is.EqualTo(0f));
            Assert.That(FilledCount(0, 3), Is.EqualTo(0));
        }
    }
}
