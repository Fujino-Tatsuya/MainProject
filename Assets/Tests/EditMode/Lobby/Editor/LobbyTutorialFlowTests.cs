using NUnit.Framework;

/// <summary>로비 튜토리얼 순수 모델 — 할 일 157 PLAN §2(앞으로만·revision·runId·한 프레임 한 전이·참여자·라커 누적·캐릭터 교체 무관).</summary>
public sealed class LobbyTutorialFlowTests
{
    const int Run = 7;

    static readonly LobbyTutorialStepKind[] Kinds =
    {
        LobbyTutorialStepKind.LockerVisit,  // 0 L00
        LobbyTutorialStepKind.Gather,       // 1 L10
        LobbyTutorialStepKind.Dialogue,     // 2 L11
        LobbyTutorialStepKind.Activity,     // 3
        LobbyTutorialStepKind.Loading,      // 4
    };

    static LobbyTutorialFlow Create(params ulong[] clientIds)
    {
        var flow = new LobbyTutorialFlow(Kinds, Run);
        foreach (var clientId in clientIds)
            flow.AddClient(clientId);
        return flow;
    }

    [Test]
    public void 시작은_0행_revision_1()
    {
        var flow = Create(0);

        Assert.That(flow.StepIndex, Is.EqualTo(0));
        Assert.That(flow.Revision, Is.EqualTo(1));
        Assert.That(flow.RunId, Is.EqualTo(Run));
        Assert.That(flow.CurrentKind, Is.EqualTo(LobbyTutorialStepKind.LockerVisit));
        Assert.That(flow.Snapshot.IsStarted, Is.True);
    }

    [Test]
    public void runId_0_은_예약값()
    {
        Assert.Throws<System.ArgumentException>(() => new LobbyTutorialFlow(Kinds, 0));
    }

    [Test]
    public void 전이마다_revision_증가와_진입_시각()
    {
        var flow = Create(0);

        Assert.That(flow.TryAdvance(Run, 1, 10, 5.5), Is.EqualTo(LobbyTutorialAdvanceResult.Advanced));
        Assert.That(flow.StepIndex, Is.EqualTo(1));
        Assert.That(flow.Revision, Is.EqualTo(2));
        Assert.That(flow.Snapshot.EnteredAtServerTime, Is.EqualTo(5.5));
        Assert.That(flow.EnteredFrame, Is.EqualTo(10));
    }

    [Test]
    public void 옛_revision_과_다른_회차_요청은_거부()
    {
        var flow = Create(0);
        flow.TryAdvance(Run, 1, 10, 0);

        Assert.That(flow.TryAdvance(Run, 1, 11, 0), Is.EqualTo(LobbyTutorialAdvanceResult.StaleRevision));   // 중복 보고
        Assert.That(flow.TryAdvance(Run + 1, 2, 11, 0), Is.EqualTo(LobbyTutorialAdvanceResult.StaleRun));
        Assert.That(flow.StepIndex, Is.EqualTo(1));
    }

    [Test]
    public void 역행과_제자리는_무시()
    {
        var flow = Create(0);
        flow.TryAdvanceTo(Run, 1, 3, 10, 0);

        Assert.That(flow.TryAdvanceTo(Run, 2, 1, 11, 0), Is.EqualTo(LobbyTutorialAdvanceResult.Backward));
        Assert.That(flow.TryAdvanceTo(Run, 2, 3, 11, 0), Is.EqualTo(LobbyTutorialAdvanceResult.Backward));
        Assert.That(flow.StepIndex, Is.EqualTo(3));
    }

    [Test]
    public void 마지막_행_다음은_범위_밖()
    {
        var flow = Create(0);
        flow.TryAdvanceTo(Run, 1, 4, 10, 0);

        Assert.That(flow.IsLastStep, Is.True);
        Assert.That(flow.TryAdvance(Run, 2, 11, 0), Is.EqualTo(LobbyTutorialAdvanceResult.OutOfRange));
    }

    [Test]
    public void 참여자_0_이면_전이_금지()
    {
        var flow = Create();

        Assert.That(flow.TryAdvance(Run, 1, 10, 0), Is.EqualTo(LobbyTutorialAdvanceResult.NoParticipants));
        Assert.That(flow.StepIndex, Is.EqualTo(0));
    }

    [Test]
    public void 같은_프레임_두번째_전이는_다음_프레임으로()
    {
        var flow = Create(0);
        flow.TryAdvance(Run, 1, 10, 0);

        Assert.That(flow.TryAdvance(Run, 2, 10, 1), Is.EqualTo(LobbyTutorialAdvanceResult.Deferred));
        Assert.That(flow.StepIndex, Is.EqualTo(1));
        Assert.That(flow.HasPendingAdvance, Is.True);
        Assert.That(flow.Tick(10), Is.False);   // 같은 프레임엔 아직

        Assert.That(flow.Tick(11), Is.True);
        Assert.That(flow.StepIndex, Is.EqualTo(2));
        Assert.That(flow.Revision, Is.EqualTo(3));
        Assert.That(flow.HasPendingAdvance, Is.False);
        Assert.That(flow.Tick(12), Is.False);
    }

