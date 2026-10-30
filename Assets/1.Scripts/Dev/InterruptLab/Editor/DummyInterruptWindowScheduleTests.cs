using NUnit.Framework;

/// <summary>
/// 할 일 139 단계 2 — 랩 허수아비 창 모드(<see cref="DummyInterruptWindowSchedule"/>)·순환 게이트 훅
/// (<see cref="TrainingDummyInterruptCycle.HoldClosed"/>)·예측기 관측 초기화(<see cref="InterruptWindowHistory.ClearKeepingLatest"/>).
/// 길이는 2 진수로 딱 떨어지는 값을 쓴다(0.5/1.5/0.25).
/// </summary>
public sealed class DummyInterruptWindowScheduleTests
{
    const float Eps = 1e-5f;

    static DummyInterruptWindowSchedule Periodic(float open = 0.5f, float closed = 1.5f) =>
        new DummyInterruptWindowSchedule(DummyInterruptWindowMode.Periodic, open, closed);

    // ─── 기본값·무기한 ─────────────────────────────────────────────────

    [Test]
    public void 기본은_무기한_열림_길이_0_6_1_5()
    {
        var s = new DummyInterruptWindowSchedule();
        Assert.AreEqual(DummyInterruptWindowMode.Unbounded, s.Mode);
        Assert.AreEqual(0.6f, s.OpenSeconds, Eps);
        Assert.AreEqual(1.5f, s.ClosedSeconds, Eps);
        for (int i = 0; i < 100; i++)
            Assert.IsTrue(s.Tick(64f, false));
    }

    // ─── 주기 경계 ─────────────────────────────────────────────────────

    [Test]
    public void 주기는_열림부터_시작()
    {
        var s = Periodic();
        Assert.IsTrue(s.IsOpen);
        Assert.IsTrue(s.Tick(0f, false));
    }

    [Test]
    public void 열림_길이_직전까지_열림_정확히_되면_닫힘()
    {
        var s = Periodic();
        Assert.IsTrue(s.Tick(0.25f, false));
        Assert.IsTrue(s.Tick(0.125f, false));
        Assert.IsTrue(s.Tick(0.0625f, false)); // 0.4375
        Assert.IsFalse(s.Tick(0.0625f, false)); // 정확히 0.5 = 닫힘(경계 = 바뀐 뒤)
        Assert.AreEqual(0f, s.PhaseElapsed, Eps);
    }

    [Test]
    public void 닫힘_길이_정확히_되면_다시_열림()
    {
        var s = Periodic();
        s.Tick(0.5f, false);
        Assert.IsFalse(s.Tick(1f, false));
        Assert.IsFalse(s.Tick(0.25f, false)); // 닫힘 1.25
        Assert.IsTrue(s.Tick(0.25f, false));  // 닫힘 1.5 = 열림
        Assert.AreEqual(0f, s.PhaseElapsed, Eps);
    }

    [Test]
    public void 경계를_넘긴_시간은_다음_단계로_이월()
    {
        var s = Periodic();
        Assert.IsFalse(s.Tick(0.75f, false));
        Assert.AreEqual(0.25f, s.PhaseElapsed, Eps);
    }

    [Test]
    public void 한_틱이_여러_주기를_넘겨도_위상은_맞다()
    {
        var s = Periodic(); // 주기 2
        Assert.IsTrue(s.Tick(2f * 1000f + 0.25f, false));
        Assert.AreEqual(0.25f, s.PhaseElapsed, Eps);
        Assert.IsFalse(s.Tick(2f * 7f + 0.5f, false)); // 0.75 → 닫힘 0.25
        Assert.AreEqual(0.25f, s.PhaseElapsed, Eps);
    }

    [Test]
    public void 음수_시간은_0으로_본다()
    {
        var s = Periodic();
        s.Tick(0.25f, false);
        Assert.IsTrue(s.Tick(-10f, false));
        Assert.AreEqual(0.25f, s.PhaseElapsed, Eps);
    }

    // ─── 길이 0 ────────────────────────────────────────────────────────

    [Test]
    public void 열림_0이면_항상_닫힘()
    {
        var s = Periodic(0f, 1.5f);
        Assert.IsFalse(s.IsOpen);
        for (int i = 0; i < 10; i++)
            Assert.IsFalse(s.Tick(0.25f, false));
    }

