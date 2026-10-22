using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 할 일 86 T5c — TRAIN_03 진행기 ↔ 훈련 상대 연결 규칙(<see cref="LobbySparringLink"/>): 보고 1회·어긋남 무시·초기화 1회·무전 이벤트 1회.
/// 끝의 전 경로 테스트는 실제 <see cref="LobbyTrainingFlow"/>·<see cref="TrainingSparringModel"/> 을 이 규칙으로 이어 A→D 를 돈다(컴포넌트가 하는 일과 같은 순서).
/// </summary>
public sealed class LobbySparringLinkTests
{
    const LobbyTrainingPhase Gathering = LobbyTrainingPhase.Gathering;
    const LobbyTrainingPhase InProgress = LobbyTrainingPhase.InProgress;
    const LobbyTrainingPhase Completed = LobbyTrainingPhase.Completed;

    static LobbySparringLink Started()
    {
        var link = new LobbySparringLink();
        Assert.That(link.BeginRound(), Is.True);
        return link;
    }

    // ─── 상대 존재 범위 ────────────────────────────────────────────────

    [Test]
    public void BotPresent_OnlyTrain03AToD()
    {
        Assert.That(LobbySparringLink.BotPresent(LobbyTrainingStage.Train02), Is.False);
        Assert.That(LobbySparringLink.BotPresent(LobbyTrainingStage.Train03A), Is.True);
        Assert.That(LobbySparringLink.BotPresent(LobbyTrainingStage.Train03D), Is.True);
        Assert.That(LobbySparringLink.BotPresent(LobbyTrainingStage.TrainEnd), Is.False);
        Assert.That(LobbySparringLink.BotPresent(LobbyTrainingStage.Lobby), Is.False);
    }

