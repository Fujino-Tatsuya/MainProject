using NUnit.Framework;

/// <summary>
/// 보스 엘리베이터 순수 시간표(<see cref="BossElevatorTimeline"/>) — 단계 시각, 이징 끝점·중간값, 경과 시간 → 단계·높이·문 포즈(늦은 합류 포함).
/// 기본 수치: 높이 12, 대기 0.5, 하강 1.2, 문 열림 1.76·닫힘 1.68(클립 길이), 사라짐 뒤 닫힘 1.5, 상승 1.5.
/// </summary>
public sealed class BossElevatorTimelineTests
{
    const float Eps = 1e-4f;
    const float ClipOpen = 1.76f;
    const float ClipClose = 1.68f;

    static BossElevatorTimeline Default() =>
        new BossElevatorTimeline(BossElevatorTimingSettings.Default, ClipOpen, ClipClose);

    // ─── 단계 시각 ───────────────────────────────────────────────────────

    [Test]
    public void PhaseStarts_DefaultSettings()
    {
        BossElevatorTimeline t = Default();
        Assert.AreEqual(0f, t.StartOf(BossElevatorPhase.TeleportFront), Eps);
        Assert.AreEqual(0.5f, t.StartOf(BossElevatorPhase.Descending), Eps);
        Assert.AreEqual(1.7f, t.StartOf(BossElevatorPhase.DoorOpening), Eps, "하강 1.2 끝");
        Assert.AreEqual(1.7f + ClipOpen, t.StartOf(BossElevatorPhase.Vanished), Eps, "Open 클립 길이 뒤");
        Assert.AreEqual(1.7f + ClipOpen + 1.5f, t.StartOf(BossElevatorPhase.DoorClosing), Eps, "사라짐 + 1.5");
        Assert.AreEqual(1.7f + ClipOpen + 1.5f + ClipClose, t.StartOf(BossElevatorPhase.Ascending), Eps, "Close 끝 뒤");
        Assert.AreEqual(1.7f + ClipOpen + 1.5f + ClipClose + 1.5f, t.StartOf(BossElevatorPhase.Done), Eps, "상승 1.5");
    }

    [Test]
    public void DoorDurations_SettingOverridesClipLength()
    {
        BossElevatorTimingSettings s = BossElevatorTimingSettings.Default;
        s.doorOpenDuration = 1f;
        s.doorCloseDuration = 2f;
        var t = new BossElevatorTimeline(s, ClipOpen, ClipClose);
        Assert.AreEqual(1f, t.DoorOpenDuration, Eps);
        Assert.AreEqual(2f, t.DoorCloseDuration, Eps);
        Assert.AreEqual(1.7f + 1f, t.VanishAt, Eps);
        Assert.AreEqual(1.7f + 1f + 1.5f + 2f, t.AscendStartAt, Eps);
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
        Assert.AreEqual(BossElevatorPhase.Ascending, t.PhaseAt(t.AscendStartAt));
        Assert.AreEqual(BossElevatorPhase.Done, t.PhaseAt(t.DoneAt));
        Assert.AreEqual(BossElevatorPhase.Done, t.PhaseAt(t.DoneAt + 100.0));
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
    public void Height_ByPhase()
    {
        BossElevatorTimeline t = Default();
        Assert.AreEqual(12f, t.HeightAt(0.0), Eps, "순간이동 순간 = 위에서 대기");
        Assert.AreEqual(12f, t.HeightAt(0.5), Eps, "하강 시작");
        Assert.AreEqual(12f * (1f - 0.96875f), t.HeightAt(0.5 + 0.6), Eps, "하강 중간 = OutQuint");
        Assert.AreEqual(0f, t.HeightAt(t.DoorOpenAt), Eps, "착지");
        Assert.AreEqual(0f, t.HeightAt(t.VanishAt + 0.1), Eps);
        Assert.AreEqual(0f, t.HeightAt(t.AscendStartAt), Eps, "상승 시작");
        Assert.AreEqual(12f * BossElevatorTimeline.EaseInCirc(0.5f), t.HeightAt(t.AscendStartAt + 0.75), Eps, "상승 중간 = InCirc");
        Assert.AreEqual(12f, t.HeightAt(t.DoneAt), Eps, "끝 = 위");
        Assert.AreEqual(12f, t.HeightAt(t.DoneAt + 5.0), Eps);
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
    public void LateJoin_AfterDone_IsUpAndClosed()
    {
        BossElevatorTimeline t = Default();
        double elapsed = t.DoneAt + 30.0;
        Assert.AreEqual(BossElevatorPhase.Done, t.PhaseAt(elapsed));
        Assert.AreEqual(12f, t.HeightAt(elapsed), Eps);
        Assert.AreEqual(BossElevatorDoorState.Closed, t.DoorPoseAt(elapsed, out _));
    }

    [Test]
    public void ZeroDurations_DoNotDivideByZero()
    {
        var s = new BossElevatorTimingSettings { height = 5f, doorOpenDuration = 0f, doorCloseDuration = 0f };
        var t = new BossElevatorTimeline(s, 0f, 0f);
        Assert.AreEqual(0f, t.DoneAt, Eps);
        Assert.AreEqual(BossElevatorPhase.Done, t.PhaseAt(0.0));
        Assert.AreEqual(5f, t.HeightAt(0.0), Eps);
    }
}
