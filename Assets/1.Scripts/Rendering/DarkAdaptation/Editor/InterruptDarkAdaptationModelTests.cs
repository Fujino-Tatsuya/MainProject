using NUnit.Framework;

/// <summary>
/// 간파 성공 암순응 곡선(<see cref="InterruptDarkAdaptationModel"/>) — 곡선 경계, 본인/타인 강도, 재시작, 정리 규칙.
/// 수치는 2 진수로 딱 떨어지는 값(0.25·0.5 …)을 쓴다.
/// </summary>
public sealed class InterruptDarkAdaptationModelTests
{
    const float Eps = 1e-5f;
    const float FadeIn = 0.25f;
    const float Hold = 0.5f;
    const float FadeOut = 0.5f;

    // ---- 곡선 경계 ----

    [Test]
    public void Evaluate_CurveBoundaries()
    {
        Assert.AreEqual(0f, InterruptDarkAdaptationModel.Evaluate(0f, 0f, 1f, FadeIn, Hold, FadeOut), Eps, "시작");
        Assert.AreEqual(0.5f, InterruptDarkAdaptationModel.Evaluate(0.125f, 0f, 1f, FadeIn, Hold, FadeOut), Eps, "fadeIn 중간");
        Assert.AreEqual(1f, InterruptDarkAdaptationModel.Evaluate(FadeIn, 0f, 1f, FadeIn, Hold, FadeOut), Eps, "fadeIn 끝 = 최대");
        Assert.AreEqual(1f, InterruptDarkAdaptationModel.Evaluate(FadeIn + Hold - 0.001f, 0f, 1f, FadeIn, Hold, FadeOut), Eps, "유지 끝 직전");
        Assert.AreEqual(0.5f, InterruptDarkAdaptationModel.Evaluate(FadeIn + Hold + 0.25f, 0f, 1f, FadeIn, Hold, FadeOut), Eps, "fadeOut 중간");
        Assert.AreEqual(0f, InterruptDarkAdaptationModel.Evaluate(FadeIn + Hold + FadeOut, 0f, 1f, FadeIn, Hold, FadeOut), Eps, "끝");
        Assert.AreEqual(0f, InterruptDarkAdaptationModel.Evaluate(10f, 0f, 1f, FadeIn, Hold, FadeOut), Eps, "끝 이후");
    }

    [Test]
    public void Evaluate_ZeroFadeIn_JumpsToPeak()
    {
        Assert.AreEqual(1f, InterruptDarkAdaptationModel.Evaluate(0f, 0f, 1f, 0f, Hold, FadeOut), Eps);
    }

    [Test]
    public void Tick_RunsCurveAndFinishes()
    {
        var model = new InterruptDarkAdaptationModel();
        model.Play(1f, FadeIn, Hold, FadeOut);
        Assert.IsTrue(model.IsPlaying);
        Assert.AreEqual(0f, model.Value, Eps, "Play 직후");

        Assert.AreEqual(1f, model.Tick(FadeIn), Eps, "fadeIn 끝");
        Assert.AreEqual(1f, model.Tick(Hold), Eps, "유지 끝 = fadeOut 시작");
        Assert.AreEqual(0.5f, model.Tick(0.25f), Eps, "fadeOut 중간");
        Assert.AreEqual(0f, model.Tick(0.25f), Eps, "끝");
        Assert.IsFalse(model.IsPlaying);
        Assert.AreEqual(0f, model.Tick(1f), Eps, "끝난 뒤 Tick");
    }

    // ---- 본인/타인 ----

    [Test]
    public void SelectIntensity_SelfOrOthers()
    {
        Assert.AreEqual(1f, InterruptDarkAdaptationModel.SelectIntensity(true, 1f, 0.25f), Eps, "본인");
        Assert.AreEqual(0.25f, InterruptDarkAdaptationModel.SelectIntensity(false, 1f, 0.25f), Eps, "타인");
    }

    // ---- 재시작 ----

