using NUnit.Framework;

// 할 일 107 Legacy — 존4 교육 목표·강조 순수 규칙(TutorialZone4EducationDisplay). 원래 TutorialZone4RulesTests 의 목표·강조 절.
public class TutorialZone4EducationTests
{
    [Test]
    public void Objective_ByStepPanelsAndG9()
    {
        Assert.IsNull(TutorialZone4EducationDisplay.ObjectiveFor(TutorialStep.Corridor3, 0, 4, false));
        Assert.IsNull(TutorialZone4EducationDisplay.ObjectiveFor(TutorialStep.Zone4Lore, 0, 4, false));
        Assert.AreEqual(TutorialGuideIds.Z4PanelGoal, TutorialZone4EducationDisplay.ObjectiveFor(TutorialStep.Zone4Combat, 3, 4, false));
        Assert.AreEqual(TutorialGuideIds.Z4Midboss, TutorialZone4EducationDisplay.ObjectiveFor(TutorialStep.Zone4Combat, 4, 4, false));
        Assert.IsNull(TutorialZone4EducationDisplay.ObjectiveFor(TutorialStep.Zone4Combat, 4, 4, true), "G9 뒤 = 보스방으로(유지 쪽)");
        Assert.IsNull(TutorialZone4EducationDisplay.ObjectiveFor(TutorialStep.Finished, 4, 4, true));
    }

    [Test]
    public void Highlights_ByStepPanelsAndG9()
    {
        Assert.AreEqual(TutorialZone4Highlight.None, TutorialZone4EducationDisplay.HighlightsFor(TutorialStep.Corridor3, 0, 4, false));
        Assert.AreEqual(TutorialZone4Highlight.BossTimer, TutorialZone4EducationDisplay.HighlightsFor(TutorialStep.Zone4Lore, 0, 4, false));
        Assert.AreEqual(TutorialZone4Highlight.None, TutorialZone4EducationDisplay.HighlightsFor(TutorialStep.Zone4Combat, 3, 4, false));
        Assert.AreEqual(TutorialZone4Highlight.MidbossRoute, TutorialZone4EducationDisplay.HighlightsFor(TutorialStep.Zone4Combat, 4, 4, false));
        Assert.AreEqual(TutorialZone4Highlight.None, TutorialZone4EducationDisplay.HighlightsFor(TutorialStep.Zone4Combat, 4, 4, true));
        Assert.AreEqual(TutorialZone4Highlight.None, TutorialZone4EducationDisplay.HighlightsFor(TutorialStep.Finished, 4, 4, true));
    }

    [Test]
    public void IsEducationObjective_OnlyOwnIds()
    {
        Assert.IsTrue(TutorialZone4EducationDisplay.IsEducationObjective(TutorialGuideIds.Z4PanelGoal));
        Assert.IsTrue(TutorialZone4EducationDisplay.IsEducationObjective(TutorialGuideIds.Z4Midboss));
        Assert.IsFalse(TutorialZone4EducationDisplay.IsEducationObjective(TutorialGuideIds.Z4GoBoss));
        Assert.IsFalse(TutorialZone4EducationDisplay.IsEducationObjective(null));
    }
}
