using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

/// <summary>로비 무전 — 할 일 86 T6b(단계 → 대사 표, 회차당 1회, 단계 바뀜 = 미재생 폐기, 늦은 클라, 문구 행).</summary>
public sealed class LobbyRadioTests
{
    static readonly List<string> Lines = new List<string>();

    // 진행기 단계 모델을 그대로 돌려 무전 모델에 넣는다 — 실제 전이 순서와 어긋나지 않게.
    static List<string> Feed(LobbyRadioModel radio, LobbyTrainingFlow flow, List<int> serialChanges = null)
    {
        var played = new List<string>();
        if (radio.OnFlowChanged(flow.Stage, flow.Phase, Lines))
            serialChanges?.Add(radio.StageSerial);
        played.AddRange(Lines);
        return played;
    }

    static LobbyTrainingFlow CreateFlow()
    {
        var flow = new LobbyTrainingFlow();
        flow.AddClient(0);
        flow.AddClient(1);
        return flow;
    }

    [Test]
    public void 처음_본_상태는_기준만_잡고_대사가_없다()
    {
        var radio = new LobbyRadioModel();
        Assert.That(radio.OnFlowChanged(LobbyTrainingStage.Lobby, LobbyTrainingPhase.Gathering, Lines), Is.False);
        Assert.That(Lines, Is.Empty);
        Assert.That(radio.StageSerial, Is.EqualTo(0));
    }

    [Test]
    public void 늦게_구독해도_지금_단계_대사를_다시_틀지_않는다()
    {
        var radio = new LobbyRadioModel();
        radio.OnFlowChanged(LobbyTrainingStage.Train02, LobbyTrainingPhase.InProgress, Lines);
        Assert.That(Lines, Is.Empty);
        Assert.That(radio.HasPlayed(LobbyRadioIds.Train02Start), Is.False);
    }