    [Test]
    public void Replay_WhilePlaying_StartsFromCurrentValue()
    {
        var model = new InterruptDarkAdaptationModel();
        model.Play(1f, FadeIn, Hold, FadeOut);
        model.Tick(FadeIn + Hold + 0.25f);           // fadeOut 중간 = 0.5
        Assert.AreEqual(0.5f, model.Value, Eps);

        model.Play(1f, FadeIn, Hold, FadeOut);
        Assert.AreEqual(0.5f, model.Value, Eps, "재시작 순간 튀지 않는다");
        Assert.AreEqual(0.75f, model.Tick(0.125f), Eps, "현재 값 → 최대로 다시 fadeIn");
        Assert.AreEqual(1f, model.Tick(0.125f), Eps);
    }

    [Test]
    public void Replay_LowerPeak_FadesDownWithoutJump()
    {
        var model = new InterruptDarkAdaptationModel();
        model.Play(1f, FadeIn, Hold, FadeOut);
        model.Tick(FadeIn);

        model.Play(0.5f, FadeIn, Hold, FadeOut);
        Assert.AreEqual(1f, model.Value, Eps, "재시작 순간");
        Assert.AreEqual(0.75f, model.Tick(0.125f), Eps);
        Assert.AreEqual(0.5f, model.Tick(0.125f), Eps);
    }

    [Test]
    public void Replay_AfterFinished_StartsFromZero()
    {
        var model = new InterruptDarkAdaptationModel();
        model.Play(1f, FadeIn, Hold, FadeOut);
        model.Tick(10f);

        model.Play(1f, FadeIn, Hold, FadeOut);
        Assert.AreEqual(0f, model.Value, Eps);
    }

    [Test]
    public void Stop_ImmediatelyZero()
    {
        var model = new InterruptDarkAdaptationModel();
        model.Play(1f, FadeIn, Hold, FadeOut);
        model.Tick(FadeIn);
        model.Stop();
        Assert.IsFalse(model.IsPlaying);
        Assert.AreEqual(0f, model.Value, Eps);

        model.Play(1f, FadeIn, Hold, FadeOut);
        Assert.AreEqual(0f, model.Value, Eps, "Stop 뒤 재생은 0 부터");
    }

    // ---- 정리 규칙 ----

    [Test]
    public void Sanitize_NaNAndNegative()
    {
        Assert.AreEqual(0f, InterruptDarkAdaptationModel.SanitizeIntensity(float.NaN), Eps);
        Assert.AreEqual(0f, InterruptDarkAdaptationModel.SanitizeIntensity(-1f), Eps);
        Assert.AreEqual(1f, InterruptDarkAdaptationModel.SanitizeIntensity(2f), Eps);
        Assert.AreEqual(0f, InterruptDarkAdaptationModel.SanitizeDuration(float.NaN), Eps);
        Assert.AreEqual(0f, InterruptDarkAdaptationModel.SanitizeDuration(-1f), Eps);
        Assert.AreEqual(0f, InterruptDarkAdaptationModel.SelectIntensity(true, float.NaN, 0.5f), Eps);
    }

    [Test]
    public void Radius_ZeroOrInvalid_DisablesReveal()
    {
        Assert.AreEqual(0f, InterruptDarkAdaptationModel.SanitizeRadius(0f), Eps);
        Assert.AreEqual(0f, InterruptDarkAdaptationModel.SanitizeRadius(-0.5f), Eps);
        Assert.AreEqual(0f, InterruptDarkAdaptationModel.SanitizeRadius(float.NaN), Eps);
        Assert.AreEqual(0.25f, InterruptDarkAdaptationModel.SanitizeRadius(0.25f), Eps);
    }

    [Test]
    public void Play_ZeroOrNaNPeak_DoesNotPlay()
    {
        var model = new InterruptDarkAdaptationModel();
        model.Play(0f, FadeIn, Hold, FadeOut);
        Assert.IsFalse(model.IsPlaying);

        model.Play(float.NaN, FadeIn, Hold, FadeOut);
        Assert.IsFalse(model.IsPlaying);
        Assert.AreEqual(0f, model.Tick(0.1f), Eps);
    }

    [Test]
    public void Play_NaNDurations_TreatedAsZero()
    {
        var model = new InterruptDarkAdaptationModel();
        model.Play(1f, float.NaN, Hold, -1f);
        Assert.AreEqual(1f, model.Value, Eps, "fadeIn NaN = 0 → 바로 최대");
        Assert.AreEqual(0f, model.Tick(Hold), Eps, "fadeOut 음수 = 0 → 유지 끝에 바로 끝");
        Assert.IsFalse(model.IsPlaying);
    }
}
