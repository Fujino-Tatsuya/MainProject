using NUnit.Framework;
using UnityEngine;

/// <summary>로비 튜토리얼 UI 바인더(할 일 158 C10) — 157 파사드 값 → 목표 패널 장면·진행 줄·대사 화자 키 매핑, 다시 그리기 판정(서명).</summary>
public sealed class LobbyTutorialUiBinderTests
{
    const string G = LobbyTutorialUiStrings.GuidePrefix;

    sealed class FakeLive : ILobbyTutorialLiveSource
    {
        public int Mask;
        public int Done;
        public int Total;
        public bool LateJoiner;

        public PartyGatherZone GatherZone(LobbyGatherPoint point) => null;
        public bool IsLocalWaitingLateJoiner => LateJoiner;
        public bool TryGetLocalChecklistMask(out int mask) { mask = Mask; return true; }
        public bool TryGetPartyComplete(out int done, out int total) { done = Done; total = Total; return true; }
        public TrainingSparringBot TrainingTarget => null;
    }

    LobbyTutorialStepTable _table;
    FakeLive _live;

    static LobbyTutorialSnapshot Snap(int revision, int step) =>
        new LobbyTutorialSnapshot { RunId = 1, Revision = revision, StepIndex = (byte)step };

    [SetUp]
    public void SetUp()
    {
        LobbyTutorialState.ResetForTests();
        _live = new FakeLive();
        _table = ScriptableObject.CreateInstance<LobbyTutorialStepTable>();
        _table.EditorSetSteps(new[]
        {
            new LobbyTutorialStep
            {
                stateId = "T10_Basic", kind = LobbyTutorialStepKind.Activity, activity = LobbyTutorialActivity.BasicCheck,
                titleKey = "LOBBY_TITLE_BASIC", objectiveKey = "LOBBY_OBJ_BASIC",
                checklistKeys = new[] { "LOBBY_CHECK_BASIC_ATTACK", "LOBBY_CHECK_Q", "LOBBY_CHECK_E", "LOBBY_CHECK_R", "LOBBY_CHECK_RMB", "LOBBY_CHECK_DASH" },
                progressMode = LobbyTutorialProgressMode.PartyComplete, progressKey = "LOBBY_PROGRESS_PARTY_DONE",
            },
            new LobbyTutorialStep { stateId = "D", kind = LobbyTutorialStepKind.Dialogue, dialogueId = "LOBBY_DLG_INTRO", objectiveKey = "LOBBY_OBJ_BASIC" },
            new LobbyTutorialStep
            {
                stateId = "T32", kind = LobbyTutorialStepKind.Activity, activity = LobbyTutorialActivity.VulnerableCc,
                titleKey = "LOBBY_TITLE_COUNTER", objectiveKey = "LOBBY_OBJ_VULN_CC", bodyKeys = new[] { "LOBBY_BODY_VULN_1", "LOBBY_BODY_VULN_2" },
            },
        });
    }

    [TearDown]
    public void TearDown()
    {
        LobbyTutorialState.ResetForTests();
        Object.DestroyImmediate(_table);
    }

    [Test]
    public void 진행_모드는_이름과_값이_같은_진행_줄_종류로()
    {
        Assert.That(LobbyTutorialUiBinding.ToProgressKind(LobbyTutorialProgressMode.None), Is.EqualTo(LobbyTutorialProgressKind.None));
        Assert.That(LobbyTutorialUiBinding.ToProgressKind(LobbyTutorialProgressMode.Gather), Is.EqualTo(LobbyTutorialProgressKind.Gather));
        Assert.That(LobbyTutorialUiBinding.ToProgressKind(LobbyTutorialProgressMode.PartyComplete), Is.EqualTo(LobbyTutorialProgressKind.PartyComplete));
        Assert.That(LobbyTutorialUiBinding.ToProgressKind(LobbyTutorialProgressMode.Countdown), Is.EqualTo(LobbyTutorialProgressKind.Countdown));
        Assert.That(LobbyTutorialUiBinding.ToProgressKind((LobbyTutorialProgressMode)99), Is.EqualTo(LobbyTutorialProgressKind.None));
    }

    [Test]
    public void 라커룸_테두리는_L00_동안만()
    {
        Assert.That(LobbyTutorialUiBinding.LockerAreaVisible(true, false, LobbyTutorialStepKind.LockerVisit), Is.True);
        Assert.That(LobbyTutorialUiBinding.LockerAreaVisible(true, false, LobbyTutorialStepKind.Gather), Is.False, "전원 방문 → L10 이면 끔");
        Assert.That(LobbyTutorialUiBinding.LockerAreaVisible(false, false, LobbyTutorialStepKind.LockerVisit), Is.False, "진행기 없음");
        Assert.That(LobbyTutorialUiBinding.LockerAreaVisible(true, true, LobbyTutorialStepKind.LockerVisit), Is.False, "출격 확정 뒤");
    }

