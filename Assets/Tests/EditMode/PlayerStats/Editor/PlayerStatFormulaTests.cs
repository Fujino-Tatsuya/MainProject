using System;
using NUnit.Framework;

// 할 일 83 — 플레이어 스탯 공식(5종 기본%·전체%, 공격력 가하는 피해%)과 서버 2차 검증 판정.
public sealed class PlayerStatFormulaTests
{
    [Test]
    public void FinalInt_AddsWithinStage_MultipliesAcrossStages()
    {
        // 기본 100, 기본 +10%·+20%, 전체 +50% → 100 × 1.3 × 1.5
        Assert.That(PlayerStatFormula.FinalInt(100, 0.1f + 0.2f, 0.5f), Is.EqualTo(195));
    }

    [Test]
    public void Final_FloatStat_NotRounded()
    {
        // 이동속도 5, 기본 +10%, 전체 −30%(감속) → 5 × 1.1 × 0.7
        Assert.That(PlayerStatFormula.Final(5f, 0.1f, -0.3f), Is.EqualTo(3.85f).Within(1e-4f));
    }

    [Test]
    public void Snapshot_AppliesDealtDamageAfterCoefficientAndFlat()
    {
        // (round(195 × 1.8) + 0) × 1.2 = 421.2
        Assert.That(PlayerStatFormula.Snapshot(195, 1.8f, 0, 0.2f), Is.EqualTo(421));
    }

    [Test]
    public void Snapshot_WithoutDealtDamage_MatchesPreviousJudgeFormula()
    {
        // 이전 판정식 max(0, round(공격력 × 계수) + 고정값) 과 같아야 한다 — 수치 회귀 없음.
        Assert.That(PlayerStatFormula.Snapshot(130, 1.8f, 10, 0f), Is.EqualTo(244));
        Assert.That(PlayerStatFormula.RawSnapshot(130, 1.8f, 10), Is.EqualTo(244));
    }

    [TestCase(1.2f, 1, 0.2f)]
    [TestCase(1.2f, 2, 0.4f)]
    [TestCase(0.8f, 1, -0.2f)]
    [TestCase(1f, 3, 0f)]
    public void PercentOf_StacksAdd(float magnitude, int stacks, float expected)
    {
        Assert.That(PlayerStatFormula.PercentOf(magnitude, stacks), Is.EqualTo(expected).Within(1e-5f));
    }

    [Test]
    public void NegativeSums_ClampToZero()
    {
        Assert.That(PlayerStatFormula.FinalInt(100, -1.5f, 0f), Is.EqualTo(0));
        Assert.That(PlayerStatFormula.Final(5f, 0f, -2f), Is.EqualTo(0f));
        Assert.That(PlayerStatFormula.Snapshot(100, 1f, 0, -2f), Is.EqualTo(0));
    }

    [Test]
    public void EveryKind_HasDistinctBaseAndTotalStatusTypes()
    {
        StatusEffectType seen = StatusEffectType.None;
        foreach (PlayerStatKind kind in Enum.GetValues(typeof(PlayerStatKind)))
        {
            PlayerStatFormula.StatusTypes(kind, out StatusEffectType basePercent, out StatusEffectType totalPercent);
            Assert.That(basePercent, Is.Not.EqualTo(StatusEffectType.None), kind.ToString());
            Assert.That(totalPercent, Is.Not.EqualTo(StatusEffectType.None), kind.ToString());
            Assert.That(seen & (basePercent | totalPercent), Is.EqualTo(StatusEffectType.None), $"{kind} 타입이 다른 스탯과 겹침");
            Assert.That(StatusEffectCategories.Of(basePercent, 0.5f), Is.EqualTo(StatusEffectCategory.Debuff), $"{kind} 기본% 분류 누락");
            seen |= basePercent | totalPercent;
        }
    }

    // 할 일 91 — 최종 배율 단계: (기본 × (1+Σ기본%) × (1+Σ전체%)) × Π최종배율

    [Test]
    public void FinalMultiplier_Default_KeepsPreviousValues()
    {
        Assert.That(PlayerStatFormula.Final(5f, 0.1f, -0.3f, 1f), Is.EqualTo(PlayerStatFormula.Final(5f, 0.1f, -0.3f)).Within(1e-5f));
        Assert.That(PlayerStatFormula.FinalInt(100, 0.3f, 0.5f, 1f), Is.EqualTo(195));
        Assert.That(PlayerStatFormula.Multiplier(0.1f, 0.2f), Is.EqualTo(1.1f * 1.2f).Within(1e-5f));
    }

    [Test]
    public void FinalMultiplier_Two_DoublesAfterPercentStages()
    {
        // 이동속도 5, 기본 +10%, 전체 +20% → 6.6, 최종 ×2 → 13.2
        Assert.That(PlayerStatFormula.Final(5f, 0.1f, 0.2f, 2f), Is.EqualTo(13.2f).Within(1e-4f));
        Assert.That(PlayerStatFormula.Multiplier(0.1f, 0.2f, 2f), Is.EqualTo(2.64f).Within(1e-4f));
    }

    [Test]
    public void FinalMultiplier_TwoSources_Multiply()
    {
        // 같은 최종 배율끼리는 곱 — StatusEffectController.GetStatMultiplier 가 넘겨주는 값(2 × 1.5 = 3)
        Assert.That(PlayerStatFormula.Final(5f, 0f, 0f, 2f * 1.5f), Is.EqualTo(15f).Within(1e-4f));
        // % 단계였다면 1 + 1.0 + 0.5 = 2.5 로 더해졌을 것 — 곱과 다르다
        Assert.That(PlayerStatFormula.Final(5f, 0f, 0f, 2f * 1.5f), Is.Not.EqualTo(PlayerStatFormula.Final(5f, 0f, 1.5f)).Within(1e-4f));
    }

