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
}
