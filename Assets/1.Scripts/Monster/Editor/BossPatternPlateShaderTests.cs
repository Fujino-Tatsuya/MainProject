using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 할 일 57 S4c2 — BossPatternVisuals 의 코드 판(사각 64² · 원판 128² · 조준 128²)을 Monster/TelegraphQuad 의 _CodeShape 모드로
/// 그린 결과가 예전 CPU 텍스처 × 같은 재질과 같은지 픽셀로 비교한다.
///
/// 예전 재질 = MA_TelegraphQuad(BossPatternVisuals 의 판 재질) — 텍스처는 흰색 + 알파만, 색은 MPB 의 _Color, 마스크는 TelegraphShade.
/// 새 경로는 같은 재질·같은 MPB 방식에서 _MainTex 대신 _CodeShape 만 바뀐다. 그래서 두 쪽 모두 실제 런타임 API
/// (기존 = Paint/PaintStyled(Texture) + Editor/LegacyDecalTextureReference 의 원본 텍스처, 새것 = Paint/PaintStyled(CodeShape))로
/// 칠한 렌더러의 재질·MPB 를 그대로 384² sRGB RT 에 그린다(판 메시와 같은 XZ 평면 — 마스크의 월드 좌표까지 같다).
/// 기준 = 채널 오차 ≤ 2/255 인 픽셀 99% 이상. 나란히 PNG(기존|셰이더|차이×16)는 _lane-artifacts 가 있을 때만 S4c2_ 접두어로 남긴다.
/// 기본 경로(_CodeShape.x = 0 — 텍스처를 쓰는 기존 사용자)는 바이트 단위로 같아야 한다(DefaultPath_*).
/// </summary>
public sealed class BossPatternPlateShaderTests
{
    const int N = 384;
    const int Tolerance = 2;
    const float RequiredRatio = 0.99f;

    public struct Case
    {
        public string label;
        public BossPatternVisuals.CodeShape shape;
        public float rim;          // < 0 = 기본(SquareRim · DiscRim)
        public Color color;
        public bool masked;
        public TelegraphMaskMode maskMode;
        public float size;
        public override string ToString() => label;
    }

    static IEnumerable<TestCaseData> Cases()
    {
        var outline = new Color(1f, 0.15f, 0.1f, 0.3f);   // TelegraphStyleSO.circle 외곽 판
        var fill = new Color(1f, 0.05f, 0.02f, 0.6f);     // 채움 판
        var tile = new Color(1f, 0.15f, 0.1f, 0.28f);     // floorTile 외곽 판
        var D = BossPatternVisuals.CodeShape.Disc;
        var S = BossPatternVisuals.CodeShape.Square;
        var cases = new[]
        {
            // 원판 — 기본(DiscTexture 0.08) · 테두리 비율 3개(DiscTextureFor) · 큰 판
            new Case { label = "disc_default", shape = D, rim = -1f, color = Color.white, size = 4f },
            new Case { label = "disc_rim0.08", shape = D, rim = 0.08f, color = outline, size = 4f },
            new Case { label = "disc_rim0.03", shape = D, rim = 0.03f, color = fill, size = 4f },
            new Case { label = "disc_rim0.2", shape = D, rim = 0.2f, color = fill, size = 4f },
            new Case { label = "disc_rim0.08_12m", shape = D, rim = 0.08f, color = fill, size = 12f },
            // 사각 — 기본(SquareTexture 0.1) · 테두리 비율 3개(SquareTextureFor)
            new Case { label = "square_default", shape = S, rim = -1f, color = Color.white, size = 4f },
            new Case { label = "square_rim0.1", shape = S, rim = 0.1f, color = tile, size = 4f },
            new Case { label = "square_rim0.05", shape = S, rim = 0.05f, color = fill, size = 4f },
            new Case { label = "square_rim0.25", shape = S, rim = 0.25f, color = fill, size = 4f },
            // 조준 — 크로스헤어 아트가 비었을 때의 폴백(BuildCrosshair 128)
            new Case { label = "crosshair", shape = BossPatternVisuals.CodeShape.Crosshair, rim = -1f, color = new Color(1f, 0.85f, 0.1f, 0.9f), size = 4f },
            new Case { label = "crosshair_1.5m", shape = BossPatternVisuals.CodeShape.Crosshair, rim = -1f, color = Color.white, size = 1.5f },
            // 마스크 켬(PaintStyled) — 반복 · 늘이기
            new Case { label = "disc_rim0.08_mask_tile", shape = D, rim = 0.08f, color = fill, masked = true, maskMode = TelegraphMaskMode.Tile, size = 4f },
            new Case { label = "square_rim0.1_mask_stretch", shape = S, rim = 0.1f, color = tile, masked = true, maskMode = TelegraphMaskMode.Stretch, size = 4f },
        };
        foreach (var c in cases) yield return new TestCaseData(c).SetName($"PatternPlate_{c.label}");
    }

