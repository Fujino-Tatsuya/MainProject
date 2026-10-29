using System;
using NUnit.Framework;

namespace VeyTrace.UI.Tests
{
    public sealed class BossSuppressCellLayoutTests
    {
        const float Tolerance = 1e-4f;
        const float EvenRow = 916f;   // 일반 규칙 확인용 줄 폭(3칸이 300 으로 딱 나뉜다)
        const float Gap = 8f;

        // BossHealthHUD.prefab 의 실제 값 — slot_boss_info.png 926×61, BossInfoSlot 964×103, 아래칸 바깥 테두리 57,45 / 812×16.
        const float SlotImageW = 926f, SlotImageH = 61f, SlotRectW = 964f, SlotRectH = 103f;
        const float LowerX = 57f, LowerY = 45f, LowerW = 812f, LowerH = 16f;

        static BossSuppressCellLayout.RowArea SlotRow() =>
            BossSuppressCellLayout.SlotPixelsToRow(SlotImageW, SlotImageH, SlotRectW, SlotRectH, LowerX, LowerY, LowerW, LowerH);

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
            Assert.That(BossSuppressCellLayout.CellWidth(EvenRow, total, Gap), Is.EqualTo(expected).Within(Tolerance));
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
            var first = BossSuppressCellLayout.PixelCell(0, total, EvenRow, Gap);
            var last = BossSuppressCellLayout.PixelCell(total - 1, total, EvenRow, Gap);
            Assert.That(first.Left, Is.EqualTo(-EvenRow * 0.5f).Within(Tolerance));
            Assert.That(last.Right, Is.EqualTo(EvenRow * 0.5f).Within(Tolerance));
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(5)]
        [TestCase(7)]
        public void Cells_HaveWholePixelEdges_ExactGaps_AndWidthsWithinOnePixel(int total)
        {
            float ideal = BossSuppressCellLayout.CellWidth(EvenRow, total, Gap);
            for (int i = 0; i < total; i++)
            {
                var c = BossSuppressCellLayout.PixelCell(i, total, EvenRow, Gap);
                Assert.That(IsWhole(c.Left) && IsWhole(c.Right), Is.True, $"칸 {i} 경계가 소수점: {c.Left}~{c.Right}");
                Assert.That(Math.Abs(c.Width - ideal), Is.LessThanOrEqualTo(1f), $"칸 {i} 폭 {c.Width} vs {ideal}");
                if (i > 0)
                {
                    var prev = BossSuppressCellLayout.PixelCell(i - 1, total, EvenRow, Gap);
                    Assert.That(c.Left - prev.Right, Is.EqualTo(Gap).Within(Tolerance), $"칸 {i - 1}~{i} 간격");
                }
            }
        }

        [Test]
        public void ThreeCells_OnEvenRow_AreSymmetric()
        {
            // 916 − 8×2 = 900 → 칸 300. 경계 −458~−158 / −150~150 / 158~458.
            var a = BossSuppressCellLayout.PixelCell(0, 3, EvenRow, Gap);
            var b = BossSuppressCellLayout.PixelCell(1, 3, EvenRow, Gap);
            var c = BossSuppressCellLayout.PixelCell(2, 3, EvenRow, Gap);
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
            for (int i = 0; i < 5; i++) sum += BossSuppressCellLayout.PixelCell(i, 5, EvenRow, Gap).Width;
            Assert.That(sum + Gap * 4, Is.EqualTo(EvenRow).Within(Tolerance));
            Assert.That(BossSuppressCellLayout.PixelCell(4, 5, EvenRow, Gap).Right, Is.EqualTo(458f));
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
            var a = BossSuppressCellLayout.PixelCell(0, 3, EvenRow, 20f);
            var c = BossSuppressCellLayout.PixelCell(2, 3, EvenRow, 20f);
            Assert.That(a.Width, Is.EqualTo(292f));   // (916 − 40) / 3
            Assert.That(a.Left, Is.EqualTo(-458f));
            Assert.That(c.Right, Is.EqualTo(458f));
        }