    [Test]
    public void 둘_다_0이면_항상_닫힘()
    {
        var s = Periodic(0f, 0f);
        Assert.IsFalse(s.Tick(0.25f, false));
        Assert.IsFalse(s.Tick(100f, false));
    }

    [Test]
    public void 닫힘_0이면_계속_열림()
    {
        var s = Periodic(0.5f, 0f);
        for (int i = 0; i < 10; i++)
            Assert.IsTrue(s.Tick(0.25f, false));
    }

    [Test]
    public void 길이_0에서_되돌리면_그_단계_처음부터()
    {
        var s = Periodic(0f, 1.5f);
        s.Tick(1f, false);
        s.SetDurations(0.5f, 1.5f);
        Assert.IsFalse(s.IsOpenPhase); // 닫힘 단계 처음부터
        Assert.IsFalse(s.Tick(1.25f, false));
        Assert.IsTrue(s.Tick(0.25f, false));
    }

    // ─── 도중 길이 변경 ────────────────────────────────────────────────

    [Test]
    public void 도중에_길게_바꾸면_경과_유지하고_새_길이로()
    {
        var s = Periodic();
        s.Tick(0.25f, false);
        s.SetDurations(1f, 1.5f);
        Assert.AreEqual(0.25f, s.PhaseElapsed, Eps);
        Assert.IsTrue(s.Tick(0.5f, false));   // 0.75 < 1
        Assert.IsFalse(s.Tick(0.25f, false)); // 1 = 닫힘
    }

    [Test]
    public void 도중에_이미_지난_길이로_줄이면_다음_틱에_바로_넘어가고_이월_없음()
    {
        var s = Periodic(1f, 1.5f);
        s.Tick(0.75f, false);
        s.SetDurations(0.5f, 1.5f);
        Assert.AreEqual(0.5f, s.PhaseElapsed, Eps); // 새 길이로 눌림
        Assert.IsFalse(s.Tick(0f, false));          // 0 초 틱이라도 바로 닫힘
        Assert.AreEqual(0f, s.PhaseElapsed, Eps);   // 넘친 0.25 는 이월하지 않음
    }

    [Test]
    public void 음수_길이는_0으로_본다()
    {
        var s = Periodic();
        s.SetDurations(-1f, -1f);
        Assert.AreEqual(0f, s.OpenSeconds, Eps);
        Assert.AreEqual(0f, s.ClosedSeconds, Eps);
        Assert.IsFalse(s.Tick(0.25f, false));
    }

    // ─── 모드 전환 = 위상 리셋 ─────────────────────────────────────────

    [Test]
    public void 주기로_바꾸면_열림_처음부터()
    {
        var s = new DummyInterruptWindowSchedule(DummyInterruptWindowMode.Unbounded, 0.5f, 1.5f);
        s.Tick(100f, false);
        s.SetMode(DummyInterruptWindowMode.Periodic);
        Assert.IsTrue(s.IsOpenPhase);
        Assert.AreEqual(0f, s.PhaseElapsed, Eps);
        Assert.IsTrue(s.Tick(0.25f, false));
        Assert.IsFalse(s.Tick(0.25f, false));
    }

    [Test]
    public void 닫힘_중_무기한으로_바꾸면_바로_열림_다시_주기면_열림부터()
    {
        var s = Periodic();
        s.Tick(1f, false); // 닫힘 0.5
        s.SetMode(DummyInterruptWindowMode.Unbounded);
        Assert.IsTrue(s.Tick(0f, false));
        s.SetMode(DummyInterruptWindowMode.Periodic);
        Assert.IsTrue(s.IsOpenPhase);
        Assert.AreEqual(0f, s.PhaseElapsed, Eps);
    }

    [Test]
    public void 같은_모드를_다시_고르면_위상_유지()
    {
        var s = Periodic();
        s.Tick(1f, false);
        s.SetMode(DummyInterruptWindowMode.Periodic);
        Assert.IsFalse(s.IsOpenPhase);
        Assert.AreEqual(0.5f, s.PhaseElapsed, Eps);
    }

    // ─── 회복 ──────────────────────────────────────────────────────────

