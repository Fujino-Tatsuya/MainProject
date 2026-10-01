using System;
using NUnit.Framework;

namespace EuniTween.Tests
{
    public sealed class EasingTests
    {
        const float Tolerance = 1e-4f;

        static Ease[] AllEases => (Ease[])Enum.GetValues(typeof(Ease));

        [TestCaseSource(nameof(AllEases))]
        public void Evaluate_StartsAtZero_EndsAtOne(Ease ease)
        {
            Assert.That(Easing.Evaluate(ease, 0f), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(Easing.Evaluate(ease, 1f), Is.EqualTo(1f).Within(Tolerance));
        }

        [TestCaseSource(nameof(AllEases))]
        public void Evaluate_ClampsInputOutsideRange(Ease ease)
        {
            Assert.That(Easing.Evaluate(ease, -1f), Is.EqualTo(Easing.Evaluate(ease, 0f)));
            Assert.That(Easing.Evaluate(ease, 2f), Is.EqualTo(Easing.Evaluate(ease, 1f)));
        }

        [TestCaseSource(nameof(AllEases))]
        public void InOut_HitsHalfAtMidpoint(Ease ease)
        {
            if (!ease.ToString().StartsWith("InOut") && ease != Ease.Linear)
                Assert.Ignore("InOut 계열만 대칭이다.");

            Assert.That(Easing.Evaluate(ease, 0.5f), Is.EqualTo(0.5f).Within(Tolerance));
        }

        [Test]
        public void OutBack_Overshoots()
        {
            Assert.That(Easing.Evaluate(Ease.OutBack, 0.6f), Is.GreaterThan(1f));
        }

        [TestCaseSource(nameof(AllEases))]
        public void Punch_ReturnsToZero_PeaksAtMidpoint(Ease ease)
        {
            Assert.That(Easing.Punch(ease, 0f), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(Easing.Punch(ease, 0.5f), Is.EqualTo(1f).Within(Tolerance));
            Assert.That(Easing.Punch(ease, 1f), Is.EqualTo(0f).Within(Tolerance));
        }
    }
}
