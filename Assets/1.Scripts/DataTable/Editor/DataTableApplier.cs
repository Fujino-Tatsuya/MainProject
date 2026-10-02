using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>시트 이름(= SO 타입 이름)과 Id(= 에셋 파일 이름)를 실제 SO 로 푼다. 테스트는 메모리 구현으로 바꿔 끼운다.</summary>
public interface IDataTableAssetLookup
{
    /// <summary>시트 이름에 해당하는 SO 타입. 없거나 여럿이면 null + 이유.</summary>
    Type FindType(string sheetName, out string error);

    /// <summary>그 타입(정확히 같은 타입)의 에셋 전부, 파일 이름 → 에셋. 같은 이름이 여럿이면 그 이름은 오류 목록으로.</summary>
    IReadOnlyDictionary<string, ScriptableObject> AssetsOf(Type type, out IReadOnlyList<string> duplicateNames);
}

/// <summary>SO 필드 하나에 쓸 값. 변환·검증을 마친 상태.</summary>
public sealed class DataTableWrite
{
    internal DataTableWrite(ScriptableObject target, string propertyPath, SerializedPropertyType kind, object value, DataTableEntry source)
    {
        Target = target;
        PropertyPath = propertyPath;
        Kind = kind;
        Value = value;
        Source = source;
    }

    public ScriptableObject Target { get; }
    public string PropertyPath { get; }
    public SerializedPropertyType Kind { get; }
    public object Value { get; }
    public DataTableEntry Source { get; }
}

/// <summary>Verify 결과 한 줄 — SO(개발자 값)와 테이블(기획 값)이 다른 필드.</summary>
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
        $"{Write.Source.Location}: {Write.Target.name}.{Write.Source.Field} — SO {CurrentValue} / 테이블 {Write.Source.Value}";
}

/// <summary>메모리 적용 전 SO 상태. <see cref="DataTableApplier.Restore"/> 로 되돌린다. JsonUtility 로 SessionState 에 넣을 수 있다.</summary>
[Serializable]
public sealed class DataTableSnapshot
{
    [Serializable]
    public struct Item
    {
        public int instanceId;          // 같은 에디터 세션 안에서는 이걸로 찾는다(에셋이 아닌 메모리 SO 도 됨).
        public string globalObjectId;   // 세션이 바뀌었으면(크래시 후 등) 이걸로.
        public string json;
    }

    public List<Item> items = new List<Item>();
}

/// <summary>
/// <see cref="DataTableEntry"/> → SO 필드 쓰기. 세 단계:
/// <list type="number">
/// <item><see cref="Bind"/> — 타입·에셋·필드를 찾고 값을 변환한다. <b>쓰지 않는다.</b> 문제는 전부 issues 로.</item>
/// <item><see cref="ApplyInMemory"/>/<see cref="ApplyToDisk"/> — Bind 에 오류가 없을 때만 호출한다(전부 또는 0).</item>
/// <item><see cref="Diff"/> — 쓰지 않고 SO 현재 값과 비교(Verify).</item>
/// </list>
/// 다루는 타입: 정수·실수·불리언·문자열·열거형(이름). 참조(프리팹·VFX 등)는 테이블 대상이 아니다 — 인스펙터에서 연결.
/// 배열 원소는 기존 크기 안에서만 쓴다(테이블이 배열 크기를 바꾸지 않는다).
/// </summary>
public static class DataTableApplier
{
    private static readonly Regex ArrayIndex = new Regex(@"\[(\d+)\]", RegexOptions.Compiled);

    public static List<DataTableWrite> Bind(IEnumerable<DataTableEntry> entries, IDataTableAssetLookup lookup, DataTableIssues issues)
    {
        var writes = new List<DataTableWrite>();
        var serialized = new Dictionary<ScriptableObject, SerializedObject>();
        var written = new Dictionary<(ScriptableObject, string), DataTableEntry>();
        var mentioned = new HashSet<ScriptableObject>();

        foreach (IGrouping<string, DataTableEntry> sheet in entries.GroupBy(e => e.Sheet, StringComparer.Ordinal))
        {
            DataTableEntry firstEntry = sheet.First();
            Type type = lookup.FindType(sheet.Key, out string typeError);
            if (type == null)
            {
                issues.Error(firstEntry.Location, typeError);
                continue;
            }

            IReadOnlyDictionary<string, ScriptableObject> assets = lookup.AssetsOf(type, out IReadOnlyList<string> duplicateNames);
            var duplicates = new HashSet<string>(duplicateNames, StringComparer.Ordinal);

            foreach (DataTableEntry entry in sheet)
            {
                if (duplicates.Contains(entry.AssetId))
                {
                    issues.Error(entry.Location, $"{type.Name} 에셋 '{entry.AssetId}' 가 여러 개다 — 파일 이름을 겹치지 않게 할 것.");
                    continue;
                }

                if (!assets.TryGetValue(entry.AssetId, out ScriptableObject target))
                {
                    issues.Error(entry.Location,
                        $"{type.Name} 에셋 '{entry.AssetId}' 가 없다. 새 항목이면 프로그래머가 SO 를 먼저 만들어야 한다(테이블은 SO 를 만들지 않는다).");
                    continue;
                }

                mentioned.Add(target);
                if (!serialized.TryGetValue(target, out SerializedObject so))
                {
                    so = new SerializedObject(target);
                    serialized[target] = so;
                }

                string path = ToPropertyPath(entry.Field);
                SerializedProperty property = so.FindProperty(path);
                if (property == null)
                {
                    issues.Error(entry.Location, path.Contains(".Array.data[")
                        ? $"{type.Name}.{entry.Field} — 필드가 없거나 배열 범위를 벗어났다(테이블은 배열 크기를 바꾸지 않는다)."
                        : $"{type.Name} 에 필드 '{entry.Field}' 가 없다(직렬화 필드 이름과 같아야 한다).");
                    continue;
                }

                if (written.TryGetValue((target, path), out DataTableEntry previous))
                {
                    issues.Error(entry.Location, $"'{target.name}.{entry.Field}' 를 {previous.Location} 에서도 쓴다.");
                    continue;
                }

                if (!TryConvert(property, entry.Value, out object value, out string convertError))
                {
                    issues.Error(entry.Location, $"'{target.name}.{entry.Field}' = '{entry.Value}' — {convertError}");
                    continue;
                }

                written[(target, path)] = entry;
                writes.Add(new DataTableWrite(target, path, property.propertyType, value, entry));
            }

            foreach (KeyValuePair<string, ScriptableObject> pair in assets.Where(p => !mentioned.Contains(p.Value)))
            {
                issues.Warning($"{firstEntry.Location.Split('!')[0]}", $"{type.Name} 에셋 '{pair.Key}' 의 행이 테이블에 없다.");
            }
        }

        return writes;
    }

