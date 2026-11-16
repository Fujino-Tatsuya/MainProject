using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// 로비 임무 브리핑 — 할 일 157 ⑨(명세 §5 L30–L34·§3.3·§10 수용 기준 11·12, Q7 A) → 할 일 292(옛 L31~L33 대사 3행·삽화 2장 → 영상 1행).
/// 영상 → L34 순서·각 1회(공용 대사 스케줄러), 영상 건너뛰기(전원 동의) 뒤에도 잠금 유지, 잠금 범위 영상+L34·모든 해제 경로,
/// 영상·대사 중 목표 패널 숨김 연속, T50 훈련 보정이 출구 확정 때 풀림, 옛 대사 문구는 연결만 끊고 보존.
/// </summary>
public sealed class LobbyBriefingTests
{
    static readonly string[] BriefRows = { "L31_BriefVideo", "L34_SortieIntro" };
    const string SortieDialogue = "LOBBY_BRIEF_SORTIE";
    // 292 로 연결을 끊은 옛 브리핑 대사(문구 행·Strings 는 남긴다).
    static readonly string[] RetiredDialogues = { "LOBBY_BRIEF_EVAL", "LOBBY_BRIEF_FACTORY", "LOBBY_BRIEF_REBELLION" };

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

    static LobbyTutorialSnapshot Snap(int revision, int step, double start = 0d, double end = 0d) =>
        new LobbyTutorialSnapshot { RunId = 1, Revision = revision, StepIndex = (byte)step, TimerStartServerTime = start, TimerEndServerTime = end };

    static LobbyTutorialStepView ViewAt(LobbyTutorialStepTable table, int index) => new LobbyTutorialStepView(index, table[index]);

    // ── 단계표 ──

    [Test]
    public void L30_다음은_영상_L31_그리고_출격_안내_L34_그리고_L40()
    {
        var table = LoadSteps();
        var l30 = Index(table, "L30_BriefGather");
        Assert.That(table[l30].kind, Is.EqualTo(LobbyTutorialStepKind.Gather));
        Assert.That(table[l30].gatherPoint, Is.EqualTo(LobbyGatherPoint.Brief), "중앙 = Gather_Brief");
        Assert.That(table[l30].holdSeconds, Is.EqualTo(0.3f));

        var video = table[l30 + 1];
        Assert.That(video.stateId, Is.EqualTo("L31_BriefVideo"));
        Assert.That(video.kind, Is.EqualTo(LobbyTutorialStepKind.Video));
        Assert.That(video.locksInput, Is.True);
        Assert.That(video.objectiveKey ?? "", Is.Empty, "영상 중 목표 없음");

        var sortie = table[l30 + 2];
        Assert.That(sortie.stateId, Is.EqualTo("L34_SortieIntro"), "L34 출격 안내 대사는 그대로");
        Assert.That(sortie.kind, Is.EqualTo(LobbyTutorialStepKind.Dialogue));
        Assert.That(sortie.locksInput, Is.True);
        Assert.That(sortie.dialogueId, Is.EqualTo(SortieDialogue));
        Assert.That(sortie.speakerKey, Is.EqualTo("LOBBY_SPEAKER_SUPERVISOR"));
        Assert.That(sortie.backgroundId ?? "", Is.Empty, "L34 = 배경 없음 = 로비");

        var after = table[l30 + 3];
        Assert.That(after.kind, Is.EqualTo(LobbyTutorialStepKind.Gather));
        Assert.That(after.gatherPoint, Is.EqualTo(LobbyGatherPoint.Sortie), "L34 끝 → L40 출격 집결");
    }

    [Test]
    public void 단계표는_옛_브리핑_대사와_삽화를_더_쓰지_않는다()
    {
        var table = LoadSteps();
        foreach (var step in table.Steps)
        {
            Assert.That(System.Array.IndexOf(RetiredDialogues, step.dialogueId), Is.EqualTo(-1), step.stateId);
            Assert.That(step.backgroundId ?? "", Is.Empty, $"{step.stateId} — 삽화(Scene_01/02) 연결 끊음(파일은 남김)");
        }

        foreach (var id in new[] { "L31_BriefIntro", "L32_BriefFactory", "L33_BriefRebellion" })
            Assert.That(table.IndexOf(id), Is.EqualTo(-1), id);
    }

