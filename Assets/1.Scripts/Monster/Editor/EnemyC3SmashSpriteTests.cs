using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 57 S3 — 미리 구운 스매시 09 sprite(Resources/Telegraph/EnemyC3_Smash09.png)가
/// 예전 런타임 경로(CPU Circle("09") → Sprite.Create)와 같은 그림·같은 화면 크기인지 확인한다(PLAN-57-enemy-c3-shader §6).
/// 픽셀 = 둘 다 같은 384×384 sRGB RenderTexture 로 옮겨 비교(채널 ≤ 2/255 인 픽셀 99% 이상).
/// 나란히 PNG(기존|신규|차이×16)는 _lane-artifacts 가 있을 때만 남긴다.
/// </summary>
public sealed class EnemyC3SmashSpriteTests
{
    const int N = 384;
    const int Tolerance = 2;
    const float RequiredRatio = 0.99f;

    static Color32[] ToRT(Texture source)
    {
        var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(N, N, RenderTextureFormat.ARGB32, 0) { sRGB = true });
        Graphics.Blit(source, rt);
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

    [Test]
    public void BakedSpriteMatchesCpuCircle()
    {
        var sprite = EnemyC3Visuals.SmashSprite;
        Assert.IsNotNull(sprite, "Run Tools/Enemy C3/Bake smash 09 sprite");
        Assert.AreEqual(N, sprite.texture.width);
        Color32[] expected = ToRT(EnemyC3BakeReference.Circle("09")), actual = ToRT(sprite.texture);
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
        WriteArtifacts("smash_09", expected, actual, diff, $"pixels\t{ratio:F5}\t{maxDiff}\n");
        Assert.GreaterOrEqual(ratio, RequiredRatio, $"smash 09: within {Tolerance}/255 = {ratio:P3}, max channel diff {maxDiff}");
    }

    [Test]
    public void ApplySmashKeepsLegacyWorldSize()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyC3SmashSpriteBaker.SourcePrefabPath);
        Assert.IsNotNull(prefab, EnemyC3SmashSpriteBaker.SourcePrefabPath);
        var go = Object.Instantiate(prefab);
        Sprite legacy = null;
        try
        {
            var sr = go.GetComponentInChildren<SpriteRenderer>(true);
            var old = sr.sprite;
            Vector3 scale = sr.transform.localScale;
            float alpha = sr.color.a;
            // 예전 ApplySmash 와 같은 식으로 만든 sprite = 기준.
            var tex = EnemyC3BakeReference.Circle("09");
            legacy = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f), tex.width / (old.bounds.size.x * EnemyC3Visuals.OuterScale("09")));

            EnemyC3Visuals.ApplySmash(go);

            Assert.AreSame(EnemyC3Visuals.SmashSprite, sr.sprite);
            Assert.AreEqual(scale, sr.transform.localScale, "transform scale must not change");
            Assert.AreEqual(new Color(1, 1, 1, alpha), sr.color, "RGB white, VFX alpha kept");
            Vector2 legacyWorld = legacy.rect.size / legacy.pixelsPerUnit, newWorld = sr.sprite.rect.size / sr.sprite.pixelsPerUnit;
            Vector2 legacyPivot = legacy.pivot / legacy.rect.size, newPivot = sr.sprite.pivot / sr.sprite.rect.size;
            string line = $"size\tlegacy rect/ppu {legacyWorld.x:F5}x{legacyWorld.y:F5} bounds {legacy.bounds.size.x:F5}\t" +
                          $"new rect/ppu {newWorld.x:F5}x{newWorld.y:F5} bounds {sr.sprite.bounds.size.x:F5}\t" +
                          $"renderer world {sr.bounds.size.x:F5}x{sr.bounds.size.z:F5}\tppu legacy {legacy.pixelsPerUnit:F5} new {sr.sprite.pixelsPerUnit:F5}\n";
            AppendResult(line);
            Assert.AreEqual(legacyWorld.x, newWorld.x, legacyWorld.x * 1e-4f, line);
            Assert.AreEqual(legacyWorld.y, newWorld.y, legacyWorld.y * 1e-4f, line);
            Assert.AreEqual(legacy.bounds.size.x, sr.sprite.bounds.size.x, legacyWorld.x * 1e-4f, line);
            Assert.AreEqual(legacyPivot.x, newPivot.x, 1e-5f);
            Assert.AreEqual(legacyPivot.y, newPivot.y, 1e-5f);

            // 풀 재사용(두 번째 대출) — 다시 불러도 같은 sprite, 크기 그대로.
            EnemyC3Visuals.ApplySmash(go);
            Assert.AreSame(EnemyC3Visuals.SmashSprite, sr.sprite);
        }
        finally
        {
            if (legacy != null) Object.DestroyImmediate(legacy);
            Object.DestroyImmediate(go);
        }
    }

    static string ArtifactDir()
    {
        // <repo>/../_lane-artifacts/<repo 폴더 이름>/57-shader/S3 — 레포 밖, 사람이 볼 산출물 자리. 없으면 남기지 않는다.
        var project = Directory.GetParent(Application.dataPath);
        var lanes = Path.Combine(project.Parent.FullName, "_lane-artifacts");
        return Directory.Exists(lanes) ? Path.Combine(lanes, project.Name, "57-shader", "S3") : null;
    }

    static void AppendResult(string line)
    {
        string dir = ArtifactDir();
        if (dir == null) return;
        Directory.CreateDirectory(dir);
        File.AppendAllText(Path.Combine(dir, "results.tsv"), line);
    }

    static void WriteArtifacts(string label, Color32[] expected, Color32[] actual, Color32[] diff, string line)
    {
        string dir = ArtifactDir();
        if (dir == null) return;
        Directory.CreateDirectory(dir);
        // 기존 | 신규 | 차이×16. 앞의 둘은 straight 알파를 어두운 회색 위에 얹어 보이게 한다.
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
        AppendResult(line);
    }

    static Color32 OverGray(Color32 c)
    {
        const int bg = 40;
        return new Color32((byte)((c.r * c.a + bg * (255 - c.a)) / 255), (byte)((c.g * c.a + bg * (255 - c.a)) / 255),
                           (byte)((c.b * c.a + bg * (255 - c.a)) / 255), 255);
    }
}
