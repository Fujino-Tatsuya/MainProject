using System.Collections.Generic;
using BeaverLobby.Player.Dash;
using NUnit.Framework;

/// <summary>
/// 쿨 시계 배율 연결(할 일 91 T8) — 배율 = <see cref="StatusEffectType.CooldownRateModifier"/> 곱(<see cref="StatusEffectController.StatMultiplierOf"/>).
/// <see cref="Player.CooldownRate"/> 가 같은 식을 복제된 목록에 쓰고, <see cref="PlayerCooldownClock"/> 이 프레임마다 그 값으로 쌓는다.
/// 여기서는 NetworkList 대신 리스트로 같은 식을 돌려 스킬 쿨·대시 충전·가붕이 패시브 쿨이 상태이상 배율대로 차는지 본다.
/// </summary>
public sealed class CooldownRateWiringTests
{
    private const StatusEffectType Rate = StatusEffectType.CooldownRateModifier;

    private readonly List<StatusEffectInstance> effects = new List<StatusEffectInstance>();
    private CooldownClock clock;
    private double source;

    [SetUp]
    public void SetUp()
    {
        effects.Clear();
        clock = new CooldownClock();
        source = 100.0;
        clock.AdvanceTo(source, CurrentRate());
    }

    [Test]
    public void Multiplier_NoneIsOne_OneIsItsMagnitude_TwoMultiply()
    {
        Assert.That(CurrentRate(), Is.EqualTo(1f), "없음 = 1");

        effects.Add(Effect(Rate, 2f, sourceId: 10));
        Assert.That(CurrentRate(), Is.EqualTo(2f).Within(1e-6f), "하나 = 그 배율");

        effects.Add(Effect(Rate, 1.5f, sourceId: 11));
        Assert.That(CurrentRate(), Is.EqualTo(3f).Within(1e-6f), "출처 둘 = 곱");
    }

    [Test]
    public void Multiplier_IgnoresOtherTypes_AndStacksArePowers()
    {
        effects.Add(Effect(StatusEffectType.MoveSpeedFinalMultiplier, 2f, sourceId: 10));
        effects.Add(Effect(StatusEffectType.AttackSpeedFinalMultiplier, 2f, sourceId: 10));
        Assert.That(CurrentRate(), Is.EqualTo(1f), "제압 가속의 다른 두 배율은 쿨 시계에 안 들어간다");

        effects.Add(Effect(Rate, 2f, sourceId: 10, stacks: 2));
        Assert.That(CurrentRate(), Is.EqualTo(4f).Within(1e-6f), "스택 = magnitude^스택(GetStatMultiplier 와 같다)");
    }

    [Test]
    public void SkillCooldown_RateTwo_EndsInHalfRealTime()
    {
        var ledger = new PlayerSkillCooldownLedger(1);
        effects.Add(Effect(Rate, 2f, sourceId: 10));
        ledger.Start(0, (float)clock.Now, 10f);

        Step(4.9);
        Assert.That(ledger.IsReady(0, (float)clock.Now), Is.False);
        Step(0.2);
        Assert.That(ledger.IsReady(0, (float)clock.Now), Is.True, "10초 쿨 = 실시간 5초");
    }

    [Test]
    public void DashCharge_RateTwo_RechargesInHalfRealTime()
    {
        var ledger = new DashChargeLedger(1, 2.0, 1, clock.Now);
        effects.Add(Effect(Rate, 2f, sourceId: 10));
        Assert.That(ledger.TryConsume(clock.Now), Is.True);

        Step(0.9);
        ledger.Advance(clock.Now);
        Assert.That(ledger.Count, Is.Zero);
        Step(0.2);
        ledger.Advance(clock.Now);
        Assert.That(ledger.Count, Is.EqualTo(1), "충전 2초 = 실시간 1초");
    }

    [Test]
    public void PaladinPassiveCooldown_RateTwo_EndsInHalfRealTime()
    {
        // FirstMeleePassive: readyAt = 쿨 시계(스킬 시계 공유) + 쿨.
        effects.Add(Effect(Rate, 2f, sourceId: 10));
        double readyAt = clock.Now + 30.0;

        Step(14.9);
        Assert.That(PassiveCooldownSync.Remaining(readyAt, clock.Now), Is.GreaterThan(0f));
        Step(0.2);
        Assert.That(PassiveCooldownSync.Remaining(readyAt, clock.Now), Is.Zero, "30초 쿨 = 실시간 15초");
    }

    [Test]
    public void EffectRemovedMidway_RateReturnsToOne_FromNextInterval()
    {
        var ledger = new PlayerSkillCooldownLedger(1);
        effects.Add(Effect(Rate, 2f, sourceId: 10));
        ledger.Start(0, (float)clock.Now, 10f);

        Step(3.0);                     // 3 × 2 = 6
        effects.Clear();               // 만료·제거(스윕 → 복제) — 다음 구간부터 1
        Assert.That(CurrentRate(), Is.EqualTo(1f));
        Step(3.0);                     // 3 × 1 = 3
        Assert.That(ledger.GetRemaining(0, (float)clock.Now), Is.EqualTo(1f).Within(1e-4f), "6 + 3 = 9, 남은 1초");

        Step(1.1);
        Assert.That(ledger.IsReady(0, (float)clock.Now), Is.True);
    }

    // PlayerCooldownClock.Sync 와 같은 순서 — 이번 구간 배율을 읽어 원천 증분에 곱한다.
    private void Step(double seconds)
    {
        source += seconds;
        clock.AdvanceTo(source, CurrentRate());
    }

    private float CurrentRate() => StatusEffectController.StatMultiplierOf(effects.Count, i => effects[i], Rate);

    private static StatusEffectInstance Effect(StatusEffectType type, float magnitude, ulong sourceId, int stacks = 1) =>
        new StatusEffectInstance { type = type, magnitude = magnitude, sourceId = sourceId, stackCount = stacks };
}
