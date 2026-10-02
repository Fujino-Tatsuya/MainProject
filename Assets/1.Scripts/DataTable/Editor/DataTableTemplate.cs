using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 현재 인스펙터 값(SO·프리팹) → xlsx 템플릿(PLAN-data-table.md D2·D3·D4). 기획이 처음 받을 xlsx 를 만들고, 직후 Verify 차이 0 이 기준선이다.
/// <para>
/// 내보내는 필드 = <b>정수·실수</b>만(구조체·배열 안쪽 포함, 배열은 원소마다 <c>phases[0]</c>).
/// 레이어 마스크·문자열·곡선·참조·bool·enum 은 넣지 않는다 — 가져오기는 bool·enum 도 되므로 필요하면 기획이 행/열을 직접 추가한다.
/// <see cref="DataTableIgnoreAttribute"/> 필드는 뺀다.
/// </para>
/// <para>대상이 하나뿐인 타입 = 키-값 시트(세로로 읽기 좋다), 여럿 = 행 테이블.</para>
/// </summary>
public static class DataTableTemplate
{
    public const string DefaultFileName = "GameData.xlsx";
    private const string LogPrefix = "[DataTable] ";

    public readonly struct Field
    {
        public Field(string path, object value, string description)
        {
            Path = path;
            Value = value;
            Description = description;
        }

        /// <summary>테이블 표기 — "charge.speed", "phases[0]".</summary>
        public string Path { get; }

        public object Value { get; }
        public string Description { get; }
    }

    /// <summary>대상(SO·컴포넌트) 하나에서 템플릿에 들어갈 필드를 직렬화 순서대로. NetworkVariable 안쪽(네트워크 상태)은 뺀다.</summary>
    public static List<Field> CollectFields(Object asset)
    {
        var fields = new List<Field>();
        Type type = asset.GetType();
        var so = new SerializedObject(asset);
        SerializedProperty it = so.GetIterator();
        bool enterChildren = true;
        while (it.Next(enterChildren))
        {
            enterChildren = false;
            string path = it.propertyPath;
            if (path == "m_Script" || path.EndsWith(".Array.size", StringComparison.Ordinal))
            {
                continue;
            }

            FieldInfo field = ResolveField(type, path);
            if (field == null || IsExcluded(field) || IsUnderIgnored(type, path))
            {
                continue;
            }

            switch (it.propertyType)
            {
                case SerializedPropertyType.Generic:
                    enterChildren = true; // 구조체·클래스·배열 안으로
                    break;
                case SerializedPropertyType.Integer:
                    fields.Add(new Field(ToTablePath(path), it.longValue, Describe(field, path)));
                    break;
                case SerializedPropertyType.Float:
                    object value = it.numericType == SerializedPropertyNumericType.Double ? (object)it.doubleValue : it.floatValue;
                    fields.Add(new Field(ToTablePath(path), value, Describe(field, path)));
                    break;
            }
        }

        return fields;
    }

    /// <summary>
    /// 타입별 대상 → 시트. 시트 이름 = 타입 이름, 또는 <c>[DataTableSheet("묶음")]</c> 이면 그 이름으로 여러 타입을 한 시트(키-값)에.
    /// 문제(이름 길이 등)는 warnings 로.
    /// </summary>
    public static List<XlsxWriteSheet> BuildSheets(IEnumerable<(Type type, IReadOnlyList<(string id, Object target)> targets)> tables, List<string> warnings)
    {
        var sheets = new List<XlsxWriteSheet>();
        var all = tables.ToList();

        // 묶음 시트(시트 이름 ≠ 타입 이름) 먼저 — 캐릭터별 시트가 앞에 오게.
        foreach (IGrouping<string, (Type type, IReadOnlyList<(string id, Object target)> targets)> group in all
                     .Where(t => AssetDatabaseLookup.SheetNameOf(t.type) != t.type.Name)
                     .GroupBy(t => AssetDatabaseLookup.SheetNameOf(t.type))
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            string nameError = XlsxWriter.ValidateSheetName(group.Key);
            if (nameError != null)
            {
                warnings.Add($"{group.Key}: {nameError} — 건너뜀.");
                continue;
            }

            sheets.Add(BuildGroupSheet(group.Key, group.OrderBy(t => t.type.Name, StringComparer.Ordinal).ToList()));
        }

        foreach ((Type type, IReadOnlyList<(string id, Object target)> targets) in all
                     .Where(t => AssetDatabaseLookup.SheetNameOf(t.type) == t.type.Name)
                     .OrderBy(t => t.type.Name, StringComparer.Ordinal))
        {
            string nameError = XlsxWriter.ValidateSheetName(type.Name);
            if (nameError != null)
            {
                warnings.Add($"{type.Name}: {nameError} — 건너뜀.");
                continue;
            }

            List<(string id, Object target)> ordered = targets.OrderBy(t => t.id, StringComparer.Ordinal).ToList();
            if (ordered.Count == 0)
            {
                warnings.Add($"{type.Name}: 대상(에셋·프리팹)이 없다 — 건너뜀.");
                continue;
            }

            sheets.Add(ordered.Count == 1
                ? BuildKeyValueSheet(type, ordered[0])
                : BuildRowSheet(type, ordered, warnings));
        }

        return sheets;
    }