    [Test]
    public void 진입_프레임의_사건은_받지_않는다()
    {
        var flow = Create(0);
        flow.TryAdvance(Run, 1, 10, 0);

        Assert.That(flow.AcceptsEvent(Run, 2, 10), Is.False);   // T31 성공과 같은 프레임 → T32 완료 금지
        Assert.That(flow.AcceptsEvent(Run, 2, 11), Is.True);
        Assert.That(flow.AcceptsEvent(Run, 1, 11), Is.False);   // 이전 단계 콜백
        Assert.That(flow.AcceptsEvent(Run + 1, 2, 11), Is.False);
    }

    [Test]
    public void 라커_방문은_1인_1회_누적()
    {
        var flow = Create(0, 1);

        Assert.That(flow.MarkLockerVisited(0), Is.True);
        Assert.That(flow.MarkLockerVisited(0), Is.False);   // 재진입
        Assert.That(flow.AllParticipantsVisitedLocker(), Is.False);
        Assert.That(flow.MarkLockerVisited(1), Is.True);
        Assert.That(flow.AllParticipantsVisitedLocker(), Is.True);
    }

    [Test]
    public void 라커_방문은_참여자만_LockerVisit_단계에서만()
    {
        var flow = Create(0);

        Assert.That(flow.MarkLockerVisited(9), Is.False);   // 스폰 전·접속자 아님
        flow.TryAdvance(Run, 1, 10, 0);
        Assert.That(flow.MarkLockerVisited(0), Is.False);
        Assert.That(flow.HasVisitedLocker(0), Is.False);
    }

    [Test]
    public void 라커_방문_기록은_단계가_지나도_남는다()
    {
        var flow = Create(0);
        flow.MarkLockerVisited(0);
        flow.TryAdvance(Run, 1, 10, 0);

        Assert.That(flow.HasVisitedLocker(0), Is.True);
    }

    [Test]
    public void 참여자_0_이면_라커_전원_방문_아님()
    {
        Assert.That(Create().AllParticipantsVisitedLocker(), Is.False);
    }

    [Test]
    public void 미방문자가_나가면_남은_전원_방문으로()
    {
        var flow = Create(0, 1);
        flow.MarkLockerVisited(0);

        Assert.That(flow.RemoveClient(1), Is.True);
        Assert.That(flow.AllParticipantsVisitedLocker(), Is.True);
    }

    [Test]
    public void 명단_고정_뒤_접속은_늦은_참가자()
    {
        var flow = Create(0, 1);

        Assert.That(flow.LockRoster(), Is.True);
        Assert.That(flow.LockRoster(), Is.False);
        flow.AddClient(2);

        Assert.That(flow.Roster, Is.EqualTo(new ulong[] { 0, 1 }));
        Assert.That(flow.Participants, Is.EqualTo(new ulong[] { 0, 1 }));
        Assert.That(flow.IsWaitingLateJoiner(2), Is.True);
        Assert.That(flow.IsWaitingLateJoiner(0), Is.False);
        Assert.That(flow.Connected.Count, Is.EqualTo(3));
    }

    [Test]
    public void 명단_전원이_나가면_전이_금지()
    {
        var flow = Create(0);
        flow.LockRoster();
        flow.AddClient(2);
        flow.RemoveClient(0);

        Assert.That(flow.TryAdvance(Run, 1, 10, 0), Is.EqualTo(LobbyTutorialAdvanceResult.NoParticipants));
    }

    [Test]
    public void 캐릭터_교체는_기록을_건드리지_않는다()
    {
        // 교체 = 같은 clientId 의 PlayerObject 디스폰·재스폰. 모델은 clientId 만 알고, 교체 경로는 AddClient/RemoveClient 를 부르지 않는다(PLAN §10.2).
        var flow = Create(0, 1);
        flow.MarkLockerVisited(0);
        flow.LockRoster();

        Assert.That(flow.AddClient(0), Is.False);   // 재스폰 알림이 와도 중복 추가 없음
        Assert.That(flow.HasVisitedLocker(0), Is.True);
        Assert.That(flow.Roster, Is.EqualTo(new ulong[] { 0, 1 }));
    }