    [Test]
    public void 회복_중엔_시계가_멈추고_끝나면_닫힘_처음부터()
    {
        var s = Periodic();
        s.Tick(0.25f, false);        // 열림 중 성공했다고 치자
        s.Tick(10f, true);           // 회복 중 — 시간 안 흐름
        Assert.IsTrue(s.IsOpenPhase);
        Assert.IsFalse(s.Tick(0.25f, false)); // 회복 끝 = 닫힘 처음(이 틱 시간은 버림)
        Assert.AreEqual(0f, s.PhaseElapsed, Eps);
        Assert.IsFalse(s.Tick(1.25f, false));
        Assert.IsTrue(s.Tick(0.25f, false));  // 닫힘 1.5 뒤 열림
    }

    [Test]
    public void 무기한은_회복이_끝나도_열림()
    {
        var s = new DummyInterruptWindowSchedule();
        s.Tick(1f, true);
        Assert.IsTrue(s.Tick(0f, false));
    }

    // ─── 순환 게이트 훅 ────────────────────────────────────────────────

    [Test]
    public void 순환_HoldClosed_는_인터럽트_가능일_때만_닫고_시간으로_풀리지_않는다()
    {
        var c = new TrainingDummyInterruptCycle(2f, 0f);
        Assert.IsTrue(c.HoldClosed());
        Assert.AreEqual(TrainingDummyInterruptState.Idle, c.State);
        Assert.IsTrue(c.IsHeldClosed);
        Assert.IsFalse(c.IsRecovering);
        Assert.IsFalse(c.TryInterrupt());
        Assert.IsFalse(c.Tick(1000f));
        Assert.AreEqual(TrainingDummyInterruptState.Idle, c.State);
        Assert.AreEqual(0f, c.Remaining, Eps);
        Assert.IsFalse(c.HoldClosed()); // 이미 닫힘
    }

    [Test]
    public void 순환_ReleaseHold_하면_다시_인터럽트_가능()
    {
        var c = new TrainingDummyInterruptCycle(2f, 0f);
        c.HoldClosed();
        Assert.IsTrue(c.ReleaseHold());
        Assert.IsTrue(c.IsInterruptible);
        Assert.IsTrue(c.TryInterrupt());
        Assert.IsFalse(c.ReleaseHold()); // 닫아 둔 게 아님 — 회복 중 상태 그대로
        Assert.AreEqual(TrainingDummyInterruptState.Vulnerable, c.State);
    }

    [Test]
    public void 순환_회복_중엔_HoldClosed_무시()
    {
        var c = new TrainingDummyInterruptCycle(2f, 1f);
        c.TryInterrupt();
        Assert.IsTrue(c.IsRecovering);
        Assert.IsFalse(c.HoldClosed());
        c.Tick(2f); // 취약 → 대기(시간 흐름)
        Assert.AreEqual(TrainingDummyInterruptState.Idle, c.State);
        Assert.IsTrue(c.IsRecovering);
        Assert.IsFalse(c.HoldClosed());
        c.Tick(1f);
        Assert.IsTrue(c.IsInterruptible);
    }

    [Test]
    public void 게이트_없으면_순환은_예전과_같다()
    {
        // 성공 → 취약 2 → 대기 1 → 무기한 인터럽트 가능. HoldClosed 를 안 부르면 IsHeldClosed 는 서지 않는다.
        var c = new TrainingDummyInterruptCycle(2f, 1f);
        Assert.IsTrue(c.TryInterrupt());
        c.Tick(2f);
        c.Tick(1f);
        Assert.IsTrue(c.IsInterruptible);
        Assert.IsFalse(c.IsHeldClosed);
        for (int i = 0; i < 100; i++)
            Assert.IsFalse(c.Tick(64f));
        Assert.IsTrue(c.IsInterruptible);
    }

    /// <summary>TrainingDummyInterrupt.Update 와 같은 순서로 게이트를 적용한다(Tick → 게이트 → 회복 중이 아니면 Hold/Release).</summary>
    static void ServerTick(TrainingDummyInterruptCycle c, DummyInterruptWindowSchedule s, float dt)
    {
        c.Tick(dt);
        bool open = s.Tick(dt, c.IsRecovering);
        if (!c.IsRecovering)
        {
            if (open) c.ReleaseHold();
            else c.HoldClosed();
        }
    }

