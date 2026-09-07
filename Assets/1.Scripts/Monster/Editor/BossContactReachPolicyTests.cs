using NUnit.Framework;

/// <summary>
/// 접촉 공격의 개시 거리 판정.
///
/// 🔴 이 규칙이 막는 것은 <b>허공 훅</b> 하나다 — 거리창(3.2)이 attackRange(2.0)보다 넓어서
///    먼 대상에게 걸어 들어가는 도중 훅이 시작되던 결함(2026-09-03 팀장 Play 관찰).
///    그래서 여기서 고정할 것은 "개시는 좁게, 유지는 저작값대로"라는 **비대칭**이다.
/// </summary>
public sealed class BossContactReachPolicyTests
{
    const float AttackRange = 2f;   // No23 실값
    const float HookMax = 2.6f;     // 훅 거리창(하향 후)
    const float Exit = 2.6f;        // 이탈 경계 = 접촉 행 거리창 최댓값

    static BossAttackEntry Hook() => new BossAttackEntry
    {
        attackId = BossAttackId.LeftHook,
        minDistance = 0f,
        maxDistance = HookMax,
        ignoreDistanceWindow = false,
    };

    static BossAttackEntry Dash() => new BossAttackEntry
    {
        attackId = BossAttackId.Dash,
        minDistance = 5f,
        maxDistance = 20f,
        ignoreDistanceWindow = false,
    };

    static BossAttackEntry Jump() => new BossAttackEntry
    {
        attackId = BossAttackId.Jump,
        minDistance = 0f,
        maxDistance = 3f,
        ignoreDistanceWindow = true,
    };

    // ─── 접촉 행 판별 ──────────────────────────────────────────────────

    /// 훅은 접촉 행이다 — 거리창을 쓰고 attackRange 안에서 성립한다.
    [Test]
    public void Hook_IsContactRow()
    {
        Assert.That(BossContactReachPolicy.IsContactRow(Hook(), AttackRange), Is.True);
    }

    /// 🔴 원거리 진입기는 접촉 행이 **아니다.** 게이트를 걸면 그 둘이 아예 못 나간다 —
    ///    돌진은 minDistance 5 로 attackRange 밖에서만 열리고, 점프는 거리창 자체를 무시한다.
    [Test]
    public void RangedOpeners_AreNotContactRows()
    {
        Assert.That(BossContactReachPolicy.IsContactRow(Dash(), AttackRange), Is.False, "돌진");
        Assert.That(BossContactReachPolicy.IsContactRow(Jump(), AttackRange), Is.False, "점프");
    }

    [Test]
    public void NullRow_IsNotContactRow()
    {
        Assert.That(BossContactReachPolicy.IsContactRow(null, AttackRange), Is.False);
    }

    // ─── 개시 상한 ────────────────────────────────────────────────────

    /// 안 붙은 상태의 접촉 행은 attackRange 까지로 좁혀진다(= 걸어 들어간 뒤에만 개시).
    [Test]
    public void NotInReach_ClampsContactRowToAttackRange()
    {
        Assert.That(
            BossContactReachPolicy.EffectiveMaxDistance(Hook(), AttackRange, inReach: false),
            Is.EqualTo(AttackRange));
    }

    /// 붙은 뒤에는 저작값 그대로 — "뒤로 빠지는 플레이어를 쫓아 치는" 의도가 살아 있어야 한다.
    [Test]
    public void InReach_KeepsAuthoredWindow()
    {
        Assert.That(
            BossContactReachPolicy.EffectiveMaxDistance(Hook(), AttackRange, inReach: true),
            Is.EqualTo(HookMax));
    }

    /// 🔴 저작이 attackRange 보다 **더 좁게** 잡은 행은 그 좁은 값을 지킨다 — Min 이어야 하는 이유다.
    ///    (Max/대입이면 거리창이 조용히 넓어져 저작 의도가 뒤집힌다.)
    [Test]
    public void TighterThanAttackRange_StaysTight()
    {
        BossAttackEntry tight = Hook();
        tight.maxDistance = 1.5f;

        Assert.That(
            BossContactReachPolicy.EffectiveMaxDistance(tight, AttackRange, inReach: false),
            Is.EqualTo(1.5f));
    }

    /// 접촉 행이 아니면 붙었는지와 무관하게 저작값이다.
    [TestCase(true)]
    [TestCase(false)]
    public void NonContactRow_IgnoresGate(bool inReach)
    {
        Assert.That(
            BossContactReachPolicy.EffectiveMaxDistance(Dash(), AttackRange, inReach),
            Is.EqualTo(20f));
    }

    // ─── 진입 / 이탈 히스테리시스 ───────────────────────────────────────

    /// attackRange 안이면 붙는다.
    [Test]
    public void WithinAttackRange_Enters()
    {
        Assert.That(BossContactReachPolicy.StaysInReach(false, 2f, AttackRange, Exit), Is.True);
        Assert.That(BossContactReachPolicy.StaysInReach(false, 1f, AttackRange, Exit), Is.True);
    }

    /// 이탈 경계를 넘으면 떨어진다.
    [Test]
    public void BeyondExit_Leaves()
    {
        Assert.That(BossContactReachPolicy.StaysInReach(true, 2.61f, AttackRange, Exit), Is.False);
        Assert.That(BossContactReachPolicy.StaysInReach(true, 8f, AttackRange, Exit), Is.False);
    }

    /// 🔴 진입과 이탈 사이(2.0 ~ 2.6)에서는 **직전 상태를 유지한다.** 경계 하나로 판정하면
    ///    이 구간에서 매 틱 뒤집혀 훅이 나갔다 말았다 한다.
    [Test]
    public void BetweenBoundaries_HoldsPreviousState()
    {
        Assert.That(BossContactReachPolicy.StaysInReach(true, 2.3f, AttackRange, Exit), Is.True,
            "붙어 있었으면 계속 붙어 있다");
        Assert.That(BossContactReachPolicy.StaysInReach(false, 2.3f, AttackRange, Exit), Is.False,
            "접근 중이었으면 아직 안 붙었다 — 여기서 훅이 나가면 허공 훅이다");
    }

    /// 이탈 경계가 진입 경계보다 작게 들어와도(저작 사고) 진입 즉시 이탈하지 않는다.
    [Test]
    public void ExitTighterThanEntry_DoesNotThrash()
    {
        Assert.That(BossContactReachPolicy.StaysInReach(true, 1.9f, AttackRange, exitDistance: 1f), Is.True);
    }
}
