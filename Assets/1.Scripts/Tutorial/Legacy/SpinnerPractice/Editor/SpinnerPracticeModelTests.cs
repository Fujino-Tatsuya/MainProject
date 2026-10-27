using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 튜토리얼 스피너봇 연습 모드 순수 규칙 고정(할 일 15 S9 · handoff-tutorial-spinner.md) —
/// 모드 상태 기계 · 창 간격 9초 · 놓침/연속 놓침 · 스폰 반경 클램프.
/// </summary>
public sealed class SpinnerPracticeModelTests
{
    #region 모드
    // 휴면은 모델에 없다 — MonsterBase.SetDormantServer 하나(handoff-tutorial.md §2).

    [Test]
    public void Mode_StartsPractice_RunsLogic_AllowsHitReactions_ButLocksHealth()
    {
        var m = new SpinnerPracticeModeModel(2f);

        Assert.That(m.Mode, Is.EqualTo(SpinnerPracticeMode.Practice));
        Assert.That(m.SuspendsServerLogic, Is.False);
        Assert.That(m.BlocksHealthDamage, Is.True);
        Assert.That(m.OverridesAttack, Is.True);
        Assert.That(m.AllowsSpin, Is.True);
        Assert.That(m.ClampsPosition, Is.True);
    }

    [Test]
    public void Mode_HasNoDormantValue()
    {
        Assert.That(System.Enum.GetNames(typeof(SpinnerPracticeMode)),
            Is.EquivalentTo(new[] { "Practice", "Transition", "Real" }));
    }

    [Test]
    public void Transition_NotTicked_TimerHolds()
    {
        // 컴포넌트는 base 휴면 중 Tick 을 부르지 않는다 — 부르지 않는 동안 남은 시간이 그대로다.
        var m = new SpinnerPracticeModeModel(2f);
        m.EnterRealMode();
        Assert.That(m.Mode, Is.EqualTo(SpinnerPracticeMode.Transition));
        Assert.That(m.TransitionRemaining, Is.EqualTo(2f).Within(1e-5f));
    }

