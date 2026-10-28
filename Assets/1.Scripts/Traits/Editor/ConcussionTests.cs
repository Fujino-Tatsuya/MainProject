using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 125 (4/5) — 뇌진탕(<see cref="StatusEffectType.Concussion"/>) 순수 규칙(<see cref="ConcussionState"/>):
/// 맞을 때마다 재설정·넉백 종료 시 기절 전환·넉백 밖 즉시 기절·기존 기절과 큰 값·슈퍼아머 거부·제압(그로기) 전환 시 대기 취소.
/// </summary>
public sealed class ConcussionTests
{
    const float Eps = 1e-4f;
    const float Stun = ConcussionState.DefaultStunSeconds;

    // ── 재설정(가산 아님) ───────────────────────────────────────────────

    [Test]
    public void Apply_DuringKnockback_ResetsTo075_NotAdded()
    {
        var state = new ConcussionState();
        Assert.AreEqual(ConcussionState.ApplyResult.Pending, state.Apply(Stun, true, StatusEffectType.None));
        Assert.AreEqual(ConcussionState.ApplyResult.Pending, state.Apply(Stun, true, StatusEffectType.None));
        Assert.AreEqual(ConcussionState.ApplyResult.Pending, state.Apply(Stun, true, StatusEffectType.None));

        Assert.AreEqual(0.75f, state.PendingStun, Eps, "3번 맞아도 0.75 — 합산 아님");
    }

    [Test]
    public void Apply_LongerPending_ResetToIncoming_NotMax()
    {
        var state = new ConcussionState();
        state.Apply(2f, true, StatusEffectType.None);
        state.Apply(Stun, true, StatusEffectType.None);

        Assert.AreEqual(0.75f, state.PendingStun, Eps, "재설정 = 들어온 값으로 갱신(큰 값 유지 아님)");
    }

    // ── 넉백 종료 시 기절 전환 ───────────────────────────────────────────

    [Test]
    public void KnockbackEnd_ConvertsToStun_AndClearsPending()
    {
        var state = new ConcussionState();
        state.Apply(Stun, true, StatusEffectType.Stunned);

        Assert.AreEqual(0.75f, state.ConsumeOnKnockbackEnd(StatusEffectType.Stunned | StatusEffectType.Concussion), Eps);
        Assert.IsFalse(state.IsPending);
        Assert.AreEqual(0f, state.ConsumeOnKnockbackEnd(StatusEffectType.None), Eps, "한 번만 바뀐다");
    }

    [Test]
    public void Apply_NotKnockedBack_StunsImmediately()
    {
        var state = new ConcussionState();
        Assert.AreEqual(ConcussionState.ApplyResult.StunNow, state.Apply(Stun, false, StatusEffectType.None));
        Assert.IsFalse(state.IsPending, "즉시 기절 — 대기 없음");
    }

    [Test]
    public void KnockbackEnd_WithoutPending_ReturnsZero()
    {
        var state = new ConcussionState();
        Assert.AreEqual(0f, state.ConsumeOnKnockbackEnd(StatusEffectType.None), Eps);
    }

    // ── 기존 기절과 큰 값 ─────────────────────────────────────────────

    [TestCase(1.2f, 0.75f, 1.2f)]   // 기존이 더 길다 — 유지
    [TestCase(0.3f, 0.75f, 0.75f)]  // 새 기절이 더 길다
    [TestCase(0f, 0.75f, 0.75f)]    // 기존 없음
    [TestCase(-1f, 0.75f, 0.75f)]   // 음수(만료 지남) = 0
    public void MergeStun_TakesMax_NotSum(float existing, float incoming, float expected)
    {
        Assert.AreEqual(expected, ConcussionState.MergeStun(existing, incoming), Eps);
    }

    // ── 슈퍼아머 거부 ─────────────────────────────────────────────────

    [Test]
    public void SuperArmor_RejectsConcussion()
    {
        var state = new ConcussionState();
        Assert.AreEqual(ConcussionState.ApplyResult.Rejected, state.Apply(Stun, true, StatusEffectType.SuperArmor));
        Assert.AreEqual(ConcussionState.ApplyResult.Rejected, state.Apply(Stun, false, StatusEffectType.SuperArmor));
        Assert.IsFalse(state.IsPending);
    }

