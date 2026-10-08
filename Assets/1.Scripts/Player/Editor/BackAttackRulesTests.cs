using NUnit.Framework;
using UnityEngine;

// PLAN-assassin A11 — 백어택 판정·적용(character_assassin.md §4.1).
public sealed class BackAttackRulesTests
{
    const float BackHalfAngle = 60f; // BossDataSO.backAttackAngle 기본값 — 후방 총 120°

    static bool Behind(Vector3 attacker) =>
        BackAttackRules.IsBehind(Vector3.forward, Vector3.zero, attacker, BackHalfAngle);

    // ── 후방 판정(보스 정면 +Z) ──

    [Test]
    public void DirectlyBehindIsBackAttack() => Assert.That(Behind(new Vector3(0f, 0f, -3f)), Is.True);

    [Test]
    public void FrontAndSideAreNotBackAttack()
    {
        Assert.That(Behind(new Vector3(0f, 0f, 3f)), Is.False);
        Assert.That(Behind(new Vector3(3f, 0f, 0f)), Is.False);  // 정측면 = 90°
    }

    [Test]
    public void BoundaryIsInclusiveAt120Degrees()
    {
        Vector3 inside = Quaternion.Euler(0f, 121f, 0f) * Vector3.forward * 3f;
        Vector3 outside = Quaternion.Euler(0f, 119f, 0f) * Vector3.forward * 3f;
        Assert.That(Behind(inside), Is.True);
        Assert.That(Behind(outside), Is.False);
        Assert.That(Behind(Quaternion.Euler(0f, -121f, 0f) * Vector3.forward * 3f), Is.True); // 좌우 대칭
    }

    [Test]
    public void HeightIsIgnored() => Assert.That(Behind(new Vector3(0f, 5f, -3f)), Is.True);

    [Test]
    public void FollowsTargetFacing()
    {
        // 보스가 +X 를 보면 -X 쪽이 후방이다 — 같은 공격자 위치라도 보스 방향에 따라 바뀐다.
        Assert.That(BackAttackRules.IsBehind(Vector3.right, Vector3.zero, new Vector3(-3f, 0f, 0f), BackHalfAngle), Is.True);
        Assert.That(BackAttackRules.IsBehind(Vector3.right, Vector3.zero, new Vector3(0f, 0f, -3f), BackHalfAngle), Is.False);
    }

    [Test]
    public void OverlappingAttackerIsNotBehind() => Assert.That(Behind(Vector3.zero), Is.False);

    // ── 적용 조건 ──

    [Test]
    public void BossBehindAppliesOnlyWithMultiplierAboveOne()
    {
        Assert.That(BackAttackRules.Applies(1.2f, false, true, true), Is.True);
        Assert.That(BackAttackRules.Applies(1f, false, true, true), Is.False);   // 다른 공격(기본값)
        Assert.That(BackAttackRules.Applies(0f, true, true, true), Is.False);    // default 구조체
    }

    [Test]
    public void NormalMonsterHasNoPositionalBackAttack() =>
        Assert.That(BackAttackRules.Applies(1.2f, false, false, true), Is.False);

    [Test]
    public void ForcedAppliesRegardlessOfPositionAndRank()
    {
        Assert.That(BackAttackRules.Applies(1.2f, true, false, false), Is.True); // 일반 몹
        Assert.That(BackAttackRules.Applies(1.2f, true, true, false), Is.True);  // 보스 정면
    }

    [Test]
    public void BossFrontWithoutForceDoesNotApply() =>
        Assert.That(BackAttackRules.Applies(1.2f, false, true, false), Is.False);

    // ── 배율 ──

    [Test]
    public void MultiplierRoundsAndClamps()
    {
        Assert.That(BackAttackRules.ApplyMultiplier(100, 1.2f), Is.EqualTo(120));
        Assert.That(BackAttackRules.ApplyMultiplier(5, 1.2f), Is.EqualTo(6));
        Assert.That(BackAttackRules.ApplyMultiplier(0, 1.2f), Is.EqualTo(0));
        Assert.That(BackAttackRules.ApplyMultiplier(int.MaxValue, 1.2f), Is.EqualTo(int.MaxValue));
    }

    // ── AttackInfo 기본값 — 기존 호출 무변화 ──

    [Test]
    public void AttackInfoDefaultsToNoBackAttack()
    {
        var info = new AttackInfo(10, AttackType.Default);
        Assert.That(info.backAttackMultiplier, Is.EqualTo(1f));
        Assert.That(info.forceBackAttack, Is.False);
    }
}
