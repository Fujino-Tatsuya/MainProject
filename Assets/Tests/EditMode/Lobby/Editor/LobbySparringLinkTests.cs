using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// 할 일 86 T5c — TRAIN_03 진행기 ↔ 훈련 상대 연결 규칙(<see cref="LobbySparringLink"/>): 보고 1회·어긋남 무시·초기화 1회·무전 이벤트(간파·벽·마무리) 1회.
/// 할 일 157 ②: 진행기 단계 = 단계표 Activity(T31 Counter ~ T40 SuppressKill). 끝의 전 경로 테스트는 실제 단계표 + <see cref="LobbyTutorialFlow"/>·
/// <see cref="TrainingSparringModel"/> 을 이 규칙으로 이어 T30→T50 을 돈다(컴포넌트가 하는 일과 같은 순서 — T33 은 초기화 실행 = 완료, 임시).
/// </summary>
public sealed class LobbySparringLinkTests
{
    static LobbySparringLink Started()
    {
        var link = new LobbySparringLink();
        Assert.That(link.BeginRound(), Is.True);
        return link;
    }

    // ─── 상대 존재 범위 ────────────────────────────────────────────────

    [Test]
    public void BotPresent_OnlyT31ToT40()
    {
        Assert.That(LobbySparringLink.BotPresent(LobbyTutorialActivity.None), Is.False, "T30 집결 대기·로비 — 스폰하지 않는다");
        Assert.That(LobbySparringLink.BotPresent(LobbyTutorialActivity.Combat), Is.False);
        Assert.That(LobbySparringLink.BotPresent(LobbyTutorialActivity.Counter), Is.True, "T30 확정 = 스폰");
        Assert.That(LobbySparringLink.BotPresent(LobbyTutorialActivity.VulnerableCc), Is.True);
        Assert.That(LobbySparringLink.BotPresent(LobbyTutorialActivity.ResetPosition), Is.True);
        Assert.That(LobbySparringLink.BotPresent(LobbyTutorialActivity.Wall), Is.True);
        Assert.That(LobbySparringLink.BotPresent(LobbyTutorialActivity.SuppressKill), Is.True);
        Assert.That(LobbySparringLink.BotPresent(LobbyTutorialActivity.FreePractice), Is.False);
    }

