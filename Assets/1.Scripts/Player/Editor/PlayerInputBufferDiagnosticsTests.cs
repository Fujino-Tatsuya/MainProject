using System.Collections.Generic;
using NUnit.Framework;

/// <summary>선입력 버퍼 진단(할 일 44 후속) — 이벤트 추적(즉시·버퍼 실행·만료·덮임·놓친 간격)과 집계(개수·중앙값·90%).</summary>
public sealed class PlayerInputBufferDiagnosticsTests
{
    private const float Hold = 0.2f;

    private readonly List<InputBufferDiagRecord> records = new List<InputBufferDiagRecord>();
    private PlayerInputBufferDiagTracker tracker;

    [SetUp]
    public void SetUp()
    {
        records.Clear();
        tracker = new PlayerInputBufferDiagTracker(records.Add, () => "행동 중");
    }

    private InputBufferDiagRecord Last(InputBufferDiagEvent evt) => records.FindLast(r => r.Event == evt);
    private int CountOf(InputBufferDiagEvent evt) => records.FindAll(r => r.Event == evt).Count;

    [Test]
    public void PressAndStartSameFrame_IsImmediateConsume_NoBuffered()
    {
        tracker.Press(PlayerBufferedInput.Attack, 10f, 100, Hold);
        tracker.Consumed(PlayerBufferedInput.None, 10f, 100, "행동 시작");

        Assert.That(CountOf(InputBufferDiagEvent.Buffered), Is.EqualTo(0));
        InputBufferDiagRecord consumed = Last(InputBufferDiagEvent.Consumed);
        Assert.That(consumed.Input, Is.EqualTo(PlayerBufferedInput.Attack));
        Assert.That(consumed.Immediate, Is.True);
    }

    [Test]
    public void PressWhileBusy_NextFrameBuffered_ThenConsumedWithElapsed()
    {
        tracker.StateChanged(false, 9.9f, 90, "→Attack");
        tracker.Press(PlayerBufferedInput.SkillMain, 10f, 100, Hold);
        tracker.BeginFrame(10.016f, 101);

        InputBufferDiagRecord buffered = Last(InputBufferDiagEvent.Buffered);
        Assert.That(buffered.Input, Is.EqualTo(PlayerBufferedInput.SkillMain));
        Assert.That(buffered.Detail, Is.EqualTo("행동 중"));

        tracker.StateChanged(true, 10.15f, 109, "→Idle");
        InputBufferDiagRecord actionable = Last(InputBufferDiagEvent.Actionable);
        Assert.That(actionable.MissedGap, Is.LessThan(0f), "놓친 입력 없음");
        Assert.That(actionable.PressToActionable, Is.EqualTo(0.15f).Within(1e-4f), "대기 입력의 press→actionable");

        tracker.Consumed(PlayerBufferedInput.None, 10.15f, 109, "행동 시작");
        InputBufferDiagRecord consumed = Last(InputBufferDiagEvent.Consumed);
        Assert.That(consumed.Immediate, Is.False);
        Assert.That(consumed.Elapsed, Is.EqualTo(0.15f).Within(1e-4f));
    }

    [Test]
    public void ExpiredWhileBusy_ThenActionable_ReportsMissedGapAndPressToActionable()
    {
        tracker.StateChanged(false, 9.9f, 90, "→Skill");
        tracker.Press(PlayerBufferedInput.Dash, 10f, 100, Hold);
        tracker.BeginFrame(10.25f, 115);

        Assert.That(Last(InputBufferDiagEvent.Expired).Input, Is.EqualTo(PlayerBufferedInput.Dash));
        Assert.That(tracker.Pending, Is.EqualTo(PlayerBufferedInput.None));

        tracker.StateChanged(true, 10.3f, 118, "→Idle");
        InputBufferDiagRecord actionable = Last(InputBufferDiagEvent.Actionable);
        Assert.That(actionable.Input, Is.EqualTo(PlayerBufferedInput.Dash));
        Assert.That(actionable.MissedGap, Is.EqualTo(0.1f).Within(1e-4f), "실행 가능 10.3 - 만료 10.2");
        Assert.That(actionable.PressToActionable, Is.EqualTo(0.3f).Within(1e-4f));
    }

    [Test]
    public void ExpiryIsDetectedBeforeActionable_SameRuleAsBuffer()
    {
        tracker.StateChanged(false, 9.9f, 90, "→Attack");
        tracker.Press(PlayerBufferedInput.Attack, 10f, 100, Hold);

        // 만료 뒤 첫 확인이 상태 전환이어도 만료가 먼저 잡혀 놓친 간격이 남는다.
        tracker.StateChanged(true, 10.21f, 113, "→Idle");
        Assert.That(CountOf(InputBufferDiagEvent.Expired), Is.EqualTo(1));
        Assert.That(Last(InputBufferDiagEvent.Actionable).MissedGap, Is.EqualTo(0.01f).Within(1e-4f));

        // 유지 시간과 같으면 아직 살아 있다(PlayerInputBuffer.Peek 와 같음).
        tracker.Press(PlayerBufferedInput.Attack, 0f, 200, Hold);
        tracker.BeginFrame(0.2f, 212);
        Assert.That(tracker.Pending, Is.EqualTo(PlayerBufferedInput.Attack));
    }

