using System.Collections.Generic;
using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 적중 섬광(할 일 132 단계 5) — 시간 → 단계·전체 길이, 히트스톱 중(성공 Started 순간) 시작, 오너/팀원/실패 분기,
/// 오너 암전 걷힘 = 섬광 순간, 강제 리셋, 덮어쓸 텍스처 칸 폴백, 디렉터 칸·프리팹 연결.
/// </summary>
public sealed class InterruptHitFlashTests
{
    private const string OverlayShaderName = "BeaverLobby/UI/InterruptHitFlashOverlay";
    private const string WorldShaderName = "BeaverLobby/VFX/InterruptHitFlash";
    private const string NetworkManagerPath = "Assets/2.Prefabs/Network/NetworkManager.prefab";
    private const string WorldVfxPath = "Assets/5.VFX/Interrupt/HitFlash/FX_InterruptHitFlash.prefab";
    private const string OverlayMaterialPath = "Assets/5.VFX/Interrupt/HitFlash/M_InterruptHitFlashOverlay.mat";

    // 이진 소수로 딱 떨어지는 길이 — 단계 경계 비교가 부동소수 오차로 흔들리지 않게(디렉터 기본값은 따로 확인).
    private sealed class FakeSettings : IInterruptHitFlashSettings, IInterruptOwnerPresentationSettings
    {
        public float Burst = 0.125f;
        public float Glare = 0.125f;
        public float Invert = 0.125f;
        public float Return = 0.125f;
        public bool SpeedLines = true;
        public InterruptEffectTimeBase TimeBase = InterruptEffectTimeBase.Unscaled; // 디렉터 기본과 같게

        public float HitFlashBurstDuration => Burst;
        public float HitFlashGlareDuration => Glare;
        public float HitFlashInvertDuration => Invert;
        public float HitFlashReturnDuration => Return;
        public bool HitFlashSpeedLines => SpeedLines;
        public InterruptEffectTimeBase HitFlashTimeBase => TimeBase;

        public float OwnerDimInDuration => 0.125f;
        public float OwnerDimOutDuration => 0.25f;
    }

    private sealed class FakeOverlay : IInterruptHitFlashOverlayOutput
    {
        public InterruptHitFlashStage Stage;
        public float Progress;
        public Vector3 Center;
        public bool SpeedLines;
        public int Shows;
        public int Hides;

        public void Show(InterruptHitFlashStage stage, float progress, Vector3 worldCenter, bool speedLines)
        {
            Stage = stage;
            Progress = progress;
            Center = worldCenter;
            SpeedLines = speedLines;
            Shows++;
        }

        public void Hide()
        {
            Stage = InterruptHitFlashStage.None;
            Hides++;
        }
    }

    private sealed class FakeDim : IInterruptOwnerDimOutput, IInterruptLightGatherOutput
    {
        public float Dim;
        public void SetOwnerDim(float intensity) => Dim = intensity;
        public void StartLightGather(PlayerInterruptSkillBase skill) { }
        public void StopLightGather() { }
    }

    private static readonly Vector3 Target = new Vector3(3f, 0f, 4f);

    private FakeSettings _settings;
    private FakeOverlay _overlay;
    private List<InterruptHitFlashMoment> _flashes;
    private InterruptHitFlashSequencer _sequencer;
    private readonly List<Object> _created = new List<Object>();

    [SetUp]
    public void SetUp()
    {
        _settings = new FakeSettings();
        _overlay = new FakeOverlay();
        _flashes = new List<InterruptHitFlashMoment>();
        _sequencer = new InterruptHitFlashSequencer(_settings, _overlay, _flashes.Add);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object created in _created)
        {
            if (created != null)
                Object.DestroyImmediate(created);
        }

