using NUnit.Framework;

/// <summary>
/// 보스 엘리베이터 순수 시간표(<see cref="BossElevatorTimeline"/>) — 단계 시각, 이징 끝점·중간값, 경과 시간 → 단계·높이·상승 속도·문 포즈·페이드 알파(늦은 합류 포함).
/// 기본 수치: 높이 12(테스트 고정 — 설정 기본은 0 = 자동), 대기 0.5, 하강 1.2, 문 열림 1.76·닫힘 1.68(클립 길이), 사라짐 뒤 닫힘 1.5, 상승 가속 1.5·속도 24, 페이드 지연 0·길이 1.5.
/// </summary>
public sealed class BossElevatorTimelineTests
{
    const float Eps = 1e-4f;
    const float ClipOpen = 1.76f;
    const float ClipClose = 1.68f;
    const float Speed = 24f;
    const float Accel = 1.5f;
    // ∫₀¹ InCirc = 1 - π/4
    static readonly float InCircArea = 1f - (float)System.Math.PI / 4f;

    // 기본 설정의 높이는 0(자동 — 리그가 칸 대기 높이로 채운다)이라 시간표 테스트는 12 로 고정한다.
    static BossElevatorTimingSettings Settings()
    {
        BossElevatorTimingSettings s = BossElevatorTimingSettings.Default;
        s.height = 12f;
        return s;
    }

    static BossElevatorTimeline Default() =>
        new BossElevatorTimeline(Settings(), ClipOpen, ClipClose);

    // ─── 높이 자동 ─────────────────────────────────────────────────────────

    [Test]
    public void Height_ZeroOrLess_UsesIdleAboveLanded()
    {
        BossElevatorTimingSettings s = BossElevatorTimingSettings.Default;
        Assert.AreEqual(0f, s.height, Eps, "기본 = 자동");
        Assert.AreEqual(10.44f, s.WithResolvedHeight(10.99f - 0.55f).height, Eps, "대기 y 10.99 − 착지 0.55");
        s.height = -1f;
        Assert.AreEqual(3f, s.WithResolvedHeight(3f).height, Eps, "음수도 자동");
        Assert.AreEqual(0f, s.WithResolvedHeight(-2f).height, Eps, "대기가 착지보다 낮으면 0");

        var t = new BossElevatorTimeline(BossElevatorTimingSettings.Default.WithResolvedHeight(10.44f), ClipOpen, ClipClose);
        Assert.AreEqual(10.44f, t.HeightAt(0.0), Eps, "재생 시작 = 칸 대기 자리(순간이동 없음)");
    }

    [Test]
    public void Height_Positive_KeepsSetting()
    {
        Assert.AreEqual(12f, Settings().WithResolvedHeight(3f).height, Eps);
    }

    // ─── 단계 시각 ───────────────────────────────────────────────────────

    [Test]
    public void PhaseStarts_DefaultSettings()
    {
        BossElevatorTimeline t = Default();
        float ascend = 1.7f + ClipOpen + 1.5f + ClipClose;
        Assert.AreEqual(0f, t.StartOf(BossElevatorPhase.TeleportFront), Eps);
        Assert.AreEqual(0.5f, t.StartOf(BossElevatorPhase.Descending), Eps);
        Assert.AreEqual(1.7f, t.StartOf(BossElevatorPhase.DoorOpening), Eps, "하강 1.2 끝");
        Assert.AreEqual(1.7f + ClipOpen, t.StartOf(BossElevatorPhase.Vanished), Eps, "Open 클립 길이 뒤");
        Assert.AreEqual(1.7f + ClipOpen + 1.5f, t.StartOf(BossElevatorPhase.DoorClosing), Eps, "사라짐 + 1.5");
        Assert.AreEqual(ascend, t.StartOf(BossElevatorPhase.Ascending), Eps, "Close 끝 뒤");
        Assert.AreEqual(ascend, t.StartOf(BossElevatorPhase.FadingOut), Eps, "페이드 지연 0 = 상승과 동시");
        Assert.AreEqual(ascend + 1.5f, t.StartOf(BossElevatorPhase.Blackout), Eps, "페이드 1.5");
        Assert.AreEqual(1.5f, t.FadeOutDuration, Eps);
    }