    [Test]
    public void ExpiredWhileActionable_IsNotCountedAsMissedGap()
    {
        tracker.Press(PlayerBufferedInput.SkillSub, 10f, 100, Hold);
        tracker.BeginFrame(10.3f, 118);
        tracker.StateChanged(false, 10.4f, 124, "→Attack");
        tracker.StateChanged(true, 11f, 160, "→Idle");

        Assert.That(Last(InputBufferDiagEvent.Actionable).MissedGap, Is.LessThan(0f));
    }

    [Test]
    public void NewPress_OverwritesPending()
    {
        tracker.Press(PlayerBufferedInput.Attack, 10f, 100, Hold);
        tracker.Press(PlayerBufferedInput.SkillUltimate, 10.05f, 103, Hold);

        InputBufferDiagRecord overwritten = Last(InputBufferDiagEvent.Overwritten);
        Assert.That(overwritten.Input, Is.EqualTo(PlayerBufferedInput.Attack));
        Assert.That(overwritten.Elapsed, Is.EqualTo(0.05f).Within(1e-4f));
        Assert.That(tracker.Pending, Is.EqualTo(PlayerBufferedInput.SkillUltimate));
    }

    [Test]
    public void DroppedAndConsumed_MatchKindOnly_OrAnyWhenNone()
    {
        tracker.Press(PlayerBufferedInput.SkillMain, 10f, 100, Hold);
        tracker.Dropped(PlayerBufferedInput.Attack, 10f, 100, "자원 부족");
        Assert.That(CountOf(InputBufferDiagEvent.Dropped), Is.EqualTo(0), "다른 종류는 무시");

        tracker.Dropped(PlayerBufferedInput.SkillMain, 10f, 100, "자원 부족");
        Assert.That(Last(InputBufferDiagEvent.Dropped).Detail, Is.EqualTo("자원 부족"));

        tracker.Press(PlayerBufferedInput.Dash, 11f, 160, Hold);
        tracker.Dropped(PlayerBufferedInput.None, 11.1f, 166, "입력 차단");
        Assert.That(Last(InputBufferDiagEvent.Dropped).Input, Is.EqualTo(PlayerBufferedInput.Dash));
        Assert.That(tracker.Pending, Is.EqualTo(PlayerBufferedInput.None));
    }

    [Test]
    public void BufferOff_PressIsIgnored()
    {
        tracker.Press(PlayerBufferedInput.Attack, 10f, 100, 0f);
        Assert.That(records, Is.Empty);
    }

    [Test]
    public void Percentile_LinearInterpolation()
    {
        Assert.That(PlayerInputBufferDiagStats.Percentile(new List<float> { 4f, 1f, 3f, 2f }, 0.5f), Is.EqualTo(2.5f).Within(1e-5f));
        var oneToTen = new List<float> { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f };
        Assert.That(PlayerInputBufferDiagStats.Percentile(oneToTen, 0.9f), Is.EqualTo(9.1f).Within(1e-4f));
        Assert.That(PlayerInputBufferDiagStats.Percentile(new List<float> { 7f }, 0.9f), Is.EqualTo(7f));
        Assert.That(float.IsNaN(PlayerInputBufferDiagStats.Percentile(new List<float>(), 0.5f)), Is.True);
    }

    [Test]
    public void Stats_CountsPerInput_AndSplitsMissedFromHeld()
    {
        var stats = new PlayerInputBufferDiagStats();
        var statsTracker = new PlayerInputBufferDiagTracker(r => stats.Add(r));

        statsTracker.Press(PlayerBufferedInput.Attack, 1f, 10, Hold);
        statsTracker.Consumed(PlayerBufferedInput.None, 1f, 10, "행동 시작"); // 즉시

        statsTracker.StateChanged(false, 1f, 10, "→Attack");
        statsTracker.Press(PlayerBufferedInput.Attack, 1.5f, 40, Hold);
        statsTracker.BeginFrame(1.8f, 58); // 만료
        statsTracker.StateChanged(true, 1.9f, 64, "→Idle"); // 놓친 간격 0.2, press→actionable 0.4

        statsTracker.StateChanged(false, 2f, 70, "→Attack");
        statsTracker.Press(PlayerBufferedInput.Attack, 2.4f, 94, Hold);
        statsTracker.StateChanged(true, 2.5f, 100, "→Idle"); // 대기 입력 press→actionable 0.1
        statsTracker.Consumed(PlayerBufferedInput.None, 2.5f, 100, "행동 시작");

        Assert.That(stats.Count(PlayerBufferedInput.Attack, InputBufferDiagEvent.Press), Is.EqualTo(3));
        Assert.That(stats.Count(PlayerBufferedInput.Attack, InputBufferDiagEvent.Consumed), Is.EqualTo(2));
        Assert.That(stats.ImmediateCount(PlayerBufferedInput.Attack), Is.EqualTo(1));
        Assert.That(stats.Count(PlayerBufferedInput.Attack, InputBufferDiagEvent.Expired), Is.EqualTo(1));
        Assert.That(stats.MissedGaps, Has.Count.EqualTo(1));
        Assert.That(stats.MissedGaps[0], Is.EqualTo(0.2f).Within(1e-4f));
        Assert.That(stats.MissedPressToActionable[0], Is.EqualTo(0.4f).Within(1e-4f));
        Assert.That(stats.HeldPressToActionable, Has.Count.EqualTo(1));
        Assert.That(stats.HeldPressToActionable[0], Is.EqualTo(0.1f).Within(1e-4f));
        StringAssert.Contains("LMB | 3 | 1 | 1 | 1 | 0 | 0", stats.BuildSummary(Hold, "x.csv"));
    }
}