    [Test]
    public void SuperArmor_AtKnockbackEnd_NoStun()
    {
        var state = new ConcussionState();
        state.Apply(Stun, true, StatusEffectType.None);

        Assert.AreEqual(0f, state.ConsumeOnKnockbackEnd(StatusEffectType.SuperArmor), Eps, "넉백 종료 순간 자격 재확인");
        Assert.IsFalse(state.IsPending);
    }

    [Test]
    public void Concussion_IsBlockedCrowdControl_AndDebuff()
    {
        Assert.AreNotEqual(StatusEffectType.None, StatusEffectImmunityPolicy.BlockedCrowdControl & StatusEffectType.Concussion);
        Assert.IsTrue(StatusEffectImmunityPolicy.ShouldIgnoreApplication(StatusEffectType.SuperArmor, StatusEffectType.Concussion));
        Assert.IsFalse(StatusEffectImmunityPolicy.ShouldIgnoreApplication(StatusEffectType.None, StatusEffectType.Concussion));
        Assert.AreEqual(StatusEffectCategory.Debuff, StatusEffectCategories.Of(StatusEffectType.Concussion, 1f));
    }

    [Test]
    public void ZeroDuration_Rejected()
    {
        var state = new ConcussionState();
        Assert.AreEqual(ConcussionState.ApplyResult.Rejected, state.Apply(0f, true, StatusEffectType.None));
    }

    // ── 제압(그로기) 전환 시 대기 기절 취소 ─────────────────────────────

    [Test]
    public void Cancel_ClearsPending()
    {
        var state = new ConcussionState();
        state.Apply(Stun, true, StatusEffectType.None);

        Assert.IsTrue(state.Cancel());
        Assert.AreEqual(0f, state.ConsumeOnKnockbackEnd(StatusEffectType.None), Eps, "취소 뒤 넉백이 끝나도 기절 없음");
        Assert.IsFalse(state.Cancel(), "두 번째 취소는 할 일 없음");
    }

    [TestCase(MonsterState.Knockback, MonsterState.Groggy, true)]  // 넉백 중 제압·그로기 진입
    [TestCase(MonsterState.Hit, MonsterState.Groggy, true)]
    [TestCase(MonsterState.Knockback, MonsterState.Dead, true)]    // 넉백이 다른 길로 끝남
    [TestCase(MonsterState.Knockback, MonsterState.Hit, true)]     // 정상 종료도 true — ExitKnockback 이 먼저 소비해 할 일 없음
    [TestCase(MonsterState.Knockback, MonsterState.Knockback, false)] // 넉백 갱신(Q 매 틱) — 유지
    [TestCase(MonsterState.Idle, MonsterState.Knockback, false)]
    [TestCase(MonsterState.Idle, MonsterState.Chase, false)]
    public void StateTransition_CancelsPending(MonsterState previous, MonsterState next, bool cancels)
    {
        Assert.AreEqual(cancels, MonsterStatusEffect.CancelsPendingConcussion(previous, next));
    }

    // ── 표시는 기절과 같다 ─────────────────────────────────────────────

    [Test]
    public void OverheadLabel_ConcussionShowsAsStunned()
    {
        Assert.AreEqual(OverheadStatusLabel.Stunned,
            OverheadStatusLabelRules.Select(StatusEffectType.Concussion, false, false, PlayerLifeState.Alive));
    }

    // ── 특성 적용 입구(TraitKnockbackEffect) ────────────────────────────

    [Test]
    public void TraitKnockback_ConcussionOption_FillsAttackInfo()
    {
        var effect = new TraitKnockbackEffect(2f, 0.2f, 0.1f, concussionDuration: Stun);
        var info = new AttackInfo(10, AttackType.SkillQ);

        effect.ApplyCrowdControl(ref info, Vector3.forward);

        Assert.AreEqual(0.75f, info.concussionDuration, Eps);
        Assert.AreEqual(0.1f, info.staggerDuration, Eps, "경직은 따로 그대로");
    }

    [Test]
    public void TraitKnockback_NoConcussionOption_LeavesZero()
    {
        var effect = new TraitKnockbackEffect(2f, 0.2f, 0.1f);
        var info = new AttackInfo(10, AttackType.SkillQ);

        effect.ApplyCrowdControl(ref info, Vector3.forward);

        Assert.AreEqual(0f, info.concussionDuration, Eps);
    }
}
