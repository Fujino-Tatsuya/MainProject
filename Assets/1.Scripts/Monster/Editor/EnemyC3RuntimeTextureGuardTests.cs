using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 57 S4b — 재발 감시(사용자 결정 10-20). 런타임 코드가 CPU 로 텍스처를 만들거나 칠하지 않는지 소스를 읽어 검사한다.
/// 범위 = Assets/1.Scripts/Monster/** · Assets/1.Scripts/Rendering/Telegraph/** 의 .cs 중 Editor 폴더 밖.
/// 금지 = Texture2D 생성(new Texture2D · 정규화 이름 UnityEngine./global:: · using 별칭 · Texture2D 로 선언된 변수·필드·반환의
///        target-typed new()) · SetPixel / SetPixels / SetPixels32 / SetPixelData · Sprite.Create(정규화 이름·별칭·using static 포함).
/// 할 일 57 R — 줄 단위 정규식 대신 작은 렉서로 주석·문자열(일반·verbatim·보간, 문자 리터럴)을 공백으로 지운 뒤
/// 파일 전체를 공백 무관(\s*) 정규식으로 검사한다 → 줄바꿈된 생성식·닫힌 블록 주석 뒤 코드도 걸리고, 문자열 속 금지어는 무시된다.
/// Roslyn 은 에디터 참조가 불확실해 쓰지 않는다. 알려진 한계: 의미 분석이 없어 인자 위치의 target-typed new(Foo(new(1,1)))는 못 본다.
/// 위반이 나오면 파일:줄과 그 줄을 메시지에 찍는다. 런타임 예고는 셰이더(EnemyC3Plate·SG_EnemyC3Decal)로 그리고,
/// CPU 기준 그림은 에디터 전용 EnemyC3BakeReference · LegacyDecalTextureReference 에만 둔다. 예외 0(S4c2 확정).
/// </summary>
public sealed class EnemyC3RuntimeTextureGuardTests
{
    static readonly string[] Roots = { "Assets/1.Scripts/Monster", "Assets/1.Scripts/Rendering/Telegraph" };

    const string Qual = @"(?:global\s*::\s*)?(?:UnityEngine\s*\.\s*)?";
    static readonly Regex TextureAlias = new Regex(@"\busing\s+(\w+)\s*=\s*" + Qual + @"Texture2D\s*;");
    static readonly Regex SpriteAlias = new Regex(@"\busing\s+(\w+)\s*=\s*" + Qual + @"Sprite\s*;");
    static readonly Regex SpriteStatic = new Regex(@"\busing\s+static\s+" + Qual + @"Sprite\s*;");
    static readonly Regex SetPixelCall = new Regex(@"\bSetPixel(?:s|s32|Data)?\b");

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

    // ── 렉서: 주석·문자열·문자 리터럴을 공백으로(줄바꿈은 남겨 줄 번호 유지). 보간 문자열의 {구멍} 은 코드로 남긴다.
    static void Blank(char[] o, int from, int to)
    {
        for (int k = from; k < to && k < o.Length; k++) if (o[k] != '\n' && o[k] != '\r') o[k] = ' ';
    }

    static int Code(string s, int i, char[] o, bool hole)
    {
        int depth = 0;
        while (i < s.Length)
        {
            char c = s[i], n = i + 1 < s.Length ? s[i + 1] : '\0';
            if (c == '/' && n == '/')
            {
                int e = s.IndexOf('\n', i); if (e < 0) e = s.Length;
                Blank(o, i, e); i = e; continue;
            }
            if (c == '/' && n == '*')
            {
                int e = s.IndexOf("*/", i + 2, System.StringComparison.Ordinal); e = e < 0 ? s.Length : e + 2;
                Blank(o, i, e); i = e; continue;
            }
            if (c == '\'')
            {
                int j = i + 1;
                while (j < s.Length && s[j] != '\'' && s[j] != '\n') { if (s[j] == '\\') j++; j++; }
                j = System.Math.Min(j + 1, s.Length);
                Blank(o, i, j); i = j; continue;
            }
            if (c == '"' || c == '@' || c == '$')
            {
                int j = i; bool verbatim = false, interp = false;
                while (j < s.Length && j - i < 2 && (s[j] == '@' || s[j] == '$')) { verbatim |= s[j] == '@'; interp |= s[j] == '$'; j++; }
                if (j < s.Length && s[j] == '"')
                {
                    Blank(o, i, j + 1);
                    i = Str(s, j + 1, o, verbatim, interp);
                    continue;
                }
            }
            if (hole)
            {
                if (c == '{') depth++;
                else if (c == '}') { if (depth == 0) return i; depth--; }
            }
            i++;
        }
        return i;
    }

