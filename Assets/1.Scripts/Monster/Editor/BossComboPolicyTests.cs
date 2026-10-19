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
}