    [Test]
    public void 단계표_id_는_Strings_키로_체크는_비트_순서대로()
    {
        var view = LobbyTutorialUiBinding.BuildObjective("LOBBY_TITLE_BASIC", "LOBBY_OBJ_BASIC", null,
            true, new[] { "LOBBY_CHECK_BASIC_ATTACK", "LOBBY_CHECK_Q", "LOBBY_CHECK_E" }, 0b101,
            LobbyTutorialProgressMode.PartyComplete, true, 1, 3);

        Assert.That(view.ChapterKey, Is.EqualTo(G + "LOBBY_TITLE_BASIC"));
        Assert.That(view.ObjectiveKey, Is.EqualTo(G + "LOBBY_OBJ_BASIC"));
        Assert.That(view.BodyKeys, Is.Empty);
        Assert.That(view.Checklist.Length, Is.EqualTo(3));
        Assert.That(view.Checklist[0].LabelKey, Is.EqualTo(G + "LOBBY_CHECK_BASIC_ATTACK"));
        Assert.That(view.Checklist[0].Done, Is.True);
        Assert.That(view.Checklist[1].Done, Is.False);
        Assert.That(view.Checklist[2].Done, Is.True);
        Assert.That(view.Progress.Kind, Is.EqualTo(LobbyTutorialProgressKind.PartyComplete));
        Assert.That(view.Progress.N, Is.EqualTo(1));
        Assert.That(view.Progress.Total, Is.EqualTo(3));
    }

    [Test]
    public void 진행_값이_없으면_진행_줄_끔_체크가_없으면_빈_목록()
    {
        var view = LobbyTutorialUiBinding.BuildObjective("T", "O", new[] { "B1", "" }, false, new[] { "C" }, 0b1,
            LobbyTutorialProgressMode.Gather, false, 2, 3);

        Assert.That(view.Progress.Kind, Is.EqualTo(LobbyTutorialProgressKind.None));
        Assert.That(LobbyTutorialUiRules.ShowProgress(view.Progress), Is.False);
        Assert.That(view.Checklist, Is.Empty);
        Assert.That(view.BodyKeys, Is.EqualTo(new[] { G + "B1", "" }));
    }

    [Test]
    public void 화자_칸이_비면_감독관()
    {
        Assert.That(LobbyTutorialUiBinding.SpeakerKeyOf(null), Is.EqualTo(LobbyTutorialUiStrings.SpeakerSupervisor.Key));
        Assert.That(LobbyTutorialUiBinding.SpeakerKeyOf(""), Is.EqualTo(LobbyTutorialUiStrings.SpeakerSupervisor.Key));
        Assert.That(LobbyTutorialUiBinding.SpeakerKeyOf("LOBBY_SPEAKER_SUPERVISOR"), Is.EqualTo(LobbyTutorialUiStrings.SpeakerSupervisor.Key));
    }

    [Test]
    public void 진행기_없으면_목표_패널_없음()
    {
        Assert.That(LobbyTutorialUiBinding.TryCaptureObjective(out _), Is.False);
    }

    [Test]
    public void 파사드_T10_은_체크와_훈련_완료_진행으로()
    {
        _live.Mask = 0b000011;
        _live.Done = 1;
        _live.Total = 2;
        LobbyTutorialState.SetLiveSource(_live);
        LobbyTutorialState.Publish(_table, Snap(1, 0));

        Assert.That(LobbyTutorialUiBinding.TryCaptureObjective(out var sig), Is.True);
        var view = LobbyTutorialUiBinding.BuildObjective(sig);
        Assert.That(view.ChapterKey, Is.EqualTo(G + "LOBBY_TITLE_BASIC"));
        Assert.That(view.Checklist.Length, Is.EqualTo(6));
        Assert.That(view.Checklist[0].Done && view.Checklist[1].Done, Is.True);
        Assert.That(view.Checklist[2].Done, Is.False);
        Assert.That(view.Progress, Is.EqualTo(new LobbyTutorialProgress(LobbyTutorialProgressKind.PartyComplete, 1, 2)));
    }

    [Test]
    public void 서명은_체크_진행_revision_이_바뀔_때만_달라진다()
    {
        LobbyTutorialState.SetLiveSource(_live);
        LobbyTutorialState.Publish(_table, Snap(1, 0));
        LobbyTutorialUiBinding.TryCaptureObjective(out var a);
        LobbyTutorialUiBinding.TryCaptureObjective(out var same);
        Assert.That(same, Is.EqualTo(a));

        _live.Mask = 0b1;
        LobbyTutorialUiBinding.TryCaptureObjective(out var checkedOne);
        Assert.That(checkedOne, Is.Not.EqualTo(a));

        _live.Done = 1;
        _live.Total = 1;
        LobbyTutorialUiBinding.TryCaptureObjective(out var progressed);
        Assert.That(progressed, Is.Not.EqualTo(checkedOne));

        LobbyTutorialState.Publish(_table, Snap(2, 2));
        LobbyTutorialUiBinding.TryCaptureObjective(out var next);
        Assert.That(next.Revision, Is.EqualTo(2));
        Assert.That(next.ChecklistMask, Is.EqualTo(-1));
        Assert.That(next.HasProgress, Is.False);
        Assert.That(LobbyTutorialUiBinding.BuildObjective(next).BodyKeys, Is.EqualTo(new[] { G + "LOBBY_BODY_VULN_1", G + "LOBBY_BODY_VULN_2" }));
    }

    [Test]
    public void 대사_단계와_늦은_참가자는_목표_패널_숨김()
    {
        LobbyTutorialState.SetLiveSource(_live);
        LobbyTutorialState.Publish(_table, Snap(1, 1));
        Assert.That(LobbyTutorialUiBinding.TryCaptureObjective(out _), Is.False);

        LobbyTutorialState.Publish(_table, Snap(2, 2));
        Assert.That(LobbyTutorialUiBinding.TryCaptureObjective(out _), Is.True);

        _live.LateJoiner = true;
        Assert.That(LobbyTutorialUiBinding.TryCaptureObjective(out _), Is.False);
    }
}
