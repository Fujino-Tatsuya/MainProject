using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 문자열 시트(Strings·ErrorCodes, 검증 통과분) → <see cref="StringTable"/> 에셋(<see cref="StringTable.AssetPath"/>) 한 개.
/// Strings 는 <see cref="StringTable.Entries"/>, ErrorCodes 는 <see cref="StringTable.Errors"/> 로 간다.
/// 테이블 Play·빌드는 <see cref="DataTableSource.Load"/> 결과로 <see cref="Write"/> 하고(오류면 Play·빌드가 멈춤),
/// 인스펙터 Play 는 <see cref="RefreshForInspectorPlay"/> 로 문자열 시트만 읽는다(오류면 막지 않고 기존 표 유지).
/// 내용이 같으면 파일을 다시 쓰지 않는다 — git 변경은 문구가 바뀌었을 때만.
/// </summary>
public static class StringTableBuild
{
    private const string LogPrefix = "[Loc] ";

    /// <summary>표 에셋을 만들거나 내용이 다르면 갱신한다. 바뀌었으면 true.</summary>
    public static bool Write(IReadOnlyList<LocalizedString> strings, IReadOnlyList<LocalizedString> errorCodes)
    {
        StringTable.Entry[] entries = ToEntries(strings);
        StringTable.Entry[] errors = ToEntries(errorCodes);
        var table = AssetDatabase.LoadAssetAtPath<StringTable>(StringTable.AssetPath);
        bool created = table == null;
        if (created)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StringTable.AssetPath));
            table = ScriptableObject.CreateInstance<StringTable>();
            table.SetEntries(entries);
            table.SetErrors(errors);
            AssetDatabase.CreateAsset(table, StringTable.AssetPath);
        }
        else if (SameEntries(table.Entries, entries) && SameEntries(table.Errors, errors))
        {
            return false;
        }
        else
        {
            table.SetEntries(entries);
            table.SetErrors(errors);
            EditorUtility.SetDirty(table);
        }

        AssetDatabase.SaveAssetIfDirty(table);
        Loc.SetTable(null); // 다음 조회 때 디스크 에셋을 다시 잡는다(새로 만든 경우 포함)
        Debug.Log($"{LogPrefix}StringTable {(created ? "생성" : "갱신")} — 키 {entries.Length}개 · 오류 코드 {errors.Length}개 ({StringTable.AssetPath}).");
        return true;
    }

    /// <summary>xlsx 의 문자열 시트(Strings·ErrorCodes)만 읽고 검증한다(SO 바인딩 없음).</summary>
    public static List<LocalizedString> LoadStrings(DataTableIssues issues, out List<LocalizedString> errorCodes, out int fileCount)
    {
        string[] files = DataTableSource.Files();
        fileCount = files.Length;
        var rows = new List<StringRow>();
        var errorRows = new List<StringRow>();
        foreach (string file in files)
        {
            string fileName = Path.GetFileName(file);
            try
            {
                IReadOnlyList<XlsxSheet> sheets = XlsxReader.Read(file);
                rows.AddRange(StringsSheet.Strings.Parse(fileName, sheets, issues));
                errorRows.AddRange(StringsSheet.ErrorCodes.Parse(fileName, sheets, issues));
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is System.Xml.XmlException)
            {
                issues.Error(fileName, $"xlsx 를 읽지 못했다: {e.Message}");
            }
        }

        errorCodes = StringTableValidator.Validate(errorRows, issues);
        return StringTableValidator.Validate(rows, issues, StringKeyOwners.Resolver(null));
    }

    /// <summary>인스펙터 Play — 수치는 xlsx 를 무시하지만 문구는 xlsx 가 유일한 원본이라 갱신한다. 오류면 기존 표를 그대로 쓴다.</summary>
    internal static void RefreshForInspectorPlay()
    {
        var issues = new DataTableIssues();
        List<LocalizedString> strings = LoadStrings(issues, out List<LocalizedString> errorCodes, out int fileCount);
        if (fileCount == 0)
        {
            return;
        }

        foreach (DataTableIssue issue in issues.Items)
        {
            if (issue.Kind == DataTableIssueKind.Error)
            {
                Debug.LogError(LogPrefix + issue);
            }
            else
            {
                Debug.LogWarning(LogPrefix + issue);
            }
        }

        if (issues.HasErrors)
        {
            Debug.LogError($"{LogPrefix}문자열 시트 오류 — StringTable 을 갱신하지 않고 기존 표로 돈다(테이블 Play·빌드는 멈춘다).");
            return;
        }

        Write(strings, errorCodes);
    }

    /// <summary>
    /// 빌드 동안만 StringTable 을 Preloaded Assets 에 넣는다 — 씬이 참조하지 않아도 빌드에 들어가고 시작 때 Loc 에 걸린다.
    /// 끝나면 원래 목록으로 되돌린다(ProjectSettings 영구 변경 없음).
    /// </summary>
    public static void WithPreloadedTable(Action build)
    {
        var table = AssetDatabase.LoadAssetAtPath<StringTable>(StringTable.AssetPath);
        Object[] original = PlayerSettings.GetPreloadedAssets();
        bool added = table != null && !original.Contains(table);
        if (added)
        {
            PlayerSettings.SetPreloadedAssets(original.Concat(new Object[] { table }).ToArray());
        }
        else if (table == null)
        {
            Debug.LogWarning($"{LogPrefix}{StringTable.AssetPath} 가 없다 — 빌드에서 모든 문구가 '#키' 로 보인다.");
        }

        try
        {
            build();
        }
        finally
        {
            if (added)
            {
                PlayerSettings.SetPreloadedAssets(original);
            }
        }
    }

    [MenuItem("Tools/Data/StringTable 갱신 (Strings·ErrorCodes 시트 → 에셋)", priority = 45)]
    private static void RefreshMenu()
    {
        var issues = new DataTableIssues();
        List<LocalizedString> strings = LoadStrings(issues, out List<LocalizedString> errorCodes, out int fileCount);
        foreach (DataTableIssue issue in issues.Items)
        {
            if (issue.Kind == DataTableIssueKind.Error)
            {
                Debug.LogError(LogPrefix + issue);
            }
            else
            {
                Debug.LogWarning(LogPrefix + issue);
            }
        }

        if (issues.HasErrors)
        {
            Debug.LogError($"{LogPrefix}문자열 시트 오류 — StringTable 을 갱신하지 않았다.");
            return;
        }

        bool changed = Write(strings, errorCodes);
        Debug.Log($"{LogPrefix}xlsx {fileCount}개 · 키 {strings.Count}개 · 오류 코드 {errorCodes.Count}개 — {(changed ? "갱신함" : "변경 없음")}.");
    }

    private static StringTable.Entry[] ToEntries(IReadOnlyList<LocalizedString> strings) =>
        (strings ?? Array.Empty<LocalizedString>()).Select(s => new StringTable.Entry(s.Key, s.Ko, s.En)).ToArray();

    private static bool SameEntries(IReadOnlyList<StringTable.Entry> current, StringTable.Entry[] next)
    {
        if (current.Count != next.Length)
        {
            return false;
        }

        for (int i = 0; i < next.Length; i++)
        {
            if (current[i].key != next[i].key || current[i].ko != next[i].ko || current[i].en != next[i].en)
            {
                return false;
            }
        }

        return true;
    }
}
