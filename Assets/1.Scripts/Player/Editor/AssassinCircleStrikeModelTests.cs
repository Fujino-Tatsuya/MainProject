using NUnit.Framework;
using UnityEngine;

public sealed class AssassinCircleStrikeModelTests
{
    // ── 시작 조건(변신 중·종료 대기 아님) ──

    [Test]
    public void StartsOnlyWhileTransformedAndNotEndPending()
    {
        Assert.That(AssassinCircleStrikeRules.CanStart(true, false), Is.True);
        Assert.That(AssassinCircleStrikeRules.CanStart(true, true), Is.False);
        Assert.That(AssassinCircleStrikeRules.CanStart(false, false), Is.False);
    }

    // ── 원 판정(수평, AABB) ──

    [Test]
    public void TargetInsideCircleIsSelected()
    {
        var bounds = new Bounds(new Vector3(1f, 1f, 0f), new Vector3(0.6f, 2f, 0.6f));
        Assert.That(AssassinCircleStrikeRules.OverlapsCircle(Vector3.zero, 2f, bounds), Is.True);
    }

    [Test]
    public void TargetOutsideCircleIsNotSelected()
    {
        var bounds = new Bounds(new Vector3(3f, 1f, 0f), new Vector3(0.6f, 2f, 0.6f));
        Assert.That(AssassinCircleStrikeRules.OverlapsCircle(Vector3.zero, 2f, bounds), Is.False);
    }

    [Test]
    public void LargeColliderTouchingCircleEdgeIsSelected()
    {
        // 보스처럼 중심은 원 밖이어도 몸통 가장자리(x=1.9)가 원 안이면 맞는다.
        var bounds = new Bounds(new Vector3(4f, 1f, 0f), new Vector3(4.2f, 2f, 4.2f));
        Assert.That(AssassinCircleStrikeRules.OverlapsCircle(Vector3.zero, 2f, bounds), Is.True);
    }

    [Test]
    public void BoxCornerOutsideCircleIsNotSelected()
    {
        // 상자 판정의 모서리(1.5,1.5 → 거리 2.12)는 원 밖 — 사각 Overlap 이 잡아도 원 필터가 버린다.
        var bounds = new Bounds(new Vector3(1.8f, 1f, 1.8f), new Vector3(0.6f, 2f, 0.6f));
        Assert.That(AssassinCircleStrikeRules.OverlapsCircle(Vector3.zero, 2f, bounds), Is.False);
    }

    [Test]
    public void HeightIsIgnoredByCircleFilter()
    {
        var bounds = new Bounds(new Vector3(0.5f, 50f, 0.5f), Vector3.one);
        Assert.That(AssassinCircleStrikeRules.OverlapsCircle(Vector3.zero, 2f, bounds), Is.True);
    }

    [Test]
    public void ZeroRadiusSelectsNothing()
    {
        var bounds = new Bounds(Vector3.zero, Vector3.one);
        Assert.That(AssassinCircleStrikeRules.OverlapsCircle(Vector3.zero, 0f, bounds), Is.False);
    }

    // ── 5타 진행 ──

    [Test]
    public void FiveStrikesThenExtraHitEventsAreIgnored()
    {
        var sequence = new AssassinCircleStrikeSequence();
        sequence.Begin(5);

        for (int i = 0; i < 5; i++)
            Assert.That(sequence.TryBeginStrike(), Is.True);

        Assert.That(sequence.TryBeginStrike(), Is.False);
        Assert.That(sequence.StrikesDone, Is.EqualTo(5));
    }

    [Test]
    public void EarlyEndLeavesRemainingStrikesToFinish()
    {
        var sequence = new AssassinCircleStrikeSequence();
        sequence.Begin(5);
        sequence.TryBeginStrike();
        sequence.TryBeginStrike();

        Assert.That(sequence.RemainingStrikes, Is.EqualTo(3));

        int drained = 0;
        while (sequence.TryBeginStrike())
            drained++;

        Assert.That(drained, Is.EqualTo(3));
        Assert.That(sequence.StrikesDone, Is.EqualTo(5));
    }

    [Test]
    public void SameTargetIsHitOncePerStrike()
    {
        var sequence = new AssassinCircleStrikeSequence();
        var boss = new object();
        sequence.Begin(5);
        sequence.TryBeginStrike();

        Assert.That(sequence.TryRegisterTarget(boss), Is.True);
        Assert.That(sequence.TryRegisterTarget(boss), Is.False);
    }

