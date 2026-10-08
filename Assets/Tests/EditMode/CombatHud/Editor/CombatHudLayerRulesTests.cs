using NUnit.Framework;

public sealed class CombatHudLayerRulesTests
{
    private const CombatHudLayers Playing =
        CombatHudLayers.Persistent | CombatHudLayers.Combat | CombatHudLayers.BossInfo;

    [Test]
    public void Alive_ShowsPersistentCombatBossInfo()
    {
        Assert.That(CombatHudLayerRules.VisibleLayers(PlayerLifeState.Alive), Is.EqualTo(Playing));
    }

    [Test]
    public void DeadPresentation_ShowsPersistentOnly()
    {
        Assert.That(CombatHudLayerRules.VisibleLayers(PlayerLifeState.DeadPresentation),
            Is.EqualTo(CombatHudLayers.Persistent));
    }

    [Test]
    public void Soul_ShowsSameLayersAsAlive()
    {
        Assert.That(CombatHudLayerRules.VisibleLayers(PlayerLifeState.Soul), Is.EqualTo(Playing));
    }

    [Test]
    public void PermanentDead_ShowsPersistentBossInfoSpectate()
    {
        Assert.That(CombatHudLayerRules.VisibleLayers(PlayerLifeState.PermanentDead),
            Is.EqualTo(CombatHudLayers.Persistent | CombatHudLayers.BossInfo | CombatHudLayers.Spectate));
    }

    [Test]
    public void EveryState_KeepsPersistentVisible()
    {
        foreach (PlayerLifeState state in System.Enum.GetValues(typeof(PlayerLifeState)))
            Assert.That(CombatHudLayerRules.VisibleLayers(state) & CombatHudLayers.Persistent,
                Is.EqualTo(CombatHudLayers.Persistent), state.ToString());
    }

    [Test]
    public void SoulOverrides_OnlyInSoul()
    {
        Assert.That(CombatHudLayerRules.ShowsSoulOverrides(PlayerLifeState.Soul), Is.True);
        Assert.That(CombatHudLayerRules.ShowsSoulOverrides(PlayerLifeState.Alive), Is.False);
        Assert.That(CombatHudLayerRules.ShowsSoulOverrides(PlayerLifeState.DeadPresentation), Is.False);
        Assert.That(CombatHudLayerRules.ShowsSoulOverrides(PlayerLifeState.PermanentDead), Is.False);
    }

    [Test]
    public void ResolveDisplayState_NoPlayerIsPermanentDead()
    {
        Assert.That(CombatHudLayerRules.ResolveDisplayState(false, false, PlayerLifeState.Alive),
            Is.EqualTo(PlayerLifeState.PermanentDead));
    }

    [Test]
    public void ResolveDisplayState_PlayerWithoutLifeCycleIsAlive()
    {
        Assert.That(CombatHudLayerRules.ResolveDisplayState(true, false, PlayerLifeState.PermanentDead),
            Is.EqualTo(PlayerLifeState.Alive));
    }

    [Test]
    public void ResolveDisplayState_UsesLifeCycleState()
    {
        Assert.That(CombatHudLayerRules.ResolveDisplayState(true, true, PlayerLifeState.Soul),
            Is.EqualTo(PlayerLifeState.Soul));
    }
}