    /// <summary>묶음 시트 — 키-값 형식, 타입마다 "#── 타입 ──" 메모 행으로 구역을 나눈다(가져오기는 # 행을 무시).</summary>
    private static XlsxWriteSheet BuildGroupSheet(string sheetName, List<(Type type, IReadOnlyList<(string id, Object target)> targets)> members)
    {
        var rows = new List<object[]>
        {
            new object[] { DataTableSchema.AssetHeader, DataTableSchema.FieldHeader, DataTableSchema.ValueHeader, "#설명" },
        };

        foreach ((Type type, IReadOnlyList<(string id, Object target)> targets) in members)
        {
            if (targets.Count == 0)
            {
                continue;
            }

            rows.Add(new object[] { $"{DataTableSchema.CommentPrefix}── {type.Name} ──" });
            foreach ((string id, Object target) in targets.OrderBy(t => t.id, StringComparer.Ordinal))
            {
                rows.AddRange(CollectFields(target).Select(f => new[] { id, f.Path, f.Value, f.Description }));
            }
        }

        return new XlsxWriteSheet(sheetName, rows, frozenRows: 1, boldRows: 1);
    }

    private static XlsxWriteSheet BuildKeyValueSheet(Type type, (string id, Object target) asset)
    {
        var rows = new List<object[]>
        {
            new object[] { DataTableSchema.AssetHeader, DataTableSchema.FieldHeader, DataTableSchema.ValueHeader, "#설명" },
        };
        rows.AddRange(CollectFields(asset.target).Select(f => new[] { asset.id, f.Path, f.Value, f.Description }));
        return new XlsxWriteSheet(type.Name, rows, frozenRows: 1, boldRows: 1);
    }

    private static XlsxWriteSheet BuildRowSheet(Type type, List<(string id, Object target)> assets, List<string> warnings)
    {
        List<List<Field>> perAsset = assets.Select(a => CollectFields(a.target)).ToList();

        // 열 = 모든 대상의 필드 합집합(배열 길이가 대상마다 달라도 다 싣는다). 그 대상에 없는 칸은 "-"(DataTableSchema.NotApplicable).
        // 순서: 첫 대상 순서를 따르고, 다른 대상에만 있는 필드는 그 대상에서 바로 앞 필드 뒤에 끼운다 — attacks[6].* 가 attacks[5].* 뒤에 오게.
        var columns = new List<Field>();
        var columnIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (List<Field> fields in perAsset)
        {
            int insertAt = 0;
            foreach (Field field in fields)
            {
                if (columnIndex.TryGetValue(field.Path, out int existing))
                {
                    insertAt = existing + 1;
                    continue;
                }

                columns.Insert(insertAt, field);
                for (int c = 0; c < columns.Count; c++)
                {
                    columnIndex[columns[c].Path] = c;
                }

                insertAt++;
            }
        }

        var rows = new List<object[]>
        {
            new object[] { DataTableSchema.IdHeader }.Concat(columns.Select(c => (object)c.Path)).ToArray(),
            new object[] { "설명 →" }.Concat(columns.Select(c => (object)c.Description)).ToArray(),
        };
        for (int i = 0; i < assets.Count; i++)
        {
            Dictionary<string, object> values = perAsset[i].ToDictionary(f => f.Path, f => f.Value);
            rows.Add(new object[] { assets[i].id }
                .Concat(columns.Select(c => values.TryGetValue(c.Path, out object v) ? v : DataTableSchema.NotApplicable))
                .ToArray());
        }

        return new XlsxWriteSheet(type.Name, rows, frozenRows: 2, boldRows: 1);
    }

