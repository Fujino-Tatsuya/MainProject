using NUnit.Framework;
using UnityEditor;

/// <summary>
/// 대사 단계 스케줄러 — 할 일 157 커밋 ③(Q5). 길이 규칙(행 → 문구 표 → 글자 수, + 끝 여유), 끝 시각 전이 1회·재진입 반복 없음,
/// L10 확정 명단 고정·캐릭터 잠금 없음·L21 표적 미스폰.
/// </summary>
public sealed class LobbyTutorialDialogueTests
{
    const float Base = 2f, PerChar = 0.08f, Min = 3f, Pad = 0.5f;

    static double Seconds(float row, float table, string text) =>
        LobbyTutorialRules.DialogueSeconds(row, table, text, Base, PerChar, Min, Pad);

    // ── 길이 규칙 ──

    [Test]
    public void 행_값이_있으면_행_값_더하기_여유()
    {
        Assert.That(Seconds(4f, 9f, "아주 긴 문장"), Is.EqualTo(4.5).Within(1e-6));
    }

    [Test]
    public void 행_값이_없으면_문구_표_표시_시간()
    {
        Assert.That(Seconds(0f, 6f, "글"), Is.EqualTo(6.5).Within(1e-6));
        Assert.That(Seconds(-1f, 6f, "글"), Is.EqualTo(6.5).Within(1e-6), "음수 행 값 = 없음");
    }

    [Test]
    public void 둘_다_없으면_글자_수_규칙_공백_제외()
    {
        // "다 모였군. 기본기는 갖췄는지부터 볼까." 공백 4 → 18자 → 2 + 18×0.08 = 3.44
        const string text = "다 모였군. 기본기는 갖췄는지부터 볼까.";
        Assert.That(LobbyTutorialRules.CountDialogueChars(text), Is.EqualTo(18));
        Assert.That(Seconds(0f, 0f, text), Is.EqualTo(3.44 + Pad).Within(1e-6));
    }

    [Test]
    public void 짧은_문구는_최소_시간_문구_없음도_최소()
    {
        Assert.That(Seconds(0f, 0f, "좋아."), Is.EqualTo(Min + Pad).Within(1e-6));
        Assert.That(Seconds(0f, 0f, null), Is.EqualTo(Min + Pad).Within(1e-6));
        Assert.That(LobbyTutorialRules.CountDialogueChars(" \n\t"), Is.EqualTo(0));
    }

    [Test]
    public void LobbyRules_에셋_수치는_Q5_기본값이고_문구_표가_연결돼_있다()
    {
        var rules = AssetDatabase.LoadAssetAtPath<LobbyRules>("Assets/9.ScriptableObject/Lobby/LobbyRules.asset");
        Assume.That(rules, Is.Not.Null);

        Assert.That(rules.DialogueBaseSeconds, Is.EqualTo(2f));
        Assert.That(rules.DialoguePerCharSeconds, Is.EqualTo(0.08f));
        Assert.That(rules.DialogueMinSeconds, Is.EqualTo(3f));
        Assert.That(rules.DialogueEndPaddingSeconds, Is.EqualTo(0.5f));
        Assert.That(rules.GuideTexts, Is.Not.Null, "guideTexts = TutorialGuideTexts");
    }

    [Test]
    public void 대사_단계_대사와_목표_키는_문구_표에_ko_폴백이_있다()
    {
        var rules = AssetDatabase.LoadAssetAtPath<LobbyRules>("Assets/9.ScriptableObject/Lobby/LobbyRules.asset");
        Assume.That(rules != null && rules.StepTable != null && rules.GuideTexts != null);

        foreach (var step in rules.StepTable.Steps)
        {
            foreach (var key in new[] { step.titleKey, step.objectiveKey, step.dialogueId, step.speakerKey, step.progressKey })
            {
                if (string.IsNullOrEmpty(key))
                    continue;
                Assert.That(rules.GuideTexts.TryGetRow(key, out var row) && !string.IsNullOrWhiteSpace(row.Text), Is.True, $"{step.stateId} {key}");
            }
        }
    }