    [Test]
    public void WallMarkerVisible_OnlyTrain03CAndD()
    {
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTrainingStage.Lobby), Is.False);
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTrainingStage.Train02), Is.False);
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTrainingStage.Train03A), Is.False);
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTrainingStage.Train03B), Is.False);
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTrainingStage.Train03C), Is.True);
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTrainingStage.Train03D), Is.True);
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTrainingStage.TrainEnd), Is.False);
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTrainingStage.Brief), Is.False);
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTrainingStage.Sortie), Is.False);
    }

    [Test]
    public void BeginRound_Twice_SecondIgnored_EndThenBeginCountsNewRound()
    {
        var link = Started();
        Assert.That(link.BeginRound(), Is.False);
        Assert.That(link.Round, Is.EqualTo(1));

        Assert.That(link.EndRound(), Is.True);
        Assert.That(link.EndRound(), Is.False);
        Assert.That(link.BeginRound(), Is.True);
        Assert.That(link.Round, Is.EqualTo(2));
    }

    // ─── 진행 알림 ────────────────────────────────────────────────────

    [Test]
    public void FlowNotice_OnlyInProgress_OncePerStage_ForwardOnly()
    {
        var link = Started();
        Assert.That(link.TryTakeFlowNotice(LobbyTrainingStage.Train03A, Gathering, out _), Is.False);

        Assert.That(link.TryTakeFlowNotice(LobbyTrainingStage.Train03A, InProgress, out var notice), Is.True);
        Assert.That(notice, Is.EqualTo(LobbyTrainingStage.Train03A));
        Assert.That(link.TryTakeFlowNotice(LobbyTrainingStage.Train03A, InProgress, out _), Is.False, "같은 단계 재평가");
        Assert.That(link.TryTakeFlowNotice(LobbyTrainingStage.Train03A, Completed, out _), Is.False, "영역 확인 대기");

        Assert.That(link.TryTakeFlowNotice(LobbyTrainingStage.Train03C, InProgress, out notice), Is.True);
        Assert.That(notice, Is.EqualTo(LobbyTrainingStage.Train03C));
        Assert.That(link.TryTakeFlowNotice(LobbyTrainingStage.Train03B, InProgress, out _), Is.False, "역순");
    }

    [Test]
    public void FlowNotice_NotSparringStageOrNoRound_Ignored()
    {
        var link = new LobbySparringLink();
        Assert.That(link.TryTakeFlowNotice(LobbyTrainingStage.Train03A, InProgress, out _), Is.False, "회차 전");

        link.BeginRound();
        Assert.That(link.TryTakeFlowNotice(LobbyTrainingStage.Train02, InProgress, out _), Is.False);
        Assert.That(link.TryTakeFlowNotice(LobbyTrainingStage.Brief, InProgress, out _), Is.False);
    }

    // ─── 완료 보고 ────────────────────────────────────────────────────

    [Test]
    public void Report_MatchingInProgressStage_AcceptedOnce()
    {
        var link = Started();
        Assert.That(link.TryAcceptReport(LobbyTrainingStage.Train03A, LobbyTrainingStage.Train03A, InProgress), Is.True);
        Assert.That(link.LastReported, Is.EqualTo(LobbyTrainingStage.Train03A));
        Assert.That(link.TryAcceptReport(LobbyTrainingStage.Train03A, LobbyTrainingStage.Train03A, InProgress), Is.False, "중복");
    }

    [Test]
    public void Report_MismatchedFlow_Ignored()
    {
        var link = Started();
        Assert.That(link.TryAcceptReport(LobbyTrainingStage.Train03B, LobbyTrainingStage.Train03A, InProgress), Is.False, "진행기보다 앞선 보고");
        Assert.That(link.TryAcceptReport(LobbyTrainingStage.Train03A, LobbyTrainingStage.Train03A, Gathering), Is.False, "집결 대기 중");
        Assert.That(link.TryAcceptReport(LobbyTrainingStage.Train03A, LobbyTrainingStage.Train03A, Completed), Is.False, "이미 완료");
        Assert.That(link.LastReported, Is.EqualTo(LobbyTrainingStage.Lobby), "무시한 보고는 기록하지 않는다");

        Assert.That(link.TryAcceptReport(LobbyTrainingStage.Train03A, LobbyTrainingStage.Train03A, InProgress), Is.True);
    }

    [Test]
    public void Report_BackwardOrNonSparring_Ignored()
    {
        var link = Started();
        Assert.That(link.TryAcceptReport(LobbyTrainingStage.Train03C, LobbyTrainingStage.Train03C, InProgress), Is.True);
        Assert.That(link.TryAcceptReport(LobbyTrainingStage.Train03B, LobbyTrainingStage.Train03B, InProgress), Is.False, "역순");
        Assert.That(link.TryAcceptReport(LobbyTrainingStage.Train02, LobbyTrainingStage.Train02, InProgress), Is.False);
    }

    [Test]
    public void Report_AfterRoundEnded_Ignored()
    {
        var link = Started();
        link.EndRound();
        Assert.That(link.TryAcceptReport(LobbyTrainingStage.Train03A, LobbyTrainingStage.Train03A, InProgress), Is.False);
    }

    // ─── B→C 초기화 ───────────────────────────────────────────────────

    [Test]
    public void PracticeReset_OncePerRound_NewRoundAllowsAgain()
    {
        var link = Started();
        Assert.That(link.TryConsumePracticeReset(), Is.True);
        Assert.That(link.TryConsumePracticeReset(), Is.False);

        link.EndRound();
        Assert.That(link.TryConsumePracticeReset(), Is.False, "회차 밖");
        link.BeginRound();
        Assert.That(link.TryConsumePracticeReset(), Is.True, "새 회차(판 끝 로비 복귀 뒤)");
    }

    // ─── 무전 이벤트 ──────────────────────────────────────────────────

    [Test]
    public void FirstCounter_OnlyInCounterIntro_OncePerRound()
    {
        var link = Started();
        Assert.That(link.TryRaiseFirstCounter(TrainingSparringStage.Idle), Is.False);
        Assert.That(link.TryRaiseFirstCounter(TrainingSparringStage.WallAndSuppress), Is.False, "C 의 간파는 TRAIN_03_COUNTER 가 아니다");
        Assert.That(link.TryRaiseFirstCounter(TrainingSparringStage.CounterIntro), Is.True);
        Assert.That(link.TryRaiseFirstCounter(TrainingSparringStage.CounterIntro), Is.False);

        link.EndRound();
        link.BeginRound();
        Assert.That(link.TryRaiseFirstCounter(TrainingSparringStage.CounterIntro), Is.True);
    }

    [Test]
    public void FirstWall_OnlyWhenRecorded_OncePerRound()
    {
        var link = Started();
        Assert.That(link.TryRaiseFirstWall(false), Is.False);
        Assert.That(link.TryRaiseFirstWall(true), Is.True);
        Assert.That(link.TryRaiseFirstWall(true), Is.False, "D 에서도 기록은 남아 있다 — 다시 내지 않는다");

        link.EndRound();
        Assert.That(link.TryRaiseFirstWall(true), Is.False, "회차 밖");
    }

    // ─── 전 경로: 진행기 + 상대 모델 ─────────────────────────────────

    static LobbyTrainingFlow FlowAtTrain03AGathering()
    {
        var flow = new LobbyTrainingFlow();
        flow.AddClient(1);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Entrance), Is.True);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Train01Start), Is.True);
        Assert.That(flow.ReportStageCompleted(LobbyTrainingStage.Train01), Is.True);
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Train02Start), Is.True);
        Assert.That(flow.ReportStageCompleted(LobbyTrainingStage.Train02), Is.True);
        Assert.That(flow.Stage, Is.EqualTo(LobbyTrainingStage.Train03A));
        Assert.That(flow.Phase, Is.EqualTo(Gathering));
        return flow;
    }

    // 컴포넌트의 EvaluateServer 와 같은 일 — 진행 중이면 알림 1회.
    static void Notify(LobbySparringLink link, LobbyTrainingFlow flow, TrainingSparringModel model)
    {
        if (link.TryTakeFlowNotice(flow.Stage, flow.Phase, out var notice))
            model.OnFlowStageStarted(notice);
    }

    // 컴포넌트의 Update 보고와 같은 일 — 상대 보고를 진행기에.
    static List<LobbyTrainingStage> Pump(LobbySparringLink link, LobbyTrainingFlow flow, TrainingSparringModel model)
    {
        var reported = new List<LobbyTrainingStage>();
        while (model.TryConsumeStageReport(out var completed))
        {
            if (!link.TryAcceptReport(completed, flow.Stage, flow.Phase))
                continue;

            Assert.That(flow.ReportStageCompleted(completed), Is.True, $"{completed} 보고를 진행기가 받아야 한다");
            reported.Add(completed);
            Notify(link, flow, model);
        }

        return reported;
    }

    [Test]
    public void FullPath_AToD_EachStageReportedOnce_ResetOnce_EventsOnce()
    {
        var flow = FlowAtTrain03AGathering();
        var link = new LobbySparringLink();
        var model = new TrainingSparringModel(TrainingSparringSegmentCheck.Evaluate(BossCounterProgress.GaugeMax, 20f));
        var counterEvents = 0;
        var wallEvents = 0;
        var resets = 0;

        Assert.That(LobbySparringLink.BotPresent(flow.Stage), Is.True, "Train03A 집결 대기 중에 상대 스폰");
        link.BeginRound();
        Notify(link, flow, model);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.Idle), "집결 전엔 알리지 않는다");

        // A: 시작 집결 → 간파 성공 → 그로기 종료
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area), Is.True);
        Notify(link, flow, model);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.CounterIntro));
        model.OnCounterSucceeded();
        if (link.TryRaiseFirstCounter(model.Stage)) counterEvents++;
        if (link.TryRaiseFirstCounter(model.Stage)) counterEvents++;
        model.OnCounterGroggyEnded();
        Assert.That(Pump(link, flow, model), Is.EqualTo(new[] { LobbyTrainingStage.Train03A }));
        Assert.That(flow.Phase, Is.EqualTo(Completed), "A→B 영역 확인 대기");

        // B: 영역 확인 → CC 적용·종료
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area), Is.True);
        Notify(link, flow, model);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.CrowdControlIntro));
        model.OnCrowdControlApplied(TrainingSparringCrowdControl.PushStagger);
        model.OnCrowdControlEnded(TrainingSparringCrowdControl.PushStagger);
        Assert.That(Pump(link, flow, model), Is.EqualTo(new[] { LobbyTrainingStage.Train03B }));

        // B→C: 영역 확인 → 상대 초기화 1회 → 플레이어 리셋 1회. 같은 단계 재평가(재접속·중복 알림)는 반복하지 않는다.
        Assert.That(flow.OnGatherConfirmed(LobbyGatherPoint.Train03Area), Is.True);
        Notify(link, flow, model);
        Notify(link, flow, model);
        Assert.That(model.TryConsumeResetRequest(), Is.True);
        Assert.That(model.TryConsumeResetRequest(), Is.False);
        Assert.That(model.OnResetExecuted(), Is.True);
        if (link.TryConsumePracticeReset()) resets++;
        if (link.TryConsumePracticeReset()) resets++;
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.WallAndSuppress));

        // C: 벽 전 제압은 C 계속 → 벽(칸 감소) → 제압 = D
        Assert.That(model.OnSuppressStarted(), Is.True);
        Assert.That(model.OnSuppressEnded(), Is.True);
        Assert.That(Pump(link, flow, model), Is.Empty);
        Assert.That(flow.Stage, Is.EqualTo(LobbyTrainingStage.Train03C));
        if (link.TryRaiseFirstWall(model.HasRecordedWall)) wallEvents++;
        model.OnWallImpact(reducedGauge: true);
        if (link.TryRaiseFirstWall(model.HasRecordedWall)) wallEvents++;
        model.OnSuppressStarted();
        if (link.TryRaiseFirstWall(model.HasRecordedWall)) wallEvents++;
        Assert.That(Pump(link, flow, model), Is.EqualTo(new[] { LobbyTrainingStage.Train03C }));
        Assert.That(flow.Stage, Is.EqualTo(LobbyTrainingStage.Train03D));
        Assert.That(flow.Phase, Is.EqualTo(InProgress), "제압 시작 순간 D — 집결 재검사 없음");

        // D: 제압 피해 → 제압 종료 → 마무리 → 처치
        model.OnSuppressDamage();
        model.OnSuppressEnded();
        Assert.That(model.TryConsumeFinishHealthRequest(), Is.True);
        Assert.That(model.OnDied(), Is.True);
        Assert.That(Pump(link, flow, model), Is.EqualTo(new[] { LobbyTrainingStage.Train03D }));
        Assert.That(flow.Stage, Is.EqualTo(LobbyTrainingStage.TrainEnd));
        Assert.That(LobbySparringLink.BotPresent(flow.Stage), Is.False, "종료 집결 = 정리");
        Assert.That(link.EndRound(), Is.True);

        Assert.That(counterEvents, Is.EqualTo(1));
        Assert.That(wallEvents, Is.EqualTo(1));
        Assert.That(resets, Is.EqualTo(1));
    }
}
