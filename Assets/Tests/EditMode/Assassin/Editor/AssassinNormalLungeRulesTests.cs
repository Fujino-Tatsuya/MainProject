using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 어쌔신 평타 방향(할 일 137 — 모든 모드 조준, 팔라딘·거너와 같은 공용 규칙)과
/// 일반 타 매 타 전진(할 일 96) 모드별 거리/시간·공격속도 배율.
/// </summary>
public sealed class AssassinNormalLungeRulesTests
{
    private static readonly Vector3 Aim = new Vector3(0f, 0f, 1f);

    // 이동 입력이 조준과 달라도 일반·강타·변신 묶음 모두 조준을 고른다 — 일반 타 전진도 이 방향이다.
    [TestCase(AssassinBasicAttackMode.Normal)]
    [TestCase(AssassinBasicAttackMode.Enhanced)]
    [TestCase(AssassinBasicAttackMode.Transformed)]
    public void MoveInputDifferentFromAimUsesAim(AssassinBasicAttackMode mode)
    {
        Assert.That(AssassinAttackDirectionRules.Resolve(mode, new Vector3(2f, 0f, 0f), Aim), Is.EqualTo(Aim));
        Assert.That(AssassinAttackDirectionRules.Resolve(mode, -Aim, Aim), Is.EqualTo(Aim));
    }

    [TestCase(AssassinBasicAttackMode.Normal)]
    [TestCase(AssassinBasicAttackMode.Enhanced)]
    [TestCase(AssassinBasicAttackMode.Transformed)]
    public void NoMoveInputUsesAim(AssassinBasicAttackMode mode)
    {
        Assert.That(AssassinAttackDirectionRules.Resolve(mode, Vector3.zero, Aim), Is.EqualTo(Aim));
    }

    // 조준이 어느 쪽이든 그대로 따라간다 — 이동 입력은 결과에 영향이 없다.
    [Test]
    public void NormalFollowsAimRegardlessOfMoveInput()
    {
        Vector3 aim = new Vector3(-1f, 0f, 1f).normalized;
        Vector3[] moves = { Vector3.zero, Vector3.right, Vector3.back, new Vector3(1f, 5f, 0f), Vector3.up };
        foreach (Vector3 move in moves)
            Assert.That(AssassinAttackDirectionRules.Resolve(AssassinBasicAttackMode.Normal, move, aim), Is.EqualTo(aim));
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
