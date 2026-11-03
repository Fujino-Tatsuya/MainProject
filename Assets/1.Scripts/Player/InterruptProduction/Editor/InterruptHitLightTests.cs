using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// 적중 라이트 연출(V1·V2, 할 일 194) — 포인트 라이트 실시간 곡선, 블룸 Volume 곡선, 진행기 끝·강제 리셋, 프리팹 연결.
/// </summary>
public sealed class InterruptHitLightTests
{
    private readonly List<Object> _objects = new List<Object>();
    private readonly List<InterruptHitLightPlayer> _players = new List<InterruptHitLightPlayer>();

    [TearDown]
    public void TearDown()
    {
        foreach (InterruptHitLightPlayer player in _players)
            player.Dispose();
        _players.Clear();
        foreach (Object o in _objects)
            if (o != null)
                Object.DestroyImmediate(o);
        _objects.Clear();
    }

    private InterruptHitLightLook CreateLook(bool withBloom)
    {
        var look = ScriptableObject.CreateInstance<InterruptHitLightLook>();
        _objects.Add(look);
        if (withBloom)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _objects.Add(profile);
            var so = new SerializedObject(look);
            so.FindProperty("bloomProfile").objectReferenceValue = profile;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        return look;
    }

    private InterruptHitLightFx CreateFx()
    {
        var go = new GameObject("FX_InterruptHitLight");
        _objects.Add(go);
        return go.AddComponent<InterruptHitLightFx>();
    }

    private InterruptHitLightPlayer CreatePlayer(InterruptHitLightLook look, InterruptHitLightFx fx)
    {
        var player = new InterruptHitLightPlayer(() => look, () => fx);
        _players.Add(player);
        return player;
    }

    [Test]
    public void 기본_수치는_Q13_따뜻한_흰주황_세기20_반경8_0점9초()
    {
        InterruptHitLightLook look = CreateLook(false);
        Assert.AreEqual(new Color(1f, 0.75f, 0.45f, 1f), look.LightColor);
        Assert.AreEqual(20f, look.LightIntensity, 1e-6f);
        Assert.AreEqual(8f, look.LightRange, 1e-6f);
        Assert.AreEqual(LightShadows.None, look.LightShadows);
        Assert.AreEqual(0.9f, look.LightDuration, 1e-6f);
        Assert.AreEqual(0.9f, look.BloomDuration, 1e-6f);
    }

    [Test]
    public void 라이트는_적중_순간_최대에서_0점9초에_0()
    {
        InterruptHitLightLook look = CreateLook(false);
        Assert.AreEqual(20f, InterruptHitLightFx.LightIntensityAt(look, 0f), 1e-4f);
        float mid = InterruptHitLightFx.LightIntensityAt(look, 0.45f);
        Assert.Greater(mid, 0f);
        Assert.Less(mid, 20f);
        Assert.AreEqual(0f, InterruptHitLightFx.LightIntensityAt(look, 0.9f));
        Assert.AreEqual(0f, InterruptHitLightFx.LightIntensityAt(null, 0f), "데이터 없음 = 0");
    }

    [Test]
    public void 월드_이펙트_라이트는_실시간으로_꺼지고_끝나면_꺼진다()
    {
        InterruptHitLightLook look = CreateLook(false);
        InterruptHitLightFx fx = CreateFx();

        fx.Play(new Vector3(1f, 2f, 3f), look, 2f);
        Assert.IsNotNull(fx.HitLight);
        Assert.IsTrue(fx.HitLight.enabled);
        Assert.AreEqual(20f, fx.HitLight.intensity, 1e-4f);
        Assert.AreEqual(16f, fx.HitLight.range, 1e-4f, "반경 × 루트 스케일");
        Assert.AreEqual(look.LightColor, fx.HitLight.color);

        fx.Tick(0.5f);
        Assert.IsTrue(fx.LightActive);
        fx.Tick(0.5f);
        Assert.IsFalse(fx.LightActive);
        Assert.IsFalse(fx.HitLight.enabled, "0 이면 추가 라이트 슬롯을 비운다");
    }

