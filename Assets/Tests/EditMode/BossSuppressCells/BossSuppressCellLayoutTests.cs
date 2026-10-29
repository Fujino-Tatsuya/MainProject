using System;
using NUnit.Framework;

namespace VeyTrace.UI.Tests
{
    public sealed class BossSuppressCellLayoutTests
    {
        const float Tolerance = 1e-4f;
        const float HpBarWidth = 916f;   // BossHealthHUD.prefab 의 HP_Fill 폭 = 기본 줄 폭
        const float Gap = 8f;

        static int FilledCount(int total, int remaining)
        {
            int count = 0;
            for (int i = 0; i < total; i++)
                if (BossSuppressCellLayout.IsFilled(i, remaining)) count++;
            return count;
        }

        static bool IsWhole(float v) => Math.Abs(v - Math.Round(v)) < Tolerance;

        // ── 채움 ──────────────────────────────────────────────────────────
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

        // ── 칸 폭 = (줄 폭 − 간격×(N−1)) / N ─────────────────────────────
        [TestCase(1, 916f)]
        [TestCase(3, 300f)]
        [TestCase(5, 176.8f)]
        public void CellWidth_SplitsRowMinusGaps(int total, float expected)
        {
            Assert.That(BossSuppressCellLayout.CellWidth(HpBarWidth, total, Gap), Is.EqualTo(expected).Within(Tolerance));
        }

        [Test]
        public void CellWidth_GapWiderThanRow_IsZero()
        {
            Assert.That(BossSuppressCellLayout.CellWidth(20f, 5, Gap), Is.EqualTo(0f));
        }

        // ── 정수 픽셀 배치 ────────────────────────────────────────────────
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(5)]
        public void Row_EndsMatchRowWidth(int total)
        {
            var first = BossSuppressCellLayout.PixelCell(0, total, HpBarWidth, Gap);
            var last = BossSuppressCellLayout.PixelCell(total - 1, total, HpBarWidth, Gap);
            Assert.That(first.Left, Is.EqualTo(-HpBarWidth * 0.5f).Within(Tolerance));
            Assert.That(last.Right, Is.EqualTo(HpBarWidth * 0.5f).Within(Tolerance));
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(5)]
        [TestCase(7)]
        public void Cells_HaveWholePixelEdges_ExactGaps_AndWidthsWithinOnePixel(int total)
        {
            float ideal = BossSuppressCellLayout.CellWidth(HpBarWidth, total, Gap);
            for (int i = 0; i < total; i++)
            {
                var c = BossSuppressCellLayout.PixelCell(i, total, HpBarWidth, Gap);
                Assert.That(IsWhole(c.Left) && IsWhole(c.Right), Is.True, $"칸 {i} 경계가 소수점: {c.Left}~{c.Right}");
                Assert.That(Math.Abs(c.Width - ideal), Is.LessThanOrEqualTo(1f), $"칸 {i} 폭 {c.Width} vs {ideal}");
                if (i > 0)
                {
                    var prev = BossSuppressCellLayout.PixelCell(i - 1, total, HpBarWidth, Gap);
                    Assert.That(c.Left - prev.Right, Is.EqualTo(Gap).Within(Tolerance), $"칸 {i - 1}~{i} 간격");
                }
            }
        }

        [Test]
        public void ThreeCells_OnHpBar_AreSymmetric()
        {
            // 916 − 8×2 = 900 → 칸 300. 경계 −458~−158 / −150~150 / 158~458.
            var a = BossSuppressCellLayout.PixelCell(0, 3, HpBarWidth, Gap);
            var b = BossSuppressCellLayout.PixelCell(1, 3, HpBarWidth, Gap);
            var c = BossSuppressCellLayout.PixelCell(2, 3, HpBarWidth, Gap);
            Assert.That(a.Left, Is.EqualTo(-458f));
            Assert.That(a.Right, Is.EqualTo(-158f));
            Assert.That(b.CenterX, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(b.Width, Is.EqualTo(300f));
            Assert.That(c.Left, Is.EqualTo(158f));
            Assert.That(c.Right, Is.EqualTo(458f));
        }

        [Test]
        public void FiveCells_RoundingErrorAbsorbedAtRowEnd()
        {
            // 칸 176.8 → 폭 177/177/176/177/177, 합 + 간격 32 = 916.
            float sum = 0f;
            for (int i = 0; i < 5; i++) sum += BossSuppressCellLayout.PixelCell(i, 5, HpBarWidth, Gap).Width;
            Assert.That(sum + Gap * 4, Is.EqualTo(HpBarWidth).Within(Tolerance));
            Assert.That(BossSuppressCellLayout.PixelCell(4, 5, HpBarWidth, Gap).Right, Is.EqualTo(458f));
        }

        [Test]
        public void FractionalRowWidth_IsRoundedToWholePixels()
        {
            var last = BossSuppressCellLayout.PixelCell(2, 3, 915.6f, Gap);
            var first = BossSuppressCellLayout.PixelCell(0, 3, 915.6f, Gap);
            Assert.That(last.Right - first.Left, Is.EqualTo(916f).Within(Tolerance));
            Assert.That(IsWhole(first.Left) && IsWhole(last.Right), Is.True);
        }

        [Test]
        public void GapChangesCellWidthButNotRowEnds()
        {
            var a = BossSuppressCellLayout.PixelCell(0, 3, HpBarWidth, 20f);
            var c = BossSuppressCellLayout.PixelCell(2, 3, HpBarWidth, 20f);
            Assert.That(a.Width, Is.EqualTo(292f));   // (916 − 40) / 3
            Assert.That(a.Left, Is.EqualTo(-458f));
            Assert.That(c.Right, Is.EqualTo(458f));
        }

        // ── 칸 없음 ───────────────────────────────────────────────────────
        [Test]
        public void ZeroCells_RowIsEmpty()
        {
            Assert.That(BossSuppressCellLayout.CellWidth(HpBarWidth, 0, Gap), Is.EqualTo(0f));
            Assert.That(BossSuppressCellLayout.PixelCell(0, 0, HpBarWidth, Gap).Width, Is.EqualTo(0f));
            Assert.That(FilledCount(0, 3), Is.EqualTo(0));
        }

        [Test]
        public void OutOfRangeIndex_IsEmpty()
        {
            Assert.That(BossSuppressCellLayout.PixelCell(3, 3, HpBarWidth, Gap).Width, Is.EqualTo(0f));
            Assert.That(BossSuppressCellLayout.PixelCell(-1, 3, HpBarWidth, Gap).Width, Is.EqualTo(0f));
        }
    }
}