    [Test]
    public void 공용_스케줄러로_영상_뒤_L34_각_1회_뒤_L40_잠금은_한_묶음()
    {
        var table = LoadSteps();
        var l31 = Index(table, "L31_BriefVideo");
        var l40 = Index(table, "L40_SortieGather");
        var flow = new LobbyTutorialFlow(table.Kinds(), 1);
        flow.AddClient(0);
        Assert.That(flow.TryAdvanceTo(1, flow.Revision, l31, 1, 0.0, ignoreParticipants: true), Is.EqualTo(LobbyTutorialAdvanceResult.Advanced));

        // 진행기 TickDialogueServer 와 같은 순서 — Start 에 길이 실기(영상 = 클립 길이), End 에 다음 행 1회.
        var clock = new LobbyTutorialDialogueClock();
        var started = new List<string>();
        var session = new LobbyBriefingLockSession();
        var begins = 0;
        var releases = 0;
        Count(session.Update(LobbyBriefingRules.IsBriefing(ViewAt(table, flow.StepIndex))), ref begins, ref releases);

        for (var frame = 2; frame < 2000; frame++)
        {
            var now = frame * 0.1;
            if (!LobbyBriefingRules.IsTimed(flow.CurrentKind))
                continue;   // L40 뒤에도 더 돌려 반복이 없음을 본다

            var step = table[flow.StepIndex];
            var revision = flow.Revision;
            switch (clock.Tick(revision, now, flow.Snapshot.TimerEndServerTime))
            {
                case LobbyTutorialDialogueAction.Start:
                    var video = step.kind == LobbyTutorialStepKind.Video;
                    started.Add(video ? step.stateId : step.dialogueId);
                    flow.SetTimer(revision, now, now + (video ? LobbyBriefingVideoRules.VideoSeconds(57d, step.dialogueSeconds) : 4.0));
                    break;
                case LobbyTutorialDialogueAction.End:
                    if (flow.TryAdvance(1, revision, frame, now) == LobbyTutorialAdvanceResult.Advanced)
                    {
                        clock.MarkEnded(revision);
                        Count(session.Update(LobbyBriefingRules.IsBriefing(ViewAt(table, flow.StepIndex))), ref begins, ref releases);
                    }
                    break;
            }
        }

        Assert.That(started, Is.EqualTo(new[] { "L31_BriefVideo", SortieDialogue }), "영상 → L34, 각 1회");
        Assert.That(flow.StepIndex, Is.EqualTo(l40));
        Assert.That(begins, Is.EqualTo(1), "잠금은 영상 진입 1회 — L34 로 넘어가도 다시 걸지 않는다");
        Assert.That(releases, Is.EqualTo(1), "L34 끝(→ L40)에 1회 해제");
        Assert.That(session.IsHolding, Is.False);
    }