    [Test]
    public void RealMode_WaitsTransition_ThenUnlocksEverything()
    {
        var m = new SpinnerPracticeModeModel(2f);
        m.EnterRealMode();

        Assert.That(m.Mode, Is.EqualTo(SpinnerPracticeMode.Transition));
        Assert.That(m.SuspendsServerLogic, Is.True);
        Assert.That(m.BlocksHealthDamage, Is.True);
        Assert.That(m.AllowsSpin, Is.False);

        Assert.That(m.Tick(1.5f), Is.False);
        Assert.That(m.TransitionRemaining, Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(m.Tick(0.5f), Is.True);

        Assert.That(m.Mode, Is.EqualTo(SpinnerPracticeMode.Real));
        Assert.That(m.SuspendsServerLogic, Is.False);
        Assert.That(m.BlocksHealthDamage, Is.False);
        Assert.That(m.OverridesAttack, Is.False);
        Assert.That(m.ClampsPosition, Is.False);
        Assert.That(m.Tick(1f), Is.False);   // 진입 신호는 한 번만
    }

    [Test]
    public void RealMode_CalledAgain_DoesNotRestartTimer()
    {
        var m = new SpinnerPracticeModeModel(2f);
        m.EnterRealMode();
        m.Tick(1.5f);
        m.EnterRealMode();
        Assert.That(m.TransitionRemaining, Is.EqualTo(0.5f).Within(1e-5f));
    }

    [Test]
    public void Transition_SuspendsLogic_OnlyThere()
    {
        var m = new SpinnerPracticeModeModel(2f);
        Assert.That(m.SuspendsServerLogic, Is.False);
        m.EnterRealMode();
        Assert.That(m.SuspendsServerLogic, Is.True);
        m.Tick(2f);
        Assert.That(m.SuspendsServerLogic, Is.False);
    }

    [Test]
    public void RealMode_ZeroDelay_IsImmediate()
    {
        var m = new SpinnerPracticeModeModel(0f);
        m.EnterRealMode();
        Assert.That(m.Mode, Is.EqualTo(SpinnerPracticeMode.Real));
    }

    [Test]
    public void EnterPractice_FromReal_GoesBackToPractice()
    {
        var m = new SpinnerPracticeModeModel(0f);
        m.EnterRealMode();
        m.EnterPractice();
        Assert.That(m.Mode, Is.EqualTo(SpinnerPracticeMode.Practice));
    }
    #endregion

    #region 창 간격
    [Test]
    public void Schedule_FirstWindowImmediate_ThenEveryInterval()
    {
        var s = new SpinnerPracticeWindowSchedule(9f);

        Assert.That(s.TryBegin(3f), Is.True);
        Assert.That(s.TryBegin(3f), Is.False);
        Assert.That(s.TryBegin(11.99f), Is.False);
        Assert.That(s.TryBegin(12f), Is.True);
        Assert.That(s.NextAllowedAt, Is.EqualTo(21f).Within(1e-5f));
    }

    [Test]
    public void Schedule_Reset_AllowsImmediately()
    {
        var s = new SpinnerPracticeWindowSchedule(9f);
        s.TryBegin(0f);
        s.Reset();
        Assert.That(s.TryBegin(1f), Is.True);
    }

    [Test]
    public void Schedule_NegativeInterval_IsZero()
    {
        var s = new SpinnerPracticeWindowSchedule(-1f);
        Assert.That(s.Interval, Is.EqualTo(0f));
        Assert.That(s.TryBegin(1f), Is.True);
        Assert.That(s.TryBegin(1f), Is.True);
    }
    #endregion

    #region 놓침 집계
    [Test]
    public void Tracker_OpenThenExpire_IsMiss()
    {
        var t = new SpinnerPracticeWindowTracker();

        Assert.That(t.Observe(false), Is.EqualTo(SpinnerWindowEvent.None));
        Assert.That(t.Observe(true), Is.EqualTo(SpinnerWindowEvent.Opened));
        Assert.That(t.Observe(true), Is.EqualTo(SpinnerWindowEvent.None));
        Assert.That(t.Observe(false), Is.EqualTo(SpinnerWindowEvent.Missed));
        Assert.That(t.Observe(false), Is.EqualTo(SpinnerWindowEvent.None));
        Assert.That(t.ConsecutiveMisses, Is.EqualTo(1));
    }

    [Test]
    public void Tracker_SuccessClosesWindow_NoMissAfterwards_AndResetsStreak()
    {
        var t = new SpinnerPracticeWindowTracker();
        for (int i = 0; i < 3; i++) { t.Observe(true); t.Observe(false); }
        Assert.That(t.ConsecutiveMisses, Is.EqualTo(3));

        t.Observe(true);
        Assert.That(t.NotifySucceeded(), Is.EqualTo(SpinnerWindowEvent.Succeeded));
        Assert.That(t.Observe(false), Is.EqualTo(SpinnerWindowEvent.None));

        Assert.That(t.ConsecutiveMisses, Is.EqualTo(0));
        Assert.That(t.TotalMisses, Is.EqualTo(3));
        Assert.That(t.TotalSuccesses, Is.EqualTo(1));
        Assert.That(t.WindowsOpened, Is.EqualTo(4));
    }

    [Test]
    public void Tracker_FourMissesInARow_CountsFour()
    {
        var t = new SpinnerPracticeWindowTracker();
        SpinnerWindowEvent last = SpinnerWindowEvent.None;
        for (int i = 0; i < 4; i++) { t.Observe(true); last = t.Observe(false); }

        Assert.That(last, Is.EqualTo(SpinnerWindowEvent.Missed));
        Assert.That(t.ConsecutiveMisses, Is.EqualTo(4));
    }

    [Test]
    public void Tracker_SuccessBeforeOpenWasObserved_EmitsOpenedToo()
    {
        var t = new SpinnerPracticeWindowTracker();
        Assert.That(t.NotifySucceeded(), Is.EqualTo(SpinnerWindowEvent.Opened | SpinnerWindowEvent.Succeeded));
        Assert.That(t.Observe(false), Is.EqualTo(SpinnerWindowEvent.None));
        Assert.That(t.WindowsOpened, Is.EqualTo(1));
    }

    [Test]
    public void Tracker_ResetCounts_KeepsOpenWindow()
    {
        var t = new SpinnerPracticeWindowTracker();
        t.Observe(true); t.Observe(false);
        t.Observe(true);
        t.ResetCounts();

        Assert.That(t.ConsecutiveMisses, Is.EqualTo(0));
        Assert.That(t.Observe(false), Is.EqualTo(SpinnerWindowEvent.Missed));
        Assert.That(t.ConsecutiveMisses, Is.EqualTo(1));
    }
    #endregion

    #region 스폰 반경
    [Test]
    public void Clamp_InsideRadius_Unchanged()
    {
        var p = new Vector3(1f, 5f, 1f);
        Assert.That(SpinnerPracticeArea.ClampHorizontal(p, Vector3.zero, 3f), Is.EqualTo(p));
    }

    [Test]
    public void Clamp_Outside_PulledOntoCircle_KeepsHeight()
    {
        Vector3 c = SpinnerPracticeArea.ClampHorizontal(new Vector3(10f, 2f, 0f), new Vector3(0f, 0f, 1f), 4f);
        Assert.That(c.y, Is.EqualTo(2f));
        float horizontal = new Vector2(c.x, c.z - 1f).magnitude;
        Assert.That(horizontal, Is.EqualTo(4f).Within(1e-4f));
        Assert.That(c.x, Is.GreaterThan(0f));
    }

    [Test]
    public void Clamp_ZeroRadius_PinsToAnchor()
    {
        Vector3 c = SpinnerPracticeArea.ClampHorizontal(new Vector3(3f, 1f, 4f), new Vector3(1f, 0f, 1f), 0f);
        Assert.That(c.x, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(c.z, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(c.y, Is.EqualTo(1f));
    }

    [Test]
    public void SegmentEnd_Inside_Unchanged()
    {
        var to = new Vector3(2f, 0f, 0f);
        Assert.That(SpinnerPracticeArea.ClampSegmentEnd(Vector3.zero, to, Vector3.zero, 5f), Is.EqualTo(to));
    }

    [Test]
    public void SegmentEnd_CrossesCircle_StopsOnSameLine()
    {
        Vector3 end = SpinnerPracticeArea.ClampSegmentEnd(
            new Vector3(0f, 0f, 1f), new Vector3(16f, 0f, 1f), Vector3.zero, 5f);

        Assert.That(end.z, Is.EqualTo(1f).Within(1e-4f));                       // 방향 유지
        Assert.That(new Vector2(end.x, end.z).magnitude, Is.EqualTo(5f).Within(1e-3f));
        Assert.That(end.x, Is.GreaterThan(0f));
    }

    [Test]
    public void SegmentEnd_StartOutsideMovingAway_StaysPut()
    {
        var from = new Vector3(6f, 0f, 0f);
        Vector3 end = SpinnerPracticeArea.ClampSegmentEnd(from, new Vector3(12f, 0f, 0f), Vector3.zero, 5f);
        Assert.That(end, Is.EqualTo(from));
    }
    #endregion
}
