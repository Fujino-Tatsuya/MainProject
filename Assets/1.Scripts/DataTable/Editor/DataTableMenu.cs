using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

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

/// <summary>
/// 실제 에셋 조회. 시트 이름 = 타입 이름(네임스페이스 없이). 하위 타입은 섞지 않는다(정확히 같은 타입만).
/// <list type="bullet">
/// <item><b>SO 타입</b> — Id = 에셋 파일 이름. <c>Assets/</c> 전체에서 그 타입 에셋을 찾는다.</item>
/// <item><b>컴포넌트 타입</b> — Id = 그 컴포넌트를 가진 <b>프리팹 파일 이름</b>(Variant 도 각자 Id). 프리팹 안(자식 포함)에 그 타입이 정확히 하나여야 한다.
/// 프리팹 전체를 훑는 건 비싸므로 Id 로 파일을 찾아 그 프리팹만 연다 → "행이 없다" 경고는 컴포넌트 시트엔 없다.</item>
/// </list>
/// 결과는 인스턴스 안에서 캐시한다 — <see cref="DataTableSource.Load"/> 한 번에 하나씩 만든다.
/// </summary>
public sealed class AssetDatabaseLookup : IDataTableAssetLookup
{
    private readonly Dictionary<Type, Dictionary<string, List<string>>> scriptableObjectPaths = new Dictionary<Type, Dictionary<string, List<string>>>();
    private Dictionary<string, List<string>> prefabPaths;

    public Type FindType(string sheetName, out string error)
    {
        Type[] matches = TypeCache.GetTypesDerivedFrom<ScriptableObject>()
            .Concat(TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
            .Where(t => !t.IsAbstract && !t.IsGenericType && t.Name == sheetName)
            .Distinct()
            .ToArray();

        error = matches.Length switch
        {
            0 => $"시트 이름 '{sheetName}' 과 같은 이름의 ScriptableObject·컴포넌트 타입이 없다(시트 이름 = 타입 이름).",
            1 => null,
            _ => $"'{sheetName}' 이름의 타입이 여럿이다: {string.Join(", ", matches.Select(t => t.FullName))}",
        };
        return matches.Length == 1 ? matches[0] : null;
    }

    public Object FindTarget(Type type, string id, out string error)
    {
        return typeof(ScriptableObject).IsAssignableFrom(type)
            ? FindScriptableObject(type, id, out error)
            : FindComponentInPrefab(type, id, out error);
    }

    public IReadOnlyCollection<string> AllIds(Type type)
    {
        return typeof(ScriptableObject).IsAssignableFrom(type) ? ScriptableObjectPaths(type).Keys : null;
    }

    private Object FindScriptableObject(Type type, string id, out string error)
    {
        if (!ScriptableObjectPaths(type).TryGetValue(id, out List<string> paths))
        {
            error = $"{type.Name} 에셋 '{id}' 가 없다. 새 항목이면 프로그래머가 SO 를 먼저 만들어야 한다(테이블은 에셋을 만들지 않는다).";
            return null;
        }

        if (paths.Count > 1)
        {
            error = $"{type.Name} 에셋 '{id}' 가 여러 개다 — 파일 이름을 겹치지 않게 할 것: {string.Join(", ", paths)}";
            return null;
        }

        error = null;
        return AssetDatabase.LoadAssetAtPath(paths[0], type);
    }

    private Dictionary<string, List<string>> ScriptableObjectPaths(Type type)
    {
        if (!scriptableObjectPaths.TryGetValue(type, out Dictionary<string, List<string>> byName))
        {
            byName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets("t:" + type.Name, new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.GetMainAssetTypeAtPath(path) == type)
                {
                    AddPath(byName, path);
                }
            }

            scriptableObjectPaths[type] = byName;
        }

        return byName;
    }

    private Object FindComponentInPrefab(Type type, string id, out string error)
    {
        if (prefabPaths == null)
        {
            prefabPaths = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                AddPath(prefabPaths, AssetDatabase.GUIDToAssetPath(guid));
            }
        }