    [Test]
    public void DoorDurations_SettingOverridesClipLength()
    {
        BossElevatorTimingSettings s = Settings();
        s.doorOpenDuration = 1f;
        s.doorCloseDuration = 2f;
        var t = new BossElevatorTimeline(s, ClipOpen, ClipClose);
        Assert.AreEqual(1f, t.DoorOpenDuration, Eps);
        Assert.AreEqual(2f, t.DoorCloseDuration, Eps);
        Assert.AreEqual(1.7f + 1f, t.VanishAt, Eps);
        Assert.AreEqual(1.7f + 1f + 1.5f + 2f, t.AscendStartAt, Eps);
    }

    [Test]
    public void FadeOutDelay_ShiftsFadeAndBlackout()
    {
        BossElevatorTimingSettings s = Settings();
        s.fadeOutDelay = 0.4f;
        s.fadeOutDuration = 2f;
        var t = new BossElevatorTimeline(s, ClipOpen, ClipClose);
        Assert.AreEqual(t.AscendStartAt + 0.4f, t.FadeOutStartAt, Eps);
        Assert.AreEqual(t.AscendStartAt + 2.4f, t.BlackoutAt, Eps);
        Assert.AreEqual(BossElevatorPhase.Ascending, t.PhaseAt(t.AscendStartAt + 0.2));
        Assert.AreEqual(BossElevatorPhase.FadingOut, t.PhaseAt(t.FadeOutStartAt));
        Assert.AreEqual(BossElevatorPhase.Blackout, t.PhaseAt(t.BlackoutAt));
    }

    [Test]
    public void PhaseAt_BoundaryBelongsToNextPhase()
    {
        BossElevatorTimeline t = Default();
        Assert.AreEqual(BossElevatorPhase.TeleportFront, t.PhaseAt(-1.0), "시작 전 = 첫 단계");
        Assert.AreEqual(BossElevatorPhase.TeleportFront, t.PhaseAt(0.0));
        Assert.AreEqual(BossElevatorPhase.TeleportFront, t.PhaseAt(0.49));
        Assert.AreEqual(BossElevatorPhase.Descending, t.PhaseAt(0.5));
        Assert.AreEqual(BossElevatorPhase.DoorOpening, t.PhaseAt(t.DoorOpenAt));
        Assert.AreEqual(BossElevatorPhase.Vanished, t.PhaseAt(t.VanishAt));
        Assert.AreEqual(BossElevatorPhase.DoorClosing, t.PhaseAt(t.DoorCloseAt));
        Assert.AreEqual(BossElevatorPhase.FadingOut, t.PhaseAt(t.AscendStartAt), "페이드 지연 0 = Ascending 은 0 길이");
        Assert.AreEqual(BossElevatorPhase.Blackout, t.PhaseAt(t.BlackoutAt));
        Assert.AreEqual(BossElevatorPhase.Blackout, t.PhaseAt(t.BlackoutAt + 100.0), "Blackout 은 끝이 없다");
    }

    // ─── 이징 ─────────────────────────────────────────────────────────────

    [Test]
    public void Easing_Endpoints()
    {
        Assert.AreEqual(0f, BossElevatorTimeline.EaseOutQuint(0f), Eps);
        Assert.AreEqual(1f, BossElevatorTimeline.EaseOutQuint(1f), Eps);
        Assert.AreEqual(0f, BossElevatorTimeline.EaseInCirc(0f), Eps);
        Assert.AreEqual(1f, BossElevatorTimeline.EaseInCirc(1f), Eps);
    }

