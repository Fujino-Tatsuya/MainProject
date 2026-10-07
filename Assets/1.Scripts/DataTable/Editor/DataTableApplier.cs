using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 시트 이름과 Id 를 실제 대상으로 푼다. 대상 = SO 에셋 또는 <b>프리팹 안의 컴포넌트</b>.
/// 시트 하나 = 타입 하나(시트 이름 = 타입 이름) 또는 <b>묶음 시트</b>(<c>[DataTableSheet("Paladin")]</c> 를 준 타입 여럿).
/// 테스트는 메모리 구현으로 바꿔 끼운다.
/// </summary>
public interface IDataTableAssetLookup
{
    /// <summary>시트 이름에 해당하는 타입들(ScriptableObject 또는 Component). 없으면 null + 이유.</summary>
    IReadOnlyList<Type> FindTypes(string sheetName, out string error);

    /// <summary>
    /// 그 타입(정확히 같은 타입)의 대상 하나. SO = 에셋 파일 이름이 Id, 컴포넌트 = 그 컴포넌트를 가진 프리팹 파일 이름이 Id.
    /// 없거나 여럿이면 null + 이유.
    /// </summary>
    Object FindTarget(Type type, string id, out string error);

    /// <summary>그 타입 대상 전부의 Id — "테이블에 행이 없다" 경고용. 세기 비싸면 null(경고 생략).</summary>
    IReadOnlyCollection<string> AllIds(Type type);
}

/// <summary>대상 필드 하나에 쓸 값. 변환·검증을 마친 상태.</summary>
public sealed class DataTableWrite
{
    internal DataTableWrite(Object target, string propertyPath, SerializedPropertyType kind, object value, DataTableEntry source)
    {
        Target = target;
        PropertyPath = propertyPath;
        Kind = kind;
        Value = value;
        Source = source;
    }

    /// <summary>SO 에셋 또는 프리팹 에셋 안의 컴포넌트.</summary>
    public Object Target { get; }

    public string PropertyPath { get; }
    public SerializedPropertyType Kind { get; }
    public object Value { get; }
    public DataTableEntry Source { get; }
}

/// <summary>Verify 결과 한 줄 — 인스펙터 값(개발자)과 테이블 값(기획)이 다른 필드.</summary>
public readonly struct DataTableDifference
{
    public DataTableDifference(DataTableWrite write, string currentValue)
    {
        Write = write;
        CurrentValue = currentValue;
    }

    public DataTableWrite Write { get; }
    public string CurrentValue { get; }

    public override string ToString() =>
        $"{Write.Source.Location}: {Write.Source.AssetId}.{Write.Source.Field} — 인스펙터 {CurrentValue} / 테이블 {Write.Source.Value}";
}

/// <summary>메모리 적용 전 상태. <see cref="DataTableApplier.Restore"/> 로 되돌린다. JsonUtility 로 SessionState 에 넣을 수 있다.</summary>
[Serializable]
public sealed class DataTableSnapshot
{
    [Serializable]
    public struct Item
    {
        public int instanceId;          // 같은 에디터 세션 안에서는 이걸로 찾는다(에셋이 아닌 메모리 객체도 됨).
        public string globalObjectId;   // 세션이 바뀌었으면(크래시 후 등) 이걸로.
        public string json;
    }

    public List<Item> items = new List<Item>();
}

/// <summary>디스크 적용 전 파일 백업 — <see cref="DataTableApplier.RestoreFromBackup"/> 가 파일째 되돌린다(바이트 동일).</summary>
[Serializable]
public sealed class DataTableDiskBackup
{
    [Serializable]
    public struct Entry
    {
        public string assetPath;
        public string backupPath;
    }

    public List<Entry> files = new List<Entry>();
}

/// <summary>
/// <see cref="DataTableEntry"/> → 대상 필드 쓰기. 대상 = SO 또는 프리팹 컴포넌트(PLAN-data-table.md D3·D4 — 프리팹 값을 SO 로 옮기지 않는다).
/// <list type="number">
/// <item><see cref="Bind"/> — 타입·대상·필드를 찾고 값을 변환한다. <b>쓰지 않는다.</b> 문제는 전부 issues 로.</item>
/// <item><see cref="ApplyInMemory"/>/<see cref="ApplyToDisk"/> — Bind 에 오류가 없을 때만 호출한다(전부 또는 0).</item>
/// <item><see cref="Diff"/> — 쓰지 않고 현재 값과 비교(Verify).</item>
/// </list>
/// 다루는 타입: 정수·실수·불리언·문자열·열거형(이름). 참조(프리팹·VFX 등)는 테이블 대상이 아니다 — 인스펙터에서 연결.
/// 배열 원소는 기존 크기 안에서만 쓴다(테이블이 배열 크기를 바꾸지 않는다).
/// </summary>
public static class DataTableApplier
{
    private static readonly Regex ArrayIndex = new Regex(@"\[(\d+)\]", RegexOptions.Compiled);