    [OneTimeSetUp]
    public void ResetResults()
    {
        string dir = ArtifactDir();
        if (dir != null && File.Exists(Path.Combine(dir, "S4c2_results.tsv"))) File.Delete(Path.Combine(dir, "S4c2_results.tsv"));
    }

    readonly List<Object> _trash = new();

    [TearDown]
    public void Cleanup()
    {
        foreach (var o in _trash) if (o != null) Object.DestroyImmediate(o);
        _trash.Clear();
    }

    MeshRenderer NewPlate(string name)
    {
        var r = BossPatternVisuals.CreateFlat(name, null);
        r.gameObject.hideFlags = HideFlags.HideAndDontSave;
        _trash.Add(r.gameObject);
        return r;
    }

    Texture2D Track(Texture2D t)
    {
        t.hideFlags = HideFlags.HideAndDontSave;
        _trash.Add(t);
        return t;
    }

    Texture2D MaskTexture()
    {
        // 결정적인 8×8 체커(밝음 255 · 어두움 40), 반복·이중선형 — 마스크 경로가 실제로 값을 바꾸게.
        var t = Track(new Texture2D(8, 8, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear });
        var px = new Color32[64];
        for (int i = 0; i < 64; i++) { byte v = (byte)(((i % 8) + (i / 8)) % 2 == 0 ? 255 : 40); px[i] = new Color32(v, v, v, 255); }
        t.SetPixels32(px);
        t.Apply(false, false);
        return t;
    }

    static float OldRim(Case c) =>
        c.rim < 0f ? (c.shape == BossPatternVisuals.CodeShape.Square ? 0.1f : 0.08f)   // 예전 SquareTexture · DiscTexture 의 리터럴
                   : Mathf.RoundToInt(Mathf.Clamp(c.rim, 0.005f, 0.5f) * 200f) / 200f;   // 예전 *TextureFor 의 캐시 키

    Texture2D OldTexture(Case c) => Track(c.shape switch
    {
        BossPatternVisuals.CodeShape.Square => LegacyDecalTextureReference.BuildSquare(64, OldRim(c)),
        BossPatternVisuals.CodeShape.Disc => LegacyDecalTextureReference.BuildDisc(128, OldRim(c)),
        _ => LegacyDecalTextureReference.BuildCrosshair(128),
    });

    static readonly Vector3 MaskOrigin = new Vector3(0.3f, 0f, -0.2f);
    static readonly Vector3 MaskRight = new Vector3(0.8f, 0f, -0.6f), MaskForward = new Vector3(0.6f, 0f, 0.8f);