    [Test]
    public void Easing_Midpoints()
    {
        // OutQuint(0.5) = 1 - 0.5^5 = 0.96875 — 빨리 내려와 천천히 멈춘다.
        Assert.AreEqual(0.96875f, BossElevatorTimeline.EaseOutQuint(0.5f), Eps);
        // InCirc(0.5) = 1 - sqrt(0.75) ≈ 0.133975 — 천천히 출발해 가속한다.
        Assert.AreEqual(1f - (float)System.Math.Sqrt(0.75), BossElevatorTimeline.EaseInCirc(0.5f), Eps);
    }

    // ─── 높이 ─────────────────────────────────────────────────────────────

    [Test]
    public void Height_DescentAndDoors()
    {
        BossElevatorTimeline t = Default();
        Assert.AreEqual(12f, t.HeightAt(0.0), Eps, "순간이동 순간 = 위에서 대기");
        Assert.AreEqual(12f, t.HeightAt(0.5), Eps, "하강 시작");
        Assert.AreEqual(12f * (1f - 0.96875f), t.HeightAt(0.5 + 0.6), Eps, "하강 중간 = OutQuint");
        Assert.AreEqual(0f, t.HeightAt(t.DoorOpenAt), Eps, "착지");
        Assert.AreEqual(0f, t.HeightAt(t.VanishAt + 0.1), Eps);
        Assert.AreEqual(0f, t.HeightAt(t.AscendStartAt), Eps, "상승 시작");
    }

    [Test]
    public void Ascent_AcceleratesThenKeepsSpeed_NoCeiling()
    {
        BossElevatorTimeline t = Default();
        double a = t.AscendStartAt;
        float accelDistance = Speed * Accel * InCircArea;
        Assert.AreEqual(accelDistance, t.HeightAt(a + Accel), 1e-3f, "가속 끝 = 속도 × 가속 시간 × (1 - π/4)");
        Assert.AreEqual(accelDistance + Speed * 1f, t.HeightAt(a + Accel + 1.0), 1e-3f, "가속 뒤 등속");
        Assert.AreEqual(accelDistance + Speed * 100f, t.HeightAt(a + Accel + 100.0), 1e-1f, "Blackout 뒤에도 상한 없음");
        Assert.Less(t.HeightAt(a + 0.75), accelDistance * 0.5f, "가속 전반은 느리다(InCirc)");
    }

    [Test]
    public void Ascent_SpeedFollowsInCircAndIsContinuous()
    {
        BossElevatorTimeline t = Default();
        double a = t.AscendStartAt;
        Assert.AreEqual(0f, t.AscendSpeedAt(a - 0.1), Eps, "상승 전 0");
        Assert.AreEqual(0f, t.AscendSpeedAt(a), Eps, "정지에서 출발");
        Assert.AreEqual(Speed * BossElevatorTimeline.EaseInCirc(0.5f), t.AscendSpeedAt(a + Accel * 0.5), Eps);
        Assert.AreEqual(Speed, t.AscendSpeedAt(a + Accel + 10.0), Eps, "가속 뒤 유지");

        // 높이의 수치 미분이 속도와 맞는다(가속 중간·가속 끝 직후).
        const double h = 1e-3;
        foreach (double s in new[] { Accel * 0.5, Accel * 0.9, Accel + 0.5 })
        {
            float numeric = (float)((t.HeightAt(a + s + h) - t.HeightAt(a + s - h)) / (2 * h));
            Assert.AreEqual(t.AscendSpeedAt(a + s), numeric, 0.1f, $"s={s}");
        }
    }

    [Test]
    public void Ascent_ZeroAccel_IsConstantSpeed()
    {
        BossElevatorTimingSettings s = Settings();
        s.ascendDuration = 0f;
        var t = new BossElevatorTimeline(s, ClipOpen, ClipClose);
        Assert.AreEqual(Speed, t.AscendSpeedAt(t.AscendStartAt), Eps);
        Assert.AreEqual(Speed * 2f, t.HeightAt(t.AscendStartAt + 2.0), 1e-3f);
    }

    // ─── 페이드 ───────────────────────────────────────────────────────────