    /// <summary>빌드 중 크래시 대비 백업 위치 — Temp 는 에디터 종료 때 지워지므로 Library 에 둔다.</summary>
    public const string BackupFolder = "Library/DataTableBuildBackup";

    public static List<DataTableWrite> Bind(IEnumerable<DataTableEntry> entries, IDataTableAssetLookup lookup, DataTableIssues issues)
    {
        var writes = new List<DataTableWrite>();
        var serialized = new Dictionary<Object, SerializedObject>();
        var written = new Dictionary<(Object, string), DataTableEntry>();

        foreach (IGrouping<string, DataTableEntry> sheet in entries.GroupBy(e => e.Sheet, StringComparer.Ordinal))
        {
            DataTableEntry firstEntry = sheet.First();
            IReadOnlyList<Type> types = lookup.FindTypes(sheet.Key, out string typeError);
            if (types == null || types.Count == 0)
            {
                issues.Error(firstEntry.Location, typeError);
                continue;
            }

            string typeNames = string.Join("/", types.Select(t => t.Name));
            var candidatesById = new Dictionary<string, List<Object>>(StringComparer.Ordinal);
            var failed = new HashSet<string>(StringComparer.Ordinal);
            foreach (DataTableEntry entry in sheet)
            {
                if (failed.Contains(entry.AssetId))
                {
                    continue; // 대상 못 찾음은 Id 당 한 번만 보고
                }

                if (!candidatesById.TryGetValue(entry.AssetId, out List<Object> candidates))
                {
                    candidates = new List<Object>();
                    var errors = new List<string>();
                    foreach (Type type in types)
                    {
                        Object found = lookup.FindTarget(type, entry.AssetId, out string targetError);
                        if (found != null)
                        {
                            candidates.Add(found);
                        }
                        else
                        {
                            errors.Add(targetError);
                        }
                    }

                    if (candidates.Count == 0)
                    {
                        issues.Error(entry.Location, types.Count == 1 ? errors[0] : $"'{entry.AssetId}' 는 {typeNames} 어디에도 없다.");
                        failed.Add(entry.AssetId);
                        continue;
                    }

                    candidatesById[entry.AssetId] = candidates;
                }

                // 묶음 시트에서 같은 Id(예: 프리팹 Player_Paladin)를 여러 타입이 가지면 그 필드를 실제로 가진 대상으로 좁힌다.
                Object target = candidates[0];
                if (candidates.Count > 1)
                {
                    string candidatePath = ToPropertyPath(entry.Field);
                    List<Object> withField = candidates.Where(c => SerializedFor(serialized, c).FindProperty(candidatePath) != null).ToList();
                    if (withField.Count != 1)
                    {
                        issues.Error(entry.Location, withField.Count == 0
                            ? $"'{entry.AssetId}' 의 {typeNames} 어디에도 필드 '{entry.Field}' 가 없다."
                            : $"'{entry.AssetId}.{entry.Field}' 가 {string.Join("/", withField.Select(c => c.GetType().Name))} 에 다 있어 어느 쪽인지 모른다.");
                        continue;
                    }

                    target = withField[0];
                }

                Type targetType = target.GetType();
                SerializedObject so = SerializedFor(serialized, target);

                string path = ToPropertyPath(entry.Field);
                SerializedProperty property = so.FindProperty(path);
                if (property == null)
                {
                    issues.Error(entry.Location, path.Contains(".Array.data[")
                        ? $"{targetType.Name}.{entry.Field} — 필드가 없거나 배열 범위를 벗어났다(테이블은 배열 크기를 바꾸지 않는다)."
                        : $"{targetType.Name} 에 필드 '{entry.Field}' 가 없다(직렬화 필드 이름과 같아야 한다).");
                    continue;
                }

                if (written.TryGetValue((target, path), out DataTableEntry previous))
                {
                    issues.Error(entry.Location, $"'{entry.AssetId}.{entry.Field}' 를 {previous.Location} 에서도 쓴다.");
                    continue;
                }

                FieldInfo resolvedField = DataTableFields.Resolve(targetType, path);
                if (property.propertyType == SerializedPropertyType.String && !DataTableFields.IsTableText(resolvedField))
                {
                    issues.Error(entry.Location,
                        $"{targetType.Name}.{entry.Field} — 문자열은 [DataTableText]가 붙은 필드만 테이블에서 다룬다.");
                    continue;
                }

                if (entry.Value.Length == 0 && property.propertyType != SerializedPropertyType.String)
                {
                    issues.Error(entry.Location, $"'{entry.AssetId}.{entry.Field}' 값이 비어 있다(이 대상에 없는 칸이면 '{DataTableSchema.NotApplicable}').");
                    continue;
                }

                if (!TryConvert(property, entry.Value, out object value, out string convertError))
                {
                    issues.Error(entry.Location, $"'{entry.AssetId}.{entry.Field}' = '{entry.Value}' — {convertError}");
                    continue;
                }

                if (property.propertyType == SerializedPropertyType.String &&
                    !SkillTooltipFormatter.TryValidate((string)value, target, out string tooltipError))
                {
                    issues.Error(entry.Location, $"'{entry.AssetId}.{entry.Field}' — {tooltipError}");
                    continue;
                }

                if (value is long || value is double)
                {
                    string rangeError = DataTableFields.CheckRange(
                        resolvedField, Convert.ToDouble(value, CultureInfo.InvariantCulture));
                    if (rangeError != null)
                    {
                        issues.Error(entry.Location, $"'{entry.AssetId}.{entry.Field}' = '{entry.Value}' — {rangeError}");
                        continue;
                    }
                }

                written[(target, path)] = entry;
                writes.Add(new DataTableWrite(target, path, property.propertyType, value, entry));
            }

            foreach (Type type in types)
            {
                IReadOnlyCollection<string> allIds = lookup.AllIds(type);
                if (allIds == null)
                {
                    continue;
                }

                foreach (string id in allIds.Where(id => !candidatesById.ContainsKey(id) && !failed.Contains(id)))
                {
                    issues.Warning(firstEntry.Location.Split('!')[0], $"{type.Name} '{id}' 의 행이 테이블에 없다.");
                }
            }
        }

        return writes;
    }

