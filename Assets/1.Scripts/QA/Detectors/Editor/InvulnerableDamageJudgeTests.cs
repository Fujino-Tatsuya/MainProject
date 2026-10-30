#if UNITY_EDITOR
using NUnit.Framework;

/// <summary>할 일 184 — P6 판정기는 무적이 이전 샘플부터 이어질 때의 HP 감소만 센다.</summary>
public sealed class InvulnerableDamageJudgeTests
{
    [Test]
    public void 무적이_막_켜진_샘플의_HP_감소는_세지_않는다()
    {
        Assert.IsFalse(InvulnerableDamageJudge.IsViolation(
            hasPreviousHealth: true, previousHealth: 100, health: 80,
            previousInvulnerable: false, invulnerable: true, inFallGrace: false));
    }

    [Test]
    public void 무적이_이어지는_중_HP_감소는_센다()
    {
        Assert.IsTrue(InvulnerableDamageJudge.IsViolation(
            hasPreviousHealth: true, previousHealth: 100, health: 80,
            previousInvulnerable: true, invulnerable: true, inFallGrace: false));
    }

    [Test]
    public void 낙사_유예_중_HP_감소는_세지_않는다()
    {
        Assert.IsFalse(InvulnerableDamageJudge.IsViolation(
            hasPreviousHealth: true, previousHealth: 100, health: 80,
            previousInvulnerable: true, invulnerable: true, inFallGrace: true));
    }

    [Test]
    public void HP_가_그대로면_세지_않는다()
    {
        Assert.IsFalse(InvulnerableDamageJudge.IsViolation(
            hasPreviousHealth: true, previousHealth: 100, health: 100,
            previousInvulnerable: true, invulnerable: true, inFallGrace: false));
    }

    [Test]
    public void 무적이_막_꺼진_샘플의_HP_감소는_세지_않는다()
    {
        Assert.IsFalse(InvulnerableDamageJudge.IsViolation(
            hasPreviousHealth: true, previousHealth: 100, health: 80,
            previousInvulnerable: true, invulnerable: false, inFallGrace: false));
    }

    [Test]
    public void 이전_HP_가_없으면_세지_않는다()
    {
        Assert.IsFalse(InvulnerableDamageJudge.IsViolation(
            hasPreviousHealth: false, previousHealth: 100, health: 80,
            previousInvulnerable: true, invulnerable: true, inFallGrace: false));
    }
}
#endif
