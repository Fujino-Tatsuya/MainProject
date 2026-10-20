using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 할 일 57 R(D) — <b>실제 SG_EnemyC3Decal</b> 을 URP 데칼 경로로 찍어 비교한다. S2·S4a 의 픽셀 비교는 Hidden/EnemyC3DecalCompare 로
/// 같은 hlsl 함수를 부를 뿐이라, 그래프에서 Custom Function 출력이 BaseColor/Alpha/NormalAlpha 에 안 이어지거나 뒤바뀌거나
/// Decal SubTarget·패스 설정이 갈라도 통과했다. 여기서는 EditMode 프리뷰 씬에 Lit 바닥 + DecalProjector + 위에서 내려다보는
/// 정사영 카메라를 두고 프로젝트 렌더러(PC_Renderer, Decal 기능)로 RT 에 찍는다.
///   기준 = 같은 바닥·같은 프로젝터에 <b>기존 텍스처 데칼</b>(MA_TelegraphDecal = SG_TelegraphDecal, Base_Map = 예전 CPU 텍스처)을 찍은 것.
///   새것 = MA_EnemyC3Decal 인스턴스(EnemyC3Visuals.NewDecalInstance) + 런타임과 같은 값 함수.
/// 카메라는 프로젝터를 RT(384²)에 꼭 맞게 찍는다 — 픽셀 = 예전 텍스처 텍셀(S2 비교와 같은 조건).
/// 패스: 프로젝트 설정(Automatic → Windows 데스크톱 = DBuffer)은 반드시, ScreenSpace 는 기능 설정을 메모리에서만 잠깐 바꿔
/// 찍고 되돌린다(애셋 저장 없음). 외곽선(S2)과 채움 모드(S4a·S4c1)는 같은 그래프라 대표 모양만 찍는다.
/// 기준 = 채널 오차 ≤ 2/255 인 픽셀 99% 이상 + 데칼이 실제로 보인다(맨바닥과 다른 픽셀 ≥ 1%).
/// </summary>
public sealed class EnemyC3DecalRenderTests
{
    const int N = 384;
    const int Tolerance = 2;
    const float RequiredRatio = 0.99f;

    public struct Case
    {
        public string label; public int kind;   // 0 외곽선 · 3 부채꼴 채움 · 4 띠 채움 · 5 합집합 채움 · 6 원판
        public string id; public Vector2 extent; public float coneRadius, coneCenter, halfAngle, pathWidth, pathLength, inner, front, fill, rim;
        public override string ToString() => label;
    }

    static IEnumerable<Case> Shapes()
    {
        // 외곽선 — 띠(01) · 부채꼴(03) · 합집합(02).
        yield return new Case { label = "outline_01_band_3x12", kind = 0, id = "01", extent = new Vector2(3, 12), pathWidth = 3, pathLength = 12 };
        yield return new Case { label = "outline_03_cone_r6_a180", kind = 0, id = "03", extent = new Vector2(12, 12), coneRadius = 6, halfAngle = 90 };
        yield return new Case
        {
            label = "outline_02_union_p3x6_r3", kind = 0, id = "02", extent = new Vector2(6, 9),
            coneRadius = 3, coneCenter = 6, halfAngle = 180, pathWidth = 3, pathLength = 6
        };
        // 채움 — 부채꼴 · 잡기 고리 · 띠 · 합집합(중간 단계) · 장판 원판(S4c1).
        yield return new Case { label = "fill_arc_a180", kind = 3, extent = new Vector2(8, 8), halfAngle = 90 };
        yield return new Case { label = "fill_ring_a360_half", kind = 3, extent = new Vector2(8, 8), halfAngle = 180, inner = 0.5f };
        yield return new Case { label = "fill_band_2x10", kind = 4, extent = new Vector2(2, 10) };
        yield return new Case
        {
            label = "fill_union_p2x18_r2.5_s12", kind = 5, extent = new Vector2(5, 20.5f),
            pathWidth = 2, pathLength = 18, coneCenter = 18, coneRadius = 2.5f, front = 20.5f * 12 / 24
        };
        yield return new Case { label = "fill_disc", kind = 6, extent = new Vector2(6, 6), fill = 0.35f, rim = 0.12f };
    }

    static IEnumerable<TestCaseData> DBufferCases() => Shapes().Select(s => new TestCaseData(s).SetName("Render_DBuffer_" + s.label));
    // ScreenSpace 는 대표 둘(외곽선 하나·채움 하나)만.
    static IEnumerable<TestCaseData> ScreenSpaceCases() => Shapes().Where(s => s.label == "outline_02_union_p3x6_r3" || s.label == "fill_union_p2x18_r2.5_s12")
        .Select(s => new TestCaseData(s).SetName("Render_ScreenSpace_" + s.label));

