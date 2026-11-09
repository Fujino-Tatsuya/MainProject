using NUnit.Framework;
using UnityEngine;

public sealed class HealthVignetteModelTests
{
    private const float Threshold = 0.8f;
    private const float Tolerance = 1e-4f;

    [Test]
    public void Severity_ZeroAboveThreshold()
    {
        Assert.That(HealthVignetteModel.Severity(1f, Threshold), Is.EqualTo(0f));
        Assert.That(HealthVignetteModel.Severity(0.81f, Threshold), Is.EqualTo(0f));
    }

    [Test]
    public void Severity_RisesLinearlyBelowThresholdToOneAtZero()
    {
        Assert.That(HealthVignetteModel.Severity(0.8f, Threshold), Is.EqualTo(0f).Within(Tolerance));
        Assert.That(HealthVignetteModel.Severity(0.4f, Threshold), Is.EqualTo(0.5f).Within(Tolerance));
        Assert.That(HealthVignetteModel.Severity(0f, Threshold), Is.EqualTo(1f).Within(Tolerance));
    }

    [Test]
    public void Severity_NonPositiveThresholdDisables()
    {
        Assert.That(HealthVignetteModel.Severity(0f, 0f), Is.EqualTo(0f));
    }

    [Test]
    public void HealthRatio_UnreplicatedMaxHpTreatedAsFull()
    {
        Assert.That(HealthVignetteModel.HealthRatio(0, 0), Is.EqualTo(1f));
        Assert.That(HealthVignetteModel.HealthRatio(50, 200), Is.EqualTo(0.25f).Within(Tolerance));
        Assert.That(HealthVignetteModel.HealthRatio(300, 200), Is.EqualTo(1f));
    }

    [Test]
    public void VignetteAlpha_ZeroWithoutSeverityEvenWhilePulsing()
    {
        Assert.That(HealthVignetteModel.VignetteAlpha(0f, 0.6f, 0.2f, 0.25f), Is.EqualTo(0f));
    }

    [Test]
    public void VignetteAlpha_BaseScalesWithSeverity()
    {
        // 위상 0 → sin 0 = 0, 기본 알파만 남는다.
        Assert.That(HealthVignetteModel.VignetteAlpha(0.5f, 0.6f, 0.2f, 0f), Is.EqualTo(0.3f).Within(Tolerance));
        Assert.That(HealthVignetteModel.VignetteAlpha(1f, 0.6f, 0.2f, 0f), Is.EqualTo(0.6f).Within(Tolerance));
    }

    [Test]
    public void VignetteAlpha_PulseAmplitudeGrowsAsHealthDrops()
    {
        // 위상 0.25 → sin 최대. 진폭 = 진폭 × 강도.
        float lowSeverityPeak = HealthVignetteModel.VignetteAlpha(0.25f, 0.6f, 0.2f, 0.25f) - 0.25f * 0.6f;
        float highSeverityPeak = HealthVignetteModel.VignetteAlpha(1f, 0.6f, 0.2f, 0.25f) - 1f * 0.6f;

        Assert.That(lowSeverityPeak, Is.EqualTo(0.05f).Within(Tolerance));
        Assert.That(highSeverityPeak, Is.EqualTo(0.2f).Within(Tolerance));
    }

    [Test]
    public void PulseSpeed_ZeroWithoutSeverityAndFasterAtLowHealth()
    {
        Assert.That(HealthVignetteModel.PulseSpeed(0f, 1f, 3f), Is.EqualTo(0f));
        Assert.That(HealthVignetteModel.PulseSpeed(0.001f, 1f, 3f), Is.EqualTo(1f).Within(0.01f));
        Assert.That(HealthVignetteModel.PulseSpeed(0.5f, 1f, 3f), Is.EqualTo(2f).Within(Tolerance));
        Assert.That(HealthVignetteModel.PulseSpeed(1f, 1f, 3f), Is.EqualTo(3f).Within(Tolerance));
    }

    [Test]
    public void ActivePulseAmplitude_ZeroWhenNotAlive()
    {
        Assert.That(HealthVignetteModel.ActivePulseAmplitude(true, 0.2f), Is.EqualTo(0.2f));
        Assert.That(HealthVignetteModel.ActivePulseAmplitude(false, 0.2f), Is.EqualTo(0f));
    }

    [Test]
    public void VignetteAlpha_NotAliveHoldsStillAtMaxAlpha()
    {
        // 사망·Soul·관전 = 체력 0 취급(강도 1) + 진폭 0 → 위상과 무관하게 최대 알파로 고정.
        float amplitude = HealthVignetteModel.ActivePulseAmplitude(false, 0.2f);

        foreach (float phase in new[] { 0f, 0.25f, 0.5f, 0.75f })
            Assert.That(HealthVignetteModel.VignetteAlpha(1f, 0.6f, amplitude, phase), Is.EqualTo(0.6f).Within(Tolerance));
    }