    [Test]
    public void 월드_이펙트_재생은_파티클을_실시간으로_돌린다()
    {
        InterruptHitLightFx fx = CreateFx();
        var child = new GameObject("Spark");
        child.transform.SetParent(fx.transform, false);
        ParticleSystem system = child.AddComponent<ParticleSystem>();

        fx.Play(Vector3.zero, CreateLook(false));

        Assert.IsTrue(system.main.useUnscaledTime, "Q8 — 슬로우·히트스톱과 무관");
    }

    [Test]
    public void 블룸은_적중_순간_가중치_1에서_곡선대로_0()
    {
        InterruptHitLightLook look = CreateLook(true);
        InterruptHitLightPlayer player = CreatePlayer(look, null);

        player.Begin(new InterruptHitLightRequest { Look = InterruptSuccessLook.HitLight }, 10f);
        Assert.IsTrue(player.IsPlaying);
        Assert.AreEqual(1f, player.BloomWeight, 1e-4f);

        player.Tick(10.45f);
        Assert.Greater(player.BloomWeight, 0f);
        Assert.Less(player.BloomWeight, 1f);

        player.Tick(10.95f);
        Assert.AreEqual(0f, player.BloomWeight);
        Assert.IsFalse(player.IsPlaying);
    }

    [Test]
    public void 블룸_프로필이_없으면_가중치_없이_라이트만()
    {
        InterruptHitLightLook look = CreateLook(false);
        InterruptHitLightFx fx = CreateFx();
        InterruptHitLightPlayer player = CreatePlayer(look, fx);

        player.Begin(new InterruptHitLightRequest { Point = Vector3.one, Look = InterruptSuccessLook.HitLight }, 0f);

        Assert.AreEqual(0f, player.BloomWeight);
        Assert.IsTrue(fx.HitLight.enabled);
        Assert.AreEqual(Vector3.one, fx.transform.position);
    }

    [Test]
    public void 강제_리셋은_라이트와_블룸을_바로_끈다()
    {
        InterruptHitLightLook look = CreateLook(true);
        InterruptHitLightFx fx = CreateFx();
        InterruptHitLightPlayer player = CreatePlayer(look, fx);
        player.Begin(new InterruptHitLightRequest { Look = InterruptSuccessLook.HitLight }, 0f);

        player.Stop();

        Assert.IsFalse(player.IsPlaying);
        Assert.AreEqual(0f, player.BloomWeight);
        Assert.IsFalse(fx.HitLight.enabled);
        Assert.IsFalse(fx.LightActive);
    }

    // ---- 단계 3 — 암순응 가(화면 패스)·나(씬 조명) ----

    private sealed class FakeDarkAdapt : ISuppressDarkAdaptOutput
    {
        public readonly List<float> Shown = new List<float>();
        public int Clears;
        public ulong Caster;
        public Vector3 Target;

        public void ShowDarkAdaptCut(float darkness, ulong casterClientId, Vector3 targetPosition)
        {
            Shown.Add(darkness);
            Caster = casterClientId;
            Target = targetPosition;
        }

        public void ClearDarkAdapt() => Clears++;
    }

    private sealed class FakeSceneTarget : ISceneLightTarget
    {
        public Light Sun;
        public Light FindSun() => Sun;
        public SphericalHarmonicsL2 AmbientProbe { get; set; }
    }

    [TestCase(true, ExpectedResult = 1f)]
    [TestCase(false, ExpectedResult = 0.35f)]
    public float 암순응_가_최대는_본인_1_팀원_0점35(bool viewerIsCaster)
    {
        InterruptHitLightLook look = CreateLook(false);
        return InterruptHitLightRules.DarkAdaptPeak(viewerIsCaster, look.DarkAdaptSelf, look.DarkAdaptOthers);
    }

    [Test]
    public void 암순응_가는_적중_순간_어두워졌다가_0점9초에_걷힌다()
    {
        InterruptHitLightLook look = CreateLook(false);
        var dark = new FakeDarkAdapt();
        var player = new InterruptHitLightPlayer(() => look, () => null, () => dark);
        _players.Add(player);

        player.Begin(new InterruptHitLightRequest
        {
            ViewerIsCaster = false, CasterClientId = 7, TargetPosition = Vector3.right, Look = InterruptSuccessLook.HitLight,
            DarkAdapt = InterruptDarkAdaptMode.ScreenPass,
        }, 0f);
        Assert.AreEqual(0.35f, dark.Shown[0], 1e-4f, "팀원 화면 = 약하게");
        Assert.AreEqual(7ul, dark.Caster);
        Assert.AreEqual(Vector3.right, dark.Target);

        player.Tick(0.45f);
        Assert.Less(dark.Shown[dark.Shown.Count - 1], 0.35f, "부드럽게 복귀");
        Assert.AreEqual(0, dark.Clears);

        player.Tick(1f);
        Assert.AreEqual(1, dark.Clears, "끝나면 0");
        Assert.IsFalse(player.IsPlaying);
    }

