using NUnit.Framework;

/// <summary>F6 보스전 지정 위치 이동 — 할 일 102(보스 전투 중에만, 연출·낙하 복구 중 금지, 지점 없으면 안 함).</summary>
public sealed class DevBossPointWarpRulesTests
{
    static DevBossPointWarpVerdict Eval(
        bool listening = true,
        bool player = true,
        bool encounter = true,
        BossEncounterPhase phase = BossEncounterPhase.Combat,
        bool falling = false,
        bool point = true)
        => DevBossPointWarpRules.Evaluate(listening, player, encounter, phase, falling, point);

    [Test]
    public void 보스_전투_중이고_지점이_있으면_이동한다()
    {
        Assert.That(Eval(), Is.EqualTo(DevBossPointWarpVerdict.Warp));
    }

    [Test]
    public void 네트워크가_안_떴으면_안_한다()
    {
        Assert.That(Eval(listening: false), Is.EqualTo(DevBossPointWarpVerdict.NetworkNotRunning));
    }

    [Test]
    public void 로컬_플레이어가_없으면_안_한다()
    {
        Assert.That(Eval(player: false), Is.EqualTo(DevBossPointWarpVerdict.NoLocalPlayer));
    }

    [Test]
    public void 보스전_디렉터가_없으면_안_한다()
    {
        Assert.That(Eval(encounter: false), Is.EqualTo(DevBossPointWarpVerdict.NotInBossCombat));
    }

    [TestCase(BossEncounterPhase.Idle)]
    [TestCase(BossEncounterPhase.AwaitingArrival)]
    [TestCase(BossEncounterPhase.Preparing)]
    [TestCase(BossEncounterPhase.Descending)]
    [TestCase(BossEncounterPhase.FailedSafe)]
    public void 전투_외_단계에서는_안_한다(BossEncounterPhase phase)
    {
        Assert.That(Eval(phase: phase), Is.EqualTo(DevBossPointWarpVerdict.NotInBossCombat));
    }

    [Test]
    public void 전투_외_단계는_Combat_하나뿐이다()
    {
        foreach (BossEncounterPhase phase in System.Enum.GetValues(typeof(BossEncounterPhase)))
        {
            DevBossPointWarpVerdict expected = phase == BossEncounterPhase.Combat
                ? DevBossPointWarpVerdict.Warp
                : DevBossPointWarpVerdict.NotInBossCombat;
            Assert.That(Eval(phase: phase), Is.EqualTo(expected), phase.ToString());
        }
    }

    [Test]
    public void 낙하_복구_중이면_안_한다()
    {
        Assert.That(Eval(falling: true), Is.EqualTo(DevBossPointWarpVerdict.FallRecovering));
    }

    [Test]
    public void 지점이_없으면_안_한다()
    {
        Assert.That(Eval(point: false), Is.EqualTo(DevBossPointWarpVerdict.NoWarpPoint));
    }

    [Test]
    public void 앞_조건이_먼저다_네트워크가_없으면_다른_사유는_보지_않는다()
    {
        Assert.That(Eval(listening: false, player: false, encounter: false, falling: true, point: false),
            Is.EqualTo(DevBossPointWarpVerdict.NetworkNotRunning));
    }

    [Test]
    public void 연출_중이면_지점이_없어도_사유는_연출이다()
    {
        Assert.That(Eval(phase: BossEncounterPhase.Preparing, point: false),
            Is.EqualTo(DevBossPointWarpVerdict.NotInBossCombat));
    }
}