    [Test]
    public void ActiveFlashAlpha_SuppressedWhenNotAliveAndRestoredOnRevive()
    {
        float peak = HealthVignetteModel.FlashAlpha(0f, 0.2f, 0.7f);

        Assert.That(HealthVignetteModel.ActiveFlashAlpha(false, peak), Is.EqualTo(0f));
        Assert.That(HealthVignetteModel.ActiveFlashAlpha(true, peak), Is.EqualTo(0.7f).Within(Tolerance));
    }

    [Test]
    public void Compose_NotAliveKeepsBlackVignetteWithoutFlash()
    {
        float amplitude = HealthVignetteModel.ActivePulseAmplitude(false, 0.2f);
        float vignetteAlpha = HealthVignetteModel.VignetteAlpha(1f, 0.6f, amplitude, 0.25f);
        float flash = HealthVignetteModel.ActiveFlashAlpha(false, HealthVignetteModel.FlashAlpha(0f, 0.2f, 0.7f));

        Color composed = HealthVignetteModel.Compose(Color.red, vignetteAlpha, Color.white, flash, 1f);

        Assert.That(composed, Is.EqualTo(new Color(0f, 0f, 0f, 0.6f)));
    }

    [Test]
    public void FlashColor_WhiteAtOrAboveThresholdAndRedderBelow()
    {
        Color white = Color.white;
        Color red = Color.red;

        Assert.That(HealthVignetteModel.FlashColor(1f, Threshold, white, red), Is.EqualTo(white));
        Assert.That(HealthVignetteModel.FlashColor(0.8f, Threshold, white, red), Is.EqualTo(white));

        Color half = HealthVignetteModel.FlashColor(0.4f, Threshold, white, red);
        Assert.That(half.r, Is.EqualTo(1f).Within(Tolerance));
        Assert.That(half.g, Is.EqualTo(0.5f).Within(Tolerance));

        Assert.That(HealthVignetteModel.FlashColor(0f, Threshold, white, red), Is.EqualTo(red));
    }

    [Test]
    public void FlashAlpha_PeaksOnHitAndFadesOutOverDuration()
    {
        Assert.That(HealthVignetteModel.FlashAlpha(0f, 0.2f, 0.7f), Is.EqualTo(0.7f).Within(Tolerance));
        Assert.That(HealthVignetteModel.FlashAlpha(0.1f, 0.2f, 0.7f), Is.EqualTo(0.35f).Within(Tolerance));
        Assert.That(HealthVignetteModel.FlashAlpha(0.2f, 0.2f, 0.7f), Is.EqualTo(0f));
        Assert.That(HealthVignetteModel.FlashAlpha(float.MaxValue, 0.2f, 0.7f), Is.EqualTo(0f));
        Assert.That(HealthVignetteModel.FlashAlpha(0f, 0f, 0.7f), Is.EqualTo(0f));
    }

    [Test]
    public void FlashAlpha_DefaultCurveMatchesLinear()
    {
        AnimationCurve curve = HealthVignetteModel.DefaultFlashCurve();
        foreach (float elapsed in new[] { -0.01f, 0f, 0.03f, 0.05f, 0.1f, 0.15f, 0.199f, 0.2f, 1f, float.MaxValue })
        {
            Assert.That(HealthVignetteModel.FlashAlpha(elapsed, 0.2f, 0.7f, curve),
                Is.EqualTo(HealthVignetteModel.FlashAlpha(elapsed, 0.2f, 0.7f)).Within(Tolerance), $"elapsed={elapsed}");
        }
        Assert.That(HealthVignetteModel.FlashAlpha(0f, 0f, 0.7f, curve), Is.EqualTo(0f));
    }

    [Test]
    public void FlashAlpha_EmptyCurveFallsBackToLinear()
    {
        Assert.That(HealthVignetteModel.FlashAlpha(0.1f, 0.2f, 0.7f, null), Is.EqualTo(0.35f).Within(Tolerance));
        Assert.That(HealthVignetteModel.FlashAlpha(0.1f, 0.2f, 0.7f, new AnimationCurve()), Is.EqualTo(0.35f).Within(Tolerance));
    }

    [Test]
    public void FlashAlpha_CustomCurveScalesPeakAndClampsNegative()
    {
        // 0.5 지점까지 1 유지 후 -1 로 — 앞 구간은 peak 그대로, 음수 배율은 0 으로 잘림.
        var hold = new AnimationCurve(new Keyframe(0f, 1f, 0f, 0f), new Keyframe(0.5f, 1f, 0f, 0f), new Keyframe(1f, -1f, 0f, 0f));
        Assert.That(HealthVignetteModel.FlashAlpha(0.1f, 1f, 0.7f, hold), Is.EqualTo(0.7f).Within(Tolerance));
        Assert.That(HealthVignetteModel.FlashAlpha(0.99f, 1f, 0.7f, hold), Is.EqualTo(0f));
    }