    [Test]
    public void 암순응_나는_태양광_환경광을_낮췄다가_원래로()
    {
        InterruptHitLightLook look = CreateLook(false);
        var go = new GameObject("Sun");
        _objects.Add(go);
        Light sun = go.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1f;
        var probe = new SphericalHarmonicsL2();
        probe.AddAmbientLight(Color.white);
        var target = new FakeSceneTarget { Sun = sun, AmbientProbe = probe };
        float ambient = probe[0, 0];
        var dim = new SceneLightDim(target);
        var dark = new FakeDarkAdapt();
        var player = new InterruptHitLightPlayer(() => look, () => null, () => dark, dim);
        _players.Add(player);

        player.Begin(new InterruptHitLightRequest { Look = InterruptSuccessLook.HitLight, DarkAdapt = InterruptDarkAdaptMode.SceneLight }, 0f);
        Assert.AreEqual(1f - look.SunDimDepth, sun.intensity, 1e-4f, "적중 순간 최대로 낮춤");
        Assert.AreEqual(ambient * (1f - look.AmbientDimDepth), target.AmbientProbe[0, 0], 1e-4f);
        Assert.AreEqual(0, dark.Shown.Count, "나는 화면 패스를 쓰지 않는다");

        player.Tick(0.45f);
        Assert.Greater(sun.intensity, 1f - look.SunDimDepth);
        Assert.Less(sun.intensity, 1f);

        player.Tick(1f);
        Assert.AreEqual(1f, sun.intensity, 1e-5f);
        Assert.AreEqual(ambient, target.AmbientProbe[0, 0], 1e-5f);
        Assert.AreEqual(1f, dim.SunFactor);
    }

    [Test]
    public void 강제_리셋은_암순응_가_나를_바로_걷는다()
    {
        InterruptHitLightLook look = CreateLook(false);
        var dark = new FakeDarkAdapt();
        var go = new GameObject("Sun");
        _objects.Add(go);
        Light sun = go.AddComponent<Light>();
        sun.intensity = 1f;
        var dim = new SceneLightDim(new FakeSceneTarget { Sun = sun });
        var player = new InterruptHitLightPlayer(() => look, () => null, () => dark, dim);
        _players.Add(player);

        player.Begin(new InterruptHitLightRequest { Look = InterruptSuccessLook.HitLight, ViewerIsCaster = true }, 0f);
        player.Stop();
        Assert.AreEqual(1, dark.Clears);

        player.Begin(new InterruptHitLightRequest { Look = InterruptSuccessLook.HitLight, DarkAdapt = InterruptDarkAdaptMode.SceneLight }, 0f);
        player.Stop();
        Assert.AreEqual(1f, sun.intensity, 1e-5f);
    }

    [TestCase(0.85f, 1f, ExpectedResult = 0.15f)]
    [TestCase(0.85f, 0f, ExpectedResult = 1f)]
    [TestCase(2f, 1f, ExpectedResult = 0f)]
    public float 암순응_나_배율(float depth, float envelope) =>
        Mathf.Round(InterruptHitLightRules.DimFactor(depth, envelope) * 1000f) / 1000f;

    [Test]
    public void 진행기_길이는_가장_긴_곡선()
    {
        InterruptHitLightLook look = CreateLook(false);
        Assert.AreEqual(0.9f, InterruptHitLightRules.Duration(look), 1e-6f);
        Assert.IsFalse(InterruptHitLightRules.Finished(look, 0.5f));
        Assert.IsTrue(InterruptHitLightRules.Finished(look, 0.9f));
        Assert.IsTrue(InterruptHitLightRules.Finished(null, 0f));
    }

