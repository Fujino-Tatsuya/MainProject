using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using Unity.Netcode;
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

    /// <summary>타입별 에셋 → 시트. 시트 이름 = 타입 이름. 문제(이름 길이, 행 테이블에서 일부 에셋만 가진 배열 원소 등)는 warnings 로.</summary>
    public static List<XlsxWriteSheet> BuildSheets(IEnumerable<(Type type, IReadOnlyList<(string id, Object target)> targets)> tables, List<string> warnings)
    {
        var sheets = new List<XlsxWriteSheet>();
        foreach ((Type type, IReadOnlyList<(string id, Object target)> targets) in tables.OrderBy(t => t.type.Name, StringComparer.Ordinal))
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

        // 모든 에셋에 있는 필드만 열로 — 빈 칸은 가져오기 오류라서. 배열 길이가 에셋마다 다르면 여기서 빠진다.
        List<Field> columns = perAsset[0]
            .Where(f => perAsset.All(fs => fs.Any(x => x.Path == f.Path)))
            .ToList();
        IEnumerable<string> dropped = perAsset.SelectMany(fs => fs.Select(f => f.Path)).Distinct()
            .Where(p => columns.All(c => c.Path != p));
        foreach (string path in dropped)
        {
            warnings.Add($"{type.Name}.{path}: 일부 에셋에만 있다(배열 길이가 다름) — 템플릿에서 뺐다.");
        }

        var rows = new List<object[]>
        {
            new object[] { DataTableSchema.IdHeader }.Concat(columns.Select(c => (object)c.Path)).ToArray(),
            new object[] { "설명 →" }.Concat(columns.Select(c => (object)c.Description)).ToArray(),
        };
        for (int i = 0; i < assets.Count; i++)
        {
            Dictionary<string, object> values = perAsset[i].ToDictionary(f => f.Path, f => f.Value);
            rows.Add(new object[] { assets[i].id }.Concat(columns.Select(c => values[c.Path])).ToArray());
        }

        return new XlsxWriteSheet(type.Name, rows, frozenRows: 2, boldRows: 1);
    }

    /// <summary>"phases.Array.data[0].hp" → "phases[0].hp" (<see cref="DataTableApplier.ToPropertyPath"/> 의 반대).</summary>
    public static string ToTablePath(string propertyPath) => propertyPath.Replace(".Array.data[", "[");

    // ── 필드 메타(무시 표시·툴팁) ───────────────────────────────

    /// <summary>propertyPath 를 따라가 마지막 필드의 FieldInfo. 배열 원소면 배열 필드를 돌려준다.</summary>
    private static FieldInfo ResolveField(Type root, string propertyPath)
    {
        FieldInfo last = null;
        Type current = root;
        string[] parts = propertyPath.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "Array")
            {
                i++; // data[n]
                current = ElementType(current);
                continue;
            }

            last = FindField(current, parts[i]);
            if (last == null)
            {
                return null;
            }

            current = last.FieldType;
        }

        return last;
    }

    /// <summary>기술 값 표시 또는 네트워크 상태(NetworkVariable·NetworkList — 인스펙터 초기값이 아니라 런타임 동기화 값).</summary>
    private static bool IsExcluded(FieldInfo field) =>
        field.IsDefined(typeof(DataTableIgnoreAttribute), false) || typeof(NetworkVariableBase).IsAssignableFrom(field.FieldType);

    private static bool IsUnderIgnored(Type root, string propertyPath)
    {
        Type current = root;
        string[] parts = propertyPath.Split('.');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i] == "Array")
            {
                i++;
                current = ElementType(current);
                continue;
            }

            FieldInfo field = FindField(current, parts[i]);
            if (field == null)
            {
                return false;
            }

            if (IsExcluded(field))
            {
                return true;
            }

            current = field.FieldType;
        }

        return false;
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
        {
            FieldInfo field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
            {
                return field;
            }
        }

        return null;
    }

    private static Type ElementType(Type collection)
    {
        if (collection == null)
        {
            return null;
        }

        if (collection.IsArray)
        {
            return collection.GetElementType();
        }

        return collection.IsGenericType ? collection.GetGenericArguments()[0] : null;
    }

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
    private static bool IsExcludedPath(string path) =>
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

    [MenuItem("Tools/Data/Export Template (현재 인스펙터 값 → xlsx)", priority = 30)]
    private static void Export()
    {
        var warnings = new List<string>();
        List<(Type, IReadOnlyList<(string id, Object target)>)> tables = CollectTargets(warnings);
        if (tables.Count == 0)
        {
            EditorUtility.DisplayDialog("Export Template", "[DataTableSheet] 가 붙은 타입이 없다.", "확인");
            return;
        }

        // 기본 경로에 바로 쓴다. 이미 있으면 기획이 쓰던 파일일 수 있으니 그때만 확인 창.
        Directory.CreateDirectory(DataTableSource.Folder);
        string path = Path.Combine(DataTableSource.Folder, DefaultFileName);
        if (File.Exists(path) && !EditorUtility.DisplayDialog("Export Template — 덮어쓰기",
                $"{path} 가 이미 있다. 덮어쓰면 기획이 고친 값은 사라진다(SVN 이력에서만 되찾을 수 있다).",
                "덮어쓰기", "취소"))
        {
            return;
        }

        List<XlsxWriteSheet> sheets = BuildSheets(tables, warnings);
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

        // 직후 Verify — 기준선(차이 0) 확인. 결과는 대화상자 대신 Console·알림(메뉴를 자동화로 눌러도 멈추지 않게).
        DataTableSource.Result result = DataTableSource.Load();
        foreach (DataTableIssue issue in result.Issues.Items)
        {
            Debug.LogWarning($"{LogPrefix}Export 직후 Verify: {issue}");
        }

        int differences = result.Issues.HasErrors ? -1 : DataTableApplier.Diff(result.Writes).Count;
        string summary = $"템플릿 {path} — 시트 {sheets.Count}개, 필드 {result.Writes.Count}개, 직후 Verify 차이 " +
                         (differences < 0 ? "확인 불가(오류)" : differences.ToString()) +
                         (differences == 0 ? " — 기준선 OK" : string.Empty);
        if (differences == 0)
        {
            Debug.Log(LogPrefix + summary);
        }
        else
        {
            Debug.LogError(LogPrefix + summary + " — 0 이어야 한다.");
        }

        EditorWindow.focusedWindow?.ShowNotification(new GUIContent($"데이터 테이블 템플릿: 시트 {sheets.Count}개 · 차이 {(differences < 0 ? "오류" : differences.ToString())}"));
    }
}
