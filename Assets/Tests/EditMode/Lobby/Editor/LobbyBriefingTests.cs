using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// 로비 임무 브리핑 — 할 일 157 ⑨(명세 §5 L30–L34·§3.3·§10 수용 기준 11·12, Q7 A). 대사 4행 순서·각 1회(공용 대사 스케줄러), 잠금 범위 L31~L34·모든 해제 경로,
/// 대사 중 목표 패널 숨김 연속, 행별 배경 ID, T50 훈련 보정이 출구 확정 때 풀림. (옛 BRIEF_01~04 한 번에 돌리던 모델 테스트는 이리로 옮겨 정리.)
/// </summary>
public sealed class LobbyBriefingTests
{
    static readonly string[] BriefRows = { "L31_BriefIntro", "L32_BriefFactory", "L33_BriefRebellion", "L34_SortieIntro" };
    static readonly string[] BriefDialogues = { "LOBBY_BRIEF_EVAL", "LOBBY_BRIEF_FACTORY", "LOBBY_BRIEF_REBELLION", "LOBBY_BRIEF_SORTIE" };
    static readonly string[] BriefBackgrounds = { "", "BRIEF_BG_FACTORY", "BRIEF_BG_REBELLION", "" };

    [SetUp]
    public void SetUp() => LobbyTutorialState.ResetForTests();

    [TearDown]
    public void TearDown() => LobbyTutorialState.ResetForTests();

    static LobbyTutorialStepTable LoadSteps()
    {
        var table = AssetDatabase.LoadAssetAtPath<LobbyTutorialStepTable>("Assets/9.ScriptableObject/Lobby/LobbyTutorialSteps.asset");
        Assert.That(table, Is.Not.Null);
        return table;
    }

    static int Index(LobbyTutorialStepTable table, string stateId)
    {
        var index = table.IndexOf(stateId);
        Assert.That(index, Is.GreaterThanOrEqualTo(0), stateId);
        return index;
    }

    static LobbyTutorialStepView View(LobbyTutorialStepTable table, string stateId)
    {
        var index = Index(table, stateId);
        return new LobbyTutorialStepView(index, table[index]);
    }

    static LobbyTutorialSnapshot Snap(int revision, int step) =>
        new LobbyTutorialSnapshot { RunId = 1, Revision = revision, StepIndex = (byte)step };

    // ── 단계표 ──

    [Test]
    public void L30_다음은_대사_4행_순서와_대사_배경_ID_그리고_L40()
    {
        var table = LoadSteps();
        var l30 = Index(table, "L30_BriefGather");
        Assert.That(table[l30].kind, Is.EqualTo(LobbyTutorialStepKind.Gather));
        Assert.That(table[l30].gatherPoint, Is.EqualTo(LobbyGatherPoint.Brief), "중앙 = Gather_Brief");
        Assert.That(table[l30].holdSeconds, Is.EqualTo(0.3f));

        for (var i = 0; i < BriefRows.Length; i++)
        {
            var step = table[l30 + 1 + i];
            Assert.That(step.stateId, Is.EqualTo(BriefRows[i]));
            Assert.That(step.kind, Is.EqualTo(LobbyTutorialStepKind.Dialogue), step.stateId);
            Assert.That(step.locksInput, Is.True, step.stateId);
            Assert.That(step.dialogueId, Is.EqualTo(BriefDialogues[i]), step.stateId);
            Assert.That(step.backgroundId ?? "", Is.EqualTo(BriefBackgrounds[i]), $"{step.stateId} 배경(L34 = 없음 = 로비 복귀)");
            Assert.That(step.speakerKey, Is.EqualTo("LOBBY_SPEAKER_SUPERVISOR"), step.stateId);
            Assert.That(step.objectiveKey ?? "", Is.Empty, $"{step.stateId} '작전 설명 중' 목표 없음");
        }

        var after = table[l30 + 1 + BriefRows.Length];
        Assert.That(after.kind, Is.EqualTo(LobbyTutorialStepKind.Gather));
        Assert.That(after.gatherPoint, Is.EqualTo(LobbyGatherPoint.Sortie), "L34 끝 → L40 출격 집결");
    }

