using System.Collections.Generic;
using NUnit.Framework;

/// <summary>간파 훈련 상대 상태기계 — 할 일 157 ⑦(명세 §5 T31~T34·§6, PLAN §5 ⑦). T40 은 커밋 ⑧ 전 임시(옛 D 규칙).</summary>
public sealed class TrainingSparringModelTests
{
    // 게이지 최대 / 감소량 = 5칸(23호 기본값 100 / 20 과 같은 비) — 테스트 입력일 뿐 모델엔 수치가 없다.
    static TrainingSparringModel Create(bool suppressAcceleration = false) =>
        new TrainingSparringModel(TrainingSparringSegmentCheck.Evaluate(BossCounterProgress.GaugeMax, 20f), suppressAcceleration);

    static List<LobbyTutorialActivity> DrainReports(TrainingSparringModel model)
    {
        var reports = new List<LobbyTutorialActivity>();
        while (model.TryConsumeStageReport(out var stage))
            reports.Add(stage);
        return reports;
    }

    static void ExpectReports(TrainingSparringModel model, params LobbyTutorialActivity[] expected) =>
        Assert.That(DrainReports(model), Is.EqualTo(expected));

    // 단계마다 프레임을 넘긴다(진행기 = 한 프레임 한 전이). 알림 프레임 = 10, 20, 30, 40, 50.
    static TrainingSparringModel AtCounter(bool suppressAcceleration = false)
    {
        var model = Create(suppressAcceleration);
        Assert.That(model.OnFlowStageStarted(LobbyTutorialActivity.Counter, 10), Is.True);
        return model;
    }

    static TrainingSparringModel AtVulnerableCc()
    {
        var model = AtCounter();
        Assert.That(model.OnCounterSucceeded(), Is.True);
        ExpectReports(model, LobbyTutorialActivity.Counter);
        Assert.That(model.OnFlowStageStarted(LobbyTutorialActivity.VulnerableCc, 20), Is.True);
        return model;
    }

    static TrainingSparringModel AtResetting()
    {
        var model = AtVulnerableCc();
        Assert.That(model.OnCrowdControlAccepted(21), Is.True);
        ExpectReports(model, LobbyTutorialActivity.VulnerableCc);
        Assert.That(model.OnFlowStageStarted(LobbyTutorialActivity.ResetPosition, 30), Is.True);
        return model;
    }

    static TrainingSparringModel AtWall()
    {
        var model = AtResetting();
        Assert.That(model.TryConsumeResetRequest(crowdControlActive: false), Is.True);
        Assert.That(model.OnResetExecuted(), Is.True);
        ExpectReports(model, LobbyTutorialActivity.ResetPosition);
        Assert.That(model.OnFlowStageStarted(LobbyTutorialActivity.Wall, 40), Is.True);
        return model;
    }

    static TrainingSparringModel AtSuppressKill()
    {
        var model = AtWall();
        Assert.That(model.OnWallAccepted(41), Is.True);
        ExpectReports(model, LobbyTutorialActivity.Wall);
        Assert.That(model.OnFlowStageStarted(LobbyTutorialActivity.SuppressKill, 50), Is.True);
        return model;
    }

    // ── 전이표 ──