    private static SerializedObject SerializedFor(Dictionary<Object, SerializedObject> cache, Object target)
    {
        if (!cache.TryGetValue(target, out SerializedObject so))
        {
            so = new SerializedObject(target);
            cache[target] = so;
        }

        return so;
    }

    /// <summary>
    /// 테이블 Play 용. 디스크에는 쓰지 않는다 — 적용 후 dirty 를 지워 저장 대상에서 뺀다.
    /// 프리팹 컴포넌트도 메모리의 프리팹 에셋을 바꾸므로, 런타임에 Instantiate·네트워크 스폰되는 사본이 테이블 값을 받는다.
    /// 반환한 스냅샷으로 <see cref="Restore"/> 해야 원래 값으로 돌아온다.
    /// </summary>
    public static DataTableSnapshot ApplyInMemory(IReadOnlyList<DataTableWrite> writes)
    {
        var snapshot = new DataTableSnapshot();
        foreach (Object target in writes.Select(w => w.Target).Distinct())
        {
            snapshot.items.Add(new DataTableSnapshot.Item
            {
                instanceId = target.GetInstanceID(),
                globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(target).ToString(),
                json = EditorJsonUtility.ToJson(target),
            });
        }

        foreach (IGrouping<Object, DataTableWrite> group in writes.GroupBy(w => w.Target))
        {
            bool wasDirty = EditorUtility.IsDirty(group.Key);
            var so = new SerializedObject(group.Key);
            foreach (DataTableWrite write in group)
            {
                Assign(so.FindProperty(write.PropertyPath), write);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            if (!wasDirty)
            {
                EditorUtility.ClearDirty(group.Key);
            }
        }

        return snapshot;
    }

    /// <summary>메모리 적용을 되돌린다. 찾지 못한 대상 수를 반환한다(0 이 정상).</summary>
    public static int Restore(DataTableSnapshot snapshot)
    {
        int missing = 0;
        foreach (DataTableSnapshot.Item item in snapshot.items)
        {
            Object target = FindSnapshotTarget(item);
            if (target == null)
            {
                missing++;
                continue;
            }

            bool wasDirty = EditorUtility.IsDirty(target);
            EditorJsonUtility.FromJsonOverwrite(item.json, target);
            if (!wasDirty)
            {
                EditorUtility.ClearDirty(target);
            }
        }

        return missing;
    }

    private static Object FindSnapshotTarget(DataTableSnapshot.Item item)
    {
        Object byInstance = EditorUtility.EntityIdToObject(item.instanceId);
        if (byInstance != null)
        {
            return byInstance;
        }

        return GlobalObjectId.TryParse(item.globalObjectId, out GlobalObjectId id)
            ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id)
            : null;
    }