    Scene _scene;
    Camera _cam;
    RenderTexture _rt;
    DecalProjector _projector;
    Material _floorMat;
    bool _async;

    [OneTimeSetUp]
    public void SetUp()
    {
        string dir = ArtifactDir();
        if (dir != null && File.Exists(Path.Combine(dir, "results.tsv"))) File.Delete(Path.Combine(dir, "results.tsv"));
        _async = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;   // 첫 사용 변형이 자리표시 셰이더로 찍히지 않게

        _scene = EditorSceneManager.NewPreviewScene();
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        SceneManager.MoveGameObjectToScene(floor, _scene);
        floor.transform.localScale = new Vector3(10, 1, 10);
        _floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(.5f, .5f, .5f) };
        floor.GetComponent<Renderer>().sharedMaterial = _floorMat;

        var light = new GameObject("Light");
        SceneManager.MoveGameObjectToScene(light, _scene);
        light.AddComponent<Light>().type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(90, 0, 0);

        var cam = new GameObject("Camera");
        SceneManager.MoveGameObjectToScene(cam, _scene);
        _cam = cam.AddComponent<Camera>();
        _cam.scene = _scene;
        _cam.orthographic = true;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = Color.black;
        _cam.nearClipPlane = .1f;
        _cam.farClipPlane = 50;
        cam.transform.SetPositionAndRotation(new Vector3(0, 10, 0), Quaternion.Euler(90, 0, 0));   // 화면 위 = 월드 +Z = 데칼 +V
        _rt = new RenderTexture(new RenderTextureDescriptor(N, N, RenderTextureFormat.ARGB32, 24) { sRGB = true }) { hideFlags = HideFlags.HideAndDontSave };
        _cam.targetTexture = _rt;