    // ── 스케줄러 ──

    [Test]
    public void 새_revision_은_Start_끝_전엔_None_끝에서_End()
    {
        var clock = new LobbyTutorialDialogueClock();

        Assert.That(clock.Tick(3, 10.0, 0.0), Is.EqualTo(LobbyTutorialDialogueAction.Start), "시작 틱은 끝 시각을 보지 않는다(아직 타이머 전)");
        Assert.That(clock.Tick(3, 12.0, 15.0), Is.EqualTo(LobbyTutorialDialogueAction.None));
        Assert.That(clock.Tick(3, 15.0, 15.0), Is.EqualTo(LobbyTutorialDialogueAction.End));
    }

    [Test]
    public void 끝_전이가_받아들여지면_같은_revision_에선_다시_End_없음()
    {
        var clock = new LobbyTutorialDialogueClock();
        clock.Tick(3, 10.0, 0.0);
        clock.Tick(3, 15.0, 15.0);
        clock.MarkEnded(3);

        Assert.That(clock.Tick(3, 16.0, 15.0), Is.EqualTo(LobbyTutorialDialogueAction.None));
        Assert.That(clock.Tick(3, 99.0, 15.0), Is.EqualTo(LobbyTutorialDialogueAction.None), "반복 없음");
    }

    [Test]
    public void 전이가_거부되면_다음_틱에_다시_End()
    {
        var clock = new LobbyTutorialDialogueClock();
        clock.Tick(3, 10.0, 0.0);
        Assert.That(clock.Tick(3, 15.0, 15.0), Is.EqualTo(LobbyTutorialDialogueAction.End));
        // MarkEnded 안 함(참여자 0 등)
        Assert.That(clock.Tick(3, 15.1, 15.0), Is.EqualTo(LobbyTutorialDialogueAction.End));
    }

    [Test]
    public void 연속_대사는_revision_마다_다시_Start()
    {
        var clock = new LobbyTutorialDialogueClock();
        clock.Tick(3, 10.0, 0.0);
        clock.MarkEnded(3);
        clock.MarkEnded(2);   // 지난 revision 은 무시

        Assert.That(clock.Tick(4, 15.0, 15.0), Is.EqualTo(LobbyTutorialDialogueAction.Start));
        Assert.That(clock.HasEnded, Is.False);
    }

    [Test]
    public void 모델과_합치면_끝에서_다음_행으로_정확히_1회()
    {
        var flow = new LobbyTutorialFlow(new[]
        {
            LobbyTutorialStepKind.Gather, LobbyTutorialStepKind.Dialogue, LobbyTutorialStepKind.Gather,
        }, 1);
        flow.AddClient(0);
        flow.TryAdvance(1, 1, 1, 0.0);                  // → 대사(rev 2)
        var clock = new LobbyTutorialDialogueClock();

        Assert.That(clock.Tick(flow.Revision, 1.0, flow.Snapshot.TimerEndServerTime), Is.EqualTo(LobbyTutorialDialogueAction.Start));
        Assert.That(flow.SetTimer(flow.Revision, 1.0, 5.0), Is.True);
        Assert.That(flow.Snapshot.TimerStartServerTime, Is.EqualTo(1.0));
        Assert.That(flow.Snapshot.TimerEndServerTime, Is.EqualTo(5.0));

        var advances = 0;
        for (var frame = 2; frame < 12; frame++)
        {
            var now = frame * 0.5;
            var revision = flow.Revision;
            if (flow.CurrentKind != LobbyTutorialStepKind.Dialogue)
                break;
            if (clock.Tick(revision, now, flow.Snapshot.TimerEndServerTime) == LobbyTutorialDialogueAction.End
                && flow.TryAdvance(1, revision, frame, now) == LobbyTutorialAdvanceResult.Advanced)
            {
                clock.MarkEnded(revision);
                advances++;
            }
        }

        Assert.That(advances, Is.EqualTo(1));
        Assert.That(flow.StepIndex, Is.EqualTo(2));
        Assert.That(flow.Snapshot.EnteredAtServerTime, Is.EqualTo(5.0), "끝 시각(5.0) 틱에 전이");
        Assert.That(flow.Snapshot.TimerEndServerTime, Is.EqualTo(0.0), "다음 행은 타이머 없음");
    }

