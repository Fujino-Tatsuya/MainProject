using System.Collections.Generic;
using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>
/// 적중 섬광(할 일 132 §15) — ②~⑥ 시간표(섬광 순간 0.133s·끝 0.433s), 히트스톱 중(성공 Started 순간) 시작, 오너/팀원/실패 분기,
/// 오너 암전 걷힘 = 섬광 순간, 단계별 화면 효과 세기, Volume 스냅샷 복구·강제 리셋 즉시 복구, 덮어쓸 텍스처 칸 폴백, 디렉터 칸·프리팹·렌더러 연결.
/// </summary>
public sealed class InterruptHitFlashTests
{
    private const string OverlayShaderName = "BeaverLobby/UI/InterruptHitFlashOverlay";
    private const string WorldShaderName = "BeaverLobby/VFX/InterruptHitFlash";
    private const string DarkAdaptationShaderName = "Hidden/Rendering/DarkAdaptation";
    private const string LensFlareShaderName = "Hidden/Universal Render Pipeline/LensFlareDataDriven";
    private const string NetworkManagerPath = "Assets/2.Prefabs/Network/NetworkManager.prefab";
    private const string WorldVfxPath = "Assets/5.VFX/Interrupt/HitFlash/FX_InterruptHitFlash.prefab";
    private const string OverlayMaterialPath = "Assets/5.VFX/Interrupt/HitFlash/M_InterruptHitFlashOverlay.mat";
    private const string LensFlarePath = "Assets/5.VFX/Interrupt/HitFlash/LF_InterruptHitFlashStarburst.asset";
    private const string AberrationProfilePath = "Assets/5.VFX/Interrupt/HitFlash/VP_InterruptHitFlash_Aberration.asset";
    private const string BloomProfilePath = "Assets/5.VFX/Interrupt/HitFlash/VP_InterruptHitFlash_Bloom.asset";
    private static readonly string[] RendererPaths = { "Assets/99.Settings/PC_Renderer.asset", "Assets/99.Settings/Mobile_Renderer.asset" };

    // 이진 소수로 딱 떨어지는 길이 — 단계 경계 비교가 부동소수 오차로 흔들리지 않게(디렉터 기본값은 따로 확인).
    // ② 1/16 · ③ 1/32 · ④ 1/32 → 섬광 순간 1/8, ⑤ 1/8 · ⑥ 1/4 → 끝 1/2.
    private sealed class FakeSettings : IInterruptHitFlashSettings, IInterruptOwnerPresentationSettings
    {
        public float Starburst = 0.0625f;
        public float Whiteout = 0.03125f;
        public float Smear = 0.03125f;
        public float Aberration = 0.125f;
        public float Settle = 0.25f;
        public InterruptEffectTimeBase TimeBase = InterruptEffectTimeBase.Unscaled; // 디렉터 기본과 같게

        public float HitFlashStarburstDuration => Starburst;
        public float HitFlashWhiteoutDuration => Whiteout;
        public float HitFlashSmearDuration => Smear;
        public float HitFlashAberrationDuration => Aberration;
        public float HitFlashSettleDuration => Settle;
        public InterruptEffectTimeBase HitFlashTimeBase => TimeBase;

        public float OwnerDimInDuration => 0.125f;
        public float OwnerDimOutDuration => 0.25f;
    }

    private sealed class FakeScreenSettings : IInterruptHitFlashScreenSettings
    {
        public LensFlareDataSRP Flare;
        public VolumeProfile AberrationProfile;
        public VolumeProfile BloomProfile;

        public Material HitFlashOverlayMaterial => null;
        public LensFlareDataSRP HitFlashLensFlare => Flare;
        public float HitFlashFlareIntensity => 2f;
        public float HitFlashFlareScale => 1f;
        public float HitFlashBlurStrength => 0.5f;
        public VolumeProfile HitFlashAberrationProfile => AberrationProfile;
        public VolumeProfile HitFlashBloomProfile => BloomProfile;
    }

    private sealed class FakeOverlay : IInterruptHitFlashOverlayOutput
    {
        public InterruptHitFlashStage Stage;
        public float Progress;
        public Vector3 Center;
        public int Shows;
        public int Hides;

        public void Show(InterruptHitFlashStage stage, float progress, Vector3 worldCenter)
        {
            Stage = stage;
            Progress = progress;
            Center = worldCenter;
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
        DarkAdaptationState.ClearHitFlash();
    }

