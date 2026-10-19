using NUnit.Framework;

/// <summary>
/// 오너 입력 정책(<see cref="PlayerLifeInputPolicy"/>)의 순수 규칙 — 연출 차단 합성과 "전투 입력이 막히는 순간 행동을 Idle 로 끊는가".
/// 할 일 46 후속 3: 엘리베이터 입력 잠금(카운트다운 끝 t=0)이 걸리는 순간 진행 중이던 대시도 같은 경로로 끊긴다.
/// </summary>
public sealed class PlayerLifeInputPolicyTests
{
    [Test]
    public void BlocksForCinematic_AnyOfThree()
    {
        Assert.IsFalse(PlayerLifeInputPolicy.BlocksForCinematic(false, false, false));
        Assert.IsTrue(PlayerLifeInputPolicy.BlocksForCinematic(true, false, false), "보스 등장 잠금");
        Assert.IsTrue(PlayerLifeInputPolicy.BlocksForCinematic(false, true, false), "엘리베이터 입력 잠금 단독");
        Assert.IsTrue(PlayerLifeInputPolicy.BlocksForCinematic(false, false, true), "숨김");
    }

    [Test]
    public void ElevatorInputLock_CancelsDashToIdle()
    {
        // 생명주기는 전부 허용(Alive)이어도 엘리베이터 입력 잠금 하나가 전투 입력을 닫는다.
        const bool lifeAllowsCombatInput = true;
        bool allowsCombatInput = lifeAllowsCombatInput && !PlayerLifeInputPolicy.BlocksForCinematic(false, true, false);
        Assert.IsFalse(allowsCombatInput);
        Assert.IsTrue(PlayerLifeInputPolicy.ShouldCancelToIdle(allowsCombatInput, PlayerActionState.Dash),
            "잠금 순간 대시는 Idle 로 끊긴다(남은 대시 이동으로 미끄러지지 않음)");
    }

    [Test]
    public void LockedAccess_CancelsEveryNonIdleState()
    {
        foreach (PlayerActionState state in System.Enum.GetValues(typeof(PlayerActionState)))
        {
            Assert.AreEqual(state != PlayerActionState.Idle,
                PlayerLifeInputPolicy.ShouldCancelToIdle(allowsCombatInput: false, state), state.ToString());
        }
    }

    [Test]
    public void AllowedAccess_KeepsDash()
    {
        Assert.IsFalse(PlayerLifeInputPolicy.ShouldCancelToIdle(allowsCombatInput: true, PlayerActionState.Dash),
            "잠금이 없으면 대시를 건드리지 않는다");
        Assert.IsFalse(PlayerLifeInputPolicy.ShouldCancelToIdle(allowsCombatInput: true, PlayerActionState.Skill));
    }
}
