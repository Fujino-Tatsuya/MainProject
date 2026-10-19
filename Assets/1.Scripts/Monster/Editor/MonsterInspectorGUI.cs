using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 몬스터 데이터 인스펙터 공용 그리기 도구 — 테이블 배지 · 한국어 공격 이름 팝업 · 배열 인덱스.
/// MonsterDataSOEditor 와 공격·콤보·페이즈 행 Drawer 가 같이 쓴다.
/// </summary>
public static class MonsterInspectorGUI
{
    public static readonly Color DiffColor = new Color(1f, 0.82f, 0.25f);
    public const string TableMark = "  [표]";

    private static readonly Regex IndexPattern = new Regex(@"\[(\d+)\]$", RegexOptions.Compiled);
    private static readonly Dictionary<(Type, string), FieldInfo> FieldCache = new Dictionary<(Type, string), FieldInfo>();
    private static string[] attackNames;
    private static GUIStyle diffLabelStyle;

    /// <summary>칸 하나의 테이블 상태. 다중 선택이면 비교하지 않는다(기존 머리 배지와 같은 규칙).</summary>
    public readonly struct TableBadge
    {
        public TableBadge(bool has, bool differs, string tableValue)
        {
            Has = has;
            Differs = differs;
            TableValue = tableValue;
        }

        public bool Has { get; }
        public bool Differs { get; }
        public string TableValue { get; }
    }

    public static TableBadge Lookup(SerializedProperty property)
    {
        SerializedObject owner = property.serializedObject;
        if (owner.isEditingMultipleObjects
            || !DataTableInspectorBadge.TryGetWrite(owner.targetObject, property.propertyPath, out DataTableWrite write))
        {
            return default;
        }

        bool same = DataTableApplier.MatchesTable(owner, write, out _);
        return new TableBadge(true, !same, write.Source.Value);
    }

    /// <summary>표시 이름 + 원래 툴팁 + 변수 이름(검색·대화용) + 테이블 표시.</summary>
    public static GUIContent Label(string text, SerializedProperty property, TableBadge badge)
    {
        string tooltip = property.tooltip;
        tooltip = string.IsNullOrEmpty(tooltip) ? $"변수: {property.name}" : $"{tooltip}\n\n변수: {property.name}";
        if (badge.Has)
        {
            tooltip += "\n[표] 테이블 모드 Play·빌드에서는 xlsx(GameData) 값으로 덮어쓴다.";
            text += TableMark;
        }

        return new GUIContent(text, tooltip);
    }

    /// <summary>테이블과 다른 값일 때 칸 아래 한 줄 높이(없으면 0).</summary>
    public static float DiffLineHeight(TableBadge badge) =>
        badge.Differs ? EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing : 0f;

    public static void DrawDiffLine(Rect rect, TableBadge badge)
    {
        if (!badge.Differs)
        {
            return;
        }

        rect = EditorGUI.IndentedRect(rect);
        EditorGUI.LabelField(rect, $"↳ 표 값 {badge.TableValue} — 테이블 모드 Play·빌드에선 이 값이 쓰인다", DiffLabelStyle);
    }

    public static void DrawDiffLineLayout(TableBadge badge)
    {
        if (badge.Differs)
        {
            DrawDiffLine(EditorGUILayout.GetControlRect(), badge);
        }
    }

    private static GUIStyle DiffLabelStyle => diffLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
    {
        normal = { textColor = DiffColor },
        fontStyle = FontStyle.Bold,
    };

    /// <summary>"attacks.Array.data[3]" → 3. 배열 원소가 아니면 -1.</summary>
    public static int ArrayIndex(SerializedProperty property)
    {
        Match match = IndexPattern.Match(property.propertyPath);
        return match.Success ? int.Parse(match.Groups[1].Value) : -1;
    }

    public static string AttackName(SerializedProperty attackIdProperty)
    {
        if (attackIdProperty.hasMultipleDifferentValues)
        {
            return "—";
        }

        var values = (BossAttackId[])Enum.GetValues(typeof(BossAttackId));
        int index = attackIdProperty.enumValueIndex;
        return index >= 0 && index < values.Length ? MonsterInspectorCatalog.AttackName(values[index]) : "?";
    }

    /// <summary>공격 종류를 한국어 이름으로 고르는 팝업. 직렬화 값(enum 순서)은 그대로다.</summary>
    public static void AttackIdPopup(Rect rect, GUIContent label, SerializedProperty property)
    {
        attackNames ??= Array.ConvertAll((BossAttackId[])Enum.GetValues(typeof(BossAttackId)), MonsterInspectorCatalog.AttackName);
        label = EditorGUI.BeginProperty(rect, label, property);
        EditorGUI.BeginChangeCheck();
        int picked = EditorGUI.Popup(rect, label, property.enumValueIndex, Array.ConvertAll(attackNames, n => new GUIContent(n)));
        if (EditorGUI.EndChangeCheck())
        {
            property.enumValueIndex = picked;
        }

        EditorGUI.EndProperty();
    }

    /// <summary>선언 필드(상속 포함). 없으면 null.</summary>
    public static FieldInfo FieldOf(Type type, string name)
    {
        if (FieldCache.TryGetValue((type, name), out FieldInfo cached))
        {
            return cached;
        }

        FieldInfo found = null;
        for (Type t = type; t != null && found == null; t = t.BaseType)
        {
            found = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        }

        FieldCache[(type, name)] = found;
        return found;
    }
}