    [Test]
    public void TransitionTable_FullPath_EachStageReportedOnce()
    {
        var model = Create();
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.Idle));

        Assert.That(model.OnFlowStageStarted(LobbyTutorialActivity.Counter, 10), Is.True);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.Counter));
        Assert.That(model.OnCounterSucceeded(), Is.True);
        ExpectReports(model, LobbyTutorialActivity.Counter);

        Assert.That(model.OnFlowStageStarted(LobbyTutorialActivity.VulnerableCc, 20), Is.True);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.VulnerableCc));
        Assert.That(model.OnCrowdControlAccepted(25), Is.True);
        ExpectReports(model, LobbyTutorialActivity.VulnerableCc);

        Assert.That(model.OnFlowStageStarted(LobbyTutorialActivity.ResetPosition, 30), Is.True);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.Resetting));
        Assert.That(model.TryConsumeResetRequest(false), Is.True);
        Assert.That(model.OnResetExecuted(), Is.True);
        ExpectReports(model, LobbyTutorialActivity.ResetPosition);

        Assert.That(model.OnFlowStageStarted(LobbyTutorialActivity.Wall, 40), Is.True);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.Wall));
        Assert.That(model.OnWallAccepted(45), Is.True);
        ExpectReports(model, LobbyTutorialActivity.Wall);

        Assert.That(model.OnFlowStageStarted(LobbyTutorialActivity.SuppressKill, 50), Is.True);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.SuppressKill));
        Assert.That(model.OnSuppressStarted(), Is.True);
        Assert.That(model.OnSuppressDamage(), Is.True);
        Assert.That(model.OnSuppressEnded(), Is.True);
        Assert.That(model.OnDied(), Is.True);
        ExpectReports(model, LobbyTutorialActivity.SuppressKill);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.Completed));
    }

    [Test]
    public void FlowNotice_RequiresPreviousCompletion_ForwardOnly()
    {
        var model = AtCounter();
        Assert.That(model.OnFlowStageStarted(LobbyTutorialActivity.Counter, 11), Is.False, "같은 단계 재알림");
        Assert.That(model.OnFlowStageStarted(LobbyTutorialActivity.VulnerableCc, 12), Is.False, "간파 성공 전엔 T32 로 가지 않는다");
        Assert.That(model.OnFlowStageStarted(LobbyTutorialActivity.Wall, 12), Is.False, "건너뛰기 불가");

        model.OnCounterSucceeded();
        Assert.That(model.OnFlowStageStarted(LobbyTutorialActivity.ResetPosition, 13), Is.False, "CC 전엔 T33 불가");
        Assert.That(model.OnFlowStageStarted(LobbyTutorialActivity.VulnerableCc, 13), Is.True);
        Assert.That(model.OnFlowStageStarted(LobbyTutorialActivity.Counter, 14), Is.False, "역순");
        Assert.That(model.OnFlowStageStarted(LobbyTutorialActivity.None, 14), Is.False);
    }

    // ── T31 ──

    [Test]
    public void Counter_CompletesAtSuccessMoment_StartsVulnerableHold_StopsSelfAction()
    {
        var model = AtCounter();
        Assert.That(model.AllowsSelfAction, Is.True, "성공 전엔 같은 공격 반복");
        Assert.That(model.ExtendsCounterWindow, Is.True, "훈련 창 연장");
        Assert.That(model.HoldsVulnerable, Is.False);
        Assert.That(model.AllowsGaugeTransition, Is.False);
        Assert.That(model.PreventsDeath, Is.True, "처치 불가");

        Assert.That(model.OnCounterSucceeded(), Is.True);
        ExpectReports(model, LobbyTutorialActivity.Counter);   // 그로기 종료를 기다리지 않는다
        Assert.That(model.HoldsVulnerable, Is.True, "성공 즉시 취약 무기한 유지 시작");
        Assert.That(model.AllowsSelfAction, Is.False);
        Assert.That(model.ExtendsCounterWindow, Is.False);
        Assert.That(model.OnCounterSucceeded(), Is.False, "중복 성공 = 보고 1회");
        ExpectReports(model);
    }

    [Test]
    public void Counter_FailureKeepsStage_SameAttackRepeats()
    {
        var model = AtCounter();
        // 실패(창 만료·정면 아님)는 모델 사건이 아니다 — 성공 전까지 그대로.
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.Counter));
        Assert.That(model.AllowsSelfAction, Is.True);
        Assert.That(model.OnCrowdControlAccepted(15), Is.False, "T31 의 CC 는 무관");
        ExpectReports(model);
    }

    // ── T32 ──

    [Test]
    public void VulnerableCc_HoldsVulnerable_NoSelfAction_NoGauge()
    {
        var model = AtVulnerableCc();
        Assert.That(model.HoldsVulnerable, Is.True);
        Assert.That(model.AllowsSelfAction, Is.False, "상대 공격 안 함");
        Assert.That(model.AllowsGaugeTransition, Is.False);
        Assert.That(model.AcceptsCrowdControl, Is.True);
    }

    [Test]
    public void VulnerableCc_SameFrameAsEntry_NotCounted()
    {
        var model = AtVulnerableCc();   // 진입 프레임 20
        Assert.That(model.OnCrowdControlAccepted(20), Is.False, "같은 프레임 연쇄 금지 — T31 성공 프레임의 사건이 T32 를 끝내지 않는다");
        Assert.That(model.OnCrowdControlAccepted(19), Is.False);
        ExpectReports(model);
        Assert.That(model.OnCrowdControlAccepted(21), Is.True);
        ExpectReports(model, LobbyTutorialActivity.VulnerableCc);
        Assert.That(model.OnCrowdControlAccepted(22), Is.False, "보고 1회");
    }

    [Test]
    public void CounterSuccessFrame_DoesNotCompleteVulnerableCcInSameFrame()
    {
        var model = AtCounter();
        model.OnCounterSucceeded();             // 프레임 30 에 성공
        ExpectReports(model, LobbyTutorialActivity.Counter);
        model.OnFlowStageStarted(LobbyTutorialActivity.VulnerableCc, 30);   // 진행기가 같은 프레임에 T32 를 열어도
        Assert.That(model.OnCrowdControlAccepted(30), Is.False, "성공과 같은 프레임 CC = 불인정");
        ExpectReports(model);
    }

    // ── T33 ──

    [Test]
    public void Resetting_WaitsForCrowdControlEnd_ThenCompletesOnce()
    {
        var model = AtResetting();
        Assert.That(model.AcceptsCrowdControl, Is.False, "위치 복귀 중 새 CC 반응 없음");
        Assert.That(model.HoldsVulnerable, Is.True, "취약 유지");
        Assert.That(model.TryConsumeResetRequest(crowdControlActive: true), Is.False, "앞 CC 반응이 끝날 때까지 기다린다");
        Assert.That(model.OnResetExecuted(), Is.False, "요청을 꺼내기 전 실행 보고는 무시");
        Assert.That(model.TryConsumeResetRequest(false), Is.True);
        Assert.That(model.TryConsumeResetRequest(false), Is.False, "1회");
        Assert.That(model.OnResetExecuted(), Is.True);
        Assert.That(model.OnResetExecuted(), Is.False);
        ExpectReports(model, LobbyTutorialActivity.ResetPosition);
    }

    // ── T34 ──

    [Test]
    public void Wall_GaugeAllowed_VulnerableHeld_CompletesOnAcceptedImpact()
    {
        var model = AtWall();
        Assert.That(model.AllowsGaugeTransition, Is.True, "칸 감소는 실제 규칙대로");
        Assert.That(model.ShowsSegmentUi, Is.True);
        Assert.That(model.HoldsVulnerable, Is.True);
        Assert.That(model.AllowsSelfAction, Is.False);
        Assert.That(model.NeedsRetryAfterMiss, Is.True);

        Assert.That(model.OnWallAccepted(40), Is.False, "진입 프레임 사건 불인정");
        Assert.That(model.OnWallAccepted(42), Is.True);
        ExpectReports(model, LobbyTutorialActivity.Wall);
        Assert.That(model.NeedsRetryAfterMiss, Is.False, "완료 뒤엔 위치 복구 안 함");
        Assert.That(model.HoldsVulnerable, Is.True, "T34 끝(T40 알림)까지 유지");
    }

    [Test]
    public void Wall_SuppressInWall_IsNotCompletion()
    {
        var model = AtWall();
        Assert.That(model.OnSuppressStarted(), Is.True, "벽으로 칸 0 이면 제압은 실제로 일어난다");
        ExpectReports(model);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.Wall), "제압은 완료 조건 아님");
        Assert.That(model.OnSuppressDamage(), Is.False, "T34 제압 피해는 T40 마무리로 세지 않는다");
    }

    // ── T40(임시 — 옛 D) ──

    [Test]
    public void SuppressKill_ReleasesHold_ResumesSelfAction()
    {
        var model = AtSuppressKill();
        Assert.That(model.HoldsVulnerable, Is.False, "취약 무기한 보정 해제");
        Assert.That(model.AllowsSelfAction, Is.True);
        Assert.That(model.AllowsGaugeTransition, Is.True);
    }

    [Test]
    public void SuppressKill_SuppressWithoutDamage_NotFinishing_DeathPrevented()
    {
        var model = AtSuppressKill();
        Assert.That(model.OnSuppressStarted(), Is.True);
        Assert.That(model.OnSuppressEnded(), Is.True);
        Assert.That(model.IsFinishing, Is.False, "대기만으로는 마무리 아님");
        Assert.That(model.PreventsDeath, Is.True);
        Assert.That(model.OnDied(), Is.False);

        model.OnSuppressStarted();
        model.OnSuppressDamage();
        model.OnSuppressEnded();
        Assert.That(model.IsFinishing, Is.True);
        Assert.That(model.PreventsDeath, Is.False);
        Assert.That(model.TryConsumeFinishHealthRequest(), Is.True);
        Assert.That(model.TryConsumeFinishHealthRequest(), Is.False);
    }

    [Test]
    public void SuppressAcceleration_OffByDefault_OnlyWhenEnabledAndSuppressed()
    {
        var off = AtSuppressKill();
        off.OnSuppressStarted();
        Assert.That(off.AppliesSuppressAcceleration, Is.False);

        var on = new TrainingSparringModel(TrainingSparringSegmentCheck.Evaluate(BossCounterProgress.GaugeMax, 20f), true);
        on.OnFlowStageStarted(LobbyTutorialActivity.Counter, 1);
        Assert.That(on.AppliesSuppressAcceleration, Is.False);
    }

    // ── 칸 판정 ──

    [Test]
    public void SegmentCheck_RequiresTwoOrMore()
    {
        Assert.That(TrainingSparringSegmentCheck.Evaluate(100f, 20f).TotalSegments, Is.EqualTo(5));
        Assert.That(TrainingSparringSegmentCheck.Evaluate(100f, 20f).IsValid, Is.True);
        Assert.That(TrainingSparringSegmentCheck.Evaluate(100f, 100f).IsValid, Is.False);
        Assert.That(TrainingSparringSegmentCheck.Evaluate(100f, 0f).TotalSegments, Is.Zero);
    }
}