    [Test]
    public void 대사_시각은_현재_revision_에만()
    {
        var flow = Create(0);
        flow.TryAdvance(Run, 1, 10, 0);

        Assert.That(flow.SetTimer(1, 1, 2), Is.False);
        Assert.That(flow.SetTimer(2, 3, 2), Is.False);   // 끝 < 시작
        Assert.That(flow.SetTimer(2, 1.5, 4.5), Is.True);
        Assert.That(flow.Snapshot.TimerStartServerTime, Is.EqualTo(1.5));
        Assert.That(flow.Snapshot.TimerEndServerTime, Is.EqualTo(4.5));

        flow.TryAdvance(Run, 2, 11, 5);
        Assert.That(flow.Snapshot.TimerEndServerTime, Is.EqualTo(0));   // 다음 단계에서 지운다
    }

    [Test]
    public void 출격_확정은_회차마다_한번()
    {
        var flow = Create(0);

        Assert.That(flow.TryCommitLaunch(), Is.True);
        Assert.That(flow.TryCommitLaunch(), Is.False);
        Assert.That(flow.Snapshot.LaunchCommitted, Is.True);
    }

    // ── 할 일 112 튜토리얼 클리어 플래그(로비 Alt+1) — 157 ② 에서 옛 LobbyTrainingFlowTests 에서 옮김 ──

    [Test]
    public void 클리어_플래그_켠_사람은_명단_고정_전_참여자와_고정_명단에서_빠진다()
    {
        var flow = Create(0, 1);

        Assert.That(flow.SetTrainingCleared(1, true, out var left), Is.True);
        Assert.That(left, Is.False, "명단 고정 전");
        Assert.That(flow.Participants, Is.EqualTo(new ulong[] { 0 }));

        flow.LockRoster();
        Assert.That(flow.Roster, Is.EqualTo(new ulong[] { 0 }));
        Assert.That(flow.IsWaitingLateJoiner(1), Is.True, "클리어 = 늦은 참가자처럼 로비 대기");
    }

    [Test]
    public void 명단_고정_뒤_켜면_즉시_명단에서_빠지고_끄면_이번_명단엔_안_돌아간다()
    {
        var flow = Create(0, 1);
        flow.LockRoster();

        Assert.That(flow.SetTrainingCleared(1, true, out var left), Is.True);
        Assert.That(left, Is.True);
        Assert.That(flow.Roster, Is.EqualTo(new ulong[] { 0 }));

        Assert.That(flow.SetTrainingCleared(1, false, out left), Is.True);
        Assert.That(left, Is.False);
        Assert.That(flow.Roster, Is.EqualTo(new ulong[] { 0 }), "고정 명단에 다시 넣지 않는다");
        Assert.That(flow.IsTrainingCleared(1), Is.False);
    }

    [Test]
    public void 클리어_플래그_같은_값_재설정과_접속자_아님은_무시_연결_종료면_지운다()
    {
        var flow = Create(0);

        Assert.That(flow.SetTrainingCleared(9, true, out _), Is.False, "접속자 아님");
        Assert.That(flow.SetTrainingCleared(0, false, out _), Is.False, "이미 꺼짐");
        Assert.That(flow.SetTrainingCleared(0, true, out _), Is.True);
        Assert.That(flow.SetTrainingCleared(0, true, out _), Is.False, "이미 켜짐");

        flow.RemoveClient(0);
        Assert.That(flow.TrainingCleared, Is.Empty);
    }

    [Test]
    public void 전원_클리어면_참여자_0_이라_보통_전이는_막히고_접속자_기준_건너뛰기만_된다()
    {
        var flow = Create(0, 1);
        flow.SetTrainingCleared(0, true, out _);
        flow.SetTrainingCleared(1, true, out _);

        Assert.That(flow.Participants, Is.Empty);
        Assert.That(flow.TryAdvance(Run, 1, 10, 0), Is.EqualTo(LobbyTutorialAdvanceResult.NoParticipants));
        Assert.That(flow.TryAdvanceTo(Run, 1, 4, 10, 0, ignoreParticipants: true), Is.EqualTo(LobbyTutorialAdvanceResult.Advanced));
        Assert.That(flow.StepIndex, Is.EqualTo(4));
    }

    [Test]
    public void 접속자가_없으면_건너뛰기도_막힌다()
    {
        var flow = Create();

        Assert.That(flow.TryAdvanceTo(Run, 1, 4, 10, 0, ignoreParticipants: true), Is.EqualTo(LobbyTutorialAdvanceResult.NoParticipants));
    }

    [Test]
    public void 미룬_건너뛰기는_접속자_기준을_그대로_가진다()
    {
        var flow = Create(0);
        flow.TryAdvance(Run, 1, 10, 0);                     // 프레임 10 에 전이
        flow.SetTrainingCleared(0, true, out _);

        Assert.That(flow.TryAdvanceTo(Run, 2, 4, 10, 0, ignoreParticipants: true), Is.EqualTo(LobbyTutorialAdvanceResult.Deferred));
        Assert.That(flow.Tick(11), Is.True);
        Assert.That(flow.StepIndex, Is.EqualTo(4));
    }
}
