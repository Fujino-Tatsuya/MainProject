using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// 할 일 86 T5c → 157 ⑦ — 간파 훈련 진행기 ↔ 훈련 상대 연결 규칙(<see cref="LobbySparringLink"/>): 보고 1회·어긋남 무시·T33 플레이어 리셋 1회·판정 창 종류.
/// 끝의 전 경로 테스트는 실제 단계표 + <see cref="LobbyTutorialFlow"/>·<see cref="TrainingSparringModel"/>·<see cref="LobbyCastWindow"/> 를 이 규칙으로 이어
/// T30→T50 을 돈다(컴포넌트가 하는 일과 같은 순서). 단계 안 사건 무전은 157 Q6 A 로 없앴다.
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
    public void WallMarkerVisible_OnlyT33AndT34()
    {
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTutorialActivity.None), Is.False);
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTutorialActivity.Combat), Is.False);
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTutorialActivity.Counter), Is.False);
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTutorialActivity.VulnerableCc), Is.False);
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTutorialActivity.ResetPosition), Is.True, "위치 복귀부터");
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTutorialActivity.Wall), Is.True);
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTutorialActivity.SuppressKill), Is.False, "T34 완료 즉시 끈다(계약 ActiveTargetWallId 와 같은 순간)");
        Assert.That(LobbySparringLink.WallMarkerVisible(LobbyTutorialActivity.FreePractice), Is.False);
    }

    [Test]
    public void CastWindowMode_T32Cc_T34Wall_OthersNone()
    {
        Assert.That(LobbySparringLink.CastWindowModeFor(LobbyTutorialActivity.Counter), Is.EqualTo(LobbyCastWindowMode.None));
        Assert.That(LobbySparringLink.CastWindowModeFor(LobbyTutorialActivity.VulnerableCc), Is.EqualTo(LobbyCastWindowMode.CrowdControl));
        Assert.That(LobbySparringLink.CastWindowModeFor(LobbyTutorialActivity.ResetPosition), Is.EqualTo(LobbyCastWindowMode.None));
        Assert.That(LobbySparringLink.CastWindowModeFor(LobbyTutorialActivity.Wall), Is.EqualTo(LobbyCastWindowMode.Wall));
        Assert.That(LobbySparringLink.CastWindowModeFor(LobbyTutorialActivity.SuppressKill), Is.EqualTo(LobbyCastWindowMode.None));
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
        public int Frame => _frame;
        public LobbyTutorialStep Step => _table[Flow.StepIndex];
        public LobbyTutorialActivity Activity => Step.activity;
        public string StateId => Step.stateId;

        // 사건 프레임을 따라 진행기 시계를 당긴다(뒤로는 안 간다).
        public void SkipTo(int frame) => _frame = System.Math.Max(_frame, frame);

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
        public readonly LobbyCastWindow Window = new LobbyCastWindow();
        public readonly Queue<LobbyTutorialActivity> Pending = new Queue<LobbyTutorialActivity>();
        public FakeDirector Director;
        public int NoticeRevision;
        public int Resets;
        public int CastSeq;          // 호스트(clientId 0)의 지금 시전 seq — 진입 기준선으로 잡힌다
        public bool CcActive;        // 상대의 CC 반응(밀림·기절) 진행 중

        // 컴포넌트의 EvaluateServer 와 같은 일 — 회차 시작·알림 1회(revision 기억)·판정 창(기준선). T33 은 CC 반응이 끝난 뒤 위치 복귀 → 플레이어 리셋 1회.
        public void Evaluate()
        {
            if (!LobbySparringLink.BotPresent(Director.Activity))
                return;
            if (!Link.IsRoundActive)
            {
                Link.BeginRound();
                Window.Reset();
            }
            if (!Link.TryTakeFlowNotice(Director.Activity, out var notice))
                return;

            NoticeRevision = Director.Flow.Revision;
            var mode = LobbySparringLink.CastWindowModeFor(notice);
            if (mode == LobbyCastWindowMode.None)
                Window.Close();
            else
            {
                Window.Begin(mode, Director.Frame);
                Window.SetBaseline(0, 1, CastSeq);
            }

            Model.OnFlowStageStarted(notice, Director.Frame);
            TryReset();
        }

        // 상대 틱의 TryRunReset 과 같은 일.
        public void TryReset()
        {
            if (Model.TryConsumeResetRequest(CcActive) && Model.OnResetExecuted() && Link.TryConsumePracticeReset())
                Resets++;
        }

        // 컴포넌트 Update 와 같은 일 — 판정 창 결과를 상대에 넣고, 상대 보고를 진행기에.
        public List<LobbyTutorialActivity> Pump(int frame)
        {
            Director.SkipTo(frame - 1);   // 이 프레임에 보고하면 진행기 전이도 이 프레임
            var r = Window.Resolve(frame);
            if (r.CrowdControlAccepted) Model.OnCrowdControlAccepted(r.EventFrame);
            if (r.WallAccepted) Model.OnWallAccepted(r.EventFrame);
            Retries += r.RetryRequested && !r.WallAccepted && Model.NeedsRetryAfterMiss ? 1 : 0;
            return Pump();
        }

        public int Retries;

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
    public void FullPath_T30ToT50_EachStageReportedOnce_ResetOnce()
    {
        var table = AssetDatabase.LoadAssetAtPath<LobbyTutorialStepTable>("Assets/9.ScriptableObject/Lobby/LobbyTutorialSteps.asset");
        Assert.That(table, Is.Not.Null);

        var h = new Harness { Director = new FakeDirector(table, "T30_CounterGather") };
        var model = h.Model;
        var w = h.Window;
        const ulong Host = 0;
        const int HostPlayer = 1;

        h.Evaluate();
        Assert.That(h.Link.IsRoundActive, Is.False, "T30 집결 대기 중엔 상대 없음");

        // T31: 집결 확정 = 스폰 + 같은 평가에서 시작 알림 → 실제 간파 성공(시전 1) = 그 순간 완료
        h.Director.Advance();
        h.Evaluate();
        Assert.That(h.Director.StateId, Is.EqualTo("T31_Counter"));
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.Counter));
        Assert.That(model.ExtendsCounterWindow, Is.True);
        h.CastSeq = 1;
        model.OnCounterSucceeded();
        w.RecordCounterSuccess(h.Director.Frame, Host);
        w.RecordLanded(h.Director.Frame, Host, HostPlayer, 1, true);
        var t31Frame = h.Director.Frame;
        Assert.That(h.Pump(t31Frame + 1), Is.EqualTo(new[] { LobbyTutorialActivity.Counter }), "그로기 종료를 기다리지 않는다");
        Assert.That(h.Director.StateId, Is.EqualTo("T32_VulnerableCC"));
        Assert.That(model.HoldsVulnerable, Is.True);

        // T32: 간파 성공 시전의 딸린 넉백(같은 시전 1) — 불인정
        var f = h.Director.Frame;
        w.RecordLanded(f + 1, Host, HostPlayer, 1, true);
        w.RecordCrowdControl(f + 1, 1, TrainingSparringCrowdControl.Knockback, true);
        Assert.That(h.Pump(f + 2), Is.Empty, "자동 간파 넉백 = 별도 CC 아님");
        Assert.That(h.Director.StateId, Is.EqualTo("T32_VulnerableCC"));

        // T32: 새 시전(2) 넉백 + 경직 = 적용 순간 완료 → T33 은 반응이 끝날 때까지 위치 복귀를 기다린다
        h.CastSeq = 2;
        w.RecordLanded(f + 3, Host, HostPlayer, 2, true);
        w.RecordCrowdControl(f + 3, 2, TrainingSparringCrowdControl.Knockback | TrainingSparringCrowdControl.Stun, true);
        h.CcActive = true;
        Assert.That(h.Pump(f + 4), Is.EqualTo(new[] { LobbyTutorialActivity.VulnerableCc }));
        Assert.That(h.Director.StateId, Is.EqualTo("T33_ResetPosition"));
        Assert.That(h.Resets, Is.Zero, "CC 반응 중엔 위치 복귀하지 않는다");
        Assert.That(model.HoldsVulnerable, Is.True);

        h.CcActive = false;
        h.TryReset();
        Assert.That(h.Resets, Is.EqualTo(1));
        f = h.Director.Frame;
        Assert.That(h.Pump(f + 1), Is.EqualTo(new[] { LobbyTutorialActivity.ResetPosition }));
        h.Evaluate();   // 같은 단계 재평가(재접속·중복 알림)는 반복하지 않는다
        h.TryReset();
        Assert.That(h.Resets, Is.EqualTo(1));
        Assert.That(h.Director.StateId, Is.EqualTo("T34_Wall"));
        Assert.That(h.Director.Step.targetWallId, Is.EqualTo("WALL_TRAIN_WEST"));
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.Wall));
        Assert.That(model.HoldsVulnerable, Is.True, "벽 연습 전 취약 유지");

        // T34: T32 넉백 에피소드가 늦게 벽에 닿아도 불인정
        f = h.Director.Frame;
        w.RecordWallImpact(f + 1, 2, true);
        Assert.That(h.Pump(f + 2), Is.Empty);

        // T34: 새 시전(3) 넉백이 다른 벽에 닿고 끝남 → 위치 복구 후 재시도
        h.CastSeq = 3;
        w.RecordLanded(f + 3, Host, HostPlayer, 3, true);
        w.RecordCrowdControl(f + 3, 3, TrainingSparringCrowdControl.Knockback, true);
        w.RecordWallImpact(f + 5, 3, false);
        w.RecordKnockbackEnded(f + 6, 3);
        Assert.That(h.Pump(f + 7), Is.Empty);
        Assert.That(h.Retries, Is.EqualTo(1));
        Assert.That(h.Director.StateId, Is.EqualTo("T34_Wall"));

        // T34: 새 시전(4) 넉백이 지정 벽에 닿음 = 충돌 자체로 완료 → 벽 키가 비는 T40
        h.CastSeq = 4;
        w.RecordLanded(f + 10, Host, HostPlayer, 4, true);
        w.RecordCrowdControl(f + 10, 4, TrainingSparringCrowdControl.Knockback, true);
        w.RecordWallImpact(f + 12, 4, true);
        Assert.That(h.Pump(f + 13), Is.EqualTo(new[] { LobbyTutorialActivity.Wall }));
        Assert.That(h.Director.StateId, Is.EqualTo("T40_SuppressKill"));
        Assert.That(h.Director.Step.targetWallId, Is.Empty, "T34 완료 = 활성 벽 ID 비움(TargetWallCleared)");
        Assert.That(model.HoldsVulnerable, Is.False, "T40 = 취약 무기한 보정 해제");

        // T40(임시 — 커밋 ⑧ 전 옛 D): 피해 없는 제압 = 마무리 아님 → 다음 제압 피해 → 제압 종료 → 마무리 → 처치
        Assert.That(model.OnSuppressStarted(), Is.True);
        Assert.That(model.OnSuppressEnded(), Is.True);
        Assert.That(model.IsFinishing, Is.False);
        Assert.That(model.OnSuppressStarted(), Is.True);
        model.OnSuppressDamage();
        model.OnSuppressEnded();
        Assert.That(model.TryConsumeFinishHealthRequest(), Is.True);
        Assert.That(model.OnDied(), Is.True);
        Assert.That(h.Pump(h.Director.Frame + 1), Is.EqualTo(new[] { LobbyTutorialActivity.SuppressKill }));
        Assert.That(h.Director.StateId, Is.EqualTo("T50_FreePractice"));
        Assert.That(LobbySparringLink.BotPresent(h.Director.Activity), Is.False, "자유 연습(출구 집결) = 정리");
        Assert.That(h.Link.EndRound(), Is.True);
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
