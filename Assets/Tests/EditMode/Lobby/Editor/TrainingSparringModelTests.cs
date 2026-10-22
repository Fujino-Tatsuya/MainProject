using System.Collections.Generic;
using NUnit.Framework;

/// <summary>TRAIN_03 훈련 상대 상태기계 — 할 일 86 T5a(명세 v0.2 §9, 사용자 결정 10-22).</summary>
public sealed class TrainingSparringModelTests
{
    // 게이지 최대 / 감소량 = 5칸(23호 기본값 100 / 20 과 같은 비) — 테스트 입력일 뿐 모델엔 수치가 없다.
    static TrainingSparringModel Create(bool suppressAcceleration = false) =>
        new TrainingSparringModel(TrainingSparringSegmentCheck.Evaluate(BossCounterProgress.GaugeMax, 20f), suppressAcceleration);

    static List<LobbyTrainingStage> DrainReports(TrainingSparringModel model)
    {
        var reports = new List<LobbyTrainingStage>();
        while (model.TryConsumeStageReport(out var stage))
            reports.Add(stage);
        return reports;
    }

    static void ExpectReports(TrainingSparringModel model, params LobbyTrainingStage[] expected) =>
        Assert.That(DrainReports(model), Is.EqualTo(expected));

    static TrainingSparringModel AtA(bool suppressAcceleration = false)
    {
        var model = Create(suppressAcceleration);
        Assert.That(model.OnFlowStageStarted(LobbyTrainingStage.Train03A), Is.True);
        return model;
    }

    static TrainingSparringModel AtB(bool suppressAcceleration = false)
    {
        var model = AtA(suppressAcceleration);
        Assert.That(model.OnCounterSucceeded(), Is.True);
        Assert.That(model.OnCounterGroggyEnded(), Is.True);
        ExpectReports(model, LobbyTrainingStage.Train03A);
        Assert.That(model.OnFlowStageStarted(LobbyTrainingStage.Train03B), Is.True);
        return model;
    }

    static TrainingSparringModel AtC(bool suppressAcceleration = false)
    {
        var model = AtB(suppressAcceleration);
        Assert.That(model.OnCrowdControlApplied(TrainingSparringCrowdControl.VulnerableKnockback), Is.True);
        Assert.That(model.OnCrowdControlEnded(TrainingSparringCrowdControl.VulnerableKnockback), Is.True);
        ExpectReports(model, LobbyTrainingStage.Train03B);
        Assert.That(model.OnFlowStageStarted(LobbyTrainingStage.Train03C), Is.True);
        Assert.That(model.TryConsumeResetRequest(), Is.True);
        Assert.That(model.OnResetExecuted(), Is.True);
        return model;
    }

    static TrainingSparringModel AtD(bool suppressAcceleration = false)
    {
        var model = AtC(suppressAcceleration);
        Assert.That(model.OnWallImpact(reducedGauge: true), Is.True);
        Assert.That(model.OnSuppressStarted(), Is.True);
        ExpectReports(model, LobbyTrainingStage.Train03C);
        return model;
    }

    // ── 전 경로 ──

