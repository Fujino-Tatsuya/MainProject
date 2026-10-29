using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// SerializedProperty 경로 ↔ C# 필드(FieldInfo). 템플릿(어떤 필드를 내보낼지)과 적용기(범위 검증)가 같이 쓴다.
/// 경로 예: <c>charge.speed</c>, <c>phases.Array.data[0]</c>(배열 원소면 배열 필드 자체를 돌려준다).
/// </summary>
public static class DataTableFields
{
    /// <summary>경로를 따라가 마지막 필드의 FieldInfo. 못 찾으면 null(Unity 내부 값 m_* 등).</summary>
    public static FieldInfo Resolve(Type root, string propertyPath)
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

    /// <summary>
    /// <see cref="Resolve(Type, string)"/> + <c>[SerializeReference]</c> 칸. 선언 타입으로 못 찾으면(다형 목록 원소의 하위 타입 필드 —
    /// 특성 효과 <c>effects[0].damageIncrease</c>, 할 일 125) 대상 객체의 실제 값을 따라가 그 런타임 타입에서 찾는다.
    /// </summary>
    public static FieldInfo Resolve(Object target, string propertyPath)
    {
        if (target == null)
        {
            return null;
        }

        return Resolve(target.GetType(), propertyPath) ?? WalkInstance(target, propertyPath, null);
    }

    /// <summary><see cref="IsUnderExcluded(Type, string)"/> + <c>[SerializeReference]</c> 칸(실제 값의 런타임 타입으로 조상을 본다).</summary>
    public static bool IsUnderExcluded(Object target, string propertyPath)
    {
        if (target == null)
        {
            return false;
        }

        if (IsUnderExcluded(target.GetType(), propertyPath))
        {
            return true;
        }

        var chain = new List<FieldInfo>();
        WalkInstance(target, propertyPath, chain);
        for (int i = 0; i < chain.Count - 1; i++)
        {
            if (IsExcluded(chain[i]))
            {
                return true;
            }
        }

        return false;
    }

    // 실제 값을 따라 경로를 걷는다 — 배열 원소·참조 칸은 들어 있는 객체의 런타임 타입에서 필드를 찾는다. 못 찾으면 null.
    private static FieldInfo WalkInstance(object root, string propertyPath, List<FieldInfo> chain)
    {
        object current = root;
        FieldInfo last = null;
        string[] parts = propertyPath.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "Array")
            {
                i++;
                // "entries.Array" 자체(배열 머리) = 배열 필드 — 타입 판 Resolve 와 같다. 이게 null 이면 순회가 배열 안으로 못 들어가
                // [SerializeReference] 원소 안쪽 배열(⑩ 스킬 수치 entries[j].value)이 시트에서 빠진다(할 일 133).
                if (i >= parts.Length)
                {
                    return last;
                }

                if (!TryParseDataIndex(parts[i], out int index) || !(current is IList list) || index >= list.Count)
                {
                    return null;
                }

                current = list[index];
                continue;
            }

            if (current == null)
            {
                return null;
            }

            last = FindField(current.GetType(), parts[i]);
            if (last == null)
            {
                return null;
            }

            chain?.Add(last);
            current = last.GetValue(current);
        }

        return last;
    }

    private static bool TryParseDataIndex(string part, out int index)
    {
        index = -1;
        const string prefix = "data[";
        return part.StartsWith(prefix, StringComparison.Ordinal) && part.EndsWith("]", StringComparison.Ordinal) &&
               int.TryParse(part.Substring(prefix.Length, part.Length - prefix.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }

    /// <summary>기술 값 표시 또는 네트워크 상태(NetworkVariable·NetworkList — 인스펙터 초기값이 아니라 런타임 동기화 값).</summary>
    public static bool IsExcluded(FieldInfo field) =>
        field.IsDefined(typeof(DataTableIgnoreAttribute), false) || typeof(NetworkVariableBase).IsAssignableFrom(field.FieldType);

    /// <summary>문자열 중 데이터 테이블이 소유하는 필드인지.</summary>
    public static bool IsTableText(FieldInfo field) =>
        field != null && field.FieldType == typeof(string) && field.IsDefined(typeof(DataTableTextAttribute), false);

    /// <summary>테이블 문자열 중 Strings 시트에 자동 키로 모을 문구인지(<c>[DataTableText(Localize = false)]</c> 는 아님).</summary>
    public static bool IsLocalizedText(FieldInfo field) =>
        IsTableText(field) && field.GetCustomAttribute<DataTableTextAttribute>(false).Localize;

    /// <summary>경로의 조상(마지막 필드 제외) 중 제외 대상이 있으면 true — 구조체·배열에 붙인 [DataTableIgnore] 는 그 아래 전부.</summary>
    public static bool IsUnderExcluded(Type root, string propertyPath)
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

    /// <summary>
    /// 필드의 <see cref="RangeAttribute"/>·<see cref="MinAttribute"/> 를 벗어나면 이유, 아니면 null.
    /// 인스펙터가 막는 범위를 테이블도 똑같이 막는다(배열 필드의 범위는 원소마다 적용 — Unity 인스펙터와 같다).
    /// </summary>
    public static string CheckRange(FieldInfo field, double value)
    {
        if (field == null)
        {
            return null;
        }

        var range = field.GetCustomAttribute<RangeAttribute>();
        if (range != null && (value < range.min || value > range.max))
        {
            return $"범위 {Format(range.min)} ~ {Format(range.max)} 밖이다(인스펙터 [Range]).";
        }

        var min = field.GetCustomAttribute<MinAttribute>();
        if (min != null && value < min.min)
        {
            return $"{Format(min.min)} 이상이어야 한다(인스펙터 [Min]).";
        }

        return null;
    }

    private static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);

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
}