    [Test]
    public void SameTargetIsHitAgainOnNextStrike()
    {
        var sequence = new AssassinCircleStrikeSequence();
        var boss = new object();
        sequence.Begin(5);
        sequence.TryBeginStrike();
        sequence.TryRegisterTarget(boss);

        sequence.TryBeginStrike();

        Assert.That(sequence.TryRegisterTarget(boss), Is.True);
    }

    [Test]
    public void NullTargetIsIgnored()
    {
        var sequence = new AssassinCircleStrikeSequence();
        sequence.Begin(5);
        sequence.TryBeginStrike();

        Assert.That(sequence.TryRegisterTarget(null), Is.False);
    }

    // ── 무적 구간·중단 ──

    [Test]
    public void InvulnerabilityHeldFromStartUntilStop()
    {
        var sequence = new AssassinCircleStrikeSequence();
        Assert.That(sequence.HoldsInvulnerability, Is.False);

        sequence.Begin(5);
        Assert.That(sequence.HoldsInvulnerability, Is.True);

        for (int i = 0; i < 5; i++)
            sequence.TryBeginStrike();
        Assert.That(sequence.HoldsInvulnerability, Is.True, "5타를 다 쳐도 동작 종료(End)까지 무적");

        sequence.Stop();
        Assert.That(sequence.HoldsInvulnerability, Is.False);
    }

    [Test]
    public void DeathCancelsRemainingStrikesAndInvulnerability()
    {
        var sequence = new AssassinCircleStrikeSequence();
        sequence.Begin(5);
        sequence.TryBeginStrike();
        sequence.TryBeginStrike();

        sequence.Stop();

        Assert.That(sequence.HoldsInvulnerability, Is.False);
        Assert.That(sequence.RemainingStrikes, Is.EqualTo(0));
        Assert.That(sequence.TryBeginStrike(), Is.False);
        Assert.That(sequence.TryRegisterTarget(new object()), Is.False);
    }

    [Test]
    public void NextUseStartsFresh()
    {
        var sequence = new AssassinCircleStrikeSequence();
        var boss = new object();
        sequence.Begin(5);
        sequence.TryBeginStrike();
        sequence.TryRegisterTarget(boss);
        sequence.Stop();

        sequence.Begin(5);

        Assert.That(sequence.StrikesDone, Is.EqualTo(0));
        Assert.That(sequence.TryBeginStrike(), Is.True);
        Assert.That(sequence.TryRegisterTarget(boss), Is.True);
    }

    // ── 종료 대기 연결(A7 모델) ──

    [Test]
    public void ReleaseDuringStrikeWaitsForSkillToFinish()
    {
        // 공격 중(FSM Skill) 해제 요청 → 종료 대기, 새 변신 E 시작 불가. 스킬이 끝나야 변신이 끝난다.
        var rules = AssassinStateRules.Default;
        var state = new AssassinStateSnapshot { rage = 40f };
        Assert.That(AssassinStateModel.TryBeginTransform(ref state, 0.0, rules, out _), Is.True);
        Assert.That(AssassinStateModel.TryRequestRelease(ref state, 2.5, rules), Is.True);

        bool endPending = AssassinStateModel.IsEndPending(state, 2.5);
        Assert.That(endPending, Is.True);
        Assert.That(AssassinCircleStrikeRules.CanStart(state.transformed, endPending), Is.False);
        Assert.That(AssassinStateModel.ShouldFinishTransform(state, 2.6, actionInProgress: true), Is.False);
        Assert.That(AssassinStateModel.ShouldFinishTransform(state, 3.8, actionInProgress: false), Is.True);
    }

    [Test]
    public void ExpiryDuringStrikeWaitsForSkillToFinish()
    {
        var rules = AssassinStateRules.Default;
        var state = new AssassinStateSnapshot { rage = 40f };
        AssassinStateModel.TryBeginTransform(ref state, 0.0, rules, out _); // 게이지 40 ÷ 10/초 = 4초

        Assert.That(AssassinStateModel.ShouldFinishTransform(state, 4.5, actionInProgress: true), Is.False);
        Assert.That(AssassinStateModel.ShouldFinishTransform(state, 5.2, actionInProgress: false), Is.True);
    }
}