        // ── 슬롯 이미지 픽셀 영역 → HUD 좌표 ────────────────────────────────
        [Test]
        public void SlotLowerBar_MapsToHudRow()
        {
            // 배율 가로 964/926, 세로 103/61. 아래칸 가운데 (463, 53) = 이미지 가운데에서 (0, −22.5)px.
            var row = SlotRow();
            Assert.That(row.CenterX, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(row.CenterY, Is.EqualTo(-22.5f * 103f / 61f).Within(Tolerance));   // −37.99
            Assert.That(row.Width, Is.EqualTo(812f * 964f / 926f).Within(Tolerance));       // 845.32
            Assert.That(row.Height, Is.EqualTo(16f * 103f / 61f).Within(Tolerance));        // 27.02
            Assert.That(row.PixelHeight, Is.EqualTo(27f));
        }

        [Test]
        public void SlotLowerBar_EdgesMatchImageEdges()
        {
            // 아래칸 아래 끝 = 이미지 아래 끝(61) = 슬롯 사각형 아래 끝(−51.5).
            var row = SlotRow();
            Assert.That(row.CenterY - row.Height * 0.5f, Is.EqualTo(-SlotRectH * 0.5f).Within(Tolerance));
            Assert.That(row.CenterX - row.Width * 0.5f, Is.EqualTo((LowerX / SlotImageW - 0.5f) * SlotRectW).Within(Tolerance));
        }

        [Test]
        public void SlotSameSizeAsImage_IsPixelForPixel()
        {
            var row = BossSuppressCellLayout.SlotPixelsToRow(100f, 50f, 100f, 50f, 10f, 30f, 40f, 10f);
            Assert.That(row.CenterX, Is.EqualTo(-20f).Within(Tolerance));   // 30 − 50
            Assert.That(row.CenterY, Is.EqualTo(-10f).Within(Tolerance));   // 25 − 35
            Assert.That(row.Width, Is.EqualTo(40f).Within(Tolerance));
            Assert.That(row.Height, Is.EqualTo(10f).Within(Tolerance));
        }

        [Test]
        public void EmptyImageOrSlot_IsEmptyRow()
        {
            Assert.That(BossSuppressCellLayout.SlotPixelsToRow(0f, 61f, 964f, 103f, 57f, 45f, 812f, 16f).Width, Is.EqualTo(0f));
            Assert.That(BossSuppressCellLayout.SlotPixelsToRow(926f, 61f, 964f, 0f, 57f, 45f, 812f, 16f).Height, Is.EqualTo(0f));
        }

        [Test]
        public void SlotRow_ThreeCells_Are276_277_276()
        {
            // 줄 845(845.32 반올림), 왼쪽 끝 −422. (845 − 16) / 3 = 276.33.
            var row = SlotRow();
            var a = BossSuppressCellLayout.PixelCell(0, 3, row.Width, Gap);
            var b = BossSuppressCellLayout.PixelCell(1, 3, row.Width, Gap);
            var c = BossSuppressCellLayout.PixelCell(2, 3, row.Width, Gap);
            Assert.That(new[] { a.Left, a.Right, b.Left, b.Right, c.Left, c.Right },
                Is.EqualTo(new[] { -422f, -146f, -138f, 139f, 147f, 423f }));
            Assert.That(new[] { a.Width, b.Width, c.Width }, Is.EqualTo(new[] { 276f, 277f, 276f }));
        }

        [Test]
        public void SlotRow_FiveCells_Are163_162_163_162_163()
        {
            // (845 − 32) / 5 = 162.6.
            var row = SlotRow();
            var widths = new float[5];
            for (int i = 0; i < 5; i++) widths[i] = BossSuppressCellLayout.PixelCell(i, 5, row.Width, Gap).Width;
            Assert.That(widths, Is.EqualTo(new[] { 163f, 162f, 163f, 162f, 163f }));
            Assert.That(BossSuppressCellLayout.PixelCell(0, 5, row.Width, Gap).Left, Is.EqualTo(-422f));
            Assert.That(BossSuppressCellLayout.PixelCell(4, 5, row.Width, Gap).Right, Is.EqualTo(423f));
        }

        // ── 칸 없음 ───────────────────────────────────────────────────────
        [Test]
        public void ZeroCells_RowIsEmpty()
        {
            Assert.That(BossSuppressCellLayout.CellWidth(EvenRow, 0, Gap), Is.EqualTo(0f));
            Assert.That(BossSuppressCellLayout.PixelCell(0, 0, EvenRow, Gap).Width, Is.EqualTo(0f));
            Assert.That(FilledCount(0, 3), Is.EqualTo(0));
        }

        [Test]
        public void OutOfRangeIndex_IsEmpty()
        {
            Assert.That(BossSuppressCellLayout.PixelCell(3, 3, EvenRow, Gap).Width, Is.EqualTo(0f));
            Assert.That(BossSuppressCellLayout.PixelCell(-1, 3, EvenRow, Gap).Width, Is.EqualTo(0f));
        }
    }
}
