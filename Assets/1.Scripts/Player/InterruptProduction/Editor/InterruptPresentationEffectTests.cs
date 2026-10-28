using System.Collections.Generic;
using BeaverLobby.Player.Dash;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// 연출 켜기/끄기 칸(할 일 132 §17) — 기본값(오너 암전만 켬)·프리팹 기본값, 끈 효과는 그리기만 안 함
/// (오너 상태 기계·섬광 시간표·섬광 순간 사건은 그대로, ②~④ 를 꺼도 섬광 순간에 암전이 걷힌다), 칸 런타임 변경.
/// </summary>
public sealed class InterruptPresentationEffectTests
{
    private const string NetworkManagerPath = "Assets/2.Prefabs/Network/NetworkManager.prefab";
    private static readonly Vector3 Target = new Vector3(3f, 0f, 4f);

    private sealed class FakeEffects : IInterruptPresentationEffectSettings
    {
        public readonly HashSet<InterruptPresentationEffect> On = new HashSet<InterruptPresentationEffect>();
        public bool IsEffectOn(InterruptPresentationEffect effect) => On.Contains(effect);
    }

    private sealed class FakeTiming : IInterruptHitFlashSettings, IInterruptOwnerPresentationSettings
    {
        public int HitFlashStarburstFrames => 2;
        public int HitFlashWhiteoutFrames => 2;
        public int HitFlashSmearFrames => 2;
        public float HitFlashAberrationDuration => 0.125f;
        public float HitFlashSettleDuration => 0.25f;
        public InterruptEffectTimeBase HitFlashTimeBase => InterruptEffectTimeBase.Unscaled;
        public float OwnerDimInDuration => 0.125f;
        public float OwnerDimOutDuration => 0.25f;
    }

    private sealed class FakeScreen : IInterruptHitFlashScreenSettings
    {
        public readonly FakeEffects Effects = new FakeEffects();
        public LensFlareDataSRP Flare;
        public VolumeProfile AberrationProfile;
        public VolumeProfile BloomProfile;

        public bool IsEffectOn(InterruptPresentationEffect effect) => Effects.IsEffectOn(effect);
        public Material HitFlashOverlayMaterial => null;
        public LensFlareDataSRP HitFlashLensFlare => Flare;
        public float HitFlashFlareIntensity => 1f;
        public float HitFlashFlareScale => 1f;
        public float HitFlashBlurStrength => 0.5f;
        public VolumeProfile HitFlashAberrationProfile => AberrationProfile;
        public VolumeProfile HitFlashBloomProfile => BloomProfile;
    }

    private sealed class FakeOutput : IInterruptOwnerDimOutput, IInterruptLightGatherOutput, IInterruptHitFlashOverlayOutput
    {
        public float Dim;
        public int GatherStarts;
        public int GatherStops;
        public InterruptHitFlashStage Stage;

        public void SetOwnerDim(float intensity) => Dim = intensity;
        public void StartLightGather(PlayerInterruptSkillBase skill) => GatherStarts++;
        public void StopLightGather() => GatherStops++;
        public void Show(InterruptHitFlashStage stage, float progress, Vector3 worldCenter, Vector3 worldDirection) => Stage = stage;
        public void Hide() => Stage = InterruptHitFlashStage.None;
    }

    private readonly List<Object> _created = new List<Object>();

    private T Track<T>(T obj) where T : Object
    {
        _created.Add(obj);
        return obj;
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

    // ─── 기본값 ───

    [Test]
    public void 기본값은_오너_암전만_켬()
    {
        foreach (InterruptPresentationEffect effect in InterruptPresentationEffectRules.All)
            Assert.AreEqual(effect == InterruptPresentationEffect.OwnerDim, InterruptPresentationEffectRules.DefaultOn(effect), effect.ToString());

        Assert.AreEqual(9, InterruptPresentationEffectRules.All.Length, "오너 암전·빛 모임·②~⑥·월드 섬광·적중 지점 라이트");
    }

    [Test]
    public void 새_디렉터_칸_기본값()
    {
        var director = Track(new GameObject("Director")).AddComponent<InterruptProductionDirector>();
        foreach (InterruptPresentationEffect effect in InterruptPresentationEffectRules.All)
            Assert.AreEqual(InterruptPresentationEffectRules.DefaultOn(effect), director.IsEffectOn(effect), effect.ToString());
        Assert.IsFalse(director.Get(InterruptProductionCue.DarkAdaptation), "팀원 암순응(A2) 기본 끔");
    }

    [Test]
    public void NetworkManager_프리팹_기본값은_오너_암전만_켬_슬로우_줌_히트스톱은_그대로()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPath);
        Assert.IsNotNull(prefab, NetworkManagerPath);
        var director = prefab.GetComponent<InterruptProductionDirector>();
        Assert.IsNotNull(director);