    [Test]
    public void 영상_건너뛰기는_전원_동의_순간_L34_로_가고_잠금은_유지()
    {
        var table = LoadSteps();
        var l31 = Index(table, "L31_BriefVideo");
        var l34 = Index(table, "L34_SortieIntro");
        var flow = new LobbyTutorialFlow(table.Kinds(), 1);
        flow.AddClient(0);
        flow.AddClient(1);
        Assert.That(flow.TryAdvanceTo(1, flow.Revision, l31, 1, 0.0, ignoreParticipants: true), Is.EqualTo(LobbyTutorialAdvanceResult.Advanced));
        var session = new LobbyBriefingLockSession();
        Assert.That(session.Update(LobbyBriefingRules.IsBriefing(ViewAt(table, flow.StepIndex))), Is.EqualTo(LobbyBriefingLockAction.Begin));

        var clock = new LobbyTutorialDialogueClock();
        Assert.That(clock.Tick(flow.Revision, 1.0, 0.0), Is.EqualTo(LobbyTutorialDialogueAction.Start));
        flow.SetTimer(flow.Revision, 1.0, 58.0);

        var voters = new List<ulong> { 0, 1 };
        var votes = new BossIntroSkipVotes();
        var videoRevision = flow.Revision;
        Assert.That(LobbyBriefingVideoRules.AcceptsSkipVote(flow.CurrentKind, flow.RunId, flow.Revision, 1, videoRevision, flow.HasPendingAdvance), Is.True);
        Assert.That(votes.TryVote(0, voters), Is.True);
        Assert.That(votes.IsUnanimous(voters), Is.False, "1/2 — 계속 재생");
        Assert.That(votes.TryVote(1, voters), Is.True);
        Assert.That(votes.IsUnanimous(voters), Is.True, "2/2 — 건너뛰기");

        // 진행기 ServerSkipVideo 와 같은 순서 — 다음 행 전이 1회 + 같은 revision 의 시계 끝 표시.
        Assert.That(flow.TryAdvance(1, videoRevision, 10, 12.0), Is.EqualTo(LobbyTutorialAdvanceResult.Advanced));
        clock.MarkEnded(videoRevision);
        Assert.That(flow.StepIndex, Is.EqualTo(l34), "영상을 건너뛰면 L34 출격 안내");
        Assert.That(session.Update(LobbyBriefingRules.IsBriefing(ViewAt(table, flow.StepIndex))), Is.EqualTo(LobbyBriefingLockAction.None),
            "영상 → L34 는 같은 묶음 — 잠금을 풀지 않는다");
        Assert.That(clock.Tick(flow.Revision, 12.1, 0.0), Is.EqualTo(LobbyTutorialDialogueAction.Start), "L34 대사 시작");

        Assert.That(LobbyBriefingVideoRules.AcceptsSkipVote(flow.CurrentKind, flow.RunId, flow.Revision, 1, videoRevision, flow.HasPendingAdvance), Is.False,
            "늦게 도착한 옛 영상 표는 버린다");
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
    public void 잠금_범위는_영상_L31_과_L34_뿐()
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
        Assert.That(session.Update(true), Is.EqualTo(LobbyBriefingLockAction.Begin), "L31 영상");
        Assert.That(session.Update(true), Is.EqualTo(LobbyBriefingLockAction.None), "L34(영상 → 대사)");
        Assert.That(session.Update(true), Is.EqualTo(LobbyBriefingLockAction.None), "L34 계속");
        Assert.That(session.IsHolding, Is.True);
        Assert.That(session.Update(false), Is.EqualTo(LobbyBriefingLockAction.Release), "L40");
        Assert.That(session.Update(false), Is.EqualTo(LobbyBriefingLockAction.None));
    }

