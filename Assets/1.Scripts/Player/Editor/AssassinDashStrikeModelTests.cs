using NUnit.Framework;

public sealed class AssassinDashStrikeModelTests
{
    // ── 경로 진행 ──

    [Test]
    public void TraveledGrowsWithElapsedTimeAtDashSpeed()
    {
        Assert.That(AssassinDashStrikeRules.TraveledAt(0.1f, 20f, 4f), Is.EqualTo(2f).Within(1e-5f));
    }

    [Test]
    public void TraveledStopsAtCap()
    {
        Assert.That(AssassinDashStrikeRules.TraveledAt(1f, 20f, 4f), Is.EqualTo(4f));
        Assert.That(AssassinDashStrikeRules.TraveledAt(1f, 20f, 1.3f), Is.EqualTo(1.3f));
    }

    [Test]
    public void TraveledIsZeroBeforeStartOrWithoutSpeed()
    {
        Assert.That(AssassinDashStrikeRules.TraveledAt(-0.1f, 20f, 4f), Is.EqualTo(0f));
        Assert.That(AssassinDashStrikeRules.TraveledAt(0.5f, 0f, 4f), Is.EqualTo(0f));
    }

    // ── 오너 보고 거리 검증 ──

    [Test]
    public void ReportedDistanceWithinRangeIsKept()
    {
        Assert.That(AssassinDashStrikeRules.TryValidateReportedDistance(1.7f, 4f, out float validated), Is.True);
        Assert.That(validated, Is.EqualTo(1.7f));
    }

    [Test]
    public void ReportedDistanceIsClampedToMaxDistance()
    {
        Assert.That(AssassinDashStrikeRules.TryValidateReportedDistance(9f, 4f, out float validated), Is.True);
        Assert.That(validated, Is.EqualTo(4f));
    }

    [Test]
    public void NegativeReportedDistanceBecomesZero()
    {
        Assert.That(AssassinDashStrikeRules.TryValidateReportedDistance(-2f, 4f, out float validated), Is.True);
        Assert.That(validated, Is.EqualTo(0f));
    }

    [Test]
    public void NonFiniteReportedDistanceIsRejected()
    {
        Assert.That(AssassinDashStrikeRules.TryValidateReportedDistance(float.NaN, 4f, out _), Is.False);
        Assert.That(AssassinDashStrikeRules.TryValidateReportedDistance(float.PositiveInfinity, 4f, out _), Is.False);
    }

    // ── 대상당 1회 ──

    [Test]
    public void SameTargetIsDamagedOncePerUse()
    {
        var ledger = new AssassinDashHitLedger();
        var boss = new object();
        ledger.Begin();

        Assert.That(ledger.TryRegisterTarget(boss), Is.True);
        Assert.That(ledger.TryRegisterTarget(boss), Is.False);
        Assert.That(ledger.DamagedCount, Is.EqualTo(1));
    }

    [Test]
    public void DifferentTargetsAreEachDamagedOnce()
    {
        var ledger = new AssassinDashHitLedger();
        ledger.Begin();

        Assert.That(ledger.TryRegisterTarget(new object()), Is.True);
        Assert.That(ledger.TryRegisterTarget(new object()), Is.True);
        Assert.That(ledger.DamagedCount, Is.EqualTo(2));
    }

    [Test]
    public void NextUseCanDamageSameTargetAgain()
    {
        var ledger = new AssassinDashHitLedger();
        var monster = new object();
        ledger.Begin();
        ledger.TryRegisterTarget(monster);

        ledger.Begin();

        Assert.That(ledger.TryRegisterTarget(monster), Is.True);
    }

    [Test]
    public void NullTargetIsIgnored()
    {
        var ledger = new AssassinDashHitLedger();
        ledger.Begin();

        Assert.That(ledger.TryRegisterTarget(null), Is.False);
    }

    // ── 변신 Q 쿨 차감 1회 ──

    [Test]
    public void CooldownRewardIsClaimedOncePerUse()
    {
        var ledger = new AssassinDashHitLedger();
        ledger.Begin();

        Assert.That(ledger.TryClaimCooldownReward(true), Is.True);
        Assert.That(ledger.TryClaimCooldownReward(true), Is.False);
    }

