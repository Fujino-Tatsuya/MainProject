using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="AttackShapeSet"/> 인스펙터(할 일 153) — 기본 칸 + '도형 추가' 메뉴. 도형 목록이 <c>[SerializeReference]</c> 라
/// 기본 인스펙터만으로는 칸에 넣을 도형 종류를 고를 수 없다(<see cref="TraitDefinitionEditor"/> 의 '효과 추가' 와 같은 이유).
/// </summary>
[CustomPropertyDrawer(typeof(AttackShapeSet))]
internal sealed class AttackShapeSetDrawer : PropertyDrawer
{
    private const float ButtonGap = 2f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUI.GetPropertyHeight(property, label, true);
        return property.isExpanded ? height + EditorGUIUtility.singleLineHeight + ButtonGap : height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        float fieldHeight = EditorGUI.GetPropertyHeight(property, label, true);
        EditorGUI.PropertyField(new Rect(position.x, position.y, position.width, fieldHeight), property, label, true);
        if (!property.isExpanded)
            return;

        Rect button = new Rect(position.x + EditorGUI.indentLevel * 15f + 15f, position.y + fieldHeight + ButtonGap,
            Mathf.Min(120f, position.width), EditorGUIUtility.singleLineHeight);
        if (GUI.Button(button, "도형 추가"))
            ShowAddMenu(property.serializedObject, property.propertyPath);
    }

    private static void ShowAddMenu(SerializedObject serializedObject, string setPath)
    {
        var menu = new GenericMenu();
        foreach (Type type in TypeCache.GetTypesDerivedFrom<AttackShape>())
        {
            if (type.IsAbstract || type.IsGenericType || type.GetConstructor(Type.EmptyTypes) == null)
                continue;

            Type captured = type;
            menu.AddItem(new GUIContent(type.Name), false, () => AddShape(serializedObject, setPath, captured));
        }

        menu.ShowAsContext();
    }

    private static void AddShape(SerializedObject serializedObject, string setPath, Type type)
    {
        serializedObject.Update();
        SerializedProperty shapes = serializedObject.FindProperty(setPath + ".shapes");
        if (shapes == null)
            return;

        shapes.arraySize++;
        shapes.GetArrayElementAtIndex(shapes.arraySize - 1).managedReferenceValue = Activator.CreateInstance(type);
        serializedObject.ApplyModifiedProperties();
    }
}