    [Test]
    public void 공용_대사_스케줄러로_22_23_24_25_순서대로_각_1회_뒤_L40()
    {
        var table = LoadSteps();
        var l31 = Index(table, "L31_BriefIntro");
        var l40 = Index(table, "L40_SortieGather");
        var flow = new LobbyTutorialFlow(table.Kinds(), 1);
        flow.AddClient(0);
        Assert.That(flow.TryAdvanceTo(1, flow.Revision, l31, 1, 0.0, ignoreParticipants: true), Is.EqualTo(LobbyTutorialAdvanceResult.Advanced));

        // 진행기 TickDialogueServer 와 같은 순서 — Start 에 길이 실기, End 에 다음 행 1회. 대사마다 집결 재검사 없음(구역 없이 넘어간다).
        var clock = new LobbyTutorialDialogueClock();
        var started = new List<string>();
        var session = new LobbyBriefingLockSession();
        var begins = 0;
        var releases = 0;
        Count(session.Update(LobbyBriefingRules.IsBriefing(new LobbyTutorialStepView(flow.StepIndex, table[flow.StepIndex]))), ref begins, ref releases);

        for (var frame = 2; frame < 400; frame++)
        {
            var now = frame * 0.1;
            if (flow.CurrentKind != LobbyTutorialStepKind.Dialogue)
                continue;   // L40 뒤에도 더 돌려 반복이 없음을 본다

            var revision = flow.Revision;
            switch (clock.Tick(revision, now, flow.Snapshot.TimerEndServerTime))
            {
                case LobbyTutorialDialogueAction.Start:
                    started.Add(table[flow.StepIndex].dialogueId);
                    flow.SetTimer(revision, now, now + 4.0);
                    break;
                case LobbyTutorialDialogueAction.End:
                    if (flow.TryAdvance(1, revision, frame, now) == LobbyTutorialAdvanceResult.Advanced)
                    {
                        clock.MarkEnded(revision);
                        Count(session.Update(LobbyBriefingRules.IsBriefing(new LobbyTutorialStepView(flow.StepIndex, table[flow.StepIndex]))),
                            ref begins, ref releases);
                    }
                    break;
            }
        }

        Assert.That(started, Is.EqualTo(BriefDialogues), "22→23→24→25, 각 1회");
        Assert.That(flow.StepIndex, Is.EqualTo(l40));
        Assert.That(begins, Is.EqualTo(1), "잠금은 L31 진입 1회 — 행이 바뀌어도 다시 걸지 않는다");
        Assert.That(releases, Is.EqualTo(1), "L34 끝(→ L40)에 1회 해제");
        Assert.That(session.IsHolding, Is.False);
    }

    static void Count(LobbyBriefingLockAction action, ref int begins, ref int releases)
    {
        if (action == LobbyBriefingLockAction.Begin)
            begins++;
        else if (action == LobbyBriefingLockAction.Release)
            releases++;
    }

    // ── 잠금 범위·해제 경로 ──

    [Test]
    public void 잠금_범위는_L31_L34_뿐()
    {
        var table = LoadSteps();
        for (var i = 0; i < table.Count; i++)
        {
            var view = new LobbyTutorialStepView(i, table[i]);
            var expected = System.Array.IndexOf(BriefRows, table[i].stateId) >= 0;
            Assert.That(LobbyBriefingRules.IsBriefing(view), Is.EqualTo(expected), table[i].stateId);
        }

        Assert.That(LobbyBriefingRules.IsBriefing(default), Is.False, "진행기 없음");
    }

    [Test]
    public void 잠금_세션_진입_1회_묶음_안_이동_없음_이탈_1회()
    {
        var session = new LobbyBriefingLockSession();
        Assert.That(session.Update(false), Is.EqualTo(LobbyBriefingLockAction.None), "L30");
        Assert.That(session.Update(true), Is.EqualTo(LobbyBriefingLockAction.Begin), "L31");
        Assert.That(session.Update(true), Is.EqualTo(LobbyBriefingLockAction.None), "L32");
        Assert.That(session.Update(true), Is.EqualTo(LobbyBriefingLockAction.None), "L33·L34");
        Assert.That(session.IsHolding, Is.True);
        Assert.That(session.Update(false), Is.EqualTo(LobbyBriefingLockAction.Release), "L40");
        Assert.That(session.Update(false), Is.EqualTo(LobbyBriefingLockAction.None));
    }