    [Test]
    public void FadeAlpha_LinearToBlackout()
    {
        BossElevatorTimeline t = Default();
        Assert.AreEqual(0f, t.FadeAlphaAt(t.DoorCloseAt), Eps, "상승 전 투명");
        Assert.AreEqual(0f, t.FadeAlphaAt(t.FadeOutStartAt), Eps);
        Assert.AreEqual(0.5f, t.FadeAlphaAt(t.FadeOutStartAt + 0.75), Eps);
        Assert.AreEqual(1f, t.FadeAlphaAt(t.BlackoutAt), Eps, "Blackout = 완전 암전");
        Assert.AreEqual(1f, t.FadeAlphaAt(t.BlackoutAt + 5.0), Eps);
    }

    [Test]
    public void FadeAlpha_ZeroDuration_JumpsAtBlackout()
    {
        BossElevatorTimingSettings s = Settings();
        s.fadeOutDelay = 0.5f;
        s.fadeOutDuration = 0f;
        var t = new BossElevatorTimeline(s, ClipOpen, ClipClose);
        Assert.AreEqual(t.FadeOutStartAt, t.BlackoutAt, Eps);
        Assert.AreEqual(0f, t.FadeAlphaAt(t.BlackoutAt - 0.01), Eps);
        Assert.AreEqual(1f, t.FadeAlphaAt(t.BlackoutAt), Eps);
    }

    // ─── 늦은 합류 ────────────────────────────────────────────────────────

    [Test]
    public void LateJoin_MidDoorOpen_ResumesClipFromMiddle()
    {
        BossElevatorTimeline t = Default();
        double elapsed = t.DoorOpenAt + ClipOpen * 0.25;
        Assert.AreEqual(BossElevatorPhase.DoorOpening, t.PhaseAt(elapsed));
        Assert.AreEqual(0f, t.HeightAt(elapsed), Eps);
        Assert.AreEqual(BossElevatorDoorState.Opening, t.DoorPoseAt(elapsed, out float n));
        Assert.AreEqual(0.25f, n, Eps);
    }

    [Test]
    public void LateJoin_DoorPoseAcrossPhases()
    {
        BossElevatorTimeline t = Default();
        Assert.AreEqual(BossElevatorDoorState.Closed, t.DoorPoseAt(1.0, out _), "하강 중 = 닫힘");
        Assert.AreEqual(BossElevatorDoorState.Open, t.DoorPoseAt(t.VanishAt + 1.0, out float open));
        Assert.AreEqual(1f, open, Eps);
        Assert.AreEqual(BossElevatorDoorState.Closing, t.DoorPoseAt(t.DoorCloseAt + ClipClose * 0.5, out float closing));
        Assert.AreEqual(0.5f, closing, Eps);
        Assert.AreEqual(BossElevatorDoorState.Closed, t.DoorPoseAt(t.AscendStartAt + 0.1, out _), "상승 중 = 닫힘");
    }

    [Test]
    public void LateJoin_AfterBlackout_IsBlackAndClosed()
    {
        BossElevatorTimeline t = Default();
        double elapsed = t.BlackoutAt + 30.0;
        Assert.AreEqual(BossElevatorPhase.Blackout, t.PhaseAt(elapsed));
        Assert.AreEqual(1f, t.FadeAlphaAt(elapsed), Eps);
        Assert.AreEqual(BossElevatorDoorState.Closed, t.DoorPoseAt(elapsed, out _));
    }

    [Test]
    public void ZeroDurations_DoNotDivideByZero()
    {
        var s = new BossElevatorTimingSettings { height = 5f, doorOpenDuration = 0f, doorCloseDuration = 0f };
        var t = new BossElevatorTimeline(s, 0f, 0f);
        Assert.AreEqual(0f, t.BlackoutAt, Eps);
        Assert.AreEqual(BossElevatorPhase.Blackout, t.PhaseAt(0.0));
        Assert.AreEqual(0f, t.HeightAt(0.0), Eps, "속도 0 = 착지 자리");
        Assert.AreEqual(1f, t.FadeAlphaAt(0.0), Eps);
    }
}
