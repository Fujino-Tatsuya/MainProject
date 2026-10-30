using System.Linq;
using NUnit.Framework;

/// <summary>로비 튜토리얼 바닥 경로 묶음 판정 — 할 일 158 C5.</summary>
public sealed class LobbyTutorialUiPathSetTests
{
    [Test]
    public void ShouldShow_OnlyExactId()
    {
        Assert.That(LobbyTutorialPathRules.ShouldShow("Central", "Central"), Is.True);
        Assert.That(LobbyTutorialPathRules.ShouldShow("Central", "central"), Is.False, "대소문자 구분");
        Assert.That(LobbyTutorialPathRules.ShouldShow("Central", "Sortie"), Is.False);
    }

    [Test]
    public void ShouldShow_EmptyCurrentHidesAll()
    {
        Assert.That(LobbyTutorialPathRules.ShouldShow("Central", null), Is.False);
        Assert.That(LobbyTutorialPathRules.ShouldShow("Central", ""), Is.False);
        Assert.That(LobbyTutorialPathRules.ShouldShow("", ""), Is.False);
        Assert.That(LobbyTutorialPathRules.ShouldShow(null, null), Is.False);
    }

    [Test]
    public void SuppressLegacy_WhileTutorialActiveEvenWithoutPath()
    {
        Assert.That(LobbyTutorialPathRules.SuppressLegacy(true, false), Is.True, "대사·훈련 단계(경로 없음)에도 옛 안내 끔");
        Assert.That(LobbyTutorialPathRules.SuppressLegacy(false, true), Is.True);
        Assert.That(LobbyTutorialPathRules.SuppressLegacy(false, false), Is.False, "진행기 없는 로비 = 옛 안내 그대로");
    }

    [Test]
    public void PathIds_UniqueAndNonEmpty()
    {
        var ids = LobbyTutorialPathIds.All;
        Assert.That(ids.All(id => !string.IsNullOrEmpty(id)), Is.True);
        Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count));
    }
}