    [Test]
    public void A부터_D까지_정상_경로와_단계별_출력()
    {
        var model = Create();
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.Idle));
        Assert.That(model.AllowsSelfAction, Is.False);

        model.OnFlowStageStarted(LobbyTrainingStage.Train03A);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.CounterIntro));
        Assert.That(model.PreventsDeath, Is.True);
        Assert.That(model.AllowsGaugeTransition, Is.False);
        Assert.That(model.ShowsSegmentUi, Is.False);
        Assert.That(model.HoldsVulnerable, Is.False);
        Assert.That(model.AllowsSelfAction, Is.True);

        model.OnCounterSucceeded();
        Assert.That(model.TryConsumeStageReport(out _), Is.False);   // 성공만으론 아직 — 그로기 종료가 완료
        model.OnCounterGroggyEnded();
        ExpectReports(model, LobbyTrainingStage.Train03A);

        model.OnFlowStageStarted(LobbyTrainingStage.Train03B);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.CrowdControlIntro));
        Assert.That(model.HoldsVulnerable, Is.True);
        Assert.That(model.AllowsSelfAction, Is.False);
        Assert.That(model.AllowsGaugeTransition, Is.False);
        Assert.That(model.ShowsSegmentUi, Is.False);
        Assert.That(model.PreventsDeath, Is.True);

        model.OnCrowdControlApplied(TrainingSparringCrowdControl.PushStagger);
        Assert.That(model.TryConsumeStageReport(out _), Is.False);   // 적용만으론 아직 — 종료가 완료
        model.OnCrowdControlEnded(TrainingSparringCrowdControl.PushStagger);
        ExpectReports(model, LobbyTrainingStage.Train03B);

        model.OnFlowStageStarted(LobbyTrainingStage.Train03C);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.Resetting));
        Assert.That(model.HoldsVulnerable, Is.False);
        Assert.That(model.AllowsSelfAction, Is.False);
        Assert.That(model.OnResetExecuted(), Is.False);              // 요청을 꺼내기 전 실행 보고는 무시
        Assert.That(model.TryConsumeResetRequest(), Is.True);
        Assert.That(model.OnResetExecuted(), Is.True);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.WallAndSuppress));
        Assert.That(model.AllowsGaugeTransition, Is.True);
        Assert.That(model.ShowsSegmentUi, Is.True);
        Assert.That(model.AllowsSelfAction, Is.True);
        Assert.That(model.PreventsDeath, Is.True);

        Assert.That(model.OnWallImpact(reducedGauge: true), Is.True);
        model.OnSuppressStarted();
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.Finisher));
        ExpectReports(model, LobbyTrainingStage.Train03C);
        Assert.That(model.OnFlowStageStarted(LobbyTrainingStage.Train03D), Is.False);   // 이미 D — 확인만
        Assert.That(model.PreventsDeath, Is.True);
        Assert.That(model.ShowsSegmentUi, Is.True);

        Assert.That(model.OnSuppressDamage(), Is.True);
        Assert.That(model.PreventsDeath, Is.True);                   // 제압 종료까지 처치 방지
        Assert.That(model.TryConsumeFinishHealthRequest(), Is.False);
        model.OnSuppressEnded();
        Assert.That(model.PreventsDeath, Is.False);
        Assert.That(model.TryConsumeFinishHealthRequest(), Is.True);
        Assert.That(model.TryConsumeFinishHealthRequest(), Is.False);   // 1회

        Assert.That(model.OnDied(), Is.True);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.Completed));
        ExpectReports(model, LobbyTrainingStage.Train03D);
        Assert.That(model.AllowsSelfAction, Is.False);
        Assert.That(model.ShowsSegmentUi, Is.False);
    }

    [Test]
    public void 제압_가속은_보류_플래그가_켜진_경우에만_C_D_제압_중_true()
    {
        var off = AtD();
        Assert.That(off.IsSuppressActive, Is.True);
        Assert.That(off.AppliesSuppressAcceleration, Is.False);

        var atA = AtA(suppressAcceleration: true);
        Assert.That(atA.OnSuppressStarted(), Is.False);              // A = 전이 중지, 제압 없음
        Assert.That(atA.AppliesSuppressAcceleration, Is.False);

        var on = AtD(suppressAcceleration: true);
        Assert.That(on.AppliesSuppressAcceleration, Is.True);
        on.OnSuppressEnded();
        Assert.That(on.AppliesSuppressAcceleration, Is.False);
    }

    // ── 중복·역순 ──

    [Test]
    public void 중복_역순_단계_알림은_무시()
    {
        var model = Create();
        Assert.That(model.OnFlowStageStarted(LobbyTrainingStage.Train03B), Is.False);   // A 전 B
        Assert.That(model.OnFlowStageStarted(LobbyTrainingStage.Train03C), Is.False);
        Assert.That(model.OnFlowStageStarted(LobbyTrainingStage.Train03A), Is.True);
        Assert.That(model.OnFlowStageStarted(LobbyTrainingStage.Train03A), Is.False);   // 중복
        Assert.That(model.OnFlowStageStarted(LobbyTrainingStage.Train03B), Is.False);   // A 미완료
        Assert.That(model.OnFlowStageStarted(LobbyTrainingStage.Train02), Is.False);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.CounterIntro));

        model = AtB();
        Assert.That(model.OnFlowStageStarted(LobbyTrainingStage.Train03A), Is.False);   // 역순
        Assert.That(model.OnFlowStageStarted(LobbyTrainingStage.Train03B), Is.False);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.CrowdControlIntro));
    }

    [Test]
    public void A_간파_그로기_종료가_성공보다_먼저면_무시하고_완료_보고는_1회()
    {
        var model = AtA();
        Assert.That(model.OnCounterGroggyEnded(), Is.False);         // 성공 없는 그로기 종료
        Assert.That(model.OnCounterSucceeded(), Is.True);
        Assert.That(model.OnCounterSucceeded(), Is.False);
        Assert.That(model.OnCounterGroggyEnded(), Is.True);
        Assert.That(model.OnCounterGroggyEnded(), Is.False);
        ExpectReports(model, LobbyTrainingStage.Train03A);
    }

    [Test]
    public void A_B에서는_제압과_벽이_무시된다()
    {
        var model = AtA();
        Assert.That(model.OnSuppressStarted(), Is.False);
        Assert.That(model.OnWallImpact(true), Is.False);
        Assert.That(model.OnSuppressEnded(), Is.False);

        model = AtB();
        Assert.That(model.OnSuppressStarted(), Is.False);
        Assert.That(model.IsSuppressActive, Is.False);
    }

    // ── A 타격의 B 미집계 ──

    [Test]
    public void A에서_걸린_CC가_B에서_끝나도_B_완료가_아니다()
    {
        var model = AtA();
        model.OnCounterSucceeded();
        Assert.That(model.OnCrowdControlApplied(TrainingSparringCrowdControl.VulnerableKnockback), Is.False);   // A 그로기 중 타격
        model.OnCounterGroggyEnded();
        DrainReports(model);
        model.OnFlowStageStarted(LobbyTrainingStage.Train03B);

        Assert.That(model.OnCrowdControlEnded(TrainingSparringCrowdControl.VulnerableKnockback), Is.False);
        Assert.That(model.TryConsumeStageReport(out _), Is.False);
    }

    [Test]
    public void B_간파_성공과_그로기는_CC가_아니고_다른_종류_종료도_불인정()
    {
        var model = AtB();
        Assert.That(model.OnCounterSucceeded(), Is.False);
        Assert.That(model.OnCounterGroggyEnded(), Is.False);
        Assert.That(model.OnCrowdControlEnded(TrainingSparringCrowdControl.PushStagger), Is.False);   // 적용 없는 종료

        model.OnCrowdControlApplied(TrainingSparringCrowdControl.VulnerableKnockback);
        Assert.That(model.OnCrowdControlEnded(TrainingSparringCrowdControl.PushStagger), Is.False);   // 다른 종류
        Assert.That(model.TryConsumeStageReport(out _), Is.False);

        Assert.That(model.OnCrowdControlEnded(TrainingSparringCrowdControl.VulnerableKnockback), Is.True);
        Assert.That(model.OnCrowdControlEnded(TrainingSparringCrowdControl.VulnerableKnockback), Is.False);
        Assert.That(model.OnCrowdControlApplied(TrainingSparringCrowdControl.PushStagger), Is.False);   // 완료 뒤
        ExpectReports(model, LobbyTrainingStage.Train03B);
    }

    [Test]
    public void B_벽_충돌은_C로_이월되지_않는다()
    {
        var model = AtB();
        Assert.That(model.OnWallImpact(reducedGauge: true), Is.False);
        model.OnCrowdControlApplied(TrainingSparringCrowdControl.VulnerableKnockback);
        model.OnCrowdControlEnded(TrainingSparringCrowdControl.VulnerableKnockback);
        model.OnFlowStageStarted(LobbyTrainingStage.Train03C);
        model.TryConsumeResetRequest();
        model.OnResetExecuted();

        Assert.That(model.HasRecordedWall, Is.False);
        Assert.That(model.OnSuppressStarted(), Is.True);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.WallAndSuppress));   // 벽 없이 제압 → D 아님
    }

    // ── 초기화 1회 ──

    [Test]
    public void 초기화_요청은_1회_재접속_중복_메시지로_반복되지_않는다()
    {
        var model = AtC();
        Assert.That(model.OnFlowStageStarted(LobbyTrainingStage.Train03C), Is.False);
        Assert.That(model.TryConsumeResetRequest(), Is.False);
        Assert.That(model.OnResetExecuted(), Is.False);
        Assert.That(model.OnFlowStageStarted(LobbyTrainingStage.Train03B), Is.False);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.WallAndSuppress));
    }

    [Test]
    public void 초기화_대기_중_이벤트는_무시()
    {
        var model = AtB();
        model.OnCrowdControlApplied(TrainingSparringCrowdControl.PushStagger);
        model.OnCrowdControlEnded(TrainingSparringCrowdControl.PushStagger);
        model.OnFlowStageStarted(LobbyTrainingStage.Train03C);

        Assert.That(model.OnWallImpact(true), Is.False);
        Assert.That(model.OnSuppressStarted(), Is.False);
        Assert.That(model.OnFlowStageStarted(LobbyTrainingStage.Train03C), Is.False);
        Assert.That(model.TryConsumeResetRequest(), Is.True);
        Assert.That(model.TryConsumeResetRequest(), Is.False);
    }

    // ── C 벽·제압 ──

    [Test]
    public void 같은_넉백의_벽과_마지막_칸은_벽_먼저_기록하고_제압에서_D()
    {
        var model = AtC();
        Assert.That(model.OnWallImpact(reducedGauge: true), Is.True);   // 벽 감소로 0칸 → 상대가 벽을 먼저 알린다
        Assert.That(model.OnSuppressStarted(), Is.True);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.Finisher));
        ExpectReports(model, LobbyTrainingStage.Train03C);
    }

    [Test]
    public void 이미_0칸이라_감소가_거부된_벽_접촉은_불인정()
    {
        var model = AtC();
        Assert.That(model.OnWallImpact(reducedGauge: false), Is.False);
        Assert.That(model.HasRecordedWall, Is.False);
        model.OnSuppressStarted();
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.WallAndSuppress));
    }

    [Test]
    public void 벽은_한_번만_기록된다()
    {
        var model = AtC();
        Assert.That(model.OnWallImpact(true), Is.True);
        Assert.That(model.OnWallImpact(true), Is.False);   // 첫 벽 무전은 1회
        Assert.That(model.HasRecordedWall, Is.True);
    }

    [Test]
    public void 벽_전_제압은_정상_실행_종료_후_C_계속_이후_벽과_다음_제압으로_D()
    {
        var model = AtC();
        Assert.That(model.OnSuppressStarted(), Is.True);    // 간파만으로 먼저 제압
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.WallAndSuppress));
        Assert.That(model.OnSuppressDamage(), Is.False);    // C 의 제압 피해는 D 목표 아님
        Assert.That(model.OnWallImpact(true), Is.False);    // 제압 중(0칸) 벽 보고는 기록하지 않는다
        Assert.That(model.OnSuppressEnded(), Is.True);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.WallAndSuppress));
        Assert.That(model.PreventsDeath, Is.True);
        Assert.That(model.TryConsumeStageReport(out _), Is.False);   // 이전 제압으로 D 자동 완료 없음

        Assert.That(model.OnWallImpact(true), Is.True);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.WallAndSuppress));   // 벽만으론 D 아님
        Assert.That(model.OnSuppressStarted(), Is.True);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.Finisher));
        ExpectReports(model, LobbyTrainingStage.Train03C);
    }

    [Test]
    public void C에서_사망은_완료가_아니다()
    {
        var model = AtC();
        Assert.That(model.OnDied(), Is.False);
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.WallAndSuppress));
    }

    // ── D ──

    [Test]
    public void D_제압_중_피해가_없으면_다음_사이클에서_재확인()
    {
        var model = AtD();
        Assert.That(model.OnSuppressEnded(), Is.True);           // 피해 없이 종료
        Assert.That(model.Stage, Is.EqualTo(TrainingSparringStage.Finisher));
        Assert.That(model.PreventsDeath, Is.True);
        Assert.That(model.TryConsumeFinishHealthRequest(), Is.False);
        Assert.That(model.OnSuppressDamage(), Is.False);         // 제압 밖 피해는 불인정
        Assert.That(model.OnDied(), Is.False);

        Assert.That(model.OnSuppressStarted(), Is.True);         // 다음 사이클(간파·벽·제압)
        Assert.That(model.OnSuppressDamage(), Is.True);
        Assert.That(model.OnSuppressDamage(), Is.False);
        Assert.That(model.OnSuppressEnded(), Is.True);
        Assert.That(model.PreventsDeath, Is.False);
        Assert.That(model.TryConsumeFinishHealthRequest(), Is.True);
        Assert.That(model.OnDied(), Is.True);
        ExpectReports(model, LobbyTrainingStage.Train03D);
    }

    [Test]
    public void D_마무리_표시는_제압_피해_뒤_제압_종료부터_사망_전까지만()
    {
        var model = AtD();
        Assert.That(model.IsFinishing, Is.False);                // 제압 중
        Assert.That(model.OnSuppressEnded(), Is.True);           // 피해 없이 종료 — 칸 표시 그대로
        Assert.That(model.IsFinishing, Is.False);

        Assert.That(model.OnSuppressStarted(), Is.True);
        Assert.That(model.OnSuppressDamage(), Is.True);
        Assert.That(model.IsFinishing, Is.False);                // 피해만으론 아직 — 제압 종료가 마무리
        Assert.That(model.OnSuppressEnded(), Is.True);
        Assert.That(model.IsFinishing, Is.True);
        Assert.That(model.TryConsumeFinishHealthRequest(), Is.True);
        Assert.That(model.IsFinishing, Is.True);                 // 체력 요청을 꺼내도 사망 전까진 유지

        Assert.That(model.OnDied(), Is.True);
        Assert.That(model.IsFinishing, Is.False);
        Assert.That(Create().IsFinishing, Is.False);             // 새로 스폰한 상대
    }

    [Test]
    public void D_중복_제압_시작과_종료는_무시하고_마무리_뒤_제압도_무시()
    {
        var model = AtD();
        Assert.That(model.OnSuppressStarted(), Is.False);
        model.OnSuppressDamage();
        Assert.That(model.OnSuppressEnded(), Is.True);
        Assert.That(model.OnSuppressEnded(), Is.False);
        Assert.That(model.OnSuppressStarted(), Is.False);        // 마무리 중
        Assert.That(model.PreventsDeath, Is.False);
        Assert.That(model.OnDied(), Is.True);
        Assert.That(model.OnDied(), Is.False);
        ExpectReports(model, LobbyTrainingStage.Train03D);
    }

    [Test]
    public void 보고를_꺼내지_않아도_완료마다_순서대로_1회씩_남는다()
    {
        var model = AtC();
        model.OnWallImpact(true);
        model.OnSuppressStarted();
        model.OnSuppressDamage();
        model.OnSuppressEnded();
        model.OnDied();
        ExpectReports(model, LobbyTrainingStage.Train03C, LobbyTrainingStage.Train03D);
    }

    // ── 총 칸 ──

    [Test]
    public void 총_칸은_게이지_최대_나누기_감소량_올림()
    {
        Assert.That(TrainingSparringSegmentCheck.Evaluate(100f, 20f).TotalSegments, Is.EqualTo(5));
        Assert.That(TrainingSparringSegmentCheck.Evaluate(100f, 30f).TotalSegments, Is.EqualTo(4));
        Assert.That(TrainingSparringSegmentCheck.Evaluate(100f, 50f).TotalSegments, Is.EqualTo(2));
        Assert.That(TrainingSparringSegmentCheck.Evaluate(100f, 50f).IsValid, Is.True);
        Assert.That(TrainingSparringSegmentCheck.Evaluate(100f, 50f).Error, Is.Null);
    }

    [Test]
    public void 총_칸이_2_미만이면_오류를_내고_칸을_늘리지_않는다()
    {
        var one = TrainingSparringSegmentCheck.Evaluate(100f, 100f);
        Assert.That(one.TotalSegments, Is.EqualTo(1));
        Assert.That(one.IsValid, Is.False);
        Assert.That(one.Error, Is.Not.Null.And.Not.Empty);

        var zeroStep = TrainingSparringSegmentCheck.Evaluate(100f, 0f);
        Assert.That(zeroStep.TotalSegments, Is.EqualTo(0));
        Assert.That(zeroStep.IsValid, Is.False);

        var model = new TrainingSparringModel(one);
        Assert.That(model.Segments.TotalSegments, Is.EqualTo(1));   // 몰래 추가 없음
    }
}
