using NUnit.Framework;
using UnityEngine;

/// <summary>
/// `PlayerUnitCollisionRules` 순수 계산 테스트 — 장애물 마스크·겹침 밀어내기 상한. (할 일 11)
/// </summary>
public sealed class PlayerUnitCollisionRulesTests
{
    private const int Default = 1 << 0;
    private const int Player = 1 << 6;
    private const int Wall = 1 << 7;
    private const int Enemy = 1 << 8;
    private const int Soul = 1 << 16;
    private const int Base = Default | Wall | Enemy;

    private static int Mask(bool isSoul, bool blockOtherPlayers, bool passThroughUnits) =>
        PlayerUnitCollisionRules.ResolveObstacleMask(
            Base | Player | Soul, Player, Soul, Enemy, isSoul, blockOtherPlayers, passThroughUnits);

    [Test]
    public void Alive_BlockOtherPlayers_BlocksEnemyAndPlayerButNotSoul()
    {
        int mask = Mask(isSoul: false, blockOtherPlayers: true, passThroughUnits: false);

        Assert.That(mask & Enemy, Is.Not.Zero);
        Assert.That(mask & Player, Is.Not.Zero);
        Assert.That(mask & Soul, Is.Zero);
        Assert.That(mask & Wall, Is.Not.Zero);
    }

    [Test]
    public void Alive_WithoutBlockOtherPlayers_PassesPlayers()
    {
        int mask = Mask(isSoul: false, blockOtherPlayers: false, passThroughUnits: false);

        Assert.That(mask & Player, Is.Zero);
        Assert.That(mask & Enemy, Is.Not.Zero);
    }

    [Test]
    public void Soul_PassesPlayers_EvenWhenBlockOtherPlayers()
    {
        int mask = Mask(isSoul: true, blockOtherPlayers: true, passThroughUnits: false);

        Assert.That(mask & Player, Is.Zero);
        Assert.That(mask & Soul, Is.Zero);
    }

    [Test]
    public void PassThroughUnits_RemovesEnemyAndPlayer_KeepsWalls()
    {
        int mask = Mask(isSoul: false, blockOtherPlayers: true, passThroughUnits: true);

        Assert.That(mask & Enemy, Is.Zero);
        Assert.That(mask & Player, Is.Zero);
        Assert.That(mask, Is.EqualTo(Default | Wall));
    }

    [Test]
    public void AccumulatePenetration_KeepsOnlyPlanarComponent()
    {
        Vector3 sum = PlayerUnitCollisionRules.AccumulatePenetration(
            Vector3.zero, new Vector3(0.6f, 0.8f, 0f), 0.5f);

        Assert.That(sum.x, Is.EqualTo(0.3f).Within(1e-5f));
        Assert.That(sum.y, Is.EqualTo(0f));
        Assert.That(sum.z, Is.EqualTo(0f));
    }

    [Test]
    public void AccumulatePenetration_SumsMultipleUnits()
    {
        Vector3 sum = PlayerUnitCollisionRules.AccumulatePenetration(Vector3.zero, Vector3.right, 0.2f);
        sum = PlayerUnitCollisionRules.AccumulatePenetration(sum, Vector3.forward, 0.1f);

        Assert.That(sum.x, Is.EqualTo(0.2f).Within(1e-5f));
        Assert.That(sum.z, Is.EqualTo(0.1f).Within(1e-5f));
    }

    [Test]
    public void AccumulatePenetration_IgnoresNonPositiveDistance()
    {
        Vector3 sum = PlayerUnitCollisionRules.AccumulatePenetration(Vector3.one, Vector3.right, 0f);

        Assert.That(sum, Is.EqualTo(Vector3.one));
    }

    [Test]
    public void SeparationStep_ShortPenetration_ResolvesInOneTick()
    {
        Vector3 step = PlayerUnitCollisionRules.ComputeSeparationStep(new Vector3(0.05f, 0f, 0f), 5f, 1f / 30f);

        Assert.That(step.x, Is.EqualTo(0.05f).Within(1e-5f));
    }

    [Test]
    public void SeparationStep_DeepPenetration_IsCappedBySpeedTimesDelta()
    {
        Vector3 step = PlayerUnitCollisionRules.ComputeSeparationStep(new Vector3(0f, 0f, -0.48f), 5f, 0.02f);

        Assert.That(step.magnitude, Is.EqualTo(0.1f).Within(1e-5f));
        Assert.That(step.z, Is.LessThan(0f));
        Assert.That(step.x, Is.EqualTo(0f));
    }

    [Test]
    public void SeparationStep_DropsVerticalComponent()
    {
        Vector3 step = PlayerUnitCollisionRules.ComputeSeparationStep(new Vector3(0.03f, 0.5f, 0.04f), 10f, 1f);

        Assert.That(step.y, Is.EqualTo(0f));
        Assert.That(step.magnitude, Is.EqualTo(0.05f).Within(1e-5f));
    }