    [Test]
    public void FinalMultiplierType_OnlyMoveSpeedAndAttackSpeed()
    {
        foreach (PlayerStatKind kind in Enum.GetValues(typeof(PlayerStatKind)))
        {
            StatusEffectType type = PlayerStatFormula.FinalMultiplierType(kind);
            bool expected = kind == PlayerStatKind.MoveSpeed || kind == PlayerStatKind.AttackSpeed;
            Assert.That(type != StatusEffectType.None, Is.EqualTo(expected), kind.ToString());
            if (!expected)
                continue;

            PlayerStatFormula.StatusTypes(kind, out StatusEffectType basePercent, out StatusEffectType totalPercent);
            Assert.That(type & (basePercent | totalPercent), Is.EqualTo(StatusEffectType.None), $"{kind} 최종 배율이 % 단계 타입과 겹침");
        }
    }

    [TestCase(StatusEffectType.MoveSpeedFinalMultiplier)]
    [TestCase(StatusEffectType.AttackSpeedFinalMultiplier)]
    [TestCase(StatusEffectType.CooldownRateModifier)]
    public void FinalMultiplierTypes_AreStatModifiers_BuffAtOrAboveOne(StatusEffectType type)
    {
        // 배율 ≥ 1 = 버프 → 보스방 연출 ClearDebuffsServer 가 지우지 않는다
        Assert.That(StatusEffectCategories.Of(type, 2f), Is.EqualTo(StatusEffectCategory.Buff));
        Assert.That(StatusEffectCategories.Of(type, 1f), Is.EqualTo(StatusEffectCategory.Buff));
        Assert.That(StatusEffectCategories.Of(type, 0.5f), Is.EqualTo(StatusEffectCategory.Debuff));
    }

    [Test]
    public void PlayerStats_WithoutStatusEffects_FinalMultiplierIsOne()
    {
        var stats = new PlayerStats(kind => kind == PlayerStatKind.MoveSpeed ? 5f : 100f, null);
        foreach (PlayerStatKind kind in Enum.GetValues(typeof(PlayerStatKind)))
            Assert.That(stats.FinalMultiplier(kind), Is.EqualTo(1f), kind.ToString());
        Assert.That(stats.FinalMoveSpeed, Is.EqualTo(5f).Within(1e-5f));
        stats.Dispose();
    }

    [Test]
    public void ShouldWarn_SameReport_Never()
    {
        var report = new PlayerStatReport { attackDamage = 195, dealtDamageMilli = 200 };
        Assert.That(PlayerStatFormula.ShouldWarn(report, report, 10f, 9.9f, 0.5f), Is.False);
    }

    [Test]
    public void ShouldWarn_MismatchInsideGrace_IsReplicationDelay()
    {
        var server = new PlayerStatReport { attackDamage = 195 };
        var client = new PlayerStatReport { attackDamage = 130 };
        Assert.That(PlayerStatFormula.ShouldWarn(server, client, 10f, 9.8f, 0.5f), Is.False);
    }

    [Test]
    public void ShouldWarn_AnyStatMismatchAfterGrace_Warns()
    {
        var server = new PlayerStatReport { attackDamage = 195, moveSpeedMilli = 5000, maxHp = 100 };
        var moveSpeedOff = server; moveSpeedOff.moveSpeedMilli = 6000;
        var maxHpOff = server; maxHpOff.maxHp = 120;

        Assert.That(PlayerStatFormula.ShouldWarn(server, moveSpeedOff, 10f, 9f, 0.5f), Is.True);
        Assert.That(PlayerStatFormula.ShouldWarn(server, maxHpOff, 10f, 9f, 0.5f), Is.True);
    }

    [Test]
    public void PlayerStats_SumsRegisteredSources_PerKind_AndMarksInputChange()
    {
        var stats = new PlayerStats(kind => kind == PlayerStatKind.MoveSpeed ? 5f : 100f, null);
        var source = new FakeSource();
        source.Base[(int)PlayerStatKind.AttackDamage] = 0.3f;
        source.Total[(int)PlayerStatKind.AttackDamage] = 0.5f;
        source.Total[(int)PlayerStatKind.MoveSpeed] = 0.2f;
        source.Base[(int)PlayerStatKind.MaxHp] = 0.1f;
        source.Dealt = 0.2f;

        stats.Register(source);

        Assert.That(stats.FinalAttackDamage, Is.EqualTo(195));
        Assert.That(stats.ResolveDamage(1.8f, 0), Is.EqualTo(421));
        Assert.That(stats.FinalMoveSpeed, Is.EqualTo(6f).Within(1e-4f));
        Assert.That(stats.Multiplier(PlayerStatKind.MoveSpeed), Is.EqualTo(1.2f).Within(1e-4f));
        Assert.That(stats.FinalMaxHp, Is.EqualTo(110));
        Assert.That(stats.FinalDefense, Is.EqualTo(100));
        Assert.That(stats.CreateReport().moveSpeedMilli, Is.EqualTo(6000));
        Assert.That(float.IsNegativeInfinity(stats.LastInputChangedTime), Is.False);

        stats.Unregister(source);
        Assert.That(stats.FinalAttackDamage, Is.EqualTo(100));
        stats.Dispose();
    }

    private sealed class FakeSource : IPlayerStatSource
    {
        public readonly float[] Base = new float[5];
        public readonly float[] Total = new float[5];
        public float Dealt;

        public float GetBasePercent(PlayerStatKind kind) => Base[(int)kind];
        public float GetTotalPercent(PlayerStatKind kind) => Total[(int)kind];
        public float DealtDamagePercent => Dealt;
        public event Action Changed { add { } remove { } }
    }
}