    /// <summary>
    /// 테이블 Play 용. 디스크에는 쓰지 않는다 — 적용 후 dirty 를 지워 저장 대상에서 뺀다.
    /// 반환한 스냅샷으로 <see cref="Restore"/> 해야 원래 값으로 돌아온다.
    /// </summary>
    public static DataTableSnapshot ApplyInMemory(IReadOnlyList<DataTableWrite> writes)
    {
        DataTableSnapshot snapshot = TakeSnapshot(writes);
        foreach (IGrouping<ScriptableObject, DataTableWrite> group in writes.GroupBy(w => w.Target))
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

    /// <summary>메모리 적용을 되돌린다. 찾지 못한 에셋 수를 반환한다(0 이 정상).</summary>
    public static int Restore(DataTableSnapshot snapshot)
    {
        int missing = 0;
        foreach (DataTableSnapshot.Item item in snapshot.items)
        {
            ScriptableObject target = FindSnapshotTarget(item);
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

    private static ScriptableObject FindSnapshotTarget(DataTableSnapshot.Item item)
    {
        if (EditorUtility.EntityIdToObject(item.instanceId) is ScriptableObject byInstance)
        {
            return byInstance;
        }

        return GlobalObjectId.TryParse(item.globalObjectId, out GlobalObjectId id)
            ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as ScriptableObject
            : null;
    }

    /// <summary>
    /// 디스크의 SO 를 테이블 값으로 덮어쓰고 저장한다(빌드·수동 동기화용). Undo 가능.
    /// 반환한 스냅샷을 <see cref="RestoreToDisk"/> 에 넘기면 적용 전 내용으로 다시 저장한다(빌드 후 원복).
    /// </summary>
    public static DataTableSnapshot ApplyToDisk(IReadOnlyList<DataTableWrite> writes)
    {
        DataTableSnapshot snapshot = TakeSnapshot(writes);
        foreach (IGrouping<ScriptableObject, DataTableWrite> group in writes.GroupBy(w => w.Target))
        {
            var so = new SerializedObject(group.Key);
            foreach (DataTableWrite write in group)
            {
                Assign(so.FindProperty(write.PropertyPath), write);
            }

            if (so.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(group.Key);
                AssetDatabase.SaveAssetIfDirty(group.Key);
            }
        }

        return snapshot;
    }

    /// <summary><see cref="ApplyToDisk"/> 전 내용으로 되돌려 저장한다. 찾지 못한 에셋 수를 반환한다(0 이 정상).</summary>
    public static int RestoreToDisk(DataTableSnapshot snapshot)
    {
        int missing = 0;
        foreach (DataTableSnapshot.Item item in snapshot.items)
        {
            ScriptableObject target = FindSnapshotTarget(item);
            if (target == null)
            {
                missing++;
                continue;
            }

            EditorJsonUtility.FromJsonOverwrite(item.json, target);
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
        }

        return missing;
    }

    private static DataTableSnapshot TakeSnapshot(IReadOnlyList<DataTableWrite> writes)
    {
        var snapshot = new DataTableSnapshot();
        foreach (ScriptableObject target in writes.Select(w => w.Target).Distinct())
        {
            snapshot.items.Add(new DataTableSnapshot.Item
            {
                instanceId = target.GetInstanceID(),
                globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(target).ToString(),
                json = EditorJsonUtility.ToJson(target),
            });
        }

        return snapshot;
    }

    /// <summary>SO 현재 값과 테이블 값이 다른 필드. 쓰지 않는다.</summary>
    public static List<DataTableDifference> Diff(IReadOnlyList<DataTableWrite> writes)
    {
        var differences = new List<DataTableDifference>();
        foreach (IGrouping<ScriptableObject, DataTableWrite> group in writes.GroupBy(w => w.Target))
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