    /// <summary>"phases.Array.data[0].hp" → "phases[0].hp" (<see cref="DataTableApplier.ToPropertyPath"/> 의 반대).</summary>
    public static string ToTablePath(string propertyPath) => propertyPath.Replace(".Array.data[", "[");

    // ── 필드 메타(무시 표시·툴팁) — 경로 해석은 DataTableFields ───────────────

    private static FieldInfo ResolveField(Type root, string propertyPath) => DataTableFields.Resolve(root, propertyPath);

    private static bool IsExcluded(FieldInfo field) => DataTableFields.IsExcluded(field);

    private static bool IsUnderIgnored(Type root, string propertyPath) => DataTableFields.IsUnderExcluded(root, propertyPath);

    private static string Describe(FieldInfo field, string propertyPath)
    {
        string tooltip = field.GetCustomAttribute<TooltipAttribute>()?.tooltip ?? string.Empty;
        return propertyPath.Contains(".Array.data[") && tooltip.Length > 0 ? tooltip + " (배열 원소)" : tooltip;
    }

    // ── 메뉴 ────────────────────────────────────────────────

    /// <summary>
    /// <see cref="DataTableSheetAttribute"/> 가 붙은 타입의 대상 전부. SO = 에셋(Id = 파일 이름),
    /// 컴포넌트 = 그 컴포넌트를 가진 프리팹(Id = 프리팹 파일 이름, 레거시·아트 폴더 제외). 겹치는 Id·프리팹 안 중복 컴포넌트는 경고 후 뺀다.
    /// </summary>
    public static List<(Type, IReadOnlyList<(string id, Object target)>)> CollectTargets(List<string> warnings)
    {
        Type[] marked = TypeCache.GetTypesWithAttribute<DataTableSheetAttribute>()
            .Where(t => !t.IsAbstract && (typeof(ScriptableObject).IsAssignableFrom(t) || typeof(MonoBehaviour).IsAssignableFrom(t)))
            .ToArray();

        var byType = marked.ToDictionary(t => t, _ => new List<(string id, Object target)>());

        foreach (Type type in marked.Where(t => typeof(ScriptableObject).IsAssignableFrom(t)))
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + type.Name, new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.GetMainAssetTypeAtPath(path) == type && !IsExcludedPath(path))
                {
                    byType[type].Add((Path.GetFileNameWithoutExtension(path), AssetDatabase.LoadAssetAtPath(path, type)));
                }
            }
        }

        var componentTypes = new HashSet<Type>(marked.Where(t => typeof(MonoBehaviour).IsAssignableFrom(t)));
        if (componentTypes.Count > 0)
        {
            string[] prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !IsExcludedPath(p))
                .ToArray();
            try
            {
                for (int i = 0; i < prefabs.Length; i++)
                {
                    if (i % 50 == 0)
                    {
                        EditorUtility.DisplayProgressBar("Export Template", $"프리팹 훑는 중 {i}/{prefabs.Length}", (float)i / prefabs.Length);
                    }

                    var root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabs[i]);
                    if (root == null)
                    {
                        continue;
                    }

                    foreach (IGrouping<Type, MonoBehaviour> group in root.GetComponentsInChildren<MonoBehaviour>(true)
                                 .Where(c => c != null && componentTypes.Contains(c.GetType()))
                                 .GroupBy(c => c.GetType()))
                    {
                        if (group.Count() > 1)
                        {
                            warnings.Add($"{group.Key.Name}: '{prefabs[i]}' 안에 {group.Count()}개 — 행 하나로 가리킬 수 없어 뺐다.");
                            continue;
                        }

                        byType[group.Key].Add((Path.GetFileNameWithoutExtension(prefabs[i]), group.First()));
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        var tables = new List<(Type, IReadOnlyList<(string id, Object target)>)>();
        foreach (KeyValuePair<Type, List<(string id, Object target)>> pair in byType)
        {
            var duplicateIds = new HashSet<string>(pair.Value.GroupBy(t => t.id).Where(g => g.Count() > 1).Select(g => g.Key));
            foreach (string id in duplicateIds)
            {
                warnings.Add($"{pair.Key.Name}: Id '{id}' 가 여러 개(파일 이름이 겹침) — 뺐다. 파일 이름을 바꿀 것.");
            }

            tables.Add((pair.Key, pair.Value.Where(t => !duplicateIds.Contains(t.id)).ToList()));
        }

        return tables;
    }

    /// <summary>게임에 스폰되지 않는 프리팹 — 레거시 폴더, SVN 아트 폴더(검수용 `*_Review` 프리팹 등).</summary>
    internal static bool IsExcludedPath(string path) =>
        path.IndexOf("legacy/", StringComparison.OrdinalIgnoreCase) >= 0 ||
        path.IndexOf("/Lagacy/", StringComparison.OrdinalIgnoreCase) >= 0 ||
        path.StartsWith("Assets/50.Art/", StringComparison.OrdinalIgnoreCase);

    /// <summary>파일을 쓰지 않고 시트별로 나갈 필드만 Console 에 — 기술 값이 섞였는지([DataTableIgnore] 빠짐) 점검용.</summary>
    [MenuItem("Tools/Data/Export Template 필드 목록 보기 (파일 안 씀)", priority = 31)]
    private static void PreviewFields()
    {
        var warnings = new List<string>();
        List<(Type, IReadOnlyList<(string id, Object target)>)> tables = CollectTargets(warnings);
        foreach ((Type type, IReadOnlyList<(string id, Object target)> targets) in tables.OrderBy(t => t.Item1.Name, StringComparer.Ordinal))
        {
            if (targets.Count == 0)
            {
                Debug.Log($"{LogPrefix}[미리보기] {type.Name}: 대상 없음");
                continue;
            }

            List<Field> fields = CollectFields(targets[0].target);
            Debug.Log($"{LogPrefix}[미리보기] {type.Name} — 대상 {targets.Count}개({string.Join(", ", targets.Select(t => t.id))}), " +
                      $"필드 {fields.Count}개: {string.Join(", ", fields.Select(f => f.Path))}");
        }

        foreach (string warning in warnings)
        {
            Debug.LogWarning($"{LogPrefix}[미리보기] {warning}");
        }
    }

    /// <summary>쓰기 전 원본 xlsx 보관 위치(병합·덮어쓰기 모두). SVN 이력과 별개로 바로 되돌릴 수 있게.</summary>
    public const string ExportBackupFolder = "Library/DataTableExportBackup";

    [MenuItem("Tools/Data/Export Template (현재 인스펙터 값 → xlsx)", priority = 30)]
    private static void Export() => Export(askWhenExists: true);

    /// <summary>확인 창 없이 바로 병합 — 기존 값 보존·원본 백업이라 안전하다. 파일이 없으면 새로 만든다. 자동화(MCP)에서도 멈추지 않는다.</summary>
    [MenuItem("Tools/Data/Export Template — 병합 (확인 없이)", priority = 32)]
    private static void ExportMerge() => Export(askWhenExists: false);

    private static void Export(bool askWhenExists)
    {
        var warnings = new List<string>();
        List<(Type, IReadOnlyList<(string id, Object target)>)> tables = CollectTargets(warnings);
        if (tables.Count == 0)
        {
            EditorUtility.DisplayDialog("Export Template", "[DataTableSheet] 가 붙은 타입이 없다.", "확인");
            return;
        }

        Directory.CreateDirectory(DataTableSource.Folder);
        string path = Path.Combine(DataTableSource.Folder, DefaultFileName);
        List<XlsxWriteSheet> sheets = BuildSheets(tables, warnings);
        bool merged = false;

        if (File.Exists(path))
        {
            // 0 = 병합, 1 = 취소, 2 = 덮어쓰기. 병합이 기본 — 기획이 쓰던 파일이 있으면 값을 지키는 쪽이 안전하다.
            int choice = !askWhenExists ? 0 : EditorUtility.DisplayDialogComplex("Export Template",
                $"{path} 가 이미 있다.\n\n" +
                "병합: 기획이 고친 값은 그대로, 새 시트·필드·대상만 현재 인스펙터 값으로 추가. 코드에서 사라진 칸은 #(메모)로.\n" +
                "덮어쓰기: 전부 현재 인스펙터 값으로(기획 값 사라짐).\n\n" +
                "어느 쪽이든 서식·수식·열 너비는 사라진다. 원본은 " + ExportBackupFolder + " 에 보관.",
                "병합(기획 값 유지)", "취소", "덮어쓰기");
            if (choice == 1)
            {
                return;
            }

            IReadOnlyList<XlsxSheet> existing;
            try
            {
                existing = XlsxReader.Read(path);
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is System.Xml.XmlException)
            {
                EditorUtility.DisplayDialog("Export Template 실패", $"기존 xlsx 를 읽지 못했다:\n{e.Message}", "확인");
                return;
            }

            Directory.CreateDirectory(ExportBackupFolder);
            string backup = Path.Combine(ExportBackupFolder,
                $"{Path.GetFileNameWithoutExtension(path)}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            File.Copy(path, backup, overwrite: true);
            Debug.Log($"{LogPrefix}Export 전 원본 보관: {backup}");

            if (choice == 0)
            {
                var report = new List<string>();
                // 묶음 시트로 옮긴 타입: 옛 "타입 이름" 시트 → 새 묶음 시트(값을 옮긴다).
                Dictionary<string, string> moved = tables
                    .Select(t => t.Item1)
                    .Where(t => AssetDatabaseLookup.SheetNameOf(t) != t.Name)
                    .ToDictionary(t => t.Name, AssetDatabaseLookup.SheetNameOf, StringComparer.Ordinal);
                sheets = DataTableMerge.Merge(existing, sheets, report, moved);
                merged = true;
                foreach (string line in report)
                {
                    Debug.Log($"{LogPrefix}병합: {line}");
                }

                if (report.Count == 0)
                {
                    Debug.Log($"{LogPrefix}병합: 추가·변경 없음.");
                }
            }
        }

        try
        {
            XlsxWriter.Write(path, sheets);
        }
        catch (IOException e)
        {
            EditorUtility.DisplayDialog("Export Template 실패", $"쓰지 못했다(Excel 에서 열려 있나?):\n{e.Message}", "확인");
            return;
        }

        foreach (string warning in warnings)
        {
            Debug.LogWarning(LogPrefix + warning);
        }

        // 직후 Verify. 새로 쓴 템플릿이면 차이 0 이 기준선, 병합이면 차이 = 기획이 고친 값(정상).
        DataTableSource.Result result = DataTableSource.Load();
        foreach (DataTableIssue issue in result.Issues.Items)
        {
            Debug.LogWarning($"{LogPrefix}Export 직후 Verify: {issue}");
        }

        int differences = result.Issues.HasErrors ? -1 : DataTableApplier.Diff(result.Writes).Count;
        string summary = $"{(merged ? "병합" : "템플릿")} {path} — 시트 {sheets.Count}개, 필드 {result.Writes.Count}개, 직후 Verify 차이 " +
                         (differences < 0 ? "확인 불가(오류 — 위 경고)" : differences.ToString());
        if (differences < 0 || (!merged && differences != 0))
        {
            Debug.LogError(LogPrefix + summary + (merged ? string.Empty : " — 새 템플릿은 0 이어야 한다."));
        }
        else
        {
            Debug.Log(LogPrefix + summary + (merged ? " (차이 = 기획이 고친 값)" : " — 기준선 OK"));
        }

        EditorWindow.focusedWindow?.ShowNotification(new GUIContent(
            $"데이터 테이블 {(merged ? "병합" : "템플릿")}: 시트 {sheets.Count}개 · 차이 {(differences < 0 ? "오류" : differences.ToString())}"));
    }
}
