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

        public float HitFlashBurstDuration => Burst;
        public float HitFlashGlareDuration => Glare;
        public float HitFlashInvertDuration => Invert;
        public float HitFlashReturnDuration => Return;
        public bool HitFlashSpeedLines => SpeedLines;

        public float OwnerDimInDuration => 0.125f;
        public float OwnerDimOutDuration => 0.25f;
        public float OwnerSuccessDimReleaseDelay => InterruptHitFlashTimeline.From(this).FlashMoment;
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
        // 히트스톱(스케일 시간 정지)은 진행에 쓰지 않는다 — 실시간 now 만 본다. Started 순간 바로 1단계.
        _sequencer.BeginOwnerOverlay(Target, 20f);
        Assert.IsTrue(_sequencer.OverlayActive);
        Assert.AreEqual(InterruptHitFlashStage.Burst, _overlay.Stage);
        Assert.AreEqual(0f, _overlay.Progress);
        Assert.AreEqual(Target, _overlay.Center);
        Assert.IsTrue(_overlay.SpeedLines);
    }

    [Test]
    public void 오버레이_단계가_실시간으로_넘어가고_끝나면_한_번_숨긴다()
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
        var dim = new FakeDim();
        var owner = new InterruptOwnerPresentation(_settings, dim, dim);
        owner.Predict(InterruptAttemptForecast.Strong, null, 20f);
        owner.Tick(20.125f);
        owner.ServerStarted(9, SlowMotionPurpose.Success, true, 20.25f);
        Assert.IsTrue(InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.Success, true, owner.SuccessDimmed));

        _sequencer.BeginOwnerOverlay(Target, 20.25f);
        _sequencer.ScheduleFlash(Moment(true, true), 20.25f);

        owner.Tick(20.5f);
        _sequencer.Tick(20.5f);
        Assert.AreEqual(1f, dim.Dim, "3단계 흰 반전 동안 암전 유지");
        Assert.AreEqual(InterruptHitFlashStage.Invert, _overlay.Stage);
        Assert.IsEmpty(_flashes);

        owner.Tick(20.625f);
        _sequencer.Tick(20.625f);
        Assert.AreEqual(0f, dim.Dim, "4단계 시작 = 암전 즉시 끔");
        Assert.AreEqual(InterruptHitFlashStage.Return, _overlay.Stage);
        Assert.AreEqual(1, _flashes.Count, "같은 순간 월드 섬광");
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
    public void 디렉터_섬광_칸_기본값과_암전_끄는_때()
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
        Assert.AreEqual(0.3f, director.OwnerSuccessDimReleaseDelay, 1e-5f, "암전 끄는 때 = 섬광 순간");
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
    }

    [Test]
    public void 월드_섬광_프리팹_파티클은_실시간_원샷_계층_스케일()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorldVfxPath);
        Assert.IsNotNull(prefab, WorldVfxPath);
        Assert.IsNotNull(prefab.GetComponent<InterruptHitFlashVfx>());

        ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
        Assert.GreaterOrEqual(systems.Length, 4, "중심·번짐·광선·링(+불티)");
        foreach (ParticleSystem system in systems)
        {
            ParticleSystem.MainModule main = system.main;
            Assert.IsTrue(main.useUnscaledTime, system.name + " 실시간 — 성공 슬로우 중에도 같은 빠르기");
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