    private static InterruptHitFlashMoment Moment(bool viewerIsCaster, bool ownerOverlay) =>
        new InterruptHitFlashMoment(InterruptProductionEvent.LocalSuccess(7, 2, Target), viewerIsCaster, ownerOverlay);

    // ─── 시간표 ───

    [Test]
    public void 디렉터_기본_시간표_섬광_순간_0_133_끝_0_433()
    {
        var director = Track(new GameObject("Director")).AddComponent<InterruptProductionDirector>();
        var timeline = InterruptHitFlashTimeline.From(director);
        Assert.AreEqual(0f, timeline.StartOf(InterruptHitFlashStage.Starburst), 1e-5f, "② = 성공 Started 순간");
        Assert.AreEqual(0.067f, timeline.StartOf(InterruptHitFlashStage.Whiteout), 1e-5f, "③");
        Assert.AreEqual(0.1f, timeline.StartOf(InterruptHitFlashStage.Smear), 1e-5f, "④");
        Assert.AreEqual(0.133f, timeline.StartOf(InterruptHitFlashStage.Aberration), 1e-5f, "⑤ = 섬광 순간");
        Assert.AreEqual(0.233f, timeline.StartOf(InterruptHitFlashStage.Settle), 1e-5f, "⑥");
        Assert.AreEqual(0.133f, timeline.FlashMoment, 1e-5f, "섬광 순간 = ②+③+④");
        Assert.AreEqual(0.433f, timeline.Total, 1e-5f, "끝");
        Assert.AreEqual(timeline.Total, timeline.StartOf(InterruptHitFlashStage.None), 1e-5f, "None = 끝");
    }

    [Test]
    public void 시간에서_단계와_진행도()
    {
        var timeline = InterruptHitFlashTimeline.From(_settings);
        AssertStage(timeline, -0.01f, InterruptHitFlashStage.None, 0f);
        AssertStage(timeline, 0f, InterruptHitFlashStage.Starburst, 0f);
        AssertStage(timeline, 0.03125f, InterruptHitFlashStage.Starburst, 0.5f);
        AssertStage(timeline, 0.0625f, InterruptHitFlashStage.Whiteout, 0f);
        AssertStage(timeline, 0.109375f, InterruptHitFlashStage.Smear, 0.5f);
        AssertStage(timeline, 0.125f, InterruptHitFlashStage.Aberration, 0f);
        AssertStage(timeline, 0.25f, InterruptHitFlashStage.Settle, 0f);
        AssertStage(timeline, 0.375f, InterruptHitFlashStage.Settle, 0.5f);
        AssertStage(timeline, 0.5f, InterruptHitFlashStage.None, 0f);
    }

    [Test]
    public void 길이_0_단계는_건너뛰고_음수_NaN_은_0()
    {
        var timeline = new InterruptHitFlashTimeline(0.125f, 0f, -1f, float.NaN, 0.25f);
        Assert.AreEqual(0.125f, timeline.FlashMoment);
        Assert.AreEqual(0.375f, timeline.Total);
        AssertStage(timeline, 0.0625f, InterruptHitFlashStage.Starburst, 0.5f);
        AssertStage(timeline, 0.125f, InterruptHitFlashStage.Settle, 0f);
        AssertStage(timeline, 0.375f, InterruptHitFlashStage.None, 0f);
        Assert.AreEqual(InterruptHitFlashStage.None, timeline.StageAt(float.NaN, out _));
        Assert.AreEqual(0f, default(InterruptHitFlashTimeline).Total, "칸이 없으면 섬광 없음");
    }

    private static void AssertStage(InterruptHitFlashTimeline timeline, float elapsed, InterruptHitFlashStage stage, float progress)
    {
        Assert.AreEqual(stage, timeline.StageAt(elapsed, out float actual), $"경과 {elapsed}");
        Assert.AreEqual(progress, actual, 1e-4f, $"경과 {elapsed} 진행도");
    }

    // ─── 오너 화면 진행 ───

    [Test]
    public void 화면_섬광은_성공_Started_순간_스타버스트부터_히트스톱_길이와_무관()
    {
        _sequencer.BeginOwnerOverlay(Target, 20f);
        Assert.IsTrue(_sequencer.OverlayActive);
        Assert.AreEqual(InterruptHitFlashStage.Starburst, _overlay.Stage);
        Assert.AreEqual(0f, _overlay.Progress);
        Assert.AreEqual(Target, _overlay.Center);
    }

