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
}
