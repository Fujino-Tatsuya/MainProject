using NUnit.Framework;
using UnityEngine;

// 할 일 47 — 어쌔신 머리카락 구동기 순수 규칙: 클립 → 보정 종류, 클립 안 초, trailing 방향을 다시 잡을 순간(전환·루프·되감기·리셋),
// 달리기 계열의 몸 방향 따라가기(10-22 — 루프 경계 튐).
public sealed class AssassinHairClipRulesTests
{
    private const int RunState = 101;
    private const int AttackState = 202;
    private const float Frame = 1f / 60f;
    private const float FollowSeconds = 0.08f;

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

    // ── 10-22 달리기 계열: 몸 방향 따라가기 ──

    [TestCase("Run_Combat_Fast_Start")]
    [TestCase("Run_Combat_Fast_Loop")]
    [TestCase("Run_Combat_Fast_Stop")]
    public void RunClipsFollowFacing(string clipName)
    {
        Assert.That(AssassinHairClipRules.FollowsFacing(clipName), Is.True);
    }

    [TestCase("Combo_Attack_02_04")]
    [TestCase("Combo_Attack_03_04")]
    [TestCase("Speed_Attack_Loop")]
    [TestCase("Dodge_Combat_F_0")]
    [TestCase("Idle_Combat")]
    [TestCase("")]
    [TestCase(null)]
    public void OtherClipsKeepLaunchDirection(string clipName)
    {
        Assert.That(AssassinHairClipRules.FollowsFacing(clipName), Is.False);
    }

    [Test]
    public void RestartedOnlyOnFirstObservationAndSolverReset()
    {
        var tracker = new AssassinHairClipTracker();
        tracker.Observe(RunState, "Run_Combat_Fast_Loop", 0.95f, true, 3);
        Assert.That(tracker.Restarted, Is.True, "처음 관측");

        tracker.Observe(RunState, "Run_Combat_Fast_Loop", 1.02f, true, 3);
        Assert.That(tracker.Restarted, Is.False, "루프 한 바퀴는 리셋이 아니다");

        tracker.Observe(RunState, "Run_Combat_Fast_Stop", 0.1f, false, 3);
        Assert.That(tracker.Restarted, Is.False, "클립 전환은 리셋이 아니다");

        tracker.Observe(RunState, "Run_Combat_Fast_Stop", 0.2f, false, 4);
        Assert.That(tracker.Restarted, Is.True, "v13 리셋");

        tracker.Invalidate();
        tracker.Observe(RunState, "Run_Combat_Fast_Stop", 0.3f, false, 4);
        Assert.That(tracker.Restarted, Is.True, "Invalidate 뒤 처음");
    }

    [Test]
    public void RunLoopCrossingDoesNotSnapToNewFacing()
    {
        // 지난 프레임 trailing 이 -Z 인데 몸이 이미 90° 돌았다(trailing 목표 -X). 루프 경계에서도 한 프레임에 조금만 돈다.
        Vector3 trailing = AssassinHairClipRules.NextTrailing(Vector3.back, Vector3.left,
            relaunch: true, restarted: false, following: true, wasFollowing: true, Frame, FollowSeconds);

        float turned = Vector3.Angle(Vector3.back, trailing);
        Assert.That(turned, Is.GreaterThan(0f));
        Assert.That(turned, Is.LessThan(20f), "한 프레임에 90° 를 한 번에 넘어가면 안 된다");
    }

    [Test]
    public void RunFollowsFacingBetweenRelaunches()
    {
        Vector3 trailing = Vector3.back;
        for (int i = 0; i < 60; i++)
            trailing = AssassinHairClipRules.NextTrailing(trailing, Vector3.left,
                relaunch: false, restarted: false, following: true, wasFollowing: true, Frame, FollowSeconds);

        Assert.That(Vector3.Angle(trailing, Vector3.left), Is.LessThan(0.5f), "1초면 몸 방향에 붙는다");
    }

    [Test]
    public void RunEntryFromOtherClipSnapsAsBefore()
    {
        Vector3 trailing = AssassinHairClipRules.NextTrailing(Vector3.back, Vector3.left,
            relaunch: true, restarted: false, following: true, wasFollowing: false, Frame, FollowSeconds);

        Assert.That(Vector3.Angle(trailing, Vector3.left), Is.LessThan(1e-3f));
    }

    [Test]
    public void SolverResetSnapsEvenInsideRun()
    {
        Vector3 trailing = AssassinHairClipRules.NextTrailing(Vector3.back, Vector3.left,
            relaunch: true, restarted: true, following: true, wasFollowing: true, Frame, FollowSeconds);

        Assert.That(Vector3.Angle(trailing, Vector3.left), Is.LessThan(1e-3f));
    }

