using NUnit.Framework;
using UnityEngine;

/// <summary>어쌔신 일반 타 매 타 전진(할 일 96) — 방향 선택·모드별 거리/시간·공격속도 배율.</summary>
public sealed class AssassinNormalLungeRulesTests
{
    private static readonly Vector3 Aim = new Vector3(0f, 0f, 1f);

    [Test]
    public void NormalWithMoveInputUsesMoveDirection()
    {
        Vector3 result = AssassinAttackDirectionRules.Resolve(AssassinBasicAttackMode.Normal, new Vector3(2f, 0f, 0f), Aim);
        Assert.That(Vector3.Distance(result, Vector3.right), Is.LessThan(1e-5f));
    }

    [Test]
    public void NormalWithoutMoveInputUsesAim()
    {
        Assert.That(AssassinAttackDirectionRules.Resolve(AssassinBasicAttackMode.Normal, Vector3.zero, Aim), Is.EqualTo(Aim));
    }

    [Test]
    public void NormalWithTinyMoveInputUsesAim()
    {
        Vector3 tiny = new Vector3(0.01f, 0f, 0.01f);
        Assert.That(AssassinAttackDirectionRules.Resolve(AssassinBasicAttackMode.Normal, tiny, Aim), Is.EqualTo(Aim));
    }

    [Test]
    public void NormalMoveDirectionIsFlattened()
    {
        Vector3 result = AssassinAttackDirectionRules.Resolve(AssassinBasicAttackMode.Normal, new Vector3(1f, 5f, 0f), Aim);
        Assert.That(result.y, Is.EqualTo(0f));
        Assert.That(Vector3.Distance(result, Vector3.right), Is.LessThan(1e-5f));
    }

    [Test]
    public void VerticalOnlyMoveInputCountsAsNoInput()
    {
        Assert.That(AssassinAttackDirectionRules.Resolve(AssassinBasicAttackMode.Normal, Vector3.up, Aim), Is.EqualTo(Aim));
    }

    [TestCase(AssassinBasicAttackMode.Enhanced)]
    [TestCase(AssassinBasicAttackMode.Transformed)]
    public void EnhancedAndTransformedIgnoreMoveInput(AssassinBasicAttackMode mode)
    {
        Assert.That(AssassinAttackDirectionRules.Resolve(mode, Vector3.right, Aim), Is.EqualTo(Aim));
    }

    [Test]
    public void DistanceAndDurationPickPairByMode()
    {
        Assert.That(AssassinLungeRules.Distance(AssassinBasicAttackMode.Normal, 0.5f, 1.2f), Is.EqualTo(0.5f));
        Assert.That(AssassinLungeRules.Duration(AssassinBasicAttackMode.Normal, 0.1f, 0.2f), Is.EqualTo(0.1f));
        Assert.That(AssassinLungeRules.Distance(AssassinBasicAttackMode.Enhanced, 0.5f, 1.2f), Is.EqualTo(1.2f));
        Assert.That(AssassinLungeRules.Duration(AssassinBasicAttackMode.Enhanced, 0.1f, 0.2f), Is.EqualTo(0.2f));
        Assert.That(AssassinLungeRules.Distance(AssassinBasicAttackMode.Transformed, 0.5f, 1.2f), Is.EqualTo(0f));
        Assert.That(AssassinLungeRules.Duration(AssassinBasicAttackMode.Transformed, 0.1f, 0.2f), Is.EqualTo(0f));
    }

    [Test]
    public void NegativeValuesClampToZero()
    {
        Assert.That(AssassinLungeRules.Distance(AssassinBasicAttackMode.Normal, -1f, 1.2f), Is.EqualTo(0f));
        Assert.That(AssassinLungeRules.Duration(AssassinBasicAttackMode.Normal, -1f, 0.2f), Is.EqualTo(0f));
    }

    [Test]
    public void AttackSpeedShortensDurationButKeepsDistance()
    {
        // 0.5m / (0.1s ÷ 2배) = 10m/s, 0.02s 틱 = 0.2m
        float duration = PlayerAttackSpeedRules.Duration(0.1f, 2f);
        Assert.That(AssassinLungeRules.StepDistance(0.5f, 0.5f, duration, 0.02f), Is.EqualTo(0.2f).Within(1e-5f));

        float remaining = 0.5f;
        int ticks = 0;
        while (remaining > 0f && ticks < 100)
        {
            remaining -= AssassinLungeRules.StepDistance(remaining, 0.5f, duration, 0.02f);
            ticks++;
        }

        Assert.That(remaining, Is.EqualTo(0f).Within(1e-5f));
        Assert.That(ticks, Is.LessThanOrEqualTo(4)); // 0.05s / 0.02s = 2.5 → 3틱(+오차 1)
    }

    [Test]
    public void DataDefaultsAreShortForwardStep()
    {
        AssassinBasicAttackData data = ScriptableObject.CreateInstance<AssassinBasicAttackData>();
        try
        {
            Assert.That(data.NormalLungeDistance, Is.EqualTo(0.5f));
            Assert.That(data.NormalLungeDuration, Is.EqualTo(0.1f));
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }
}
