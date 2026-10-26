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
    public void RePress_DuringHold_Accepted_WindowClosedImmediately()
    {
        // 할 일 95 — 받는 즉시 창이 닫히고 Releasing. 해제 클립 Custom1 은 무시되고 End 가 끝낸다.
        var model = Begun();
        Assert.That(model.TryRequestRelease(animatorInGuardHold: true), Is.True);
        Assert.That(model.ReleaseRequested, Is.True);
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Releasing));
        Assert.That(model.IsGuardWindowOpen, Is.False);
        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.Pass));

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
    public void AttackAfterReleaseRequested_BeforeCustom1_NoCounter()
    {
        // 할 일 95 — 해제 동작 초반(Custom1 전) 정면 피격도 반격하지 않는다.
        var model = Begun();
        Assert.That(model.TryRequestRelease(animatorInGuardHold: true), Is.True);
        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Releasing));
        Assert.That(model.HasPendingCounter, Is.False);
        Assert.That(model.BlocksKnockback, Is.False);
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

    // ── E 홀드(할 일 95) ──
    // TickHold(animatorInGuardHold, now, max) — 서버 매 틱. 최대 유지 시간은 방어중에 처음 들어온 틱부터 잰다.

    const float HoldMax = 3f;

    [Test]
    public void Hold_TapMode_NoHoldNotice_NeverReleases()
    {
        // 탭 방식은 누름/뗌 통보가 없다 — 방어중 길이는 애니메이터 전이(1.5초)가 정한다.
        var model = Begun();
        Assert.That(model.HoldMode, Is.False);
        Assert.That(model.TickHold(true, 0f, HoldMax), Is.EqualTo(GuardHoldRelease.None));
        Assert.That(model.TickHold(true, 100f, HoldMax), Is.EqualTo(GuardHoldRelease.None));
        Assert.That(model.ReleaseRequested, Is.False);
    }

    [Test]
    public void Hold_WhileHeld_KeepsGuarding()
    {
        var model = Begun();
        model.OnHoldInput(held: true);
        Assert.That(model.HoldMode, Is.True);
        Assert.That(model.TickHold(true, 10f, HoldMax), Is.EqualTo(GuardHoldRelease.None));
        Assert.That(model.TickHold(true, 12.9f, HoldMax), Is.EqualTo(GuardHoldRelease.None));
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Guarding));
        Assert.That(model.IsGuardWindowOpen, Is.True);
    }

    [Test]
    public void Hold_ReleaseKey_DuringGuardHold_ReleasesOnce_WindowClosedImmediately()
    {
        var model = Begun();
        model.OnHoldInput(true);
        model.TickHold(true, 10f, HoldMax);
        model.OnHoldInput(false);

        Assert.That(model.TickHold(true, 10.5f, HoldMax), Is.EqualTo(GuardHoldRelease.KeyReleased));
        Assert.That(model.ReleaseRequested, Is.True);
        // 할 일 95 — 창은 바로 닫히고 Releasing. 해제 클립 Custom1 은 무시되고 End 가 끝낸다.
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Releasing));
        Assert.That(model.IsGuardWindowOpen, Is.False);
        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.Pass));
        // 해제 CrossFade 중(Custom1 전)에는 다시 내지 않는다.
        Assert.That(model.TickHold(true, 10.6f, HoldMax), Is.EqualTo(GuardHoldRelease.None));

        model.OnGuardWindowClosed();
        GuardEventResult end = model.OnEnd();
        Assert.That(end.Ended, Is.EqualTo(GuardEndReason.GuardExpired));
        Assert.That(end.CounterHitDue, Is.False);
    }

    [Test]
    public void Hold_MaxDuration_MeasuredFromGuardHoldEntry()
    {
        var model = BegunClosed();
        model.OnHoldInput(true);
        // 준비 중(애니메이터가 아직 방어중 아님)은 시간을 재지 않는다.
        Assert.That(model.TickHold(false, 0f, HoldMax), Is.EqualTo(GuardHoldRelease.None));
        model.OnGuardWindowOpened();
        Assert.That(model.TickHold(false, 0.25f, HoldMax), Is.EqualTo(GuardHoldRelease.None));

        Assert.That(model.TickHold(true, 0.5f, HoldMax), Is.EqualTo(GuardHoldRelease.None)); // 방어중 진입 = 0.5
        Assert.That(model.TickHold(true, 3.25f, HoldMax), Is.EqualTo(GuardHoldRelease.None));
        Assert.That(model.TickHold(true, 3.5f, HoldMax), Is.EqualTo(GuardHoldRelease.MaxDuration));
        Assert.That(model.ReleaseRequested, Is.True);
    }

    [Test]
    public void Hold_ReleasedDuringPrepare_ReleasesOnFirstGuardHoldTick()
    {
        // 준비 중에 이미 뗐다 — 방어중에 들어온 첫 틱에 해제(최소 한 프레임 방어중). 준비 중에는 해제하지 않는다.
        var model = BegunClosed();
        model.OnHoldInput(true);
        model.OnHoldInput(false);
        Assert.That(model.TickHold(false, 0.1f, HoldMax), Is.EqualTo(GuardHoldRelease.None));
        model.OnGuardWindowOpened();
        Assert.That(model.TickHold(false, 0.2f, HoldMax), Is.EqualTo(GuardHoldRelease.None));
        Assert.That(model.IsGuardWindowOpen, Is.True); // 준비 중 Custom0 뒤 방어 창은 열려 있다

        Assert.That(model.TickHold(true, 0.35f, HoldMax), Is.EqualTo(GuardHoldRelease.KeyReleased));
    }

    [Test]
    public void Hold_FirstNoticeAlreadyReleased_CountsAsHoldMode()
    {
        // 원격 오너가 시작 RPC 를 받기 전에 뗐다 — 첫 통보가 false 여도 홀드 방식이고 방어중 첫 틱에 해제한다.
        var model = BegunClosed();
        model.OnHoldInput(false);
        Assert.That(model.HoldMode, Is.True);
        model.OnGuardWindowOpened();
        Assert.That(model.TickHold(true, 0.35f, HoldMax), Is.EqualTo(GuardHoldRelease.KeyReleased));
    }

    [Test]
    public void Hold_RePressAfterRelease_DoesNotResumeHold()
    {
        var model = BegunClosed();
        model.OnHoldInput(true);
        model.OnHoldInput(false);
        model.OnHoldInput(true);
        model.OnGuardWindowOpened();
        Assert.That(model.TickHold(true, 0.35f, HoldMax), Is.EqualTo(GuardHoldRelease.KeyReleased));
    }

    [Test]
    public void Hold_BlockWhileHeld_Counters_NoRelease()
    {
        var model = Begun();
        model.OnHoldInput(true);
        model.TickHold(true, 0f, HoldMax);
        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.BlockAndCounter));

        model.OnHoldInput(false);
        Assert.That(model.TickHold(true, 0.5f, HoldMax), Is.EqualTo(GuardHoldRelease.None));
        Assert.That(model.TickHold(true, 10f, HoldMax), Is.EqualTo(GuardHoldRelease.None));
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Countering));
    }

    [Test]
    public void Hold_AndRePress_ShareOneRelease()
    {
        // E 재입력 해제(할 일 94)와 홀드 해제는 같은 한 번짜리 요청이다 — 겹쳐도 CrossFade 는 한 번.
        var model = Begun();
        model.OnHoldInput(true);
        Assert.That(model.TryRequestRelease(animatorInGuardHold: true), Is.True);
        model.OnHoldInput(false);
        Assert.That(model.TickHold(true, 1f, HoldMax), Is.EqualTo(GuardHoldRelease.None));
    }

    [Test]
    public void Hold_ZeroMax_ReleasesOnFirstGuardHoldTick()
    {
        var model = Begun();
        model.OnHoldInput(true);
        Assert.That(model.TickHold(true, 5f, 0f), Is.EqualTo(GuardHoldRelease.MaxDuration));
    }

    [Test]
    public void Hold_NoticeBeforeBeginOrAfterEnd_Ignored_ResetClears()
    {
        var idle = new FirstMeleeGuardCounterModel();
        idle.OnHoldInput(true);
        Assert.That(idle.HoldMode, Is.False);

        var model = Begun();
        model.OnHoldInput(true);
        model.Reset();
        Assert.That(model.HoldMode, Is.False);

        model.Begin(Vector3.forward, GuardAngle);
        model.OnGuardWindowOpened();
        Assert.That(model.HoldMode, Is.False);
        Assert.That(model.TickHold(true, 0f, HoldMax), Is.EqualTo(GuardHoldRelease.None));
    }

    // ── 해제 진입 즉시 창 닫힘(할 일 95) ──
    // 방어중 → 해제로 넘어가는 순간 Custom1 을 기다리지 않는다. Custom1 이 뒤에 오면 무시한다.

    [Test]
    public void ReleaseEntry_HoldMaxDuration_WindowClosedImmediately()
    {
        var model = Begun();
        model.OnHoldInput(true);
        Assert.That(model.TickHold(true, 0f, HoldMax), Is.EqualTo(GuardHoldRelease.None));
        Assert.That(model.TickHold(true, HoldMax, HoldMax), Is.EqualTo(GuardHoldRelease.MaxDuration));

        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Releasing));
        Assert.That(model.IsGuardWindowOpen, Is.False);
        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.HasPendingCounter, Is.False);
    }

    [Test]
    public void ReleaseEntry_TapExitTime_WindowClosedImmediately()
    {
        // 탭 방식 방어중 exitTime 전이 — 요청 없이 애니메이터가 해제로 넘어간 순간(서버 OnTick 감지).
        var model = Begun();
        model.OnReleaseEntered();
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Releasing));
        Assert.That(model.IsGuardWindowOpen, Is.False);
        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.Pass));
        Assert.That(model.OnEnd().Ended, Is.EqualTo(GuardEndReason.GuardExpired));
    }

    [Test]
    public void ReleaseEntry_DashCancel_WindowClosedImmediately()
    {
        // 대시 취소 = 서버 OnEnd(DashCancelled) → Reset — 그 순간 창이 닫혀 이후 피격은 Pass, 늦은 Custom1·End 도 무시.
        var model = Begun();
        Assert.That(model.CounterDueOnCancel(SkillEndReason.DashCancelled), Is.False);
        model.Reset();

        Assert.That(model.IsGuardWindowOpen, Is.False);
        Assert.That(model.Evaluate(Hit(Front)), Is.EqualTo(GuardVerdict.Pass));
        model.OnGuardWindowClosed();
        Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Idle));
        Assert.That(model.OnEnd().HasEnded, Is.False);
    }

    [Test]
    public void Custom1_AfterReleaseEntry_IgnoredNoDoubleClose()
    {
        // 네 경로 모두 — Custom1 이 이미 닫힌 창에 오면 아무 일도 없다(단계 그대로, End 는 한 번만 끝낸다).
        var rePress = Begun();
        rePress.TryRequestRelease(animatorInGuardHold: true);

        var holdReleased = Begun();
        holdReleased.OnHoldInput(true);
        holdReleased.OnHoldInput(false);
        holdReleased.TickHold(true, 0f, HoldMax);

        var holdMax = Begun();
        holdMax.OnHoldInput(true);
        holdMax.TickHold(true, 0f, 0f);

        var tap = Begun();
        tap.OnReleaseEntered();

        foreach (FirstMeleeGuardCounterModel model in new[] { rePress, holdReleased, holdMax, tap })
        {
            Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Releasing));
            Assert.DoesNotThrow(() => model.OnGuardWindowClosed());
            model.OnReleaseEntered();
            Assert.That(model.Phase, Is.EqualTo(FirstMeleeGuardPhase.Releasing));
            Assert.That(model.IsGuardWindowOpen, Is.False);

            Assert.That(model.OnEnd().Ended, Is.EqualTo(GuardEndReason.GuardExpired));
            Assert.That(model.OnEnd().HasEnded, Is.False);
        }
    }

    [Test]
    public void ReleaseEntered_NotGuarding_Ignored()
    {
        // 반격 중·준비 전(Idle) 해제 진입 신호는 무시 — 반격 단계가 유지된다.
        var countering = Countering();
        countering.OnReleaseEntered();
        Assert.That(countering.Phase, Is.EqualTo(FirstMeleeGuardPhase.Countering));
        Assert.That(countering.HasPendingCounter, Is.True);

        var idle = new FirstMeleeGuardCounterModel();
        idle.OnReleaseEntered();
        Assert.That(idle.Phase, Is.EqualTo(FirstMeleeGuardPhase.Idle));
    }

    // ── 대시 취소(할 일 95) ──

    [Test]
    public void DashCancel_AllowedInSkillState_Only()
    {
        // 준비·방어중·해제·반격은 모두 Skill 상태다 — 단계와 무관하게 끊는다.
        Assert.That(FirstMeleeGuardRules.AllowsDashCancel(PlayerActionState.Skill), Is.True);
        Assert.That(FirstMeleeGuardRules.AllowsDashCancel(PlayerActionState.Focus), Is.False);
        Assert.That(FirstMeleeGuardRules.AllowsDashCancel(PlayerActionState.Idle), Is.False);
    }

    [Test]
    public void DashCancel_DuringPrepareGuardOrRelease_NoCounter()
    {
        Assert.That(BegunClosed().CounterDueOnCancel(SkillEndReason.DashCancelled), Is.False); // 준비(Custom0 전)
        Assert.That(Begun().CounterDueOnCancel(SkillEndReason.DashCancelled), Is.False);       // 방어중

        var releasing = Begun();
        releasing.OnGuardWindowClosed();
        Assert.That(releasing.CounterDueOnCancel(SkillEndReason.DashCancelled), Is.False);     // 해제
    }

    [Test]
    public void DashCancel_DuringCounter_BeforeHit_CounterStillDue()
    {
        // 방어 성공 뒤 반격 판정(Hit) 전 대시 — 반격 피해는 보장 반격으로 1회 나간다(결정 2 그대로).
        var model = Countering();
        Assert.That(model.CounterDueOnCancel(SkillEndReason.DashCancelled), Is.True);
        model.MarkCounterFired();
        Assert.That(model.CounterDueOnCancel(SkillEndReason.DashCancelled), Is.False); // 보장 반격은 한 번
    }

    [Test]
    public void DashCancel_DuringCounter_AfterHit_NoSecondCounter()
    {
        var model = Countering();
        Assert.That(model.OnCounterHit(), Is.True);
        model.MarkCounterFired();
        Assert.That(model.CounterDueOnCancel(SkillEndReason.DashCancelled), Is.False);
    }

    [Test]
    public void Cancel_CasterDied_NoCounter_OtherReasonsSameAsDash()
    {
        var model = Countering();
        Assert.That(model.CounterDueOnCancel(SkillEndReason.CasterDied), Is.False);
        Assert.That(model.CounterDueOnCancel(SkillEndReason.Cancelled), Is.True);
        Assert.That(model.CounterDueOnCancel(SkillEndReason.MaxDurationReached), Is.True);
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