    [Test]
    public void NonRunClipsKeepLaunchDirectionUntilRelaunch()
    {
        // Q·평타·회피는 예전 그대로 — relaunch 사이에는 몸이 돌아도 출발 방향을 유지하고, relaunch 에 다시 잡는다.
        Vector3 kept = AssassinHairClipRules.NextTrailing(Vector3.back, Vector3.left,
            relaunch: false, restarted: false, following: false, wasFollowing: false, Frame, FollowSeconds);
        Assert.That(kept, Is.EqualTo(Vector3.back));

        Vector3 relaunched = AssassinHairClipRules.NextTrailing(Vector3.back, Vector3.left,
            relaunch: true, restarted: false, following: false, wasFollowing: true, Frame, FollowSeconds);
        Assert.That(Vector3.Angle(relaunched, Vector3.left), Is.LessThan(1e-3f));
    }

    [Test]
    public void RunWithoutValidTrailingSnaps()
    {
        Vector3 trailing = AssassinHairClipRules.NextTrailing(Vector3.zero, Vector3.left,
            relaunch: true, restarted: false, following: true, wasFollowing: true, Frame, FollowSeconds);

        Assert.That(Vector3.Angle(trailing, Vector3.left), Is.LessThan(1e-3f));
    }

    [Test]
    public void FollowFacingIsFrameRateIndependent()
    {
        Vector3 at60 = Vector3.back;
        for (int i = 0; i < 6; i++)
            at60 = AssassinHairClipRules.FollowFacing(at60, Vector3.left, 1f / 60f, FollowSeconds);
        Vector3 at30 = Vector3.back;
        for (int i = 0; i < 3; i++)
            at30 = AssassinHairClipRules.FollowFacing(at30, Vector3.left, 1f / 30f, FollowSeconds);

        // Vector3.Angle 은 거의 같은 두 벡터에서 acos 정밀도 때문에 0.02° 안팎을 돌려줄 수 있어 거리로 비교한다(1e-4 ≈ 0.006°).
        Assert.That(Vector3.Distance(at60, at30), Is.LessThan(1e-4f));
    }

