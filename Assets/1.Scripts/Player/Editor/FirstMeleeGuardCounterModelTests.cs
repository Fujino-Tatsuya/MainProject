using NUnit.Framework;
using UnityEngine;

// PLAN-paladin-counter-e C3 — 팔라딘 E 단계 상태기계: 방어 대기 → 첫 성공 → 반격(무시·넉백 무시) → 종료.
public sealed class FirstMeleeGuardCounterModelTests
{
    const float T0 = 10f;
    const float GuardDuration = 1f;   // FirstMeleeSubSkillData.guardDuration 기본값
    const float GuardAngle = 180f;    // guardAngle 기본값 — 좌우 90°
    const float HitDelay = 0.1f;      // counterHitDelay 자리값
    const float CounterDuration = 0.4f; // counterDuration 자리값

    static readonly Vector3 Front = new Vector3(0f, 0f, 5f);
    static readonly Vector3 Back = new Vector3(0f, 0f, -5f);

    static FirstMeleeGuardCounterModel Begun()
    {
        var model = new FirstMeleeGuardCounterModel();
        model.Begin(T0, Vector3.forward, GuardDuration, GuardAngle, HitDelay, CounterDuration);
        return model;
    }

    static GuardIncoming Hit(Vector3 attackerPos, float damage = 10f, bool hasAttacker = true, bool forcedUnblockable = false) =>
        new GuardIncoming(hasAttacker, attackerPos, Vector3.zero, damage, forcedUnblockable);

    // T0 + 0.2 에 정면 성공 → counterHitAt = T0 + 0.3, counterEndAt = T0 + 0.6.
    const float BlockAt = T0 + 0.2f;

    static FirstMeleeGuardCounterModel Countering()
    {
        var model = Begun();
        Assert.That(model.Evaluate(BlockAt, Hit(Front)), Is.EqualTo(GuardVerdict.BlockAndCounter));
        return model;
    }

    // ── 시작·방어 ──

