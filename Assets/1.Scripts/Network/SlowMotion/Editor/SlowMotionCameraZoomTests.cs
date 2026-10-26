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

    [Test]
    public void SuccessSlow_DepthUsesCurrentProfileScale()
    {
        // 할 일 87: 시도(0.5) 유지 중 성공(0.25)으로 다시 걸면, 줌 깊이는 지금 재생 중인 프로필(0.25) 기준이다.
        var session = new SlowMotionSession();
        var attempt = new SlowMotionProfile { scale = 0.5f, enterDuration = 0.125f, holdDuration = 0.25f, exitDuration = 0.25f };
        var success = new SlowMotionProfile { scale = 0.25f, enterDuration = 0.125f, holdDuration = 1f, exitDuration = 0.5f };
        session.Apply(SlowMotionEvent.CreateStart(1, 8.0, 1, attempt));
        Assert.AreEqual(1f, Depth(session, 8.25), Eps, "시도 유지 = 시도 프로필 기준 최대 깊이");

        session.Apply(SlowMotionEvent.CreateStart(2, 8.25, 1, success, SlowMotionPurpose.Success));
        Assert.AreEqual(0.25f, session.CurrentProfileScale, Eps);
        Assert.AreEqual(0.5f / 0.75f, Depth(session, 8.25), Eps, "성공 진입 시작 — 성공 프로필 기준으로 다시 깊어진다");
        Assert.AreEqual(1f, Depth(session, 8.25 + 0.5), Eps, "성공 유지 = 최대 깊이");
    }

    [Test]
    public void HitStopBelowProfileScale_DepthPinnedToOne()
    {
        // 할 일 87 C2: 히트스톱 배율(0.125)이 프로필 배율(0.25)보다 낮으면 줌 규칙은 그대로 두고 깊이 1 로 고정된다.
        var session = new SlowMotionSession();
        var success = new SlowMotionProfile
        {
            scale = 0.25f, hitStopDuration = 0.0625f, hitStopScale = 0.125f,
            enterDuration = 0.125f, holdDuration = 1f, exitDuration = 0.5f,
        };
        session.Apply(SlowMotionEvent.CreateStart(1, 8.0, 1, success, SlowMotionPurpose.Success));

        Assert.AreEqual(SlowMotionPhase.HitStop, session.PhaseAt(8.03125));
        Assert.AreEqual(1f, Depth(session, 8.0), Eps, "히트스톱 시작");
        Assert.AreEqual(1f, Depth(session, 8.03125), Eps, "히트스톱 중");
        Assert.AreEqual(1f, Depth(session, 8.0625 + 0.0625), Eps, "히트스톱 배율 → 프로필 배율 진입 중에도 1");
        Assert.AreEqual(1f, Depth(session, 8.0625 + 0.5), Eps, "유지");
    }

    static float Depth(SlowMotionSession session, double t) =>
        SlowMotionCameraZoom.TargetDepth(true, true, true, session.ScaleAt(t), session.CurrentProfileScale);

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
