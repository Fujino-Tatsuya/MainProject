using NUnit.Framework;

public sealed class ShieldVignetteModelTests
{
    private const float Tolerance = 1e-4f;

    [Test]
    public void ShouldShow_OnlyWhenAliveWithShield()
    {
        Assert.That(ShieldVignetteModel.ShouldShow(10, true), Is.True);
        Assert.That(ShieldVignetteModel.ShouldShow(0, true), Is.False);
        Assert.That(ShieldVignetteModel.ShouldShow(10, false), Is.False, "사망·Soul·관전에선 숨긴다");
    }

    [Test]
    public void StepAlpha_FadesInToShownAlphaOverFadeInDuration()
    {
        float half = ShieldVignetteModel.StepAlpha(0f, true, 0.4f, 0.2f, 0.5f, 0.1f);
        float full = ShieldVignetteModel.StepAlpha(half, true, 0.4f, 0.2f, 0.5f, 0.1f);
        float held = ShieldVignetteModel.StepAlpha(full, true, 0.4f, 0.2f, 0.5f, 1f);

        Assert.That(half, Is.EqualTo(0.2f).Within(Tolerance));
        Assert.That(full, Is.EqualTo(0.4f).Within(Tolerance));
        Assert.That(held, Is.EqualTo(0.4f).Within(Tolerance), "비율 비례·맥동 없이 고정");
    }

    [Test]
    public void StepAlpha_FadesOutOverFadeOutDuration()
    {
        float half = ShieldVignetteModel.StepAlpha(0.4f, false, 0.4f, 0.2f, 0.5f, 0.25f);
        float zero = ShieldVignetteModel.StepAlpha(half, false, 0.4f, 0.2f, 0.5f, 0.25f);

        Assert.That(half, Is.EqualTo(0.2f).Within(Tolerance));
        Assert.That(zero, Is.EqualTo(0f).Within(Tolerance));
    }

    [Test]
    public void StepAlpha_ZeroDurationSnaps()
    {
        Assert.That(ShieldVignetteModel.StepAlpha(0f, true, 0.4f, 0f, 0f, 0f), Is.EqualTo(0.4f));
        Assert.That(ShieldVignetteModel.StepAlpha(0.4f, false, 0.4f, 0f, 0f, 0f), Is.EqualTo(0f));
    }

    [Test]
    public void StepAlpha_LoweredShownAlphaStillFadesDown()
    {
        // 표시 중 고정 알파를 0 으로 낮춰도 현재 알파에서 멈추지 않고 페이드 아웃 시간 안에 내려간다.
        float next = ShieldVignetteModel.StepAlpha(0.4f, true, 0f, 0.2f, 0.4f, 0.4f);

        Assert.That(next, Is.EqualTo(0f).Within(Tolerance));
    }
}