    // ── L10 확정·캐릭터 잠금 없음 ──

    [Test]
    public void L10_확정은_명단을_고정하고_이후_접속은_명단_밖()
    {
        var flow = new LobbyTutorialFlow(new[] { LobbyTutorialStepKind.LockerVisit, LobbyTutorialStepKind.Gather, LobbyTutorialStepKind.Dialogue }, 1);
        flow.AddClient(0);
        flow.AddClient(1);
        flow.TryAdvance(1, 1, 1, 0);

        Assert.That(flow.LockRoster(), Is.True);
        Assert.That(flow.LockRoster(), Is.False, "확정은 1회");
        flow.AddClient(2);

        Assert.That(flow.Roster, Is.EquivalentTo(new ulong[] { 0, 1 }));
        Assert.That(flow.IsWaitingLateJoiner(2), Is.True);
    }

    [Test]
    public void 명단_고정_뒤_캐릭터_교체해도_명단_라커_기록_유지()
    {
        var flow = new LobbyTutorialFlow(new[] { LobbyTutorialStepKind.LockerVisit, LobbyTutorialStepKind.Gather, LobbyTutorialStepKind.Dialogue }, 1);
        flow.AddClient(0);
        flow.MarkLockerVisited(0);
        flow.TryAdvance(1, 1, 1, 0);
        flow.LockRoster();

        // 교체 = 같은 clientId 재스폰 — 모델 호출 없음(AddClient 는 중복이라 무시)
        flow.AddClient(0);

        Assert.That(flow.Roster, Is.EquivalentTo(new ulong[] { 0 }));
        Assert.That(flow.HasVisitedLocker(0), Is.True);
        Assert.That(flow.IsParticipant(0), Is.True);
    }

    [Test]
    public void 캐릭터_교체는_명단과_무관하게_출격_카운트다운과_출격_뒤만_거부()
    {
        Assert.That(LobbyTutorialRules.CharacterChangeBlock(false, false), Is.EqualTo(LobbyCharacterChangeReject.None), "훈련 중 명단 안도 허용");
        Assert.That(LobbyTutorialRules.CharacterChangeBlock(false, true), Is.EqualTo(LobbyCharacterChangeReject.CountdownActive));
        Assert.That(LobbyTutorialRules.CharacterChangeBlock(true, true), Is.EqualTo(LobbyCharacterChangeReject.GameStarting));
    }

    [Test]
    public void L11_L21_대사_행은_훈련_내용이_없어_표적을_스폰하지_않는다()
    {
        var rules = AssetDatabase.LoadAssetAtPath<LobbyRules>("Assets/9.ScriptableObject/Lobby/LobbyRules.asset");
        Assume.That(rules != null && rules.StepTable != null);
        var table = rules.StepTable;

        foreach (var id in new[] { "L11_CentralIntro", "L21_TrainingIntro" })
        {
            var index = table.IndexOf(id);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), id);
            var step = table[index];
            Assert.That(step.kind, Is.EqualTo(LobbyTutorialStepKind.Dialogue), id);
            Assert.That(step.locksInput, Is.False, $"{id} — 이동 허용 대사(스케줄러 대상)");
            Assert.That(LobbyTrainingSupportRules.Train01TargetsActive(step.activity), Is.False, $"{id} 표적 미스폰");
        }

        var l10 = table[table.IndexOf("L10_CentralGather")];
        Assert.That(l10.gatherPoint, Is.EqualTo(LobbyGatherPoint.Brief), "중앙 = Gather_Brief");
        Assert.That(l10.holdSeconds, Is.EqualTo(0.3f));
        Assert.That(LobbyTutorialRules.RosterLockIndex(table.Steps), Is.EqualTo(table.IndexOf("L10_CentralGather")), "명단 고정 = L10 확정");
    }
}
