using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 공격 행 — 제목 = 공격 이름 + 핵심 수치. 펼치면 피해·쿨·거리·가중치·간파 창만 먼저 보이고,
/// 판정 모양·예고·애니 이름은 행 안의 "세부" 에 다시 접힌다(PLAN-monster-data-inspector §3.3).
/// </summary>
[CustomPropertyDrawer(typeof(BossAttackEntry))]
public sealed class BossAttackEntryDrawer : PropertyDrawer
{
    private const string DetailKeyPrefix = "MonsterInspector.AttackDetail.";

    private static float Line => EditorGUIUtility.singleLineHeight;
    private static float Gap => EditorGUIUtility.standardVerticalSpacing;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = Line;
        if (!property.isExpanded)
        {
            return height;
        }

        foreach (string name in CoreRows)
        {
            height += Gap + Line + DiffHeight(property, name);
        }

        height += Gap + Line; // 세부 접기
        if (DetailOpen(property))
        {
            foreach (SerializedProperty child in DetailChildren(property))
            {
                height += Gap + EditorGUI.GetPropertyHeight(child, true) + MonsterInspectorGUI.DiffLineHeight(MonsterInspectorGUI.Lookup(child));
            }
        }

        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, GUIContent.none, property);
        var row = new Rect(position.x, position.y, position.width, Line);
        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, Title(property), true);
        if (!property.isExpanded)
        {
            EditorGUI.EndProperty();
            return;
        }

        EditorGUI.indentLevel++;
        foreach (string name in CoreRows)
        {
            row.y += row.height + Gap;
            row.height = Line;
            if (name == "distance")
            {
                DrawDistance(ref row, property);
                continue;
            }

            SerializedProperty child = property.FindPropertyRelative(name);
            MonsterInspectorGUI.TableBadge badge = MonsterInspectorGUI.Lookup(child);
            GUIContent childLabel = MonsterInspectorGUI.Label(MonsterInspectorCatalog.AttackFields[name], child, badge);
            DrawTinted(badge, () =>
            {
                if (name == "attackId")
                {
                    MonsterInspectorGUI.AttackIdPopup(row, childLabel, child);
                }
                else
                {
                    EditorGUI.PropertyField(row, child, childLabel);
                }
            });
            DrawDiff(ref row, badge);
        }

        row.y += row.height + Gap;
        row.height = Line;
        bool open = DetailOpen(property);
        bool now = EditorGUI.Foldout(row, open, "세부 (판정 모양 · 예고 · 애니 이름 · 전진)", true);
        if (now != open)
        {
            SessionState.SetBool(DetailKey(property), now);
        }

        if (now)
        {
            EditorGUI.indentLevel++;
            foreach (SerializedProperty child in DetailChildren(property))
            {
                row.y += row.height + Gap;
                row.height = EditorGUI.GetPropertyHeight(child, true);
                MonsterInspectorGUI.TableBadge badge = MonsterInspectorGUI.Lookup(child);
                string text = MonsterInspectorCatalog.AttackFields.TryGetValue(child.name, out string known)
                    ? known
                    : ObjectNames.NicifyVariableName(child.name);
                GUIContent childLabel = MonsterInspectorGUI.Label(text, child, badge);
                Rect at = row;
                DrawTinted(badge, () => EditorGUI.PropertyField(at, child, childLabel, true));
                DrawDiff(ref row, badge);
            }

            EditorGUI.indentLevel--;
        }

        EditorGUI.indentLevel--;
        EditorGUI.EndProperty();
    }

    /// <summary>펼쳤을 때 먼저 보이는 줄. "distance" = 거리 최소~최대 한 줄.</summary>
    private static readonly string[] CoreRows = { "attackId", "damage", "cooldown", "distance", "weight", "opensCounterWindow", "counterWindowDuration" };

    private static IEnumerable<SerializedProperty> DetailChildren(SerializedProperty entry)
    {
        SerializedProperty child = entry.Copy();
        SerializedProperty end = entry.GetEndProperty();
        if (!child.NextVisible(true))
        {
            yield break;
        }

        while (!SerializedProperty.EqualContents(child, end))
        {
            if (!MonsterInspectorCatalog.AttackCoreFields.Contains(child.name))
            {
                yield return child.Copy();
            }

            if (!child.NextVisible(false))
            {
                yield break;
            }
        }
    }

    private static float DiffHeight(SerializedProperty entry, string row)
    {
        if (row != "distance")
        {
            return MonsterInspectorGUI.DiffLineHeight(MonsterInspectorGUI.Lookup(entry.FindPropertyRelative(row)));
        }

        return MonsterInspectorGUI.DiffLineHeight(MonsterInspectorGUI.Lookup(entry.FindPropertyRelative("minDistance")))
            + MonsterInspectorGUI.DiffLineHeight(MonsterInspectorGUI.Lookup(entry.FindPropertyRelative("maxDistance")));
    }

    private static void DrawDistance(ref Rect row, SerializedProperty entry)
    {
        SerializedProperty min = entry.FindPropertyRelative("minDistance");
        SerializedProperty max = entry.FindPropertyRelative("maxDistance");
        MonsterInspectorGUI.TableBadge minBadge = MonsterInspectorGUI.Lookup(min);
        MonsterInspectorGUI.TableBadge maxBadge = MonsterInspectorGUI.Lookup(max);

        string text = "거리 (최소 ~ 최대 m)" + (minBadge.Has || maxBadge.Has ? MonsterInspectorGUI.TableMark : "");
        Rect field = EditorGUI.PrefixLabel(row, new GUIContent(text, $"{min.tooltip}\n{max.tooltip}\n\n변수: minDistance · maxDistance"));
        int indent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        float half = (field.width - 14f) * 0.5f;
        var left = new Rect(field.x, field.y, half, field.height);
        var tilde = new Rect(left.xMax, field.y, 14f, field.height);
        var right = new Rect(tilde.xMax, field.y, half, field.height);
        DrawTinted(minBadge, () => EditorGUI.PropertyField(left, min, GUIContent.none));
        EditorGUI.LabelField(tilde, "~");
        DrawTinted(maxBadge, () => EditorGUI.PropertyField(right, max, GUIContent.none));
        EditorGUI.indentLevel = indent;

        DrawDiff(ref row, minBadge);
        DrawDiff(ref row, maxBadge);
    }

    private static void DrawDiff(ref Rect row, MonsterInspectorGUI.TableBadge badge)
    {
        if (!badge.Differs)
        {
            return;
        }

        row.y += row.height + Gap;
        row.height = Line;
        MonsterInspectorGUI.DrawDiffLine(row, badge);
    }

    private static void DrawTinted(MonsterInspectorGUI.TableBadge badge, System.Action draw)
    {
        Color previous = GUI.backgroundColor;
        if (badge.Differs)
        {
            GUI.backgroundColor = MonsterInspectorGUI.DiffColor;
        }

        draw();
        GUI.backgroundColor = previous;
    }

    private static string Title(SerializedProperty entry)
    {
        int index = MonsterInspectorGUI.ArrayIndex(entry);
        string name = MonsterInspectorGUI.AttackName(entry.FindPropertyRelative("attackId"));
        float weight = entry.FindPropertyRelative("weight").floatValue;
        int damage = entry.FindPropertyRelative("damage").intValue;
        float cooldown = entry.FindPropertyRelative("cooldown").floatValue;
        float min = entry.FindPropertyRelative("minDistance").floatValue;
        float max = entry.FindPropertyRelative("maxDistance").floatValue;
        bool anywhere = entry.FindPropertyRelative("ignoreDistanceWindow").boolValue;

        string distance = anywhere ? "거리 무관" : $"{min:0.#}~{max:0.#}m";
        string pick = weight <= 0f ? "룰렛 제외" : $"가중치 {weight:0.#}";
        string slot = index >= 0 ? $"#{index}  " : "";
        return $"{slot}{name}    피해 {(damage == 0 ? "기본" : damage.ToString())} · 쿨 {cooldown:0.#}s · {distance} · {pick}";
    }

    private static bool DetailOpen(SerializedProperty entry) => SessionState.GetBool(DetailKey(entry), false);

    private static string DetailKey(SerializedProperty entry) =>
        DetailKeyPrefix + entry.serializedObject.targetObject.GetInstanceID() + "." + entry.propertyPath;
}

