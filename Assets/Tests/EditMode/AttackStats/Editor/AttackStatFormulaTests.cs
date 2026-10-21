using System;
using NUnit.Framework;

// 할 일 83 — 플레이어 공격 스탯 공식(기본%·전체%·가하는 피해%)과 서버 2차 검증 판정.
public sealed class AttackStatFormulaTests
{
    [Test]
    public void FinalAttack_AddsWithinStage_MultipliesAcrossStages()
    {
        // 기본 100, 기본 +10%·+20%, 전체 +50% → 100 × 1.3 × 1.5
        Assert.That(AttackStatFormula.FinalAttack(100, 0.1f + 0.2f, 0.5f), Is.EqualTo(195));
    }

    [Test]
    public void Snapshot_AppliesDealtDamageAfterCoefficientAndFlat()
    {
        // (round(195 × 1.8) + 0) × 1.2 = 421.2
        Assert.That(AttackStatFormula.Snapshot(195, 1.8f, 0, 0.2f), Is.EqualTo(421));
    }

    [Test]
    public void Snapshot_WithoutDealtDamage_MatchesPreviousJudgeFormula()
    {
        // 이전 판정식 max(0, round(공격력 × 계수) + 고정값) 과 같아야 한다 — 수치 회귀 없음.
        Assert.That(AttackStatFormula.Snapshot(130, 1.8f, 10, 0f), Is.EqualTo(244));
        Assert.That(AttackStatFormula.RawSnapshot(130, 1.8f, 10), Is.EqualTo(244));
    }

    [TestCase(1.2f, 1, 0.2f)]
    [TestCase(1.2f, 2, 0.4f)]
    [TestCase(0.8f, 1, -0.2f)]
    [TestCase(1f, 3, 0f)]
    public void PercentOf_StacksAdd(float magnitude, int stacks, float expected)
    {
        Assert.That(AttackStatFormula.PercentOf(magnitude, stacks), Is.EqualTo(expected).Within(1e-5f));
    }

    [Test]
    public void NegativeSums_ClampToZero()
    {
        Assert.That(AttackStatFormula.FinalAttack(100, -1.5f, 0f), Is.EqualTo(0));
        Assert.That(AttackStatFormula.Snapshot(100, 1f, 0, -2f), Is.EqualTo(0));
    }

    [Test]
    public void ShouldWarn_SameReport_Never()
    {
        var report = new AttackStatReport(195, 200);
        Assert.That(AttackStatFormula.ShouldWarn(report, report, 10f, 9.9f, 0.5f), Is.False);
    }

    [Test]
    public void ShouldWarn_MismatchInsideGrace_IsReplicationDelay()
    {
        var server = new AttackStatReport(195, 0);
        var client = new AttackStatReport(130, 0);
        Assert.That(AttackStatFormula.ShouldWarn(server, client, 10f, 9.8f, 0.5f), Is.False);
    }

    [Test]
    public void ShouldWarn_MismatchAfterGrace_Warns()
    {
        var server = new AttackStatReport(195, 0);
        Assert.That(AttackStatFormula.ShouldWarn(server, new AttackStatReport(130, 0), 10f, 9f, 0.5f), Is.True);
        Assert.That(AttackStatFormula.ShouldWarn(server, new AttackStatReport(195, 200), 10f, 9f, 0.5f), Is.True);
    }

    [Test]
    public void PlayerAttackStats_SumsRegisteredSources_AndMarksInputChange()
    {
        var stats = new PlayerAttackStats(() => 100, null);
        var source = new FakeSource { BaseAttackPercent = 0.3f, TotalAttackPercent = 0.5f, DealtDamagePercent = 0.2f };

        stats.Register(source);

        Assert.That(stats.FinalAttackDamage, Is.EqualTo(195));
        Assert.That(stats.ResolveDamage(1.8f, 0), Is.EqualTo(421));
        Assert.That(stats.CreateReport(), Is.EqualTo(new AttackStatReport(195, 200)));
        Assert.That(float.IsNegativeInfinity(stats.LastInputChangedTime), Is.False);

        stats.Unregister(source);
        Assert.That(stats.FinalAttackDamage, Is.EqualTo(100));
        stats.Dispose();
    }

    private sealed class FakeSource : IAttackStatSource
    {
        public float BaseAttackPercent { get; set; }
        public float TotalAttackPercent { get; set; }
        public float DealtDamagePercent { get; set; }
        public event Action Changed { add { } remove { } }
    }
}
