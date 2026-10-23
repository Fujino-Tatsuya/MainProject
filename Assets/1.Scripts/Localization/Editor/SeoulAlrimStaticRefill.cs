using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 기본 UI 글꼴 <b>서울알림체 Medium</b> TMP 에셋을 <b>정적(Static)</b>으로 다시 채운다(10-23 결정 a).
/// 동적이면 Play 중 새 글자마다 에셋이 바뀌어 모든 워크트리에 병합 불가 충돌이 생긴다 — 쓸 글자를 미리 굽고 Static 으로 잠근다.
/// 같은 에셋에 더한다(guid·기존 글리프 유지). 새 문구를 넣으면 이 메뉴를 다시 돌리고 폰트 에셋을 커밋한다(Docs/tech/localization.md §10).
/// 글자 집합 = ASCII + KS X 1001 한글 2350자 + GameData.xlsx Strings·ErrorCodes ko·en + 코드 문구 리터럴 + 문구 SO + 씬·프리팹 라벨 + 기호.
/// </summary>
public static class SeoulAlrimStaticRefill
{
    const string FontAssetPath = "Assets/Fonts/SeoulAlrim/SeoulAlrimTTF-Medium SDF.asset";
    const string SourceFontPath = "Assets/Fonts/SeoulAlrim/SeoulAlrimTTF-Medium.ttf";
    const string LogPrefix = "[SeoulAlrimStatic] ";

    // 문구 SO·씬·프리팹·코드를 훑는 폴더. 코드는 런타임 문구만 — Editor·Tests 폴더(로그·메뉴 문구)는 뺀다.
    const string ScriptsFolder = "Assets/1.Scripts";
    const string ScriptableObjectFolder = "Assets/9.ScriptableObject";
    static readonly string[] LabelFolders = { "Assets/0.Scenes", "Assets/2.Prefabs" };

    const string Symbols = "—–→←↑↓×·•…▼▲★☆※%°~「」『』【】‘’“”○●◆◇■□△▽♥♡√";

    [MenuItem("Tools/UI/Fonts/Refill SeoulAlrim (Static)")]
    static void Refill()
    {
        var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (asset == null) { Debug.LogError($"{LogPrefix}글꼴 에셋이 없다: {FontAssetPath}"); return; }

        var sources = new List<(string name, int count)>();
        string chars = CharacterSet(sources);
        int before = asset.characterTable.Count;
        long sizeBefore = new FileInfo(FontAssetPath).Length;

        // 정적 에셋은 원본 서체 참조가 비어 있다 — 글자를 더하려면 잠깐 Dynamic 으로 두고 서체를 다시 건다(setter 가 internal 이라 직렬화로).
        asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        if (asset.sourceFontFile == null)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (font == null) { Debug.LogError($"{LogPrefix}원본 서체가 없다: {SourceFontPath}"); return; }
            var so = new SerializedObject(asset);
            so.FindProperty("m_SourceFontFile").objectReferenceValue = font;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 반환값·missing 은 쓰지 않는다 — 더할 글자가 하나도 없으면(재실행) TMP 가 요청 전체를 missing 으로 돌려준다.
        asset.TryAddCharacters(chars, out _);
        AttachSubAssets(asset);
        asset.atlasPopulationMode = AtlasPopulationMode.Static;

        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(FontAssetPath);

        long sizeAfter = new FileInfo(FontAssetPath).Length;
        string notFilled = NotFilled(asset, chars);
        Debug.Log($"{LogPrefix}{FontAssetPath} Static 저장 · 요청 {chars.Length}자({string.Join(", ", sources.Select(s => $"{s.name} {s.count}"))})" +
                  $" · 글자 {before} → {asset.characterTable.Count} · 아틀라스 {asset.atlasTextures.Length}장" +
                  $" · 파일 {sizeBefore / 1024}KB → {sizeAfter / 1024}KB");
        if (notFilled.Length > 0)
            Debug.LogWarning($"{LogPrefix}채우지 못한 글자 {notFilled.Length}개(서체에 없음 — 폴백이 그린다): {Describe(notFilled)}");
        else
            Debug.Log($"{LogPrefix}채우지 못한 글자 없음.");
    }

