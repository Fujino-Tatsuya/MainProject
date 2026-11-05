using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 간파 연출 단계별 강화(할 일 219) — 1단계 일반 / 2단계 대쉬 / 3단계 제압.
/// 단계별 V3 에셋 선택(빈 칸 = 1단계 대체)·제압 화이트아웃 스위치·오너 판정 순간 제압 예측과 서버 보정·제압 성공 슬로우 프로필·프리팹 연결.
/// </summary>
public sealed class InterruptTierScalingTests
{
    private const string NetworkManagerPrefabPath = "Assets/2.Prefabs/Network/NetworkManager.prefab";

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
        InterruptProductionDirector.DebugSuppressForecastOverride = null;
    }

    private InterruptHitLightLook CreateLook(int whiteoutFrames = 3)
    {
        var look = ScriptableObject.CreateInstance<InterruptHitLightLook>();
        _objects.Add(look);
        var so = new SerializedObject(look);
        so.FindProperty("whiteoutFrames").intValue = whiteoutFrames;
        so.ApplyModifiedPropertiesWithoutUndo();
        return look;
    }

    private InterruptHitLightFx CreateFx(string name)
    {
        var go = new GameObject(name);
        _objects.Add(go);
        return go.AddComponent<InterruptHitLightFx>();
    }

    private InterruptProductionDirector CreateDirector()
    {
        var go = new GameObject("Director");
        _objects.Add(go);
        return go.AddComponent<InterruptProductionDirector>();
    }

    // ---- 순수 규칙 ----

    [TestCase(InterruptPresentationTier.Normal, true, true, ExpectedResult = InterruptPresentationTier.Normal)]
    [TestCase(InterruptPresentationTier.Fail, true, true, ExpectedResult = InterruptPresentationTier.Normal)]
    [TestCase(InterruptPresentationTier.Dash, true, false, ExpectedResult = InterruptPresentationTier.Dash)]
    [TestCase(InterruptPresentationTier.Dash, false, true, ExpectedResult = InterruptPresentationTier.Normal)] // 대쉬 칸 빔 → 1단계
    [TestCase(InterruptPresentationTier.Suppress, false, true, ExpectedResult = InterruptPresentationTier.Suppress)]
    [TestCase(InterruptPresentationTier.Suppress, true, false, ExpectedResult = InterruptPresentationTier.Normal)] // 제압 칸 빔 → 1단계(대쉬 아님)
    public InterruptPresentationTier 단계별_에셋_묶음_빈_칸은_1단계(InterruptPresentationTier tier, bool hasDash, bool hasSuppress) =>
        InterruptPresentationTierRules.SelectReferenceAssetTier(tier, hasDash, hasSuppress);

    [TestCase(InterruptSuccessLook.HitLightReference, InterruptPresentationTier.Suppress, true, 3, true, ExpectedResult = 3)]
    [TestCase(InterruptSuccessLook.HitLightReference, InterruptPresentationTier.Suppress, true, 3, false, ExpectedResult = 0)] // 스위치 끔
    [TestCase(InterruptSuccessLook.HitLightReference, InterruptPresentationTier.Suppress, true, 0, true, ExpectedResult = 0)]  // SO 프레임 0
    [TestCase(InterruptSuccessLook.HitLightReference, InterruptPresentationTier.Suppress, false, 3, true, ExpectedResult = 0)] // 팀원 화면
    [TestCase(InterruptSuccessLook.HitLightReference, InterruptPresentationTier.Dash, true, 3, true, ExpectedResult = 0)]     // 2단계 없음
    [TestCase(InterruptSuccessLook.HitLightReference, InterruptPresentationTier.Normal, true, 3, true, ExpectedResult = 0)]   // 1단계 없음
    [TestCase(InterruptSuccessLook.HitLightWhiteout, InterruptPresentationTier.Normal, true, 3, false, ExpectedResult = 3)]   // V2 는 스위치와 무관
    [TestCase(InterruptSuccessLook.HitLight, InterruptPresentationTier.Suppress, true, 3, true, ExpectedResult = 0)]
    public int 제압_화이트아웃은_V3_제압_단계_스위치_SO_프레임_본인_화면_모두일_때만(InterruptSuccessLook look, InterruptPresentationTier tier,
        bool viewerIsCaster, int frames, bool suppressWhiteout) =>
        InterruptPresentationTierRules.WhiteoutFrames(look, tier, viewerIsCaster, frames, suppressWhiteout);

    [TestCase(InterruptPresentationTier.Normal, false, ExpectedResult = InterruptPresentationTier.Normal)]
    [TestCase(InterruptPresentationTier.Dash, false, ExpectedResult = InterruptPresentationTier.Dash)]
    [TestCase(InterruptPresentationTier.Normal, true, ExpectedResult = InterruptPresentationTier.Suppress)]
    [TestCase(InterruptPresentationTier.Dash, true, ExpectedResult = InterruptPresentationTier.Suppress)]
    [TestCase(InterruptPresentationTier.Suppress, false, ExpectedResult = InterruptPresentationTier.Suppress)] // 랩 재현
    public InterruptPresentationTier 오너_판정_순간_제압_예측이면_3단계(InterruptPresentationTier predicted, bool predictsSuppress) =>
        InterruptPresentationTierRules.OwnerJudgeTier(predicted, predictsSuppress);

    [TestCase(true, InterruptPresentationTier.Normal, InterruptPresentationTier.Suppress, InterruptSuccessLook.HitLightReference, ExpectedResult = true)]
    [TestCase(true, InterruptPresentationTier.Suppress, InterruptPresentationTier.Normal, InterruptSuccessLook.HitLightReference, ExpectedResult = true)]
    [TestCase(true, InterruptPresentationTier.Suppress, InterruptPresentationTier.Suppress, InterruptSuccessLook.HitLightReference, ExpectedResult = false)] // 같으면 다시 안 틂
    [TestCase(false, InterruptPresentationTier.Normal, InterruptPresentationTier.Suppress, InterruptSuccessLook.HitLightReference, ExpectedResult = false)] // cue 로 안 틀었음 = 평소 경로
    [TestCase(true, InterruptPresentationTier.Normal, InterruptPresentationTier.Suppress, InterruptSuccessLook.HitFlash, ExpectedResult = false)]
    public bool 서버_표식은_보험_다를_때만_바로잡는다(bool ownerCueHandled, InterruptPresentationTier played, InterruptPresentationTier server,
        InterruptSuccessLook serverLook) =>
        InterruptPresentationTierRules.ServerCorrectsOwner(ownerCueHandled, played, server, serverLook);

    // ---- 제압 성공 슬로우 ----

    [Test]
    public void 성공_슬로우는_제압_표식이면_제압_프로필()
    {
        var settings = new InterruptProductionStageTests.FakeSettings();
        Assert.AreSame(settings.SuppressSuccess, InterruptProductionRules.SelectSuccessProfile(true, settings));
        Assert.AreSame(settings.Success, InterruptProductionRules.SelectSuccessProfile(false, settings));
    }

    [Test]
    public void 서버_성공_단계가_제압이면_제압_프로필로_슬로우를_건다()
    {
        var settings = new InterruptProductionStageTests.FakeSettings();
        var port = new InterruptProductionStageTests.FakeTimeScale();
        var stage = new InterruptSuccessStage(settings, port);
        InterruptProductionEvent success = InterruptProductionEvent.Success(3, null, null, true, Vector3.one, Vector3.zero);

        stage.Server(success, null);
        stage.Server(success.WithSuppressInterrupt(true), null);
        stage.Server(InterruptProductionEvent.Success(3, null, null, true, Vector3.one, Vector3.zero, InterruptVariant.Dash), null);

        Assert.AreSame(settings.Success, port.Starts[0].profile, "일반 = 기존 성공");
        Assert.AreSame(settings.SuppressSuccess, port.Starts[1].profile, "제압 = 제압 전용");
        Assert.AreSame(settings.Success, port.Starts[2].profile, "대쉬 = 기존 성공");
        Assert.IsTrue(port.SuppressFlags[1], "제압 표식은 그대로 실린다");
    }

    [Test]
    public void 새_슬로우_SO_제압_성공_기본값은_PLAN_시작값()
    {
        var settings = ScriptableObject.CreateInstance<InterruptSlowMotionSettings>();
        _objects.Add(settings);
        SlowMotionProfile p = settings.InterruptSuppressSuccessProfile;
        Assert.AreEqual(0.3f, p.scale, 1e-6f);
        Assert.AreEqual(0.08f, p.hitStopDuration, 1e-6f);
        Assert.AreEqual(0.02f, p.hitStopScale, 1e-6f);
        Assert.AreEqual(0.02f, p.enterDuration, 1e-6f);
        Assert.AreEqual(0.35f, p.holdDuration, 1e-6f);
        Assert.AreEqual(0.25f, p.exitDuration, 1e-6f);
        Assert.AreEqual(0.1f, p.failExitDuration, 1e-6f);
    }

    // ---- 디렉터 단계별 에셋 ----

    [Test]
    public void 디렉터_단계_칸이_비면_1단계_V3_를_쓴다_동작_변화_0()
    {
        InterruptProductionDirector director = CreateDirector();
        InterruptHitLightLook v3 = CreateLook(0);
        var so = new SerializedObject(director);
        so.FindProperty("hitLightLookReference").objectReferenceValue = v3;
        so.FindProperty("hitLightVfxReference").objectReferenceValue = CreateFx("FX_V3");
        so.ApplyModifiedPropertiesWithoutUndo();

        foreach (InterruptPresentationTier tier in new[] { InterruptPresentationTier.Normal, InterruptPresentationTier.Dash, InterruptPresentationTier.Suppress })
        {
            Assert.AreSame(v3, director.HitLightLookFor(InterruptSuccessLook.HitLightReference, tier), tier.ToString());
            Assert.AreEqual(InterruptPresentationTier.Normal, director.ReferenceAssetTier(tier));
        }

        // SO 만 있고 프리팹이 비면 여전히 1단계(섞어 쓰지 않는다).
        so.FindProperty("hitLightLookDash").objectReferenceValue = CreateLook(0);
        so.ApplyModifiedPropertiesWithoutUndo();
        Assert.AreSame(v3, director.HitLightLookFor(InterruptSuccessLook.HitLightReference, InterruptPresentationTier.Dash));
    }

    [Test]
    public void 디렉터_단계_칸이_차면_그_단계_SO_V1_V2_는_그대로()
    {
        InterruptProductionDirector director = CreateDirector();
        InterruptHitLightLook v1 = CreateLook(3);
        InterruptHitLightLook v3 = CreateLook(0);
        InterruptHitLightLook dash = CreateLook(0);
        InterruptHitLightLook suppress = CreateLook(3);
        var so = new SerializedObject(director);
        so.FindProperty("hitLightLook").objectReferenceValue = v1;
        so.FindProperty("hitLightVfx").objectReferenceValue = CreateFx("FX_V1");
        so.FindProperty("hitLightLookReference").objectReferenceValue = v3;
        so.FindProperty("hitLightVfxReference").objectReferenceValue = CreateFx("FX_V3");
        so.FindProperty("hitLightLookDash").objectReferenceValue = dash;
        so.FindProperty("hitLightVfxDash").objectReferenceValue = CreateFx("FX_V3_Dash");
        so.FindProperty("hitLightLookSuppress").objectReferenceValue = suppress;
        so.FindProperty("hitLightVfxSuppress").objectReferenceValue = CreateFx("FX_V3_Suppress");
        so.ApplyModifiedPropertiesWithoutUndo();

        Assert.AreSame(v3, director.HitLightLookFor(InterruptSuccessLook.HitLightReference, InterruptPresentationTier.Normal));
        Assert.AreSame(dash, director.HitLightLookFor(InterruptSuccessLook.HitLightReference, InterruptPresentationTier.Dash));
        Assert.AreSame(suppress, director.HitLightLookFor(InterruptSuccessLook.HitLightReference, InterruptPresentationTier.Suppress));
        Assert.AreSame(v1, director.HitLightLookFor(InterruptSuccessLook.HitLight, InterruptPresentationTier.Suppress), "V1 은 단계 무관");
        Assert.AreSame(v3, director.HitLightLookFor(InterruptSuccessLook.HitLightReference), "옛 1인자 = 1단계");
        Assert.IsTrue(director.SuppressWhiteout, "제압 화이트아웃 스위치 기본 켬");
    }

    // ---- 진행기 ----

    private sealed class TierAssets : IInterruptHitLightAssets
    {
        public readonly Dictionary<InterruptPresentationTier, InterruptHitLightLook> Looks = new Dictionary<InterruptPresentationTier, InterruptHitLightLook>();
        public readonly Dictionary<InterruptPresentationTier, InterruptHitLightFx> Fxs = new Dictionary<InterruptPresentationTier, InterruptHitLightFx>();

        public InterruptHitLightLook LookFor(in InterruptHitLightRequest request) =>
            Looks.TryGetValue(request.Tier, out InterruptHitLightLook look) ? look : null;

        public InterruptHitLightFx FxFor(in InterruptHitLightRequest request) =>
            Fxs.TryGetValue(request.Tier, out InterruptHitLightFx fx) ? fx : null;

        public UnityEngine.Rendering.VolumeProfile GradeFor(in InterruptHitLightRequest request) =>
            InterruptHitLightRules.SelectGrade(LookFor(request), Looks.TryGetValue(InterruptPresentationTier.Normal, out InterruptHitLightLook normal) ? normal : null);
    }

    private sealed class FakeOverlay : IInterruptHitFlashOverlayOutput
    {
        public int Shows;
        public void Show(InterruptHitFlashStage stage, float progress, Vector3 worldCenter, Vector3 worldDirection) => Shows++;
        public void Hide() { }
    }

    [Test]
    public void 진행기는_요청_단계로_에셋을_고르고_제압_화이트아웃은_스위치를_따른다()
    {
        var assets = new TierAssets();
        assets.Looks[InterruptPresentationTier.Normal] = CreateLook(0);
        assets.Looks[InterruptPresentationTier.Suppress] = CreateLook(3);
        InterruptHitLightFx suppressFx = CreateFx("FX_Suppress");
        assets.Fxs[InterruptPresentationTier.Suppress] = suppressFx;
        var overlay = new FakeOverlay();
        var player = new InterruptHitLightPlayer(assets, null, null, overlay);
        _players.Add(player);

        var request = new InterruptHitLightRequest
        {
            Look = InterruptSuccessLook.HitLightReference, ViewerIsCaster = true, Tier = InterruptPresentationTier.Suppress, SuppressWhiteout = true,
        };
        player.Begin(request, 0f, 10);
        Assert.IsTrue(player.WhiteoutShown, "3단계 V3 + 스위치 켬 = 화이트아웃");
        Assert.IsTrue(suppressFx.HitLight != null, "제압 프리팹 인스턴스를 틀었다");

        player.Stop();
        request.SuppressWhiteout = false;
        int before = overlay.Shows;
        player.Begin(request, 1f, 20);
        Assert.AreEqual(before, overlay.Shows, "스위치 끔 = 화이트아웃 없음");

        player.Stop();
        player.Begin(new InterruptHitLightRequest
        {
            Look = InterruptSuccessLook.HitLightReference, ViewerIsCaster = true, Tier = InterruptPresentationTier.Normal, SuppressWhiteout = true,
        }, 2f, 30);
        Assert.AreEqual(before, overlay.Shows, "1단계 V3 = 화이트아웃 없음");
    }

    // ---- 오너 제압 예측 ----

    [Test]
    public void 제압_예측_덮어쓰기와_대상_없음()
    {
        Assert.IsFalse(InterruptProductionDirector.PredictsSuppress(null));
        var target = new GameObject("Target");
        _objects.Add(target);
        Assert.IsFalse(InterruptProductionDirector.PredictsSuppress(target), "예측 구현 없음 = 일반");
        InterruptProductionDirector.DebugSuppressForecastOverride = _ => true;
        Assert.IsTrue(InterruptProductionDirector.PredictsSuppress(target));
    }

    // ---- 프리팹·에셋 ----

    [Test]
    public void NetworkManager_디렉터는_단계별_V3_에셋에_이어져_있다()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPrefabPath);
        Assert.IsNotNull(prefab, NetworkManagerPrefabPath);
        var director = prefab.GetComponent<InterruptProductionDirector>();
        var so = new SerializedObject(director);
        Assert.AreEqual(InterruptHitLightTierAuthoring.DashLookPath, AssetDatabase.GetAssetPath(so.FindProperty("hitLightLookDash").objectReferenceValue));
        Assert.AreEqual(InterruptHitLightTierAuthoring.DashPrefabPath, AssetDatabase.GetAssetPath(so.FindProperty("hitLightVfxDash").objectReferenceValue));
        Assert.AreEqual(InterruptHitLightTierAuthoring.SuppressLookPath, AssetDatabase.GetAssetPath(so.FindProperty("hitLightLookSuppress").objectReferenceValue));
        Assert.AreEqual(InterruptHitLightTierAuthoring.SuppressPrefabPath, AssetDatabase.GetAssetPath(so.FindProperty("hitLightVfxSuppress").objectReferenceValue));
        Assert.IsTrue(so.FindProperty("suppressWhiteout").boolValue);
        Assert.AreEqual(InterruptPresentationTier.Dash, director.ReferenceAssetTier(InterruptPresentationTier.Dash));
        Assert.AreEqual(InterruptPresentationTier.Suppress, director.ReferenceAssetTier(InterruptPresentationTier.Suppress));
    }

    [Test]
    public void 단계별_SO_는_각자_블룸_프로필_제압만_화이트아웃()
    {
        var normal = AssetDatabase.LoadAssetAtPath<InterruptHitLightLook>(InterruptHitLightReferenceAuthoring.LookPath);
        var dash = AssetDatabase.LoadAssetAtPath<InterruptHitLightLook>(InterruptHitLightTierAuthoring.DashLookPath);
        var suppress = AssetDatabase.LoadAssetAtPath<InterruptHitLightLook>(InterruptHitLightTierAuthoring.SuppressLookPath);
        Assert.IsNotNull(normal);
        Assert.IsNotNull(dash);
        Assert.IsNotNull(suppress);
        Assert.AreEqual(InterruptHitLightTierAuthoring.DashBloomPath, AssetDatabase.GetAssetPath(dash.BloomProfile));
        Assert.AreEqual(InterruptHitLightTierAuthoring.SuppressBloomPath, AssetDatabase.GetAssetPath(suppress.BloomProfile));
        Assert.AreEqual(0, dash.WhiteoutFrames, "2단계 화이트아웃 없음");
        Assert.Greater(suppress.WhiteoutFrames, 0, "3단계 화이트아웃 켬(시작값 3)");
        // 색 보정도 단계별(219 후속 11-05) — 서로 다른 에셋, 시작값은 일반과 같다.
        Assert.AreEqual(InterruptHitLightReferenceAuthoring.GradeProfilePath, AssetDatabase.GetAssetPath(normal.GradeProfile));
        Assert.AreEqual(InterruptHitLightTierAuthoring.DashGradePath, AssetDatabase.GetAssetPath(dash.GradeProfile));
        Assert.AreEqual(InterruptHitLightTierAuthoring.SuppressGradePath, AssetDatabase.GetAssetPath(suppress.GradeProfile));
        Assert.AreNotSame(normal.GradeProfile, dash.GradeProfile);
        Assert.AreNotSame(normal.GradeProfile, suppress.GradeProfile);
        Assert.AreNotSame(dash.GradeProfile, suppress.GradeProfile);
        Assert.IsTrue(dash.GradeProfile.TryGet(out UnityEngine.Rendering.Universal.ColorAdjustments _));
        Assert.IsTrue(suppress.GradeProfile.TryGet(out UnityEngine.Rendering.Universal.ColorAdjustments _));
        Assert.LessOrEqual(suppress.GapRayCount, 16);
    }

    private InterruptHitLightLook WithGrade(InterruptHitLightLook look, out UnityEngine.Rendering.VolumeProfile profile)
    {
        profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
        _objects.Add(profile);
        var so = new SerializedObject(look);
        so.FindProperty("gradeProfile").objectReferenceValue = profile;
        so.ApplyModifiedPropertiesWithoutUndo();
        return look;
    }

    [Test]
    public void 단계_색_보정은_단계_것_비면_일반_것_단계_SO_없으면_없음()
    {
        InterruptHitLightLook normal = WithGrade(CreateLook(0), out UnityEngine.Rendering.VolumeProfile normalGrade);
        InterruptHitLightLook dash = WithGrade(CreateLook(0), out UnityEngine.Rendering.VolumeProfile dashGrade);
        InterruptHitLightLook empty = CreateLook(3);

        Assert.AreSame(dashGrade, InterruptHitLightRules.SelectGrade(dash, normal), "단계 프로필");
        Assert.AreSame(normalGrade, InterruptHitLightRules.SelectGrade(empty, normal), "단계 칸 비면 일반");
        Assert.AreSame(normalGrade, InterruptHitLightRules.SelectGrade(normal, normal), "일반 = 일반");
        Assert.IsNull(InterruptHitLightRules.SelectGrade(null, normal), "단계 SO 없음 = 없음");
        Assert.IsNull(InterruptHitLightRules.SelectGrade(empty, null));
    }

    [Test]
    public void 색_보정_칸이_빈_단계는_일반_프로필로_길이와_가중치를_센다()
    {
        InterruptHitLightLook normal = WithGrade(CreateLook(0), out UnityEngine.Rendering.VolumeProfile normalGrade);
        InterruptHitLightLook empty = CreateLook(0);
        UnityEngine.Rendering.VolumeProfile grade = InterruptHitLightRules.SelectGrade(empty, normal);
        Assert.AreSame(normalGrade, grade);
        Assert.AreEqual(InterruptHitLightRules.GradeWeight(normal, 0f), InterruptHitLightRules.GradeWeight(empty, grade, 0f), 1e-6f);
        Assert.AreEqual(0f, InterruptHitLightRules.GradeWeight(empty, 0f), "옛 1인자 = SO 칸만 본다");
        Assert.AreEqual(InterruptHitLightRules.Duration(normal), InterruptHitLightRules.Duration(empty, grade), 1e-6f);
    }

    // 11-05 사용자 결정 — V3(일반·대쉬·제압)에서 파편(Debris) 파티클 삭제. V1·V2(FX_InterruptHitLight)는 그대로.
    [TestCase(InterruptHitLightReferenceAuthoring.PrefabPath)]
    [TestCase(InterruptHitLightTierAuthoring.DashPrefabPath)]
    [TestCase(InterruptHitLightTierAuthoring.SuppressPrefabPath)]
    public void V3_프리팹에는_파편_파티클이_없다(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, path);
        Assert.IsNull(prefab.transform.Find("Debris"), path + " — 파편 삭제(11-05)");
        foreach (string child in new[] { "Rays", "Sparks", "Streak", "Core" })
            Assert.IsNotNull(prefab.transform.Find(child), path + " " + child + " 는 그대로");
    }

    // ---- 11-05 — V3 이펙트 = 게임 시간(히트스톱·슬로우 따라감), V1·V2 = 실시간 ----

    [TestCase(InterruptHitLightReferenceAuthoring.PrefabPath, true)]
    [TestCase(InterruptHitLightTierAuthoring.DashPrefabPath, true)]
    [TestCase(InterruptHitLightTierAuthoring.SuppressPrefabPath, true)]
    [TestCase(InterruptHitLightAuthoring.PrefabPath, false)]
    public void V3_프리팹만_게임_시간(string path, bool expected)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, path);
        Assert.AreEqual(expected, prefab.GetComponent<InterruptHitLightFx>().UsesGameTime, path);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void 재생하면_파티클_시간_기준이_칸을_따른다(bool gameTime)
    {
        InterruptHitLightFx fx = CreateFx("GameTimeFx");
        var child = new GameObject("Sparks");
        child.transform.SetParent(fx.transform, false);
        ParticleSystem system = child.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = system.main;
        main.useUnscaledTime = gameTime; // 반대로 시작
        fx.UsesGameTime = gameTime;

        fx.Play(Vector3.zero, CreateLook(0));

        Assert.AreEqual(!gameTime, system.main.useUnscaledTime, gameTime ? "게임 시간 = unscaled 끔" : "실시간 = unscaled 켬");
    }

    [Test]
    public void 게임_시간_라이트는_히트스톱_중_곡선이_멈추고_실시간_안전장치로_끝난다()
    {
        InterruptHitLightFx fx = CreateFx("GameTimeLight");
        fx.UsesGameTime = true;
        InterruptHitLightLook look = CreateLook(0);
        fx.Play(Vector3.zero, look);
        float start = fx.HitLight.intensity;

        // 히트스톱(게임 시간 거의 0) — 곡선은 거의 안 움직이고 실시간만 흐른다.
        fx.Tick(0f, look.LightDuration * 2f);
        Assert.IsTrue(fx.LightActive, "실시간으로는 곡선 길이를 넘었지만 게임 시간은 멈춰 있다");
        Assert.AreEqual(start, fx.HitLight.intensity, 1e-4f);

        // timeScale 0 근처에 갇혀도 곡선 길이 + 안전장치 초가 지나면 끈다.
        fx.Tick(0f, InterruptHitLightFx.GameTimeSafetyRealSeconds);
        Assert.IsFalse(fx.LightActive);
        Assert.AreEqual(0f, fx.HitLight.intensity);
    }

    [Test]
    public void 라이트_안전장치_규칙()
    {
        InterruptHitLightLook look = CreateLook(0);
        Assert.IsFalse(InterruptHitLightRules.LightTimedOut(look, look.LightDuration));
        Assert.IsTrue(InterruptHitLightRules.LightTimedOut(look, look.LightDuration + InterruptHitLightFx.GameTimeSafetyRealSeconds));
        Assert.IsTrue(InterruptHitLightRules.LightTimedOut(null, 0f), "데이터 없음 = 끝");
    }
}