/// <summary>콤보 행 — 제목 = 이름 · 가중치 · 쿨 · 순서(공격 이름으로).</summary>
[CustomPropertyDrawer(typeof(BossComboEntry))]
public sealed class BossComboEntryDrawer : PropertyDrawer
{
    private static float Line => EditorGUIUtility.singleLineHeight;
    private static float Gap => EditorGUIUtility.standardVerticalSpacing;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = Line;
        if (property.isExpanded)
        {
            foreach (SerializedProperty child in Children(property))
            {
                height += Gap + EditorGUI.GetPropertyHeight(child, true);
            }
        }

        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, GUIContent.none, property);
        var row = new Rect(position.x, position.y, position.width, Line);
        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, Title(property), true);
        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            foreach (SerializedProperty child in Children(property))
            {
                row.y += row.height + Gap;
                row.height = EditorGUI.GetPropertyHeight(child, true);
                string text = MonsterInspectorCatalog.ComboFields.TryGetValue(child.name, out string known)
                    ? known
                    : ObjectNames.NicifyVariableName(child.name);
                EditorGUI.PropertyField(row, child, MonsterInspectorGUI.Label(text, child, default), true);
            }

            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    private static IEnumerable<SerializedProperty> Children(SerializedProperty entry)
    {
        SerializedProperty child = entry.Copy();
        SerializedProperty end = entry.GetEndProperty();
        if (!child.NextVisible(true))
        {
            yield break;
        }

        while (!SerializedProperty.EqualContents(child, end))
        {
            yield return child.Copy();
            if (!child.NextVisible(false))
            {
                yield break;
            }
        }
    }

    private static string Title(SerializedProperty entry)
    {
        string name = entry.FindPropertyRelative("name").stringValue;
        float weight = entry.FindPropertyRelative("weight").floatValue;
        float cooldown = entry.FindPropertyRelative("cooldown").floatValue;
        SerializedProperty steps = entry.FindPropertyRelative("steps");
        var order = new List<string>();
        for (int i = 0; i < steps.arraySize; i++)
        {
            order.Add(MonsterInspectorGUI.AttackName(steps.GetArrayElementAtIndex(i).FindPropertyRelative("attack")));
        }

        string pick = weight <= 0f ? "룰렛 제외" : $"가중치 {weight:0.#}";
        string sequence = order.Count == 0 ? "(타 없음)" : string.Join(" → ", order);
        return $"{(string.IsNullOrEmpty(name) ? "(이름 없음)" : name)}    {pick} · 쿨 {cooldown:0.#}s · {sequence}";
    }
}