    [Test]
    public void 순환과_주기_합치면_열림_닫힘_성공_회복_닫힘부터()
    {
        var c = new TrainingDummyInterruptCycle(0.5f, 0f);
        var s = Periodic();

        ServerTick(c, s, 0.25f);
        Assert.IsTrue(c.IsInterruptible);
        ServerTick(c, s, 0.25f);                 // 열림 0.5 = 닫힘
        Assert.IsTrue(c.IsHeldClosed);
        Assert.IsFalse(c.TryInterrupt());
        ServerTick(c, s, 1.5f);                  // 닫힘 1.5 = 열림
        Assert.IsTrue(c.IsInterruptible);

        Assert.IsTrue(c.TryInterrupt());         // 성공 → 취약 0.5
        ServerTick(c, s, 0.25f);
        Assert.AreEqual(TrainingDummyInterruptState.Vulnerable, c.State);
        ServerTick(c, s, 0.25f);                 // 회복 끝 → 같은 틱에 닫힘(열렸다 닫히는 상태가 남지 않음)
        Assert.IsTrue(c.IsHeldClosed);
        ServerTick(c, s, 1.25f);
        Assert.IsTrue(c.IsHeldClosed);
        ServerTick(c, s, 0.25f);                 // 닫힘 1.5 뒤 열림
        Assert.IsTrue(c.IsInterruptible);
    }

    // ─── 예측기 관측 초기화 ────────────────────────────────────────────

    static InterruptWindowHistory OpenedAndClosedTwice()
    {
        var h = new InterruptWindowHistory();
        h.Record(1.0, true);
        h.Record(1.5, false);
        h.Record(3.0, true);
        h.Record(3.5, false);
        h.Record(5.0, true); // 지금 열림
        return h;
    }

    [Test]
    public void 초기화하면_추정_길이를_모른다()
    {
        InterruptWindowHistory h = OpenedAndClosedTwice();
        Assert.IsTrue(h.TryGetTypicalOpenDuration(out double before));
        Assert.AreEqual(0.5, before, 1e-9);

        h.ClearKeepingLatest();
        Assert.IsFalse(h.TryGetTypicalOpenDuration(out _));
        h.Estimate(5.25, true, out float elapsed, out float expected);
        Assert.AreEqual(-1f, expected, Eps);
        Assert.AreEqual(0.25f, elapsed, Eps); // 지금 열림·경과는 남는다
    }

    [Test]
    public void 초기화_뒤_마지막_전이만_남는다()
    {
        InterruptWindowHistory h = OpenedAndClosedTwice();
        h.ClearKeepingLatest();
        Assert.AreEqual(1, h.Count);
        Assert.AreEqual(InterruptWindowState.Open, h.StateAt(5.0));
        Assert.AreEqual(InterruptWindowState.Unknown, h.StateAt(4.0)); // 지운 구간은 추측하지 않는다
    }

    [Test]
    public void 초기화_뒤_새_관측으로만_추정한다()
    {
        InterruptWindowHistory h = OpenedAndClosedTwice();
        h.ClearKeepingLatest();
        h.Record(5.25, false);
        h.Record(7.0, true);
        h.Record(8.0, false);
        Assert.IsTrue(h.TryGetTypicalOpenDuration(out double typical));
        Assert.AreEqual(0.625, typical, 1e-9); // (0.25 + 1.0) / 2 — 지우기 전 0.5 들은 빠짐
    }

    [Test]
    public void 링버퍼가_돈_뒤에도_초기화는_마지막_전이를_남긴다()
    {
        var h = new InterruptWindowHistory(4);
        for (int i = 0; i < 9; i++)
            h.Record(i, i % 2 == 0); // 마지막 = 8, 열림
        h.ClearKeepingLatest();
        Assert.AreEqual(1, h.Count);
        Assert.AreEqual(InterruptWindowState.Open, h.StateAt(8.5));
        h.Record(9.5, false);
        Assert.IsTrue(h.TryGetTypicalOpenDuration(out double typical));
        Assert.AreEqual(1.5, typical, 1e-9);
    }

    [Test]
    public void 빈_기록_초기화는_아무것도_안_한다()
    {
        var h = new InterruptWindowHistory();
        h.ClearKeepingLatest();
        Assert.AreEqual(0, h.Count);
        Assert.IsFalse(h.TryGetTypicalOpenDuration(out _));
    }

    [Test]
    public void 전역_초기화_진입점은_세션_없이도_안전()
    {
        Assert.DoesNotThrow(InterruptWindowReplicaTracker.ResetObservations);
    }
}