        if (!prefabPaths.TryGetValue(id, out List<string> paths))
        {
            error = $"프리팹 '{id}' 가 없다(Id = 프리팹 파일 이름).";
            return null;
        }

        // 같은 이름 프리팹이 여럿이면 그 타입을 가진 것만 후보.
        var candidates = new List<(string path, Component component)>();
        foreach (string path in paths)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Component[] found = root == null
                ? Array.Empty<Component>()
                : root.GetComponentsInChildren(type, includeInactive: true).Where(c => c.GetType() == type).ToArray();
            if (found.Length > 1)
            {
                error = $"프리팹 '{path}' 안에 {type.Name} 이 {found.Length}개다 — 테이블 행 하나로 가리킬 수 없다.";
                return null;
            }

            if (found.Length == 1)
            {
                candidates.Add((path, found[0]));
            }
        }

        if (candidates.Count == 1)
        {
            error = null;
            return candidates[0].component;
        }

        error = candidates.Count == 0
            ? $"프리팹 '{id}' 에 {type.Name} 컴포넌트가 없다."
            : $"{type.Name} 을 가진 '{id}' 프리팹이 여러 개다 — 파일 이름을 겹치지 않게 할 것: {string.Join(", ", candidates.Select(c => c.path))}";
        return null;
    }

    private static void AddPath(Dictionary<string, List<string>> byName, string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (!byName.TryGetValue(name, out List<string> list))
        {
            list = new List<string>();
            byName[name] = list;
        }

        list.Add(path);
    }
}

/// <summary>Tools/Data 메뉴 — Verify(차이만 보고) · 인스펙터 값(SO·프리팹)을 테이블 값으로 덮어쓰기 · 폴더 열기.</summary>
public static class DataTableMenu
{
    private const string Root = "Tools/Data/";
    private const string LogPrefix = "[DataTable] ";

    [MenuItem(Root + "Verify (인스펙터 값과 테이블 차이 보기)", priority = 0)]
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

        // 씬 인스턴스가 테이블 필드를 오버라이드하면 그 인스턴스엔 테이블 값이 안 닿는다(D6-4).
        List<DataTableSceneOverrides.Finding> overrides = DataTableSceneOverrides.Find(result.Writes);
        foreach (DataTableSceneOverrides.Finding finding in overrides)
        {
            Debug.LogWarning(LogPrefix + finding, AssetDatabase.LoadAssetAtPath<SceneAsset>(finding.ScenePath));
        }

        // 결과는 대화상자 대신 Console·알림 — 자주 누르는 메뉴라 클릭 한 번 줄이고, 자동화로 눌러도 멈추지 않게.
        string summary = (differences.Count == 0
            ? $"Verify: 차이 없음 — 필드 {result.Writes.Count}개가 테이블과 같다."
            : $"Verify: 인스펙터 값(SO·프리팹) 과 테이블 값이 다른 필드 {differences.Count}개 / 전체 {result.Writes.Count}개 — 목록은 위.") +
            (overrides.Count > 0 ? $" ⚠️ 씬 오버라이드 {overrides.Count}개(테이블 값이 안 닿음)." : string.Empty);
        Debug.Log(LogPrefix + summary);
        EditorWindow.focusedWindow?.ShowNotification(new GUIContent(summary));
    }

    [MenuItem(Root + "인스펙터 값을 테이블 값으로 덮어쓰기", priority = 20)]
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

        if (!EditorUtility.DisplayDialog("인스펙터 값을 테이블 값으로 덮어쓰기",
                $"디스크의 SO·프리팹 필드 {differences.Count}개를 테이블 값으로 바꾼다. 인스펙터에서 실험하던 값은 사라진다(Undo 가능).",
                "덮어쓰기", "취소"))
        {
            return;
        }

        DataTableApplier.ApplyToDisk(result.Writes, keepBackup: false);
        Debug.Log($"{LogPrefix}SO·프리팹 필드 {differences.Count}개를 테이블 값으로 덮어썼다.");
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
