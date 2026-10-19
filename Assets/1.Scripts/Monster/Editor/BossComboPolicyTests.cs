using NUnit.Framework;

/// <summary>
/// 23호 콤보 판정(PLAN-boss-combo §3.2~3.3, 팀장 10-18·19).
/// 🔴 핵심 — 간격이면 기다리고, 사거리 밖이면 짧은 추격 유예 뒤 **버린다**. 개전 첫 타는 아직 안 나갔으니 기다리며 다가간다.
///    돌진 → 잡기(forceFire)는 사거리 무시하고 무조건 시전한다.
/// </summary>
public sealed class BossComboPolicyTests
{
    const float Interval = 0.3f;

    // ── 룰렛 후보 ──

    [Test]
    public void Candidate_AllGatesPass()
    {
        Assert.That(BossComboPolicy.IsCandidate(20f, 0, 0, 99f, 8f, true), Is.True);
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    public void Candidate_ZeroWeightNever(float weight)
    {
        Assert.That(BossComboPolicy.IsCandidate(weight, 0, 0, 99f, 8f, true), Is.False);
    }

    [Test]
    public void Candidate_PhaseGate()
    {
        Assert.That(BossComboPolicy.IsCandidate(20f, 0, 1, 99f, 8f, true), Is.False);
        Assert.That(BossComboPolicy.IsCandidate(20f, 1, 1, 99f, 8f, true), Is.True);
    }

    [Test]
    public void Candidate_ComboCooldownBoundary()
    {
        Assert.That(BossComboPolicy.IsCandidate(20f, 0, 0, 7.99f, 8f, true), Is.False);
        Assert.That(BossComboPolicy.IsCandidate(20f, 0, 0, 8f, 8f, true), Is.True);
    }

    // 첫 타가 거리창 밖이거나 행 쿨이면 콤보로 우회하지 않는다.
    [Test]
    public void Candidate_FirstStepGateFails()
    {
        Assert.That(BossComboPolicy.IsCandidate(20f, 0, 0, 99f, 8f, false), Is.False);
    }

    // ── 다음 타 ──

    static BossComboPolicy.StepGate Next(bool started = true, bool lockValid = true, float since = 1f,
                                         bool inWindow = true, bool forceFire = false,
                                         float chaseElapsed = 0f, float chaseGrace = 0f) =>
        BossComboPolicy.NextStep(started, lockValid, since, Interval, inWindow, forceFire, chaseElapsed, chaseGrace);

    [Test]
    public void Step_FiresWhenReady()
    {
        Assert.That(Next(), Is.EqualTo(BossComboPolicy.StepGate.Fire));
    }

    [Test]
    public void Step_WaitsForInterval()
    {
        Assert.That(Next(since: Interval - 0.01f), Is.EqualTo(BossComboPolicy.StepGate.Wait));
        Assert.That(Next(since: Interval), Is.EqualTo(BossComboPolicy.StepGate.Fire));
    }

    // 유예 0 이면 사거리 이탈 = 바로 버림.
    [Test]
    public void Step_OutOfWindow_NoGrace_Drops()
    {
        Assert.That(Next(inWindow: false), Is.EqualTo(BossComboPolicy.StepGate.Drop));
    }

    // 간격이 아직이면 거리와 무관하게 기다린다.
    [Test]
    public void Step_IntervalCheckedBeforeWindow()
    {
        Assert.That(Next(since: 0.1f, inWindow: false), Is.EqualTo(BossComboPolicy.StepGate.Wait));
    }

    // 짧은 추격 유예(팀장 10-19: 1초) — 유예 동안은 다가가며 기다리고, 넘기면 버린다.
    [Test]
    public void Step_OutOfWindow_ChaseGraceThenDrop()
    {
        Assert.That(Next(inWindow: false, chaseElapsed: 0f, chaseGrace: 1f), Is.EqualTo(BossComboPolicy.StepGate.Wait), "막 벗어남");
        Assert.That(Next(inWindow: false, chaseElapsed: 0.99f, chaseGrace: 1f), Is.EqualTo(BossComboPolicy.StepGate.Wait), "유예 직전");
        Assert.That(Next(inWindow: false, chaseElapsed: 1f, chaseGrace: 1f), Is.EqualTo(BossComboPolicy.StepGate.Drop), "유예 끝");
    }

    [Test]
    public void Step_ChaseCaughtUp_Fires()
    {
        Assert.That(Next(inWindow: true, chaseElapsed: 0.5f, chaseGrace: 1f), Is.EqualTo(BossComboPolicy.StepGate.Fire));
    }

    // 돌진 → 잡기(forceFire, 팀장 10-19) — 사거리 밖이어도 무조건 시전.
    [Test]
    public void Step_ForceFire_IgnoresWindow()
    {
        Assert.That(Next(inWindow: false, forceFire: true), Is.EqualTo(BossComboPolicy.StepGate.Fire));
    }

    // 그래도 간격·고정 대상은 지킨다.
    [Test]
    public void Step_ForceFire_StillWaitsIntervalAndNeedsLock()
    {
        Assert.That(Next(since: 0.1f, inWindow: false, forceFire: true), Is.EqualTo(BossComboPolicy.StepGate.Wait));
        Assert.That(Next(lockValid: false, forceFire: true), Is.EqualTo(BossComboPolicy.StepGate.Drop));
    }

    [Test]
    public void Step_LockLost_Drops()
    {
        Assert.That(Next(lockValid: false), Is.EqualTo(BossComboPolicy.StepGate.Drop));
    }

    // 개전 첫 타(아직 아무것도 안 나감) — 사거리 밖이면 버리지 않고 기다린다(다가간다). 고정도 아직 없다.
    [Test]
    public void Opening_FirstStepWaitsInsteadOfDrop()
    {
        Assert.That(Next(started: false, lockValid: false, inWindow: false), Is.EqualTo(BossComboPolicy.StepGate.Wait));
        Assert.That(Next(started: false, lockValid: false, inWindow: true), Is.EqualTo(BossComboPolicy.StepGate.Fire));
    }

    // ── 즉시 연결(PLAN-boss-combo-chain, 팀장 10-19) ──

    [Test]
    public void Chain_IntervalIsZero_OtherwiseNormal()
    {
        Assert.That(BossComboPolicy.StepInterval(true, Interval), Is.EqualTo(0f));
        Assert.That(BossComboPolicy.StepInterval(false, Interval), Is.EqualTo(Interval));
    }

    [Test]
    public void Chain_FiresImmediatelyEvenOutOfRange()
    {
        // 맞힌 순간 넘어와 간격 0 · 회피로 사거리 밖이어도 친다.
        bool inWindow = BossComboPolicy.IgnoresRange(chainOnHit: true, forceFire: false) || false;
        Assert.That(BossComboPolicy.NextStep(true, true, 0f, BossComboPolicy.StepInterval(true, Interval), inWindow),
                    Is.EqualTo(BossComboPolicy.StepGate.Fire));
    }

    [Test]
    public void NoChain_KeepsOldRules()
    {
        Assert.That(BossComboPolicy.IgnoresRange(false, false), Is.False);
        Assert.That(BossComboPolicy.IgnoresRange(false, true), Is.True, "forceFire(돌진 → 잡기)는 그대로 무조건");
        Assert.That(BossComboPolicy.NextStep(true, true, 0f, BossComboPolicy.StepInterval(false, Interval), true),
                    Is.EqualTo(BossComboPolicy.StepGate.Wait), "간격 0.3 은 기다린다");
    }

    [Test]
    public void Chain_StillDropsWhenLockLost()
    {
        // 즉시 연결이어도 고정 대상이 죽거나 어그로가 바뀌면 버린다(사거리만 무시한다).
        Assert.That(BossComboPolicy.NextStep(true, false, 0f, 0f, true), Is.EqualTo(BossComboPolicy.StepGate.Drop));
    }

    [TestCase(true, true, 1, 3, true)]    // 첫 타 뒤 — 다음 타 남음
    [TestCase(true, true, 2, 3, true)]
    [TestCase(true, true, 3, 3, false)]   // 마지막 타 — 넘길 곳 없음
    [TestCase(true, false, 1, 3, false)]  // 즉시 연결 끔
    [TestCase(false, true, 1, 3, false)]  // 콤보 없음
    [TestCase(true, true, 0, 3, false)]   // 아직 첫 타 전(개전 대기)
    public void ShouldChain_Cases(bool active, bool chain, int next, int count, bool expected)
    {
        Assert.That(BossComboPolicy.ShouldChain(active, chain, next, count), Is.EqualTo(expected));
    }

    [Test]
    public void HookOrUpper_OnlyLightHits()
    {
        Assert.That(BossComboPolicy.IsHookOrUpper(BossAttackId.LeftHook), Is.True);
        Assert.That(BossComboPolicy.IsHookOrUpper(BossAttackId.RightHook), Is.True);
        Assert.That(BossComboPolicy.IsHookOrUpper(BossAttackId.Upper), Is.True);
        Assert.That(BossComboPolicy.IsHookOrUpper(BossAttackId.Grab), Is.False);
        Assert.That(BossComboPolicy.IsHookOrUpper(BossAttackId.Dash), Is.False);
        Assert.That(BossComboPolicy.IsHookOrUpper(BossAttackId.Jump), Is.False);
    }
}
