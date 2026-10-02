using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 현재 인스펙터 값(SO·프리팹) → xlsx 템플릿(PLAN-data-table.md D2·D3·D4·D7).
/// <para>
/// 내보내는 필드 = <b>정수·실수</b>만(구조체·배열 안쪽 포함, 배열은 원소마다 <c>phases[0]</c>).
/// 레이어 마스크·문자열·곡선·참조·bool·enum 은 넣지 않는다 — 가져오기는 bool·enum 도 되므로 필요하면 기획이 행을 직접 추가한다.
/// <see cref="DataTableIgnoreAttribute"/> 필드는 뺀다.
/// </para>
/// <para>
/// 배치 = <b>세로 표</b>(D7): 행 = 필드, 열 = <c>필드 | #설명 | 대상…</c>. 타입마다 <c>#■ 타입</c> 구역,
/// 코드의 <c>[Header]</c> 는 <c>#  ─ 제목</c>, 구조체 배열 원소는 <c>#  ▸ attacks[3] · 이름</c> 소구역. 가로 스크롤 없이 위아래로 읽는다.
/// </para>
/// </summary>
public static class DataTableTemplate
{
    public const string DefaultFileName = "GameData.xlsx";
    private const string LogPrefix = "[DataTable] ";

    /// <summary>시트 순서 — 이 목록 순서대로, 나머지는 이름 순.</summary>
    private static readonly string[] PreferredSheetOrder = { "Player", "Paladin", "Gunner", "Monster", "MidBoss", "Boss" };

    public readonly struct Field
    {
        public Field(string path, object value, string description, string section = null, string element = null, string subSection = null)
        {
            Path = path;
            Value = value;
            Description = description;
            Section = section;
            Element = element;
            SubSection = subSection;
        }

        /// <summary>테이블 표기 — "charge.speed", "phases[0]".</summary>
        public string Path { get; }

        public object Value { get; }
        public string Description { get; }

        /// <summary>바깥 필드의 <c>[Header]</c>(앞 필드에서 이어짐). 없으면 null.</summary>
        public string Section { get; }

        /// <summary>구조체 배열 원소면 "attacks[3] · JumpAttack"(원소 첫 enum/string 필드가 이름표). 아니면 null.</summary>
        public string Element { get; }

        /// <summary>원소 안쪽 필드의 <c>[Header]</c>. 없으면 null.</summary>
        public string SubSection { get; }
    }

    /// <summary>대상(SO·컴포넌트) 하나에서 템플릿에 들어갈 필드를 직렬화 순서대로. NetworkVariable 안쪽(네트워크 상태)은 뺀다.</summary>
    public static List<Field> CollectFields(Object asset)
    {
        var fields = new List<Field>();
        Type type = asset.GetType();
        var so = new SerializedObject(asset);
        SerializedProperty it = so.GetIterator();
        bool enterChildren = true;
        string section = null;
        string elementKey = null;
        string elementLabel = null;
        string subSection = null;
        while (it.Next(enterChildren))
        {
            enterChildren = false;
            string path = it.propertyPath;
            if (path == "m_Script" || path.EndsWith(".Array.size", StringComparison.Ordinal))
            {
                continue;
            }

            FieldInfo field = ResolveField(type, path);

            // [Header] 는 내보내지 않는 필드(레이어 마스크 등)에 붙어 있어도 구역을 연다.
            if (field != null && path.IndexOf('.') < 0)
            {
                string header = field.GetCustomAttribute<HeaderAttribute>()?.header;
                if (header != null)
                {
                    section = header;
                }
            }

            if (field == null || IsExcluded(field) || IsUnderIgnored(type, path))
            {
                continue;
            }

            if (it.propertyType == SerializedPropertyType.Generic)
            {
                enterChildren = true; // 구조체·클래스·배열 안으로
                continue;
            }

            if (it.propertyType != SerializedPropertyType.Integer && it.propertyType != SerializedPropertyType.Float)
            {
                continue;
            }

            // 구조체 배열 원소(attacks.Array.data[3].cooldown) — 원소가 바뀌면 이름표·안쪽 구역을 새로.
            string element = null;
            string sub = null;
            int arrayAt = path.IndexOf(".Array.data[", StringComparison.Ordinal);
            int close = arrayAt < 0 ? -1 : path.IndexOf(']', arrayAt);
            if (close > 0 && close + 1 < path.Length && path[close + 1] == '.')
            {
                string key = path.Substring(0, close + 1);
                if (key != elementKey)
                {
                    elementKey = key;
                    elementLabel = ElementLabel(so, key);
                    subSection = null;
                }

                string leafHeader = DataTableFields.Resolve(type, path)?.GetCustomAttribute<HeaderAttribute>()?.header;
                if (leafHeader != null)
                {
                    subSection = leafHeader;
                }

                element = elementLabel;
                sub = subSection;
            }

            object value = it.propertyType == SerializedPropertyType.Integer
                ? it.longValue
                : it.numericType == SerializedPropertyNumericType.Double ? (object)it.doubleValue : it.floatValue;
            fields.Add(new Field(ToTablePath(path), value, Describe(field, path), section, element, sub));
        }

        return fields;
    }