    [Test]
    public void 디렉터는_적중_라이트_데이터와_프리팹이_둘_다_있어야_V1_V2()
    {
        var go = new GameObject("Director");
        _objects.Add(go);
        var director = go.AddComponent<InterruptProductionDirector>();
        Assert.AreEqual(InterruptSuccessLook.HitFlash, director.SuccessLook(InterruptPresentationTier.Normal), "둘 다 없음 = V0");

        var so = new SerializedObject(director);
        so.FindProperty("hitLightLook").objectReferenceValue = CreateLook(false);
        so.ApplyModifiedPropertiesWithoutUndo();
        Assert.AreEqual(InterruptSuccessLook.HitFlash, director.SuccessLook(InterruptPresentationTier.Normal), "프리팹 없음 = V0");

        so.FindProperty("hitLightVfx").objectReferenceValue = CreateFx();
        so.ApplyModifiedPropertiesWithoutUndo();
        Assert.AreEqual(InterruptSuccessLook.HitLight, director.SuccessLook(InterruptPresentationTier.Normal));
        Assert.AreEqual(InterruptSuccessLook.HitLightWhiteout, director.SuccessLook(InterruptPresentationTier.Dash));
    }

    // ---- 단계 4 — 불꽃·메시 파편 ----

    private static ParticleSystem PrefabSystem(string child)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(InterruptHitLightAuthoring.PrefabPath);
        Assert.IsNotNull(prefab, InterruptHitLightAuthoring.PrefabPath);
        Transform t = prefab.transform.Find(child);
        Assert.IsNotNull(t, child);
        var system = t.GetComponent<ParticleSystem>();
        Assert.IsNotNull(system, child);
        Assert.IsFalse(system.main.playOnAwake, child + " — 디렉터가 성공 순간 튼다");
        Assert.IsFalse(system.main.loop, child);
        Assert.IsTrue(system.main.useUnscaledTime, child + " — Q8 실시간");
        Assert.IsNotNull(t.GetComponent<ParticleSystemRenderer>().sharedMaterial, child + " 재질");
        return system;
    }

    [Test]
    public void 프리팹_불꽃은_늘어난_빌보드_버스트()
    {
        ParticleSystem sparks = PrefabSystem("Sparks");
        Assert.AreEqual(ParticleSystemRenderMode.Stretch, sparks.GetComponent<ParticleSystemRenderer>().renderMode);
        Assert.Greater(sparks.emission.burstCount, 0);
        Assert.Greater(sparks.main.gravityModifier.constant, 0f);
    }

    [Test]
    public void 프리팹_파편은_메시_에셋_중력_바닥_충돌()
    {
        ParticleSystem debris = PrefabSystem("Debris");
        var renderer = debris.GetComponent<ParticleSystemRenderer>();
        Assert.AreEqual(ParticleSystemRenderMode.Mesh, renderer.renderMode);
        Assert.IsNotNull(renderer.mesh);
        Assert.AreEqual(InterruptHitLightAuthoring.DebrisMeshPath, AssetDatabase.GetAssetPath(renderer.mesh), "파편 메시는 에셋(런타임 생성 금지)");
        Assert.IsTrue(debris.collision.enabled);
        Assert.Greater(debris.main.gravityModifier.constant, 0f);
        Assert.LessOrEqual(debris.emission.GetBurst(0).maxCount, 12, "파편은 몇 개만");
    }

    [Test]
    public void NetworkManager_프리팹_디렉터에_적중_라이트가_이어져_있다()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2.Prefabs/Network/NetworkManager.prefab");
        Assert.IsNotNull(prefab);
        var director = prefab.GetComponent<InterruptProductionDirector>();
        var so = new SerializedObject(director);
        Assert.IsNotNull(so.FindProperty("hitLightLook").objectReferenceValue, "hitLightLook");
        Assert.IsNotNull(so.FindProperty("hitLightVfx").objectReferenceValue, "hitLightVfx");
        Assert.IsNotNull(director.HitLightLook.BloomProfile, "간파 전용 블룸 프로필");
        Assert.AreEqual(InterruptSuccessLook.HitLight, director.SuccessLook(InterruptPresentationTier.Normal), "기본 일반 = V1");
        Assert.AreEqual(InterruptSuccessLook.HitLightWhiteout, director.SuccessLook(InterruptPresentationTier.Dash), "기본 대쉬 = V2");
    }
}