    [Test]
    public void 잠금_세션_단계_이탈_회차_초기화도_해제하고_다시_들어오면_다시_건다()
    {
        var session = new LobbyBriefingLockSession();
        session.Update(true);
        Assert.That(session.Update(false), Is.EqualTo(LobbyBriefingLockAction.Release), "L32 중 회차 초기화(L00 등)");
        Assert.That(session.Update(true), Is.EqualTo(LobbyBriefingLockAction.Begin), "재진입 = 다시 잠금(짝 유지)");
    }

    [Test]
    public void 잠금_세션_강제_종료는_잡고_있을_때만_해제_신호()
    {
        var session = new LobbyBriefingLockSession();
        Assert.That(session.End(), Is.False, "잡지 않음 = 풀 것 없음");
        session.Update(true);
        Assert.That(session.End(), Is.True, "디스폰·씬 언로드·호스트 셧다운");
        Assert.That(session.IsHolding, Is.False);
        Assert.That(session.End(), Is.False, "두 번째 종료는 신호 없음");
        Assert.That(session.Update(false), Is.EqualTo(LobbyBriefingLockAction.None), "종료 뒤 이탈 알림이 와도 이중 해제 없음");
    }

    [Test]
    public void 잠금_장부는_내가_건_것만_푼다()
    {
        var ledger = new LobbyBriefingLockLedger();
        Assert.That(ledger.ShouldLock(1, lockedByOther: false), Is.True);
        ledger.Record(1);
        Assert.That(ledger.ShouldLock(1, lockedByOther: true), Is.False, "이미 내가 잠근 사람은 다시 잠그지 않는다");
        Assert.That(ledger.ShouldLock(2, lockedByOther: true), Is.False, "다른 계통(보스 등장 연출)이 잠근 사람은 건드리지 않는다");
        Assert.That(ledger.ShouldLock(3, lockedByOther: false), Is.True);
        ledger.Record(3);

        var release = new List<ulong>();
        ledger.TakeAll(release);
        Assert.That(release, Is.EqualTo(new ulong[] { 1, 3 }));
        Assert.That(ledger.Count, Is.EqualTo(0));

        ledger.TakeAll(release);
        Assert.That(release, Is.Empty, "두 번째 해제(이탈 뒤 디스폰)는 풀 대상 없음");
    }

    [Test]
    public void 잠금_장부_퇴장한_사람은_풀_대상에서_빠진다()
    {
        var ledger = new LobbyBriefingLockLedger();
        ledger.Record(1);
        ledger.Record(2);
        Assert.That(ledger.Forget(2), Is.True);
        Assert.That(ledger.Forget(2), Is.False);

        var release = new List<ulong>();
        ledger.TakeAll(release);
        Assert.That(release, Is.EqualTo(new ulong[] { 1 }));
    }

    // ── 파사드: 패널 숨김 연속·배경 ID ──

