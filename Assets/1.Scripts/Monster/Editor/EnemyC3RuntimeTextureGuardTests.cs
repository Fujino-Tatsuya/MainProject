using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 57 S4b — 재발 감시(사용자 결정 10-20). 런타임 코드가 CPU 로 텍스처를 만들거나 칠하지 않는지 소스를 읽어 검사한다.
/// 범위 = Assets/1.Scripts/Monster/** · Assets/1.Scripts/Rendering/Telegraph/** 의 .cs 중 Editor 폴더 밖.
/// 금지 = new Texture2D · SetPixel / SetPixels / SetPixels32 / SetPixelData · Sprite.Create. 주석 줄(//, ///, /* */ 안)은 제외.
/// 위반이 나오면 파일:줄과 그 줄을 메시지에 찍는다. 런타임 예고는 셰이더(EnemyC3Plate·SG_EnemyC3Decal)로 그리고,
/// CPU 기준 그림은 에디터 전용 EnemyC3BakeReference · LegacyDecalTextureReference 에만 둔다. 예외 0(S4c2 확정).
/// </summary>
public sealed class EnemyC3RuntimeTextureGuardTests
{
    static readonly string[] Roots = { "Assets/1.Scripts/Monster", "Assets/1.Scripts/Rendering/Telegraph" };

    static readonly Regex Forbidden = new Regex(@"\bnew\s+Texture2D\b|\bSetPixel(s|s32|Data)?\b|\bSprite\s*\.\s*Create\b");

    // 멤버 선언 줄(최상위 클래스 안 4칸 들여쓰기) — 위반 줄이 속한 메서드 이름을 찾는 데 쓴다.
    static readonly Regex MemberDecl = new Regex(@"^ {4}(?![ /\[{}])[^=;(]*?\b(\w+)\s*(<[^>()]*>)?\s*\(");

    /// <summary>
    /// 메서드 단위 예외(파일 + 메서드 이름) — 그 메서드 안의 사용만 허용하고, 같은 파일이라도 밖에 새로 생긴 사용은 실패한다.
    /// 🔴 비어 있다 = 런타임 전체 금지 확정(할 일 57 S4c2, 사용자 결정 10-20). 57 이전의 비-C3 경로
    ///    (AoeTelegraph·BossDirectionIndicator — S4c1, BossPatternVisuals — S4c2)도 셰이더로 옮겼다(원본 = Editor/LegacyDecalTextureReference).
    ///    항목을 다시 넣으려면 사용자 결정이 필요하다 — <see cref="NoRuntimeExceptions"/> 가 막는다.
    /// </summary>
    static readonly (string file, string[] methods, string reason)[] PendingDecision =
        System.Array.Empty<(string file, string[] methods, string reason)>();

    public readonly struct Hit
    {
        public readonly int line; public readonly string method, text;
        public Hit(int line, string method, string text) { this.line = line; this.method = method; this.text = text; }
    }

