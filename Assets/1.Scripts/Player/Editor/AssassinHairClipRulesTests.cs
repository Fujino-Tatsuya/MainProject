using NUnit.Framework;

// 할 일 47 — 어쌔신 머리카락 구동기 순수 규칙: 클립 → 보정 종류, 클립 안 초, trailing 방향을 다시 잡을 순간(전환·루프·되감기·리셋).
public sealed class AssassinHairClipRulesTests
{
    private const int RunState = 101;
    private const int AttackState = 202;

    [Test]
    public void DodgeUsesDiveHair()
    {
        Assert.That(AssassinHairClipRules.Select("Dodge_Combat_F_0"), Is.EqualTo(AssassinHairOverlayKind.Dive));
    }

    [TestCase("Run_Combat_Fast_Loop")]
    [TestCase("Combo_Attack_Wave_01")]
    [TestCase("Combo_Attack_02_04")]
    [TestCase("Idle")]
    [TestCase("Skill_02_Move_30pct")]
    public void ProfiledClipsUseActionHair(string clipName)
    {
        Assert.That(AssassinHairClipRules.Select(clipName), Is.EqualTo(AssassinHairOverlayKind.Action));
    }

    [TestCase("Death")]
    [TestCase("Skill_02_Move_35pct")]
    [TestCase("")]
    [TestCase(null)]
    public void OtherClipsUseSolverOnly(string clipName)
    {
        Assert.That(AssassinHairClipRules.Select(clipName), Is.EqualTo(AssassinHairOverlayKind.None));
    }

    [Test]
    public void LoopingClipTimeWrapsInsideOneCycle()
    {
        Assert.That(AssassinHairClipRules.ClipTime(2.25f, 2f, true), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(AssassinHairClipRules.LoopIndex(2.25f, true), Is.EqualTo(2));
    }

    [Test]
    public void NonLoopingClipTimeHoldsAtEnd()
    {
        Assert.That(AssassinHairClipRules.ClipTime(1.7f, 1.25f, false), Is.EqualTo(1.25f).Within(1e-5f));
        Assert.That(AssassinHairClipRules.LoopIndex(1.7f, false), Is.EqualTo(0));
    }

    [Test]
    public void ClipTimeIsPlaybackSpeedIndependent()
    {
        // 평타는 3.6배로 재생돼도 정규화 시간 × 원본 길이 = 클립 위 같은 위치.
        Assert.That(AssassinHairClipRules.ClipTime(0.4f, 1.25f, false), Is.EqualTo(0.5f).Within(1e-5f));
    }

    [Test]
    public void DegenerateInputsGiveZeroTime()
    {
        Assert.That(AssassinHairClipRules.ClipTime(0.5f, 0f, true), Is.EqualTo(0f));
        Assert.That(AssassinHairClipRules.ClipTime(-0.2f, 1f, false), Is.EqualTo(0f));
    }

    [Test]
    public void FirstObservationRelaunches()
    {
        var tracker = new AssassinHairClipTracker();

        Assert.That(tracker.Observe(RunState, "Run_Combat_Fast_Loop", 0.1f, true, 0), Is.True);
    }

    [Test]
    public void AdvancingSameStateKeepsDirection()
    {
        var tracker = new AssassinHairClipTracker();
        tracker.Observe(RunState, "Run_Combat_Fast_Loop", 0.1f, true, 0);

        Assert.That(tracker.Observe(RunState, "Run_Combat_Fast_Loop", 0.6f, true, 0), Is.False);
    }

    [Test]
    public void LoopCrossingRelaunches()
    {
        var tracker = new AssassinHairClipTracker();
        tracker.Observe(RunState, "Run_Combat_Fast_Loop", 0.95f, true, 0);

        Assert.That(tracker.Observe(RunState, "Run_Combat_Fast_Loop", 1.02f, true, 0), Is.True);
    }

    [Test]
    public void NonLoopingClipPastEndDoesNotRelaunch()
    {
        var tracker = new AssassinHairClipTracker();
        tracker.Observe(AttackState, "Combo_Attack_Wave_01", 0.95f, false, 0);

        Assert.That(tracker.Observe(AttackState, "Combo_Attack_Wave_01", 1.3f, false, 0), Is.False);
    }

    [Test]
    public void SameStateReentryRewindRelaunches()
    {
        // Any State 로 같은 평타 상태에 다시 들어가면 정규화 시간이 되감긴다.
        var tracker = new AssassinHairClipTracker();
        tracker.Observe(AttackState, "Combo_Attack_Wave_01", 0.8f, false, 0);

        Assert.That(tracker.Observe(AttackState, "Combo_Attack_Wave_01", 0.05f, false, 0), Is.True);
    }

    [Test]
    public void StateChangeRelaunches()
    {
        var tracker = new AssassinHairClipTracker();
        tracker.Observe(RunState, "Run_Combat_Fast_Loop", 0.5f, true, 0);

        Assert.That(tracker.Observe(AttackState, "Combo_Attack_Wave_01", 0.5f, false, 0), Is.True);
    }

    [Test]
    public void DominantClipChangeInsideStateRelaunches()
    {
        var tracker = new AssassinHairClipTracker();
        tracker.Observe(RunState, "Run_Combat_Fast_Start", 0.5f, false, 0);

        Assert.That(tracker.Observe(RunState, "Run_Combat_Fast_Loop", 0.55f, true, 0), Is.True);
    }

    [Test]
    public void SolverResetRelaunches()
    {
        // 순간이동·재활성으로 v13 이 리셋되면 위치·방향이 바뀌었을 수 있다.
        var tracker = new AssassinHairClipTracker();
        tracker.Observe(RunState, "Run_Combat_Fast_Loop", 0.3f, true, 4);

        Assert.That(tracker.Observe(RunState, "Run_Combat_Fast_Loop", 0.35f, true, 5), Is.True);
    }

    [Test]
    public void InvalidateMakesNextObservationFirst()
    {
        var tracker = new AssassinHairClipTracker();
        tracker.Observe(RunState, "Run_Combat_Fast_Loop", 0.3f, true, 0);
        tracker.Invalidate();

        Assert.That(tracker.Observe(RunState, "Run_Combat_Fast_Loop", 0.35f, true, 0), Is.True);
    }
}