    [TestCaseSource(nameof(Cases))]
    public void CodeShapeMatchesOldTexture(Case c)
    {
        MeshRenderer oldPlate = NewPlate("OldPlate"), newPlate = NewPlate("NewPlate");
        Texture2D old = OldTexture(c);

        if (c.masked)
        {
            var layer = new TelegraphLayer(c.color)
            {
                mask = new TelegraphMask { texture = MaskTexture(), mode = c.maskMode, tileSize = 0.5f, strength = 0.7f, rotation = 20f },
            };
            var extent = new Vector2(c.size, c.size);
            BossPatternVisuals.PaintStyled(oldPlate, layer, old, MaskOrigin, MaskRight, MaskForward, extent);
            BossPatternVisuals.PaintStyled(newPlate, layer, c.shape, c.rim, MaskOrigin, MaskRight, MaskForward, extent);
        }
        else
        {
            BossPatternVisuals.Paint(oldPlate, c.color, old);
            if (c.rim < 0f) BossPatternVisuals.Paint(newPlate, c.color, c.shape);
            else BossPatternVisuals.Paint(newPlate, c.color, c.shape, c.rim);
        }

        Assert.AreSame(oldPlate.sharedMaterial, newPlate.sharedMaterial, "same plate material (MA_TelegraphQuad copy)");
        Assert.AreEqual("Monster/TelegraphQuad", newPlate.sharedMaterial.shader.name);
        Assert.IsTrue(newPlate.sharedMaterial.shader.isSupported, "TelegraphQuad not supported");
        var block = new MaterialPropertyBlock();
        newPlate.GetPropertyBlock(block);
        Assert.IsFalse(block.HasTexture("_MainTex"), "code shape must not carry a texture");

        Color32[] expected = Render(oldPlate, c.size), actual = Render(newPlate, c.size);
        int ok = 0, maxDiff = 0, covered = 0;
        var diff = new Color32[N * N];
        for (int i = 0; i < expected.Length; i++)
        {
            Color32 e = expected[i], a = actual[i];
            int d = Mathf.Max(Mathf.Max(Mathf.Abs(e.r - a.r), Mathf.Abs(e.g - a.g)), Mathf.Max(Mathf.Abs(e.b - a.b), Mathf.Abs(e.a - a.a)));
            if (d <= Tolerance) ok++;
            if (e.a > 0) covered++;
            maxDiff = Mathf.Max(maxDiff, d);
            byte v = (byte)Mathf.Min(255, d * 16);
            diff[i] = new Color32(v, v, v, 255);
        }
        float ratio = ok / (float)expected.Length;
        WriteArtifacts(c.label, expected, actual, diff, ratio, maxDiff);
        Assert.Greater(covered, N * N / 50, $"{c.label}: old texture drew almost nothing — wrong case?");
        Assert.GreaterOrEqual(ratio, RequiredRatio, $"{c.label}: within {Tolerance}/255 = {ratio:P3}, max channel diff {maxDiff}");
    }

    // ── 기본 경로(텍스처) — 기존 TelegraphQuad 사용자 결과가 바이트 단위로 같아야 한다 ──