    /// <summary>소스 한 파일의 금지 호출(주석 줄 제외)과 그 줄을 감싼 메서드 이름.</summary>
    public static List<Hit> Scan(string source)
    {
        var hits = new List<Hit>();
        string[] lines = source.Replace("\r\n", "\n").Split('\n');
        bool inBlock = false;
        string method = null;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i], trimmed = line.TrimStart();
            if (inBlock)
            {
                int end = line.IndexOf("*/", System.StringComparison.Ordinal);
                if (end < 0) continue;
                inBlock = false;
                line = line.Substring(end + 2);
                trimmed = line.TrimStart();
            }
            if (trimmed.StartsWith("//")) continue;
            if (trimmed.StartsWith("/*"))
            {
                if (trimmed.IndexOf("*/", 2, System.StringComparison.Ordinal) < 0) inBlock = true;
                continue;
            }
            var decl = MemberDecl.Match(line);
            if (decl.Success) method = decl.Groups[1].Value;
            else if (line.StartsWith("    ") && !line.StartsWith("     ") && trimmed.Length > 0 && trimmed != "{" && trimmed != "}"
                     && !trimmed.StartsWith("[")) method = null; // 필드·프로퍼티 등 다른 멤버 — 앞 메서드를 이어받지 않는다
            if (Forbidden.IsMatch(line)) hits.Add(new Hit(i + 1, method, trimmed));
        }
        return hits;
    }

    static IEnumerable<string> RuntimeFiles()
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        foreach (string root in Roots)
        {
            string dir = Path.Combine(project, root);
            if (!Directory.Exists(dir)) continue;
            foreach (string full in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(project, full).Replace('\\', '/');
                if (rel.Split('/').Contains("Editor")) continue;
                yield return rel;
            }
        }
    }

    static bool Allowed(string file, string method) => Allowed(PendingDecision, file, method);

    static bool Allowed((string file, string[] methods, string reason)[] exceptions, string file, string method) =>
        method != null && exceptions.Any(p => p.file == file && p.methods.Contains(method));

    [Test]
    public void NoRuntimeExceptions()
    {
        // 예외 0 확정 — 실제 예외 목록이 비어 있어야 한다(예외 메커니즘 자체는 ScannerCatchesForbiddenCalls 가 가짜 목록으로 검사).
        Assert.IsEmpty(PendingDecision, "runtime texture creation is banned without exceptions (할 일 57 S4c2)");
    }

    [Test]
    public void RuntimeCodeDoesNotBuildTextures()
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var violations = new List<string>();
        int files = 0;
        foreach (string file in RuntimeFiles())
        {
            files++;
            foreach (var hit in Scan(File.ReadAllText(Path.Combine(project, file))))
                if (!Allowed(file, hit.method))
                    violations.Add($"{file}:{hit.line} ({hit.method ?? "?"}) {hit.text}");
        }
        Assert.Greater(files, 0, "no runtime files scanned — wrong roots?");
        Assert.IsEmpty(violations, "Runtime CPU texture creation (use a shader; editor-only bakes go in an Editor folder):\n" +
                                   string.Join("\n", violations));
    }

    [Test]
    public void PendingDecisionEntriesStillExist()
    {
        // 예외가 가리키는 메서드가 사라지거나 금지 호출이 없어지면 목록에서 지우라고 알린다(목록이 낡지 않게).
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        foreach (var p in PendingDecision)
        {
            string path = Path.Combine(project, p.file);
            Assert.IsTrue(File.Exists(path), p.file + " gone — remove it from PendingDecision");
            var used = Scan(File.ReadAllText(path)).Select(h => h.method).ToHashSet();
            foreach (string m in p.methods)
                Assert.IsTrue(used.Contains(m), $"{p.file} {m} no longer builds textures — remove it from PendingDecision");
        }
    }

    [Test]
    public void ScannerCatchesForbiddenCalls()
    {
        // 자기검사 — 가짜 소스로 검출·주석 제외·메서드 귀속이 맞는지.
        const string fake =
            "public static class Fake\n" +
            "{\n" +
            "    static int Field = 1;\n" +
            "    // new Texture2D(1, 1) in a comment\n" +
            "    /// SetPixels32 in a doc comment\n" +
            "    /* Sprite.Create(\n" +
            "       SetPixel( */\n" +
            "    static Texture2D BuildDiscTexture(int size)\n" +
            "    {\n" +
            "        var t = new Texture2D(size, size);\n" +
            "        t.SetPixels32(null);\n" +
            "        return t;\n" +
            "    }\n" +
            "    public static Sprite Make(Texture2D t) => Sprite.Create(t, default, default);\n" +
            "    static Texture2D Other() { var t=new  Texture2D(2,2); t.SetPixel(0,0,default); t.SetPixels(null); t.SetPixelData(new byte[0],0); return t; }\n" +
            "    static readonly Texture2D Cached = new Texture2D(4, 4);\n" +
            "}\n";
        var hits = Scan(fake);
        string dump = string.Join("\n", hits.Select(h => $"{h.line} {h.method} {h.text}"));
        CollectionAssert.AreEqual(new[] { 10, 11, 14, 15, 16 }, hits.Select(h => h.line).ToArray(), dump);
        Assert.AreEqual("BuildDiscTexture", hits[0].method, dump);
        Assert.AreEqual("BuildDiscTexture", hits[1].method, dump);
        Assert.AreEqual("Make", hits[2].method, dump);
        Assert.AreEqual("Other", hits[3].method, dump);
        Assert.IsNull(hits[4].method, "field initializer must not inherit the previous method: " + dump);

        // 예외 메커니즘 — 가짜 예외 목록으로. 예외는 메서드 단위라 예외 파일의 다른 메서드에서 나온 사용은 걸린다.
        var fakeExceptions = new[] { ("Assets/1.Scripts/Monster/Fake.cs", new[] { "BuildDiscTexture" }, "self-test") };
        Assert.IsTrue(Allowed(fakeExceptions, "Assets/1.Scripts/Monster/Fake.cs", "BuildDiscTexture"));
        Assert.IsFalse(Allowed(fakeExceptions, "Assets/1.Scripts/Monster/Fake.cs", "Make"));
        Assert.IsFalse(Allowed(fakeExceptions, "Assets/1.Scripts/Monster/Fake.cs", null));
        Assert.IsFalse(Allowed(fakeExceptions, "Assets/1.Scripts/Monster/EnemyC3Visuals.cs", "BuildDiscTexture"));

        // 실제 예외는 0 — S4c1·S4c2 에서 지운 경로가 다시 생기면 걸려야 한다.
        Assert.IsFalse(Allowed("Assets/1.Scripts/Monster/AoeTelegraph.cs", "BuildDiscTexture"));
        Assert.IsFalse(Allowed("Assets/1.Scripts/Monster/Boss/BossDirectionIndicator.cs", "BuildArcTexture"));
        Assert.IsFalse(Allowed("Assets/1.Scripts/Monster/Boss/BossPatternVisuals.cs", "BuildDisc"));
        Assert.IsFalse(Allowed("Assets/1.Scripts/Monster/Boss/BossPatternVisuals.cs", "BuildSquare"));
        Assert.IsFalse(Allowed("Assets/1.Scripts/Monster/Boss/BossPatternVisuals.cs", "BuildCrosshair"));
    }
}
