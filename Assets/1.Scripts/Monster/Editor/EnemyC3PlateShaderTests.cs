using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 할 일 57 S1 — 바닥 판(12·14·15·16, 점프 06·07)을 EnemyC3Plate 셰이더로 그린 결과가
/// 예전 CPU Bake 텍스처(Monster/TelegraphQuad 로 그린 화면)와 같은지 픽셀로 비교한다(PLAN-57-enemy-c3-shader §6).
/// 둘 다 같은 384×384 sRGB RenderTexture 에 같은 블렌드로 그려 화면에 닿는 값끼리 비교한다.
/// 기준 = 채널 오차 ≤ 2/255 인 픽셀 99% 이상. 나란히 PNG(기존|셰이더|차이×16)는 _lane-artifacts 가 있을 때만 남긴다.
/// </summary>
public sealed class EnemyC3PlateShaderTests
{
    const int N = 384;
    const int Tolerance = 2;
    const float RequiredRatio = 0.99f;

    // 판은 디자인 단위(원 330·사각 300)로 정규화돼 월드 크기와 무관해야 한다 — 크기 3개로 그것까지 확인한다.
    static readonly float[] Sizes = { 1f, 4f, 12f };

    static IEnumerable<TestCaseData> Cases()
    {
        var plates = new (string id, bool square)[] { ("12", false), ("14", false), ("15", true), ("16", true), ("06", false), ("07", false) };
        foreach (var (id, square) in plates)
            foreach (bool fill in new[] { false, true })
                foreach (float size in Sizes)
                    yield return new TestCaseData(id, square, fill, size)
                        .SetName($"Plate_{id}_{(square ? "square" : "circle")}_{(fill ? "fill" : "boundary")}_{size}m");
    }

    [OneTimeSetUp]
    public void ResetResults()
    {
        string dir = ArtifactDir();
        if (dir != null && File.Exists(Path.Combine(dir, "results.tsv"))) File.Delete(Path.Combine(dir, "results.tsv"));
    }

    static Mesh _quad;
    static Mesh Quad
    {
        get
        {
            if (_quad != null) return _quad;
            // BossPatternVisuals 판과 같은 UV — 화면(XY) 평면에 세웠다.
            _quad = new Mesh { name = "EnemyC3TestQuad", hideFlags = HideFlags.HideAndDontSave };
            _quad.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0), new Vector3(.5f, -.5f, 0) };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            return _quad;
        }
    }

    static Color32[] Render(Material mat, MaterialPropertyBlock block, float size)
    {
        var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(N, N, RenderTextureFormat.ARGB32, 0) { sRGB = true });
        var cb = new CommandBuffer { name = "EnemyC3PlateTest" };
        cb.SetRenderTarget(rt);
        cb.ClearRenderTarget(false, true, Color.clear);
        cb.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(-size * .5f, size * .5f, -size * .5f, size * .5f, -1, 1));
        cb.DrawMesh(Quad, Matrix4x4.Scale(new Vector3(size, size, 1)), mat, 0, 0, block);
        Graphics.ExecuteCommandBuffer(cb);
        cb.Release();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var read = new Texture2D(N, N, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, N, N), 0, 0, false);
        read.Apply(false, false);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var pixels = read.GetPixels32();
        Object.DestroyImmediate(read);
        return pixels;
    }

    [TestCaseSource(nameof(Cases))]
    public void ShaderMatchesCpuBake(string id, bool square, bool fill, float size)
    {
        var quadMat = Resources.Load<Material>("Telegraph/MA_TelegraphQuad");
        Assert.IsNotNull(quadMat, "MA_TelegraphQuad");
        var plateMat = EnemyC3Visuals.PlateMaterial;
        Assert.IsNotNull(plateMat, "EnemyC3Plate material");
        Assert.IsTrue(plateMat.shader.isSupported, "EnemyC3Plate shader not supported");

        // 기존: Bake 텍스처 + BossPatternVisuals.Paint 와 같은 속성(흰색, 마스크 꺼짐).
        var oldBlock = new MaterialPropertyBlock();
        oldBlock.SetTexture("_MainTex", square ? EnemyC3BakeReference.Square(id, fill) : EnemyC3BakeReference.Circle(id, fill));
        oldBlock.SetColor("_Color", Color.white);
        oldBlock.SetTexture("_MaskTex", Texture2D.whiteTexture);
        oldBlock.SetVector("_MaskParams", new Vector4(0, 0, 1, 0));
        var newBlock = new MaterialPropertyBlock();
        EnemyC3Visuals.SetPlateProperties(newBlock, id, square, fill);

        Color32[] expected = Render(quadMat, oldBlock, size), actual = Render(plateMat, newBlock, size);
        int ok = 0, maxDiff = 0;
        var diff = new Color32[N * N];
        for (int i = 0; i < expected.Length; i++)
        {
            Color32 e = expected[i], a = actual[i];
            int d = Mathf.Max(Mathf.Max(Mathf.Abs(e.r - a.r), Mathf.Abs(e.g - a.g)), Mathf.Max(Mathf.Abs(e.b - a.b), Mathf.Abs(e.a - a.a)));
            if (d <= Tolerance) ok++;
            maxDiff = Mathf.Max(maxDiff, d);
            byte v = (byte)Mathf.Min(255, d * 16);
            diff[i] = new Color32(v, v, v, 255);
        }
        float ratio = ok / (float)expected.Length;
        string label = $"{id}_{(square ? "square" : "circle")}_{(fill ? "fill" : "boundary")}_{size}m";
        WriteArtifacts(label, expected, actual, diff, ratio, maxDiff);
        Assert.GreaterOrEqual(ratio, RequiredRatio, $"{label}: within {Tolerance}/255 = {ratio:P3}, max channel diff {maxDiff}");
    }

    static string ArtifactDir()
    {
        // <repo>/../_lane-artifacts/<repo 폴더 이름>/57-shader/S1 — 레포 밖, 사람이 볼 산출물 자리. 없으면 남기지 않는다.
        var project = Directory.GetParent(Application.dataPath);
        var lanes = Path.Combine(project.Parent.FullName, "_lane-artifacts");
        return Directory.Exists(lanes) ? Path.Combine(lanes, project.Name, "57-shader", "S1") : null;
    }

    static void WriteArtifacts(string label, Color32[] expected, Color32[] actual, Color32[] diff, float ratio, int maxDiff)
    {
        string dir = ArtifactDir();
        if (dir == null) return;
        Directory.CreateDirectory(dir);
        // 기존 | 셰이더 | 차이×16. 앞의 둘은 프리멀티 값을 어두운 회색 위에 얹어 보이게 한다.
        var sheet = new Texture2D(N * 3, N, TextureFormat.RGBA32, false);
        var px = new Color32[N * 3 * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                px[y * N * 3 + x] = OverGray(expected[i]);
                px[y * N * 3 + N + x] = OverGray(actual[i]);
                px[y * N * 3 + 2 * N + x] = diff[i];
            }
        sheet.SetPixels32(px);
        sheet.Apply(false, false);
        File.WriteAllBytes(Path.Combine(dir, label + ".png"), sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
        File.AppendAllText(Path.Combine(dir, "results.tsv"), $"{label}\t{ratio:F5}\t{maxDiff}\n");
    }

    static Color32 OverGray(Color32 premul)
    {
        const int bg = 40;
        int k = 255 - premul.a;
        return new Color32((byte)Mathf.Min(255, premul.r + bg * k / 255), (byte)Mathf.Min(255, premul.g + bg * k / 255),
                           (byte)Mathf.Min(255, premul.b + bg * k / 255), 255);
    }
}
