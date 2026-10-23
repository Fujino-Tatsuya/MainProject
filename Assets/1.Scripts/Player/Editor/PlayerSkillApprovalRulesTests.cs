using NUnit.Framework;

/// <summary>
/// 스킬 시전 승인 사전 조건(할 일 75) — 서버 최종 승인과 원격 오너 1차 검증이 같은 판정·순서를 쓴다.
/// 순서 = 미배정 → 슬롯 교체 경합 → 실행 중 → 쿨타임 → 사망 → 상태/차단 효과.
/// </summary>
public sealed class PlayerSkillApprovalRulesTests
{
    private static PlayerSkillApproval Evaluate(
        bool assigned = true, bool indexMatches = true, bool active = false, bool cooldownReady = true,
        bool dead = false, bool usableWhileDead = false, bool canUseSkillState = true) =>
        PlayerSkillApprovalRules.EvaluatePreconditions(
            assigned, indexMatches, active, cooldownReady, dead, usableWhileDead, canUseSkillState);

    [Test]
    public void AllPreconditionsMet_Approved()
    {
        Assert.That(Evaluate(), Is.EqualTo(PlayerSkillApproval.Approved));
    }

    [Test]
    public void EachFailure_ReportsItsReason()
    {
        Assert.That(Evaluate(assigned: false), Is.EqualTo(PlayerSkillApproval.Unassigned));
        Assert.That(Evaluate(indexMatches: false), Is.EqualTo(PlayerSkillApproval.SlotRace));
        Assert.That(Evaluate(active: true), Is.EqualTo(PlayerSkillApproval.SkillActive));
        Assert.That(Evaluate(cooldownReady: false), Is.EqualTo(PlayerSkillApproval.Cooldown));
        Assert.That(Evaluate(dead: true), Is.EqualTo(PlayerSkillApproval.Dead));
        Assert.That(Evaluate(canUseSkillState: false), Is.EqualTo(PlayerSkillApproval.StateOrEffect));
    }

    [Test]
    public void Order_FirstFailureWins()
    {
        Assert.That(Evaluate(assigned: false, cooldownReady: false), Is.EqualTo(PlayerSkillApproval.Unassigned));
        Assert.That(Evaluate(indexMatches: false, active: true), Is.EqualTo(PlayerSkillApproval.SlotRace));
        Assert.That(Evaluate(active: true, cooldownReady: false), Is.EqualTo(PlayerSkillApproval.SkillActive));
        Assert.That(Evaluate(cooldownReady: false, canUseSkillState: false), Is.EqualTo(PlayerSkillApproval.Cooldown),
            "쿨타임이 상태보다 먼저(기존 서버 순서)");
        Assert.That(Evaluate(cooldownReady: false, dead: true, usableWhileDead: true), Is.EqualTo(PlayerSkillApproval.Cooldown));
    }

    [Test]
    public void Dead_OnlyUsableWhileDeadPasses_IgnoresStateGate()
    {
        Assert.That(Evaluate(dead: true, usableWhileDead: true, canUseSkillState: false),
            Is.EqualTo(PlayerSkillApproval.Approved), "사망 중 허용 스킬은 Idle/Move 조건을 보지 않는다");
        Assert.That(Evaluate(dead: true, usableWhileDead: false, canUseSkillState: true),
            Is.EqualTo(PlayerSkillApproval.Dead));
    }
}
