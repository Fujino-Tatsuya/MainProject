using NUnit.Framework;
using UnityEngine;

/// <summary>로비 근접 문 열림/닫힘 히스테리시스·진행도·평면 거리 — 할 일 78.</summary>
public sealed class LobbyProximityDoorRulesTests
{
    const float Open = 2f;
    const float Close = 3f;

    static float Sqr(float d) => d * d;

    [Test]
    public void 닫힌_문은_열림_반경_안_경계_포함에서_열린다()
    {
        Assert.That(LobbyProximityDoorRules.NextOpen(false, Sqr(1f), Open, Close), Is.True);
        Assert.That(LobbyProximityDoorRules.NextOpen(false, Sqr(2f), Open, Close), Is.True);
    }

    [Test]
    public void 닫힌_문은_두_반경_사이에서_닫힌_채로_있다()
    {
        Assert.That(LobbyProximityDoorRules.NextOpen(false, Sqr(2.5f), Open, Close), Is.False);
    }

    [Test]
    public void 열린_문은_두_반경_사이에서_열린_채로_있다()
    {
        Assert.That(LobbyProximityDoorRules.NextOpen(true, Sqr(2.5f), Open, Close), Is.True);
        Assert.That(LobbyProximityDoorRules.NextOpen(true, Sqr(3f), Open, Close), Is.True);
    }

    [Test]
    public void 열린_문은_닫힘_반경_밖에서_닫힌다()
    {
        Assert.That(LobbyProximityDoorRules.NextOpen(true, Sqr(3.01f), Open, Close), Is.False);
    }

    [Test]
    public void 대상이_없으면_닫힌다()
    {
        Assert.That(LobbyProximityDoorRules.NextOpen(true, float.PositiveInfinity, Open, Close), Is.False);
        Assert.That(LobbyProximityDoorRules.NextOpen(false, float.PositiveInfinity, Open, Close), Is.False);
    }

    [Test]
    public void 경계를_오가도_한_번만_바뀐다()
    {
        var isOpen = false;
        var changes = 0;
        foreach (var d in new[] { 2.6f, 1.9f, 2.1f, 1.95f, 2.4f, 2.9f, 2.2f, 3.2f, 2.9f, 2.5f })
        {
            var next = LobbyProximityDoorRules.NextOpen(isOpen, Sqr(d), Open, Close);
            if (next != isOpen) changes++;
            isOpen = next;
        }

        Assert.That(changes, Is.EqualTo(2)); // 1.9 에서 열림, 3.2 에서 닫힘
        Assert.That(isOpen, Is.False);
    }

    [Test]
    public void 닫힘_반경이_더_작으면_열림_반경으로_올려_쓴다()
    {
        Assert.That(LobbyProximityDoorRules.NextOpen(true, Sqr(1.9f), Open, 1f), Is.True);
        Assert.That(LobbyProximityDoorRules.NextOpen(true, Sqr(2.1f), Open, 1f), Is.False);
    }

    [Test]
    public void 진행도는_여닫는_시간이_따로다()
    {
        Assert.That(LobbyProximityDoorRules.StepProgress(0f, true, 0.5f, 1f, 0.25f), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(LobbyProximityDoorRules.StepProgress(1f, false, 0.5f, 1f, 0.25f), Is.EqualTo(0.75f).Within(1e-5f));
    }

    [Test]
    public void 진행도는_0과_1을_넘지_않고_0초면_즉시다()
    {
        Assert.That(LobbyProximityDoorRules.StepProgress(0.9f, true, 0.5f, 0.5f, 1f), Is.EqualTo(1f));
        Assert.That(LobbyProximityDoorRules.StepProgress(0.1f, false, 0.5f, 0.5f, 1f), Is.EqualTo(0f));
        Assert.That(LobbyProximityDoorRules.StepProgress(0f, true, 0f, 0.5f, 0f), Is.EqualTo(1f));
        Assert.That(LobbyProximityDoorRules.StepProgress(1f, false, 0.5f, 0f, 0f), Is.EqualTo(0f));
    }

    [Test]
    public void 거리는_평면_거리다()
    {
        Assert.That(LobbyProximityDoorRules.PlanarSqrDistance(new Vector3(3f, 10f, 4f), Vector3.zero), Is.EqualTo(25f).Within(1e-5f));
    }
}