    static int Str(string s, int i, char[] o, bool verbatim, bool interp)
    {
        while (i < s.Length)
        {
            char c = s[i], n = i + 1 < s.Length ? s[i + 1] : '\0';
            if (!verbatim && c == '\\') { Blank(o, i, i + 2); i += 2; continue; }
            if (c == '"')
            {
                if (verbatim && n == '"') { Blank(o, i, i + 2); i += 2; continue; }
                Blank(o, i, i + 1); return i + 1;
            }
            if (interp && c == '{')
            {
                if (n == '{') { Blank(o, i, i + 2); i += 2; continue; }
                o[i] = ' ';
                i = Code(s, i + 1, o, true);           // 구멍 = 코드(안의 new Texture2D 도 잡는다)
                if (i < s.Length) { o[i] = ' '; i++; }
                continue;
            }
            if (interp && c == '}' && n == '}') { Blank(o, i, i + 2); i += 2; continue; }
            if (!verbatim && c == '\n') return i;      // 닫히지 않은 일반 문자열 — 줄 끝에서 끊는다
            Blank(o, i, i + 1); i++;
        }
        return i;
    }

    /// <summary>주석·문자열·문자 리터럴을 공백으로 지운 소스(길이·줄바꿈 동일).</summary>
    public static string StripCommentsAndStrings(string source)
    {
        var o = source.ToCharArray();
        Code(source, 0, o, false);
        return new string(o);
    }

    static string Alt(IEnumerable<string> names) => string.Join("|", names.Select(Regex.Escape));