    [Test]
    public void DefaultPath_IgnoresCodeShapeValues()
    {
        // 같은 판을 코드 모양(테두리 0.3)으로 썼다가 텍스처로 되돌려도, 처음부터 텍스처로만 칠한 판과 바이트가 같다.
        // (_CodeShape 를 한 번도 넣지 않은 MPB = 재질 기본값 (0,0,0,0) 경로와도 같다.)
        var tex = Track(LegacyDecalTextureReference.BuildDisc(128, 0.08f));
        var color = new Color(0.9f, 0.2f, 0.2f, 0.8f);
        MeshRenderer fresh = NewPlate("Fresh"), reused = NewPlate("Reused");
        BossPatternVisuals.Paint(fresh, color, tex);
        BossPatternVisuals.Paint(reused, Color.cyan, BossPatternVisuals.CodeShape.Disc, 0.3f);
        BossPatternVisuals.Paint(reused, color, tex);

        var block = new MaterialPropertyBlock();
        reused.GetPropertyBlock(block);
        Assert.AreEqual(Vector4.zero, block.GetVector("_CodeShape"), "texture paint resets the code shape");

        var raw = new MaterialPropertyBlock();   // _CodeShape 없음 — 재질 기본값
        raw.SetTexture("_MainTex", tex);
        raw.SetColor("_Color", color);
        raw.SetVector("_MaskParams", new Vector4(0f, 0f, 1f, 0f));

        Color32[] a = Render(fresh, 4f), b = Render(reused, 4f), c = Render(fresh.sharedMaterial, raw, 4f);
        int covered = 0, reusedDiff = 0, rawDiff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].a > 0) covered++;
            if (!a[i].Equals(b[i])) reusedDiff++;
            if (!a[i].Equals(c[i])) rawDiff++;
        }
        Assert.Greater(covered, N * N / 50);
        Assert.AreEqual(0, reusedDiff, "reused plate pixels differ from a texture-only plate");
        Assert.AreEqual(0, rawDiff, "pixels differ from the material-default (_CodeShape unset) path");
    }

    [Test]
    public void DefaultPath_MaterialDefaultIsTexture()
    {
        // 재질 에셋은 고치지 않았다 — 셰이더 기본값 (0,0,0,0) = 텍스처 경로.
        var mat = Resources.Load<Material>("Telegraph/MA_TelegraphQuad");
        Assert.IsNotNull(mat, "MA_TelegraphQuad");
        Assert.AreEqual(Vector4.zero, mat.GetVector("_CodeShape"));
    }

    [Test]
    public void Paint_NullTextureKeepsCodeShape_TextureResets()
    {
        // WellsDroneAttack 은 조준 색만 바꿀 때 Paint(_cross, col, null) 을 부른다 — 모양(코드 조준)이 남아야 한다.
        var r = NewPlate("Cross");
        BossPatternVisuals.Paint(r, Color.red, BossPatternVisuals.CodeShape.Crosshair);
        BossPatternVisuals.Paint(r, Color.green, (Texture)null);
        var block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        Assert.AreEqual(new Vector4(3f, 0f, 0f, 0f), block.GetVector("_CodeShape"));
        Assert.AreEqual(Color.green, block.GetColor("_Color"));

        // 테두리 비율은 예전 텍스처 캐시처럼 0.005 단위로 묶는다 · 생략 = 기본.
        BossPatternVisuals.Paint(r, Color.white, BossPatternVisuals.CodeShape.Disc, 0.0812f);
        r.GetPropertyBlock(block);
        Assert.AreEqual(0.08f, block.GetVector("_CodeShape").y, 1e-6f);
        BossPatternVisuals.Paint(r, Color.white, BossPatternVisuals.CodeShape.Square);
        r.GetPropertyBlock(block);
        Assert.AreEqual(new Vector4(1f, BossPatternVisuals.SquareRim, 0f, 0f), block.GetVector("_CodeShape"));
    }

    // ── 렌더 · 산출물 ──

    static Color32[] Render(Renderer r, float size)
    {
        var block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        return Render(r.sharedMaterial, block, size);
    }

    static Mesh PlateMesh()
    {
        // BossPatternVisuals 판 메시 그대로(XZ 평면, 위 +Y).
        var go = BossPatternVisuals.CreateFlat("MeshProbe", null);
        Mesh m = go.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(go.gameObject);
        return m;
    }

    static Color32[] Render(Material mat, MaterialPropertyBlock block, float size)
    {
        // 위에서 내려다보는 정사영 — 판 전체가 RT 를 꽉 채운다. 월드 좌표(마스크)는 런타임 판과 같다.
        var down = Quaternion.Euler(90f, 0f, 0f);
        var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(N, N, RenderTextureFormat.ARGB32, 0) { sRGB = true });
        var cb = new CommandBuffer { name = "BossPatternPlateTest" };
        cb.SetRenderTarget(rt);
        cb.ClearRenderTarget(false, true, Color.clear);
        cb.SetViewProjectionMatrices(Matrix4x4.Rotate(Quaternion.Inverse(down)),Matrix4x4.Ortho(-size * .5f, size * .5f, -size * .5f, size * .5f, -1, 1));
        cb.DrawMesh(PlateMesh(), Matrix4x4.Scale(new Vector3(size, 1f, size)), mat, 0, 0, block);
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

    static string ArtifactDir()
    {
        // <repo>/../_lane-artifacts/<repo 폴더 이름>/57-shader/S4c — 레포 밖, 사람이 볼 산출물 자리. 없으면 남기지 않는다.
        var project = Directory.GetParent(Application.dataPath);
        var lanes = Path.Combine(project.Parent.FullName, "_lane-artifacts");
        return Directory.Exists(lanes) ? Path.Combine(lanes, project.Name, "57-shader", "S4c") : null;
    }

    static void WriteArtifacts(string label, Color32[] expected, Color32[] actual, Color32[] diff, float ratio, int maxDiff)
    {
        string dir = ArtifactDir();
        if (dir == null) return;
        Directory.CreateDirectory(dir);
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
        File.WriteAllBytes(Path.Combine(dir, "S4c2_" + label + ".png"), sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
        File.AppendAllText(Path.Combine(dir, "S4c2_results.tsv"), $"{label}\t{ratio:F5}\t{maxDiff}\n");
    }

    static Color32 OverGray(Color32 premul)
    {
        const int bg = 40;
        int k = 255 - premul.a;
        return new Color32((byte)Mathf.Min(255, premul.r + bg * k / 255), (byte)Mathf.Min(255, premul.g + bg * k / 255),
                           (byte)Mathf.Min(255, premul.b + bg * k / 255), 255);
    }
}
