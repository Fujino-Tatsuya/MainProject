using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 테이블 원본 위치와 "폴더 전체 읽기 → 검증 → SO 연결" 한 번에 하는 진입점.
/// 테이블 Play(D1.5)·빌드(D1.6)·메뉴가 모두 <see cref="Load"/> 하나를 쓴다.
/// </summary>
public static class DataTableSource
{
    /// <summary>
    /// xlsx 폴더(SVN). 이름이 <c>~</c> 로 끝나면 Unity 가 임포트하지 않는다 —
    /// Excel 이 편집 중 만드는 잠금 파일(<c>~$Player.xlsx</c>)에 .meta 가 생겨 SVN 이 더러워지는 걸 막는다.
    /// 🔴 PLAN-data-table.md §8 Q2(위치) 확정 전 잠정값.
    /// </summary>
    public const string Folder = "Assets/50.Art/DataTable~";

    public sealed class Result
    {
        public Result(List<DataTableWrite> writes, DataTableIssues issues, int fileCount)
        {
            Writes = writes;
            Issues = issues;
            FileCount = fileCount;
        }

        public List<DataTableWrite> Writes { get; }
        public DataTableIssues Issues { get; }
        public int FileCount { get; }
    }

    public static Result Load()
    {
        var issues = new DataTableIssues();
        var entries = new List<DataTableEntry>();
        string[] files = Directory.Exists(Folder)
            ? Directory.GetFiles(Folder, "*.xlsx", SearchOption.TopDirectoryOnly)
                .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();

        foreach (string file in files)
        {
            string fileName = Path.GetFileName(file);
            try
            {
                entries.AddRange(DataTableSchema.Parse(fileName, XlsxReader.Read(file), issues));
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is System.Xml.XmlException)
            {
                issues.Error(fileName, $"xlsx 를 읽지 못했다: {e.Message}");
            }
        }

        List<DataTableWrite> writes = DataTableApplier.Bind(entries, new AssetDatabaseLookup(), issues);
        return new Result(writes, issues, files.Length);
    }
}

/// <summary>시트 이름 = SO 타입 이름(네임스페이스 없이), Id = 에셋 파일 이름. 하위 타입은 섞지 않는다(정확히 같은 타입만).</summary>
public sealed class AssetDatabaseLookup : IDataTableAssetLookup
{
    public Type FindType(string sheetName, out string error)
    {
        Type[] matches = TypeCache.GetTypesDerivedFrom<ScriptableObject>()
            .Where(t => !t.IsAbstract && !t.IsGenericType && t.Name == sheetName)
            .ToArray();

        error = matches.Length switch
        {
            0 => $"시트 이름 '{sheetName}' 과 같은 이름의 ScriptableObject 타입이 없다(시트 이름 = SO 타입 이름).",
            1 => null,
            _ => $"'{sheetName}' 이름의 SO 타입이 여럿이다: {string.Join(", ", matches.Select(t => t.FullName))}",
        };
        return matches.Length == 1 ? matches[0] : null;
    }

    public IReadOnlyDictionary<string, ScriptableObject> AssetsOf(Type type, out IReadOnlyList<string> duplicateNames)
    {
        var byName = new Dictionary<string, ScriptableObject>(StringComparer.Ordinal);
        var duplicates = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:" + type.Name, new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetDatabase.LoadMainAssetAtPath(path) is ScriptableObject asset) || asset.GetType() != type)
            {
                continue;
            }

            string name = Path.GetFileNameWithoutExtension(path);
            if (byName.ContainsKey(name))
            {
                duplicates.Add(name);
                continue;
            }

            byName[name] = asset;
        }

        duplicateNames = duplicates;
        return byName;
    }
}

/// <summary>Tools/Data 메뉴 — Verify(차이만 보고) · 디스크 SO 를 테이블 값으로 덮어쓰기 · 폴더 열기.</summary>
public static class DataTableMenu
{
    private const string Root = "Tools/Data/";
    private const string LogPrefix = "[DataTable] ";

    [MenuItem(Root + "Verify (SO 와 테이블 차이 보기)", priority = 0)]
    private static void Verify()
    {
        DataTableSource.Result result = DataTableSource.Load();
        if (!ReportIssues(result, "Verify"))
        {
            return;
        }

        List<DataTableDifference> differences = DataTableApplier.Diff(result.Writes);
        foreach (DataTableDifference difference in differences)
        {
            Debug.Log(LogPrefix + difference, difference.Write.Target);
        }

        EditorUtility.DisplayDialog("데이터 테이블 Verify",
            differences.Count == 0
                ? $"차이 없음 — 필드 {result.Writes.Count}개가 테이블과 같다."
                : $"SO(개발자 값) 와 테이블(기획 값) 이 다른 필드 {differences.Count}개 / 전체 {result.Writes.Count}개.\n목록은 Console.",
            "확인");
    }

    [MenuItem(Root + "SO 를 테이블 값으로 덮어쓰기", priority = 20)]
    private static void OverwriteDisk()
    {
        DataTableSource.Result result = DataTableSource.Load();
        if (!ReportIssues(result, "덮어쓰기"))
        {
            return;
        }

        List<DataTableDifference> differences = DataTableApplier.Diff(result.Writes);
        if (differences.Count == 0)
        {
            EditorUtility.DisplayDialog("데이터 테이블", "이미 테이블과 같다. 바꿀 것이 없다.", "확인");
            return;
        }

        if (!EditorUtility.DisplayDialog("SO 를 테이블 값으로 덮어쓰기",
                $"디스크의 SO 필드 {differences.Count}개를 테이블 값으로 바꾼다. 인스펙터에서 실험하던 값은 사라진다(Undo 가능).",
                "덮어쓰기", "취소"))
        {
            return;
        }

        DataTableApplier.ApplyToDisk(result.Writes);
        Debug.Log($"{LogPrefix}SO 필드 {differences.Count}개를 테이블 값으로 덮어썼다.");
    }

    [MenuItem(Root + "테이블 폴더 열기", priority = 40)]
    private static void RevealFolder()
    {
        if (!Directory.Exists(DataTableSource.Folder))
        {
            EditorUtility.DisplayDialog("데이터 테이블", $"폴더가 없다: {DataTableSource.Folder}\nSVN 업데이트를 확인할 것.", "확인");
            return;
        }

        EditorUtility.RevealInFinder(DataTableSource.Folder);
    }

    /// <summary>문제를 Console 에 찍고, 오류가 있거나 읽은 파일이 없으면 false.</summary>
    internal static bool ReportIssues(DataTableSource.Result result, string action)
    {
        foreach (DataTableIssue issue in result.Issues.Items)
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

        if (result.FileCount == 0)
        {
            EditorUtility.DisplayDialog("데이터 테이블", $"xlsx 가 없다: {DataTableSource.Folder}", "확인");
            return false;
        }

        if (result.Issues.HasErrors)
        {
            int errors = result.Issues.Items.Count(i => i.Kind == DataTableIssueKind.Error);
            var preview = new StringBuilder();
            foreach (DataTableIssue issue in result.Issues.Items.Where(i => i.Kind == DataTableIssueKind.Error).Take(5))
            {
                preview.AppendLine("• " + issue);
            }

            EditorUtility.DisplayDialog($"데이터 테이블 {action} 중단",
                $"오류 {errors}개 — 아무 SO 도 바꾸지 않았다.\n\n{preview}{(errors > 5 ? "…나머지는 Console\n" : string.Empty)}",
                "확인");
            return false;
        }

        return true;
    }
}
