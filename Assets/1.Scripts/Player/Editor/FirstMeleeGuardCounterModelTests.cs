using NUnit.Framework;
using UnityEngine;

// PLAN-paladin-counter-e C3 · PLAN-anim-event-timing P1-③ — 팔라딘 E 단계 상태기계(애니메이션 이벤트 구동):
// 방어 대기(Custom0 창 열림 ~ Custom1 닫힘) → 해제(해제 클립 End 종료), 또는 첫 성공 → 반격(Hit 판정 · End 종료, 무시·넉백 무시) → 종료.
public sealed class FirstMeleeGuardCounterModelTests
{
    const float GuardAngle = 180f;    // guardAngle 기본값 — 좌우 90°

    static readonly Vector3 Front = new Vector3(0f, 0f, 5f);
    static readonly Vector3 Back = new Vector3(0f, 0f, -5f);

    // Begin 만 — 방어 창은 아직 닫혀 있다(Custom0 전).
    static FirstMeleeGuardCounterModel BegunClosed()
    {
        var model = new FirstMeleeGuardCounterModel();
        model.Begin(Vector3.forward, GuardAngle);
        return model;
    }

    // Begin + Custom0 — 방어 창 열림.
    static FirstMeleeGuardCounterModel Begun()
    {
        var model = BegunClosed();
        model.OnGuardWindowOpened();
        return model;
    }

    static GuardIncoming Hit(Vector3 attackerPos, float damage = 10f, bool hasAttacker = true, bool forcedUnblockable = false) =>
        new GuardIncoming(hasAttacker, attackerPos, Vector3.zero, damage, forcedUnblockable);