    /// <summary>"attacks[3]" + 원소의 첫 enum·string 필드 값(예: "attacks[3] · JumpAttack").</summary>
    private static string ElementLabel(SerializedObject so, string elementPropertyPath)
    {
        string label = ToTablePath(elementPropertyPath);
        SerializedProperty element = so.FindProperty(elementPropertyPath);
        if (element == null)
        {
            return label;
        }

        SerializedProperty child = element.Copy();
        SerializedProperty end = element.GetEndProperty();
        if (!child.NextVisible(true))
        {
            return label;
        }

        while (!SerializedProperty.EqualContents(child, end))
        {
            if (child.propertyType == SerializedPropertyType.Enum && child.enumValueIndex >= 0 && child.enumValueIndex < child.enumNames.Length)
            {
                return $"{label} · {child.enumNames[child.enumValueIndex]}";
            }

            if (child.propertyType == SerializedPropertyType.String && !string.IsNullOrEmpty(child.stringValue))
            {
                return $"{label} · {child.stringValue}";
            }

            if (!child.NextVisible(false))
            {
                break;
            }
        }

        return label;
    }

    /// <summary>
    /// 타입별 대상 → 시트. 시트 이름 = 타입 이름, 또는 <c>[DataTableSheet("묶음")]</c> 이름(여러 타입을 한 시트에, Order 순).
    /// 모든 시트가 세로 표. 문제(이름 길이 등)는 warnings 로.
    /// </summary>
    public static List<XlsxWriteSheet> BuildSheets(IEnumerable<(Type type, IReadOnlyList<(string id, Object target)> targets)> tables, List<string> warnings)
    {
        var sheets = new List<XlsxWriteSheet>();
        foreach (IGrouping<string, (Type type, IReadOnlyList<(string id, Object target)> targets)> group in tables
                     .Where(t => t.targets.Count > 0)
                     .GroupBy(t => AssetDatabaseLookup.SheetNameOf(t.type))
                     .OrderBy(g => SheetRank(g.Key))
                     .ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            string nameError = XlsxWriter.ValidateSheetName(group.Key);
            if (nameError != null)
            {
                warnings.Add($"{group.Key}: {nameError} — 건너뜀.");
                continue;
            }

            sheets.Add(BuildVerticalSheet(group.Key, group
                .OrderBy(t => t.type.GetCustomAttribute<DataTableSheetAttribute>(false)?.Order ?? 100)
                .ThenBy(t => t.type.Name, StringComparer.Ordinal)
                .ToList()));
        }

        return sheets;
    }

    private static int SheetRank(string sheet)
    {
        int index = Array.IndexOf(PreferredSheetOrder, sheet);
        return index < 0 ? PreferredSheetOrder.Length : index;
    }