    [Test]
    public void 대사_4행_동안_목표_패널_숨김이_이벤트_사이에도_연속이고_배경_ID_는_행별()
    {
        var table = LoadSteps();
        var l30 = Index(table, "L30_BriefGather");
        var l40 = Index(table, "L40_SortieGather");
        var log = new List<string>();
        var backgrounds = new List<string>();
        var panelShownDuringBrief = new List<string>();
        var inBrief = false;

        void CheckPanel(string where)
        {
            if (inBrief && LobbyTutorialState.ObjectivePanelVisible)
                panelShownDuringBrief.Add(where);
        }

        LobbyTutorialState.DialogueEnded += (id, rev) => { log.Add($"End:{id}"); CheckPanel($"Ended {id}"); };
        LobbyTutorialState.StepChanged += (prev, next, rev) => { log.Add($"Step:{next.StateId}"); CheckPanel($"Step {next.StateId}"); };
        LobbyTutorialState.DialogueStarted += view =>
        {
            log.Add($"Start:{view.DialogueId}");
            backgrounds.Add(view.BackgroundId);
            CheckPanel($"Started {view.DialogueId}");
        };

        LobbyTutorialState.Publish(table, Snap(1, l30));
        Assert.That(LobbyTutorialState.ObjectivePanelVisible, Is.True, "L30 = 중앙에 모이세요");

        var revision = 1;
        for (var i = l30 + 1; i < l40; i++)
        {
            inBrief = true;
            LobbyTutorialState.Publish(table, Snap(++revision, i));
            Assert.That(LobbyTutorialState.ObjectivePanelVisible, Is.False, table[i].stateId);
            Assert.That(LobbyTutorialState.IsInputLockedForBriefing, Is.True, table[i].stateId);
            Assert.That(LobbyTutorialState.Dialogue.BackgroundId, Is.EqualTo(BriefBackgrounds[i - l30 - 1]), table[i].stateId);
        }

        inBrief = false;   // L34 → L40 의 Ended 시점엔 이미 L40 이라 패널이 켜진다(정상)
        LobbyTutorialState.Publish(table, Snap(++revision, l40));
        Assert.That(LobbyTutorialState.ObjectivePanelVisible, Is.True, "L40 = 출격 지점에 모이세요");
        Assert.That(LobbyTutorialState.IsInputLockedForBriefing, Is.False);

        Assert.That(panelShownDuringBrief, Is.Empty, "대사 사이에 이전 목표가 다시 나타나지 않는다");
        Assert.That(backgrounds, Is.EqualTo(BriefBackgrounds));
        Assert.That(log, Is.EqualTo(new[]
        {
            "Step:L30_BriefGather",
            "Step:L31_BriefIntro", "Start:LOBBY_BRIEF_EVAL",
            "End:LOBBY_BRIEF_EVAL", "Step:L32_BriefFactory", "Start:LOBBY_BRIEF_FACTORY",
            "End:LOBBY_BRIEF_FACTORY", "Step:L33_BriefRebellion", "Start:LOBBY_BRIEF_REBELLION",
            "End:LOBBY_BRIEF_REBELLION", "Step:L34_SortieIntro", "Start:LOBBY_BRIEF_SORTIE",
            "End:LOBBY_BRIEF_SORTIE", "Step:L40_SortieGather",
        }), "연속 대사 = Ended 직후 같은 호출에서 Started");
    }

    // ── T50 훈련 보정 ──

    [Test]
    public void T50_훈련_보정은_출구_확정_L30_에서_풀린다()
    {
        var table = LoadSteps();
        var t50 = Index(table, "T50_FreePractice");
        Assert.That(table[t50].trainingProfile, Is.EqualTo(LobbyTutorialTrainingProfile.Training), "명세 §5 T50 — 자유 연습 동안 체력 하한 유지");
        Assert.That(LobbyTutorialRules.HealthFloorFor(table[t50], inLockedRoster: true), Is.EqualTo(1));

        var next = table[t50 + 1];
        Assert.That(next.stateId, Is.EqualTo("L30_BriefGather"), "출구 집결 확정 = 다음 행");
        Assert.That(LobbyTutorialRules.HealthFloorFor(next, inLockedRoster: true), Is.Zero, "확정 전이에서 하한 해제");
        foreach (var id in BriefRows)
            Assert.That(LobbyTutorialRules.HealthFloorFor(table[Index(table, id)], inLockedRoster: true), Is.Zero, id);
    }

    // ── 문구 ──

    [Test]
    public void 문구_에셋에_브리핑_대사_행이_있고_옛_무전_목표_키는_보존()
    {
        var table = AssetDatabase.LoadAssetAtPath<TutorialGuideTextTable>(TutorialGuideTextTable.AssetPath);
        Assume.That(table, Is.Not.Null, "TutorialGuideTexts 에셋 없음");
        foreach (var id in BriefDialogues)
        {
            Assert.That(table.TryGetRow(id, out var row), Is.True, id);
            Assert.That(row.Text, Is.Not.Empty, id);
        }

        foreach (var id in new[] { LobbyRadioIds.Brief01, LobbyRadioIds.Brief02, LobbyRadioIds.Brief03, LobbyRadioIds.Brief04 })
            Assert.That(table.TryGetRow(id, out _), Is.True, $"{id} 키 보존(재생 안 함)");

        foreach (var id in LobbyObjectiveIds.All)
            Assert.That(table.TryGetRow(id, out _), Is.True, $"{id} 키 보존(표시 안 함)");
    }
}