    static FirstMeleeGuardCounterModel Countering()
    {
        var model = Begun();
        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.BlockAndCounter));
        return model;
    }

    // ── 시작·방어 ──

    [Test]
    public void Begin_EntersGuarding_WindowClosed()
    {
        var model = new FirstMeleeGuardCounterModel();
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Idle));

        model.Begin(Vector3.forward, GuardAngle);
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Guarding));
        Assert.That(model.IsGuardWindowOpen, Is.False);
        Assert.That(model.GuardForward, Is.EqualTo(Vector3.forward));
        Assert.That(model.HasPendingCounter, Is.False);
        Assert.That(model.BlocksKnockback, Is.False);

        model.OnGuardWindowOpened();
        Assert.That(model.IsGuardWindowOpen, Is.True);
    }

    [Test]
    public void AttackBeforeWindowOpened_Pass_StaysGuarding()
    {
        // Custom0 전 피격은 정면이어도 Pass — 창이 열린 뒤에는 막는다.
        var model = BegunClosed();
        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Guarding));
        Assert.That(model.HasPendingCounter, Is.False);

        model.OnGuardWindowOpened();
        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.BlockAndCounter));
    }

    [Test]
    public void FrontAttackDuringOpenWindow_BlockAndCounter()
    {
        var model = Begun();
        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.BlockAndCounter));
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Countering));
        Assert.That(model.Blocked, Is.True);
        Assert.That(model.IsGuardWindowOpen, Is.False);
    }

    [Test]
    public void BackAttackDuringGuard_Pass_StaysGuarding()
    {
        var model = Begun();
        Assert.That(model.Evaluate(Hit(Back)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Guarding));
        Assert.That(model.HasPendingCounter, Is.False);
    }

    // ── 방어 불가 ──

    [Test]
    public void NoAttacker_Pass()
    {
        var model = Begun();
        Assert.That(model.Evaluate(Hit(Front, hasAttacker: false)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Guarding));
    }

    [Test]
    public void ForcedUnblockable_Pass()
    {
        var model = Begun();
        Assert.That(model.Evaluate(Hit(Front, forcedUnblockable: true)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Guarding));
    }

    [Test]
    public void ZeroDamage_Pass_NoTrigger()
    {
        var model = Begun();
        Assert.That(model.Evaluate(Hit(Front, damage: 0f)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Evaluate(Hit(Front, damage: -1f)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Guarding));
        Assert.That(model.HasPendingCounter, Is.False);
    }

    // ── 방어 창 닫힘(만료) ──

    [Test]
    public void AttackAfterWindowClosed_Pass()
    {
        // Custom1 뒤에는 정면이어도 Pass — 종료 처리 순서에 기대지 않는다.
        var model = Begun();
        model.OnGuardWindowClosed();
        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.HasPendingCounter, Is.False);
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Releasing), "해제 중 피격은 반격으로 넘어가지 않는다");
    }

    [Test]
    public void GuardWindowClosed_OnlyClosesWindow_Releasing()
    {
        // 할 일 91 A5 — Custom1 은 창만 닫는다. E 는 해제 모션 끝(End)까지 이어진다.
        var model = Begun();
        model.OnGuardWindowClosed();
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Releasing));
        Assert.That(model.IsGuardWindowOpen, Is.False);
        Assert.That(model.HasPendingCounter, Is.False);
        Assert.That(model.BlocksKnockback, Is.False);

        // 두 번째 Custom1·늦은 Custom0 은 무시 — 창이 다시 열리지 않는다.
        model.OnGuardWindowClosed();
        model.OnGuardWindowOpened();
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Releasing));
        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.Pass));
    }

    [Test]
    public void ReleaseEnd_EndedGuardExpired_NoCounter()
    {
        var model = Begun();
        model.OnGuardWindowClosed();

        GuardEventResult end = model.OnEnd();
        Assert.That(end.Ended, Is.EqualTo(GuardEndReason.GuardExpired));
        Assert.That(end.CounterHitDue, Is.False);
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Finished));

        // 두 번째 End 는 무시.
        Assert.That(model.OnEnd().HasEnded, Is.False);
    }

    [Test]
    public void EndWhileGuarding_Ignored()
    {
        // Custom1 없이 온 End 는 단계가 안 맞아 무시한다(기존 규칙) — 안전망이 끝낸다.
        var model = Begun();
        Assert.That(model.OnEnd().HasEnded, Is.False);
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Guarding));
        Assert.That(model.IsGuardWindowOpen, Is.True);
    }

    [Test]
    public void GuardWindowClosed_WithoutOpen_StillReleasesAndEnds()
    {
        // Custom0 이 빠진 클립이어도 Custom1 → End 로 E 가 끝난다(막지는 못한 채).
        var model = BegunClosed();
        model.OnGuardWindowClosed();
        Assert.That(model.OnEnd().Ended, Is.EqualTo(GuardEndReason.GuardExpired));
    }

    // ── 반격 중 ──

    [Test]
    public void Countering_BackAttack_Ignored()
    {
        var model = Countering();
        Assert.That(model.Evaluate(Hit(Back)), Is.EqualTo(GuardVerdict.Ignore));
    }

    [Test]
    public void Countering_Unblockable_Pass()
    {
        var model = Countering();
        Assert.That(model.Evaluate(Hit(Front, hasAttacker: false)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Evaluate(Hit(Front, forcedUnblockable: true)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Evaluate(Hit(Front, damage: 0f)), Is.EqualTo(GuardVerdict.Pass));
    }

    [Test]
    public void Countering_SecondFrontHit_Ignored_CounterNotRestarted()
    {
        var model = Countering();

        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.Ignore));
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Countering));

        // 반격 판정은 그래도 한 번뿐.
        Assert.That(model.OnCounterHit(), Is.True);
        model.MarkCounterFired();
        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.Ignore));
        Assert.That(model.OnCounterHit(), Is.False);
        Assert.That(model.HasPendingCounter, Is.False);
    }

    [Test]
    public void Countering_AttackAfterCounterEnd_Pass()
    {
        // 무시는 End 이벤트까지 — 그 뒤 피격은 정상 적용.
        var model = Countering();
        model.OnEnd();
        Assert.That(model.Evaluate(Hit(Back)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.Pass));
    }

    [Test]
    public void Countering_LateGuardWindowClosed_Ignored()
    {
        // 반격 상태로 CrossFade 하는 동안 해제 클립의 Custom1 이 뒤늦게 와도 반격 단계가 유지된다.
        var model = Countering();
        model.OnGuardWindowClosed();
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Countering));
        Assert.That(model.BlocksKnockback, Is.True);
        Assert.That(model.HasPendingCounter, Is.True);
    }

    [Test]
    public void Countering_LateGuardWindowOpened_DoesNotReopen()
    {
        var model = Countering();
        model.OnGuardWindowOpened();
        Assert.That(model.IsGuardWindowOpen, Is.False);
        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.Ignore));
    }

    // ── 반격 시점 ──

    [Test]
    public void GuardingOrIdle_CounterEvents_Ignored()
    {
        // Hit 은 Countering 이 아니면, End 는 Releasing·Countering 이 아니면 아무 일도 하지 않는다.
        var model = Begun();
        Assert.That(model.OnCounterHit(), Is.False);
        Assert.That(model.OnEnd().HasEnded, Is.False);
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Guarding));

        // 해제 중 반격 Hit 은 무시(반격으로 넘어가지 않았다).
        model.OnGuardWindowClosed();
        Assert.That(model.OnCounterHit(), Is.False);
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Releasing));

        var idle = new FirstMeleeGuardCounterModel();
        Assert.That(idle.OnCounterHit(), Is.False);
        Assert.That(idle.OnEnd().HasEnded, Is.False);
        idle.OnGuardWindowClosed();
        Assert.That(idle.Phase, Is.EqualTo(FirstMeleeGuardPhase.Idle));
    }

    [Test]
    public void CounterHit_DueExactlyOnce()
    {
        var model = Countering();
        Assert.That(model.OnCounterHit(), Is.True);

        // MarkCounterFired 를 아직 안 불러도 다시 오지 않는다.
        Assert.That(model.OnCounterHit(), Is.False);
        model.MarkCounterFired();
        Assert.That(model.OnCounterHit(), Is.False);
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Countering));
    }

    [Test]
    public void CounterHit_DuplicateFromStateReentry_FiresOnce()
    {
        // 원격 오너 동기로 반격 상태가 다시 들어가 Hit·End 가 두 번 와도 반격 1회·종료 1회.
        var model = Countering();
        int fired = 0;
        if (model.OnCounterHit()) { fired++; model.MarkCounterFired(); }
        if (model.OnCounterHit()) { fired++; model.MarkCounterFired(); }

        GuardEventResult end1 = model.OnEnd();
        if (end1.CounterHitDue) fired++;
        if (model.OnCounterHit()) fired++;
        GuardEventResult end2 = model.OnEnd();

        Assert.That(fired, Is.EqualTo(1));
        Assert.That(end1.Ended, Is.EqualTo(GuardEndReason.CounterFinished));
        Assert.That(end2.HasEnded, Is.False);
    }

    [Test]
    public void CounterEnd_AfterHit_CounterFinished_NoSecondHit()
    {
        var model = Countering();
        Assert.That(model.OnCounterHit(), Is.True);
        model.MarkCounterFired();

        GuardEventResult end = model.OnEnd();
        Assert.That(end.Ended, Is.EqualTo(GuardEndReason.CounterFinished));
        Assert.That(end.CounterHitDue, Is.False);
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Finished));
    }

    [Test]
    public void CounterEnd_WithoutHit_HitDueAndFinished()
    {
        // 클립에서 Hit 이 빠졌거나 End 와 같은 시점(같은 프레임에 End 가 먼저) — 반격과 종료가 같이 온다.
        var model = Countering();
        GuardEventResult r = model.OnEnd();
        Assert.That(r.CounterHitDue, Is.True);
        Assert.That(r.Ended, Is.EqualTo(GuardEndReason.CounterFinished));
        Assert.That(model.HasPendingCounter, Is.True); // 호출측이 반격을 먼저 실행해야 한다

        // 뒤따라 온 Hit 은 무시 — 반격이 종료 뒤로 밀려 두 번 나가지 않는다.
        model.MarkCounterFired();
        Assert.That(model.OnCounterHit(), Is.False);
    }

    [Test]
    public void HitThenEnd_SameFrame_BothHandledInOrder()
    {
        // Hit·End 가 같은 시점(같은 프레임에 Hit 먼저) — 반격 1회 후 종료.
        var model = Countering();
        Assert.That(model.OnCounterHit(), Is.True);
        model.MarkCounterFired();
        GuardEventResult end = model.OnEnd();
        Assert.That(end.CounterHitDue, Is.False);
        Assert.That(end.Ended, Is.EqualTo(GuardEndReason.CounterFinished));
        Assert.That(model.HasPendingCounter, Is.False);
    }

    // ── 반격 보장 ──

    [Test]
    public void HasPendingCounter_TrueAfterBlock_FalseAfterMarkFired()
    {
        var model = Begun();
        Assert.That(model.HasPendingCounter, Is.False);

        model.Evaluate(Hit(Front));
        Assert.That(model.HasPendingCounter, Is.True);

        model.MarkCounterFired();
        Assert.That(model.HasPendingCounter, Is.False);
    }

    [Test]
    public void HasPendingCounter_FalseWhenGuardExpired()
    {
        var model = Begun();
        model.OnGuardWindowClosed();
        Assert.That(model.OnEnd().Ended, Is.EqualTo(GuardEndReason.GuardExpired));
        Assert.That(model.HasPendingCounter, Is.False);
    }

    // ── 넉백 ──

    [Test]
    public void BlocksKnockback_OnlyWhileCountering()
    {
        var model = Begun();
        Assert.That(model.BlocksKnockback, Is.False); // 방어 대기 중에는 넉백 정상

        model.Evaluate(Hit(Front));
        Assert.That(model.BlocksKnockback, Is.True);
        Assert.That(model.OnCounterHit(), Is.True);
        model.MarkCounterFired();
        Assert.That(model.BlocksKnockback, Is.True); // 반격 판정 뒤에도 End 까지 유지
    }

    [Test]
    public void BlocksKnockback_FalseAfterCounterEnd()
    {
        var model = Countering();
        model.OnEnd();
        Assert.That(model.BlocksKnockback, Is.False);
    }

    // ── E 재입력 해제(할 일 94) ──
    // inGuardHold = 서버 애니메이터가 방어중(SubSkillGuardIdle) 상태이고 전이 중이 아님.

    [Test]
    public void RePress_DuringPrepare_Ignored()
    {
        // 준비 클립 재생 중(Custom0 전후 모두) — 애니메이터가 아직 방어중이 아니다.
        var model = BegunClosed();
        Assert.That(model.TryRequestRelease(animatorInGuardHold: false), Is.False);
        model.OnGuardWindowOpened();
        Assert.That(model.TryRequestRelease(animatorInGuardHold: false), Is.False);
        Assert.That(model.ReleaseRequested, Is.False);
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Guarding));
    }

    [Test]
    public void RePress_DuringHold_Accepted_PhaseUnchanged_WindowStillOpen()
    {
        // 받아도 단계·창은 그대로 — 창은 해제 클립 Custom1 이, 종료는 End 가 기존 경로로 처리한다.
        var model = Begun();
        Assert.That(model.TryRequestRelease(animatorInGuardHold: true), Is.True);
        Assert.That(model.ReleaseRequested, Is.True);
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Guarding));
        Assert.That(model.IsGuardWindowOpen, Is.True);

        model.OnGuardWindowClosed();
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Releasing));
        GuardEventResult end = model.OnEnd();
        Assert.That(end.Ended, Is.EqualTo(GuardEndReason.GuardExpired));
        Assert.That(end.CounterHitDue, Is.False);
    }

    [Test]
    public void RePress_Twice_SecondIgnored()
    {
        // 해제 CrossFade 중(Custom1 전) 연타 — 애니메이터가 아직 방어중으로 읽혀도 다시 받지 않는다.
        var model = Begun();
        Assert.That(model.TryRequestRelease(animatorInGuardHold: true), Is.True);
        Assert.That(model.TryRequestRelease(animatorInGuardHold: true), Is.False);
    }

    [Test]
    public void RePress_WhileReleasing_Ignored()
    {
        // 방어중 시간이 다 돼 해제로 넘어간 뒤(Custom1) — 원격 오너가 늦게 보낸 재입력도 무시.
        var model = Begun();
        model.OnGuardWindowClosed();
        Assert.That(model.TryRequestRelease(animatorInGuardHold: true), Is.False);
        Assert.That(model.TryRequestRelease(animatorInGuardHold: false), Is.False);
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Releasing));
    }

    [Test]
    public void RePress_WhileTransitioningToRelease_Ignored()
    {
        // 방어중 → 해제 exitTime 전이 중(Custom1 전) — 애니메이터 판정이 거짓이라 무시.
        var model = Begun();
        Assert.That(model.TryRequestRelease(animatorInGuardHold: false), Is.False);
        Assert.That(model.ReleaseRequested, Is.False);
    }

    [Test]
    public void RePress_WhileCountering_Ignored()
    {
        var model = Countering();
        Assert.That(model.TryRequestRelease(animatorInGuardHold: true), Is.False);
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Countering));
        Assert.That(model.HasPendingCounter, Is.True);
    }

    [Test]
    public void RePress_IdleOrFinished_Ignored()
    {
        var model = new FirstMeleeGuardCounterModel();
        Assert.That(model.TryRequestRelease(animatorInGuardHold: true), Is.False);

        var finished = Countering();
        finished.OnEnd();
        Assert.That(finished.TryRequestRelease(animatorInGuardHold: true), Is.False);
    }

    [Test]
    public void BlockAfterReleaseRequested_BeforeCustom1_StillCounters()
    {
        // 해제 CrossFade 가 Custom1 에 닿기 전까지 창은 열려 있다 — 그 사이 방어 성공은 그대로 반격.
        var model = Begun();
        Assert.That(model.TryRequestRelease(animatorInGuardHold: true), Is.True);
        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.BlockAndCounter));
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Countering));

        model.OnGuardWindowClosed(); // 반격 중 늦게 온 Custom1 은 무시
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Countering));
    }

    [Test]
    public void ReleaseRequested_ClearedByResetAndBegin()
    {
        var model = Begun();
        model.TryRequestRelease(animatorInGuardHold: true);
        model.Reset();
        Assert.That(model.ReleaseRequested, Is.False);

        model.Begin(Vector3.forward, GuardAngle);
        model.OnGuardWindowOpened();
        Assert.That(model.TryRequestRelease(animatorInGuardHold: true), Is.True);
    }

    // ── 종료 후 ──

    [Test]
    public void Finished_AllPass()
    {
        var model = Countering();
        model.OnEnd();
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Finished));

        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Evaluate(Hit(Back)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.BlocksKnockback, Is.False);
        model.OnGuardWindowOpened();
        Assert.That(model.IsGuardWindowOpen, Is.False);
        model.OnGuardWindowClosed();
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Finished));
        Assert.That(model.OnEnd().HasEnded, Is.False);
    }

    [Test]
    public void Reset_ReturnsIdle_AllPass()
    {
        var model = Countering();
        model.Reset();

        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Idle));
        Assert.That(model.HasPendingCounter, Is.False);
        Assert.That(model.Blocked, Is.False);
        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.BlocksKnockback, Is.False);
        model.OnGuardWindowOpened();
        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.Pass)); // Idle 에서 Custom0 은 창을 열지 않는다
        Assert.That(model.OnCounterHit(), Is.False);
        Assert.That(model.OnEnd().HasEnded, Is.False);
    }
}