    [Test]
    public void CrateOnlyHitDoesNotConsumeCooldownReward()
    {
        var ledger = new AssassinDashHitLedger();
        ledger.Begin();

        Assert.That(ledger.TryClaimCooldownReward(false), Is.False);
        Assert.That(ledger.CooldownRewardClaimed, Is.False);
        Assert.That(ledger.TryClaimCooldownReward(true), Is.True);
    }

    [Test]
    public void NextUseCanClaimCooldownRewardAgain()
    {
        var ledger = new AssassinDashHitLedger();
        ledger.Begin();
        ledger.TryClaimCooldownReward(true);

        ledger.Begin();

        Assert.That(ledger.TryClaimCooldownReward(true), Is.True);
    }

    [Test]
    public void ReductionWithLedgerFloorsAtZero()
    {
        // §7.3 예: 남은 2.8초 → 1.3초, 하한 0 — 차감은 A2 장부가 하고 여기선 1회 규칙과 함께 확인한다.
        var cooldowns = new PlayerSkillCooldownLedger(1);
        cooldowns.Start(0, 0f, 3f);

        cooldowns.Reduce(0, 0.2f, 1.5f);
        Assert.That(cooldowns.GetRemaining(0, 0.2f), Is.EqualTo(1.3f).Within(1e-5f));

        cooldowns.Reduce(0, 0.2f, 1.5f);
        Assert.That(cooldowns.GetRemaining(0, 0.2f), Is.EqualTo(0f));
    }

    // ── 단계 시점 = 클립 이벤트(PLAN-anim-event-timing P2-3) ──

    // 일반·변신 Q 돌진 클립: Custom0(출발) → 경로 끝(출발 + 거리/속도) → End 순서이고, End 가 안전망(maxActiveDuration) 앞이다.
    // 실제 시간 = 클립 시각 ÷ 상태 speed.
    [TestCase("Assets/9.ScriptableObject/Player/Assassin/AssassinDashStrikeSkillData.asset")]
    [TestCase("Assets/9.ScriptableObject/Player/Assassin/AssassinTransformedDashStrikeSkillData.asset")]
    public void DashClip_StartsAtCustom0_EndsAfterPathAndBeforeSafetyNet(string dataPath)
    {
        AssassinDashStrikeSkillData data = UnityEditor.AssetDatabase.LoadAssetAtPath<AssassinDashStrikeSkillData>(dataPath);
        Assert.That(data, Is.Not.Null, dataPath);

        UnityEditor.Animations.AnimatorController controller =
            UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
                "Assets/4.Animations/Player/Assassin/AssassinAnimatorController.controller");
        UnityEditor.Animations.AnimatorState state = null;
        foreach (UnityEditor.Animations.ChildAnimatorState child in controller.layers[0].stateMachine.states)
        {
            if (child.state.name == data.AnimatorStateName)
                state = child.state;
        }

        Assert.That(state, Is.Not.Null, data.AnimatorStateName);
        UnityEngine.AnimationClip clip = state.motion as UnityEngine.AnimationClip;
        Assert.That(clip, Is.Not.Null);

        float start = SkillEventTime(clip, SkillAnimationEventType.Custom0) / state.speed;
        float end = SkillEventTime(clip, SkillAnimationEventType.End) / state.speed;

        Assert.That(start, Is.GreaterThanOrEqualTo(0f), "Custom0 없음");
        Assert.That(end, Is.GreaterThanOrEqualTo(start + data.DashDuration), "End 가 경로 끝보다 앞");
        Assert.That(end, Is.LessThan(data.MaxActiveDuration), "End 가 안전망 뒤");
    }

    private static float SkillEventTime(UnityEngine.AnimationClip clip, SkillAnimationEventType type)
    {
        foreach (UnityEngine.AnimationEvent e in UnityEditor.AnimationUtility.GetAnimationEvents(clip))
        {
            if (e.functionName == nameof(PlayerAnimationEventRelay.HandleSkillEvent) && e.intParameter == (int)type)
                return e.time;
        }

        return -1f;
    }
}