    [Test]
    public void 화면_섬광_단계가_진행기_시계로_넘어가고_끝나면_한_번_숨긴다()
    {
        _sequencer.BeginOwnerOverlay(Target, 20f);
        _sequencer.Tick(20.0625f);
        Assert.AreEqual(InterruptHitFlashStage.Whiteout, _overlay.Stage);
        _sequencer.Tick(20.109375f);
        Assert.AreEqual(InterruptHitFlashStage.Smear, _overlay.Stage);
        Assert.AreEqual(0.5f, _overlay.Progress, 1e-4f);
        _sequencer.Tick(20.125f);
        Assert.AreEqual(InterruptHitFlashStage.Aberration, _overlay.Stage);
        _sequencer.Tick(20.25f);
        Assert.AreEqual(InterruptHitFlashStage.Settle, _overlay.Stage);
        _sequencer.Tick(20.5f);
        Assert.IsFalse(_sequencer.OverlayActive);
        Assert.AreEqual(InterruptHitFlashStage.None, _sequencer.OverlayStage);
        Assert.AreEqual(1, _overlay.Hides);
        _sequencer.Tick(20.6f);
        Assert.AreEqual(1, _overlay.Hides, "숨긴 뒤엔 다시 부르지 않는다");
    }

    // ─── 섬광 순간 ───

    [Test]
    public void 섬광_순간은_스타버스트_화이트아웃_스미어_뒤에_한_번()
    {
        _sequencer.ScheduleFlash(Moment(false, false), 20f);
        _sequencer.Tick(20.109375f);
        Assert.IsEmpty(_flashes, "④ 끝 전");
        _sequencer.Tick(20.125f);
        Assert.AreEqual(1, _flashes.Count);
        Assert.AreEqual(Target, _flashes[0].Event.TargetPosition);
        _sequencer.Tick(20.5f);
        Assert.AreEqual(1, _flashes.Count);
        Assert.AreEqual(0, _sequencer.PendingFlashCount);
    }

    [Test]
    public void 섬광_순간_길이_0이면_바로()
    {
        _settings.Starburst = _settings.Whiteout = _settings.Smear = 0f;
        _sequencer.ScheduleFlash(Moment(false, false), 20f);
        Assert.AreEqual(1, _flashes.Count);
    }

    [Test]
    public void 여러_발동자_성공은_각각_섬광_순간()
    {
        _sequencer.ScheduleFlash(Moment(false, false), 20f);
        _sequencer.ScheduleFlash(Moment(true, true), 20.0625f);
        _sequencer.Tick(20.125f);
        Assert.AreEqual(1, _flashes.Count);
        _sequencer.Tick(20.1875f);
        Assert.AreEqual(2, _flashes.Count);
        Assert.IsTrue(_flashes[1].OwnerOverlay);
    }

    // ─── 강제 리셋 ───