    [Test]
    public void 잠금_세션_단계_이탈_회차_초기화도_해제하고_다시_들어오면_다시_건다()
    {
        var session = new LobbyBriefingLockSession();
        session.Update(true);
        Assert.That(session.Update(false), Is.EqualTo(LobbyBriefingLockAction.Release), "영상 중 회차 초기화(L00 등)");
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

    // ── 파사드: 영상·대사 동안 패널 숨김 연속 ──

    [Test]
    public void 영상과_L34_동안_목표_패널_숨김이_연속이고_영상_보기는_영상_행만()
    {
        var table = LoadSteps();
        var l30 = Index(table, "L30_BriefGather");
        var l31 = Index(table, "L31_BriefVideo");
        var l34 = Index(table, "L34_SortieIntro");
        var l40 = Index(table, "L40_SortieGather");
        var log = new List<string>();
        var panelShownDuringBrief = new List<string>();
        var inBrief = false;

        void CheckPanel(string where)
        {
            if (inBrief && LobbyTutorialState.ObjectivePanelVisible)
                panelShownDuringBrief.Add(where);
        }

        LobbyTutorialState.DialogueEnded += (id, rev) => { log.Add($"End:{id}"); CheckPanel($"Ended {id}"); };
        LobbyTutorialState.StepChanged += (prev, next, rev) => { log.Add($"Step:{next.StateId}"); CheckPanel($"Step {next.StateId}"); };
        LobbyTutorialState.DialogueStarted += view => { log.Add($"Start:{view.DialogueId}"); CheckPanel($"Started {view.DialogueId}"); };

        LobbyTutorialState.Publish(table, Snap(1, l30));
        Assert.That(LobbyTutorialState.ObjectivePanelVisible, Is.True, "L30 = 중앙에 모이세요");
        Assert.That(LobbyTutorialState.IsVideoActive, Is.False);

        inBrief = true;
        LobbyTutorialState.Publish(table, Snap(2, l31));
        Assert.That(LobbyTutorialState.IsVideoActive, Is.True, "영상 행 진입 — 시작 시각은 아직 0(서버 Start 틱 전)");
        Assert.That(LobbyTutorialState.Video.StartServerTime, Is.EqualTo(0d));
        Assert.That(LobbyTutorialState.IsDialogueActive, Is.False, "영상 행은 대사 패널을 띄우지 않는다");
        Assert.That(LobbyTutorialState.ObjectivePanelVisible, Is.False);
        Assert.That(LobbyTutorialState.IsInputLockedForBriefing, Is.True);

        LobbyTutorialState.Publish(table, Snap(2, l31, 10d, 67d));   // 같은 revision — 서버가 시각을 실음(이벤트 없음)
        Assert.That(LobbyTutorialState.Video.StartServerTime, Is.EqualTo(10d));
        Assert.That(LobbyTutorialState.Video.EndServerTime, Is.EqualTo(67d));
        Assert.That(LobbyTutorialState.Video.Revision, Is.EqualTo(2));
        Assert.That(LobbyTutorialState.Video.Clip, Is.SameAs(table[l31].videoClip));

        LobbyTutorialState.Publish(table, Snap(3, l34));
        Assert.That(LobbyTutorialState.IsVideoActive, Is.False, "L34 = 영상 끝");
        Assert.That(LobbyTutorialState.IsDialogueActive, Is.True);
        Assert.That(LobbyTutorialState.ObjectivePanelVisible, Is.False);
        Assert.That(LobbyTutorialState.IsInputLockedForBriefing, Is.True);

        inBrief = false;   // L34 → L40 의 Ended 시점엔 이미 L40 이라 패널이 켜진다(정상)
        LobbyTutorialState.Publish(table, Snap(4, l40));
        Assert.That(LobbyTutorialState.ObjectivePanelVisible, Is.True, "L40 = 출격 지점에 모이세요");
        Assert.That(LobbyTutorialState.IsInputLockedForBriefing, Is.False);

        Assert.That(panelShownDuringBrief, Is.Empty, "영상 → 대사 사이에 이전 목표가 다시 나타나지 않는다");
        Assert.That(log, Is.EqualTo(new[]
        {
            "Step:L30_BriefGather",
            "Step:L31_BriefVideo",
            "Step:L34_SortieIntro", "Start:LOBBY_BRIEF_SORTIE",
            "End:LOBBY_BRIEF_SORTIE", "Step:L40_SortieGather",
        }));
    }

    [Test]
    public void 출격_확정_뒤엔_영상_보기가_꺼진다()
    {
        var table = LoadSteps();
        var l31 = Index(table, "L31_BriefVideo");
        var snap = Snap(2, l31, 1d, 58d);
        snap.LaunchCommitted = true;
        LobbyTutorialState.Publish(table, snap);

        Assert.That(LobbyTutorialState.IsVideoActive, Is.False);
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
    public void 문구_에셋에_L34_와_연결_끊은_옛_브리핑_대사_행이_남고_옛_무전_목표_키도_보존()
    {
        var table = AssetDatabase.LoadAssetAtPath<TutorialGuideTextTable>(TutorialGuideTextTable.AssetPath);
        Assume.That(table, Is.Not.Null, "TutorialGuideTexts 에셋 없음");
        foreach (var id in new[] { SortieDialogue, RetiredDialogues[0], RetiredDialogues[1], RetiredDialogues[2] })   // 292 — 연결만 끊고 문구 행은 남긴다
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