    /// <summary>
    /// 디스크의 대상을 테이블 값으로 덮어쓰고 저장한다(빌드용). 쓰기 전에 대상 파일(.asset·.prefab)을 통째로 백업한다 —
    /// 프리팹 Variant 의 오버라이드 목록까지 원래대로 돌리려면 값 되쓰기가 아니라 파일 복원이어야 바이트가 같다.
    /// 반환한 백업을 <see cref="RestoreFromBackup"/> 에 넘겨 되돌린다. 백업 목록은 디스크에도 남겨 빌드 중 크래시 후 복구에 쓴다.
    /// <paramref name="keepBackup"/> = false 는 되돌릴 생각이 없는 수동 덮어쓰기용(백업이 남으면 다음 에디터 시작 때 크래시 복구가 되돌려 버린다).
    /// </summary>
    public static DataTableDiskBackup ApplyToDisk(IReadOnlyList<DataTableWrite> writes, bool keepBackup = true)
    {
        var backup = new DataTableDiskBackup();
        string[] assetPaths = writes.Select(w => AssetDatabase.GetAssetPath(w.Target)).Distinct().ToArray();
        if (keepBackup)
        {
            Directory.CreateDirectory(BackupFolder);
            foreach (string assetPath in assetPaths)
            {
                string backupPath = Path.Combine(BackupFolder, AssetDatabase.AssetPathToGUID(assetPath) + Path.GetExtension(assetPath));
                File.Copy(assetPath, backupPath, overwrite: true);
                backup.files.Add(new DataTableDiskBackup.Entry { assetPath = assetPath, backupPath = backupPath });
            }

            File.WriteAllText(ManifestPath, JsonUtility.ToJson(backup, prettyPrint: true));
        }

        // 🔴 프리팹은 SaveAssetIfDirty 로 저장되지 않는다(10-07 실측 — Player_Gunner 에 maxHp 오버라이드가 안 써져 빌드가 base 50 으로 나갔다).
        //    SavePrefabAsset 이 파일 쓰기 + 재임포트를 한다. base 를 먼저 저장해야 한다 — base 재임포트가 Variant 를
        //    다시 읽으므로, Variant 에 먼저 쓴 값은 저장 전에 날아간다. 그래서 Variant 깊이 순으로 적용·저장한다.
        foreach (IGrouping<int, IGrouping<string, DataTableWrite>> depth in writes
                     .GroupBy(w => AssetDatabase.GetAssetPath(w.Target))
                     .GroupBy(file => PrefabVariantDepth(file.Key))
                     .OrderBy(d => d.Key))
        {
            foreach (IGrouping<string, DataTableWrite> file in depth)
            {
                foreach (IGrouping<Object, DataTableWrite> group in file.GroupBy(w => w.Target))
                {
                    var so = new SerializedObject(group.Key);
                    foreach (DataTableWrite write in group)
                    {
                        Assign(so.FindProperty(write.PropertyPath), write);
                    }

                    if (so.ApplyModifiedProperties())
                    {
                        EditorUtility.SetDirty(group.Key);
                    }
                }

                if (file.Key.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    PrefabUtility.SavePrefabAsset(AssetDatabase.LoadAssetAtPath<GameObject>(file.Key));
                }
                else
                {
                    AssetDatabase.SaveAssetIfDirty(AssetDatabase.GUIDFromAssetPath(file.Key));
                }
            }
        }

        return backup;
    }