        foreach (InterruptPresentationEffect effect in InterruptPresentationEffectRules.All)
            Assert.AreEqual(InterruptPresentationEffectRules.DefaultOn(effect), director.IsEffectOn(effect), effect.ToString());
        Assert.IsFalse(director.Get(InterruptProductionCue.DarkAdaptation), "팀원 암순응(A2) 끔");
        Assert.IsTrue(director.Get(InterruptProductionCue.HitStop), "A1 히트스톱 그대로");
        Assert.IsTrue(director.Get(InterruptProductionCue.OwnerZoom), "A3 오너 줌 그대로");
        Assert.Greater(director.InterruptSuccessProfile.hitStopDuration, 0f, "성공 히트스톱 프로필 그대로");
    }

    [Test]
    public void 디렉터_칸_런타임_변경은_인스턴스_값만()
    {
        var director = Track(new GameObject("Director")).AddComponent<InterruptProductionDirector>();
        foreach (InterruptPresentationEffect effect in InterruptPresentationEffectRules.All)
        {
            director.SetEffect(effect, true);
            Assert.IsTrue(director.IsEffectOn(effect), effect.ToString());
            director.SetEffect(effect, false);
            Assert.IsFalse(director.IsEffectOn(effect), effect.ToString());
        }

        foreach (InterruptProductionCue cue in (InterruptProductionCue[])System.Enum.GetValues(typeof(InterruptProductionCue)))
        {
            director.Set(cue, true);
            Assert.IsTrue(director.Get(cue), cue.ToString());
            director.Set(cue, false);
            Assert.IsFalse(director.Get(cue), cue.ToString());
        }
    }

    [Test]
    public void 섬광_단계와_효과_칸()
    {
        var effects = new FakeEffects();
        Assert.IsTrue(InterruptPresentationEffectRules.StageVisible(null, InterruptHitFlashStage.Whiteout), "칸이 없으면 그린다");
        foreach (InterruptHitFlashStage stage in InterruptHitFlashTimeline.Order)
        {
            Assert.IsTrue(InterruptPresentationEffectRules.TryGetStageEffect(stage, out InterruptPresentationEffect effect));
            Assert.IsFalse(InterruptPresentationEffectRules.StageVisible(effects, stage), stage.ToString());
            effects.On.Add(effect);
            Assert.IsTrue(InterruptPresentationEffectRules.StageVisible(effects, stage), stage.ToString());
        }

        Assert.IsFalse(InterruptPresentationEffectRules.TryGetStageEffect(InterruptHitFlashStage.None, out _));
    }

    // ─── 오너 출력 문 ───

    [Test]
    public void 암전을_끄면_상태_기계는_그대로_덮개만_0()
    {
        var effects = new FakeEffects();
        var output = new FakeOutput();
        var gate = new InterruptOwnerOutputGate(effects, output, output);
        var owner = new InterruptOwnerPresentation(new FakeTiming(), gate, gate);

        owner.Predict(InterruptAttemptForecast.Strong, null, 0f);
        owner.Tick(0.125f);
        Assert.AreEqual(InterruptOwnerPresentationState.AttemptStrong, owner.State);
        Assert.AreEqual(1f, owner.DimIntensity, "상태 기계 값은 그대로");
        Assert.AreEqual(1f, gate.RequestedDim);
        Assert.AreEqual(0f, output.Dim, "그림만 0");
        Assert.AreEqual(0, output.GatherStarts, "빛 모임 칸 꺼짐");
        Assert.IsTrue(owner.LightGatherActive, "빛 모임 상태는 그대로");

        // Play 중 칸을 켜면 다음 프레임(Sync) 바로 보인다.
        effects.On.Add(InterruptPresentationEffect.OwnerDim);
        gate.Sync();
        Assert.AreEqual(1f, output.Dim);
        effects.On.Remove(InterruptPresentationEffect.OwnerDim);
        gate.Sync();
        Assert.AreEqual(0f, output.Dim);

        owner.ServerStarted(7, SlowMotionPurpose.Success, true, 0.2f);
        Assert.IsTrue(owner.SuccessDimmed, "암전 칸을 꺼도 성공 조건(강 시도)은 그대로 — 섬광 오버레이 조건이 꼬이지 않는다");
    }

    [Test]
    public void 켠_효과만_출력에_넘긴다()
    {
        var effects = new FakeEffects();
        effects.On.Add(InterruptPresentationEffect.OwnerDim);
        effects.On.Add(InterruptPresentationEffect.LightGather);
        var output = new FakeOutput();
        var gate = new InterruptOwnerOutputGate(effects, output, output);
        var owner = new InterruptOwnerPresentation(new FakeTiming(), gate, gate);

        owner.Predict(InterruptAttemptForecast.Strong, null, 0f);
        owner.Tick(0.125f);
        Assert.AreEqual(1f, output.Dim);
        Assert.AreEqual(1, output.GatherStarts);
        owner.LocalHit();
        Assert.AreEqual(1, output.GatherStops, "정지는 늘 넘긴다");
    }

    // ─── 섬광 — 끈 효과는 그리기만 ───

    // 디렉터와 같은 배선 — 오너 상태 기계(문 뒤) + 진행기 + 섬광 순간에 암전 끄기.
    private void RunSuccess(FakeEffects effects, IInterruptHitFlashOverlayOutput overlay, out InterruptOwnerPresentation owner,
        out FakeOutput output, out List<int> flashFrames, out InterruptHitFlashSequencer sequencer)
    {
        var timing = new FakeTiming();
        var localOutput = new FakeOutput();
        var gate = new InterruptOwnerOutputGate(effects, localOutput, localOutput);
        var localOwner = new InterruptOwnerPresentation(timing, gate, gate);
        var frames = new List<int>();
        int frame = 0;
        var localSequencer = new InterruptHitFlashSequencer(timing, overlay, m =>
        {
            frames.Add(frame);
            if (m.ViewerIsCaster)
                localOwner.SuccessFlashMoment(m.Event.TriggerId, frame / 60f);
        });

        localOwner.Predict(InterruptAttemptForecast.Strong, null, -1f);
        localOwner.Tick(-0.5f);
        localOwner.ServerStarted(7, SlowMotionPurpose.Success, true, 0f);
        bool ownerOverlay = InterruptHitFlashRules.ShouldPlayOwnerOverlay(SlowMotionPurpose.Success, true, localOwner.SuccessDimmed);
        Assert.IsTrue(ownerOverlay, "효과 칸과 무관하게 오버레이 조건은 상태로");
        localSequencer.BeginOwnerOverlay(Target, Vector3.forward, 0f, 0);
        localSequencer.ScheduleFlash(new InterruptHitFlashMoment(InterruptProductionEvent.LocalSuccess(7, 2, Target), true, true), 0);

        for (frame = 1; frame <= 5; frame++)
        {
            localOwner.Tick(frame / 60f);
            gate.Sync();
            localSequencer.Tick(frame / 60f, frame);
            Assert.AreEqual(1f, localOwner.DimIntensity, $"섬광 순간 전 암전 상태 유지 — 프레임 {frame}");
        }

        frame = 6;
        localOwner.Tick(frame / 60f);
        localSequencer.Tick(frame / 60f, frame);
        gate.Sync();

        owner = localOwner;
        output = localOutput;
        flashFrames = frames;
        sequencer = localSequencer;
    }

    [Test]
    public void 스타버스트_화이트아웃_스미어를_꺼도_섬광_순간에_암전이_걷힌다()
    {
        var effects = new FakeEffects();
        effects.On.Add(InterruptPresentationEffect.OwnerDim); // 기본값 = 암전만
        var overlay = new FakeOutput();
        RunSuccess(effects, overlay, out InterruptOwnerPresentation owner, out FakeOutput output, out List<int> flashFrames,
            out InterruptHitFlashSequencer sequencer);

        CollectionAssert.AreEqual(new[] { 6 }, flashFrames, "섬광 순간 = 2+2+2 프레임 뒤 한 번");
        Assert.AreEqual(0f, owner.DimIntensity, "섬광 순간 = 암전 걷힘");
        Assert.AreEqual(0f, output.Dim);
        Assert.AreEqual(InterruptHitFlashStage.Aberration, sequencer.OverlayStage, "시간표는 그대로 돈다");

        owner.Tick(1f);
        Assert.AreEqual(InterruptOwnerPresentationState.None, owner.State, "성공 단계가 정상으로 끝난다");
    }

    [Test]
    public void 모든_효과를_꺼도_상태_기계와_섬광_순간은_정상()
    {
        var effects = new FakeEffects();
        var overlay = new FakeOutput();
        RunSuccess(effects, overlay, out InterruptOwnerPresentation owner, out FakeOutput output, out List<int> flashFrames, out _);

        CollectionAssert.AreEqual(new[] { 6 }, flashFrames);
        Assert.AreEqual(0f, output.Dim, "암전 그림 없음");
        Assert.AreEqual(0, output.GatherStarts, "빛 모임 그림 없음");
        Assert.AreEqual(0f, owner.DimIntensity, "상태 기계는 섬광 순간에 암전을 끔");
        owner.Tick(1f);
        Assert.AreEqual(InterruptOwnerPresentationState.None, owner.State);
    }

    [Test]
    public void 실제_오버레이는_꺼진_단계를_그리지_않는다()
    {
        var flare = Track(ScriptableObject.CreateInstance<LensFlareDataSRP>());
        flare.elements = new LensFlareDataElementSRP[0];
        var screen = new FakeScreen
        {
            Flare = flare,
            AberrationProfile = Track(ScriptableObject.CreateInstance<VolumeProfile>()),
            BloomProfile = Track(ScriptableObject.CreateInstance<VolumeProfile>()),
        };
        var output = new InterruptHitFlashOverlay(screen);
        try
        {
            // 전부 끔 — ② 플레어·③ 블러·⑤ 색수차·블룸 0.
            output.Show(InterruptHitFlashStage.Starburst, 0f, Target, Vector3.forward);
            Assert.IsFalse(output.FlareActive, "② 꺼짐");
            output.Show(InterruptHitFlashStage.Whiteout, 0f, Target, Vector3.forward);
            Assert.AreEqual(0f, DarkAdaptationState.HitFlashBlurStrength, "③ 꺼짐");
            output.Show(InterruptHitFlashStage.Aberration, 0f, Target, Vector3.forward);
            Assert.AreEqual(0f, output.AberrationWeight, "⑤ 꺼짐");
            Assert.AreEqual(0f, output.BloomWeight);

            // 켠 단계만.
            screen.Effects.On.Add(InterruptPresentationEffect.Starburst);
            screen.Effects.On.Add(InterruptPresentationEffect.Aberration);
            output.Show(InterruptHitFlashStage.Starburst, 0f, Target, Vector3.forward);
            Assert.IsTrue(output.FlareActive, "② 켬");
            output.Show(InterruptHitFlashStage.Whiteout, 0f, Target, Vector3.forward);
            Assert.IsFalse(output.FlareActive);
            Assert.AreEqual(0f, DarkAdaptationState.HitFlashBlurStrength, "③ 여전히 꺼짐");
            output.Show(InterruptHitFlashStage.Aberration, 0f, Target, Vector3.forward);
            Assert.AreEqual(1f, output.AberrationWeight, "⑤ 켬");
            output.Show(InterruptHitFlashStage.Settle, 0f, Target, Vector3.forward);
            Assert.AreEqual(0f, output.AberrationWeight, "⑥ 꺼짐 — 그 단계 동안 0");

            output.Hide();
            Assert.AreEqual(0f, output.AberrationWeight, "숨김 = 원래대로");
        }
        finally
        {
            output.Dispose();
        }
    }

    // ─── 월드 섬광·적중 지점 라이트 ───

    private InterruptHitFlashVfx NewWorldVfx()
    {
        var go = Track(new GameObject("WorldFlash"));
        go.AddComponent<ParticleSystem>();
        return go.AddComponent<InterruptHitFlashVfx>();
    }

    [Test]
    public void 월드_섬광과_라이트는_따로_끈다()
    {
        InterruptHitFlashVfx vfx = NewWorldVfx();

        vfx.Play(Target, 1f, false, true);
        Assert.IsFalse(vfx.Clock.Running, "파티클 꺼짐");
        Assert.IsNotNull(vfx.HitLight);
        Assert.IsTrue(vfx.HitLight.enabled, "라이트만 번쩍");

        vfx.Play(Target, 1f, true, false);
        Assert.IsTrue(vfx.Clock.Running, "파티클 켬");
        Assert.IsFalse(vfx.HitLight.enabled, "라이트 꺼짐");
    }

    [Test]
    public void 라이트를_끄면_라이트를_만들지_않는다()
    {
        InterruptHitFlashVfx vfx = NewWorldVfx();
        vfx.Play(Target, 1f, true, false);
        Assert.IsNull(vfx.HitLight, "첫 재생에 라이트 꺼짐이면 만들지 않는다");
    }
}