    // 동적 생성 중 만들어진 아틀라스·머티리얼을 에셋 안에 넣는다(안 넣으면 저장 후 텍스처가 사라진다).
    static void AttachSubAssets(TMP_FontAsset asset)
    {
        for (int i = 0; i < asset.atlasTextures.Length; i++)
        {
            Texture2D tex = asset.atlasTextures[i];
            if (tex == null || AssetDatabase.Contains(tex)) continue;
            tex.name = $"{asset.name} Atlas{(i == 0 ? "" : " " + i)}";
            AssetDatabase.AddObjectToAsset(tex, asset);
        }
        if (asset.material != null && !AssetDatabase.Contains(asset.material))
        {
            asset.material.name = asset.name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
        }
    }

    // 요청했는데 에셋 글자 표에 없는 글자(서체에 없는 글자 + 아틀라스가 꽉 차 빠진 것).
    public static string NotFilled(TMP_FontAsset asset, string chars)
    {
        var have = new HashSet<uint>(asset.characterTable.Select(c => c.unicode));
        return new string(chars.Where(c => !have.Contains(c)).ToArray());
    }

    public static string Describe(string chars) =>
        string.Join(" ", chars.Select(c => char.IsWhiteSpace(c) ? $"U+{(int)c:X4}" : $"{c}(U+{(int)c:X4})"));

    /// <summary>구울 글자 전부(정렬·중복 제거). <paramref name="sources"/> 에 출처별 글자 수를 남긴다(겹침 포함).</summary>
    public static string CharacterSet(List<(string name, int count)> sources = null)
    {
        var set = new SortedSet<char>();
        void Add(string name, IEnumerable<char> chars)
        {
            var part = new HashSet<char>(chars.Where(Bakeable));
            set.UnionWith(part);
            sources?.Add((name, part.Count));
        }

        Add("ASCII", Enumerable.Range(0x20, 0x7F - 0x20).Select(i => (char)i));
        Add("KS X 1001 한글", KsX1001Hangul());
        Add("xlsx Strings·ErrorCodes", XlsxStrings());
        Add("코드 문구", UiStrings.All.SelectMany(s => s.Ko ?? string.Empty).Concat(CodeLiterals()));
        Add("문구 SO", YamlText(ScriptableObjectFolder, "*.asset", null));
        Add("씬·프리팹 라벨", LabelFolders.SelectMany(f => YamlText(f, "*.prefab", "m_text:").Concat(YamlText(f, "*.unity", "m_text:"))));
        Add("기호", Symbols);
        return new string(set.ToArray());
    }

    // 서로게이트(이모지 등)·제어 문자·사용자 정의 영역·이체 선택자(⚠️ 의 U+FE0F)·폭 없는 문자는 굽지 않는다.
    static bool Bakeable(char c) =>
        !char.IsControl(c) && !char.IsSurrogate(c) && !(c >= '' && c <= '')
        && !(c >= '︀' && c <= '️') && !(c >= '​' && c <= '‏') && c != '﻿';

    /// <summary>KS X 1001 완성형 한글 2350자 = EUC-KR 0xB0A1–0xC8FE.</summary>
    public static IEnumerable<char> KsX1001Hangul()
    {
        Encoding euckr = Encoding.GetEncoding(51949);
        var bytes = new byte[2];
        for (int lead = 0xB0; lead <= 0xC8; lead++)
        {
            for (int trail = 0xA1; trail <= 0xFE; trail++)
            {
                bytes[0] = (byte)lead;
                bytes[1] = (byte)trail;
                string s = euckr.GetString(bytes);
                if (s.Length == 1 && s[0] >= '가' && s[0] <= '힣') yield return s[0];
            }
        }
    }

