using NUnit.Framework;

/// <summary>로비 훈련 단계 모델 — 할 일 86 PLAN §3.2·§7(앞으로만·1회 전이, 훈련 선택, 늦은 참가자, 새 회차 초기화).</summary>
public sealed class LobbyTrainingFlowTests
{
    static LobbyTrainingFlow Create(params ulong[] clientIds)
    {
        var flow = new LobbyTrainingFlow();
        foreach (var clientId in clientIds)
            flow.AddClient(clientId);
        return flow;
    }

    static void AssertAt(LobbyTrainingFlow flow, LobbyTrainingStage stage, LobbyTrainingPhase phase)
    {
        Assert.That(flow.Stage, Is.EqualTo(stage));
        Assert.That(flow.Phase, Is.EqualTo(phase));
    }

    // 입구 → … → 브리핑 완료(출격 집결 활성)까지 정상 진행.
    static void RunTrainingToBriefCompleted(LobbyTrainingFlow flow)
    {
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Entrance), Is.True);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Train01Start), Is.True);
        Assert.That(flow.ReportStageCompleted(LobbyTrainingStage.Train01), Is.True);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Train02Start), Is.True);
        Assert.That(flow.ReportStageCompleted(LobbyTrainingStage.Train02), Is.True);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area), Is.True);
        Assert.That(flow.ReportStageCompleted(LobbyTrainingStage.Train03A), Is.True);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area), Is.True);
        Assert.That(flow.ReportStageCompleted(LobbyTrainingStage.Train03B), Is.True);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area), Is.True);
        Assert.That(flow.ReportStageCompleted(LobbyTrainingStage.Train03C), Is.True);
        Assert.That(flow.ReportStageCompleted(LobbyTrainingStage.Train03D), Is.True);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.TrainEnd), Is.True);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Brief), Is.True);
        Assert.That(flow.ReportStageCompleted(LobbyTrainingStage.Brief), Is.True);
    }

    [Test]
    public void 시작은_Lobby_집결_대기이고_입구와_출격이_둘_다_활성()
    {
        var flow = Create(0);

        AssertAt(flow, LobbyTrainingStage.Lobby, LobbyTrainingPhase.Gathering);
        Assert.That(flow.IsPointActive(LobbyGatherPoint.Entrance), Is.True);
        Assert.That(flow.IsPointActive(LobbyGatherPoint.Sortie), Is.True);
        Assert.That(flow.IsPointActive(LobbyGatherPoint.Train01Start), Is.False);
        Assert.That(flow.IsCharacterLocked, Is.False);
    }

    [Test]
    public void 전체_전이_순서()
    {
        var flow = Create(0, 1);

        flow.OnGatherConfirmed(LobbyGatherPoint.Entrance);
        AssertAt(flow, LobbyTrainingStage.Train01, LobbyTrainingPhase.Gathering);   // 입구 확정 ≠ TRAIN_01 시작
        flow.OnGatherConfirmed(LobbyGatherPoint.Train01Start);
        AssertAt(flow, LobbyTrainingStage.Train01, LobbyTrainingPhase.InProgress);
        flow.ReportStageCompleted(LobbyTrainingStage.Train01);
        AssertAt(flow, LobbyTrainingStage.Train02, LobbyTrainingPhase.Gathering);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train02Start);
        AssertAt(flow, LobbyTrainingStage.Train02, LobbyTrainingPhase.InProgress);
        flow.ReportStageCompleted(LobbyTrainingStage.Train02);
        AssertAt(flow, LobbyTrainingStage.Train03A, LobbyTrainingPhase.Gathering);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area);
        AssertAt(flow, LobbyTrainingStage.Train03A, LobbyTrainingPhase.InProgress);
        flow.ReportStageCompleted(LobbyTrainingStage.Train03A);
        AssertAt(flow, LobbyTrainingStage.Train03A, LobbyTrainingPhase.Completed);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area);
        AssertAt(flow, LobbyTrainingStage.Train03B, LobbyTrainingPhase.InProgress);
        flow.ReportStageCompleted(LobbyTrainingStage.Train03B);
        AssertAt(flow, LobbyTrainingStage.Train03B, LobbyTrainingPhase.Completed);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area);
        AssertAt(flow, LobbyTrainingStage.Train03C, LobbyTrainingPhase.InProgress);
        flow.ReportStageCompleted(LobbyTrainingStage.Train03C);
        AssertAt(flow, LobbyTrainingStage.Train03D, LobbyTrainingPhase.InProgress);   // C→D 즉시
        flow.ReportStageCompleted(LobbyTrainingStage.Train03D);
        AssertAt(flow, LobbyTrainingStage.TrainEnd, LobbyTrainingPhase.Gathering);
        flow.OnGatherConfirmed(LobbyGatherPoint.TrainEnd);
        AssertAt(flow, LobbyTrainingStage.Brief, LobbyTrainingPhase.Gathering);
        flow.OnGatherConfirmed(LobbyGatherPoint.Brief);
        AssertAt(flow, LobbyTrainingStage.Brief, LobbyTrainingPhase.InProgress);
        flow.ReportStageCompleted(LobbyTrainingStage.Brief);
        AssertAt(flow, LobbyTrainingStage.Brief, LobbyTrainingPhase.Completed);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Sortie), Is.True);
        Assert.That(flow.Stage, Is.EqualTo(LobbyTrainingStage.Sortie));
    }

    [Test]
    public void Train03_AB_완료_뒤만_영역_확인_전용이고_C에서는_구역이_꺼진다()
    {
        var flow = Create(0);
        flow.OnGatherConfirmed(LobbyGatherPoint.Entrance);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train01Start);
        flow.ReportStageCompleted(LobbyTrainingStage.Train01);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train02Start);
        flow.ReportStageCompleted(LobbyTrainingStage.Train02);

        Assert.That(flow.IsAreaCheckOnly, Is.False);   // 세 번째 시작 집결은 유지 시간 그대로
        flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area);
        Assert.That(flow.IsPointActive(LobbyGatherPoint.Train03Area), Is.False);   // 진행 중엔 꺼짐
        flow.ReportStageCompleted(LobbyTrainingStage.Train03A);
        Assert.That(flow.IsPointActive(LobbyGatherPoint.Train03Area), Is.True);
        Assert.That(flow.IsAreaCheckOnly, Is.True);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area);
        flow.ReportStageCompleted(LobbyTrainingStage.Train03B);
        Assert.That(flow.IsAreaCheckOnly, Is.True);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area);

        Assert.That(flow.Stage, Is.EqualTo(LobbyTrainingStage.Train03C));
        Assert.That(flow.IsPointActive(LobbyGatherPoint.Train03Area), Is.False);
        Assert.That(flow.IsAreaCheckOnly, Is.False);
    }

    [Test]
    public void 같은_집결_확정_중복은_무시()
    {
        var flow = Create(0);

        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Entrance), Is.True);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Entrance), Is.False);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Train01Start), Is.True);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Train01Start), Is.False);
        AssertAt(flow, LobbyTrainingStage.Train01, LobbyTrainingPhase.InProgress);
    }

    [Test]
    public void 같은_단계_완료_중복_보고는_무시()
    {
        var flow = Create(0);
        flow.OnGatherConfirmed(LobbyGatherPoint.Entrance);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train01Start);

        Assert.That(flow.ReportStageCompleted(LobbyTrainingStage.Train01), Is.True);
        Assert.That(flow.ReportStageCompleted(LobbyTrainingStage.Train01), Is.False);
        AssertAt(flow, LobbyTrainingStage.Train02, LobbyTrainingPhase.Gathering);
    }

    [Test]
    public void 지난_단계_보고와_비활성_구역_확정은_뒤로_가지_않는다()
    {
        var flow = Create(0);
        flow.OnGatherConfirmed(LobbyGatherPoint.Entrance);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train01Start);
        flow.ReportStageCompleted(LobbyTrainingStage.Train01);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train02Start);

        Assert.That(flow.ReportStageCompleted(LobbyTrainingStage.Train01), Is.False);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Entrance), Is.False);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Train01Start), Is.False);
        Assert.That(flow.ReportStageCompleted(LobbyTrainingStage.Lobby), Is.False);
        AssertAt(flow, LobbyTrainingStage.Train02, LobbyTrainingPhase.InProgress);
    }

    [Test]
    public void 현재가_아닌_단계_완료_보고는_무시()
    {
        var flow = Create(0);
        flow.OnGatherConfirmed(LobbyGatherPoint.Entrance);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train01Start);

        Assert.That(flow.ReportStageCompleted(LobbyTrainingStage.Train02), Is.False);
        Assert.That(flow.ReportStageCompleted(LobbyTrainingStage.Train03C), Is.False);
        Assert.That(flow.ReportStageCompleted(LobbyTrainingStage.Brief), Is.False);
        AssertAt(flow, LobbyTrainingStage.Train01, LobbyTrainingPhase.InProgress);
    }

    [Test]
    public void 집결_대기_중_완료_보고는_무시_시작_위치_집결이_먼저()
    {
        var flow = Create(0);
        flow.OnGatherConfirmed(LobbyGatherPoint.Entrance);

        Assert.That(flow.ReportStageCompleted(LobbyTrainingStage.Train01), Is.False);
        AssertAt(flow, LobbyTrainingStage.Train01, LobbyTrainingPhase.Gathering);
    }

    [Test]
    public void 집결로_끝나는_단계는_보고로_넘어가지_않는다()
    {
        var flow = Create(0);
        Assert.That(flow.ReportStageCompleted(LobbyTrainingStage.Lobby), Is.False);

        flow.OnGatherConfirmed(LobbyGatherPoint.Entrance);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train01Start);
        flow.ReportStageCompleted(LobbyTrainingStage.Train01);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train02Start);
        flow.ReportStageCompleted(LobbyTrainingStage.Train02);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area);
        flow.ReportStageCompleted(LobbyTrainingStage.Train03A);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area);
        flow.ReportStageCompleted(LobbyTrainingStage.Train03B);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area);
        flow.ReportStageCompleted(LobbyTrainingStage.Train03C);
        flow.ReportStageCompleted(LobbyTrainingStage.Train03D);

        Assert.That(flow.ReportStageCompleted(LobbyTrainingStage.TrainEnd), Is.False);
        AssertAt(flow, LobbyTrainingStage.TrainEnd, LobbyTrainingPhase.Gathering);
    }

    [Test]
    public void 훈련은_선택_Lobby_에서_출격_확정이면_바로_Sortie()
    {
        var flow = Create(0, 1);

        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Sortie), Is.True);

        Assert.That(flow.Stage, Is.EqualTo(LobbyTrainingStage.Sortie));
        Assert.That(flow.HasStartedTraining, Is.False);
        Assert.That(flow.IsPointActive(LobbyGatherPoint.Entrance), Is.False);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Entrance), Is.False);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Sortie), Is.False);
    }

    [Test]
    public void 훈련_중엔_출격_비활성_브리핑_완료_뒤_활성()
    {
        var flow = Create(0);
        flow.OnGatherConfirmed(LobbyGatherPoint.Entrance);

        Assert.That(flow.IsPointActive(LobbyGatherPoint.Sortie), Is.False);
        Assert.That(flow.IsPointActive(LobbyGatherPoint.Entrance), Is.False);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Sortie), Is.False);
        Assert.That(flow.IsTrainingInProgress, Is.True);

        flow.OnGatherConfirmed(LobbyGatherPoint.Train01Start);
        flow.ReportStageCompleted(LobbyTrainingStage.Train01);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train02Start);
        flow.ReportStageCompleted(LobbyTrainingStage.Train02);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area);
        flow.ReportStageCompleted(LobbyTrainingStage.Train03A);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area);
        flow.ReportStageCompleted(LobbyTrainingStage.Train03B);
        flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area);
        flow.ReportStageCompleted(LobbyTrainingStage.Train03C);
        flow.ReportStageCompleted(LobbyTrainingStage.Train03D);
        flow.OnGatherConfirmed(LobbyGatherPoint.TrainEnd);
        flow.OnGatherConfirmed(LobbyGatherPoint.Brief);
        Assert.That(flow.IsPointActive(LobbyGatherPoint.Sortie), Is.False);   // 브리핑 중

        flow.ReportStageCompleted(LobbyTrainingStage.Brief);

        Assert.That(flow.IsPointActive(LobbyGatherPoint.Sortie), Is.True);
        Assert.That(flow.IsPointActive(LobbyGatherPoint.Brief), Is.False);
        Assert.That(flow.IsTrainingInProgress, Is.False);
    }

    [Test]
    public void 단계마다_켜지는_구역은_하나_Lobby_와_브리핑_완료만_예외()
    {
        var flow = Create(0);
        RunTrainingToBriefCompleted(flow);
        Assert.That(flow.IsPointActive(LobbyGatherPoint.Sortie), Is.True);

        var activeCount = 0;
        foreach (LobbyGatherPoint point in System.Enum.GetValues(typeof(LobbyGatherPoint)))
        {
            if (point != LobbyGatherPoint.None && flow.IsPointActive(point))
                activeCount++;
        }

        Assert.That(activeCount, Is.EqualTo(1));
    }

    [Test]
    public void 입구_확정_때_훈련_명단_고정_늦은_참가자는_출격_인원에만()
    {
        var flow = Create(0, 1);
        flow.OnGatherConfirmed(LobbyGatherPoint.Entrance);

        flow.AddClient(2);

        Assert.That(flow.TrainingRoster, Is.EquivalentTo(new ulong[] { 0, 1 }));
        Assert.That(flow.ParticipantsFor(LobbyGatherPoint.Train01Start), Is.EquivalentTo(new ulong[] { 0, 1 }));
        Assert.That(flow.ParticipantsFor(LobbyGatherPoint.Brief), Is.EquivalentTo(new ulong[] { 0, 1 }));
        Assert.That(flow.ParticipantsFor(LobbyGatherPoint.Sortie), Is.EquivalentTo(new ulong[] { 0, 1, 2 }));
        Assert.That(flow.IsWaitingLateJoiner(2), Is.True);
        Assert.That(flow.IsWaitingLateJoiner(1), Is.False);
    }

    [Test]
    public void Lobby_에서_접속한_사람은_훈련_명단에_들어간다()
    {
        var flow = Create(0);
        flow.AddClient(1);

        Assert.That(flow.IsWaitingLateJoiner(1), Is.False);
        Assert.That(flow.ParticipantsFor(LobbyGatherPoint.Entrance), Is.EquivalentTo(new ulong[] { 0, 1 }));

        flow.OnGatherConfirmed(LobbyGatherPoint.Entrance);
        Assert.That(flow.TrainingRoster, Is.EquivalentTo(new ulong[] { 0, 1 }));
    }

    [Test]
    public void 연결_종료자는_모든_명단에서_빠진다()
    {
        var flow = Create(0, 1, 2);
        flow.OnGatherConfirmed(LobbyGatherPoint.Entrance);
        flow.AddClient(3);

        Assert.That(flow.RemoveClient(1), Is.True);
        Assert.That(flow.RemoveClient(3), Is.True);
        Assert.That(flow.RemoveClient(9), Is.False);

        Assert.That(flow.TrainingRoster, Is.EquivalentTo(new ulong[] { 0, 2 }));
        Assert.That(flow.ParticipantsFor(LobbyGatherPoint.Sortie), Is.EquivalentTo(new ulong[] { 0, 2 }));
        Assert.That(flow.IsWaitingLateJoiner(3), Is.False);
    }

    [Test]
    public void 캐릭터_잠금은_입구_확정_뒤부터()
    {
        var flow = Create(0);
        Assert.That(flow.IsCharacterLocked, Is.False);

        flow.OnGatherConfirmed(LobbyGatherPoint.Entrance);

        Assert.That(flow.IsCharacterLocked, Is.True);
    }

    [Test]
    public void 새_회차는_Lobby_부터_기록_초기화_접속자는_유지()
    {
        var flow = Create(0, 1);
        RunTrainingToBriefCompleted(flow);
        flow.OnGatherConfirmed(LobbyGatherPoint.Sortie);

        flow.Reset();

        AssertAt(flow, LobbyTrainingStage.Lobby, LobbyTrainingPhase.Gathering);
        Assert.That(flow.HasStartedTraining, Is.False);
        Assert.That(flow.TrainingRoster, Is.Empty);
        Assert.That(flow.Connected, Is.EquivalentTo(new ulong[] { 0, 1 }));
        Assert.That(flow.IsPointActive(LobbyGatherPoint.Entrance), Is.True);
        Assert.That(flow.IsPointActive(LobbyGatherPoint.Sortie), Is.True);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Entrance), Is.True);
    }

    [Test]
    public void None_구역_확정은_무시()
    {
        var flow = Create(0);

        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.None), Is.False);
        AssertAt(flow, LobbyTrainingStage.Lobby, LobbyTrainingPhase.Gathering);
    }
}