    [Test]
    public void FollowFacingStaysHorizontalWhenOpposite()
    {
        Vector3 result = AssassinHairClipRules.FollowFacing(Vector3.forward, Vector3.back, Frame, FollowSeconds);

        Assert.That(Mathf.Abs(result.y), Is.LessThan(1e-5f));
        Assert.That(result.magnitude, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(Vector3.Angle(Vector3.forward, result), Is.GreaterThan(0f));
    }

    [Test]
    public void FollowFacingDegenerateInputs()
    {
        Assert.That(AssassinHairClipRules.FollowFacing(Vector3.back, Vector3.left, Frame, 0f), Is.EqualTo(Vector3.left), "지연 0 = 바로 맞춤");
        Assert.That(AssassinHairClipRules.FollowFacing(Vector3.back, Vector3.left, 0f, FollowSeconds), Is.EqualTo(Vector3.back), "멈춘 시간 = 그대로");
        Assert.That(AssassinHairClipRules.FollowFacing(Vector3.back, Vector3.up, Frame, FollowSeconds), Is.EqualTo(Vector3.back), "수평 성분 없는 목표 = 그대로");
        Assert.That(AssassinHairClipRules.FollowFacing(Vector3.back, Vector3.left, float.NaN, FollowSeconds), Is.EqualTo(Vector3.back), "NaN 시간 = 그대로");
    }

    // ── 10-22 보정 전환 이어주기(관성 전환): Stop→Start 재진입·공격 취소→달리기 등에서 보정 모양이 한 프레임에 바뀌던 튐 ──

    private const int StopState = 303;
    private const float BlendSeconds = 0.2f;

    [Test]
    public void LoopOnlyMarksPureLoopCrossing()
    {
        var tracker = new AssassinHairClipTracker();
        tracker.Observe(RunState, "Run_Combat_Fast_Loop", 0.95f, true, 0);
        Assert.That(tracker.LoopOnly, Is.False, "처음 관측");

        Assert.That(tracker.Observe(RunState, "Run_Combat_Fast_Loop", 1.02f, true, 0), Is.True);
        Assert.That(tracker.LoopOnly, Is.True, "같은 클립 루프 한 바퀴");

        Assert.That(tracker.Observe(RunState, "Run_Combat_Fast_Loop", 1.10f, true, 0), Is.False);
        Assert.That(tracker.LoopOnly, Is.False, "relaunch 아님");

        Assert.That(tracker.Observe(StopState, "Run_Combat_Fast_Stop", 0.05f, false, 0), Is.True);
        Assert.That(tracker.LoopOnly, Is.False, "클립 전환");

        Assert.That(tracker.Observe(RunState, "Run_Combat_Fast_Start", 0f, false, 0), Is.True);
        Assert.That(tracker.LoopOnly, Is.False, "Stop→Start 재진입");

        tracker.Observe(RunState, "Run_Combat_Fast_Start", 0.5f, false, 0);
        Assert.That(tracker.Observe(RunState, "Run_Combat_Fast_Start", 0.1f, false, 0), Is.True);
        Assert.That(tracker.LoopOnly, Is.False, "되감기");

        Assert.That(tracker.Observe(RunState, "Run_Combat_Fast_Start", 0.2f, false, 1), Is.True);
        Assert.That(tracker.LoopOnly, Is.False, "v13 리셋");
    }

    [Test]
    public void CarryWeightFadesFromOneToZero()
    {
        Assert.That(AssassinHairClipRules.CarryWeight(0f, BlendSeconds), Is.EqualTo(1f));
        Assert.That(AssassinHairClipRules.CarryWeight(BlendSeconds * 0.5f, BlendSeconds), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(AssassinHairClipRules.CarryWeight(BlendSeconds, BlendSeconds), Is.EqualTo(0f));
        Assert.That(AssassinHairClipRules.CarryWeight(float.PositiveInfinity, BlendSeconds), Is.EqualTo(0f), "이어주기 없음");
        Assert.That(AssassinHairClipRules.CarryWeight(0f, 0f), Is.EqualTo(0f), "길이 0 = 끔");
        Assert.That(AssassinHairClipRules.CarryWeight(float.NaN, BlendSeconds), Is.EqualTo(0f), "NaN");

        float previous = 1f;
        for (int i = 1; i <= 12; i++)
        {
            float weight = AssassinHairClipRules.CarryWeight(i * Frame, BlendSeconds);
            Assert.That(weight, Is.LessThanOrEqualTo(previous), $"{i} 프레임");
            previous = weight;
        }
    }

    [Test]
    public void CarryStartsAtPreviousShapeAndEndsAtNewShape()
    {
        Quaternion previous = Quaternion.AngleAxis(40f, Vector3.right);
        Quaternion current = Quaternion.AngleAxis(-5f, Vector3.up);
        Quaternion offset = AssassinHairClipRules.CarryOffset(previous, current);

        Assert.That(Quaternion.Angle(AssassinHairClipRules.ApplyCarry(offset, 1f, current), previous), Is.LessThan(0.01f), "전환 순간 = 지난 모양");
        Assert.That(AssassinHairClipRules.ApplyCarry(offset, 0f, current), Is.EqualTo(current), "끝 = 새 모양 그대로");
    }

    [Test]
    public void CarryRemovesOneFrameJump()
    {
        // Stop→Start 재진입처럼 보정이 한 프레임에 40° → 0° 로 바뀌는 경우. 이어주면 프레임당 변화가 작고 0.2초 뒤 새 모양이다.
        Quaternion before = Quaternion.AngleAxis(40f, Vector3.right);
        Quaternion after = Quaternion.identity;
        Quaternion offset = AssassinHairClipRules.CarryOffset(before, after);
        Quaternion last = before;
        float maxStep = 0f;
        for (int i = 0; i <= 13; i++)
        {
            Quaternion applied = AssassinHairClipRules.ApplyCarry(offset, AssassinHairClipRules.CarryWeight(i * Frame, BlendSeconds), after);
            maxStep = Mathf.Max(maxStep, Quaternion.Angle(last, applied));
            last = applied;
        }

        Assert.That(maxStep, Is.LessThan(7f), "프레임당 최대 변화(이어주지 않으면 40°)");
        Assert.That(Quaternion.Angle(last, after), Is.LessThan(0.01f), "0.2초 뒤 새 모양");
    }

    [Test]
    public void SecondSwitchDuringCarryStartsFromShownShape()
    {
        // 이어주는 도중 또 바뀌면(Stop→Start→Stop 연타) 화면에 보이던 모양에서 다시 이어준다.
        Quaternion a = Quaternion.AngleAxis(40f, Vector3.right);
        Quaternion b = Quaternion.identity;
        Quaternion c = Quaternion.AngleAxis(-30f, Vector3.right);
        Quaternion shown = AssassinHairClipRules.ApplyCarry(AssassinHairClipRules.CarryOffset(a, b),
            AssassinHairClipRules.CarryWeight(5 * Frame, BlendSeconds), b);
        Quaternion offset = AssassinHairClipRules.CarryOffset(shown, c);

        Assert.That(Quaternion.Angle(AssassinHairClipRules.ApplyCarry(offset, 1f, c), shown), Is.LessThan(0.01f));
    }
}