    [Test]
    public void Begin_EntersGuarding()
    {
        var model = new FirstMeleeGuardCounterModel();
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Idle));

        model.Begin(T0, Vector3.forward, GuardDuration, GuardAngle, HitDelay, CounterDuration);
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Guarding));
        Assert.That(model.GuardForward, Is.EqualTo(Vector3.forward));
        Assert.That(model.HasPendingCounter, Is.False);
        Assert.That(model.BlocksKnockback(T0), Is.False);
    }

    [Test]
    public void FrontAttackDuringGuard_BlockAndCounter()
    {
        var model = Begun();
        Assert.That(model.Evaluate(BlockAt, Hit(Front)), Is.EqualTo(GuardVerdict.BlockAndCounter));
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Countering));
        Assert.That(model.CounterHitAt, Is.EqualTo(BlockAt + HitDelay).Within(1e-5f));
        Assert.That(model.CounterEndAt, Is.EqualTo(BlockAt + CounterDuration).Within(1e-5f));
    }

    [Test]
    public void BackAttackDuringGuard_Pass_StaysGuarding()
    {
        var model = Begun();
        Assert.That(model.Evaluate(BlockAt, Hit(Back)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Guarding));
        Assert.That(model.HasPendingCounter, Is.False);
    }

    // ── 방어 불가 ──

    [Test]
    public void NoAttacker_Pass()
    {
        var model = Begun();
        Assert.That(model.Evaluate(BlockAt, Hit(Front, hasAttacker: false)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Guarding));
    }

    [Test]
    public void ForcedUnblockable_Pass()
    {
        var model = Begun();
        Assert.That(model.Evaluate(BlockAt, Hit(Front, forcedUnblockable: true)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Guarding));
    }

    [Test]
    public void ZeroDamage_Pass_NoTrigger()
    {
        var model = Begun();
        Assert.That(model.Evaluate(BlockAt, Hit(Front, damage: 0f)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Evaluate(BlockAt, Hit(Front, damage: -1f)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Guarding));
        Assert.That(model.HasPendingCounter, Is.False);
    }

    // ── 대기 만료 ──

    [Test]
    public void AttackAtExactGuardEnd_Pass()
    {
        // 방어는 now < guardEnd — 경계 시각은 이미 대기 밖이다(Tick 전이라도).
        var model = Begun();
        Assert.That(model.Evaluate(T0 + GuardDuration, Hit(Front)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.HasPendingCounter, Is.False);
    }

    [Test]
    public void Tick_GuardExpires_EndedGuardExpired_NoCounter()
    {
        var model = Begun();
        GuardTickResult before = model.Tick(T0 + GuardDuration - 0.01f);
        Assert.That(before.HasEnded, Is.False);
        Assert.That(before.CounterHitDue, Is.False);

        GuardTickResult at = model.Tick(T0 + GuardDuration);
        Assert.That(at.Ended, Is.EqualTo(GuardEndReason.GuardExpired));
        Assert.That(at.CounterHitDue, Is.False);
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Finished));
    }

    // ── 반격 중 ──

    [Test]
    public void Countering_BackAttack_Ignored()
    {
        var model = Countering();
        Assert.That(model.Evaluate(BlockAt + 0.05f, Hit(Back)), Is.EqualTo(GuardVerdict.Ignore));
    }

    [Test]
    public void Countering_Unblockable_Pass()
    {
        var model = Countering();
        float now = BlockAt + 0.05f;
        Assert.That(model.Evaluate(now, Hit(Front, hasAttacker: false)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Evaluate(now, Hit(Front, forcedUnblockable: true)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Evaluate(now, Hit(Front, damage: 0f)), Is.EqualTo(GuardVerdict.Pass));
    }

    [Test]
    public void Countering_SecondFrontHit_Ignored_CounterNotRestarted()
    {
        var model = Countering();
        float hitAt = model.CounterHitAt;
        float endAt = model.CounterEndAt;

        Assert.That(model.Evaluate(BlockAt + 0.15f, Hit(Front)), Is.EqualTo(GuardVerdict.Ignore));
        Assert.That(model.CounterHitAt, Is.EqualTo(hitAt));
        Assert.That(model.CounterEndAt, Is.EqualTo(endAt));

        // 반격 판정은 그래도 한 번뿐.
        Assert.That(model.Tick(hitAt).CounterHitDue, Is.True);
        model.MarkCounterFired();
        Assert.That(model.Evaluate(hitAt + 0.05f, Hit(Front)), Is.EqualTo(GuardVerdict.Ignore));
        Assert.That(model.Tick(hitAt + 0.05f).CounterHitDue, Is.False);
        Assert.That(model.HasPendingCounter, Is.False);
    }

    [Test]
    public void Countering_AttackAtExactCounterEnd_Pass()
    {
        // 무시는 now < counterEndAt — 종료 처리(Tick) 전이라도 경계 시각부터는 정상 적용.
        var model = Countering();
        Assert.That(model.Evaluate(model.CounterEndAt, Hit(Back)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Evaluate(model.CounterEndAt, Hit(Front)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Countering));
    }

    // ── 반격 시점 ──

    [Test]
    public void Tick_CounterHitDue_ExactlyOnce_AfterDelay()
    {
        var model = Countering();
        Assert.That(model.Tick(BlockAt + 0.05f).CounterHitDue, Is.False);

        GuardTickResult due = model.Tick(model.CounterHitAt);
        Assert.That(due.CounterHitDue, Is.True);
        Assert.That(due.HasEnded, Is.False);

        // MarkCounterFired 를 아직 안 불러도 다시 오지 않는다.
        Assert.That(model.Tick(model.CounterHitAt + 0.01f).CounterHitDue, Is.False);
        model.MarkCounterFired();
        Assert.That(model.Tick(model.CounterHitAt + 0.02f).CounterHitDue, Is.False);
    }

    [Test]
    public void Tick_CounterFinished_AfterDuration()
    {
        var model = Countering();
        Assert.That(model.Tick(model.CounterHitAt).CounterHitDue, Is.True);
        model.MarkCounterFired();

        Assert.That(model.Tick(model.CounterEndAt - 0.01f).HasEnded, Is.False);

        GuardTickResult end = model.Tick(model.CounterEndAt);
        Assert.That(end.Ended, Is.EqualTo(GuardEndReason.CounterFinished));
        Assert.That(end.CounterHitDue, Is.False);
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Finished));
    }

    [Test]
    public void Tick_LargeFrameStep_CounterHitDueAndFinishedSameTick_BothTrue()
    {
        var model = Countering();
        GuardTickResult r = model.Tick(model.CounterEndAt + 1f);
        Assert.That(r.CounterHitDue, Is.True);
        Assert.That(r.Ended, Is.EqualTo(GuardEndReason.CounterFinished));
        Assert.That(model.HasPendingCounter, Is.True); // 호출측이 반격을 먼저 실행해야 한다
    }

    // ── 반격 보장 ──

    [Test]
    public void Begin_ClampsHitDelayToDuration()
    {
        var model = new FirstMeleeGuardCounterModel();
        model.Begin(T0, Vector3.forward, GuardDuration, GuardAngle, counterHitDelay: 0.9f, counterDuration: 0.4f);
        Assert.That(model.CounterHitDelay, Is.EqualTo(0.4f).Within(1e-6f));

        model.Begin(T0, Vector3.forward, GuardDuration, GuardAngle, counterHitDelay: -0.5f, counterDuration: 0.4f);
        Assert.That(model.CounterHitDelay, Is.EqualTo(0f));

        // Clamp 된 지연 — 반격 판정이 종료보다 늦게 잡히지 않는다.
        model.Begin(T0, Vector3.forward, GuardDuration, GuardAngle, counterHitDelay: 0.9f, counterDuration: 0.4f);
        Assert.That(model.Evaluate(BlockAt, Hit(Front)), Is.EqualTo(GuardVerdict.BlockAndCounter));
        Assert.That(model.CounterHitAt, Is.LessThanOrEqualTo(model.CounterEndAt));
    }

    [Test]
    public void HasPendingCounter_TrueAfterBlock_FalseAfterMarkFired()
    {
        var model = Begun();
        Assert.That(model.HasPendingCounter, Is.False);

        model.Evaluate(BlockAt, Hit(Front));
        Assert.That(model.HasPendingCounter, Is.True);

        model.MarkCounterFired();
        Assert.That(model.HasPendingCounter, Is.False);
    }

    [Test]
    public void HasPendingCounter_FalseWhenGuardExpired()
    {
        var model = Begun();
        Assert.That(model.Tick(T0 + GuardDuration).Ended, Is.EqualTo(GuardEndReason.GuardExpired));
        Assert.That(model.HasPendingCounter, Is.False);
    }

    // ── 넉백 ──

    [Test]
    public void BlocksKnockback_OnlyWhileCountering()
    {
        var model = Begun();
        Assert.That(model.BlocksKnockback(BlockAt), Is.False); // 방어 대기 중에는 넉백 정상

        model.Evaluate(BlockAt, Hit(Front));
        Assert.That(model.BlocksKnockback(BlockAt), Is.True);
        Assert.That(model.BlocksKnockback(model.CounterEndAt - 0.01f), Is.True);

        model.Tick(model.CounterEndAt);
        Assert.That(model.BlocksKnockback(model.CounterEndAt), Is.False);
    }

    [Test]
    public void BlocksKnockback_FalseAtCounterEnd()
    {
        // 종료 처리(Tick) 전이라도 counterEndAt 부터는 통과.
        var model = Countering();
        Assert.That(model.BlocksKnockback(model.CounterEndAt), Is.False);
    }

    // ── 종료 후 ──

    [Test]
    public void Finished_AllPass()
    {
        var model = Countering();
        model.Tick(model.CounterEndAt);
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Finished));

        float later = model.CounterEndAt + 0.1f;
        Assert.That(model.Evaluate(later, Hit(Front)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Evaluate(later, Hit(Back)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.BlocksKnockback(later), Is.False);
        Assert.That(model.Tick(later).HasEnded, Is.False);
    }

    [Test]
    public void Reset_ReturnsIdle_AllPass()
    {
        var model = Countering();
        model.Reset();

        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Idle));
        Assert.That(model.HasPendingCounter, Is.False);
        Assert.That(model.Evaluate(BlockAt, Hit(Front)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.BlocksKnockback(BlockAt), Is.False);
        GuardTickResult r = model.Tick(BlockAt + 5f);
        Assert.That(r.CounterHitDue, Is.False);
        Assert.That(r.HasEnded, Is.False);
    }
}