        _created.Clear();
    }

    private static InterruptHitFlashMoment Moment(bool viewerIsCaster, bool ownerOverlay) =>
        new InterruptHitFlashMoment(InterruptProductionEvent.LocalSuccess(7, 2, Target), viewerIsCaster, ownerOverlay);

    // ─── 시간표 ───

    [Test]
    public void 시간표_기본_4단계_섬광_순간과_전체_길이()
    {
        var timeline = new InterruptHitFlashTimeline(0.1f, 0.1f, 0.1f, 0.1f);
        Assert.AreEqual(0.3f, timeline.FlashMoment, 1e-5f, "섬광 순간 = 1~3단계 합");
        Assert.AreEqual(0.4f, timeline.Total, 1e-5f, "전체 약 0.4초");
    }

    [Test]
    public void 시간에서_단계와_진행도()
    {
        var timeline = InterruptHitFlashTimeline.From(_settings);
        AssertStage(timeline, -0.01f, InterruptHitFlashStage.None, 0f);
        AssertStage(timeline, 0f, InterruptHitFlashStage.Burst, 0f);
        AssertStage(timeline, 0.0625f, InterruptHitFlashStage.Burst, 0.5f);
        AssertStage(timeline, 0.125f, InterruptHitFlashStage.Glare, 0f);
        AssertStage(timeline, 0.3125f, InterruptHitFlashStage.Invert, 0.5f);
        AssertStage(timeline, 0.375f, InterruptHitFlashStage.Return, 0f);
        AssertStage(timeline, 0.4375f, InterruptHitFlashStage.Return, 0.5f);
        AssertStage(timeline, 0.5f, InterruptHitFlashStage.None, 0f);
    }

    [Test]
    public void 길이_0_단계는_건너뛰고_음수_NaN_은_0()
    {
        var timeline = new InterruptHitFlashTimeline(0.125f, 0f, -1f, float.NaN);
        Assert.AreEqual(0.125f, timeline.FlashMoment);
        Assert.AreEqual(0.125f, timeline.Total);
        AssertStage(timeline, 0.0625f, InterruptHitFlashStage.Burst, 0.5f);
        AssertStage(timeline, 0.125f, InterruptHitFlashStage.None, 0f);
        Assert.AreEqual(InterruptHitFlashStage.None, timeline.StageAt(float.NaN, out _));
        Assert.AreEqual(0f, default(InterruptHitFlashTimeline).Total, "칸이 없으면 섬광 없음");
    }

    private static void AssertStage(InterruptHitFlashTimeline timeline, float elapsed, InterruptHitFlashStage stage, float progress)
    {
        Assert.AreEqual(stage, timeline.StageAt(elapsed, out float actual), $"경과 {elapsed}");
        Assert.AreEqual(progress, actual, 1e-4f, $"경과 {elapsed} 진행도");
    }

    // ─── 오버레이 진행 ───

    [Test]
    public void 오버레이는_성공_Started_순간_1단계부터_히트스톱_길이와_무관()
    {
        // 히트스톱 길이와 상관없이 Started 순간 바로 1단계(진행 빠르기는 진행기 시계 — 아래 '배율 곡선' 테스트).
        _sequencer.BeginOwnerOverlay(Target, 20f);
        Assert.IsTrue(_sequencer.OverlayActive);
        Assert.AreEqual(InterruptHitFlashStage.Burst, _overlay.Stage);
        Assert.AreEqual(0f, _overlay.Progress);
        Assert.AreEqual(Target, _overlay.Center);
        Assert.IsTrue(_overlay.SpeedLines);
    }

    [Test]
    public void 오버레이_단계가_진행기_시계로_넘어가고_끝나면_한_번_숨긴다()
    {
        _sequencer.BeginOwnerOverlay(Target, 20f);
        _sequencer.Tick(20.1875f);
        Assert.AreEqual(InterruptHitFlashStage.Glare, _overlay.Stage);
        Assert.AreEqual(0.5f, _overlay.Progress, 1e-4f);
        _sequencer.Tick(20.25f);
        Assert.AreEqual(InterruptHitFlashStage.Invert, _overlay.Stage);
        _sequencer.Tick(20.375f);
        Assert.AreEqual(InterruptHitFlashStage.Return, _overlay.Stage);
        _sequencer.Tick(20.5f);
        Assert.IsFalse(_sequencer.OverlayActive);
        Assert.AreEqual(InterruptHitFlashStage.None, _sequencer.OverlayStage);
        Assert.AreEqual(1, _overlay.Hides);
        _sequencer.Tick(20.6f);
        Assert.AreEqual(1, _overlay.Hides, "숨긴 뒤엔 다시 부르지 않는다");
    }

    [Test]
    public void 집중선_칸은_시작_순간_값()
    {
        _settings.SpeedLines = false;
        _sequencer.BeginOwnerOverlay(Target, 20f);
        _settings.SpeedLines = true;
        _sequencer.Tick(20.3f);
        Assert.IsFalse(_overlay.SpeedLines);
    }

    // ─── 섬광 순간 ───

    [Test]
    public void 섬광_순간은_1에서_3단계_뒤에_한_번()
    {
        _sequencer.ScheduleFlash(Moment(false, false), 20f);
        _sequencer.Tick(20.25f);
        Assert.IsEmpty(_flashes, "3단계 끝 전");
        _sequencer.Tick(20.375f);
        Assert.AreEqual(1, _flashes.Count);
        Assert.AreEqual(Target, _flashes[0].Event.TargetPosition);
        _sequencer.Tick(20.5f);
        Assert.AreEqual(1, _flashes.Count);
        Assert.AreEqual(0, _sequencer.PendingFlashCount);
    }

    [Test]
    public void 섬광_순간_길이_0이면_바로()
    {
        _settings.Burst = _settings.Glare = _settings.Invert = 0f;
        _sequencer.ScheduleFlash(Moment(false, false), 20f);
        Assert.AreEqual(1, _flashes.Count);
    }

    [Test]
    public void 여러_발동자_성공은_각각_섬광_순간()
    {
        _sequencer.ScheduleFlash(Moment(false, false), 20f);
        _sequencer.ScheduleFlash(Moment(true, true), 20.125f);
        _sequencer.Tick(20.375f);
        Assert.AreEqual(1, _flashes.Count);
        _sequencer.Tick(20.5f);
        Assert.AreEqual(2, _flashes.Count);
        Assert.IsTrue(_flashes[1].OwnerOverlay);
    }

    // ─── 강제 리셋 ───

    [Test]
    public void 강제_리셋은_오버레이_즉시_끄고_섬광_순간_취소()
    {
        _sequencer.BeginOwnerOverlay(Target, 20f);
        _sequencer.ScheduleFlash(Moment(true, true), 20f);
        _sequencer.Tick(20.1f);
        _sequencer.ForcedReset();
        Assert.IsFalse(_sequencer.OverlayActive);
        Assert.AreEqual(1, _overlay.Hides);
        Assert.AreEqual(InterruptHitFlashStage.None, _overlay.Stage);
        _sequencer.Tick(20.5f);
        Assert.IsEmpty(_flashes);
        Assert.AreEqual(1, _overlay.Hides, "리셋 뒤 Tick 은 다시 보이지도 숨기지도 않는다");
    }

    [Test]
    public void 아무것도_없을_때_강제_리셋은_숨김을_부르지_않는다()
    {
        _sequencer.ForcedReset();
        Assert.AreEqual(0, _overlay.Hides);
    }

    // ─── 분기 ───

    [Test]
    public void 오너_오버레이는_내_성공이고_암전이_켜졌을_때만()
    {
        Assert.IsTrue(InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.Success, true, true));
        Assert.IsFalse(InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.Success, true, false), "약 시도(대상 없음 포함)의 성공");
        Assert.IsFalse(InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.Success, false, true), "팀원 화면");
        Assert.IsFalse(InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.Attempt, true, true), "시도 슬로우");
        Assert.IsFalse(InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.AttemptWeak, true, true));
    }

    [Test]
    public void 섬광_순간_암순응_선택()
    {
        Assert.AreEqual(InterruptHitFlashDarkAdaptation.Others, InterruptHitFlashRules.SelectDarkAdaptation(false, false));
        Assert.AreEqual(InterruptHitFlashDarkAdaptation.Others, InterruptHitFlashRules.SelectDarkAdaptation(false, true));
        Assert.AreEqual(InterruptHitFlashDarkAdaptation.None, InterruptHitFlashRules.SelectDarkAdaptation(true, true),
            "본인 + 오버레이 = 마스크 암전·섬광이 대신");
        Assert.AreEqual(InterruptHitFlashDarkAdaptation.Self, InterruptHitFlashRules.SelectDarkAdaptation(true, false));
    }

    [Test]
    public void 오너_암전_걷힘은_섬광_순간과_같은_프레임()
    {
        // 디렉터와 같은 배선 — 진행기의 섬광 순간이 오너 암전도 끈다(시간표 하나).
        var dim = new FakeDim();
        var owner = new InterruptOwnerPresentation(_settings, dim, dim);
        var sequencer = new InterruptHitFlashSequencer(_settings, _overlay, m =>
        {
            _flashes.Add(m);
            if (m.ViewerIsCaster)
                owner.SuccessFlashMoment(m.Event.TriggerId, 0f);
        });
        owner.Predict(InterruptAttemptForecast.Strong, null, 20f);
        owner.Tick(20.125f);
        owner.ServerStarted(7, SlowMotionPurpose.Success, true, 20.25f);
        Assert.IsTrue(InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.Success, true, owner.SuccessDimmed));

        sequencer.BeginOwnerOverlay(Target, 20.25f);
        sequencer.ScheduleFlash(Moment(true, true), 20.25f);

        owner.Tick(20.5f);
        sequencer.Tick(20.5f);
        Assert.AreEqual(1f, dim.Dim, "3단계 흰 반전 동안 암전 유지");
        Assert.AreEqual(InterruptHitFlashStage.Invert, _overlay.Stage);
        Assert.IsEmpty(_flashes);

        owner.Tick(20.625f);
        sequencer.Tick(20.625f);
        Assert.AreEqual(0f, dim.Dim, "4단계 시작 = 암전 즉시 끔");
        Assert.AreEqual(InterruptHitFlashStage.Return, _overlay.Stage);
        Assert.AreEqual(1, _flashes.Count, "같은 순간 월드 섬광");
    }

    // ─── 시간 기준(할 일 132 §14) ───

    // 성공 프로필(NetworkManager.prefab 값) — 히트스톱 0.05s×0.02 → 진입 0.05s → 유지 0.25 1.2s → 복귀 0.4s.
    private static SlowMotionProfile SuccessProfile() => new SlowMotionProfile
    {
        scale = 0.25f, hitStopDuration = 0.05f, hitStopScale = 0.02f,
        enterDuration = 0.05f, holdDuration = 1.2f, exitDuration = 0.4f, failExitDuration = 0.1f,
    };

    private struct CurveRun
    {
        public float FirstBurstProgressAfterHitStop; // 히트스톱 끝(실시간 0.05) 1단계 진행도
        public float[] StageStartReal;               // 단계 1~4 시작·끝(실시간) — [0]=1단계 시작 … [4]=끝
        public float FlashReal;                       // 섬광 순간(실시간)
        public float DimOffReal;                      // 오너 암전이 0 이 된 때(실시간)
    }

    // 실시간 1/1000초 프레임으로 성공 슬로우 곡선을 돌린다 — 진행기는 칸의 시계(스케일 = 실시간 − 덜 흐른 시간), 오너 기계는 실시간.
    private CurveRun RunUnderSuccessCurve(InterruptEffectTimeBase timeBase)
    {
        const double Dt = 0.001;
        _settings.Burst = _settings.Glare = _settings.Invert = _settings.Return = 0.1f;
        _settings.TimeBase = timeBase;

        var timeline = new SlowMotionTimeline();
        timeline.Start(0.0, SuccessProfile());
        float ClockAt(double t) => timeBase == InterruptEffectTimeBase.Scaled ? (float)(t - timeline.LostTimeUntil(t)) : (float)t;

        var dim = new FakeDim();
        var owner = new InterruptOwnerPresentation(_settings, dim, dim);
        var run = new CurveRun { StageStartReal = new float[5], FlashReal = -1f, DimOffReal = -1f };
        double now = 0.0;
        var sequencer = new InterruptHitFlashSequencer(_settings, _overlay, m =>
        {
            run.FlashReal = (float)now;
            if (m.ViewerIsCaster)
                owner.SuccessFlashMoment(m.Event.TriggerId, (float)now);
        });

        // 강 시도 → 암전 다 들어온 뒤 성공(실시간 0). 시도 중 암전은 0 → 1 로 이미 올라 있다.
        owner.Predict(InterruptAttemptForecast.Strong, null, -1f);
        owner.Tick(-0.5f);
        owner.ServerStarted(7, SlowMotionPurpose.Success, true, 0f);
        Assert.AreEqual(timeBase, sequencer.Clock);
        sequencer.BeginOwnerOverlay(Target, ClockAt(0.0));
        sequencer.ScheduleFlash(Moment(true, true), ClockAt(0.0));

        InterruptHitFlashStage last = InterruptHitFlashStage.Burst;
        for (int i = 1; i <= 3000; i++)
        {
            now = i * Dt;
            owner.Tick((float)now);
            sequencer.Tick(ClockAt(now));
            if (i == 50)
                run.FirstBurstProgressAfterHitStop = sequencer.OverlayStage == InterruptHitFlashStage.Burst ? sequencer.OverlayProgress : -1f;
            if (run.DimOffReal < 0f && dim.Dim <= 0f)
                run.DimOffReal = (float)now;

            InterruptHitFlashStage stage = sequencer.OverlayStage;
            if (stage != last)
            {
                int index = stage == InterruptHitFlashStage.None ? 4 : (int)stage - 1;
                run.StageStartReal[index] = (float)now;
                last = stage;
            }

            if (stage == InterruptHitFlashStage.None && run.FlashReal >= 0f)
                break;
        }

        return run;
    }

    [Test]
    public void 칸을_스케일로_바꾸면_섬광_단계와_섬광_순간이_성공_슬로우를_따라_늘어난다()
    {
        CurveRun run = RunUnderSuccessCurve(InterruptEffectTimeBase.Scaled);

        // 히트스톱(0.05s×0.02) 동안 스케일 0.001 — 1단계는 1% 만 나아간 채 거의 멈춰 보인다.
        Assert.AreEqual(0.01f, run.FirstBurstProgressAfterHitStop, 0.002f);

        // 해석값(보고 표): 히트스톱·진입 실시간 0.1 동안 스케일 0.00775, 이후 유지 0.25 — 단계 하나(스케일 0.1) = 실시간 0.4.
        const float Tol = 0.0025f;
        Assert.AreEqual(0.469f, run.StageStartReal[1], Tol, "2단계 시작(1단계 실시간 약 0.47s)");
        Assert.AreEqual(0.869f, run.StageStartReal[2], Tol, "3단계 시작");
        Assert.AreEqual(1.269f, run.StageStartReal[3], Tol, "4단계 시작 = 섬광 순간");
        Assert.AreEqual(1.474f, run.StageStartReal[4], Tol, "끝 — 4단계는 복귀 곡선(0.25 → 1)에 걸쳐 약 0.2s");

        Assert.AreEqual(run.StageStartReal[3], run.FlashReal, 1e-6f, "섬광 순간(월드 섬광·암순응) = 진행기 4단계 시작 같은 프레임");
        Assert.AreEqual(run.FlashReal, run.DimOffReal, 1e-6f, "오너 암전 끔 = 섬광 순간 같은 프레임");
    }

    [Test]
    public void 기본_실시간_시계면_히트스톱_슬로우와_무관하게_섬광_순간_0_3초_끝_0_4초()
    {
        var director = Track(new GameObject("Director")).AddComponent<InterruptProductionDirector>();
        CurveRun run = RunUnderSuccessCurve(director.HitFlashTimeBase);
        Assert.AreEqual(0.5f, run.FirstBurstProgressAfterHitStop, 0.02f, "히트스톱 중에도 실시간으로 나아간다");
        Assert.AreEqual(0.1f, run.StageStartReal[1], 0.0015f);
        Assert.AreEqual(0.2f, run.StageStartReal[2], 0.0015f);
        Assert.AreEqual(0.3f, run.StageStartReal[3], 0.0015f, "4단계 시작 = 섬광 순간 실시간 0.3s");
        Assert.AreEqual(0.4f, run.StageStartReal[4], 0.0015f, "끝 실시간 0.4s");
        Assert.AreEqual(run.StageStartReal[3], run.FlashReal, 1e-6f, "섬광 순간(월드 섬광·암순응) = 진행기 4단계 시작 같은 프레임");
        Assert.AreEqual(run.FlashReal, run.DimOffReal, 1e-6f, "오너 암전 끔 = 섬광 순간 같은 프레임");
    }

    [Test]
    public void 오너_암전_들어옴과_걷힘은_슬로우_중에도_실시간()
    {
        // 들어옴 0.125·걷힘 0.25(실시간) — 진행기 시계와 상관없이 실시간 now 로만 램프한다.
        var dim = new FakeDim();
        var owner = new InterruptOwnerPresentation(_settings, dim, dim);
        owner.Predict(InterruptAttemptForecast.Strong, null, 0f);
        owner.Tick(0.0625f);
        Assert.AreEqual(0.5f, dim.Dim, 1e-4f, "들어옴 절반 = 실시간 0.0625s");
        owner.Tick(0.125f);
        Assert.AreEqual(1f, dim.Dim);

        owner.ServerStarted(5, SlowMotionPurpose.Attempt, true, 0.125f);
        owner.ServerFailed(5, 0.25f);
        owner.Tick(0.375f);
        Assert.AreEqual(0.5f, dim.Dim, 1e-4f, "걷힘 절반 = 실시간 0.125s");
        owner.Tick(0.5f);
        Assert.AreEqual(0f, dim.Dim);
    }

    [Test]
    public void 진행기_시계는_쉬는_동안만_칸을_따른다()
    {
        Assert.AreEqual(InterruptEffectTimeBase.Unscaled, _sequencer.Clock, "기본 실시간");
        _settings.TimeBase = InterruptEffectTimeBase.Scaled;
        Assert.AreEqual(InterruptEffectTimeBase.Scaled, _sequencer.Clock, "쉬는 중엔 바로 따른다");

        _sequencer.ScheduleFlash(Moment(false, false), 0f);
        _settings.TimeBase = InterruptEffectTimeBase.Unscaled;
        Assert.AreEqual(InterruptEffectTimeBase.Scaled, _sequencer.Clock, "진행 중 칸을 바꾸면 다음 성공부터");

        _sequencer.Tick(1f);
        Assert.AreEqual(InterruptEffectTimeBase.Unscaled, _sequencer.Clock);
    }

    [Test]
    public void 시간_기준_시계_선택()
    {
        Assert.IsTrue(InterruptEffectTime.UsesUnscaledTime(InterruptEffectTimeBase.Unscaled));
        Assert.IsFalse(InterruptEffectTime.UsesUnscaledTime(InterruptEffectTimeBase.Scaled));
        Assert.AreEqual(Time.unscaledTime, InterruptEffectTime.Now(InterruptEffectTimeBase.Unscaled));
        Assert.AreEqual(Time.time, InterruptEffectTime.Now(InterruptEffectTimeBase.Scaled));
    }

    [Test]
    public void 실패_대상_없음_약은_오버레이_조건이_서지_않는다()
    {
        var dim = new FakeDim();
        var owner = new InterruptOwnerPresentation(_settings, dim, dim);

        owner.Predict(InterruptAttemptForecast.Strong, null, 20f);
        owner.ServerStarted(5, SlowMotionPurpose.Attempt, true, 20f);
        owner.ServerFailed(5, 20.1f);
        Assert.IsFalse(owner.SuccessDimmed, "실패 = 성공 사건이 없다");

        owner.Predict(InterruptAttemptForecast.NoTarget, null, 21f);
        Assert.IsFalse(owner.SuccessDimmed);

        owner.ForcedReset();
        owner.Predict(InterruptAttemptForecast.Weak, null, 22f);
        owner.ServerStarted(6, SlowMotionPurpose.Success, true, 22.1f);
        Assert.IsFalse(InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.Success, true, owner.SuccessDimmed));
    }

    // ─── 덮어쓸 텍스처 칸 폴백 ───

    [Test]
    public void 오버레이_텍스처_칸_순번()
    {
        Assert.AreEqual(0, InterruptHitFlashRules.OverlayTextureIndex(InterruptHitFlashStage.Burst));
        Assert.AreEqual(1, InterruptHitFlashRules.OverlayTextureIndex(InterruptHitFlashStage.Glare));
        Assert.AreEqual(2, InterruptHitFlashRules.OverlayTextureIndex(InterruptHitFlashStage.Invert));
        Assert.AreEqual(2, InterruptHitFlashRules.OverlayTextureIndex(InterruptHitFlashStage.Return), "4단계는 3단계 그림이 걷힌다");
        Assert.AreEqual(-1, InterruptHitFlashRules.OverlayTextureIndex(InterruptHitFlashStage.None));
    }

    [Test]
    public void 오버레이_칸이_비면_절차_생성_차면_그_단계만_텍스처()
    {
        Shader shader = Shader.Find(OverlayShaderName);
        Assert.IsNotNull(shader, OverlayShaderName);
        var material = Track(new Material(shader));
        var texture = Track(new Texture2D(2, 2));

        InterruptHitFlashRules.ApplyOverlayKeywords(material);
        foreach (string keyword in InterruptHitFlashRules.OverlayTextureKeywords)
            Assert.IsFalse(material.IsKeywordEnabled(keyword), keyword);

        material.SetTexture("_GlareTex", texture);
        Assert.IsTrue(InterruptHitFlashRules.ApplyOverlayKeywords(material));
        Assert.IsFalse(material.IsKeywordEnabled("_BURST_TEX_ON"));
        Assert.IsTrue(material.IsKeywordEnabled("_GLARE_TEX_ON"));
        Assert.IsFalse(material.IsKeywordEnabled("_INVERT_TEX_ON"));
        Assert.IsFalse(InterruptHitFlashRules.ApplyOverlayKeywords(material), "바뀐 것 없으면 false");

        material.SetTexture("_GlareTex", null);
        Assert.IsTrue(InterruptHitFlashRules.ApplyOverlayKeywords(material));
        Assert.IsFalse(material.IsKeywordEnabled("_GLARE_TEX_ON"), "비우면 다시 절차 생성");
    }

    [Test]
    public void 월드_섬광_재질_칸은_빛_모임과_같은_키워드_규칙()
    {
        Shader shader = Shader.Find(WorldShaderName);
        Assert.IsNotNull(shader, WorldShaderName);
        var material = Track(new Material(shader));
        Assert.IsFalse(InterruptLightGatherRules.ApplyOverrideKeyword(material));
        material.SetTexture(InterruptLightGatherRules.OverrideTextureProperty, Track(new Texture2D(2, 2)));
        Assert.IsTrue(InterruptLightGatherRules.ApplyOverrideKeyword(material));
        Assert.IsTrue(material.IsKeywordEnabled(InterruptLightGatherRules.OverrideTextureKeyword));
    }

    [Test]
    public void 셰이더_오류_없음()
    {
        foreach (string name in new[] { OverlayShaderName, WorldShaderName })
        {
            Shader shader = Shader.Find(name);
            Assert.IsNotNull(shader, name);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), name);
        }
    }

    // ─── 순서·디렉터·프리팹 ───

    [Test]
    public void 섬광_오버레이는_암전_덮개_위()
    {
        Assert.Greater(InterruptHitFlashOverlay.SortingOrder, InterruptOwnerHudCover.SortingOrder);
        Assert.Less(InterruptHitFlashOverlay.SortingOrder, short.MaxValue, "PerformanceOverlay 아래");
    }

    [Test]
    public void 디렉터_섬광_칸_기본값()
    {
        var go = Track(new GameObject("Director"));
        var director = go.AddComponent<InterruptProductionDirector>();
        var so = new SerializedObject(director);
        Assert.AreEqual(0.1f, so.FindProperty("hitFlashBurstDuration").floatValue, 1e-6f);
        Assert.AreEqual(0.1f, so.FindProperty("hitFlashGlareDuration").floatValue, 1e-6f);
        Assert.AreEqual(0.1f, so.FindProperty("hitFlashInvertDuration").floatValue, 1e-6f);
        Assert.AreEqual(0.1f, so.FindProperty("hitFlashReturnDuration").floatValue, 1e-6f);
        Assert.IsTrue(so.FindProperty("hitFlashSpeedLines").boolValue);
        Assert.AreEqual(1f, so.FindProperty("hitFlashWorldScale").floatValue, 1e-6f);
        Assert.AreEqual(1f, so.FindProperty("hitFlashHeightOffset").floatValue, 1e-6f);
        Assert.AreEqual(InterruptEffectTimeBase.Unscaled, director.HitFlashTimeBase, "진행기 기본 = 실시간 시계");
        Assert.AreEqual(0.3f, InterruptHitFlashTimeline.From(director).FlashMoment, 1e-5f, "섬광 순간(진행기 시계)");
    }

    [Test]
    public void NetworkManager_디렉터에_오버레이_재질과_월드_섬광_연결()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPath);
        Assert.IsNotNull(prefab, NetworkManagerPath);
        var so = new SerializedObject(prefab.GetComponent<InterruptProductionDirector>());

        var material = so.FindProperty("hitFlashOverlayMaterial").objectReferenceValue as Material;
        Assert.IsNotNull(material, "hitFlashOverlayMaterial");
        Assert.AreEqual(OverlayMaterialPath, AssetDatabase.GetAssetPath(material));
        Assert.AreEqual(OverlayShaderName, material.shader.name);

        var vfx = so.FindProperty("hitFlashWorldVfx").objectReferenceValue as InterruptHitFlashVfx;
        Assert.IsNotNull(vfx, "hitFlashWorldVfx");
        Assert.AreEqual(WorldVfxPath, AssetDatabase.GetAssetPath(vfx));
        Assert.AreEqual(InterruptEffectTimeBase.Unscaled, prefab.GetComponent<InterruptProductionDirector>().HitFlashTimeBase,
            "섬광 진행기 = 실시간");
    }

    [Test]
    public void 월드_섬광_프리팹_파티클은_스케일_시간_원샷_계층_스케일()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorldVfxPath);
        Assert.IsNotNull(prefab, WorldVfxPath);
        var vfx = prefab.GetComponent<InterruptHitFlashVfx>();
        Assert.IsNotNull(vfx);
        Assert.AreEqual(InterruptEffectTimeBase.Scaled, vfx.TimeBase, "기본 스케일 — 성공 슬로우를 따라 느리게 터진다");

        ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
        Assert.GreaterOrEqual(systems.Length, 4, "중심·번짐·광선·링(+불티)");
        foreach (ParticleSystem system in systems)
        {
            ParticleSystem.MainModule main = system.main;
            Assert.IsFalse(main.useUnscaledTime, system.name + " 스케일 시간(재생 때 칸 값으로 다시 넣는다)");
            Assert.IsFalse(main.playOnAwake, system.name);
            Assert.IsFalse(main.loop, system.name);
            Assert.AreEqual(ParticleSystemScalingMode.Hierarchy, main.scalingMode, system.name);

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            Assert.IsNotNull(renderer.sharedMaterial, system.name);
            Assert.AreEqual(WorldShaderName, renderer.sharedMaterial.shader.name, system.name);
        }
    }

    private T Track<T>(T obj) where T : Object
    {
        _created.Add(obj);
        return obj;
    }
}