    // 원본 = SVN GameData.xlsx(StringTable.asset 이 아니다) — 데이터 테이블과 같은 리더·파서.
    static IEnumerable<char> XlsxStrings()
    {
        var chars = new List<char>();
        var issues = new DataTableIssues();
        foreach (string file in DataTableSource.Files())
        {
            IReadOnlyList<XlsxSheet> sheets = XlsxReader.Read(file);
            foreach (StringsSheet sheet in StringsSheet.All)
            {
                foreach (StringRow row in sheet.Parse(Path.GetFileName(file), sheets, issues))
                {
                    chars.AddRange(row.Ko ?? string.Empty);
                    chars.AddRange(row.En ?? string.Empty);
                }
            }
        }
        return chars;
    }

    // 런타임 .cs 의 문자열 리터럴 안 비ASCII 글자(UiString 기본값·LobbyUiStrings 등). 주석은 건너뛴다.
    static IEnumerable<char> CodeLiterals()
    {
        foreach (string file in Directory.GetFiles(ScriptsFolder, "*.cs", SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("/Editor/") || normalized.Contains("/Tests/")) continue;
            foreach (char c in StringLiteralChars(File.ReadAllText(file)))
                if (c > '\u007F') yield return c;
        }
    }

    /// <summary>C# 소스에서 문자열·문자 리터럴 안 글자만(<c>//</c>·<c>/* */</c> 주석 제외). 보간 구멍 안 코드도 섞이지만 ASCII 라 무해하다.</summary>
    public static IEnumerable<char> StringLiteralChars(string src)
    {
        int i = 0;
        while (i < src.Length)
        {
            char c = src[i];
            char next = i + 1 < src.Length ? src[i + 1] : '\0';
            if (c == '/' && next == '/') { while (i < src.Length && src[i] != '\n') i++; continue; }
            if (c == '/' && next == '*') { int end = src.IndexOf("*/", i + 2, StringComparison.Ordinal); i = end < 0 ? src.Length : end + 2; continue; }
            if (c == '"' || c == '\'')
            {
                bool verbatim = c == '"' && i > 0 && (src[i - 1] == '@' || (src[i - 1] == '$' && i > 1 && src[i - 2] == '@'));
                i++;
                while (i < src.Length)
                {
                    char d = src[i];
                    if (!verbatim && d == '\\') { i += 2; continue; }
                    if (d == c)
                    {
                        if (verbatim && i + 1 < src.Length && src[i + 1] == '"') { yield return '"'; i += 2; continue; }
                        break;
                    }
                    if (!verbatim && d == '\n') break;
                    yield return d;
                    i++;
                }
                i++;
                continue;
            }
            i++;
        }
    }

    static readonly Regex UnicodeEscape = new Regex(@"\\u([0-9A-Fa-f]{4})");
    static readonly Regex YamlKeyLine = new Regex(@"^\s*(- )?[A-Za-z_][A-Za-z0-9_]*:( |$)|^---");

    // Unity YAML 은 비ASCII 를 "가" 로 쓰기도, 그대로 쓰기도 한다 — 둘 다 모은다. lineKey 가 있으면 그 키로 시작하는 줄과 이어지는 줄만.
    static IEnumerable<char> YamlText(string folder, string pattern, string lineKey)
    {
        if (!Directory.Exists(folder)) yield break;
        foreach (string file in Directory.GetFiles(folder, pattern, SearchOption.AllDirectories))
        {
            bool inValue = false;
            foreach (string line in File.ReadLines(file))
            {
                if (lineKey != null)
                {
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith(lineKey, StringComparison.Ordinal)) inValue = true;
                    else if (inValue && YamlKeyLine.IsMatch(line)) inValue = false;
                    if (!inValue) continue;
                }
                foreach (char c in DecodeYamlEscapes(line))
                    if (c > '\u007F') yield return c;
            }
        }
    }

    public static string DecodeYamlEscapes(string line) =>
        line.IndexOf("\\u", StringComparison.Ordinal) < 0
            ? line
            : UnicodeEscape.Replace(line, m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
}
