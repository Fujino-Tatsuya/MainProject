using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 몬스터·보스 데이터 인스펙터 — 한국어 이름 + 묶음별 접기 + 칸 옆 테이블 표시(PLAN-monster-data-inspector.md).
/// <para>
/// 🔴 **보이는 것만 바꾼다.** 직렬화·변수 이름·배열 순서는 그대로고, 값은 전부 SerializedProperty 로만 쓴다
/// (다중 편집 · Undo · 프리팹 오버라이드 유지). 접힘 상태는 SessionState 에만 둔다 — 열고 접는 것으로 에셋이 바뀌지 않는다.
/// </para>
/// 칸 이름·묶음은 <see cref="MonsterInspectorCatalog"/> 한 곳에서 고친다.
/// </summary>
[CustomEditor(typeof(MonsterDataSO), true)]
[CanEditMultipleObjects]
public sealed class MonsterDataSOEditor : Editor, IDataTableFieldBadgeEditor
{
    private const string FoldKeyPrefix = "MonsterInspector.Fold.";

    /// <summary>처음부터 펼쳐 두는 배열(PLAN §3.3 — 공격 목록·콤보는 펼침). 행 자체는 제목만 보이게 접혀 있다.</summary>
    private static readonly HashSet<string> OpenArrays = new HashSet<string> { "attacks", "combos" };