        var d = new GameObject("Decal");
        SceneManager.MoveGameObjectToScene(d, _scene);
        d.transform.rotation = Quaternion.Euler(90, 0, 0);   // BossAttackConeTelegraph.CreateDecal 과 같은 방향
        _projector = d.AddComponent<DecalProjector>();
        _projector.fadeFactor = 1f;
        _projector.enabled = false;
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        ShaderUtil.allowAsyncCompilation = _async;
        if (_cam != null) _cam.targetTexture = null;
        if (_rt != null) { _rt.Release(); Object.DestroyImmediate(_rt); }
        if (_floorMat != null) Object.DestroyImmediate(_floorMat);
        if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
    }

    Color32[] Shot()
    {
        _cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = _rt;
        var read = new Texture2D(N, N, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, N, N), 0, 0, false);
        read.Apply(false, false);
        RenderTexture.active = prev;
        var px = read.GetPixels32();
        Object.DestroyImmediate(read);
        return px;
    }

    // ── 렌더러 데칼 기능 — 실제 기법 확인 · ScreenSpace 로 잠깐 바꾸기(메모리만) ──

    static DecalRendererFeature Feature(out ScriptableRendererData data)
    {
        data = null;
        var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (asset == null) return null;
        var list = new SerializedObject(asset).FindProperty("m_RendererDataList");
        int index = new SerializedObject(asset).FindProperty("m_DefaultRendererIndex").intValue;
        data = list.GetArrayElementAtIndex(index).objectReferenceValue as ScriptableRendererData;
        return data != null ? data.rendererFeatures.OfType<DecalRendererFeature>().FirstOrDefault(f => f.isActive) : null;
    }

    static string Technique(DecalRendererFeature f, ScriptableRendererData data)
    {
        var m = typeof(DecalRendererFeature).GetMethod("GetTechnique", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(ScriptableRendererData) }, null);
        return m != null ? m.Invoke(f, new object[] { data }).ToString() : "?";
    }

    static object SettingsOf(DecalRendererFeature f) =>
        typeof(DecalRendererFeature).GetField("m_Settings", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(f);

    // DecalTechniqueOption 은 internal enum — 값을 object 로 주고받는다(Automatic 0 · DBuffer 1 · ScreenSpace 2).
    static void SetOption(DecalRendererFeature f, ScriptableRendererData data, object option)
    {
        var settings = SettingsOf(f);
        var field = settings.GetType().GetField("technique");
        field.SetValue(settings, System.Enum.ToObject(field.FieldType, option));
        f.Create();          // 데칼 시스템을 다시 만든다(m_RecreateSystems) — 인스펙터에서 기법을 바꿀 때(OnValidate)와 같은 경로
        data.SetDirty();     // 렌더러를 다시 만든다(기법에 따라 깊이·노멀 프리패스 요구가 바뀐다) — 애셋 저장 표시는 아니다
    }

    [Test]
    public void ProjectUsesDBufferDecals()
    {
        var f = Feature(out var data);
        Assert.IsNotNull(f, "active DecalRendererFeature on the default renderer");
        Assert.AreEqual("DBuffer", Technique(f, data), "project decal technique (Automatic on Windows desktop) — update this test if it changes");
    }

    [TestCaseSource(nameof(DBufferCases))]
    public void RealDecal_DBuffer_MatchesTextureDecal(Case c)
    {
        var f = Feature(out var data);
        Assume.That(f, Is.Not.Null);
        Assume.That(Technique(f, data), Is.EqualTo("DBuffer"));
        Compare(c, "DBuffer");
    }

    [TestCaseSource(nameof(ScreenSpaceCases))]
    public void RealDecal_ScreenSpace_MatchesTextureDecal(Case c)
    {
        var f = Feature(out var data);
        Assume.That(f, Is.Not.Null);
        var settings = SettingsOf(f);
        Assume.That(settings, Is.Not.Null, "URP DecalRendererFeature internals changed");
        object before = settings.GetType().GetField("technique").GetValue(settings);
        Assume.That(before.ToString(), Is.EqualTo("Automatic").Or.EqualTo("DBuffer").Or.EqualTo("ScreenSpace"));
        try
        {
            SetOption(f, data, 2);   // ScreenSpace
            Assume.That(Technique(f, data), Is.EqualTo("ScreenSpace"));
            Compare(c, "ScreenSpace");
        }
        finally { SetOption(f, data, before); }
    }

    void Compare(Case c, string technique)
    {
        var source = Resources.Load<Material>("Telegraph/MA_TelegraphDecal");
        Assert.IsNotNull(source, "MA_TelegraphDecal");
        Assert.IsNotNull(EnemyC3Visuals.DecalMaterial, "MA_EnemyC3Decal");

        // 프로젝터 크기 = 외곽선은 Bake 의 sampleExtent(외곽선 포함), 채움은 모양 크기. 카메라는 그 사각형을 RT 에 꼭 맞춘다.
        Vector2 size = c.kind == 0 ? c.extent + Vector2.one * (2 * EnemyC3Visuals.DecalPadding(c.id, c.extent, c.coneRadius, c.pathWidth, c.pathLength)) : c.extent;
        _projector.size = new Vector3(size.x, size.y, 4f);
        _cam.orthographicSize = size.y * .5f;
        _cam.aspect = size.x / size.y;

        Color color = c.kind == 0 ? Color.white : c.kind == 6 ? new Color(1f, 0f, 0f, .35f) : EnemyC3Visuals.FillColor("01");
        Texture2D oldTex = null;
        Material oldMat = new Material(source) { hideFlags = HideFlags.HideAndDontSave }, newMat = null;
        try
        {
            switch (c.kind)
            {
                case 0: oldTex = EnemyC3BakeReference.Decal(c.id, c.extent, c.coneRadius, c.coneCenter, c.halfAngle, c.pathWidth, c.pathLength); break;   // 캐시 — 지우지 않는다
                case 3: oldTex = LegacyDecalTextureReference.BuildArcTexture(Color.white, c.halfAngle, isBack: false, inner: c.inner, outer: 1f, size: 256); break;
                case 4: oldTex = EnemyC3FillShaderTests.OldRect(Color.white); break;
                case 5: oldTex = EnemyC3FillShaderTests.OldUnion(Color.white, c.extent, c.pathWidth * .5f, c.pathLength, c.coneCenter, c.coneRadius, c.front); break;
                default: oldTex = LegacyDecalTextureReference.BuildDiscTexture(color, 128, c.fill, c.rim); break;
            }
            // 기존: 텍스처 데칼. 원판은 색을 텍스처에 구웠으므로 흰색, 나머지는 런타임처럼 _BaseColor 에 색.
            oldMat.SetTexture("Base_Map", oldTex);
            oldMat.SetColor("_BaseColor", c.kind == 6 ? Color.white : color);
            oldMat.SetTexture("_MaskTex", Texture2D.whiteTexture);
            oldMat.SetVector("_MaskParams", new Vector4(0, 0, 1, 0));

            newMat = EnemyC3Visuals.NewDecalInstance(source, color);
            newMat.hideFlags = HideFlags.HideAndDontSave;
            switch (c.kind)
            {
                case 0: EnemyC3Visuals.SetDecalProperties(newMat, c.id, c.extent, c.coneRadius, c.coneCenter, c.halfAngle, c.pathWidth, c.pathLength); break;
                case 3: EnemyC3Visuals.SetArcFill(newMat, c.halfAngle, c.inner); break;
                case 4: EnemyC3Visuals.SetRectFill(newMat); break;
                case 5: EnemyC3Visuals.SetUnionFill(newMat, c.extent, c.pathWidth * .5f, c.pathLength, c.coneCenter, c.coneRadius, c.front); break;
                default: EnemyC3Visuals.SetDiscFill(newMat, c.fill, c.rim); break;
            }

            _projector.enabled = false;
            Color32[] floor = Shot();
            _projector.material = oldMat; _projector.enabled = true;
            Color32[] expected = Shot();
            _projector.material = newMat;
            Color32[] actual = Shot();
            _projector.enabled = false; _projector.material = null;

            int ok = 0, maxDiff = 0, visible = 0, oldVisible = 0;
            var diff = new Color32[N * N];
            for (int i = 0; i < expected.Length; i++)
            {
                int d = Diff(expected[i], actual[i]);
                if (d <= Tolerance) ok++;
                maxDiff = Mathf.Max(maxDiff, d);
                if (Diff(actual[i], floor[i]) > 8) visible++;
                if (Diff(expected[i], floor[i]) > 8) oldVisible++;
                byte v = (byte)Mathf.Min(255, d * 16);
                diff[i] = new Color32(v, v, v, 255);
            }
            float ratio = ok / (float)expected.Length, shown = visible / (float)expected.Length;
            string label = technique + "_" + c.label;
            WriteArtifacts(label, floor, expected, actual, diff, ratio, maxDiff, shown, oldVisible / (float)expected.Length);
            Assert.Greater(oldVisible / (float)expected.Length, 0.01f, $"{label}: the baseline texture decal did not render — test setup is broken");
            Assert.Greater(shown, 0.01f, $"{label}: SG_EnemyC3Decal drew nothing on the floor");
            Assert.GreaterOrEqual(ratio, RequiredRatio, $"{label}: within {Tolerance}/255 = {ratio:P3}, max channel diff {maxDiff}");
        }
        finally
        {
            _projector.enabled = false; _projector.material = null;
            Object.DestroyImmediate(oldMat);
            if (newMat != null) Object.DestroyImmediate(newMat);
            if (oldTex != null && c.kind != 0) Object.DestroyImmediate(oldTex);
        }
    }

    static int Diff(Color32 a, Color32 b) =>
        Mathf.Max(Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g)), Mathf.Abs(a.b - b.b));

    static string ArtifactDir()
    {
        // <repo>/../_lane-artifacts/<repo 폴더 이름>/57-shader/R/render — 레포 밖, 사람이 볼 산출물 자리. 없으면 남기지 않는다.
        var project = Directory.GetParent(Application.dataPath);
        var lanes = Path.Combine(project.Parent.FullName, "_lane-artifacts");
        return Directory.Exists(lanes) ? Path.Combine(lanes, project.Name, "57-shader", "R", "render") : null;
    }

    static void WriteArtifacts(string label, Color32[] floor, Color32[] expected, Color32[] actual, Color32[] diff, float ratio, int maxDiff, float shown, float oldShown)
    {
        string dir = ArtifactDir();
        if (dir == null) return;
        Directory.CreateDirectory(dir);
        // 맨바닥 | 기존 텍스처 데칼 | SG_EnemyC3Decal | 차이×16
        var sheet = new Texture2D(N * 4, N, TextureFormat.RGBA32, false);
        var px = new Color32[N * 4 * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x, row = y * N * 4;
                px[row + x] = floor[i]; px[row + N + x] = expected[i]; px[row + 2 * N + x] = actual[i]; px[row + 3 * N + x] = diff[i];
            }
        sheet.SetPixels32(px);
        sheet.Apply(false, false);
        File.WriteAllBytes(Path.Combine(dir, label + ".png"), sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
        File.AppendAllText(Path.Combine(dir, "results.tsv"), $"{label}\t{ratio:F5}\t{maxDiff}\tshown={shown:F4}\told={oldShown:F4}\n");
    }
}