/// <summary>콤보 한 타 — "1타 [공격] [무조건 시전]" 한 줄.</summary>
[CustomPropertyDrawer(typeof(BossComboStep))]
public sealed class BossComboStepDrawer : PropertyDrawer
{
    private const float ToggleWidth = 110f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        int index = MonsterInspectorGUI.ArrayIndex(property);
        SerializedProperty attack = property.FindPropertyRelative("attack");
        SerializedProperty forceFire = property.FindPropertyRelative("forceFire");

        var popup = new Rect(position.x, position.y, position.width - ToggleWidth - 4f, position.height);
        var toggle = new Rect(popup.xMax + 4f, position.y, ToggleWidth, position.height);
        MonsterInspectorGUI.AttackIdPopup(popup, new GUIContent(index >= 0 ? $"{index + 1}타" : "타", "변수: attack"), attack);

        int indent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        EditorGUI.BeginProperty(toggle, GUIContent.none, forceFire);
        EditorGUI.BeginChangeCheck();
        bool value = EditorGUI.ToggleLeft(toggle, new GUIContent("무조건 시전", forceFire.tooltip + "\n\n변수: forceFire"), forceFire.boolValue);
        if (EditorGUI.EndChangeCheck())
        {
            forceFire.boolValue = value;
        }

        EditorGUI.EndProperty();
        EditorGUI.indentLevel = indent;
    }
}

/// <summary>페이즈 행 — 제목 = "페이즈 N — HP 66% 이하 · 송전기".</summary>
[CustomPropertyDrawer(typeof(BossPhaseEntry))]
public sealed class BossPhaseEntryDrawer : PropertyDrawer
{
    private static readonly string[] Rows = { "hpThreshold", "sequence", "damageMultiplier", "speedMultiplier" };

    private static float Line => EditorGUIUtility.singleLineHeight;
    private static float Gap => EditorGUIUtility.standardVerticalSpacing;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = Line;
        if (property.isExpanded)
        {
            foreach (string name in Rows)
            {
                height += Gap + Line + MonsterInspectorGUI.DiffLineHeight(MonsterInspectorGUI.Lookup(property.FindPropertyRelative(name)));
            }
        }

        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, GUIContent.none, property);
        var row = new Rect(position.x, position.y, position.width, Line);
        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, Title(property), true);
        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            foreach (string name in Rows)
            {
                row.y += row.height + Gap;
                row.height = Line;
                SerializedProperty child = property.FindPropertyRelative(name);
                MonsterInspectorGUI.TableBadge badge = MonsterInspectorGUI.Lookup(child);
                Color previous = GUI.backgroundColor;
                if (badge.Differs)
                {
                    GUI.backgroundColor = MonsterInspectorGUI.DiffColor;
                }

                EditorGUI.PropertyField(row, child, MonsterInspectorGUI.Label(MonsterInspectorCatalog.PhaseFields[name], child, badge));
                GUI.backgroundColor = previous;
                if (badge.Differs)
                {
                    row.y += row.height + Gap;
                    MonsterInspectorGUI.DrawDiffLine(row, badge);
                }
            }

            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    private static string Title(SerializedProperty entry)
    {
        int index = MonsterInspectorGUI.ArrayIndex(entry);
        float threshold = entry.FindPropertyRelative("hpThreshold").floatValue;
        SerializedProperty sequence = entry.FindPropertyRelative("sequence");
        string sequenceName = sequence.enumValueIndex >= 0 && sequence.enumValueIndex < sequence.enumDisplayNames.Length
            ? sequence.enumDisplayNames[sequence.enumValueIndex]
            : "?";
        return $"페이즈 {index + 1} — HP {threshold * 100f:0}% 이하 · {sequenceName}";
    }
}
