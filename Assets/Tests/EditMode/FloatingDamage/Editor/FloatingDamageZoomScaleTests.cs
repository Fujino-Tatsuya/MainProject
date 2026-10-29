using NUnit.Framework;

// 할 일 154 — 카메라 줌 배율 = 현재 ÷ 기준, [최소, 최대] 클램프, 끔·잘못된 값이면 1.
public sealed class FloatingDamageZoomScaleTests
{
    const float Reference = 89.8f;

    [Test]
    public void ReferenceZoom_IsOne()
    {
        Assert.That(FloatingDamageZoomScalePolicy.Evaluate(true, Reference, Reference, 0.5f, 2f),
            Is.EqualTo(1f).Within(1e-5f));
    }

    [Test]
    public void ZoomIn_IsLargerThanOne()
    {
        // 간파 성공 줌 0.85 → 거리 0.85 배 → 1m 가 1/0.85 배 크게 보인다.
        float scale = FloatingDamageZoomScalePolicy.Evaluate(true, Reference / 0.85f, Reference, 0.5f, 2f);
        Assert.That(scale, Is.GreaterThan(1f));
        Assert.That(scale, Is.EqualTo(1f / 0.85f).Within(1e-4f));
    }

    [Test]
    public void ZoomOut_IsSmallerThanOne()
    {
        float scale = FloatingDamageZoomScalePolicy.Evaluate(true, Reference * 0.8f, Reference, 0.5f, 2f);
        Assert.That(scale, Is.EqualTo(0.8f).Within(1e-4f));
    }

    [Test]
    public void ClampsToMinAndMax()
    {
        Assert.That(FloatingDamageZoomScalePolicy.Evaluate(true, Reference * 0.1f, Reference, 0.5f, 2f),
            Is.EqualTo(0.5f));
        Assert.That(FloatingDamageZoomScalePolicy.Evaluate(true, Reference * 10f, Reference, 0.5f, 2f),
            Is.EqualTo(2f));
    }

    [Test]
    public void Disabled_IsAlwaysOne()
    {
        Assert.That(FloatingDamageZoomScalePolicy.Evaluate(false, Reference * 3f, Reference, 0.5f, 2f),
            Is.EqualTo(1f));
        Assert.That(FloatingDamageZoomScalePolicy.Evaluate(false, Reference * 0.1f, Reference, 0.5f, 2f),
            Is.EqualTo(1f));
    }

    [TestCase(0f, 89.8f)]
    [TestCase(89.8f, 0f)]
    public void InvalidMeasurement_IsOne(float current, float reference)
    {
        Assert.That(FloatingDamageZoomScalePolicy.Evaluate(true, current, reference, 0.5f, 2f), Is.EqualTo(1f));
    }
}
