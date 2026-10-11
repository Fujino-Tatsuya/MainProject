using NUnit.Framework;

public sealed class InterruptHighlightModelTests
{
    private const float Tolerance = 1e-4f;

    [Test]
    public void IsHighlightTarget_OnlyWhenWindowOpenAndInRange()
    {
        Assert.That(InterruptHighlightModel.IsHighlightTarget(true, true), Is.True);
        Assert.That(InterruptHighlightModel.IsHighlightTarget(true, false), Is.False, "창이 열려도 사거리 밖이면 끈다");
        Assert.That(InterruptHighlightModel.IsHighlightTarget(false, true), Is.False, "사거리 안이어도 창이 닫혀 있으면 끈다");
        Assert.That(InterruptHighlightModel.IsHighlightTarget(false, false), Is.False);
    }

    [Test]
    public void ShouldHighlight_OnlyWhenAliveWithTarget()
    {
        Assert.That(InterruptHighlightModel.ShouldHighlight(true, true), Is.True);
        Assert.That(InterruptHighlightModel.ShouldHighlight(false, true), Is.False);
        Assert.That(InterruptHighlightModel.ShouldHighlight(true, false), Is.False, "사망·Soul·관전에선 끈다");
    }

    [Test]
    public void StepIntensity_FadesInAndOutOverDurations()
    {
        float half = InterruptHighlightModel.StepIntensity(0f, true, 0.2f, 0.4f, 0.1f);
        float full = InterruptHighlightModel.StepIntensity(half, true, 0.2f, 0.4f, 0.1f);
        float held = InterruptHighlightModel.StepIntensity(full, true, 0.2f, 0.4f, 1f);
        float fading = InterruptHighlightModel.StepIntensity(held, false, 0.2f, 0.4f, 0.1f);
        float off = InterruptHighlightModel.StepIntensity(fading, false, 0.2f, 0.4f, 1f);

        Assert.That(half, Is.EqualTo(0.5f).Within(Tolerance));
        Assert.That(full, Is.EqualTo(1f).Within(Tolerance));
        Assert.That(held, Is.EqualTo(1f).Within(Tolerance));
        Assert.That(fading, Is.EqualTo(0.75f).Within(Tolerance), "꺼짐은 페이드 아웃 시간 기준");
        Assert.That(off, Is.EqualTo(0f).Within(Tolerance), "조건이 꺼지면 원래대로");
    }

    [Test]
    public void StepIntensity_ZeroDurationSnaps()
    {
        Assert.That(InterruptHighlightModel.StepIntensity(0f, true, 0f, 0f, 0f), Is.EqualTo(1f));
        Assert.That(InterruptHighlightModel.StepIntensity(1f, false, 0f, 0f, 0f), Is.EqualTo(0f));
    }

    [Test]
    public void StepIntensity_ZeroDeltaHolds()
    {
        // 게임 시간이 멈추면(timeScale 0) 강조도 그 자리에 멈춘다.
        Assert.That(InterruptHighlightModel.StepIntensity(0.25f, true, 0.2f, 0.4f, 0f),
            Is.EqualTo(0.25f).Within(Tolerance));
    }

    [Test]
    public void Pulse_StartsBrightAndWraps()
    {
        Assert.That(InterruptHighlightModel.PulseWave(0f), Is.EqualTo(1f).Within(Tolerance), "켜지는 순간 가장 밝다");
        Assert.That(InterruptHighlightModel.PulseWave(0.5f), Is.EqualTo(0f).Within(Tolerance));

        float phase = InterruptHighlightModel.AdvancePhase(0.75f, 0.5f, 0.25f);
        Assert.That(phase, Is.EqualTo(0.25f).Within(Tolerance), "한 주기를 넘으면 0~1 로 감는다");
        Assert.That(InterruptHighlightModel.AdvancePhase(0.3f, 0f, 1f), Is.EqualTo(0f), "주기 0 = 맥동 없음");
    }

    [Test]
    public void GlowAlpha_ScalesPulseByIntensity()
    {
        Assert.That(InterruptHighlightModel.GlowAlpha(1f, 1f, 0.4f), Is.EqualTo(1f).Within(Tolerance));
        Assert.That(InterruptHighlightModel.GlowAlpha(1f, 0f, 0.4f), Is.EqualTo(0.4f).Within(Tolerance));
        Assert.That(InterruptHighlightModel.GlowAlpha(0.5f, 0f, 0.4f), Is.EqualTo(0.2f).Within(Tolerance));
        Assert.That(InterruptHighlightModel.GlowAlpha(0f, 1f, 0.4f), Is.EqualTo(0f), "꺼지면 맥동도 보이지 않는다");
    }
}
