using NUnit.Framework;

/// <summary>
/// 서버 사망 판정(<see cref="PlayerLifeActionRules"/>) — 넉백 승인·스킬 승인이 쓰는 기준(할 일 182).
/// FSM 은 사망해도 Dead 로 가지 않으므로 생명주기 Alive 만 행동 가능이어야 한다.
/// </summary>
public sealed class PlayerLifeActionRulesTests
{
    [Test]
    public void Alive_CanAct()
    {
        Assert.IsTrue(PlayerLifeActionRules.CanAct(PlayerLifeState.Alive));
    }

    [TestCase(PlayerLifeState.DeadPresentation)]
    [TestCase(PlayerLifeState.Soul)]
    [TestCase(PlayerLifeState.PermanentDead)]
    public void NotAlive_CannotAct(PlayerLifeState state)
    {
        Assert.IsFalse(PlayerLifeActionRules.CanAct(state), state.ToString());
    }

    // 할 일 183 — 강제 이동(넉백·구속) 진입. 사망 연출 중 늦게 도착한 넉백·구속이 시체를 움직이지 않아야 한다.
    [Test]
    public void Alive_AcceptsForcedMotion()
    {
        Assert.IsTrue(PlayerLifeActionRules.AcceptsForcedMotion(true, PlayerLifeState.Alive));
    }

    [TestCase(PlayerLifeState.DeadPresentation)]
    [TestCase(PlayerLifeState.Soul)]
    [TestCase(PlayerLifeState.PermanentDead)]
    public void NotAlive_RejectsForcedMotion(PlayerLifeState state)
    {
        Assert.IsFalse(PlayerLifeActionRules.AcceptsForcedMotion(true, state), state.ToString());
    }

    [TestCase(PlayerLifeState.Alive)]
    [TestCase(PlayerLifeState.DeadPresentation)]
    public void WithoutLifeCycle_AcceptsForcedMotion(PlayerLifeState state)
    {
        Assert.IsTrue(PlayerLifeActionRules.AcceptsForcedMotion(false, state), state.ToString());
    }

    // 사망 연출 진입 순간 진행 중 넉백·대시·구속은 Idle 전이로 끊긴다(Exit 가 넉백 속도·대시 이동을 0 으로). 그 전제가 깨지지 않게 묶는다.
    [TestCase(PlayerActionState.Knockback)]
    [TestCase(PlayerActionState.Dash)]
    [TestCase(PlayerActionState.Restrained)]
    public void DeathAccess_CancelsMotionStatesToIdle(PlayerActionState state)
    {
        PlayerLifeGameplayAccess access = PlayerLifeGameplayAccess.FromState(PlayerLifeState.DeadPresentation);
        Assert.IsFalse(access.AllowsMovement);
        Assert.IsTrue(PlayerLifeInputPolicy.ShouldCancelToIdle(access.AllowsCombatInput, state), state.ToString());
    }

    // Soul 은 이동을 막지 않는다(부활 대기 중 이동).
    [Test]
    public void SoulAccess_KeepsMovement()
    {
        Assert.IsTrue(PlayerLifeGameplayAccess.FromState(PlayerLifeState.Soul).AllowsMovement);
    }
}
