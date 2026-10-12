using NUnit.Framework;

/// <summary>
/// 오너 슬로우 줌인 규칙(<see cref="SlowMotionCameraZoom"/>) — 내 슬로우/남의 슬로우/유휴/낙하·관전, 배율 경계.
/// 수치는 2 진수로 딱 떨어지는 값(0.5·0.75 …)을 쓴다.
/// </summary>
public sealed class SlowMotionCameraZoomTests
{
    const float Eps = 1e-5f;
    const float ProfileScale = 0.5f;

    // ---- 목표 깊이 ----

    [Test]
    public void MySlow_DepthFollowsSlowDepth()
    {
        Assert.AreEqual(0f, SlowMotionCameraZoom.TargetDepth(true, true, true, 1f, ProfileScale), Eps, "진입 직전");
        Assert.AreEqual(0.5f, SlowMotionCameraZoom.TargetDepth(true, true, true, 0.75f, ProfileScale), Eps, "진입 중간");
        Assert.AreEqual(1f, SlowMotionCameraZoom.TargetDepth(true, true, true, ProfileScale, ProfileScale), Eps, "유지");
    }

    [Test]
    public void MySlow_ScaleBelowProfile_ClampsToOne()
    {
        Assert.AreEqual(1f, SlowMotionCameraZoom.TargetDepth(true, true, true, 0.25f, ProfileScale), Eps);
    }

    [Test]
    public void OthersSlow_NoZoom()
    {
        Assert.AreEqual(0f, SlowMotionCameraZoom.TargetDepth(true, false, true, ProfileScale, ProfileScale), Eps);
    }

    [Test]
    public void Idle_NoZoom()
    {
        Assert.AreEqual(0f, SlowMotionCameraZoom.TargetDepth(false, true, true, 1f, ProfileScale), Eps);
    }

    [Test]
    public void FallViewOrSpectator_NoZoom()
    {
        Assert.AreEqual(0f, SlowMotionCameraZoom.TargetDepth(true, true, false, ProfileScale, ProfileScale), Eps);
    }

    [Test]
    public void ProfileThatDoesNotSlow_NoZoom()
    {
        Assert.AreEqual(0f, SlowMotionCameraZoom.TargetDepth(true, true, true, 1f, 1f), Eps);
    }

    [Test]
    public void NaNInputs_NoZoom()
    {
        Assert.AreEqual(0f, SlowMotionCameraZoom.TargetDepth(true, true, true, float.NaN, ProfileScale), Eps);
        Assert.AreEqual(0f, SlowMotionCameraZoom.TargetDepth(true, true, true, ProfileScale, float.NaN), Eps);
    }

    // ---- 오프셋 배율 ----

    [Test]
    public void Multiplier_LerpsFromOneToZoomFactor()
    {
        Assert.AreEqual(1f, SlowMotionCameraZoom.OffsetMultiplier(0f, 0.75f), Eps);
        Assert.AreEqual(0.875f, SlowMotionCameraZoom.OffsetMultiplier(0.5f, 0.75f), Eps);
        Assert.AreEqual(0.75f, SlowMotionCameraZoom.OffsetMultiplier(1f, 0.75f), Eps);
    }

    [Test]
    public void Multiplier_DepthZeroIsExactlyOne()
    {
        // 복귀가 끝나면 원래 오프셋으로 정확히 돌아와야 한다.
        Assert.AreEqual(1f, SlowMotionCameraZoom.OffsetMultiplier(0f, 0.85f));
    }

    [Test]
    public void Multiplier_DepthOutOfRange_Clamped()
    {
        Assert.AreEqual(0.75f, SlowMotionCameraZoom.OffsetMultiplier(2f, 0.75f), Eps);
        Assert.AreEqual(1f, SlowMotionCameraZoom.OffsetMultiplier(-1f, 0.75f), Eps);
        Assert.AreEqual(1f, SlowMotionCameraZoom.OffsetMultiplier(float.NaN, 0.75f), Eps);
    }

    [Test]
    public void ZoomFactor_ClampedToPullInOnly()
    {
        Assert.AreEqual(1f, SlowMotionCameraZoom.ClampZoomFactor(1.5f), Eps, "줌아웃 배율은 1");
        Assert.AreEqual(SlowMotionCameraZoom.MinZoomFactor, SlowMotionCameraZoom.ClampZoomFactor(0f), Eps, "0 은 하한");
        Assert.AreEqual(SlowMotionCameraZoom.MinZoomFactor, SlowMotionCameraZoom.ClampZoomFactor(-1f), Eps);
        Assert.AreEqual(1f, SlowMotionCameraZoom.ClampZoomFactor(float.NaN), Eps, "NaN 은 줌 없음");
    }

    // ---- 감쇠 ----

    [Test]
    public void StepDepth_LimitedByBlendSpeed()
    {
        float half = SlowMotionCameraZoom.DepthBlendSeconds * 0.5f;
        Assert.AreEqual(0.5f, SlowMotionCameraZoom.StepDepth(1f, 0f, half), Eps, "발동자 전환 — 한 번에 튀지 않는다");
        Assert.AreEqual(0f, SlowMotionCameraZoom.StepDepth(1f, 0f, SlowMotionCameraZoom.DepthBlendSeconds), Eps);
        Assert.AreEqual(0.25f, SlowMotionCameraZoom.StepDepth(0f, 0.25f, 1f), Eps, "목표를 넘지 않는다");
    }

    [Test]
    public void StepDepth_NegativeDeltaOrNaN_Safe()
    {
        Assert.AreEqual(0.5f, SlowMotionCameraZoom.StepDepth(0.5f, 0f, -1f), Eps);
        Assert.AreEqual(0f, SlowMotionCameraZoom.StepDepth(float.NaN, 0f, 0f), Eps);
    }
}