    /// <summary>소스 한 파일의 금지 호출(주석·문자열 제외, 줄당 1개)과 그 줄을 감싼 메서드 이름.</summary>
    public static List<Hit> Scan(string source)
    {
        source = source.Replace("\r\n", "\n");
        string code = StripCommentsAndStrings(source);
        string[] lines = source.Split('\n'), codeLines = code.Split('\n');

        var lineStart = new int[codeLines.Length];
        for (int i = 1; i < codeLines.Length; i++) lineStart[i] = lineStart[i - 1] + codeLines[i - 1].Length + 1;
        int LineOf(int index) { int l = System.Array.BinarySearch(lineStart, index); return l >= 0 ? l : ~l - 1; }

        // 타입 이름 — Texture2D(정규화 이름 포함) + using 별칭.
        var texAliases = TextureAlias.Matches(code).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        string tex = "(?:" + Qual + "Texture2D" + (texAliases.Count > 0 ? "|(?:global\\s*::\\s*)?(?:" + Alt(texAliases) + ")" : "") + ")";
        var spriteAliases = SpriteAlias.Matches(code).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        string sprite = "(?:" + Qual + "Sprite" + (spriteAliases.Count > 0 ? "|(?:global\\s*::\\s*)?(?:" + Alt(spriteAliases) + ")" : "") + ")";

        var found = new List<int>();   // 위반 시작 위치(코드 인덱스)
        void Add(Regex r) { foreach (Match m in r.Matches(code)) found.Add(m.Index); }

        Add(new Regex(@"\bnew\s+" + tex + @"\b(?!\s*\.)"));                    // new Texture2D / new UnityEngine.Texture2D / new T(별칭)
        Add(SetPixelCall);
        Add(new Regex(@"(?<![\w.])" + sprite + @"\s*\.\s*Create\b"));           // Sprite.Create / UnityEngine.Sprite.Create / S.Create
        if (SpriteStatic.IsMatch(code)) Add(new Regex(@"(?<![\w.])Create\s*\("));  // using static UnityEngine.Sprite;

        // target-typed new — Texture2D 로 선언된 변수·필드·프로퍼티 이름에 new(...) 를 넣는 곳(선언·나중 대입·다중 선언자).
        var names = new Regex(@"(?<![\w.])" + tex + @"\s*\??\s+(\w+)\s*(?=[=;,){])").Matches(code).Cast<Match>()
            .Select(m => m.Groups[1].Value).Where(n => n != "in" && n != "is" && n != "as").Distinct().ToList();
        if (names.Count > 0)
        {
            Add(new Regex(@"(?<![\w.])(?:" + Alt(names) + @")\s*=\s*new\s*\("));
            Add(new Regex(@"(?<![\w.])(?:" + Alt(names) + @")\s*\{[^{}]*\}\s*=\s*new\s*\("));   // 자동 프로퍼티 초기화
        }
        // Texture2D 를 돌려주는 메서드·프로퍼티의 => new(...) / 본문 안 return new(...).
        Add(new Regex(@"(?<![\w.])" + tex + @"\s*\??\s+\w+\s*(?:<[^<>]*>)?\s*(?:\([^()]*\))?\s*=>\s*new\s*\("));
        foreach (Match m in new Regex(@"(?<![\w.])" + tex + @"\s*\??\s+\w+\s*(?:<[^<>]*>)?\s*\([^()]*\)\s*(?:where[^{;]*)?\{").Matches(code))
        {
            int open = m.Index + m.Length - 1, depth = 0, close = open;
            for (; close < code.Length; close++)
            {
                if (code[close] == '{') depth++;
                else if (code[close] == '}' && --depth == 0) break;
            }
            foreach (Match r in new Regex(@"\breturn\s+new\s*\(").Matches(code.Substring(open, close - open))) found.Add(open + r.Index);
        }

        // 메서드 귀속 — 줄 단위(주석·문자열을 지운 줄로).
        var methodAt = new string[codeLines.Length];
        string method = null;
        for (int i = 0; i < codeLines.Length; i++)
        {
            string line = codeLines[i], trimmed = line.TrimStart();
            var decl = MemberDecl.Match(line);
            if (decl.Success) method = decl.Groups[1].Value;
            else if (line.StartsWith("    ") && !line.StartsWith("     ") && trimmed.Length > 0 && trimmed != "{" && trimmed != "}"
                     && !trimmed.StartsWith("[")) method = null; // 필드·프로퍼티 등 다른 멤버 — 앞 메서드를 이어받지 않는다
            methodAt[i] = method;
        }

        return found.Select(LineOf).Distinct().OrderBy(l => l)
            .Select(l => new Hit(l + 1, methodAt[l], lines[l].Trim())).ToList();
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

    // 할 일 57 R — 우회 사례. 줄 끝 표시 "//HIT" = 그 줄이 걸려야 한다, "//MISS" = 걸리면 안 된다(표시는 주석이라 검사 대상이 아니다).
    // 표시 없는 줄도 걸리면 안 된다.
    const string Bypass =
        "using T = UnityEngine.Texture2D;\n" +
        "using S = global::UnityEngine.Sprite;\n" +
        "public class Bypass\n" +
        "{\n" +
        "    static readonly UnityEngine.Texture2D A = new UnityEngine.Texture2D(1, 1); //HIT\n" +
        "    static readonly global::UnityEngine.Texture2D B = new global::UnityEngine.Texture2D(1, 1); //HIT\n" +
        "    static readonly Texture2D F = new(4, 4); //HIT\n" +
        "    static Texture2D P { get; } = new(2, 2); //HIT\n" +
        "    static T Alias() => new T(1, 1); //HIT\n" +
        "    static Texture2D Arrow() => new(1, 1); //HIT\n" +
        "    static Texture2D Block()\n" +
        "    {\n" +
        "        Texture2D t = new(8, 8); //HIT\n" +
        "        t = new(9, 9); //HIT\n" +
        "        var u = new //HIT\n" +
        "            Texture2D(3, 3);\n" +
        "        /* closed */ var v = new Texture2D(5, 5); //HIT\n" +
        "        string s = \"new Texture2D(1,1) SetPixels Sprite.Create\"; //MISS\n" +
        "        string w = @\"SetPixel(\"\" new Texture2D\"; //MISS\n" +
        "        string x = $\"{{SetPixel}} Sprite.Create {s.Length} \\\" new Texture2D\"; //MISS\n" +
        "        string y = $@\"SetPixels32 {w} \"\" Sprite.Create\"; //MISS\n" +
        "        char q = '\"'; var z = new Texture2D(6, 6); //HIT\n" +
        "        string h = $\"{new Texture2D(7, 7).width}\"; //HIT\n" +
        "        return new(1, 1); //HIT\n" +
        "    }\n" +
        "    static object Spr(Texture2D t) => UnityEngine.Sprite.Create(t, default, default); //HIT\n" +
        "    static object Spr2(Texture2D t) => global::UnityEngine.Sprite.Create(t, default, default); //HIT\n" +
        "    static object Spr3(Texture2D t) => S.Create(t, default, default); //HIT\n" +
        "    static void Paint(Texture2D t) { t.SetPixelData(new byte[0], 0); } //HIT\n" +
        "    static int[] Ok() => new int[2]; //MISS\n" +
        "}\n";

    [Test]
    public void ScannerCatchesBypasses()
    {
        var hits = Scan(Bypass);
        string dump = string.Join("\n", hits.Select(h => $"{h.line} {h.method} {h.text}"));
        string[] lines = Bypass.Split('\n');
        var expected = Enumerable.Range(0, lines.Length).Where(i => lines[i].EndsWith("//HIT")).Select(i => i + 1).ToArray();
        Assert.Greater(expected.Length, 15, "self-test lost its HIT markers");
        CollectionAssert.AreEqual(expected, hits.Select(h => h.line).ToArray(), dump);
        Assert.AreEqual("Block", hits.Single(h => h.text.StartsWith("t = new(9")).method, dump);
        Assert.AreEqual("Paint", hits.Single(h => h.text.Contains("SetPixelData")).method, dump);
    }

    [Test]
    public void StripperBlanksOnlyCommentsAndStrings()
    {
        const string src = "a/*x\ny*/b // c\n\"d\\\"e\" @\"f\"\"g\" $\"h{i + \"j\"}k\" 'l' '\\'' m";
        string o = StripCommentsAndStrings(src);
        Assert.AreEqual(src.Length, o.Length);
        Assert.AreEqual(src.Count(c => c == '\n'), o.Count(c => c == '\n'), "newlines kept for line numbers");
        string kept = new string(o.Where(c => !char.IsWhiteSpace(c)).ToArray());
        Assert.AreEqual("abi+m", kept);
    }
}
