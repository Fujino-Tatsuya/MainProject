using System.Collections.Generic;
using NUnit.Framework;

/// <summary>로비 단계별 표시 판정·바닥 화살표 수치 — 할 일 111.</summary>
public sealed class LobbyTrainingStageVisibilityRulesTests
{
    [Test]
    public void 진행기가_없으면_목록이_맞아도_꺼짐()
    {
        var conditions = new List<LobbyStageCondition> { new LobbyStageCondition(LobbyTrainingStage.Lobby) };

        Assert.That(LobbyTrainingStageVisibilityRules.IsVisible(false, LobbyTrainingStage.Lobby, LobbyTrainingPhase.Gathering, conditions), Is.False);
        Assert.That(LobbyTrainingStageVisibilityRules.IsVisible(true, LobbyTrainingStage.Lobby, LobbyTrainingPhase.Gathering, conditions), Is.True);
    }

    [Test]
    public void 빈_목록이나_null_은_늘_꺼짐()
    {
        Assert.That(LobbyTrainingStageVisibilityRules.IsVisible(true, LobbyTrainingStage.Lobby, LobbyTrainingPhase.Gathering, new List<LobbyStageCondition>()), Is.False);
        Assert.That(LobbyTrainingStageVisibilityRules.IsVisible(true, LobbyTrainingStage.Lobby, LobbyTrainingPhase.Gathering, null), Is.False);
    }

    [TestCase(LobbyTrainingPhase.Gathering)]
    [TestCase(LobbyTrainingPhase.InProgress)]
    [TestCase(LobbyTrainingPhase.Completed)]
    public void 하위_상태를_안_가리면_그_단계_전체에서_켜짐(LobbyTrainingPhase phase)
    {
        var conditions = new List<LobbyStageCondition> { new LobbyStageCondition(LobbyTrainingStage.Train02) };

        Assert.That(LobbyTrainingStageVisibilityRules.IsVisible(true, LobbyTrainingStage.Train02, phase, conditions), Is.True);
        Assert.That(LobbyTrainingStageVisibilityRules.IsVisible(true, LobbyTrainingStage.Train01, phase, conditions), Is.False);
    }

    [TestCase(LobbyTrainingPhase.Gathering, false)]
    [TestCase(LobbyTrainingPhase.InProgress, false)]
    [TestCase(LobbyTrainingPhase.Completed, true)]
    public void 출격_집결은_Brief_Completed_에서만(LobbyTrainingPhase phase, bool expected)
    {
        var conditions = new List<LobbyStageCondition>
        {
            new LobbyStageCondition(LobbyTrainingStage.Brief, LobbyTrainingPhaseMask.Completed),
        };

        Assert.That(LobbyTrainingStageVisibilityRules.IsVisible(true, LobbyTrainingStage.Brief, phase, conditions), Is.EqualTo(expected));
    }

    [Test]
    public void 하위_상태를_가렸는데_아무것도_안_고르면_꺼짐()
    {
        var conditions = new List<LobbyStageCondition> { new LobbyStageCondition(LobbyTrainingStage.Train01, 0) };

        Assert.That(LobbyTrainingStageVisibilityRules.IsVisible(true, LobbyTrainingStage.Train01, LobbyTrainingPhase.Gathering, conditions), Is.False);
    }

    [Test]
    public void 여러_줄_중_하나만_맞아도_켜짐()
    {
        var conditions = new List<LobbyStageCondition>
        {
            new LobbyStageCondition(LobbyTrainingStage.Train01, LobbyTrainingPhaseMask.Gathering),
            new LobbyStageCondition(LobbyTrainingStage.Train03A, LobbyTrainingPhaseMask.Gathering | LobbyTrainingPhaseMask.Completed),
        };

        Assert.That(LobbyTrainingStageVisibilityRules.IsVisible(true, LobbyTrainingStage.Train01, LobbyTrainingPhase.Gathering, conditions), Is.True);
        Assert.That(LobbyTrainingStageVisibilityRules.IsVisible(true, LobbyTrainingStage.Train01, LobbyTrainingPhase.InProgress, conditions), Is.False);
        Assert.That(LobbyTrainingStageVisibilityRules.IsVisible(true, LobbyTrainingStage.Train03A, LobbyTrainingPhase.Completed, conditions), Is.True);
        Assert.That(LobbyTrainingStageVisibilityRules.IsVisible(true, LobbyTrainingStage.Train03A, LobbyTrainingPhase.InProgress, conditions), Is.False);
        Assert.That(LobbyTrainingStageVisibilityRules.IsVisible(true, LobbyTrainingStage.Sortie, LobbyTrainingPhase.InProgress, conditions), Is.False);
    }

    [Test]
    public void 하위_상태_비트는_enum_값과_한_칸씩_대응()
    {
        Assert.That(LobbyTrainingStageVisibilityRules.ToMask(LobbyTrainingPhase.Gathering), Is.EqualTo(LobbyTrainingPhaseMask.Gathering));
        Assert.That(LobbyTrainingStageVisibilityRules.ToMask(LobbyTrainingPhase.InProgress), Is.EqualTo(LobbyTrainingPhaseMask.InProgress));
        Assert.That(LobbyTrainingStageVisibilityRules.ToMask(LobbyTrainingPhase.Completed), Is.EqualTo(LobbyTrainingPhaseMask.Completed));
    }

    [Test]
    public void 화살표_반복_수와_흐름_속도는_간격_기준()
    {
        Assert.That(LobbyFloorArrowStrip.TilingFor(4f, 1f), Is.EqualTo(4f).Within(1e-5f));
        Assert.That(LobbyFloorArrowStrip.TilingFor(3f, 0.5f), Is.EqualTo(6f).Within(1e-5f));
        Assert.That(LobbyFloorArrowStrip.ScrollTilesPerSecond(1.5f, 0.5f), Is.EqualTo(3f).Within(1e-5f));
        Assert.That(LobbyFloorArrowStrip.ScrollTilesPerSecond(-1f, 1f), Is.EqualTo(-1f).Within(1e-5f));
        Assert.That(LobbyFloorArrowStrip.TilingFor(4f, 0f), Is.GreaterThan(0f));   // 간격 0 이어도 나눗셈 폭주 없음
    }
}
