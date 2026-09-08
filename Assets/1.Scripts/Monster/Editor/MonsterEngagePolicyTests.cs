using NUnit.Framework;

/// <summary>
/// 조우 직후 첫 공격 지연.
///
/// 🔴 여기서 고정할 것은 값이 아니라 <b>"지연 뒤에 정확히 풀린다"</b>는 관계 하나다.
///    저작값(SpinnerBot 0.6초 등)은 튜닝 대상이라 테스트로 잠그지 않는다.
/// </summary>
public sealed class MonsterEngagePolicyTests
{
    const float Now = 100f;

    // MonsterBase.CooldownReady 와 같은 식. 도장값이 이 판정을 통과하는 시점을 본다.
    static bool Ready(float now, float stamp, float cooldown) => now - stamp >= cooldown;

    // ─── 걸지 말아야 할 때 ──────────────────────────────────────────────

    [Test]
    public void 지연_0이면_안_건다()
    {
        Assert.IsFalse(MonsterEngagePolicy.ShouldDelay(0f));
    }

    [Test]
    public void 음수_지연도_안_건다()
    {
        Assert.IsFalse(MonsterEngagePolicy.ShouldDelay(-1f));
    }

    [Test]
    public void 양수_지연이면_건다()
    {
        Assert.IsTrue(MonsterEngagePolicy.ShouldDelay(0.6f));
    }

    // ─── 지연 뒤에 풀린다 ──────────────────────────────────────────────

    [Test]
    public void 도장_직후에는_공격_불가()
    {
        float stamp = MonsterEngagePolicy.FirstAttackStamp(Now, 1.25f, 0.6f);
        Assert.IsFalse(Ready(Now, stamp, 1.25f));
    }

    [Test]
    public void 지연_경과_직전까지_불가()
    {
        float stamp = MonsterEngagePolicy.FirstAttackStamp(Now, 1.25f, 0.6f);
        Assert.IsFalse(Ready(Now + 0.59f, stamp, 1.25f));
    }

    [Test]
    public void 지연_경과_시점에_풀린다()
    {
        float stamp = MonsterEngagePolicy.FirstAttackStamp(Now, 1.25f, 0.6f);
        Assert.IsTrue(Ready(Now + 0.6f, stamp, 1.25f));
    }

    // 🔴 지연이 쿨보다 길면 도장이 미래가 된다 — 그래도 같은 식으로 성립해야 한다.
    [Test]
    public void 지연이_쿨보다_길어도_지연_기준으로_풀린다()
    {
        const float cooldown = 0.5f;
        const float delay = 2f;
        float stamp = MonsterEngagePolicy.FirstAttackStamp(Now, cooldown, delay);

        Assert.Greater(stamp, Now, "지연이 쿨보다 길면 도장은 미래여야 한다");
        Assert.IsFalse(Ready(Now + 1.99f, stamp, cooldown));
        Assert.IsTrue(Ready(Now + 2f, stamp, cooldown));
    }

    [Test]
    public void 쿨이_길어도_지연_기준으로_풀린다()
    {
        const float cooldown = 6f;   // SpinnerBot 스핀 쿨 같은 긴 값
        const float delay = 0.6f;
        float stamp = MonsterEngagePolicy.FirstAttackStamp(Now, cooldown, delay);

        Assert.IsFalse(Ready(Now + 0.59f, stamp, cooldown));
        Assert.IsTrue(Ready(Now + 0.6f, stamp, cooldown));
    }

    // 지연을 0 으로 넣으면 "방금 풀린 상태" — 즉 기존 동작과 같아야 한다.
    [Test]
    public void 지연_0_도장은_즉시_공격_가능()
    {
        float stamp = MonsterEngagePolicy.FirstAttackStamp(Now, 1.25f, 0f);
        Assert.IsTrue(Ready(Now, stamp, 1.25f));
    }

    // ─── 사거리 진입/이탈 히스테리시스 ─────────────────────────────────

    const float Range = 2.5f;    // SpinnerBot 실값
    const float Margin = 0.5f;

    [Test]
    public void 밖에서는_사거리_안쪽만_진입()
    {
        Assert.IsTrue(MonsterEngagePolicy.IsInAttackRange(false, 2.5f, Range, Margin));
        Assert.IsFalse(MonsterEngagePolicy.IsInAttackRange(false, 2.51f, Range, Margin));
    }

    [Test]
    public void 안에서는_여유까지_유지()
    {
        Assert.IsTrue(MonsterEngagePolicy.IsInAttackRange(true, 3f, Range, Margin));
        Assert.IsFalse(MonsterEngagePolicy.IsInAttackRange(true, 3.01f, Range, Margin));
    }

    // 🔴 이 구간(2.5~3.0)이 히스테리시스의 존재 이유다 — 여기서 깜빡이면 진입 트리거가
    //    매 프레임 다시 걸려 몹이 영원히 공격하지 못한다.
    [Test]
    public void 경계_구간에서_진입은_한_번만()
    {
        bool inRange = MonsterEngagePolicy.IsInAttackRange(false, 2.4f, Range, Margin);
        Assert.IsTrue(inRange, "2.4m 는 진입");

        // 2.4 ↔ 2.8 을 오가도 "밖"으로 떨어지지 않으므로 재진입이 발생하지 않는다.
        inRange = MonsterEngagePolicy.IsInAttackRange(inRange, 2.8f, Range, Margin);
        Assert.IsTrue(inRange);
        inRange = MonsterEngagePolicy.IsInAttackRange(inRange, 2.4f, Range, Margin);
        Assert.IsTrue(inRange);
    }

    [Test]
    public void 완전히_벗어났다_돌아오면_다시_진입()
    {
        bool inRange = MonsterEngagePolicy.IsInAttackRange(false, 2f, Range, Margin);
        Assert.IsTrue(inRange);

        inRange = MonsterEngagePolicy.IsInAttackRange(inRange, 5f, Range, Margin);
        Assert.IsFalse(inRange, "여유를 넘겼으면 이탈");

        inRange = MonsterEngagePolicy.IsInAttackRange(inRange, 2f, Range, Margin);
        Assert.IsTrue(inRange, "다시 들어오면 재진입 — 지연도 다시 걸린다");
    }

    [Test]
    public void 여유가_음수여도_사거리보다_좁아지지_않는다()
    {
        Assert.IsTrue(MonsterEngagePolicy.IsInAttackRange(true, Range, Range, -3f));
    }
}