    [Test]
    public void 강제_리셋은_화면_섬광_즉시_끄고_섬광_순간_취소()
    {
        _sequencer.BeginOwnerOverlay(Target, 20f);
        _sequencer.ScheduleFlash(Moment(true, true), 20f);
        _sequencer.Tick(20.07f);
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
    public void 오너_화면_섬광은_내_성공이고_암전이_켜졌을_때만()
    {
        Assert.IsTrue(InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.Success, true, true));
        Assert.IsFalse(InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.Success, true, false), "약 시도(대상 없음 포함)의 성공");
        Assert.IsFalse(InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.Success, false, true), "팀원 화면 — 월드 섬광·암순응만");
        Assert.IsFalse(InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.Attempt, true, true), "시도 슬로우");
        Assert.IsFalse(InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.AttemptWeak, true, true));
    }

    [Test]
    public void 섬광_순간_암순응_선택()
    {
        Assert.AreEqual(InterruptHitFlashDarkAdaptation.Others, InterruptHitFlashRules.SelectDarkAdaptation(false, false));
        Assert.AreEqual(InterruptHitFlashDarkAdaptation.Others, InterruptHitFlashRules.SelectDarkAdaptation(false, true));
        Assert.AreEqual(InterruptHitFlashDarkAdaptation.None, InterruptHitFlashRules.SelectDarkAdaptation(true, true),
            "본인 + 화면 섬광 = 마스크 암전·섬광이 대신");
        Assert.AreEqual(InterruptHitFlashDarkAdaptation.Self, InterruptHitFlashRules.SelectDarkAdaptation(true, false));
    }

    [Test]
    public void 팀원_화면은_화면_효과_없이_섬광_순간만()
    {
        // 디렉터와 같은 배선 — 팀원 화면은 BeginOwnerOverlay 를 부르지 않고 섬광 순간만 예약한다.
        bool ownerOverlay = InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.Success, false, false);
        if (ownerOverlay)
            _sequencer.BeginOwnerOverlay(Target, 0f);
        _sequencer.ScheduleFlash(Moment(false, ownerOverlay), 0f);

        for (float t = 0f; t <= 0.5f; t += 1f / 64f)
            _sequencer.Tick(t);

        Assert.AreEqual(0, _overlay.Shows, "오버레이·블러·색수차·Volume 없음");
        Assert.AreEqual(1, _flashes.Count, "월드 섬광·암순응은 섬광 순간에");
        Assert.AreEqual(InterruptHitFlashDarkAdaptation.Others,
            InterruptHitFlashRules.SelectDarkAdaptation(_flashes[0].ViewerIsCaster, _flashes[0].OwnerOverlay));
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

        owner.Tick(20.359375f);
        sequencer.Tick(20.359375f);
        Assert.AreEqual(1f, dim.Dim, "④ 스미어 동안 암전 유지");
        Assert.AreEqual(InterruptHitFlashStage.Smear, _overlay.Stage);
        Assert.IsEmpty(_flashes);

        owner.Tick(20.375f);
        sequencer.Tick(20.375f);
        Assert.AreEqual(0f, dim.Dim, "⑤ 시작 = 암전 즉시 끔");
        Assert.AreEqual(InterruptHitFlashStage.Aberration, _overlay.Stage);
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
        public float FirstProgressAfterHitStop; // 히트스톱 끝(실시간 0.05) ② 진행도
        public float[] StageStartReal;           // [0..4] = ②~⑥ 시작(실시간), [5] = 끝
        public float FlashReal;                  // 섬광 순간(실시간)
        public float DimOffReal;                 // 오너 암전이 0 이 된 때(실시간)
    }

    // 실시간 1/1000초 프레임으로 성공 슬로우 곡선을 돌린다 — 진행기는 칸의 시계(스케일 = 실시간 − 덜 흐른 시간), 오너 기계는 실시간.
    // 단계 길이 = 디렉터 기본값.
    private CurveRun RunUnderSuccessCurve(InterruptEffectTimeBase timeBase)
    {
        const double Dt = 0.001;
        _settings.Starburst = 0.067f;
        _settings.Whiteout = 0.033f;
        _settings.Smear = 0.033f;
        _settings.Aberration = 0.1f;
        _settings.Settle = 0.2f;
        _settings.TimeBase = timeBase;

        var timeline = new SlowMotionTimeline();
        timeline.Start(0.0, SuccessProfile());
        float ClockAt(double t) => timeBase == InterruptEffectTimeBase.Scaled ? (float)(t - timeline.LostTimeUntil(t)) : (float)t;

        var dim = new FakeDim();
        var owner = new InterruptOwnerPresentation(_settings, dim, dim);
        var run = new CurveRun { StageStartReal = new float[6], FlashReal = -1f, DimOffReal = -1f };
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

        InterruptHitFlashStage last = InterruptHitFlashStage.Starburst;
        for (int i = 1; i <= 5000; i++)
        {
            now = i * Dt;
            owner.Tick((float)now);
            sequencer.Tick(ClockAt(now));
            if (i == 50)
                run.FirstProgressAfterHitStop = sequencer.OverlayStage == InterruptHitFlashStage.Starburst ? sequencer.OverlayProgress : -1f;
            if (run.DimOffReal < 0f && dim.Dim <= 0f)
                run.DimOffReal = (float)now;

            InterruptHitFlashStage stage = sequencer.OverlayStage;
            if (stage != last)
            {
                int index = stage == InterruptHitFlashStage.None ? 5 : System.Array.IndexOf(InterruptHitFlashTimeline.Order, stage);
                run.StageStartReal[index] = (float)now;
                last = stage;
            }

            if (stage == InterruptHitFlashStage.None && run.FlashReal >= 0f)
                break;
        }

        return run;
    }

    [Test]
    public void 기본_실시간_시계면_히트스톱_슬로우와_무관하게_섬광_순간_0_133초_끝_0_433초()
    {
        var director = Track(new GameObject("Director")).AddComponent<InterruptProductionDirector>();
        CurveRun run = RunUnderSuccessCurve(director.HitFlashTimeBase);
        Assert.AreEqual(InterruptHitFlashStage.Whiteout, InterruptHitFlashTimeline.Order[1]);
        Assert.AreEqual(0.067f, run.StageStartReal[1], 0.0015f, "③ 시작");
        Assert.AreEqual(0.1f, run.StageStartReal[2], 0.0015f, "④ 시작");
        Assert.AreEqual(0.133f, run.StageStartReal[3], 0.0015f, "⑤ 시작 = 섬광 순간 실시간 0.133s");
        Assert.AreEqual(0.233f, run.StageStartReal[4], 0.0015f, "⑥ 시작");
        Assert.AreEqual(0.433f, run.StageStartReal[5], 0.0015f, "끝 실시간 0.433s");
        Assert.AreEqual(run.StageStartReal[3], run.FlashReal, 1e-6f, "섬광 순간(월드 섬광·암순응) = 진행기 ⑤ 시작 같은 프레임");
        Assert.AreEqual(run.FlashReal, run.DimOffReal, 1e-6f, "오너 암전 끔 = 섬광 순간 같은 프레임");
    }

    [Test]
    public void 칸을_스케일로_바꾸면_섬광_단계와_섬광_순간이_성공_슬로우를_따라_늘어난다()
    {
        CurveRun run = RunUnderSuccessCurve(InterruptEffectTimeBase.Scaled);

        // 히트스톱(0.05s×0.02) 동안 스케일 0.001 — ②(0.067)는 1.5% 만 나아간 채 거의 멈춰 보인다.
        Assert.AreEqual(0.001f / 0.067f, run.FirstProgressAfterHitStop, 0.003f);
        Assert.Greater(run.FlashReal, 0.133f, "섬광 순간이 실시간보다 늦다");
        for (int i = 1; i < run.StageStartReal.Length; i++)
            Assert.Greater(run.StageStartReal[i], run.StageStartReal[i - 1], $"단계 {i} 순서");
        Assert.AreEqual(run.StageStartReal[3], run.FlashReal, 1e-6f, "섬광 순간 = 진행기 ⑤ 시작 같은 프레임");
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
    public void 실패_대상_없음_약은_화면_섬광_조건이_서지_않는다()
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

    // ─── 단계별 화면 효과 세기 ───

    [Test]
    public void 단계별_화면_효과_세기()
    {
        InterruptHitFlashLook starburst = InterruptHitFlashRules.Look(InterruptHitFlashStage.Starburst, 0f);
        Assert.AreEqual(1f, starburst.Flare, "② 렌즈 플레어");
        Assert.AreEqual(0f, starburst.Blur + starburst.Aberration + starburst.Bloom, "② 배경은 암전 그대로");

        InterruptHitFlashLook whiteout = InterruptHitFlashRules.Look(InterruptHitFlashStage.Whiteout, 0.5f);
        Assert.AreEqual(1f, whiteout.Blur, "③ 방사형 블러 최대");
        Assert.AreEqual(0f, whiteout.Flare);

        InterruptHitFlashLook smear = InterruptHitFlashRules.Look(InterruptHitFlashStage.Smear, 0.5f);
        Assert.AreEqual(0f, smear.Flare + smear.Blur + smear.Aberration + smear.Bloom, "④ 그림 한 장만");

        InterruptHitFlashLook aberrationStart = InterruptHitFlashRules.Look(InterruptHitFlashStage.Aberration, 0f);
        InterruptHitFlashLook aberrationEnd = InterruptHitFlashRules.Look(InterruptHitFlashStage.Aberration, 1f);
        Assert.AreEqual(1f, aberrationStart.Aberration, "⑤ 색수차");
        Assert.Greater(aberrationStart.Blur, 0f, "⑤ 블러");
        Assert.Greater(aberrationEnd.Bloom, aberrationStart.Bloom, "⑤ 블룸 상승");

        InterruptHitFlashLook settleStart = InterruptHitFlashRules.Look(InterruptHitFlashStage.Settle, 0f);
        InterruptHitFlashLook settleEnd = InterruptHitFlashRules.Look(InterruptHitFlashStage.Settle, 1f);
        Assert.AreEqual(aberrationEnd.Blur, settleStart.Blur, 1e-5f, "⑤→⑥ 블러 이어짐");
        Assert.AreEqual(aberrationEnd.Bloom, settleStart.Bloom, 1e-5f, "⑤→⑥ 블룸 이어짐");
        Assert.AreEqual(0f, settleEnd.Blur + settleEnd.Aberration + settleEnd.Bloom, 1e-5f, "⑥ 끝 = 0");

        InterruptHitFlashLook none = InterruptHitFlashRules.Look(InterruptHitFlashStage.None, 0.5f);
        Assert.AreEqual(0f, none.Flare + none.Blur + none.Aberration + none.Bloom);
    }

    [Test]
    public void 화면_중심은_카메라_뒤나_NaN_이면_가운데()
    {
        Assert.AreEqual(new Vector2(0.25f, 0.75f), InterruptHitFlashRules.ViewportCenter(new Vector3(0.25f, 0.75f, 10f)));
        Assert.AreEqual(new Vector2(0.5f, 0.5f), InterruptHitFlashRules.ViewportCenter(new Vector3(0.25f, 0.75f, -1f)));
        Assert.AreEqual(new Vector2(0.5f, 0.5f), InterruptHitFlashRules.ViewportCenter(new Vector3(float.NaN, 0.75f, 10f)));
    }

    [Test]
    public void 렌더_모드_섬광_블러와_플레어_층은_값이_있을_때만()
    {
        Assert.AreEqual(DarkAdaptationMode.None, DarkAdaptationModes.Select(0f, 0f, false, 0f, false), "평소 패스 없음");
        Assert.AreEqual(DarkAdaptationMode.RadialBlur, DarkAdaptationModes.Select(0f, 0f, false, 0.2f, false));
        Assert.AreEqual(DarkAdaptationMode.FlareLayer | DarkAdaptationMode.Mask, DarkAdaptationModes.Select(0f, 1f, true, 0f, true));
    }

    // ─── Volume 스냅샷 ───

    [Test]
    public void Volume_스냅샷은_처음_값을_잡고_끝에_돌려준다()
    {
        var snapshot = new InterruptHitFlashVolumeSnapshot();
        Assert.IsFalse(snapshot.Release(out _, out _), "잡은 적 없으면 되돌릴 것 없음");

        Assert.IsTrue(snapshot.Capture(0.3f, 0.6f));
        Assert.IsFalse(snapshot.Capture(1f, 1f), "재생 중 다시 잡지 않는다(새 성공이 이어져도 처음 값)");
        Assert.IsTrue(snapshot.Release(out float aberration, out float bloom));
        Assert.AreEqual(0.3f, aberration);
        Assert.AreEqual(0.6f, bloom);
        Assert.IsFalse(snapshot.Captured);
        Assert.IsFalse(snapshot.Release(out _, out _), "한 번만");
    }

    [Test]
    public void 실제_출력_Volume_가중치_블러_플레어가_끝과_강제_리셋에_즉시_복구된다()
    {
        var flare = Track(ScriptableObject.CreateInstance<LensFlareDataSRP>());
        flare.elements = new LensFlareDataElementSRP[0];
        var settings = new FakeScreenSettings
        {
            Flare = flare,
            AberrationProfile = Track(ScriptableObject.CreateInstance<VolumeProfile>()),
            BloomProfile = Track(ScriptableObject.CreateInstance<VolumeProfile>()),
        };
        var output = new InterruptHitFlashOverlay(settings);
        try
        {
            var sequencer = new InterruptHitFlashSequencer(_settings, output, _flashes.Add);

            sequencer.BeginOwnerOverlay(Target, 0f);
            Assert.IsTrue(output.FlareActive, "② 플레어");
            Assert.IsNotNull(DarkAdaptationState.HitFlashFlareTarget, "② 플레어 층");
            Assert.AreEqual(0f, DarkAdaptationState.HitFlashBlurStrength);

            sequencer.Tick(0.07f);
            Assert.IsFalse(output.FlareActive, "③ 플레어 끔");
            Assert.IsNull(DarkAdaptationState.HitFlashFlareTarget);
            Assert.AreEqual(0.5f, DarkAdaptationState.HitFlashBlurStrength, 1e-5f, "③ 블러 = 칸 최대");

            sequencer.Tick(0.375f);
            Assert.AreEqual(InterruptHitFlashStage.Settle, sequencer.OverlayStage);
            Assert.AreEqual(0.5f, output.AberrationWeight, 1e-4f, "⑥ 절반 색수차");
            Assert.Greater(output.BloomWeight, 0f);
            Assert.Greater(DarkAdaptationState.HitFlashBlurStrength, 0f);

            // 강제 리셋 — 같은 호출 안에서 전부 원래대로.
            sequencer.ForcedReset();
            Assert.AreEqual(0f, output.AberrationWeight);
            Assert.AreEqual(0f, output.BloomWeight);
            Assert.AreEqual(0f, DarkAdaptationState.HitFlashBlurStrength);
            Assert.IsNull(DarkAdaptationState.HitFlashFlareTarget);
            Assert.IsFalse(output.FlareActive);

            // 끝까지 돌면 끝에 복구.
            sequencer.BeginOwnerOverlay(Target, 10f);
            sequencer.Tick(10.3f);
            Assert.Greater(output.AberrationWeight, 0f);
            sequencer.Tick(10.5f);
            Assert.AreEqual(0f, output.AberrationWeight, "끝 = 스냅샷 값");
            Assert.AreEqual(0f, output.BloomWeight);
            Assert.AreEqual(0f, DarkAdaptationState.HitFlashBlurStrength);
        }
        finally
        {
            output.Dispose();
        }
    }

    // ─── 덮어쓸 텍스처 칸 폴백 ───

    [Test]
    public void 오버레이_텍스처_칸_순번()
    {
        Assert.AreEqual(0, InterruptHitFlashRules.OverlayTextureIndex(InterruptHitFlashStage.Starburst));
        Assert.AreEqual(1, InterruptHitFlashRules.OverlayTextureIndex(InterruptHitFlashStage.Whiteout));
        Assert.AreEqual(2, InterruptHitFlashRules.OverlayTextureIndex(InterruptHitFlashStage.Smear));
        Assert.AreEqual(-1, InterruptHitFlashRules.OverlayTextureIndex(InterruptHitFlashStage.Aberration), "⑤⑥ = 실제 화면 효과");
        Assert.AreEqual(-1, InterruptHitFlashRules.OverlayTextureIndex(InterruptHitFlashStage.Settle));
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
        foreach (string property in InterruptHitFlashRules.OverlayTextureProperties)
            Assert.IsTrue(material.HasProperty(property), property);

        material.SetTexture("_WhiteoutTex", texture);
        Assert.IsTrue(InterruptHitFlashRules.ApplyOverlayKeywords(material));
        Assert.IsFalse(material.IsKeywordEnabled("_STARBURST_TEX_ON"));
        Assert.IsTrue(material.IsKeywordEnabled("_WHITEOUT_TEX_ON"));
        Assert.IsFalse(material.IsKeywordEnabled("_SMEAR_TEX_ON"));
        Assert.IsFalse(InterruptHitFlashRules.ApplyOverlayKeywords(material), "바뀐 것 없으면 false");

        material.SetTexture("_WhiteoutTex", null);
        Assert.IsTrue(InterruptHitFlashRules.ApplyOverlayKeywords(material));
        Assert.IsFalse(material.IsKeywordEnabled("_WHITEOUT_TEX_ON"), "비우면 다시 절차 생성");
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
        foreach (string name in new[] { OverlayShaderName, WorldShaderName, DarkAdaptationShaderName })
        {
            Shader shader = Shader.Find(name);
            Assert.IsNotNull(shader, name);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), name);
        }

        Assert.AreEqual(3, Shader.Find(DarkAdaptationShaderName).passCount, "휘도·마스크 덮개·방사형 블러");
    }

    // ─── 순서·디렉터·프리팹·렌더러 ───

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
        Assert.AreEqual(0.067f, so.FindProperty("hitFlashStarburstDuration").floatValue, 1e-6f);
        Assert.AreEqual(0.033f, so.FindProperty("hitFlashWhiteoutDuration").floatValue, 1e-6f);
        Assert.AreEqual(0.033f, so.FindProperty("hitFlashSmearDuration").floatValue, 1e-6f);
        Assert.AreEqual(0.1f, so.FindProperty("hitFlashAberrationDuration").floatValue, 1e-6f);
        Assert.AreEqual(0.2f, so.FindProperty("hitFlashSettleDuration").floatValue, 1e-6f);
        foreach (string removed in new[] { "hitFlashBurstDuration", "hitFlashGlareDuration", "hitFlashInvertDuration", "hitFlashReturnDuration", "hitFlashSpeedLines" })
            Assert.IsNull(so.FindProperty(removed), removed + " — 옛 1~4단계 칸은 없앤다");
        Assert.AreEqual(1f, so.FindProperty("hitFlashWorldScale").floatValue, 1e-6f);
        Assert.AreEqual(1f, so.FindProperty("hitFlashHeightOffset").floatValue, 1e-6f);
        Assert.AreEqual(InterruptEffectTimeBase.Unscaled, director.HitFlashTimeBase, "진행기 기본 = 실시간 시계");
    }

    [Test]
    public void NetworkManager_디렉터에_섬광_에셋_연결()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPath);
        Assert.IsNotNull(prefab, NetworkManagerPath);
        var director = prefab.GetComponent<InterruptProductionDirector>();

        Assert.IsNotNull(director.HitFlashOverlayMaterial, "hitFlashOverlayMaterial");
        Assert.AreEqual(OverlayMaterialPath, AssetDatabase.GetAssetPath(director.HitFlashOverlayMaterial));
        Assert.AreEqual(OverlayShaderName, director.HitFlashOverlayMaterial.shader.name);

        var so = new SerializedObject(director);
        var vfx = so.FindProperty("hitFlashWorldVfx").objectReferenceValue as InterruptHitFlashVfx;
        Assert.IsNotNull(vfx, "hitFlashWorldVfx");
        Assert.AreEqual(WorldVfxPath, AssetDatabase.GetAssetPath(vfx));

        Assert.AreEqual(LensFlarePath, AssetDatabase.GetAssetPath(director.HitFlashLensFlare), "② 렌즈 플레어");
        Assert.AreEqual(AberrationProfilePath, AssetDatabase.GetAssetPath(director.HitFlashAberrationProfile), "색수차 프로필");
        Assert.AreEqual(BloomProfilePath, AssetDatabase.GetAssetPath(director.HitFlashBloomProfile), "블룸 프로필");
        Assert.AreEqual(InterruptEffectTimeBase.Unscaled, director.HitFlashTimeBase, "섬광 진행기 = 실시간");
        Assert.AreEqual(0.133f, InterruptHitFlashTimeline.From(director).FlashMoment, 1e-5f, "프리팹 섬광 순간");
        Assert.AreEqual(0.433f, InterruptHitFlashTimeline.From(director).Total, 1e-5f, "프리팹 끝");
    }

    [Test]
    public void 섬광_에셋_내용()
    {
        var flare = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(LensFlarePath);
        Assert.IsNotNull(flare, LensFlarePath);
        Assert.Greater(flare.elements.Length, 2, "가는 빛살 여러 개");
        float longest = 0f;
        LensFlareDataElementSRP horizontal = null;
        foreach (LensFlareDataElementSRP element in flare.elements)
        {
            Assert.IsNull(element.lensFlareTexture, "텍스처 없이 절차 모양");
            float length = element.sizeXY.x * element.uniformScale;
            if (length > longest)
            {
                longest = length;
                horizontal = element;
            }
        }

        Assert.IsNotNull(horizontal);
        Assert.AreEqual(0f, Mathf.Repeat(horizontal.rotation, 180f), 1e-3f, "가장 긴 빛살 = 가로(아나모픽)");

        var aberration = AssetDatabase.LoadAssetAtPath<VolumeProfile>(AberrationProfilePath);
        Assert.IsTrue(aberration.TryGet(out ChromaticAberration ca) && ca.intensity.overrideState && ca.intensity.value > 0f, "색수차 오버라이드");
        var bloomProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(BloomProfilePath);
        Assert.IsTrue(bloomProfile.TryGet(out Bloom bloom) && bloom.intensity.overrideState && bloom.intensity.value > 0f, "블룸 오버라이드");
    }

    [Test]
    public void 렌더러_둘_다_플레어_층_셰이더_연결()
    {
        foreach (string path in RendererPaths)
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            Assert.IsNotNull(data, path);
            DarkAdaptationFeature feature = null;
            foreach (ScriptableRendererFeature f in data.rendererFeatures)
            {
                if (f is DarkAdaptationFeature found)
                    feature = found;
            }

            Assert.IsNotNull(feature, path + " DarkAdaptationFeature");
            var shader = new SerializedObject(feature).FindProperty("_lensFlareShader").objectReferenceValue as Shader;
            Assert.IsNotNull(shader, path + " _lensFlareShader");
            Assert.AreEqual(LensFlareShaderName, shader.name, path);
        }
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
