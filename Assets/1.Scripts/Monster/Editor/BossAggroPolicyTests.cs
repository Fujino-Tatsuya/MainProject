using NUnit.Framework;

/// <summary>
/// 주기 어그로 재선정 판정.
///
/// 🔴 이 규칙의 존재 이유는 "언제 바꾸는가"보다 <b>"언제 절대 안 바꾸는가"</b> 에 있다.
///    공격 도중 타깃이 바뀌면 조준·잡기 체인·돌진 방향이 함께 흔들린다.
/// </summary>
public sealed class BossAggroPolicyTests
{
    const float Interval = 8f;

    /// 교전 중 대기/추격에서만 재선정이 성립한다.
    [TestCase(MonsterState.Idle,  true)]
    [TestCase(MonsterState.Chase, true)]
    public void ElapsedInterval_RetargetsOnlyWhileIdleOrChasing(MonsterState state, bool expected)
    {
        Assert.That(BossAggroPolicy.ShouldRetarget(state, Interval, Interval), Is.EqualTo(expected));
    }

    /// 🔴 공격·피격·그로기·넉백·복귀·사망 중에는 간격이 지나도 바꾸지 않는다.
    [TestCase(MonsterState.Attack)]
    [TestCase(MonsterState.Hit)]
    [TestCase(MonsterState.Groggy)]
    [TestCase(MonsterState.Knockback)]
    [TestCase(MonsterState.Return)]
    [TestCase(MonsterState.Dead)]
    public void CommittedStates_NeverRetarget(MonsterState state)
    {
        Assert.That(BossAggroPolicy.ShouldRetarget(state, Interval * 10f, Interval), Is.False);
    }

    /// 간격 경계 — 딱 도달하면 성립, 조금 모자라면 안 된다.
    [Test]
    public void Boundary_RequiresFullInterval()
    {
        Assert.That(BossAggroPolicy.ShouldRetarget(MonsterState.Chase, Interval - 0.01f, Interval), Is.False);
        Assert.That(BossAggroPolicy.ShouldRetarget(MonsterState.Chase, Interval, Interval), Is.True);
    }

    /// 간격 0 이하 = 기능 끄기. 저작으로 껐을 때 매 틱 재선정이 도는 사고를 막는다.
    [TestCase(0f)]
    [TestCase(-1f)]
    public void NonPositiveInterval_DisablesRetargeting(float interval)
    {
        Assert.That(BossAggroPolicy.ShouldRetarget(MonsterState.Chase, 9999f, interval), Is.False);
    }
}
