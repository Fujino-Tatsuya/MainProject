using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// 제압 간파 렌즈 고스트(할 일 270) — 에셋(원·육각형 5개, 광원 쪽 없음)·제압 단계 SO 에만 연결·진행기가 켜고 서서히 끔·고스트 렌더 모드.
/// 문서: Docs/tech/interrupt-presentation.md §9.11.
/// </summary>
public sealed class InterruptSuppressGhostTests
{
    private const string NetworkManagerPrefabPath = "Assets/2.Prefabs/Network/NetworkManager.prefab";
    private const string StarburstPath = "Assets/5.VFX/Interrupt/HitFlash/LF_InterruptHitFlashStarburst.asset";

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
        DarkAdaptationState.HitLightGhostFlare = false;
    }

    // ---- 에셋 ----

    [Test]
    public void 고스트_에셋은_부드러운_원_여러개_크기섞임_가산_광원쪽_없음()
    {
        var ghost = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(InterruptSuppressGhostAuthoring.GhostPath);
        Assert.IsNotNull(ghost, InterruptSuppressGhostAuthoring.GhostPath);
        Assert.GreaterOrEqual(ghost.elements.Length, 8, "상 여러 개가 줄지어");

        float minSize = float.MaxValue, maxSize = 0f;
        var positions = new HashSet<float>();
        foreach (LensFlareDataElementSRP e in ghost.elements)
        {
            Assert.IsTrue(e.visible);
            Assert.AreEqual(SRPLensFlareBlendMode.Additive, e.blendMode, "가산");
            Assert.AreEqual(SRPLensFlareType.Circle, e.flareType, "부드러운 원(보케)");
            Assert.Greater(e.position, 0.3f, "광원 쪽 빛 없음(상만)");
            Assert.LessOrEqual(e.localIntensity, 0.5f, "은은하게(낮은 세기)");
            positions.Add(e.position);
            minSize = Mathf.Min(minSize, e.uniformScale);
            maxSize = Mathf.Max(maxSize, e.uniformScale);
        }

        Assert.Greater(maxSize, minSize * 4f, "작은 점과 큰 원판이 섞임");
        Assert.AreEqual(ghost.elements.Length, positions.Count, "위치가 다 다르다");
        Assert.That(positions, Has.Some.GreaterThan(1f), "화면 중심 너머 반대편 포함");
    }

    [Test]
    public void 고스트는_제압_단계_SO_에만_있고_스타버스트와_별개()
    {
        var ghost = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(InterruptSuppressGhostAuthoring.GhostPath);
        var normal = AssetDatabase.LoadAssetAtPath<InterruptHitLightLook>(InterruptHitLightReferenceAuthoring.LookPath);
        var dash = AssetDatabase.LoadAssetAtPath<InterruptHitLightLook>(InterruptHitLightTierAuthoring.DashLookPath);
        var suppress = AssetDatabase.LoadAssetAtPath<InterruptHitLightLook>(InterruptHitLightTierAuthoring.SuppressLookPath);
        Assert.AreSame(ghost, suppress.GhostFlare, "제압 SO ghostFlare");
        Assert.IsNull(normal.GhostFlare, "일반 변화 없음");
        Assert.IsNull(dash.GhostFlare, "대쉬 변화 없음");
        Assert.AreNotSame(AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(StarburstPath), ghost);
        Assert.Greater(suppress.GhostDuration, 0f);
        Assert.Greater(suppress.GhostIntensity, 0f);
    }

    [Test]
    public void NetworkManager_디렉터_제압_단계만_고스트_SO_를_고른다()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPrefabPath);
        var director = prefab.GetComponent<InterruptProductionDirector>();
        Assert.IsNotNull(director.HitLightLookFor(InterruptSuccessLook.HitLightReference, InterruptPresentationTier.Suppress).GhostFlare);
        Assert.IsNull(director.HitLightLookFor(InterruptSuccessLook.HitLightReference, InterruptPresentationTier.Normal).GhostFlare);
        Assert.IsNull(director.HitLightLookFor(InterruptSuccessLook.HitLightReference, InterruptPresentationTier.Dash).GhostFlare);
    }

    // ---- 규칙·진행기 ----

    private InterruptHitLightLook CreateLook(LensFlareDataSRP ghost, float duration = 2f)
    {
        var look = ScriptableObject.CreateInstance<InterruptHitLightLook>();
        _objects.Add(look);
        var so = new SerializedObject(look);
        so.FindProperty("ghostFlare").objectReferenceValue = ghost;
        so.FindProperty("ghostDuration").floatValue = duration;
        so.FindProperty("ghostIntensity").floatValue = 1f;
        so.ApplyModifiedPropertiesWithoutUndo();
        return look;
    }

    private LensFlareDataSRP CreateFlare()
    {
        var flare = ScriptableObject.CreateInstance<LensFlareDataSRP>();
        flare.elements = new LensFlareDataElementSRP[0];
        _objects.Add(flare);
        return flare;
    }

    [Test]
    public void 고스트_세기는_모양이_있을_때만_곡선대로_줄어_끝에_0()
    {
        InterruptHitLightLook withGhost = CreateLook(CreateFlare(), 2f);
        InterruptHitLightLook without = CreateLook(null, 2f);
        Assert.AreEqual(1f, InterruptHitLightRules.GhostAmount(withGhost, 0f), 1e-4f);
        float mid = InterruptHitLightRules.GhostAmount(withGhost, 1f);
        Assert.That(mid, Is.GreaterThan(0f).And.LessThan(1f), "서서히");
        Assert.AreEqual(0f, InterruptHitLightRules.GhostAmount(withGhost, 2f));
        Assert.AreEqual(0f, InterruptHitLightRules.GhostAmount(without, 0f), "모양 없으면 없음");
        Assert.GreaterOrEqual(InterruptHitLightRules.Duration(withGhost), 2f, "진행기 길이에 고스트 포함");
    }

    [Test]
    public void 진행기는_고스트_모양이_있는_단계에서만_켜고_끝나면_끈다()
    {
        LensFlareDataSRP flare = CreateFlare();
        var assets = new Assets();
        assets.Looks[InterruptPresentationTier.Normal] = CreateLook(null, 2f);
        assets.Looks[InterruptPresentationTier.Suppress] = CreateLook(flare, 2f);
        var player = new InterruptHitLightPlayer(assets);
        _players.Add(player);

        var point = new Vector3(1f, 2f, 3f);
        player.Begin(new InterruptHitLightRequest { Point = point, Origin = InterruptHitLightOrigin.Target, Tier = InterruptPresentationTier.Normal }, 0f);
        Assert.AreEqual(0f, player.GhostIntensity, "일반 = 고스트 없음");
        Assert.IsFalse(DarkAdaptationState.HitLightGhostFlare);

        player.Begin(new InterruptHitLightRequest { Point = point, Origin = InterruptHitLightOrigin.Target, Tier = InterruptPresentationTier.Suppress }, 10f);
        Assert.Greater(player.GhostIntensity, 0f, "제압 = 적중 순간 켬");
        Assert.IsTrue(DarkAdaptationState.HitLightGhostFlare, "고스트 렌더 패스 켬");
        Assert.AreSame(flare, player.Ghost.lensFlareData);
        Assert.AreEqual(point, player.Ghost.transform.position, "광원 = 적중 라이트 위치");

        player.Tick(11f);
        Assert.That(player.GhostIntensity, Is.GreaterThan(0f).And.LessThan(1f), "서서히 사라짐");

        player.Tick(12.5f);
        Assert.AreEqual(0f, player.GhostIntensity, "끝 = 끔");
        Assert.IsFalse(DarkAdaptationState.HitLightGhostFlare);

        player.Begin(new InterruptHitLightRequest { Point = point, Origin = InterruptHitLightOrigin.Target, Tier = InterruptPresentationTier.Suppress }, 20f);
        player.Stop();
        Assert.AreEqual(0f, player.GhostIntensity, "강제 리셋 = 즉시 끔");
        Assert.IsFalse(DarkAdaptationState.HitLightGhostFlare);
    }

    [Test]
    public void 고스트_렌더_모드는_켜졌을_때만()
    {
        Assert.AreEqual(DarkAdaptationMode.None, DarkAdaptationModes.Select(0f, 0f, false, 0f, false, false));
        Assert.AreEqual(DarkAdaptationMode.GhostFlare, DarkAdaptationModes.Select(0f, 0f, false, 0f, false, true));
    }

    private sealed class Assets : IInterruptHitLightAssets
    {
        public readonly Dictionary<InterruptPresentationTier, InterruptHitLightLook> Looks = new Dictionary<InterruptPresentationTier, InterruptHitLightLook>();

        public InterruptHitLightLook LookFor(in InterruptHitLightRequest request) =>
            Looks.TryGetValue(request.Tier, out InterruptHitLightLook look) ? look : null;

        public InterruptHitLightFx FxFor(in InterruptHitLightRequest request) => null;

        public VolumeProfile GradeFor(in InterruptHitLightRequest request) => null;
    }
}