    // 0 = 프리팹이 아니거나 일반 프리팹, n = base 까지 Variant 단계 수.
    private static int PrefabVariantDepth(string assetPath)
    {
        int depth = 0;
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        while (prefab != null && PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Variant)
        {
            depth++;
            prefab = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
        }

        return depth;
    }

    /// <summary>백업 파일을 제자리로 복사하고 다시 임포트한다. 실패한 파일 수를 반환한다(0 이 정상). 다 되돌리면 백업을 지운다.</summary>
    public static int RestoreFromBackup(DataTableDiskBackup backup)
    {
        // 에디터가 에셋 파일을 메모리 매핑으로 쥐고 있으면 덮어쓰기가 IOException 으로 실패한다(10-07 빌드마다 2~12개).
        AssetDatabase.ReleaseCachedFileHandles();

        var remaining = new DataTableDiskBackup();
        foreach (DataTableDiskBackup.Entry file in backup.files)
        {
            try
            {
                File.Copy(file.backupPath, file.assetPath, overwrite: true);
                AssetDatabase.ImportAsset(file.assetPath, ImportAssetOptions.ForceUpdate);
                File.Delete(file.backupPath);
            }
            catch (IOException e)
            {
                remaining.files.Add(file);
                Debug.LogError($"[DataTable] 원복 실패 {file.assetPath} — 백업 {file.backupPath} 을 직접 복사할 것. {e.Message}");
            }
        }

        // 남은 것만 기록한다 — 통째로 남기면 다음 크래시 복구가 이미 지운 백업까지 찾다가 실패를 쏟아낸다.
        if (remaining.files.Count > 0)
        {
            File.WriteAllText(ManifestPath, JsonUtility.ToJson(remaining, prettyPrint: true));
        }
        else if (File.Exists(ManifestPath))
        {
            File.Delete(ManifestPath);
        }

        return remaining.files.Count;
    }

    /// <summary>지난 빌드가 원복 전에 멈췄다면 남은 백업. 없으면 null.</summary>
    public static DataTableDiskBackup PendingBackup() =>
        File.Exists(ManifestPath) ? JsonUtility.FromJson<DataTableDiskBackup>(File.ReadAllText(ManifestPath)) : null;

    private static string ManifestPath => Path.Combine(BackupFolder, "manifest.json");

    /// <summary>대상(SO·프리팹 컴포넌트) 현재 값과 테이블 값이 다른 필드. 쓰지 않는다.</summary>
    /// <summary>
    /// <paramref name="current"/>(대상 그 자체 또는 그 대상의 씬 인스턴스)의 값이 테이블 값과 같은가. 인스펙터 표시(D6-5)용.
    /// 필드를 못 찾으면 true(표시할 차이 없음).
    /// </summary>
    public static bool MatchesTable(SerializedObject current, DataTableWrite write, out string currentValue)
    {
        SerializedProperty property = current.FindProperty(write.PropertyPath);
        if (property == null)
        {
            currentValue = null;
            return true;
        }

        currentValue = Describe(property);
        return Matches(property, write);
    }

    public static List<DataTableDifference> Diff(IReadOnlyList<DataTableWrite> writes)
    {
        var differences = new List<DataTableDifference>();
        foreach (IGrouping<Object, DataTableWrite> group in writes.GroupBy(w => w.Target))
        {
            var so = new SerializedObject(group.Key);
            foreach (DataTableWrite write in group)
            {
                SerializedProperty property = so.FindProperty(write.PropertyPath);
                if (!Matches(property, write))
                {
                    differences.Add(new DataTableDifference(write, Describe(property)));
                }
            }
        }

        return differences;
    }

    /// <summary>"phases[0].hp" → "phases.Array.data[0].hp" (SerializedProperty 경로).</summary>
    public static string ToPropertyPath(string field) => ArrayIndex.Replace(field.Trim(), ".Array.data[$1]");

    // ── 값 변환 ─────────────────────────────────────────────

    private static bool TryConvert(SerializedProperty property, string text, out object value, out string error)
    {
        value = null;
        error = null;
        switch (property.propertyType)
        {
            case SerializedPropertyType.Integer:
                if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number) ||
                    number != decimal.Truncate(number))
                {
                    error = "정수가 아니다.";
                    return false;
                }

                (long min, long max) = IntegerRange(property.numericType);
                if (number < min || number > max)
                {
                    error = $"{property.numericType} 범위({min} ~ {max})를 벗어났다.";
                    return false;
                }

