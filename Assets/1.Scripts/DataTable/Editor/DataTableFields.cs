using System;
using System.Globalization;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

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

    /// <summary>기술 값 표시 또는 네트워크 상태(NetworkVariable·NetworkList — 인스펙터 초기값이 아니라 런타임 동기화 값).</summary>
    public static bool IsExcluded(FieldInfo field) =>
        field.IsDefined(typeof(DataTableIgnoreAttribute), false) || typeof(NetworkVariableBase).IsAssignableFrom(field.FieldType);

    /// <summary>문자열 중 데이터 테이블이 소유하는 필드인지.</summary>
    public static bool IsTableText(FieldInfo field) =>
        field != null && field.FieldType == typeof(string) && field.IsDefined(typeof(DataTableTextAttribute), false);

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