    [Test]
    public void SeparationStep_TinyPenetration_IsIgnored()
    {
        Vector3 step = PlayerUnitCollisionRules.ComputeSeparationStep(
            new Vector3(PlayerUnitCollisionRules.MinSeparationDistance * 0.5f, 0f, 0f), 5f, 0.02f);

        Assert.That(step, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void DashEnds_WhenCutShortByUnit()
    {
        Assert.That(PlayerUnitCollisionRules.ShouldEndDashOnUnitBlock(0.4f, 0.15f, unitBlocksRemaining: true), Is.True);
        Assert.That(PlayerUnitCollisionRules.ShouldEndDashOnUnitBlock(0.4f, 0f, unitBlocksRemaining: true), Is.True);
    }

    [Test]
    public void DashContinues_WhenCutShortByWall()
    {
        Assert.That(PlayerUnitCollisionRules.ShouldEndDashOnUnitBlock(0.4f, 0f, unitBlocksRemaining: false), Is.False);
    }

    [Test]
    public void DashContinues_WhenFullMoveApplied()
    {
        Assert.That(PlayerUnitCollisionRules.ShouldEndDashOnUnitBlock(0.4f, 0.4f, unitBlocksRemaining: true), Is.False);
        Assert.That(PlayerUnitCollisionRules.ShouldEndDashOnUnitBlock(
            0.4f, 0.4f - PlayerUnitCollisionRules.DashBlockedTolerance * 0.5f, unitBlocksRemaining: true), Is.False);
    }

    [Test]
    public void DashContinues_WhenNothingRequested()
    {
        Assert.That(PlayerUnitCollisionRules.ShouldEndDashOnUnitBlock(0f, 0f, unitBlocksRemaining: true), Is.False);
    }

    [Test]
    public void SeparationStep_ZeroSpeedOrDelta_DoesNothing()
    {
        Vector3 penetration = new Vector3(0.3f, 0f, 0f);

        Assert.That(PlayerUnitCollisionRules.ComputeSeparationStep(penetration, 0f, 0.02f), Is.EqualTo(Vector3.zero));
        Assert.That(PlayerUnitCollisionRules.ComputeSeparationStep(penetration, 5f, 0f), Is.EqualTo(Vector3.zero));
    }

    // ── 대시 유닛 관통(할 일 209) ──

    [Test]
    public void DashPassable_OnlyNormalRankMonsters()
    {
        Assert.That(PlayerUnitCollisionRules.IsDashPassableUnit(true, MonsterRank.Normal), Is.True);
        Assert.That(PlayerUnitCollisionRules.IsDashPassableUnit(true, MonsterRank.MidBoss), Is.False);
        Assert.That(PlayerUnitCollisionRules.IsDashPassableUnit(true, MonsterRank.Boss), Is.False);
        Assert.That(PlayerUnitCollisionRules.IsDashPassableUnit(false, MonsterRank.Normal), Is.False); // 파일런·허수아비 등 몬스터 아닌 Enemy 몸
    }

    [Test]
    public void DashPassLimit_IsTwo()
    {
        Assert.That(PlayerUnitCollisionRules.DashSmallMonsterPassLimit, Is.EqualTo(2));
    }

    [Test]
    public void DashUnitPass_PassesTwoNormalMonsters_StopsAtThird()
    {
        var pass = new PlayerDashUnitPass();
        pass.Begin(PlayerUnitCollisionRules.DashSmallMonsterPassLimit);

        Assert.That(pass.TryPass(1, passable: true), Is.True);
        Assert.That(pass.TryPass(2, passable: true), Is.True);
        Assert.That(pass.TryPass(3, passable: true), Is.False);
        Assert.That(pass.PassedCount, Is.EqualTo(2));
        Assert.That(pass.IsPassed(3), Is.False);
    }

    [Test]
    public void DashUnitPass_SameUnitCountsOnce()
    {
        var pass = new PlayerDashUnitPass();
        pass.Begin(2);

        Assert.That(pass.TryPass(7, true), Is.True);
        Assert.That(pass.TryPass(7, true), Is.True);
        Assert.That(pass.PassedCount, Is.EqualTo(1));
        Assert.That(pass.IsPassed(7), Is.True);
    }

    [Test]
    public void DashUnitPass_NonPassableBlocks_WithoutUsingBudget()
    {
        var pass = new PlayerDashUnitPass();
        pass.Begin(2);

        Assert.That(pass.TryPass(9, passable: false), Is.False); // 보스·중간보스
        Assert.That(pass.PassedCount, Is.Zero);
        Assert.That(pass.TryPass(1, true), Is.True);
        Assert.That(pass.TryPass(2, true), Is.True);
    }

    [Test]
    public void DashUnitPass_Inactive_PassesNothing()
    {
        var pass = new PlayerDashUnitPass();

        Assert.That(pass.TryPass(1, true), Is.False);
        Assert.That(pass.IsPassed(1), Is.False);

        pass.Begin(2);
        pass.TryPass(1, true);
        pass.End();

        Assert.That(pass.IsActive, Is.False);
        Assert.That(pass.IsPassed(1), Is.False);
        Assert.That(pass.PassedCount, Is.Zero);
    }

    [Test]
    public void DashUnitPass_BeginResetsPreviousDash()
    {
        var pass = new PlayerDashUnitPass();
        pass.Begin(2);
        pass.TryPass(1, true);
        pass.TryPass(2, true);

        pass.Begin(2);

        Assert.That(pass.PassedCount, Is.Zero);
        Assert.That(pass.TryPass(3, true), Is.True);
    }

    [Test]
    public void DashUnitPass_CopyIsIndependent()
    {
        var live = new PlayerDashUnitPass();
        live.Begin(2);
        live.TryPass(1, true);

        var copy = new PlayerDashUnitPass();
        copy.CopyFrom(live);
        Assert.That(copy.IsActive, Is.True);
        Assert.That(copy.IsPassed(1), Is.True);
        Assert.That(copy.TryPass(2, true), Is.True);
        Assert.That(copy.TryPass(3, true), Is.False);

        Assert.That(live.PassedCount, Is.EqualTo(1)); // 예측이 진짜 장부를 건드리지 않는다
        Assert.That(live.IsPassed(2), Is.False);
    }
}
