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

        public void ShowDarkAdaptCut(float darkness) => Shown.Add(darkness);

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
            ViewerIsCaster = false, Look = InterruptSuccessLook.HitLight,
            DarkAdapt = InterruptDarkAdaptMode.ScreenPass,
        }, 0f);
        Assert.AreEqual(0.35f, dark.Shown[0], 1e-4f, "팀원 화면 = 약하게");

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
        // 기본은 V3(11-03) — V1·V2 데이터 규칙을 보려고 단계 버전을 V1·V2 로(랩 버튼과 같은 런타임 값).
        director.TierProfile(InterruptPresentationTier.Normal).flash = InterruptTierFlash.HitLight;
        director.TierProfile(InterruptPresentationTier.Dash).flash = InterruptTierFlash.HitLightWhiteout;
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

    // ---- 194 후속 — 월드 이펙트 발생 위치 = 시전자 오른손 무기 ----

    private static readonly Vector3 TargetPoint = new Vector3(5f, 1.2f, 0f);
    private static readonly Vector3 WeaponPoint = new Vector3(-1f, 1.4f, 2f);

    [Test]
    public void 위치_선택_무기_대상_못찾음_대체()
    {
        Assert.AreEqual(WeaponPoint, InterruptHitLightRules.FxPoint(InterruptHitLightOrigin.CasterWeapon, TargetPoint, WeaponPoint));
        Assert.AreEqual(TargetPoint, InterruptHitLightRules.FxPoint(InterruptHitLightOrigin.Target, TargetPoint, WeaponPoint), "대상 선택 = 무기 무시");
        Assert.AreEqual(TargetPoint, InterruptHitLightRules.FxPoint(InterruptHitLightOrigin.CasterWeapon, TargetPoint, null), "무기 못 찾음 = 대상");
    }

    [Test]
    public void 월드_이펙트는_요청의_시전자_무기_위치에서_나고_따라가지_않는다()
    {
        InterruptHitLightFx fx = CreateFx();
        var asked = new List<ulong>();
        Vector3 weapon = WeaponPoint;
        var player = new InterruptHitLightPlayer(() => CreateLook(false), () => fx, casterWeapon: id =>
        {
            asked.Add(id);
            return weapon;
        });
        _players.Add(player);

        player.Begin(new InterruptHitLightRequest
        {
            Point = TargetPoint, CasterClientId = 3, Origin = InterruptHitLightOrigin.CasterWeapon, Look = InterruptSuccessLook.HitLight,
        }, 0f);

        CollectionAssert.AreEqual(new[] { 3ul }, asked, "요청에 실린 시전자로 찾는다");
        Assert.AreEqual(WeaponPoint, player.FxPoint);
        Assert.AreEqual(WeaponPoint, fx.transform.position);

        weapon = Vector3.zero; // 시전자가 움직여도
        player.Tick(0.3f);
        Assert.AreEqual(WeaponPoint, fx.transform.position, "성공 순간 위치에 남는다");
        Assert.AreEqual(1, asked.Count, "무기 위치는 시작 때 한 번만 읽는다");
    }

    [Test]
    public void 랩_허수아비_가상_시전자처럼_무기를_못_찾으면_대상_적중점()
    {
        InterruptHitLightFx fx = CreateFx();
        var player = new InterruptHitLightPlayer(() => CreateLook(false), () => fx, casterWeapon: _ => null);
        _players.Add(player);

        player.Begin(new InterruptHitLightRequest
        {
            Point = TargetPoint, CasterClientId = 99, Origin = InterruptHitLightOrigin.CasterWeapon, Look = InterruptSuccessLook.HitLight,
        }, 0f);

        Assert.AreEqual(TargetPoint, fx.transform.position);
    }

    [Test]
    public void 위치_대상이면_무기를_찾지도_않는다()
    {
        int asked = 0;
        var player = new InterruptHitLightPlayer(() => CreateLook(false), () => null, casterWeapon: _ =>
        {
            asked++;
            return WeaponPoint;
        });
        _players.Add(player);

        player.Begin(new InterruptHitLightRequest { Point = TargetPoint, Origin = InterruptHitLightOrigin.Target, Look = InterruptSuccessLook.HitLight }, 0f);

        Assert.AreEqual(0, asked);
        Assert.AreEqual(TargetPoint, player.FxPoint);
    }

    [Test]
    public void 단계_발생_위치_기본은_틈_수직_직렬화_값은_그대로()
    {
        Assert.AreEqual(0, (int)InterruptHitLightOrigin.CasterWeapon, "직렬화 값 — 순서 바꾸지 않는다");
        Assert.AreEqual(1, (int)InterruptHitLightOrigin.Target);
        Assert.AreEqual(2, (int)InterruptHitLightOrigin.Gap);
        Assert.AreEqual(InterruptHitLightOrigin.Gap, new InterruptTierProfile().hitLightOrigin);
        var go = new GameObject("Director");
        _objects.Add(go);
        var director = go.AddComponent<InterruptProductionDirector>();
        Assert.AreEqual(InterruptHitLightOrigin.Gap, director.TierProfile(InterruptPresentationTier.Normal).hitLightOrigin);
        Assert.AreEqual(InterruptHitLightOrigin.Gap, director.TierProfile(InterruptPresentationTier.Dash).hitLightOrigin);
    }

    [Test]
    public void NetworkManager_프리팹_단계_위치는_틈_수직()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2.Prefabs/Network/NetworkManager.prefab");
        Assert.IsNotNull(prefab);
        var director = prefab.GetComponentInChildren<InterruptProductionDirector>(true);
        Assert.IsNotNull(director);
        Assert.AreEqual(InterruptHitLightOrigin.Gap, director.TierProfile(InterruptPresentationTier.Normal).hitLightOrigin);
        Assert.AreEqual(InterruptHitLightOrigin.Gap, director.TierProfile(InterruptPresentationTier.Dash).hitLightOrigin);
    }

    // ---- 194 후속 — 틈 수직: 무기와 대상 표면 사이, 시전자→대상 축에 수직 ----

    private static readonly Vector3 TopDownView = new Vector3(0f, -0.8f, 0.6f); // 탑다운 카메라(앞으로 기울어짐)

    [Test]
    public void 틈_위치는_무기와_대상_표면의_중간점()
    {
        Vector3 surface = new Vector3(4f, 1.3f, 0.5f);
        InterruptHitLightPlacement p = InterruptHitLightRules.Place(InterruptHitLightOrigin.Gap, TargetPoint, WeaponPoint, null, surface, TopDownView);
        AssertVector((WeaponPoint + surface) * 0.5f, p.Position);
        Assert.IsTrue(p.HasDirection);
    }

    [Test]
    public void 틈_대체_무기_없으면_시전자_중심_표면_없으면_대상_시전자_없으면_대상_방향_없음()
    {
        Vector3 center = new Vector3(0f, 1f, 0f);
        InterruptHitLightPlacement noWeapon = InterruptHitLightRules.Place(InterruptHitLightOrigin.Gap, TargetPoint, null, center, null, TopDownView);
        AssertVector((center + TargetPoint) * 0.5f, noWeapon.Position, "무기 없음 = 시전자 중심, 표면 없음 = 대상 적중점");
        Assert.IsTrue(noWeapon.HasDirection);

        InterruptHitLightPlacement weaponWins = InterruptHitLightRules.Place(InterruptHitLightOrigin.Gap, TargetPoint, WeaponPoint, center, null, TopDownView);
        AssertVector((WeaponPoint + TargetPoint) * 0.5f, weaponWins.Position, "무기가 있으면 중심은 안 쓴다");

        InterruptHitLightPlacement noCaster = InterruptHitLightRules.Place(InterruptHitLightOrigin.Gap, TargetPoint, null, null, null, TopDownView);
        AssertVector(TargetPoint, noCaster.Position, "시전자 못 찾음 = 대상 적중점");
        Assert.IsFalse(noCaster.HasDirection, "방향 없음 = V1·V2 처음 모습");

        InterruptHitLightPlacement overlap = InterruptHitLightRules.Place(InterruptHitLightOrigin.Gap, TargetPoint, TargetPoint + Vector3.up, null, null, TopDownView);
        Assert.IsFalse(overlap.HasDirection, "시전자·대상이 바닥에서 겹치면 축 없음");

        Assert.IsFalse(InterruptHitLightRules.Place(InterruptHitLightOrigin.CasterWeapon, TargetPoint, WeaponPoint, center, null, TopDownView).HasDirection,
            "무기 모드 = 방향 없음");
        Assert.IsFalse(InterruptHitLightRules.Place(InterruptHitLightOrigin.Target, TargetPoint, WeaponPoint, center, null, TopDownView).HasDirection,
            "대상 모드 = 방향 없음");
    }

    [Test]
    public void 틈_방향은_바닥_평면에서_시전자_대상_축에_수직()
    {
        InterruptHitLightPlacement p = InterruptHitLightRules.Place(InterruptHitLightOrigin.Gap, TargetPoint, WeaponPoint, null, null, TopDownView);
        Vector3 expectedAxis = new Vector3(TargetPoint.x - WeaponPoint.x, 0f, TargetPoint.z - WeaponPoint.z).normalized;
        AssertVector(expectedAxis, p.Axis, "축 = 시전자 쪽 → 대상, 높이 무시");
        Assert.AreEqual(1f, p.Perpendicular.magnitude, 1e-4f);
        Assert.AreEqual(0f, p.Perpendicular.y, 1e-5f, "수직선은 바닥 평면");
        Assert.AreEqual(0f, Vector3.Dot(p.Perpendicular, p.Axis), 1e-5f, "축에 수직");
        Assert.AreEqual(1f, Mathf.Abs(InterruptHitLightRules.FlatPerpendicular(Vector3.back).x), 1e-5f, "축 = 앞뒤 → 수직선 = 좌우");
        Assert.AreEqual(Vector3.zero, InterruptHitLightRules.FlatPerpendicular(Vector3.up), "축 없음 = 0");
    }

    [Test]
    public void 카메라_기울기_면은_화면을_향하고_화면에서_수직선_각도로_보인다()
    {
        Vector3 view = TopDownView.normalized;
        Vector3 line = new Vector3(1f, 0f, 1f).normalized;
        Quaternion rotation = InterruptHitLightRules.ViewTilted(line, view);
        AssertVector(view, rotation * Vector3.forward, "로컬 Z = 시선(면이 카메라를 향한다)");
        Vector3 x = rotation * Vector3.right;
        Assert.AreEqual(0f, Vector3.Dot(x, view), 1e-5f, "로컬 X 는 화면 면 안");
        // 화면 면에 투영했을 때 같은 방향 = 화면에서 그 각도로 보인다.
        AssertVector(Vector3.ProjectOnPlane(line, view).normalized, x, "로컬 X = 수직선의 화면 투영");
        Assert.Greater(Vector3.Dot(x, line), 0.5f, "바닥 선을 카메라 쪽으로 기울인 것");

        Quaternion straightDown = InterruptHitLightRules.ViewTilted(Vector3.right, Vector3.down);
        AssertVector(Vector3.right, straightDown * Vector3.right, "수직 아래 시선 = 바닥 선 그대로");

        Quaternion parallel = InterruptHitLightRules.ViewTilted(Vector3.down, Vector3.down);
        Assert.AreEqual(0f, Vector3.Dot(parallel * Vector3.right, Vector3.down), 1e-5f, "선이 시선과 평행해도 화면 면 안(대체)");
        AssertVector(Vector3.down, InterruptHitLightRules.ViewTilted(line, Vector3.zero) * Vector3.forward, "시선 없음 = 수직 아래");
    }

    [Test]
    public void 틈_불꽃_방향은_수직선_양쪽_퍼짐_원뿔_안()
    {
        Vector3 perpendicular = Vector3.right;
        for (int i = 0; i <= 10; i++)
        {
            float u = i / 10f;
            Vector3 plus = InterruptHitLightRules.SparkDirection(perpendicular, true, 30f, u, 1f - u);
            Vector3 minus = InterruptHitLightRules.SparkDirection(perpendicular, false, 30f, 1f - u, u);
            Assert.LessOrEqual(Vector3.Angle(plus, perpendicular), 30.01f);
            Assert.LessOrEqual(Vector3.Angle(minus, -perpendicular), 30.01f);
            Assert.AreEqual(1f, plus.magnitude, 1e-4f);
        }

        AssertVector(perpendicular, InterruptHitLightRules.SparkDirection(perpendicular, true, 0f, 0.7f, 0.3f), "퍼짐 0 = 수직선");
    }

    [Test]
    public void 틈_모드는_무기를_찾고_없으면_시전자_중심_대상_표면을_묻고_한_번만_계산()
    {
        InterruptHitLightFx fx = CreateFx();
        var scene = new FakeScene { Center = new Vector3(0f, 1f, 0f), Surface = new Vector3(4.5f, 1.2f, 0f), View = TopDownView };
        var player = new InterruptHitLightPlayer(() => CreateLook(false), () => fx, casterWeapon: _ => null, scene: scene);
        _players.Add(player);

        player.Begin(new InterruptHitLightRequest
        {
            Point = TargetPoint, CasterClientId = 7, Origin = InterruptHitLightOrigin.Gap, Look = InterruptSuccessLook.HitLight,
        }, 0f);

        Assert.AreEqual(7ul, scene.AskedCaster);
        AssertVector(scene.Center, scene.AskedFrom, "대상 표면은 시전자 쪽 점에서 가장 가까운 곳");
        AssertVector((scene.Center + scene.Surface) * 0.5f, player.FxPoint);
        AssertVector(player.FxPoint, fx.transform.position);
        Assert.IsTrue(player.Placement.HasDirection);

        scene.Center = Vector3.zero;
        player.Tick(0.3f);
        Assert.AreEqual(1, scene.Calls, "성공 순간 한 번만");
    }

    [Test]
    public void 틈_재생은_빛줄기_가로빛줄을_기울이고_무기_모드는_프리팹_값으로_되돌린다()
    {
        InterruptHitLightFx fx = CreateFx();
        ParticleSystem rays = AddSystem(fx, InterruptHitLightFx.RaysName, ParticleSystemRenderMode.Mesh);
        ParticleSystem streak = AddSystem(fx, InterruptHitLightFx.StreakName, ParticleSystemRenderMode.Mesh);
        ParticleSystem sparks = AddSystem(fx, InterruptHitLightFx.SparksName, ParticleSystemRenderMode.Stretch);
        InterruptHitLightLook look = CreateLook(false);

        InterruptHitLightPlacement placement = InterruptHitLightRules.Place(InterruptHitLightOrigin.Gap, TargetPoint, WeaponPoint, null, null, TopDownView);
        fx.Play(placement, look);
        Assert.AreEqual(ParticleSystemRenderSpace.Local, rays.GetComponent<ParticleSystemRenderer>().alignment);
        Assert.AreEqual(ParticleSystemRenderSpace.Local, streak.GetComponent<ParticleSystemRenderer>().alignment);
        AssertVector(InterruptHitLightRules.ViewTilted(placement.Perpendicular, placement.ViewForward) * Vector3.right, rays.transform.right);
        AssertVector(InterruptHitLightRules.ViewTilted(placement.Perpendicular, placement.ViewForward) * Vector3.right, streak.transform.right,
            "가로 빛줄 기본 = 수직선");
        Assert.AreEqual(look.GapRayCount, rays.particleCount, "빛줄기 줄 수만큼 직접 방출");
        Assert.IsFalse(rays.emission.enabled);
        Assert.IsFalse(sparks.emission.enabled);
        Assert.Greater(sparks.particleCount, 0);

        fx.Play(WeaponPoint, look);
        Assert.AreEqual(ParticleSystemRenderSpace.View, rays.GetComponent<ParticleSystemRenderer>().alignment, "무기 모드 = 처음 모습");
        Assert.AreEqual(ParticleSystemRenderSpace.View, streak.GetComponent<ParticleSystemRenderer>().alignment);
        Assert.AreEqual(Quaternion.identity, rays.transform.localRotation);
        Assert.IsTrue(rays.emission.enabled);
        Assert.IsTrue(sparks.emission.enabled);
    }

    private ParticleSystem AddSystem(InterruptHitLightFx fx, string name, ParticleSystemRenderMode mode)
    {
        var go = new GameObject(name);
        go.transform.SetParent(fx.transform, false);
        ParticleSystem system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = system.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        system.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6) });
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = mode;
        renderer.alignment = ParticleSystemRenderSpace.View;
        return system;
    }

    private static void AssertVector(Vector3 expected, Vector3 actual, string message = null)
    {
        Assert.Less((expected - actual).magnitude, 1e-4f, $"{message} expected {expected} actual {actual}");
    }

    private sealed class FakeScene : IInterruptHitLightScene
    {
        public Vector3 Center;
        public Vector3 Surface;
        public Vector3 View;
        public ulong AskedCaster;
        public Vector3 AskedFrom;
        public int Calls;

        public bool TryGetCasterCenter(ulong casterClientId, out Vector3 center)
        {
            Calls++;
            AskedCaster = casterClientId;
            center = Center;
            return true;
        }

        public bool TryGetTargetSurface(Vector3 targetPoint, Vector3 from, out Vector3 surface)
        {
            AskedFrom = from;
            surface = Surface;
            return true;
        }

        public bool TryGetViewForward(out Vector3 forward)
        {
            forward = View;
            return true;
        }
    }

    [Test]
    public void 무기_기준점은_소켓_위치에_로컬_오프셋()
    {
        var go = new GameObject("Player");
        _objects.Add(go);
        var socket = new GameObject("R_WeaponSocket").transform;
        socket.SetParent(go.transform, false);
        socket.position = WeaponPoint;
        var anchor = go.AddComponent<PlayerWeaponAnchor>();
        Assert.IsFalse(anchor.TryGetRightWeaponPoint(out _), "칸 비면 없음");

        var so = new SerializedObject(anchor);
        so.FindProperty("rightWeapon").objectReferenceValue = socket;
        so.ApplyModifiedPropertiesWithoutUndo();
        Assert.IsTrue(anchor.TryGetRightWeaponPoint(out Vector3 point));
        Assert.AreEqual(WeaponPoint, point);

        so.FindProperty("localOffset").vector3Value = Vector3.up;
        so.ApplyModifiedPropertiesWithoutUndo();
        anchor.TryGetRightWeaponPoint(out point);
        Assert.AreEqual(WeaponPoint + Vector3.up, point);
    }

    [TestCase("Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab", "R_WeaponSocket")]
    [TestCase("Assets/2.Prefabs/Player/Gunner/Player_Gunner.prefab", "LaserGun")]
    [TestCase("Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab", "Dagger_Weapon_R")]
    public void 캐릭터_Variant_루트에_오른손_무기_기준점(string path, string weapon)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, path);
        var anchor = prefab.GetComponent<PlayerWeaponAnchor>();
        Assert.IsNotNull(anchor, "Variant 루트 PlayerWeaponAnchor");
        Assert.IsNotNull(anchor.RightWeapon);
        Assert.AreEqual(weapon, anchor.RightWeapon.name);
        Assert.IsTrue(anchor.RightWeapon.IsChildOf(prefab.transform.Find("Armature")), "애니메이션으로 움직이는 Armature 안 본");
    }

    // ---- 단계 6 — V2 화이트아웃 ----

    private sealed class FakeOverlay : IInterruptHitFlashOverlayOutput
    {
        public readonly List<InterruptHitFlashStage> Shown = new List<InterruptHitFlashStage>();
        public int Hides;

        public void Show(InterruptHitFlashStage stage, float progress, Vector3 worldCenter, Vector3 worldDirection) => Shown.Add(stage);
        public void Hide() => Hides++;
    }

    [Test]
    public void V2_화이트아웃은_본인_화면에_3프레임_뒤_숨긴다()
    {
        InterruptHitLightLook look = CreateLook(false);
        Assert.AreEqual(3, look.WhiteoutFrames, "기본 3프레임(Q12)");
        var overlay = new FakeOverlay();
        var player = new InterruptHitLightPlayer(() => look, () => null, null, null, overlay);
        _players.Add(player);

        player.Begin(new InterruptHitLightRequest { Look = InterruptSuccessLook.HitLightWhiteout, ViewerIsCaster = true }, 0f, 100);
        player.Tick(0.016f, 101);
        player.Tick(0.033f, 102);
        Assert.AreEqual(3, overlay.Shown.Count);
        Assert.IsTrue(overlay.Shown.TrueForAll(s => s == InterruptHitFlashStage.Whiteout), "③ 그림 재사용");
        Assert.IsTrue(player.WhiteoutShown);
        Assert.AreEqual(0, overlay.Hides);

        player.Tick(0.05f, 103);
        Assert.AreEqual(3, overlay.Shown.Count);
        Assert.AreEqual(1, overlay.Hides);
        Assert.IsFalse(player.WhiteoutShown);

        player.Tick(1f, 160);
        Assert.IsFalse(player.IsPlaying);
        Assert.AreEqual(1, overlay.Hides, "숨김은 한 번");
    }

    [TestCase(InterruptSuccessLook.HitLightWhiteout, false)] // 팀원 화면
    [TestCase(InterruptSuccessLook.HitLight, true)]          // V1
    public void 화이트아웃_없는_경우(InterruptSuccessLook successLook, bool viewerIsCaster)
    {
        InterruptHitLightLook look = CreateLook(false);
        var overlay = new FakeOverlay();
        var player = new InterruptHitLightPlayer(() => look, () => null, null, null, overlay);
        _players.Add(player);

        player.Begin(new InterruptHitLightRequest { Look = successLook, ViewerIsCaster = viewerIsCaster }, 0f, 1);
        player.Tick(0.016f, 2);

        Assert.AreEqual(0, overlay.Shown.Count);
        Assert.AreEqual(0, overlay.Hides);
    }

    [Test]
    public void 강제_리셋은_화이트아웃을_바로_숨긴다()
    {
        InterruptHitLightLook look = CreateLook(false);
        var overlay = new FakeOverlay();
        var player = new InterruptHitLightPlayer(() => look, () => null, null, null, overlay);
        _players.Add(player);
        player.Begin(new InterruptHitLightRequest { Look = InterruptSuccessLook.HitLightWhiteout, ViewerIsCaster = true }, 0f, 1);

        player.Stop();

        Assert.AreEqual(1, overlay.Hides);
        Assert.IsFalse(player.WhiteoutShown);
    }

    [Test]
    public void 화이트아웃_전용_칸은_3번만_켜고_재질은_디렉터_값()
    {
        var go = new GameObject("Director");
        _objects.Add(go);
        var director = go.AddComponent<InterruptProductionDirector>();
        var settings = new InterruptHitLightWhiteoutSettings(director);

        Assert.IsTrue(settings.IsEffectOn(InterruptPresentationEffect.Whiteout), "V0 ③ 칸이 꺼져 있어도 그린다");
        Assert.IsFalse(settings.IsEffectOn(InterruptPresentationEffect.Starburst));
        Assert.IsFalse(settings.IsEffectOn(InterruptPresentationEffect.Smear));
        Assert.IsTrue(InterruptPresentationEffectRules.StageVisible(settings, InterruptHitFlashStage.Whiteout));
        Assert.AreEqual(director.HitFlashBlurStrength, settings.HitFlashBlurStrength);
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

    // ---- 단계 5 — 빛줄기 ----

    [Test]
    public void 프리팹_빛줄기는_날개_메시_에셋_카메라_정렬_회전_무작위()
    {
        ParticleSystem rays = PrefabSystem("Rays");
        var renderer = rays.GetComponent<ParticleSystemRenderer>();
        Assert.AreEqual(ParticleSystemRenderMode.Mesh, renderer.renderMode);
        Assert.AreEqual(ParticleSystemRenderSpace.View, renderer.alignment, "카메라 정렬");
        Assert.AreEqual(InterruptHitLightAuthoring.RaysMeshPath, AssetDatabase.GetAssetPath(renderer.mesh), "빛줄기 메시는 에셋");
        Assert.AreEqual("BeaverLobby/VFX/InterruptHitLightRays", renderer.sharedMaterial.shader.name);
        Assert.AreNotEqual(rays.main.startRotation.constantMin, rays.main.startRotation.constantMax, "회전 무작위");
        Assert.IsTrue(rays.sizeOverLifetime.enabled, "길이 시간 변화");
        Assert.IsTrue(rays.colorOverLifetime.enabled, "알파 시간 변화");
    }

    [Test]
    public void 빛줄기_메시는_날개_8에서_12개()
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(InterruptHitLightAuthoring.RaysMeshPath);
        Assert.IsNotNull(mesh);
        int blades = mesh.vertexCount / 4;
        Assert.That(blades, Is.InRange(8, 12));
        Assert.AreEqual(blades * 6, mesh.triangles.Length);
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
        Assert.AreEqual(InterruptSuccessLook.HitLightReference, director.SuccessLook(InterruptPresentationTier.Normal), "기본 일반 = V3(11-03)");
        Assert.AreEqual(InterruptSuccessLook.HitLightReference, director.SuccessLook(InterruptPresentationTier.Dash), "기본 대쉬 = V3(11-03)");
    }

    // ---- 194 후속 — V3 레퍼런스 맞춤 ----

    [TestCase(true, true, InterruptSuccessLook.HitLightReference)]
    [TestCase(true, false, InterruptSuccessLook.HitLightReference)]
    [TestCase(false, true, InterruptSuccessLook.HitFlash)] // V1·V2 데이터만 있으면 V3 대신 V0
    [TestCase(false, false, InterruptSuccessLook.HitFlash)]
    public void V3_선택표는_V3_전용_데이터가_있을_때만(bool hasReference, bool hasHitLight, InterruptSuccessLook expected)
    {
        var tier = new InterruptTierProfile(false, InterruptTierFlash.HitLightReference);
        Assert.AreEqual(expected, InterruptPresentationTierRules.SelectSuccessLook(tier, true, true, hasHitLight, hasReference));
    }

    [Test]
    public void V3_는_적중_라이트_경로_화이트아웃_없음_암순응_기본_나()
    {
        Assert.IsTrue(InterruptPresentationTierRules.UsesHitLight(InterruptSuccessLook.HitLightReference));
        Assert.IsTrue(InterruptPresentationTierRules.UsesReferenceLook(InterruptSuccessLook.HitLightReference));
        Assert.IsFalse(InterruptPresentationTierRules.UsesReferenceLook(InterruptSuccessLook.HitLight));
        Assert.IsFalse(InterruptPresentationTierRules.UsesReferenceLook(InterruptSuccessLook.HitLightWhiteout));
        Assert.AreEqual(0, InterruptPresentationTierRules.WhiteoutFrames(InterruptSuccessLook.HitLightReference, true, 3));
        Assert.AreEqual(InterruptDarkAdaptMode.SceneLight, InterruptPresentationTierRules.DefaultDarkAdapt(InterruptTierFlash.HitLightReference));
        Assert.IsNull(InterruptPresentationTierRules.DefaultDarkAdapt(InterruptTierFlash.HitLight));
        Assert.AreEqual(4, (int)InterruptTierFlash.HitLightReference, "직렬화 값 — 순서 고정");
        Assert.AreEqual(4, (int)InterruptSuccessLook.HitLightReference);
        // V1 선택은 V3 칸 유무와 무관하다.
        var v1 = new InterruptTierProfile(false, InterruptTierFlash.HitLight);
        Assert.AreEqual(InterruptSuccessLook.HitLight, InterruptPresentationTierRules.SelectSuccessLook(v1, true, true, true, false));
    }

    [Test]
    public void 디렉터_V3_는_전용_SO_프리팹을_쓰고_V1_V2_칸과_독립()
    {
        var go = new GameObject("Director");
        _objects.Add(go);
        var director = go.AddComponent<InterruptProductionDirector>();
        InterruptHitLightLook v1Look = CreateLook(false);
        InterruptHitLightLook v3Look = CreateLook(false);
        var so = new SerializedObject(director);
        so.FindProperty("hitLightLook").objectReferenceValue = v1Look;
        so.FindProperty("hitLightVfx").objectReferenceValue = CreateFx();
        so.ApplyModifiedPropertiesWithoutUndo();
        director.TierProfile(InterruptPresentationTier.Normal).flash = InterruptTierFlash.HitLightReference;
        Assert.AreEqual(InterruptSuccessLook.HitFlash, director.SuccessLook(InterruptPresentationTier.Normal), "V3 칸 비면 V0");
        Assert.AreEqual(InterruptSuccessLook.HitFlash, director.SuccessLook(InterruptPresentationTier.Dash), "대쉬 기본 V3 — V3 칸 비면 V0(V1·V2 칸으로 대신하지 않음)");

        so.Update(); // 위에서 런타임으로 바꾼 단계 값을 읽어 둔다(Apply 가 되돌리지 않게)
        so.FindProperty("hitLightLookReference").objectReferenceValue = v3Look;
        so.FindProperty("hitLightVfxReference").objectReferenceValue = CreateFx();
        so.ApplyModifiedPropertiesWithoutUndo();
        Assert.AreEqual(InterruptSuccessLook.HitLightReference, director.SuccessLook(InterruptPresentationTier.Normal));
        Assert.AreSame(v3Look, director.HitLightLookFor(InterruptSuccessLook.HitLightReference));
        Assert.AreSame(v1Look, director.HitLightLookFor(InterruptSuccessLook.HitLight));
        Assert.AreSame(v1Look, director.HitLightLookFor(InterruptSuccessLook.HitLightWhiteout));
    }

    [Test]
    public void 진행기는_요청_종류로_데이터_이펙트를_고르고_바뀌면_앞_이펙트를_끈다()
    {
        InterruptHitLightLook v1Look = CreateLook(false);
        InterruptHitLightLook v3Look = CreateLook(false);
        var so = new SerializedObject(v3Look);
        so.FindProperty("lightIntensity").floatValue = 4f;
        so.ApplyModifiedPropertiesWithoutUndo();
        InterruptHitLightFx v1Fx = CreateFx();
        InterruptHitLightFx v3Fx = CreateFx();
        var player = new InterruptHitLightPlayer(
            look => InterruptPresentationTierRules.UsesReferenceLook(look) ? v3Look : v1Look,
            look => InterruptPresentationTierRules.UsesReferenceLook(look) ? v3Fx : v1Fx);
        _players.Add(player);

        player.Begin(new InterruptHitLightRequest { Look = InterruptSuccessLook.HitLight }, 0f);
        Assert.AreEqual(20f, v1Fx.HitLight.intensity, 1e-4f);

        player.Begin(new InterruptHitLightRequest { Look = InterruptSuccessLook.HitLightReference }, 1f);
        Assert.AreEqual(4f, v3Fx.HitLight.intensity, 1e-4f, "V3 데이터");
        Assert.IsFalse(v1Fx.HitLight.enabled, "버전이 바뀌면 앞 인스턴스 라이트를 끈다");

        player.Stop();
        Assert.IsFalse(v3Fx.HitLight.enabled, "강제 리셋은 마지막으로 튼 인스턴스");
    }

    private InterruptHitLightLook CreateGradeLook()
    {
        InterruptHitLightLook look = CreateLook(false);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _objects.Add(profile);
        var so = new SerializedObject(look);
        so.FindProperty("gradeProfile").objectReferenceValue = profile;
        so.ApplyModifiedPropertiesWithoutUndo();
        return look;
    }

    [Test]
    public void 채도_Volume_가중치는_적중_순간_1에서_곡선대로_0()
    {
        InterruptHitLightLook look = CreateGradeLook();
        Assert.AreEqual(1f, InterruptHitLightRules.GradeWeight(look, 0f), 1e-4f);
        float mid = InterruptHitLightRules.GradeWeight(look, 0.45f);
        Assert.Greater(mid, 0f);
        Assert.Less(mid, 1f);
        Assert.AreEqual(0f, InterruptHitLightRules.GradeWeight(look, 0.9f));
        Assert.AreEqual(0f, InterruptHitLightRules.GradeWeight(CreateLook(false), 0f), "프로필 없음(V1·V2) = 0");
        Assert.AreEqual(0f, InterruptHitLightRules.GradeWeight(null, 0f));

        InterruptHitLightPlayer player = CreatePlayer(look, null);
        player.Begin(new InterruptHitLightRequest { Look = InterruptSuccessLook.HitLightReference }, 10f);
        Assert.AreEqual(1f, player.GradeWeight, 1e-4f);
        Assert.AreEqual(0f, player.BloomWeight, "블룸 프로필 없음 = 블룸 없음(서로 독립)");
        player.Tick(10.45f);
        Assert.AreEqual(mid, player.GradeWeight, 1e-4f);
        player.Tick(10.95f);
        Assert.AreEqual(0f, player.GradeWeight);
        Assert.IsFalse(player.IsPlaying);
    }

    [Test]
    public void 색_보정_길이는_프로필이_있을_때만_진행기_길이에_든다()
    {
        InterruptHitLightLook look = CreateLook(false);
        var so = new SerializedObject(look);
        so.FindProperty("gradeDuration").floatValue = 2f;
        so.ApplyModifiedPropertiesWithoutUndo();
        Assert.AreEqual(0.9f, InterruptHitLightRules.Duration(look), 1e-6f, "프로필 없음 = V1·V2 길이 그대로");

        so.FindProperty("gradeProfile").objectReferenceValue = ScriptableObject.CreateInstance<VolumeProfile>();
        _objects.Add(so.FindProperty("gradeProfile").objectReferenceValue);
        so.ApplyModifiedPropertiesWithoutUndo();
        Assert.AreEqual(2f, InterruptHitLightRules.Duration(look), 1e-6f);
    }

    [Test]
    public void V3_에셋은_V1_V2_에셋과_값이_독립()
    {
        var v1 = AssetDatabase.LoadAssetAtPath<InterruptHitLightLook>(InterruptHitLightAuthoring.LookPath);
        var v3 = AssetDatabase.LoadAssetAtPath<InterruptHitLightLook>(InterruptHitLightReferenceAuthoring.LookPath);
        Assert.IsNotNull(v1);
        Assert.IsNotNull(v3);
        Assert.AreNotSame(v1, v3);

        // V1·V2 값 보존(버전 보존 원칙).
        Assert.AreEqual(20f, v1.LightIntensity, 1e-4f);
        Assert.AreEqual(8f, v1.LightRange, 1e-4f);
        Assert.AreEqual(0.85f, v1.SunDimDepth, 1e-4f);
        Assert.AreEqual(0.7f, v1.AmbientDimDepth, 1e-4f);
        Assert.AreEqual(3, v1.WhiteoutFrames);
        Assert.IsNull(v1.GradeProfile, "V1·V2 는 색 보정 없음");

        // V3 시작값.
        Assert.AreEqual(2f, v3.LightIntensity, 1e-4f);
        Assert.AreEqual(3f, v3.LightRange, 1e-4f);
        Assert.Greater(v3.SunDimDepth, v1.SunDimDepth, "V3 가 더 어둡다");
        Assert.Greater(v3.AmbientDimDepth, v1.AmbientDimDepth);
        Assert.AreEqual(0, v3.WhiteoutFrames);
        Assert.AreNotSame(v1.BloomProfile, v3.BloomProfile, "V3 전용 블룸");

        Assert.IsTrue(v3.BloomProfile.TryGet(out UnityEngine.Rendering.Universal.Bloom bloom));
        Assert.AreEqual(2f, bloom.threshold.value, 1e-4f);
        Assert.AreEqual(0.6f, bloom.intensity.value, 1e-4f);
        Assert.IsNotNull(v3.GradeProfile);
        Assert.IsTrue(v3.GradeProfile.TryGet(out UnityEngine.Rendering.Universal.ColorAdjustments color));
        Assert.IsTrue(color.saturation.overrideState);
        Assert.Less(color.saturation.value, 0f, "채도 낮춤");
        Assert.Greater(color.colorFilter.value.b, color.colorFilter.value.g, "보랏빛 색 필터");
    }

    [Test]
    public void V3_프리팹은_중심_점_가로_빛줄_가는_불꽃_메시는_에셋()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(InterruptHitLightReferenceAuthoring.PrefabPath);
        Assert.IsNotNull(prefab, InterruptHitLightReferenceAuthoring.PrefabPath);
        Assert.IsNotNull(prefab.GetComponent<InterruptHitLightFx>());

        foreach ((string child, string meshPath) in new[]
                 {
                     ("Core", InterruptHitLightReferenceAuthoring.CoreMeshPath),
                     ("Streak", InterruptHitLightReferenceAuthoring.StreakMeshPath),
                 })
        {
            Transform t = prefab.transform.Find(child);
            Assert.IsNotNull(t, child);
            Assert.IsFalse(t.GetComponent<ParticleSystem>().main.playOnAwake, child);
            var renderer = t.GetComponent<ParticleSystemRenderer>();
            Assert.AreEqual(ParticleSystemRenderMode.Mesh, renderer.renderMode, child);
            Assert.AreEqual(ParticleSystemRenderSpace.View, renderer.alignment, child + " 카메라 정렬");
            Assert.AreEqual(meshPath, AssetDatabase.GetAssetPath(renderer.mesh), child + " 메시는 에셋");
            Assert.AreEqual("BeaverLobby/VFX/InterruptHitLightRays", renderer.sharedMaterial.shader.name, child + " 셰이더 재사용");
        }

        Assert.AreEqual(0f, prefab.transform.Find("Streak").GetComponent<ParticleSystem>().main.startRotation.constant, "빛줄은 늘 화면 가로");

        ParticleSystem v1Sparks = PrefabSystem("Sparks");
        ParticleSystem v3Sparks = prefab.transform.Find("Sparks").GetComponent<ParticleSystem>();
        Assert.Less(v3Sparks.main.startSize.constantMax, v1Sparks.main.startSize.constantMin, "더 가늘게");
        Assert.Greater(v3Sparks.GetComponent<ParticleSystemRenderer>().lengthScale, v1Sparks.GetComponent<ParticleSystemRenderer>().lengthScale, "더 길게");
        Assert.Greater(v3Sparks.emission.GetBurst(0).minCount, v1Sparks.emission.GetBurst(0).maxCount, "더 많이");

        ParticleSystem v1Rays = PrefabSystem("Rays");
        ParticleSystem v3Rays = prefab.transform.Find("Rays").GetComponent<ParticleSystem>();
        Assert.Less(v3Rays.main.startSize.constantMax, v1Rays.main.startSize.constantMin, "빛줄기는 보조로 작게");
        Assert.Less(v3Rays.main.startColor.color.a, 1f, "알파 낮춤");
    }

    [Test]
    public void NetworkManager_프리팹_디렉터에_V3_가_이어져_있고_일반_대쉬_제압_기본이_V3()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2.Prefabs/Network/NetworkManager.prefab");
        var director = prefab.GetComponent<InterruptProductionDirector>();
        var so = new SerializedObject(director);
        Assert.AreEqual(InterruptHitLightReferenceAuthoring.LookPath,
            AssetDatabase.GetAssetPath(so.FindProperty("hitLightLookReference").objectReferenceValue));
        Assert.AreEqual(InterruptHitLightReferenceAuthoring.PrefabPath,
            AssetDatabase.GetAssetPath(so.FindProperty("hitLightVfxReference").objectReferenceValue));
        Assert.AreEqual(InterruptTierFlash.HitLightReference, director.TierProfile(InterruptPresentationTier.Normal).flash, "기본 일반 = V3(11-03 사용자 결정)");
        Assert.AreEqual(InterruptTierFlash.HitLightReference, director.TierProfile(InterruptPresentationTier.Dash).flash, "기본 대쉬 = V3");
        Assert.AreEqual(InterruptDarkAdaptMode.SceneLight, director.TierProfile(InterruptPresentationTier.Normal).darkAdapt, "V3 암순응 = 나");
        Assert.AreEqual(InterruptDarkAdaptMode.SceneLight, director.TierProfile(InterruptPresentationTier.Dash).darkAdapt, "V3 암순응 = 나");

        // 제압 = 섬광 시퀀스 그대로 + V3 적중 라이트를 더한다(대체 아님).
        Assert.AreEqual(InterruptTierFlash.SuppressSequence, director.TierProfile(InterruptPresentationTier.Suppress).flash);
        Assert.AreEqual(InterruptSuccessLook.SuppressSequence, director.SuccessLook(InterruptPresentationTier.Suppress), "제압 시퀀스 유지");
        Assert.AreEqual(InterruptSuccessLook.HitLightReference, director.SequenceHitLight(InterruptPresentationTier.Suppress), "제압 + V3");
        Assert.AreEqual(InterruptSuccessLook.HitFlash, director.SequenceHitLight(InterruptPresentationTier.Normal), "일반은 더하지 않음(본 섬광이 V3)");
        Assert.AreEqual(InterruptSuccessLook.HitFlash, director.SequenceHitLight(InterruptPresentationTier.Fail), "실패 = 연출 없음");
    }

    [Test]
    public void 디렉터_제압_V3_는_시퀀스가_틀고_V3_데이터가_있을_때만()
    {
        var go = new GameObject("Director");
        _objects.Add(go);
        var director = go.AddComponent<InterruptProductionDirector>();
        var sequence = ScriptableObject.CreateInstance<SuppressFlashSequence>();
        _objects.Add(sequence);
        var so = new SerializedObject(director);
        so.FindProperty("suppressFlashSequence").objectReferenceValue = sequence;
        so.ApplyModifiedPropertiesWithoutUndo();
        Assert.AreEqual(InterruptSuccessLook.HitFlash, director.SequenceHitLight(InterruptPresentationTier.Suppress), "V3 칸 비면 더하지 않음");

        so.FindProperty("hitLightLookReference").objectReferenceValue = CreateLook(false);
        so.FindProperty("hitLightVfxReference").objectReferenceValue = CreateFx();
        so.ApplyModifiedPropertiesWithoutUndo();
        Assert.AreEqual(InterruptSuccessLook.SuppressSequence, director.SuccessLook(InterruptPresentationTier.Suppress));
        Assert.AreEqual(InterruptSuccessLook.HitLightReference, director.SequenceHitLight(InterruptPresentationTier.Suppress));
        Assert.AreEqual(InterruptSuccessLook.HitLightReference, director.SuccessLook(InterruptPresentationTier.Normal), "코드 기본 일반 = V3");
        Assert.AreEqual(InterruptSuccessLook.HitLightReference, director.SuccessLook(InterruptPresentationTier.Dash), "코드 기본 대쉬 = V3");

        director.SuppressFlashEnabled = false;
        Assert.AreEqual(InterruptSuccessLook.HitFlash, director.SequenceHitLight(InterruptPresentationTier.Suppress), "A6 끔 = 시퀀스 없음 → 더하지 않음(②~⑥)");
    }
}
