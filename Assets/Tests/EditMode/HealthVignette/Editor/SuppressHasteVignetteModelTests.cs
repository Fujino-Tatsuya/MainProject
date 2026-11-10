using NUnit.Framework;

public sealed class SuppressHasteVignetteModelTests
{
    private const float Tolerance = 1e-4f;

    [Test]
    public void StepAlpha_FadesInToShownAlphaOverFadeInDuration()
    {
        float half = SuppressHasteVignetteModel.StepAlpha(0f, true, 0.4f, 0.2f, 0.5f, 0.1f);
        float full = SuppressHasteVignetteModel.StepAlpha(half, true, 0.4f, 0.2f, 0.5f, 0.1f);
        float held = SuppressHasteVignetteModel.StepAlpha(full, true, 0.4f, 0.2f, 0.5f, 1f);

        Assert.That(half, Is.EqualTo(0.2f).Within(Tolerance));
        Assert.That(full, Is.EqualTo(0.4f).Within(Tolerance));
        Assert.That(held, Is.EqualTo(0.4f).Within(Tolerance), "비율 비례·맥동 없이 고정");
    }

    [Test]
    public void StepAlpha_FadesOutOverFadeOutDuration()
    {
        float half = SuppressHasteVignetteModel.StepAlpha(0.4f, false, 0.4f, 0.2f, 0.5f, 0.25f);
        float zero = SuppressHasteVignetteModel.StepAlpha(half, false, 0.4f, 0.2f, 0.5f, 0.25f);

        Assert.That(half, Is.EqualTo(0.2f).Within(Tolerance));
        Assert.That(zero, Is.EqualTo(0f).Within(Tolerance));
    }

    [Test]
    public void StepAlpha_ZeroDurationSnaps()
    {
        Assert.That(SuppressHasteVignetteModel.StepAlpha(0f, true, 0.4f, 0f, 0f, 0f), Is.EqualTo(0.4f));
        Assert.That(SuppressHasteVignetteModel.StepAlpha(0.4f, false, 0.4f, 0f, 0f, 0f), Is.EqualTo(0f));
    }

    [Test]
    public void StepAlpha_LoweredShownAlphaStillFadesDown()
    {
        // 표시 중 고정 알파를 0 으로 낮춰도 현재 알파에서 멈추지 않고 페이드 아웃 시간 안에 내려간다.
        float next = SuppressHasteVignetteModel.StepAlpha(0.4f, true, 0f, 0.2f, 0.4f, 0.4f);

        Assert.That(next, Is.EqualTo(0f).Within(Tolerance));
    }

    [Test]
    public void BurstFactor_StartsAtMultiplierAndDecaysToOne()
    {
        Assert.That(SuppressHasteVignetteModel.BurstFactor(0f, 0.2f, 1.8f), Is.EqualTo(1.8f).Within(Tolerance));
        Assert.That(SuppressHasteVignetteModel.BurstFactor(0.1f, 0.2f, 1.8f), Is.EqualTo(1.4f).Within(Tolerance));
        Assert.That(SuppressHasteVignetteModel.BurstFactor(0.2f, 0.2f, 1.8f), Is.EqualTo(1f));
        Assert.That(SuppressHasteVignetteModel.BurstFactor(float.MaxValue, 0.2f, 1.8f), Is.EqualTo(1f));
    }

    [Test]
    public void BurstFactor_DisabledByZeroDurationOrMultiplierAtMostOne()
    {
        Assert.That(SuppressHasteVignetteModel.BurstFactor(0f, 0f, 1.8f), Is.EqualTo(1f));
        Assert.That(SuppressHasteVignetteModel.BurstFactor(0f, 0.2f, 1f), Is.EqualTo(1f));
        Assert.That(SuppressHasteVignetteModel.BurstFactor(0f, 0.2f, 0.5f), Is.EqualTo(1f));
    }

    [Test]
    public void LineFrame_StepsByFpsAndZeroFpsIsFixed()
    {
        Assert.That(SuppressHasteVignetteModel.LineFrame(0.99f / 24f, 24f), Is.EqualTo(0));
        Assert.That(SuppressHasteVignetteModel.LineFrame(1.01f / 24f, 24f), Is.EqualTo(1));
        Assert.That(SuppressHasteVignetteModel.LineFrame(1f, 24f), Is.EqualTo(24));
        Assert.That(SuppressHasteVignetteModel.LineFrame(5f, 0f), Is.EqualTo(0));
    }

    [Test]
    public void IsTurnOn_OnlyOnRisingEdge()
    {
        Assert.That(SuppressHasteVignetteModel.IsTurnOn(false, true), Is.True);
        Assert.That(SuppressHasteVignetteModel.IsTurnOn(true, true), Is.False);
        Assert.That(SuppressHasteVignetteModel.IsTurnOn(true, false), Is.False);
        Assert.That(SuppressHasteVignetteModel.IsTurnOn(false, false), Is.False);
    }
}
