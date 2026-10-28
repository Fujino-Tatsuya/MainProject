using System.Collections.Generic;
using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>
/// 적중 섬광(할 일 132 §15·§17) — ②~⑥ 시간표(②③④ 2·2·2 렌더 프레임 → 섬광 순간, ⑤⑥ 0.1·0.2초), 히트스톱 중(성공 Started 순간) 시작, 오너/팀원/실패 분기,
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

    // ②③④ = 렌더 프레임 수(디렉터 기본 2·2·2 → 섬광 순간 6 프레임, §17). ⑤⑥ = 이진 소수로 딱 떨어지는 초 —
    // 단계 경계 비교가 부동소수 오차로 흔들리지 않게(디렉터 기본값은 따로 확인). ⑤ 1/8 · ⑥ 1/4 → 섬광 순간 뒤 3/8.
    private sealed class FakeSettings : IInterruptHitFlashSettings, IInterruptOwnerPresentationSettings
    {
        public int Starburst = 2;
        public int Whiteout = 2;
        public int Smear = 2;
        public float Aberration = 0.125f;
        public float Settle = 0.25f;
        public InterruptEffectTimeBase TimeBase = InterruptEffectTimeBase.Unscaled; // 디렉터 기본과 같게

        public int HitFlashStarburstFrames => Starburst;
        public int HitFlashWhiteoutFrames => Whiteout;
        public int HitFlashSmearFrames => Smear;
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
        public readonly HashSet<InterruptPresentationEffect> Off = new HashSet<InterruptPresentationEffect>();

        public bool IsEffectOn(InterruptPresentationEffect effect) => !Off.Contains(effect);

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
        public Vector3 Direction;
        public int Shows;
        public int Hides;

        public void Show(InterruptHitFlashStage stage, float progress, Vector3 worldCenter, Vector3 worldDirection)
        {
            Stage = stage;
            Progress = progress;
            Center = worldCenter;
            Direction = worldDirection;
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
    private static readonly Vector3 Attack = new Vector3(0f, 0f, 1f);

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
    public void 디렉터_기본_시간표_2_2_2_프레임_뒤_섬광_순간_그_뒤_0_3초()
    {
        var director = Track(new GameObject("Director")).AddComponent<InterruptProductionDirector>();
        var timeline = InterruptHitFlashTimeline.From(director);
        Assert.AreEqual(0, timeline.StartFrameOf(InterruptHitFlashStage.Starburst), "② = 성공 Started 프레임");
        Assert.AreEqual(2, timeline.StartFrameOf(InterruptHitFlashStage.Whiteout), "③");
        Assert.AreEqual(4, timeline.StartFrameOf(InterruptHitFlashStage.Smear), "④");
        Assert.AreEqual(6, timeline.FlashMomentFrames, "섬광 순간 = ②+③+④ 끝 프레임");
        Assert.AreEqual(6, timeline.StartFrameOf(InterruptHitFlashStage.Aberration), "⑤ = 섬광 순간");
        Assert.AreEqual(0.1f, timeline.StartAfterFlashOf(InterruptHitFlashStage.Settle), 1e-5f, "⑥ = 섬광 순간 + 0.1초");
        Assert.AreEqual(0.3f, timeline.AfterFlashDuration, 1e-5f, "끝 = 섬광 순간 + 0.3초");
        Assert.AreEqual(timeline.AfterFlashDuration, timeline.StartAfterFlashOf(InterruptHitFlashStage.None), 1e-5f, "None = 끝");
    }

    [Test]
    public void 프레임과_초에서_단계와_진행도()
    {
        var timeline = InterruptHitFlashTimeline.From(_settings);
        AssertStage(timeline, -1, 0f, InterruptHitFlashStage.None, 0f);
        AssertStage(timeline, 0, -1f, InterruptHitFlashStage.Starburst, 0f);
        AssertStage(timeline, 1, -1f, InterruptHitFlashStage.Starburst, 0.5f);
        AssertStage(timeline, 2, -1f, InterruptHitFlashStage.Whiteout, 0f);
        AssertStage(timeline, 5, -1f, InterruptHitFlashStage.Smear, 0.5f);
        AssertStage(timeline, 5, 99f, InterruptHitFlashStage.Smear, 0.5f, "섬광 순간 전엔 초를 보지 않는다");
        AssertStage(timeline, 6, 0f, InterruptHitFlashStage.Aberration, 0f);
        AssertStage(timeline, 7, 0.0625f, InterruptHitFlashStage.Aberration, 0.5f);
        AssertStage(timeline, 9, 0.125f, InterruptHitFlashStage.Settle, 0f);
        AssertStage(timeline, 30, 0.25f, InterruptHitFlashStage.Settle, 0.5f, "⑤⑥ 은 프레임과 무관하게 초");
        AssertStage(timeline, 31, 0.375f, InterruptHitFlashStage.None, 0f);
        AssertStage(timeline, 6, -1f, InterruptHitFlashStage.None, 0f, "섬광 순간 시각을 모르면 없음");
    }

    [Test]
    public void 길이_0_단계는_건너뛰고_음수_NaN_은_0()
    {
        var timeline = new InterruptHitFlashTimeline(2, 0, -1, float.NaN, 0.25f);
        Assert.AreEqual(2, timeline.FlashMomentFrames);
        Assert.AreEqual(0.25f, timeline.AfterFlashDuration);
        AssertStage(timeline, 1, -1f, InterruptHitFlashStage.Starburst, 0.5f);
        AssertStage(timeline, 2, 0f, InterruptHitFlashStage.Settle, 0f);
        AssertStage(timeline, 3, 0.25f, InterruptHitFlashStage.None, 0f);
        Assert.AreEqual(InterruptHitFlashStage.None, timeline.StageAt(2, float.NaN, out _));
        Assert.AreEqual(0, default(InterruptHitFlashTimeline).FlashMomentFrames, "칸이 없으면 섬광 없음");
        Assert.AreEqual(0f, default(InterruptHitFlashTimeline).AfterFlashDuration);
    }

    private static void AssertStage(InterruptHitFlashTimeline timeline, int frames, float seconds, InterruptHitFlashStage stage,
        float progress, string message = "")
    {
        Assert.AreEqual(stage, timeline.StageAt(frames, seconds, out float actual), $"프레임 {frames} 초 {seconds} {message}");
        Assert.AreEqual(progress, actual, 1e-4f, $"프레임 {frames} 초 {seconds} 진행도");
    }

    // ─── 오너 화면 진행 ───

    [Test]
    public void 화면_섬광은_성공_Started_순간_스타버스트부터_히트스톱_길이와_무관()
    {
        _sequencer.BeginOwnerOverlay(Target, Attack, 20f, 100);
        Assert.IsTrue(_sequencer.OverlayActive);
        Assert.AreEqual(InterruptHitFlashStage.Starburst, _overlay.Stage);
        Assert.AreEqual(0f, _overlay.Progress);
        Assert.AreEqual(Target, _overlay.Center);
        Assert.AreEqual(Attack, _overlay.Direction, "공격 방향을 출력에 넘긴다(④ 스미어)");
        _sequencer.Tick(20.1f, 104);
        Assert.AreEqual(InterruptHitFlashStage.Smear, _overlay.Stage);
        Assert.AreEqual(Attack, _overlay.Direction, "단계가 바뀌어도 같은 방향");
    }

    [Test]
    public void 화면_섬광_단계가_프레임으로_넘어가고_섬광_순간부터_초_끝나면_한_번_숨긴다()
    {
        _sequencer.BeginOwnerOverlay(Target, Attack, 20f, 100);
        _sequencer.Tick(20f, 101);
        Assert.AreEqual(InterruptHitFlashStage.Starburst, _overlay.Stage, "시간이 멈춰도(히트스톱) 프레임으로 넘어간다");
        _sequencer.Tick(20f, 102);
        Assert.AreEqual(InterruptHitFlashStage.Whiteout, _overlay.Stage);
        _sequencer.Tick(20.01f, 105);
        Assert.AreEqual(InterruptHitFlashStage.Smear, _overlay.Stage);
        Assert.AreEqual(0.5f, _overlay.Progress, 1e-4f);
        _sequencer.Tick(20.5f, 106);
        Assert.AreEqual(InterruptHitFlashStage.Aberration, _overlay.Stage, "섬광 순간 프레임 = ⑤ 시작(진행 0)");
        Assert.AreEqual(0f, _overlay.Progress);
        _sequencer.Tick(20.625f, 107);
        Assert.AreEqual(InterruptHitFlashStage.Settle, _overlay.Stage, "⑤ 0.125초 뒤 ⑥");
        _sequencer.Tick(20.875f, 108);
        Assert.IsFalse(_sequencer.OverlayActive);
        Assert.AreEqual(InterruptHitFlashStage.None, _sequencer.OverlayStage);
        Assert.AreEqual(1, _overlay.Hides);
        _sequencer.Tick(21f, 109);
        Assert.AreEqual(1, _overlay.Hides, "숨긴 뒤엔 다시 부르지 않는다");
    }

    // ─── 섬광 순간 ───

    [Test]
    public void 섬광_순간은_스타버스트_화이트아웃_스미어_프레임_뒤에_한_번()
    {
        _sequencer.ScheduleFlash(Moment(false, false), 100);
        _sequencer.Tick(20f, 105);
        Assert.IsEmpty(_flashes, "④ 끝 전");
        _sequencer.Tick(20f, 106);
        Assert.AreEqual(1, _flashes.Count, "2+2+2 프레임 뒤 — 시간과 무관");
        Assert.AreEqual(Target, _flashes[0].Event.TargetPosition);
        _sequencer.Tick(21f, 120);
        Assert.AreEqual(1, _flashes.Count);
        Assert.AreEqual(0, _sequencer.PendingFlashCount);
    }

    [Test]
    public void 섬광_순간_길이_0이면_바로()
    {
        _settings.Starburst = _settings.Whiteout = _settings.Smear = 0;
        _sequencer.ScheduleFlash(Moment(false, false), 100);
        Assert.AreEqual(1, _flashes.Count);
    }

    [Test]
    public void 여러_발동자_성공은_각각_섬광_순간()
    {
        _sequencer.ScheduleFlash(Moment(false, false), 100);
        _sequencer.ScheduleFlash(Moment(true, true), 102);
        _sequencer.Tick(20f, 106);
        Assert.AreEqual(1, _flashes.Count);
        _sequencer.Tick(20f, 108);
        Assert.AreEqual(2, _flashes.Count);
        Assert.IsTrue(_flashes[1].OwnerOverlay);
    }

    // ─── 강제 리셋 ───

    [Test]
    public void 강제_리셋은_화면_섬광_즉시_끄고_섬광_순간_취소()
    {
        _sequencer.BeginOwnerOverlay(Target, Attack, 20f, 100);
        _sequencer.ScheduleFlash(Moment(true, true), 100);
        _sequencer.Tick(20.07f, 103);
        _sequencer.ForcedReset();
        Assert.IsFalse(_sequencer.OverlayActive);
        Assert.AreEqual(1, _overlay.Hides);
        Assert.AreEqual(InterruptHitFlashStage.None, _overlay.Stage);
        _sequencer.Tick(20.5f, 130);
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
            _sequencer.BeginOwnerOverlay(Target, Attack, 0f, 0);
        _sequencer.ScheduleFlash(Moment(false, ownerOverlay), 0);

        for (int frame = 0; frame <= 32; frame++)
            _sequencer.Tick(frame / 64f, frame);

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

        sequencer.BeginOwnerOverlay(Target, Attack, 20.25f, 100);
        sequencer.ScheduleFlash(Moment(true, true), 100);

        owner.Tick(20.33f);
        sequencer.Tick(20.33f, 105);
        Assert.AreEqual(1f, dim.Dim, "④ 스미어 동안 암전 유지");
        Assert.AreEqual(InterruptHitFlashStage.Smear, _overlay.Stage);
        Assert.IsEmpty(_flashes);

        owner.Tick(20.35f);
        sequencer.Tick(20.35f, 106);
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
        public int[] StageStartFrame;            // [0..4] = ②~⑥ 시작 프레임, [5] = 끝
        public float[] StageStartReal;           // 같은 순간의 실시간
        public int FlashFrame;                   // 섬광 순간 프레임
        public int DimOffFrame;                  // 오너 암전이 0 이 된 프레임
    }

    // 실시간 1/60초 렌더 프레임으로 성공 슬로우 곡선을 돌린다 — ②③④ 는 프레임, ⑤⑥ 은 칸의 시계(스케일 = 실시간 − 덜 흐른 시간), 오너 기계는 실시간.
    // 단계 길이 = 디렉터 기본값(2·2·2 프레임, 0.1·0.2초).
    private const double FrameDt = 1.0 / 60.0;

    private CurveRun RunUnderSuccessCurve(InterruptEffectTimeBase timeBase)
    {
        _settings.Starburst = 2;
        _settings.Whiteout = 2;
        _settings.Smear = 2;
        _settings.Aberration = 0.1f;
        _settings.Settle = 0.2f;
        _settings.TimeBase = timeBase;

        var timeline = new SlowMotionTimeline();
        timeline.Start(0.0, SuccessProfile());
        float ClockAt(double t) => timeBase == InterruptEffectTimeBase.Scaled ? (float)(t - timeline.LostTimeUntil(t)) : (float)t;

        var dim = new FakeDim();
        var owner = new InterruptOwnerPresentation(_settings, dim, dim);
        var run = new CurveRun { StageStartFrame = new int[6], StageStartReal = new float[6], FlashFrame = -1, DimOffFrame = -1 };
        int frame = 0;
        double now = 0.0;
        var sequencer = new InterruptHitFlashSequencer(_settings, _overlay, m =>
        {
            run.FlashFrame = frame;
            if (m.ViewerIsCaster)
                owner.SuccessFlashMoment(m.Event.TriggerId, (float)now);
        });

        // 강 시도 → 암전 다 들어온 뒤 성공(프레임 0 = 실시간 0). 시도 중 암전은 0 → 1 로 이미 올라 있다.
        owner.Predict(InterruptAttemptForecast.Strong, null, -1f);
        owner.Tick(-0.5f);
        owner.ServerStarted(7, SlowMotionPurpose.Success, true, 0f);
        Assert.AreEqual(timeBase, sequencer.Clock);
        sequencer.BeginOwnerOverlay(Target, Attack, ClockAt(0.0), 0);
        sequencer.ScheduleFlash(Moment(true, true), 0);

        InterruptHitFlashStage last = InterruptHitFlashStage.Starburst;
        for (frame = 1; frame <= 600; frame++)
        {
            now = frame * FrameDt;
            owner.Tick((float)now);
            sequencer.Tick(ClockAt(now), frame);
            if (run.DimOffFrame < 0 && dim.Dim <= 0f)
                run.DimOffFrame = frame;

            InterruptHitFlashStage stage = sequencer.OverlayStage;
            if (stage != last)
            {
                int index = stage == InterruptHitFlashStage.None ? 5 : System.Array.IndexOf(InterruptHitFlashTimeline.Order, stage);
                run.StageStartFrame[index] = frame;
                run.StageStartReal[index] = (float)now;
                last = stage;
            }

            if (stage == InterruptHitFlashStage.None && run.FlashFrame >= 0)
                break;
        }

        return run;
    }

    [Test]
    public void 기본_실시간_시계면_2_2_2_프레임_뒤_섬광_순간_그_뒤_실시간_0_3초()
    {
        var director = Track(new GameObject("Director")).AddComponent<InterruptProductionDirector>();
        CurveRun run = RunUnderSuccessCurve(director.HitFlashTimeBase);
        Assert.AreEqual(InterruptHitFlashStage.Whiteout, InterruptHitFlashTimeline.Order[1]);
        Assert.AreEqual(2, run.StageStartFrame[1], "③ 시작 프레임 — 히트스톱(시간 정지)과 무관");
        Assert.AreEqual(4, run.StageStartFrame[2], "④ 시작 프레임");
        Assert.AreEqual(6, run.StageStartFrame[3], "⑤ 시작 = 섬광 순간 프레임");
        Assert.AreEqual(6, run.FlashFrame, "섬광 순간(월드 섬광·라이트·암순응) = ⑤ 시작 같은 프레임");
        Assert.AreEqual(6, run.DimOffFrame, "오너 암전 끔 = 섬광 순간 같은 프레임");
        float flashReal = run.StageStartReal[3];
        Assert.AreEqual(0.1f, run.StageStartReal[4] - flashReal, FrameDt + 1e-4, "⑥ 시작 = 섬광 순간 + 실시간 0.1초");
        Assert.AreEqual(0.3f, run.StageStartReal[5] - flashReal, FrameDt + 1e-4, "끝 = 섬광 순간 + 실시간 0.3초");
    }

    [Test]
    public void 칸을_스케일로_바꾸면_색수차_가라앉음만_성공_슬로우를_따라_늘어나고_섬광_순간_프레임은_그대로()
    {
        CurveRun run = RunUnderSuccessCurve(InterruptEffectTimeBase.Scaled);

        Assert.AreEqual(2, run.StageStartFrame[1], "②③④ 는 프레임 — 시계 칸과 무관");
        Assert.AreEqual(4, run.StageStartFrame[2]);
        Assert.AreEqual(6, run.FlashFrame);
        Assert.AreEqual(6, run.DimOffFrame, "오너 암전 끔 = 섬광 순간 같은 프레임");
        Assert.Greater(run.StageStartReal[5] - run.StageStartReal[3], 0.3f + 0.05f, "⑤⑥ 이 성공 슬로우(0.25)를 따라 늘어난다");
        for (int i = 1; i < run.StageStartReal.Length; i++)
            Assert.Greater(run.StageStartFrame[i], run.StageStartFrame[i - 1], $"단계 {i} 순서");
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

        _sequencer.ScheduleFlash(Moment(false, false), 0);
        _settings.TimeBase = InterruptEffectTimeBase.Unscaled;
        Assert.AreEqual(InterruptEffectTimeBase.Scaled, _sequencer.Clock, "진행 중 칸을 바꾸면 다음 성공부터");

        _sequencer.Tick(1f, 10);
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
        Assert.AreEqual(1f, aberrationStart.Bloom, "섬광 순간(⑤ 시작)부터 블룸 가중치 최대 — 코어가 번진다(§16)");
        Assert.AreEqual(1f, aberrationEnd.Bloom, "⑤ 동안 유지");

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

            sequencer.BeginOwnerOverlay(Target, Attack, 0f, 0);
            Assert.IsTrue(output.FlareActive, "② 플레어");
            Assert.IsNotNull(DarkAdaptationState.HitFlashFlareTarget, "② 플레어 층");
            Assert.AreEqual(0f, DarkAdaptationState.HitFlashBlurStrength);

            sequencer.Tick(0.03f, 2);
            Assert.IsFalse(output.FlareActive, "③ 플레어 끔");
            Assert.IsNull(DarkAdaptationState.HitFlashFlareTarget);
            Assert.AreEqual(0.5f, DarkAdaptationState.HitFlashBlurStrength, 1e-5f, "③ 블러 = 칸 최대");

            sequencer.Tick(0.1f, 6); // 섬광 순간(⑤ 시작) = 시각 0.1
            sequencer.Tick(0.35f, 7);
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
            sequencer.BeginOwnerOverlay(Target, Vector3.zero, 10f, 20);
            sequencer.Tick(10f, 26);
            sequencer.Tick(10.3f, 27);
            Assert.Greater(output.AberrationWeight, 0f);
            sequencer.Tick(10.5f, 28);
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
    public void 오버레이_재질_칸_16_수정()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(OverlayMaterialPath);
        Assert.IsNotNull(material, OverlayMaterialPath);
        Assert.AreEqual(0.75f, material.GetFloat("_WhiteoutOpacity"), 1e-4f, "③ 화이트아웃 불투명도 ~0.75");
        Assert.Greater(material.GetFloat("_WhiteoutSilhouette"), 0f, "③ 오너·대상 실루엣이 비친다");
        Assert.Greater(material.GetFloat("_SmearGrow"), 0f, "④ 실루엣을 키운다");
        Assert.IsFalse(material.HasProperty("_RainbowIntensity"), "⑤⑥ 무지개 고리 칸은 없앴다(Volume 색수차만)");
        Assert.IsTrue(material.HasProperty("_SmearAxis"), "④ 공격 방향(런타임)");
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

    // ─── §16 스미어 방향·오버레이 단계 ───

    [Test]
    public void 스미어_방향은_공격_방향을_화면에_투영한다()
    {
        const float Aspect = 16f / 9f;
        var center = new Vector3(0.5f, 0.5f, 10f);

        Assert.IsTrue(InterruptHitFlashRules.TryScreenDirection(center, new Vector3(0.6f, 0.5f, 10f), Aspect, out Vector2 right));
        Assert.AreEqual(1f, right.x, 1e-5f, "화면 오른쪽");
        Assert.AreEqual(0f, right.y, 1e-5f);

        // 뷰포트 x 0.1 = 화면 높이 단위 0.1 × 화면비 — 같은 높이 단위만큼 위로 가면 45°.
        Assert.IsTrue(InterruptHitFlashRules.TryScreenDirection(center, new Vector3(0.6f, 0.5f + 0.1f * Aspect, 10f), Aspect, out Vector2 diagonal));
        Assert.AreEqual(diagonal.x, diagonal.y, 1e-5f, "화면비 보정");
        Assert.AreEqual(1f, diagonal.magnitude, 1e-5f, "길이 1");

        Assert.IsTrue(InterruptHitFlashRules.TryScreenDirection(center, new Vector3(0.5f, 0.3f, 10f), Aspect, out Vector2 down));
        Assert.AreEqual(-1f, down.y, 1e-5f, "화면 아래");

        Assert.IsFalse(InterruptHitFlashRules.TryScreenDirection(center, new Vector3(0.6f, 0.5f, -1f), Aspect, out _), "앞 점이 카메라 뒤");
        Assert.IsFalse(InterruptHitFlashRules.TryScreenDirection(new Vector3(0.5f, 0.5f, -1f), center, Aspect, out _), "적중 지점이 카메라 뒤");
        Assert.IsFalse(InterruptHitFlashRules.TryScreenDirection(center, center, Aspect, out _), "카메라 축과 나란한 방향 = 화면 길이 0 → 재질 기본 방향");
        Assert.IsFalse(InterruptHitFlashRules.TryScreenDirection(center, new Vector3(float.NaN, 0.5f, 10f), Aspect, out _));
    }

    [Test]
    public void 스미어_방향_투영_탑다운_카메라()
    {
        // 탑다운(아래로 60°) 카메라 — 월드 +x 공격 = 화면 오른쪽, 월드 +z 공격 = 화면 위쪽.
        var cameraObject = Track(new GameObject("Camera"));
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(new Vector3(0f, 10f, -6f), Quaternion.Euler(60f, 0f, 0f));
        camera.aspect = 16f / 9f;
        Vector3 hit = new Vector3(0f, 1f, 0f);

        Vector3 c = camera.WorldToViewportPoint(hit);
        Assert.IsTrue(InterruptHitFlashRules.TryScreenDirection(c, camera.WorldToViewportPoint(hit + Vector3.right), camera.aspect, out Vector2 x));
        Assert.AreEqual(1f, x.x, 1e-3f);
        Assert.IsTrue(InterruptHitFlashRules.TryScreenDirection(c, camera.WorldToViewportPoint(hit + Vector3.forward), camera.aspect, out Vector2 z));
        Assert.AreEqual(0f, z.x, 1e-3f);
        Assert.Greater(z.y, 0.99f, "앞(+z) = 화면 위");
    }

    [Test]
    public void 오버레이_캔버스는_스타버스트_화이트아웃_스미어만_그린다()
    {
        Assert.IsTrue(InterruptHitFlashRules.OverlayDraws(InterruptHitFlashStage.Starburst));
        Assert.IsTrue(InterruptHitFlashRules.OverlayDraws(InterruptHitFlashStage.Whiteout));
        Assert.IsTrue(InterruptHitFlashRules.OverlayDraws(InterruptHitFlashStage.Smear));
        Assert.IsFalse(InterruptHitFlashRules.OverlayDraws(InterruptHitFlashStage.Aberration), "⑤ 무지개 원 없음 — Volume 색수차만");
        Assert.IsFalse(InterruptHitFlashRules.OverlayDraws(InterruptHitFlashStage.Settle), "⑥ 무지개 고리 없음");
        Assert.IsFalse(InterruptHitFlashRules.OverlayDraws(InterruptHitFlashStage.None));
    }

    [Test]
    public void 오너_상태가_시도의_공격_방향을_든다()
    {
        var dim = new FakeDim();
        var owner = new InterruptOwnerPresentation(_settings, dim, dim);
        Assert.AreEqual(Vector3.zero, owner.AttackDirection, "처음엔 모름");

        owner.Predict(InterruptAttemptForecast.Strong, null, 0f, Vector3.right);
        Assert.AreEqual(Vector3.right, owner.AttackDirection, "근접 = 시전 방향 / 대쉬 = 대시 방향(입력 순간 예측이 받은 값)");
        Assert.IsFalse(owner.Predict(InterruptAttemptForecast.Strong, null, 0.05f, Vector3.forward), "결론 전 중복 입력");
        Assert.AreEqual(Vector3.right, owner.AttackDirection, "무시한 입력은 방향을 바꾸지 않는다");

        owner.ServerStarted(9, SlowMotionPurpose.Success, true, 0.2f);
        Assert.IsTrue(owner.SuccessDimmed);
        Assert.AreEqual(Vector3.right, owner.AttackDirection, "성공 사건 순간에도 그 시도의 방향");
    }

    // ─── §16 월드 섬광 실시간 상한·라이트 ───

    // 실시간 1/60초 프레임으로 시계를 돌려 끝난 실시간을 잰다. timeScaleAt = 실시간 → 그 프레임 Time.timeScale.
    private static float RunClock(float effectLength, float cap, System.Func<float, float> timeScaleAt, out InterruptHitFlashVfxClock clock)
    {
        const float Dt = 1f / 60f;
        clock = default;
        clock.Begin(effectLength, cap);
        float real = 0f;
        for (int i = 0; i < 600 && clock.Running; i++)
        {
            float scale = timeScaleAt(real);
            clock.Advance(Dt * scale, Dt);
            real += Dt;
        }

        return real;
    }

    [Test]
    public void 월드_섬광은_스케일_시간이어도_실시간_상한_안에_끝난다()
    {
        // 성공 슬로우 0.25 — 상한 없으면 0.45 / 0.25 = 1.8초 남는다.
        float real = RunClock(0.45f, 0.3f, _ => 0.25f, out InterruptHitFlashVfxClock slow);
        Assert.LessOrEqual(real, 0.3f + 1f / 60f + 1e-4f, "실시간 약 0.3s 상한");
        Assert.GreaterOrEqual(slow.EffectElapsed, 0.45f - 1e-4f, "효과는 끝까지 돈다(빨리 감기)");
        Assert.IsFalse(slow.CutByCap);

        // 히트스톱(0.02) 0.05s 뒤 슬로우 0.25 — 시간 배율이 바뀌어도 남은 몫을 다시 나눈다.
        real = RunClock(0.45f, 0.3f, t => t < 0.05f ? 0.02f : 0.25f, out InterruptHitFlashVfxClock hitStop);
        Assert.LessOrEqual(real, 0.3f + 1f / 60f + 1e-4f);
        Assert.GreaterOrEqual(hitStop.EffectElapsed, 0.45f - 1e-4f);
    }

    [Test]
    public void 월드_섬광_슬로우가_없으면_원래_빠르기()
    {
        float speed = 0f;
        var clock = default(InterruptHitFlashVfxClock);
        clock.Begin(0.2f, 0.3f);
        float real = 0f;
        while (clock.Running)
        {
            speed = Mathf.Max(speed, clock.Advance(1f / 60f, 1f / 60f));
            real += 1f / 60f;
        }

        Assert.AreEqual(1f, speed, 1e-5f, "상한 안에 끝나면 배속 1(느려지지도 빨라지지도 않음)");
        Assert.AreEqual(0.2f, real, 1f / 60f + 1e-4f);

        clock.Begin(0.45f, 0f);
        Assert.AreEqual(1f, clock.Advance(0.25f / 60f, 1f / 60f), "상한 0 = 상한 없음");
    }

    [Test]
    public void 시간이_멈추면_상한에서_잘린다()
    {
        float real = RunClock(0.45f, 0.3f, _ => 0f, out InterruptHitFlashVfxClock frozen);
        Assert.LessOrEqual(real, 0.3f + 1f / 60f + 1e-4f);
        Assert.IsTrue(frozen.CutByCap, "남은 입자는 지운다");
    }

    [Test]
    public void 라이트_번쩍_곡선_시간에서_세기()
    {
        AnimationCurve linear = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        Assert.AreEqual(0f, InterruptEffectLight.Pulse(linear, 10f, -0.01f, 0.25f), "시작 전");
        Assert.AreEqual(10f, InterruptEffectLight.Pulse(linear, 10f, 0f, 0.25f), 1e-4f, "섬광 순간 = 최대");
        Assert.AreEqual(5f, InterruptEffectLight.Pulse(linear, 10f, 0.125f, 0.25f), 1e-4f);
        Assert.AreEqual(0f, InterruptEffectLight.Pulse(linear, 10f, 0.25f, 0.25f), "끝 = 0(꺼짐)");
        Assert.AreEqual(0f, InterruptEffectLight.Pulse(linear, 10f, 0f, 0f), "길이 0 = 라이트 없음");
        Assert.AreEqual(0f, InterruptEffectLight.Pulse(AnimationCurve.Constant(0f, 1f, -1f), 10f, 0.1f, 0.25f), "음수 곡선 = 0");
    }

    private InterruptHitFlashVfx NewWorldVfx()
    {
        var go = Track(new GameObject("WorldFlash"));
        go.AddComponent<ParticleSystem>();
        return go.AddComponent<InterruptHitFlashVfx>();
    }

    [Test]
    public void 적중_지점_라이트는_섬광_순간_켜져_곡선대로_꺼진다()
    {
        InterruptHitFlashVfx vfx = NewWorldVfx();
        float peak = new SerializedObject(vfx).FindProperty("lightIntensity").floatValue;
        vfx.Play(Target, 2f);

        Light light = vfx.HitLight;
        Assert.IsNotNull(light);
        Assert.AreEqual(LightType.Point, light.type);
        Assert.AreEqual(LightShadows.None, light.shadows, "그림자 기본 끔");
        Assert.AreEqual(Target, light.transform.position, "적중 지점");
        Assert.AreEqual(2f * new SerializedObject(vfx).FindProperty("lightRange").floatValue, light.range, 1e-4f, "반경 × 루트 스케일");
        Assert.IsTrue(light.enabled);
        Assert.AreEqual(peak, light.intensity, 1e-4f, "섬광 순간 = 최대");

        float last = light.intensity;
        for (float t = 0f; t < vfx.LightDuration - 0.02f; t += 0.02f)
        {
            vfx.Tick(0.02f, 0.005f);
            Assert.LessOrEqual(light.intensity, last + 1e-4f, $"줄어든다 {t}");
            last = light.intensity;
        }

        vfx.Tick(0.05f, 0.0125f);
        Assert.IsFalse(light.enabled, "~0.25s(실시간) 뒤 꺼짐 — 슬로우와 무관");
    }

    [Test]
    public void 월드_섬광과_라이트는_재사용한다()
    {
        InterruptHitFlashVfx vfx = NewWorldVfx();
        vfx.Play(Target, 1f);
        Light first = vfx.HitLight;
        int children = vfx.transform.childCount;

        vfx.Play(Target + Vector3.right, 1f);
        Assert.AreSame(first, vfx.HitLight, "두 번째 재생에 새 라이트 없음");
        Assert.AreEqual(children, vfx.transform.childCount, "새 오브젝트 없음");
        Assert.IsTrue(first.enabled, "다시 번쩍");

        vfx.Stop();
        Assert.IsFalse(first.enabled, "강제 리셋 = 라이트 끔");
        Assert.IsFalse(vfx.Clock.Running);
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
        Assert.AreEqual(2, so.FindProperty("hitFlashStarburstFrames").intValue, "② 렌더 프레임 수(§17)");
        Assert.AreEqual(2, so.FindProperty("hitFlashWhiteoutFrames").intValue);
        Assert.AreEqual(2, so.FindProperty("hitFlashSmearFrames").intValue);
        Assert.AreEqual(0.1f, so.FindProperty("hitFlashAberrationDuration").floatValue, 1e-6f);
        Assert.AreEqual(0.2f, so.FindProperty("hitFlashSettleDuration").floatValue, 1e-6f);
        foreach (string removed in new[] { "hitFlashBurstDuration", "hitFlashGlareDuration", "hitFlashInvertDuration", "hitFlashReturnDuration", "hitFlashSpeedLines" })
            Assert.IsNull(so.FindProperty(removed), removed + " — 옛 1~4단계 칸은 없앤다");
        foreach (string removed in new[] { "hitFlashStarburstDuration", "hitFlashWhiteoutDuration", "hitFlashSmearDuration" })
            Assert.IsNull(so.FindProperty(removed), removed + " — ②③④ 초 칸은 프레임 칸으로 교체(§17)");
        Assert.AreEqual(0.6f, so.FindProperty("hitFlashBlurStrength").floatValue, 1e-6f, "방사형 블러 강하게(§16)");
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
        Assert.AreEqual(6, InterruptHitFlashTimeline.From(director).FlashMomentFrames, "프리팹 섬광 순간 = 2+2+2 프레임");
        Assert.AreEqual(0.3f, InterruptHitFlashTimeline.From(director).AfterFlashDuration, 1e-5f, "프리팹 섬광 순간 뒤 ⑤⑥");
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
        Assert.GreaterOrEqual(horizontal.fallOff, 0.6f, "가로 빛살 끝이 서서히 흐려진다(완만한 감쇠, §16)");

        // §16 — 딱딱한 흰 원판 대신 작은 부드러운 코어, 빛살 길이는 제각각(+ 무작위 다수).
        var rayLengths = new HashSet<float>();
        bool randomRays = false;
        foreach (LensFlareDataElementSRP element in flare.elements)
        {
            bool round = Mathf.Abs(element.sizeXY.x - element.sizeXY.y) < 1e-3f;
            if (round)
            {
                Assert.LessOrEqual(element.sizeXY.x * element.uniformScale, 1.2f, "코어는 작게");
                Assert.GreaterOrEqual(element.fallOff, 0.4f, "코어는 부드럽게(가우시안에 가까운 감쇠)");
                continue;
            }

            rayLengths.Add(element.sizeXY.x);
            if (element.allowMultipleElement && element.distribution == SRPLensFlareDistribution.Random && element.count >= 8)
                randomRays = true;
        }

        Assert.GreaterOrEqual(rayLengths.Count, 8, "빛살 길이 제각각");
        Assert.IsTrue(randomRays, "무작위 회전·길이 빛살 다수");

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

        Assert.AreEqual(0.3f, vfx.RealTimeCap, 1e-6f, "실시간 상한 0.3s(§16)");
        Assert.AreEqual(0.25f, vfx.LightDuration, 1e-6f, "라이트 ~0.25s(§16)");
        Assert.IsNull(prefab.GetComponentInChildren<Light>(true), "라이트는 프리팹에 두지 않고 첫 재생 때 한 번 만든다");

        ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
        Assert.AreEqual(3, systems.Length, "중심·파란 번짐·불티 — 바퀴살 광선·고리 없음(§16)");
        foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
        {
            Assert.AreNotEqual("Rays", child.name);
            Assert.AreNotEqual("Ring", child.name);
        }

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