    /// <summary>
    /// 세로 표 한 장. 타입마다:
    /// <code>
    /// #■ 타입
    /// 필드 | #설명 | 대상1 | 대상2 …      ← 머리글(굵게)
    /// #  ─ [Header] 제목
    /// #  ▸ attacks[3] · JumpAttack       ← 구조체 배열 원소
    /// 필드경로 | 설명 | 값 | 값 …          ← 그 대상에 없는 칸은 "-"
    /// (빈 행)
    /// </code>
    /// </summary>
    private static XlsxWriteSheet BuildVerticalSheet(string sheetName, List<(Type type, IReadOnlyList<(string id, Object target)> targets)> members)
    {
        var rows = new List<object[]>();
        var bold = new HashSet<int>();

        foreach ((Type type, IReadOnlyList<(string id, Object target)> targets) in members)
        {
            List<(string id, Object target)> ordered = targets.OrderBy(t => t.id, StringComparer.Ordinal).ToList();
            List<List<Field>> perTarget = ordered.Select(t => CollectFields(t.target)).ToList();
            List<Field> columns = UnionInOrder(perTarget);
            if (columns.Count == 0)
            {
                continue;
            }

            bold.Add(rows.Count);
            rows.Add(new object[] { $"{DataTableSchema.CommentPrefix}■ {type.Name}" });
            bold.Add(rows.Count);
            rows.Add(new object[] { DataTableSchema.VerticalFieldHeader, DataTableSchema.DescriptionHeader }
                .Concat(ordered.Select(t => (object)t.id)).ToArray());

            List<Dictionary<string, object>> values = perTarget.Select(fs => fs.ToDictionary(f => f.Path, f => f.Value)).ToList();
            string section = null;
            string element = null;
            string sub = null;
            foreach (Field field in columns)
            {
                if (field.Section != null && field.Section != section)
                {
                    section = field.Section;
                    element = null;
                    rows.Add(new object[] { $"{DataTableSchema.CommentPrefix}  ─ {section}" });
                }

                if (field.Element != element)
                {
                    element = field.Element;
                    sub = null;
                    if (element != null)
                    {
                        rows.Add(new object[] { $"{DataTableSchema.CommentPrefix}  ▸ {element}" });
                    }
                }

                if (field.SubSection != null && field.SubSection != sub)
                {
                    sub = field.SubSection;
                    rows.Add(new object[] { $"{DataTableSchema.CommentPrefix}     · {sub}" });
                }

                rows.Add(new object[] { field.Path, field.Description }
                    .Concat(values.Select(v => v.TryGetValue(field.Path, out object value) ? value : DataTableSchema.NotApplicable))
                    .ToArray());
            }

            rows.Add(Array.Empty<object>());
        }

        return new XlsxWriteSheet(sheetName, rows, frozenRows: 0, boldRows: 0, frozenColumns: 1, boldRowIndices: bold);
    }

    /// <summary>
    /// 대상들의 필드 합집합. 순서는 첫 대상을 따르고, 다른 대상에만 있는 필드는 그 대상에서 바로 앞 필드 뒤에 끼운다
    /// (attacks[6].* 가 attacks[5].* 뒤에 오게). 구역 정보는 처음 본 대상의 것을 쓴다.
    /// </summary>
    private static List<Field> UnionInOrder(List<List<Field>> perTarget)
    {
        var columns = new List<Field>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (List<Field> fields in perTarget)
        {
            int insertAt = 0;
            foreach (Field field in fields)
            {
                if (index.TryGetValue(field.Path, out int existing))
                {
                    insertAt = existing + 1;
                    continue;
                }

                columns.Insert(insertAt, field);
                for (int c = insertAt; c < columns.Count; c++)
                {
                    index[columns[c].Path] = c;
                }

                insertAt++;
            }
        }

        return columns;
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
        path.StartsWith("Assets/50.Art/", StringComparison.OrdinalIgnoreCase) ||
        ArchivedPrefabs.Contains(Path.GetFileNameWithoutExtension(path));

    /// <summary>
    /// 보관만 하고 스폰하지 않는 프리팹(AGENTS.md — 2026-09-29 base+Variant 전환 뒤 참고용). 테이블 열로 나오면 헷갈린다(D7, 은희).
    /// </summary>
    private static readonly HashSet<string> ArchivedPrefabs = new HashSet<string>(StringComparer.Ordinal) { "Paladin_VFX" };

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
                // 코드가 아는 시트 이름(타입 이름·묶음 이름) — 옛 배치의 이 시트들은 새 배치로 대체된다(값은 (대상, 필드) 로 따라간다).
                var known = new HashSet<string>(tables.SelectMany(t => new[] { t.Item1.Name, AssetDatabaseLookup.SheetNameOf(t.Item1) }), StringComparer.Ordinal);
                sheets = DataTableMerge.Merge(existing, sheets, report, known);
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
