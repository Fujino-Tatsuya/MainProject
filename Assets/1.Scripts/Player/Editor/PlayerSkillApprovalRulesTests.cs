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

    // ── 대쉬 간파(할 일 115): Dash 상태 + Interrupt 슬롯만, 순서는 기존과 같고 마지막에 창 ──

    private static PlayerSkillApproval EvaluateDash(
        bool assigned = true, bool indexMatches = true, bool interruptSlot = true, bool active = false,
        bool cooldownReady = true, bool dead = false, bool dashState = true, bool blocked = false,
        bool inWindow = true) =>
        PlayerSkillApprovalRules.EvaluateDashInterruptPreconditions(
            assigned, indexMatches, interruptSlot, active, cooldownReady, dead, dashState, blocked, inWindow);

    [Test]
    public void DashInterrupt_AllMet_Approved()
    {
        Assert.That(EvaluateDash(), Is.EqualTo(PlayerSkillApproval.Approved));
    }

    [Test]
    public void DashInterrupt_EachFailure_ReportsItsReason()
    {
        Assert.That(EvaluateDash(assigned: false), Is.EqualTo(PlayerSkillApproval.Unassigned));
        Assert.That(EvaluateDash(indexMatches: false), Is.EqualTo(PlayerSkillApproval.SlotRace));
        Assert.That(EvaluateDash(active: true), Is.EqualTo(PlayerSkillApproval.SkillActive), "이미 실린 간파 포함");
        Assert.That(EvaluateDash(cooldownReady: false), Is.EqualTo(PlayerSkillApproval.Cooldown));
        Assert.That(EvaluateDash(dead: true), Is.EqualTo(PlayerSkillApproval.Dead));
        Assert.That(EvaluateDash(interruptSlot: false), Is.EqualTo(PlayerSkillApproval.StateOrEffect), "Interrupt 슬롯만");
        Assert.That(EvaluateDash(dashState: false), Is.EqualTo(PlayerSkillApproval.StateOrEffect), "Dash 상태만");
        Assert.That(EvaluateDash(blocked: true), Is.EqualTo(PlayerSkillApproval.StateOrEffect));
        Assert.That(EvaluateDash(inWindow: false), Is.EqualTo(PlayerSkillApproval.DashWindow));
    }

    [Test]
    public void DashInterrupt_Order_MatchesExisting()
    {
        Assert.That(EvaluateDash(assigned: false, cooldownReady: false), Is.EqualTo(PlayerSkillApproval.Unassigned));
        Assert.That(EvaluateDash(indexMatches: false, active: true), Is.EqualTo(PlayerSkillApproval.SlotRace));
        Assert.That(EvaluateDash(active: true, cooldownReady: false), Is.EqualTo(PlayerSkillApproval.SkillActive));
        Assert.That(EvaluateDash(cooldownReady: false, dead: true), Is.EqualTo(PlayerSkillApproval.Cooldown), "쿨 → 사망");
        Assert.That(EvaluateDash(cooldownReady: false, dashState: false, inWindow: false), Is.EqualTo(PlayerSkillApproval.Cooldown),
            "쿨 거부가 상태·창보다 먼저 — 오너 쿨 거부 피드백(Q3)");
        Assert.That(EvaluateDash(dead: true, blocked: true), Is.EqualTo(PlayerSkillApproval.Dead), "사망 → 차단 효과");
        Assert.That(EvaluateDash(blocked: true, inWindow: false), Is.EqualTo(PlayerSkillApproval.StateOrEffect), "상태 → 창");
    }

    [Test]
    public void DashInterrupt_ExistingRulesUnchanged_DashStateStillRejected()
    {
        // 기존 일반 승인은 Dash 상태(canUseSkillState=false)를 여전히 거부한다.
        Assert.That(Evaluate(canUseSkillState: false), Is.EqualTo(PlayerSkillApproval.StateOrEffect));
    }
}
