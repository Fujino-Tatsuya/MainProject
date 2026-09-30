using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 보호막 인스턴스 규칙 고정 (PLAN-gunner.md G1, character_gunner.md §6.5 · D8).
/// - 합계 = 인스턴스 합, 만료가 가장 빠른 것부터 소모, 만료 없는 것은 마지막
/// - HolyShield = 같은 출처 교체, GunnerCharge = 합산(지속시간 독립)
/// </summary>
public sealed class HealthShieldTests
{
    const ulong SourceA = 1;
    const ulong SourceB = 2;

    static Health NewHealth() => new Health(100, 0);

    [Test]
    public void GunnerCharge_StacksWithIndependentDurations()
    {
        // 기획서 §6.5 예시: 100(5초) → 2초 뒤 100(5초) → 5초 시점에 첫 100만 사라짐
        Health health = NewHealth();
        health.AddShield(ShieldType.GunnerCharge, SourceA, 100, 5f, now: 0);
        health.AddShield(ShieldType.GunnerCharge, SourceA, 100, 5f, now: 2);
        Assert.That(health.CurrentShield, Is.EqualTo(200));

        var expired = new List<ShieldInstance>();
        Assert.That(health.RemoveExpiredShields(5, expired), Is.True);
        Assert.That(expired.Count, Is.EqualTo(1));
        Assert.That(health.CurrentShield, Is.EqualTo(100));

        Assert.That(health.RemoveExpiredShields(7, expired), Is.True);
        Assert.That(health.HasShield, Is.False);
    }

    [Test]
    public void HolyShield_ReplacesOnlySameSource()
    {
        Health health = NewHealth();
        health.AddShield(ShieldType.GunnerCharge, SourceB, 30, 5f, now: 0);
        health.AddShield(ShieldType.HolyShield, SourceA, 50, 5f, now: 0);
        health.AddShield(ShieldType.HolyShield, SourceA, 50, 5f, now: 1);

        Assert.That(health.CurrentShield, Is.EqualTo(80), "E 재사용은 자기 것만 교체 — 거너 보호막은 남는다");
        Assert.That(health.Shields.Count, Is.EqualTo(2));
    }

    [Test]
    public void Damage_ConsumesEarliestExpiryFirst_InfiniteLast()
    {
        Health health = NewHealth();
        health.AddShield(ShieldType.HolyShield, SourceA, 40, 0f, now: 0);    // 만료 없음
        health.AddShield(ShieldType.GunnerCharge, SourceB, 30, 10f, now: 0); // 10초
        health.AddShield(ShieldType.GunnerCharge, SourceB, 20, 3f, now: 0);  // 3초 — 가장 먼저 소모

        var depleted = new List<ShieldInstance>();
        int absorbed = health.TakeShieldDamage(35, depleted);

        Assert.That(absorbed, Is.EqualTo(35));
        Assert.That(depleted.Count, Is.EqualTo(1));
        Assert.That(depleted[0].amount, Is.EqualTo(0));
        Assert.That(health.CurrentShield, Is.EqualTo(55)); // 3초짜리 20 소진 + 10초짜리 15 남음 + 무한 40
        Assert.That(health.ContainsShield(ShieldType.HolyShield, SourceA), Is.True);

        // HUD 비율 분모 — 깎여도 부여 당시 양은 그대로다
        int granted = 0;
        foreach (ShieldInstance s in health.Shields)
            granted += s.grantedAmount;
        Assert.That(granted, Is.EqualTo(70)); // 남은 두 인스턴스의 부여량 40 + 30
    }

    [Test]
    public void Damage_BeyondTotal_AbsorbsOnlyTotal()
    {
        Health health = NewHealth();
        health.AddShield(ShieldType.GunnerCharge, SourceA, 10, 5f, now: 0);

        int absorbed = health.TakeShieldDamage(25, null);

        Assert.That(absorbed, Is.EqualTo(10));
        Assert.That(health.HasShield, Is.False);
    }

    [Test]
    public void Clear_RemovesAllAndReports()
    {
        Health health = NewHealth();
        health.AddShield(ShieldType.HolyShield, SourceA, 50, 5f, now: 0);
        health.AddShield(ShieldType.GunnerCharge, SourceB, 30, 5f, now: 0);

        var removed = new List<ShieldInstance>();
        health.ClearShields(removed);

        Assert.That(removed.Count, Is.EqualTo(2));
        Assert.That(health.CurrentShield, Is.EqualTo(0));
    }
}
