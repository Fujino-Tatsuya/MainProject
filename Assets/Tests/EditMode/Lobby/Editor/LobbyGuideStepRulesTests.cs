using NUnit.Framework;

/// <summary>
/// 로비 안내 경로 스텝 판정 — 할 일 111. 할 일 157 ②에서 단계 입력을 단계표 기준(명단 고정·입구/출격 집결 활성·출격 확정)으로 옮겼다.
/// 바뀐 점: 명단 고정 전(L00·L10)엔 입구 경로를 켜지 않는다(입구 집결은 L20) — 158 이 라커·중앙 경로로 대체.
/// </summary>
public sealed class LobbyGuideStepRulesTests
{
    // L00·L10 — 명단 고정 전.
    private static LobbyGuideStepInput BeforeRoster(bool characterDone = false) => new LobbyGuideStepInput
    {
        Ready = true,
        CharacterStepDone = characterDone,
    };

    // 명단 고정 뒤 훈련·브리핑 단계(입구·출격 집결 아님).
    private static LobbyGuideStepInput Training(bool inRoster, bool characterDone = true) => new LobbyGuideStepInput
    {
        Ready = true,
        RosterLocked = true,
        InTrainingRoster = inRoster,
        CharacterStepDone = characterDone,
    };

    [Test]
    public void NotReady_IsNone()
    {
        Assert.That(LobbyGuideStepRules.Evaluate(new LobbyGuideStepInput()), Is.EqualTo(LobbyGuideStep.None));
        var input = BeforeRoster();
        input.Ready = false;
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.None));
    }

    [Test]
    public void FirstEntry_ShowsCharacterSelect()
    {
        Assert.That(LobbyGuideStepRules.Evaluate(BeforeRoster()), Is.EqualTo(LobbyGuideStep.CharacterSelect));
    }

    [Test]
    public void BeforeRoster_CharacterDone_IsNone_EntranceComesAtL20()
    {
        Assert.That(LobbyGuideStepRules.Evaluate(BeforeRoster(characterDone: true)), Is.EqualTo(LobbyGuideStep.None));
    }

    [Test]
    public void EntranceGather_Roster_ShowsTutorial_UntilInsideEntrance()
    {
        var input = Training(inRoster: true);
        input.EntranceOpen = true;
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.Tutorial));
        input.InEntranceZone = true;
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.None));
    }

    [Test]
    public void TrainingCleared_BeforeRoster_ShowsSortie_AfterCharacterStep()
    {
        var input = BeforeRoster(characterDone: true);
        input.TrainingCleared = true;
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.Sortie));

        // 클리어 플래그여도 캐릭터 스텝은 먼저
        input.CharacterStepDone = false;
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.CharacterSelect));
    }

    [Test]
    public void InsideSortieZone_IsNone_InAnyStep()
    {
        var before = BeforeRoster();
        before.InSortieZone = true;
        Assert.That(LobbyGuideStepRules.Evaluate(before), Is.EqualTo(LobbyGuideStep.None));

        var sortie = Training(inRoster: true);
        sortie.SortieOpen = true;
        sortie.InSortieZone = true;
        Assert.That(LobbyGuideStepRules.Evaluate(sortie), Is.EqualTo(LobbyGuideStep.None));
    }

    [Test]
    public void RosterDuringTraining_IsNone()
    {
        Assert.That(LobbyGuideStepRules.Evaluate(Training(inRoster: true)), Is.EqualTo(LobbyGuideStep.None));
        Assert.That(LobbyGuideStepRules.Evaluate(Training(inRoster: true, characterDone: false)), Is.EqualTo(LobbyGuideStep.None));

        var cleared = Training(inRoster: true);
        cleared.TrainingCleared = true;
        Assert.That(LobbyGuideStepRules.Evaluate(cleared), Is.EqualTo(LobbyGuideStep.None), "명단 안이면 훈련 구역이 안내");
    }

    [Test]
    public void SortieGather_Roster_ShowsSortie_EvenWithoutCharacterStep()
    {
        // 훈련 명단은 캐릭터가 잠겨 있다(②) — 캐릭터 스텝을 건너뛴다.
        var input = Training(inRoster: true, characterDone: false);
        input.SortieOpen = true;
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.Sortie));
    }

    [Test]
    public void LateJoinerDuringTraining_CharacterThenSortie()
    {
        var input = Training(inRoster: false, characterDone: false);
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.CharacterSelect));
        input.CharacterStepDone = true;
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.Sortie));
    }

    [Test]
    public void SortieCountdown_SkipsCharacterStep()
    {
        var input = BeforeRoster();
        input.SortieOpen = true;            // 시연 즉시 출격 플래그
        input.SortieCountdownActive = true;
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.Sortie));
    }

    [Test]
    public void InstantSortieFlag_BeforeRoster_ShowsSortie()
    {
        var input = BeforeRoster(characterDone: true);
        input.SortieOpen = true;
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.Sortie));
    }

    [Test]
    public void Launching_IsNone()
    {
        var input = Training(inRoster: false, characterDone: false);
        input.Launching = true;
        Assert.That(LobbyGuideStepRules.Evaluate(input), Is.EqualTo(LobbyGuideStep.None));
    }
}