    [Test]
    public void 훈련_전체_진행의_단계_진입_대사가_표_순서대로_한_번씩_나온다()
    {
        var flow = CreateFlow();
        var radio = new LobbyRadioModel();
        var all = new List<string>();
        var serials = new List<int>();
        Feed(radio, flow);

        void Step(bool ok)
        {
            Assert.That(ok, Is.True);
            all.AddRange(Feed(radio, flow, serials));
        }

        Step(flow.OnGatherConfirmed(LobbyGatherPoint.Entrance));
        Step(flow.OnGatherConfirmed(LobbyGatherPoint.Train01Start));
        Step(flow.ReportStageCompleted(LobbyTrainingStage.Train01));
        Step(flow.OnGatherConfirmed(LobbyGatherPoint.Train02Start));
        Step(flow.ReportStageCompleted(LobbyTrainingStage.Train02));
        Step(flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area));
        Step(flow.ReportStageCompleted(LobbyTrainingStage.Train03A));
        Step(flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area));
        Step(flow.ReportStageCompleted(LobbyTrainingStage.Train03B));
        Step(flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area));
        Step(flow.ReportStageCompleted(LobbyTrainingStage.Train03C));
        Step(flow.ReportStageCompleted(LobbyTrainingStage.Train03D));
        Step(flow.OnGatherConfirmed(LobbyGatherPoint.TrainEnd));
        Step(flow.OnGatherConfirmed(LobbyGatherPoint.Brief));
        Step(flow.ReportStageCompleted(LobbyTrainingStage.Brief));
        Step(flow.OnGatherConfirmed(LobbyGatherPoint.Sortie));

        CollectionAssert.AreEqual(new[]
        {
            LobbyRadioIds.LobbyTrainRoute, LobbyRadioIds.Train01Start, LobbyRadioIds.Train01End, LobbyRadioIds.Train02Start,
            LobbyRadioIds.Train02End, LobbyRadioIds.Train03Start, LobbyRadioIds.Train03Cc,
            LobbyRadioIds.Train03Reset, LobbyRadioIds.Train03WallGuide, LobbyRadioIds.Train03Suppress,
            LobbyRadioIds.TrainEnd, LobbyRadioIds.BriefRoute,
        }, all);

        // 단계가 바뀐 전이마다 번호 1씩: Train01·02·03A·03B·03C·03D·TrainEnd·Brief·Sortie = 9번.
        CollectionAssert.AreEqual(Enumerable.Range(1, 9), serials);
    }

    [Test]
    public void 훈련_없이_출격하면_대사_없이_단계만_바뀐다()
    {
        var flow = CreateFlow();
        var radio = new LobbyRadioModel();
        Feed(radio, flow);
        var serials = new List<int>();

        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Sortie), Is.True);
        Assert.That(Feed(radio, flow, serials), Is.Empty);
        CollectionAssert.AreEqual(new[] { 1 }, serials);
    }

    [Test]
    public void 하위_상태만_바뀌면_단계_번호는_그대로다()
    {
        var radio = new LobbyRadioModel();
        radio.OnFlowChanged(LobbyTrainingStage.Train01, LobbyTrainingPhase.Gathering, Lines);
        Assert.That(radio.OnFlowChanged(LobbyTrainingStage.Train01, LobbyTrainingPhase.InProgress, Lines), Is.False);
        CollectionAssert.AreEqual(new[] { LobbyRadioIds.Train01Start }, Lines);
        Assert.That(radio.StageSerial, Is.EqualTo(0));
    }

    [Test]
    public void 같은_상태_중복_알림은_무시된다()
    {
        var radio = new LobbyRadioModel();
        radio.OnFlowChanged(LobbyTrainingStage.Lobby, LobbyTrainingPhase.Gathering, Lines);
        radio.OnFlowChanged(LobbyTrainingStage.Train01, LobbyTrainingPhase.Gathering, Lines);
        Assert.That(radio.OnFlowChanged(LobbyTrainingStage.Train01, LobbyTrainingPhase.Gathering, Lines), Is.False);
        Assert.That(Lines, Is.Empty);
        Assert.That(radio.StageSerial, Is.EqualTo(1));
    }

    [Test]
    public void 단계_안_사건_대사는_회차당_한_번()
    {
        var radio = new LobbyRadioModel();
        Assert.That(radio.TryPlay(LobbyRadioIds.Train03Counter, true), Is.True);
        Assert.That(radio.TryPlay(LobbyRadioIds.Train03Counter, true), Is.False);
        Assert.That(radio.TryPlay(LobbyRadioIds.Train03Counter, false), Is.True, "oncePerRound=false 는 반복 허용");
        Assert.That(radio.TryPlay(string.Empty, false), Is.False);
    }

    [Test]
    public void 사건으로_먼저_튼_id_는_단계_진입_때_다시_나오지_않는다()
    {
        var radio = new LobbyRadioModel();
        radio.OnFlowChanged(LobbyTrainingStage.Train03C, LobbyTrainingPhase.InProgress, Lines);
        Assert.That(radio.TryPlay(LobbyRadioIds.Train03Suppress, true), Is.True);
        radio.OnFlowChanged(LobbyTrainingStage.Train03D, LobbyTrainingPhase.InProgress, Lines);
        Assert.That(Lines, Is.Empty);
    }

    [Test]
    public void 새_회차는_다시_틀_수_있고_단계_번호는_계속_오른다()
    {
        var radio = new LobbyRadioModel();
        radio.OnFlowChanged(LobbyTrainingStage.Lobby, LobbyTrainingPhase.Gathering, Lines);
        radio.OnFlowChanged(LobbyTrainingStage.Train01, LobbyTrainingPhase.Gathering, Lines);
        radio.TryPlay(LobbyRadioIds.Train03Wall, true);
        var before = radio.StageSerial;

        radio.Reset();
        Assert.That(radio.StageSerial, Is.GreaterThan(before));
        radio.OnFlowChanged(LobbyTrainingStage.Lobby, LobbyTrainingPhase.Gathering, Lines);
        radio.OnFlowChanged(LobbyTrainingStage.Train01, LobbyTrainingPhase.Gathering, Lines);
        CollectionAssert.AreEqual(new[] { LobbyRadioIds.LobbyTrainRoute }, Lines);
        Assert.That(radio.TryPlay(LobbyRadioIds.Train03Wall, true), Is.True);
    }

    // ---------------- 클라 수신 ----------------

    [Test]
    public void 수신_더_큰_단계_번호면_대기_줄을_먼저_버린다()
    {
        var receiver = new LobbyRadioReceiver();
        Assert.That(receiver.AcceptLine(0, out var discard), Is.True);
        Assert.That(discard, Is.False);

        Assert.That(receiver.AcceptLine(2, out discard), Is.True);
        Assert.That(discard, Is.True);
        Assert.That(receiver.AcceptLine(2, out discard), Is.True);
        Assert.That(discard, Is.False);
    }

    [Test]
    public void 수신_지난_단계_대사는_버린다()
    {
        var receiver = new LobbyRadioReceiver();
        Assert.That(receiver.OnStageAdvanced(3), Is.True);
        Assert.That(receiver.OnStageAdvanced(3), Is.False);
        Assert.That(receiver.AcceptLine(2, out _), Is.False);
    }

    [Test]
    public void 늦게_붙은_클라는_다음_대사부터_받는다()
    {
        var receiver = new LobbyRadioReceiver();   // 서버는 이미 번호 5 — 지난 대사는 다시 오지 않는다
        Assert.That(receiver.AcceptLine(5, out var discard), Is.True);
        Assert.That(discard, Is.True, "비어 있는 대기 줄을 비우는 것뿐 — 해 없음");
    }

    // ---------------- 큐 ----------------

    [Test]
    public void 큐_일반_무전은_하나씩_순서대로()
    {
        var queue = new TutorialSubtitleQueue();
        Assert.That(queue.Enqueue(new TutorialSubtitle("A", "a", 2f)), Is.True);
        Assert.That(queue.Enqueue(new TutorialSubtitle("B", "b", 2f)), Is.False);
        Assert.That(queue.Current.Id, Is.EqualTo("A"));
        queue.Advance(2f);
        Assert.That(queue.Current.Id, Is.EqualTo("B"));
    }

    [Test]
    public void 큐_미재생_폐기는_지금_뜬_줄을_남기고_대기_줄만_버린다()
    {
        var queue = new TutorialSubtitleQueue();
        queue.Enqueue(new TutorialSubtitle("A", "a", 2f));
        queue.Enqueue(new TutorialSubtitle("B", "b", 2f));
        queue.Advance(0.5f);

        queue.ClearPending();
        Assert.That(queue.HasCurrent, Is.True);
        Assert.That(queue.Current.Id, Is.EqualTo("A"));
        Assert.That(queue.Elapsed, Is.EqualTo(0.5f).Within(1e-4f));
        Assert.That(queue.PendingCount, Is.EqualTo(0));

        queue.Enqueue(new TutorialSubtitle("C", "c", 2f));
        queue.Advance(1.5f);
        Assert.That(queue.Current.Id, Is.EqualTo("C"));
    }

    // ---------------- 문구 ----------------

    [Test]
    public void 문구_초안이_모든_무전_id_를_한_번씩_덮는다()
    {
        CollectionAssert.AreEquivalent(LobbyRadioIds.All, TutorialGuideUIAuthoring.LobbyRadioTexts.Select(t => t.id));
        Assert.That(TutorialGuideUIAuthoring.LobbyRadioTexts.All(t => !string.IsNullOrWhiteSpace(t.text) && t.seconds > 0f), Is.True);
    }

    [Test]
    public void 단계_진입_대사는_전부_무전_id_다()
    {
        foreach (LobbyTrainingStage stage in System.Enum.GetValues(typeof(LobbyTrainingStage)))
        foreach (LobbyTrainingPhase phase in System.Enum.GetValues(typeof(LobbyTrainingPhase)))
        foreach (var id in LobbyRadioSchedule.LinesOnEnter(stage, phase))
            Assert.That(LobbyRadioIds.IsLobbyRadio(id), Is.True, $"{stage}/{phase} → {id}");
    }

    [Test]
    public void 문구_에셋에_모든_무전_행이_있다()
    {
        var table = AssetDatabase.LoadAssetAtPath<TutorialGuideTextTable>(TutorialGuideTextTable.AssetPath);
        Assume.That(table, Is.Not.Null, "TutorialGuideTexts 에셋 없음");
        foreach (var id in LobbyRadioIds.All)
        {
            Assert.That(table.TryGetRow(id, out var row), Is.True, $"{id} 행 없음 — 메뉴 Tools/UI/로비 무전 문구 추가");
            Assert.That(row.Text, Is.Not.Empty, id);
        }
    }
}