    [Test]
    public void WallMarkerVisible_OnlyT33ToT40()
    {
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTutorialActivity.None), Is.False);
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTutorialActivity.Combat), Is.False);
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTutorialActivity.Counter), Is.False);
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTutorialActivity.VulnerableCc), Is.False);
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTutorialActivity.ResetPosition), Is.True, "옛 Train03C 진입(초기화)부터");
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTutorialActivity.Wall), Is.True);
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTutorialActivity.SuppressKill), Is.True);
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTutorialActivity.FreePractice), Is.False);
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

    // ─── 시작 알림 ────────────────────────────────────────────────────

    [Test]
    public void FlowNotice_OncePerStage_ForwardOnly()
    {
        var link = Started();
        Assert.That(link.TryTakeFlowNotice(LobbyTutorialActivity.None, out _), Is.False, "집결 대기");

        Assert.That(link.TryTakeFlowNotice(LobbyTutorialActivity.Counter, out var notice), Is.True);
        Assert.That(notice, Is.EqualTo(LobbyTutorialActivity.Counter));
        Assert.That(link.TryTakeFlowNotice(LobbyTutorialActivity.Counter, out _), Is.False, "같은 단계 재평가");

        Assert.That(link.TryTakeFlowNotice(LobbyTutorialActivity.ResetPosition, out notice), Is.True);
        Assert.That(notice, Is.EqualTo(LobbyTutorialActivity.ResetPosition));
        Assert.That(link.TryTakeFlowNotice(LobbyTutorialActivity.VulnerableCc, out _), Is.False, "역순");
    }

    [Test]
    public void FlowNotice_NotSparringStageOrNoRound_Ignored()
    {
        var link = new LobbySparringLink();
        Assert.That(link.TryTakeFlowNotice(LobbyTutorialActivity.Counter, out _), Is.False, "회차 전");

        link.BeginRound();
        Assert.That(link.TryTakeFlowNotice(LobbyTutorialActivity.Combat, out _), Is.False);
        Assert.That(link.TryTakeFlowNotice(LobbyTutorialActivity.FreePractice, out _), Is.False);
    }

    // ─── 완료 보고 ────────────────────────────────────────────────────

    [Test]
    public void Report_MatchingStage_AcceptedOnce()
    {
        var link = Started();
        Assert.That(link.TryAcceptReport(LobbyTutorialActivity.Counter, LobbyTutorialActivity.Counter), Is.True);
        Assert.That(link.LastReported, Is.EqualTo(LobbyTutorialActivity.Counter));
        Assert.That(link.TryAcceptReport(LobbyTutorialActivity.Counter, LobbyTutorialActivity.Counter), Is.False, "중복");
    }

    [Test]
    public void Report_MismatchedFlow_Ignored()
    {
        var link = Started();
        Assert.That(link.TryAcceptReport(LobbyTutorialActivity.VulnerableCc, LobbyTutorialActivity.Counter), Is.False, "진행기보다 앞선 보고");
        Assert.That(link.TryAcceptReport(LobbyTutorialActivity.Counter, LobbyTutorialActivity.None), Is.False, "집결 대기 중");
        Assert.That(link.LastReported, Is.EqualTo(LobbyTutorialActivity.None), "무시한 보고는 기록하지 않는다");

        Assert.That(link.TryAcceptReport(LobbyTutorialActivity.Counter, LobbyTutorialActivity.Counter), Is.True);
    }

    [Test]
    public void Report_BackwardOrNonSparring_Ignored()
    {
        var link = Started();
        Assert.That(link.TryAcceptReport(LobbyTutorialActivity.Wall, LobbyTutorialActivity.Wall), Is.True);
        Assert.That(link.TryAcceptReport(LobbyTutorialActivity.VulnerableCc, LobbyTutorialActivity.VulnerableCc), Is.False, "역순");
        Assert.That(link.TryAcceptReport(LobbyTutorialActivity.Combat, LobbyTutorialActivity.Combat), Is.False);
    }

    [Test]
    public void Report_AfterRoundEnded_Ignored()
    {
        var link = Started();
        link.EndRound();
        Assert.That(link.TryAcceptReport(LobbyTutorialActivity.Counter, LobbyTutorialActivity.Counter), Is.False);
    }

    // ─── T33 초기화 ───────────────────────────────────────────────────

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

    [Test]
    public void FirstFinish_OnlyWhenFinishing_OncePerRound_NewRoundAllowsAgain()
    {
        var link = Started();
        Assert.That(link.TryRaiseFirstFinish(false), Is.False, "피해 없이 끝난 제압 — 마무리 아님");
        Assert.That(link.TryRaiseFirstFinish(true), Is.True);
        Assert.That(link.TryRaiseFirstFinish(true), Is.False, "마무리 중 매 프레임 — 다시 내지 않는다");

        link.EndRound();
        Assert.That(link.TryRaiseFirstFinish(true), Is.False, "회차 밖");
        link.BeginRound();
        Assert.That(link.TryRaiseFirstFinish(true), Is.True, "새 회차(판 끝 로비 복귀 뒤)");
        Assert.That(link.TryRaiseFirstFinish(true), Is.False);
    }

    // ─── 전 경로: 진행기(실제 단계표 + 순수 모델) + 상대 모델 ───────────

    // 진행기를 흉내 낸다 — 단계표 행·revision·한 프레임 한 전이는 LobbyTutorialFlow 그대로.
    sealed class FakeDirector
    {
        const int Run = 3;
        readonly LobbyTutorialStepTable _table;
        int _frame = 100;

        public FakeDirector(LobbyTutorialStepTable table, string startStateId)
        {
            _table = table;
            Flow = new LobbyTutorialFlow(table.Kinds(), Run);
            Flow.AddClient(1);
            Flow.LockRoster();
            Assert.That(Flow.TryAdvanceTo(Run, 1, table.IndexOf(startStateId), _frame, 0), Is.EqualTo(LobbyTutorialAdvanceResult.Advanced));
        }

        public LobbyTutorialFlow Flow { get; }
        public LobbyTutorialStep Step => _table[Flow.StepIndex];
        public LobbyTutorialActivity Activity => Step.activity;
        public string StateId => Step.stateId;

        // 집결 확정·완료 보고 = 다음 행(프레임을 넘겨 한 프레임 한 전이를 지킨다).
        public void Advance() =>
            Assert.That(Flow.TryAdvance(Run, Flow.Revision, ++_frame, 0), Is.EqualTo(LobbyTutorialAdvanceResult.Advanced), StateId);

        public bool ReportActivity(LobbyTutorialActivity completed, int revision)
        {
            if (Step.kind != LobbyTutorialStepKind.Activity || Step.activity != completed || revision != Flow.Revision)
                return false;
            Advance();
            return true;
        }
    }

    sealed class Harness
    {
        public readonly LobbySparringLink Link = new LobbySparringLink();
        public readonly TrainingSparringModel Model = new TrainingSparringModel(TrainingSparringSegmentCheck.Evaluate(BossCounterProgress.GaugeMax, 20f));
        public readonly Queue<LobbyTutorialActivity> Pending = new Queue<LobbyTutorialActivity>();
        public FakeDirector Director;
        public int NoticeRevision;
        public int Resets;

        // 컴포넌트의 EvaluateServer 와 같은 일 — 회차 시작·알림 1회(revision 기억). T33 알림이면 초기화 실행 → 플레이어 리셋 1회 → T33 완료 대기.
        public void Evaluate()
        {
            if (!LobbySparringLink.BotPresent(Director.Activity))
                return;
            if (!Link.IsRoundActive)
                Link.BeginRound();
            if (!Link.TryTakeFlowNotice(Director.Activity, out var notice))
                return;

            NoticeRevision = Director.Flow.Revision;
            Model.OnFlowStageStarted(notice);
            if (Model.TryConsumeResetRequest() && Model.OnResetExecuted() && Link.TryConsumePracticeReset())
            {
                Resets++;
                Pending.Enqueue(LobbyTutorialActivity.ResetPosition);
            }
        }

        // 컴포넌트의 Update 보고와 같은 일 — 상대 보고(+ T33 임시 보고)를 진행기에.
        public List<LobbyTutorialActivity> Pump()
        {
            var reported = new List<LobbyTutorialActivity>();
            while (Model.TryConsumeStageReport(out var completed))
                Pending.Enqueue(completed);

            while (Pending.Count > 0)
            {
                var completed = Pending.Dequeue();
                if (!Link.TryAcceptReport(completed, Director.Activity))
                    continue;

                Assert.That(Director.ReportActivity(completed, NoticeRevision), Is.True, $"{completed} 보고를 진행기가 받아야 한다");
                reported.Add(completed);
                Evaluate();
            }

            return reported;
        }
    }

    [Test]
    public void FullPath_T30ToT50_EachStageReportedOnce_ResetOnce_EventsOnce()
    {
        var table = AssetDatabase.LoadAssetAtPath<LobbyTutorialStepTable>("Assets/9.ScriptableObject/Lobby/LobbyTutorialSteps.asset");
        Assert.That(table, Is.Not.Null);

        var h = new Harness { Director = new FakeDirector(table, "T30_CounterGather") };
        var model = h.Model;
        var link = h.Link;
        var counterEvents = 0;
        var wallEvents = 0;
        var finishEvents = 0;

        h.Evaluate();
        Assert.That(link.IsRoundActive, Is.False, "T30 집결 대기 중엔 상대 없음");

        // T31: 집결 확정 = 스폰 + 같은 평가에서 시작 알림 → 간파 성공 → 그로기 종료
        h.Director.Advance();
        h.Evaluate();
        Assert.That(h.Director.StateId, Is.EqualTo("T31_Counter"));
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.CounterIntro));
        model.OnCounterSucceeded();
        if (link.TryRaiseFirstCounter(model.Stage)) counterEvents++;
        if (link.TryRaiseFirstCounter(model.Stage)) counterEvents++;
        model.OnCounterGroggyEnded();
        Assert.That(h.Pump(), Is.EqualTo(new[] { LobbyTutorialActivity.Counter }));

        // T32: CC 적용·종료
        Assert.That(h.Director.StateId, Is.EqualTo("T32_VulnerableCC"));
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.CrowdControlIntro));
        model.OnCrowdControlApplied(TrainingSparringCrowdControl.PushStagger);
        model.OnCrowdControlEnded(TrainingSparringCrowdControl.PushStagger);

        // T32 완료 → T33 알림 = 상대 초기화 1회 → 플레이어 리셋 1회 → T33 완료(임시) → T34
        Assert.That(h.Pump(), Is.EqualTo(new[] { LobbyTutorialActivity.VulnerableCc, LobbyTutorialActivity.ResetPosition }));
        h.Evaluate();   // 같은 단계 재평가(재접속·중복 알림)는 반복하지 않는다
        Assert.That(h.Resets, Is.EqualTo(1));
        Assert.That(h.Director.StateId, Is.EqualTo("T34_Wall"));
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.WallAndSuppress));

        // T34: 벽 전 제압은 계속 → 벽(칸 감소) → 제압 = T40
        Assert.That(model.OnSuppressStarted(), Is.True);
        Assert.That(model.OnSuppressEnded(), Is.True);
        Assert.That(h.Pump(), Is.Empty);
        Assert.That(h.Director.StateId, Is.EqualTo("T34_Wall"));
        if (link.TryRaiseFirstWall(model.HasRecordedWall)) wallEvents++;
        model.OnWallImpact(reducedGauge: true);
        if (link.TryRaiseFirstWall(model.HasRecordedWall)) wallEvents++;
        model.OnSuppressStarted();
        if (link.TryRaiseFirstWall(model.HasRecordedWall)) wallEvents++;
        Assert.That(h.Pump(), Is.EqualTo(new[] { LobbyTutorialActivity.Wall }));
        Assert.That(h.Director.StateId, Is.EqualTo("T40_SuppressKill"), "제압 시작 순간 T40 — 집결 재검사 없음");

        // T40: 진입 제압이 피해 없이 끝남 = 마무리 아님 → 다음 제압 피해 → 제압 종료 → 마무리 → 처치
        Assert.That(model.OnSuppressEnded(), Is.True);
        Assert.That(model.IsFinishing, Is.False);
        if (link.TryRaiseFirstFinish(model.IsFinishing)) finishEvents++;
        Assert.That(model.OnSuppressStarted(), Is.True);
        model.OnSuppressDamage();
        model.OnSuppressEnded();
        Assert.That(model.TryConsumeFinishHealthRequest(), Is.True);
        if (link.TryRaiseFirstFinish(model.IsFinishing)) finishEvents++;
        if (link.TryRaiseFirstFinish(model.IsFinishing)) finishEvents++;
        Assert.That(model.OnDied(), Is.True);
        Assert.That(h.Pump(), Is.EqualTo(new[] { LobbyTutorialActivity.SuppressKill }));
        Assert.That(h.Director.StateId, Is.EqualTo("T50_FreePractice"));
        Assert.That(LobbySparringLink.BotPresent(h.Director.Activity), Is.False, "자유 연습(출구 집결) = 정리");
        Assert.That(link.EndRound(), Is.True);

        Assert.That(counterEvents, Is.EqualTo(1));
        Assert.That(wallEvents, Is.EqualTo(1));
        Assert.That(finishEvents, Is.EqualTo(1));
        Assert.That(h.Resets, Is.EqualTo(1));
    }

    [Test]
    public void StaleRevision_ReportIgnoredByDirector()
    {
        var table = AssetDatabase.LoadAssetAtPath<LobbyTutorialStepTable>("Assets/9.ScriptableObject/Lobby/LobbyTutorialSteps.asset");
        var director = new FakeDirector(table, "T31_Counter");
        var oldRevision = director.Flow.Revision;
        director.Advance();   // T32

        Assert.That(director.ReportActivity(LobbyTutorialActivity.VulnerableCc, oldRevision), Is.False, "지난 revision");
        Assert.That(director.ReportActivity(LobbyTutorialActivity.Counter, director.Flow.Revision), Is.False, "지난 단계");
        Assert.That(director.ReportActivity(LobbyTutorialActivity.VulnerableCc, director.Flow.Revision), Is.True);
    }
}