    [Test]
    public void Compose_FlashOverridesColorWhileStrongerThanVignette()
    {
        Color composed = HealthVignetteModel.Compose(Color.red, 0.2f, Color.white, 0.8f, 0f);

        Assert.That(composed.a, Is.EqualTo(0.8f).Within(Tolerance));
        Assert.That(composed.g, Is.EqualTo(1f).Within(Tolerance)); // 플래시 비중 1 → 흰색
    }

    [Test]
    public void Compose_NoFlashKeepsVignetteColor()
    {
        Color composed = HealthVignetteModel.Compose(Color.red, 0.4f, Color.white, 0f, 0f);

        Assert.That(composed, Is.EqualTo(new Color(1f, 0f, 0f, 0.4f)));
    }

    // 셰이더 전환 — 기본값(사각 0·보정 0·세기 1)이 옛 방사형 텍스처(CreateRadial)와 같은 모양인지.
    private static float OldRadialAlpha(Vector2 uv, float inner, float outer)
    {
        float dx = uv.x * 2f - 1f;
        float dy = uv.y * 2f - 1f;
        float distance = Mathf.Sqrt(dx * dx + dy * dy);
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, outer, distance));
    }

    [Test]
    public void EdgeMask_DefaultsMatchOldRadialTexture()
    {
        Vector2[] samples =
        {
            new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, 0f),
            new Vector2(0.05f, 0.3f), new Vector2(0.9f, 0.85f), new Vector2(0.97f, 0.5f), new Vector2(0.2f, 0.02f),
        };
        foreach (Vector2 uv in samples)
        {
            float expected = OldRadialAlpha(uv, 0.9f, 1.2f);
            float actual = HealthVignetteModel.EdgeMask(uv, 0.9f, 1.2f, 0f, 1f, 16f / 9f, 0f);
            Assert.That(actual, Is.EqualTo(expected).Within(1e-3f), $"uv {uv}");
        }
    }

    [Test]
    public void EdgeMask_CenterClearEdgesOpaqueAndStrengthScales()
    {
        Assert.That(HealthVignetteModel.EdgeMask(new Vector2(0.5f, 0.5f), 0.9f, 1.2f, 0.5f, 1f, 16f / 9f, 1f), Is.EqualTo(0f));
        Assert.That(HealthVignetteModel.EdgeMask(new Vector2(0f, 0f), 0.9f, 1.2f, 0f, 1f, 16f / 9f, 0f), Is.EqualTo(1f).Within(Tolerance));
        Assert.That(HealthVignetteModel.EdgeMask(new Vector2(0f, 0f), 0.9f, 1.2f, 0f, 0.5f, 16f / 9f, 0f), Is.EqualTo(0.5f).Within(Tolerance));
    }

    [Test]
    public void EdgeMask_AspectCorrectionNarrowsSideBandToTopBandWidth()
    {
        // 보정 1: 좌우 끝에서 세로 기준 같은 픽셀 거리의 점은 위아래 끝의 대응점과 같은 마스크(사각 1 이면 변을 따라 고르게).
        float aspect = 16f / 9f;
        float fromTop = 0.05f;                       // 세로 uv 거리
        float fromSide = fromTop / aspect;          // 같은 픽셀 거리의 가로 uv 거리
        float top = HealthVignetteModel.EdgeMask(new Vector2(0.5f, 1f - fromTop), 0.8f, 1.0f, 1f, 1f, aspect, 1f);
        float side = HealthVignetteModel.EdgeMask(new Vector2(1f - fromSide, 0.5f), 0.8f, 1.0f, 1f, 1f, aspect, 1f);
        Assert.That(side, Is.EqualTo(top).Within(1e-3f));

        // 보정 0(옛 방식)은 좌우 띠가 더 넓다 — 같은 픽셀 거리에서 좌우가 더 진하다.
        float sideOld = HealthVignetteModel.EdgeMask(new Vector2(1f - fromSide, 0.5f), 0.8f, 1.0f, 1f, 1f, aspect, 0f);
        Assert.That(sideOld, Is.GreaterThan(top));
    }

    [Test]
    public void Compose_DarkenMultipliesRgbByBlackButKeepsAlpha()
    {
        Color halfDark = HealthVignetteModel.Compose(Color.red, 0.6f, Color.white, 0f, 0.5f);
        Color fullDark = HealthVignetteModel.Compose(Color.red, 0.6f, Color.white, 0f, 1f);

        Assert.That(halfDark.r, Is.EqualTo(0.5f).Within(Tolerance));
        Assert.That(halfDark.a, Is.EqualTo(0.6f).Within(Tolerance));
        Assert.That(fullDark, Is.EqualTo(new Color(0f, 0f, 0f, 0.6f)));
    }
}
