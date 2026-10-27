using NUnit.Framework;

// 할 일 92-5 — Dodge 구간(웅크리기 0~10f / 공중 10~34f / 착지 34~62f, 60fps)을 대시 지속 안에 넣는 규칙.
public sealed class DodgePhaseTimingTests
{
    private const float Fps = 60f;
    private const float Duration = 0.25f;
    private const float Tolerance = 1e-5f;

    private static DodgePhaseTiming Default(float ratio = 0.95f, float duration = Duration) =>
        new DodgePhaseTiming(10, 34, 62, Fps, ratio, duration);

    [Test]
    public void AllottedSeconds_AirborneIsRatio_RestSplitByFrameLength()
    {
        DodgePhaseTiming t = Default();
        Assert.That(t.IsValid, Is.True);
        Assert.That(t.AirborneSeconds, Is.EqualTo(Duration * 0.95f).Within(Tolerance));
        Assert.That(t.CrouchSeconds, Is.EqualTo(Duration * 0.05f * 10f / 38f).Within(Tolerance));
        Assert.That(t.LandingSeconds, Is.EqualTo(Duration * 0.05f * 28f / 38f).Within(Tolerance));
        Assert.That(t.Duration, Is.EqualTo(Duration).Within(Tolerance), "구간 시간 합 = 대시 지속");
    }

    [Test]
    public void PhaseAt_Boundaries()
    {
        DodgePhaseTiming t = Default();
        Assert.That(t.PhaseAt(0f), Is.EqualTo(DodgePhase.Crouch));
        Assert.That(t.PhaseAt(t.CrouchSeconds), Is.EqualTo(DodgePhase.Airborne));
        Assert.That(t.PhaseAt(t.CrouchSeconds + t.AirborneSeconds), Is.EqualTo(DodgePhase.Landing));
        Assert.That(t.PhaseAt(Duration), Is.EqualTo(DodgePhase.Landing));
    }

    [Test]
    public void ClipFrame_AtProgress0_Boundaries_And1()
    {
        DodgePhaseTiming t = Default();
        Assert.That(t.ClipFrameAt(0f, Fps), Is.EqualTo(0f).Within(1e-3f));
        Assert.That(t.ClipFrameAt(t.CrouchSeconds, Fps), Is.EqualTo(10f).Within(1e-3f));
        Assert.That(t.ClipFrameAt(t.CrouchSeconds + t.AirborneSeconds, Fps), Is.EqualTo(34f).Within(1e-3f));
        Assert.That(t.ClipFrameAt(Duration, Fps), Is.EqualTo(62f).Within(1e-3f), "대시 끝 = 클립 62f");
        Assert.That(t.ClipFrameAt(Duration + 1f, Fps), Is.EqualTo(62f).Within(1e-3f), "끝 뒤는 62f 에서 멈춘다");
    }

    [Test]
    public void SpeedOf_IsClipLengthOverAllotted()
    {
        DodgePhaseTiming t = Default();
        Assert.That(t.SpeedOf(DodgePhase.Crouch), Is.EqualTo((10f / Fps) / t.CrouchSeconds).Within(1e-3f));
        Assert.That(t.SpeedOf(DodgePhase.Airborne), Is.EqualTo((24f / Fps) / t.AirborneSeconds).Within(1e-4f));
        Assert.That(t.SpeedOf(DodgePhase.Landing), Is.EqualTo((28f / Fps) / t.LandingSeconds).Within(1e-3f));
        // 0.25s · 0.95: 공중 ≈ 1.684, 웅크리기 = 착지 ≈ 50.67(5% 를 프레임 비로 나눠 둘이 같다)
        Assert.That(t.SpeedOf(DodgePhase.Airborne), Is.EqualTo(1.6842f).Within(1e-3f));
        Assert.That(t.SpeedOf(DodgePhase.Crouch), Is.EqualTo(50.667f).Within(1e-2f));
        Assert.That(t.SpeedOf(DodgePhase.Landing), Is.EqualTo(50.667f).Within(1e-2f));
    }

    [TestCase(1f / 60f)]
    [TestCase(1f / 144f)]
    [TestCase(1f / 30f)]
    public void IntegratedFrameSpeeds_ReachEndFrameWhenDashEnds(float dt)
    {
        DodgePhaseTiming t = Default();
        float clock = 0f;
        float clipTime = 0f;
        while (clock < Duration)
        {
            float step = System.Math.Min(dt, Duration - clock);
            clipTime += t.FrameSpeed(clock, clock + step) * step;
            clock += step;
        }

        Assert.That(clipTime * Fps, Is.EqualTo(62f).Within(1e-2f), "Animator 가 쌓는 방식(dt×속도)으로도 대시 끝 = 62f");
        Assert.That(t.FrameSpeed(Duration, Duration + dt), Is.EqualTo(0f), "끝 뒤는 멈춤");
    }

    [Test]
    public void FrameSpeed_InsideOnePhase_IsPhaseSpeed()
    {
        DodgePhaseTiming t = Default();
        float airStart = t.CrouchSeconds;
        Assert.That(t.FrameSpeed(airStart + 0.01f, airStart + 0.02f), Is.EqualTo(t.SpeedOf(DodgePhase.Airborne)));
    }

    [Test]
    public void Ratio1_GivesAllTimeToAirborne()
    {
        DodgePhaseTiming t = Default(ratio: 1f);
        Assert.That(t.AirborneSeconds, Is.EqualTo(Duration).Within(Tolerance));
        Assert.That(t.Duration, Is.EqualTo(Duration).Within(Tolerance));
        Assert.That(t.ClipFrameAt(Duration, Fps), Is.EqualTo(62f).Within(1e-3f));
    }

    [Test]
    public void InvalidInput_IsNotValid()
    {
        Assert.That(new DodgePhaseTiming(10, 34, 62, Fps, 0.95f, 0f).IsValid, Is.False);
        Assert.That(new DodgePhaseTiming(40, 34, 62, Fps, 0.95f, Duration).IsValid, Is.False);
        Assert.That(new DodgePhaseTiming(10, 34, 62, 0f, 0.95f, Duration).IsValid, Is.False);
    }
}
