using NUnit.Framework;

/// <summary>로비 안내 경로 스텝 판정 — 할 일 111. 판정 표 = Docs/tech/lobby-training.md "안내 스텝".</summary>
public sealed class LobbyGuideStepRulesTests
{
    private static LobbyGuideStepInput Lobby(bool characterDone = false) => new LobbyGuideStepInput
    {
        Ready = true,
        Stage = LobbyTrainingStage.Lobby,
        Phase = LobbyTrainingPhase.Gathering,
        CharacterStepDone = characterDone,
    };

    private static LobbyGuideStepInput Training(LobbyTrainingStage stage, LobbyTrainingPhase phase, bool inRoster, bool characterDone = true) =>
        new LobbyGuideStepInput
        {
            Ready = true,
            Stage = stage,
            Phase = phase,
            InTrainingRoster = inRoster,
            CharacterStepDone = characterDone,
        };

    [Test]
    public void NotReady_IsNone()
    {
        Assert.That(LobbyGuideStepRules.Evaluate(new LobbyGuideStepInput()), Is.EqualTo(LobbyGuideStep.None));
        var input = Lobby();
        input.Ready = false;
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.None));
    }

    [Test]
    public void FirstEntry_ShowsCharacterSelect()
    {
        Assert.That(LobbyGuideStepRules.Evaluate(Lobby()), Is.EqualTo(LobbyGuideStep.CharacterSelect));
    }

    [Test]
    public void CharacterDone_ShowsTutorial_UntilInsideEntrance()
    {
        var input = Lobby(characterDone: true);
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.Tutorial));
        input.InEntranceZone = true;
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.None));
    }

    [Test]
    public void TrainingCleared_SkipsTutorial()
    {
        var input = Lobby(characterDone: true);
        input.TrainingCleared = true;
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.Sortie));

        // 클리어 플래그여도 캐릭터 스텝은 먼저
        input.CharacterStepDone = false;
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.CharacterSelect));
    }

    [Test]
    public void InsideSortieZone_IsNone_InAnyStep()
    {
        var lobby = Lobby();
        lobby.InSortieZone = true;
        Assert.That(LobbyGuideStepRules.Evaluate(lobby), Is.EqualTo(LobbyGuideStep.None));

        var brief = Training(LobbyTrainingStage.Brief, LobbyTrainingPhase.Completed, inRoster: true);
        brief.InSortieZone = true;
        Assert.That(LobbyGuideStepRules.Evaluate(brief), Is.EqualTo(LobbyGuideStep.None));
    }

    [TestCase(LobbyTrainingStage.Train01, LobbyTrainingPhase.Gathering)]
    [TestCase(LobbyTrainingStage.Train02, LobbyTrainingPhase.InProgress)]
    [TestCase(LobbyTrainingStage.Train03C, LobbyTrainingPhase.InProgress)]
    [TestCase(LobbyTrainingStage.TrainEnd, LobbyTrainingPhase.Gathering)]
    [TestCase(LobbyTrainingStage.Brief, LobbyTrainingPhase.Gathering)]
    [TestCase(LobbyTrainingStage.Brief, LobbyTrainingPhase.InProgress)]
    public void RosterDuringTraining_IsNone(LobbyTrainingStage stage, LobbyTrainingPhase phase)
    {
        Assert.That(LobbyGuideStepRules.Evaluate(Training(stage, phase, inRoster: true)), Is.EqualTo(LobbyGuideStep.None));
        Assert.That(LobbyGuideStepRules.Evaluate(Training(stage, phase, inRoster: true, characterDone: false)), Is.EqualTo(LobbyGuideStep.None));
    }

    [Test]
    public void BriefCompleted_Roster_ShowsSortie_EvenWithoutCharacterStep()
    {
        // 훈련 명단은 캐릭터가 잠겨 있다 — 캐릭터 스텝을 건너뛴다.
        var input = Training(LobbyTrainingStage.Brief, LobbyTrainingPhase.Completed, inRoster: true, characterDone: false);
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.Sortie));
    }

    [Test]
    public void LateJoinerDuringTraining_CharacterThenSortie()
    {
        var input = Training(LobbyTrainingStage.Train02, LobbyTrainingPhase.InProgress, inRoster: false, characterDone: false);
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.CharacterSelect));
        input.CharacterStepDone = true;
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.Sortie));
    }

    [Test]
    public void SortieCountdown_SkipsCharacterStep()
    {
        var input = Lobby();
        input.SortieCountdownActive = true;
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.Tutorial));
    }

    // 훈련 종료 집결 확정 = 명단 그대로 + 클리어 플래그 켜짐 — 브리핑 집결·진행 중엔 꺼 두고, 브리핑 완료부터 ③.
    [TestCase(LobbyTrainingPhase.Gathering, LobbyGuideStep.None)]
    [TestCase(LobbyTrainingPhase.InProgress, LobbyGuideStep.None)]
    [TestCase(LobbyTrainingPhase.Completed, LobbyGuideStep.Sortie)]
    public void TrainingFinished_RosterAndCleared_SortieAfterBriefing(LobbyTrainingPhase phase, LobbyGuideStep expected)
    {
        var input = Training(LobbyTrainingStage.Brief, phase, inRoster: true);
        input.TrainingCleared = true;
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(expected));
    }

    [Test]
    public void TrainingFinished_NextRound_ShowsSortieNotTutorial()
    {
        // 판 끝 로비 복귀·Alt+3 — 새 진행기 Lobby 단계, 명단 비어 있음, 훈련 완료 플래그는 이어짐.
        var input = Lobby(characterDone: true);
        input.TrainingCleared = true;
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.Sortie));
    }

    [Test]
    public void SortieStage_IsNone()
    {
        Assert.That(LobbyGuideStepRules.Evaluate(Training(LobbyTrainingStage.Sortie, LobbyTrainingPhase.InProgress, inRoster: false, characterDone: false)),
            Is.EqualTo(LobbyGuideStep.None));
    }
}