    private static GUIStyle groupFoldoutStyle;
    private static GUIStyle noteStyle;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
        }

        if (!serializedObject.isEditingMultipleObjects && !Directory.Exists(DataTableSource.Folder))
        {
            EditorGUILayout.LabelField("[표] 비교 불가 — 데이터 테이블 폴더가 없다(SVN 아트 미수신). 칸 옆 표시가 안 나온다.", NoteStyle);
        }

        Dictionary<string, List<SerializedProperty>> buckets = Bucket();
        foreach (MonsterInspectorCatalog.Group group in MonsterInspectorCatalog.Groups)
        {
            if (buckets.TryGetValue(group.Key, out List<SerializedProperty> properties))
            {
                DrawGroup(group, properties);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>최상위 칸을 묶음별로 나눈다. 사전에 없는 칸은 미분류로 간다.</summary>
    private Dictionary<string, List<SerializedProperty>> Bucket()
    {
        var buckets = new Dictionary<string, List<SerializedProperty>>();
        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (iterator.name == "m_Script")
            {
                continue;
            }

            string key = MonsterInspectorCatalog.GroupOf(iterator.name);
            if (!buckets.TryGetValue(key, out List<SerializedProperty> list))
            {
                buckets[key] = list = new List<SerializedProperty>();
            }

            list.Add(iterator.Copy());
        }

        foreach (List<SerializedProperty> list in buckets.Values)
        {
            // 사전에 적은 순서. 미분류는 선언 순서 그대로(정렬 키가 같아 안정 정렬이 유지).
            list.Sort((a, b) => Order(a).CompareTo(Order(b)));
        }

        return buckets;
    }

    private static int Order(SerializedProperty property) =>
        MonsterInspectorCatalog.Fields.TryGetValue(property.name, out MonsterInspectorCatalog.Field field) ? field.Order : int.MaxValue;

    private void DrawGroup(MonsterInspectorCatalog.Group group, List<SerializedProperty> properties)
    {
        string key = FoldKeyPrefix + group.Key;
        bool open = SessionState.GetBool(key, DefaultOpen(group));

        EditorGUILayout.Space(2f);
        bool now = EditorGUILayout.Foldout(open, $"{group.Title}   ({properties.Count})", true, GroupFoldoutStyle);
        if (now != open)
        {
            SessionState.SetBool(key, now);
        }

        if (!now)
        {
            return;
        }

        Color previous = GUI.color;
        if (group.Dimmed)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
        }

        EditorGUI.indentLevel++;
        if (!string.IsNullOrEmpty(group.Note))
        {
            EditorGUILayout.LabelField(group.Note, NoteStyle);
        }

        foreach (SerializedProperty property in properties)
        {
            DrawField(property);
        }

        EditorGUI.indentLevel--;
        GUI.color = previous;
    }

    /// <summary>원거리 묶음은 원거리 몬스터일 때만 펼쳐서 시작한다.</summary>
    private bool DefaultOpen(MonsterInspectorCatalog.Group group)
    {
        if (group.Key != MonsterInspectorCatalog.Ranged)
        {
            return group.DefaultOpen;
        }

        SerializedProperty archetype = serializedObject.FindProperty("archetype");
        return archetype != null && !archetype.hasMultipleDifferentValues
            && (archetype.enumValueIndex == (int)MonsterArchetype.RangedTurret || archetype.enumValueIndex == (int)MonsterArchetype.RangedMobile);
    }

    private void DrawField(SerializedProperty property)
    {
        string text = MonsterInspectorCatalog.Fields.TryGetValue(property.name, out MonsterInspectorCatalog.Field field)
            ? field.Label
            : ObjectNames.NicifyVariableName(property.name);

        if (property.isArray && property.propertyType != SerializedPropertyType.String)
        {
            DrawArray(property, MonsterInspectorGUI.Label(text, property, default));
            return;
        }

        MonsterInspectorGUI.TableBadge badge = MonsterInspectorGUI.Lookup(property);
        DrawValue(property, MonsterInspectorGUI.Label(text, property, badge), badge);
        MonsterInspectorGUI.DrawDiffLineLayout(badge);
    }

    private void DrawValue(SerializedProperty property, GUIContent label, MonsterInspectorGUI.TableBadge badge)
    {
        Color previous = GUI.backgroundColor;
        if (badge.Differs)
        {
            GUI.backgroundColor = MonsterInspectorGUI.DiffColor;
        }

        FieldInfo info = MonsterInspectorGUI.FieldOf(target.GetType(), property.name);
        if (info != null && HasDecorator(info))
        {
            // [Header]·[Space] 가 붙은 칸은 PropertyField 로 그리면 머리글이 엉뚱한 묶음에 따라 나온다 → 값만 직접 그린다.
            DrawBare(EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight), property, label, info);
        }
        else
        {
            EditorGUILayout.PropertyField(property, label, true);
        }

        GUI.backgroundColor = previous;
    }

    private static bool HasDecorator(FieldInfo info) =>
        info.IsDefined(typeof(HeaderAttribute), true) || info.IsDefined(typeof(SpaceAttribute), true);

    /// <summary>머리글 없이 값만 — Range·Min 은 여기서 직접 지킨다.</summary>
    private static void DrawBare(Rect rect, SerializedProperty property, GUIContent label, FieldInfo info)
    {
        var range = info.GetCustomAttribute<RangeAttribute>(true);
        var min = info.GetCustomAttribute<MinAttribute>(true);

        label = EditorGUI.BeginProperty(rect, label, property);
        EditorGUI.BeginChangeCheck();
        switch (property.propertyType)
        {
            case SerializedPropertyType.Float when range != null:
                float slid = EditorGUI.Slider(rect, label, property.floatValue, range.min, range.max);
                if (EditorGUI.EndChangeCheck()) property.floatValue = slid;
                break;
            case SerializedPropertyType.Integer when range != null:
                int slidInt = EditorGUI.IntSlider(rect, label, property.intValue, (int)range.min, (int)range.max);
                if (EditorGUI.EndChangeCheck()) property.intValue = slidInt;
                break;
            case SerializedPropertyType.Float:
                float f = EditorGUI.FloatField(rect, label, property.floatValue);
                if (EditorGUI.EndChangeCheck()) property.floatValue = min != null ? Mathf.Max(min.min, f) : f;
                break;
            case SerializedPropertyType.Integer:
                int i = EditorGUI.IntField(rect, label, property.intValue);
                if (EditorGUI.EndChangeCheck()) property.intValue = min != null ? Mathf.Max((int)min.min, i) : i;
                break;
            case SerializedPropertyType.Boolean:
                bool b = EditorGUI.Toggle(rect, label, property.boolValue);
                if (EditorGUI.EndChangeCheck()) property.boolValue = b;
                break;
            case SerializedPropertyType.String:
                string s = EditorGUI.TextField(rect, label, property.stringValue);
                if (EditorGUI.EndChangeCheck()) property.stringValue = s;
                break;
            case SerializedPropertyType.Enum:
                int e = EditorGUI.Popup(rect, label, property.enumValueIndex, property.enumDisplayNames.Select(n => new GUIContent(n)).ToArray());
                if (EditorGUI.EndChangeCheck()) property.enumValueIndex = e;
                break;
            case SerializedPropertyType.ObjectReference:
                Object o = EditorGUI.ObjectField(rect, label, property.objectReferenceValue, info.FieldType, false);
                if (EditorGUI.EndChangeCheck()) property.objectReferenceValue = o;
                break;
            case SerializedPropertyType.Color:
                Color c = EditorGUI.ColorField(rect, label, property.colorValue);
                if (EditorGUI.EndChangeCheck()) property.colorValue = c;
                break;
            default:
                EditorGUI.EndChangeCheck();
                EditorGUI.EndProperty();
                // 위에서 못 다루는 타입 — 머리글이 따라 나와도 값을 잃지 않는 쪽을 고른다.
                EditorGUILayout.PropertyField(property, label, true);
                return;
        }

        EditorGUI.EndProperty();
    }

    /// <summary>
    /// 배열 — 개수 칸 + 원소. 🔴 끌어서 순서 바꾸기는 일부러 없다: 공격 행 순서가 쿨다운 슬롯·xlsx 행과 묶여 있다.
    /// 개수를 줄이면 끝에서부터 지워진다(중간 삭제 없음 = 슬롯 번호가 밀리지 않는다).
    /// </summary>
    private void DrawArray(SerializedProperty array, GUIContent label)
    {
        label.text = $"{label.text}   ({(array.hasMultipleDifferentValues ? "—" : array.arraySize.ToString())})";
        string key = FoldKeyPrefix + "Array." + array.name;
        bool open = SessionState.GetBool(key, OpenArrays.Contains(array.name));
        bool now = EditorGUILayout.Foldout(open, label, true);
        if (now != open)
        {
            SessionState.SetBool(key, now);
        }

        if (!now)
        {
            return;
        }

        EditorGUI.indentLevel++;
        SerializedProperty size = array.FindPropertyRelative("Array.size");
        EditorGUILayout.PropertyField(size, new GUIContent("개수", "줄이면 끝에서부터 지워진다. 늘리면 마지막 행이 복사돼 끝에 붙는다."));
        if (size.hasMultipleDifferentValues)
        {
            EditorGUILayout.HelpBox("선택한 에셋끼리 행 개수가 달라 행은 에셋 하나씩 고친다.", MessageType.Info);
            EditorGUI.indentLevel--;
            return;
        }

        for (int i = 0; i < array.arraySize; i++)
        {
            SerializedProperty element = array.GetArrayElementAtIndex(i);
            DrawElement(array.name, element, i);
        }

        EditorGUI.indentLevel--;
    }

    private void DrawElement(string arrayName, SerializedProperty element, int index)
    {
        MonsterInspectorGUI.TableBadge badge = MonsterInspectorGUI.Lookup(element);
        switch (arrayName)
        {
            case "openingCombo":
                MonsterInspectorGUI.AttackIdPopup(EditorGUILayout.GetControlRect(),
                    MonsterInspectorGUI.Label($"{index + 1}타", element, badge), element);
                break;
            case "partyHpMultipliers":
                EditorGUILayout.PropertyField(element, MonsterInspectorGUI.Label($"{index + 1}인{(index == 2 ? " 이상" : "")}", element, badge));
                break;
            default:
                // 공격·콤보·페이즈 행은 각자 Drawer 가 제목을 만든다.
                EditorGUILayout.PropertyField(element, true);
                break;
        }

        MonsterInspectorGUI.DrawDiffLineLayout(badge);
    }

    private static GUIStyle GroupFoldoutStyle => groupFoldoutStyle ??= new GUIStyle(EditorStyles.foldout)
    {
        fontStyle = FontStyle.Bold,
        fontSize = 12,
    };

    private static GUIStyle NoteStyle => noteStyle ??= new GUIStyle(EditorStyles.miniLabel)
    {
        wordWrap = true,
        normal = { textColor = new Color(0.6f, 0.6f, 0.6f) },
    };
}
