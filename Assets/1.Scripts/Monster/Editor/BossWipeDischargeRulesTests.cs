using NUnit.Framework;

/// <summary>
/// 전멸기 판정 규칙(할 일 105 후속 — Docs/tech/handoff-boss-lethal-invuln-bypass.md).
/// · 전멸기 = 방 전체(AllMask) + 즉사 + 무적으로 못 피함(데이터 칸 1). 안전 칸이 하나라도 있으면 칸 판정 그대로.
/// · 사망 처리 대상 = 살아 있고 23호가 쥐고 있지 않은 사람.
/// </summary>
public sealed class BossWipeDischargeRulesTests
{
    [Test]
    public void AllMask_Lethal_Unavoidable_IsWipe()
    {
        Assert.That(BossWipeDischargeRules.IsWipe(BossTileGrid.AllMask, lethal: true, unavoidable: true), Is.True);
    }

    [Test]
    public void SafeTilesLeft_NotWipe()
    {
        // 북진 압박 1회차 — 안전 구역을 뺀 마스크. 칸 위 사람만 무적 무시 즉사 피해(안전 칸 사람은 산다).
        ulong withSafe = BossTileGrid.AllMask & ~1UL;
        Assert.That(BossWipeDischargeRules.IsWipe(withSafe, lethal: true, unavoidable: true), Is.False);
    }

    [Test]
    public void DataFieldOff_NotWipe()
    {
        // 0 = 예전처럼 칸 판정 즉사 피해 — 무적이면 피한다.
        Assert.That(BossWipeDischargeRules.IsWipe(BossTileGrid.AllMask, lethal: true, unavoidable: false), Is.False);
    }

    [Test]
    public void NotLethal_NotWipe()
    {
        // ④ raidSelfDestructBlastLethal 끔 = 일반 피해 대폭발, 전멸기가 아니다.
        Assert.That(BossWipeDischargeRules.IsWipe(BossTileGrid.AllMask, lethal: false, unavoidable: true), Is.False);
    }

    [Test]
    public void EmptyMask_NotWipe()
    {
        Assert.That(BossWipeDischargeRules.IsWipe(0UL, lethal: true, unavoidable: true), Is.False);
    }

    [Test]
    public void Alive_NotHeld_Killed()
    {
        Assert.That(BossWipeDischargeRules.ShouldKill(alive: true, heldByBoss: false), Is.True);
    }

    [Test]
    public void HeldByBoss_Spared()
    {
        Assert.That(BossWipeDischargeRules.ShouldKill(alive: true, heldByBoss: true), Is.False);
    }

    [Test]
    public void NotAlive_Skipped()
    {
        // 쓰러짐(사망 연출)·영혼·완전 사망 — 이미 죽었다.
        Assert.That(BossWipeDischargeRules.ShouldKill(alive: false, heldByBoss: false), Is.False);
        Assert.That(BossWipeDischargeRules.ShouldKill(alive: false, heldByBoss: true), Is.False);
    }
}