                value = (long)number;
                return true;

            case SerializedPropertyType.Float:
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double real) ||
                    double.IsNaN(real) || double.IsInfinity(real))
                {
                    error = "숫자가 아니다.";
                    return false;
                }

                value = real;
                return true;

            case SerializedPropertyType.Boolean:
                switch (text.ToUpperInvariant())
                {
                    case "TRUE":
                    case "1":
                        value = true;
                        return true;
                    case "FALSE":
                    case "0":
                        value = false;
                        return true;
                    default:
                        error = "TRUE/FALSE 가 아니다.";
                        return false;
                }

            case SerializedPropertyType.String:
                value = text;
                return true;

            case SerializedPropertyType.Enum:
                string[] names = property.enumNames;
                int index = Array.FindIndex(names, n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    error = $"허용 값: {string.Join(", ", names)}";
                    return false;
                }

                value = index;
                return true;

            default:
                error = $"{property.propertyType} 타입은 테이블로 다루지 않는다(참조·구조체는 인스펙터에서 연결, 구조체는 하위 필드를 쓸 것).";
                return false;
        }
    }

    // ulong 은 long 범위까지만 받는다 — 수치 테이블에 그보다 큰 값은 없다.
    private static (long min, long max) IntegerRange(SerializedPropertyNumericType type)
    {
        switch (type)
        {
            case SerializedPropertyNumericType.Int64: return (long.MinValue, long.MaxValue);
            case SerializedPropertyNumericType.UInt64: return (0, long.MaxValue);
            case SerializedPropertyNumericType.UInt32: return (0, uint.MaxValue);
            case SerializedPropertyNumericType.Int16: return (short.MinValue, short.MaxValue);
            case SerializedPropertyNumericType.UInt16: return (0, ushort.MaxValue);
            case SerializedPropertyNumericType.UInt8: return (0, byte.MaxValue);
            case SerializedPropertyNumericType.Int8: return (sbyte.MinValue, sbyte.MaxValue);
            default: return (int.MinValue, int.MaxValue);
        }
    }

    private static void Assign(SerializedProperty property, DataTableWrite write)
    {
        switch (write.Kind)
        {
            case SerializedPropertyType.Integer:
                property.longValue = (long)write.Value;
                break;
            case SerializedPropertyType.Float:
                if (property.numericType == SerializedPropertyNumericType.Double)
                {
                    property.doubleValue = (double)write.Value;
                }
                else
                {
                    property.floatValue = (float)(double)write.Value;
                }

                break;
            case SerializedPropertyType.Boolean:
                property.boolValue = (bool)write.Value;
                break;
            case SerializedPropertyType.String:
                property.stringValue = (string)write.Value;
                break;
            case SerializedPropertyType.Enum:
                property.enumValueIndex = (int)write.Value;
                break;
        }
    }

    private static bool Matches(SerializedProperty property, DataTableWrite write)
    {
        switch (write.Kind)
        {
            case SerializedPropertyType.Integer:
                return property.longValue == (long)write.Value;
            case SerializedPropertyType.Float:
                return property.numericType == SerializedPropertyNumericType.Double
                    ? property.doubleValue == (double)write.Value
                    : property.floatValue == (float)(double)write.Value;
            case SerializedPropertyType.Boolean:
                return property.boolValue == (bool)write.Value;
            case SerializedPropertyType.String:
                return string.Equals(property.stringValue, (string)write.Value, StringComparison.Ordinal);
            case SerializedPropertyType.Enum:
                return property.enumValueIndex == (int)write.Value;
            default:
                return true;
        }
    }

    private static string Describe(SerializedProperty property)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.Integer: return property.longValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Float:
                return property.numericType == SerializedPropertyNumericType.Double
                    ? property.doubleValue.ToString("R", CultureInfo.InvariantCulture)
                    : property.floatValue.ToString("R", CultureInfo.InvariantCulture);
            case SerializedPropertyType.Boolean: return property.boolValue ? "TRUE" : "FALSE";
            case SerializedPropertyType.String: return property.stringValue;
            case SerializedPropertyType.Enum:
                return property.enumValueIndex >= 0 && property.enumValueIndex < property.enumNames.Length
                    ? property.enumNames[property.enumValueIndex]
                    : property.enumValueIndex.ToString(CultureInfo.InvariantCulture);
            default: return property.propertyType.ToString();
        }
    }
}
