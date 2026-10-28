using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 특성 정의 인스펙터(할 일 125 3/5) — 기본 인스펙터 + '효과 추가' 메뉴. 효과 목록이 <c>[SerializeReference]</c> 라
/// 기본 인스펙터만으로는 칸에 넣을 효과 종류를 고를 수 없다. 종류 목록은 <see cref="TraitEffect"/> 의 구체 하위 클래스 전부.
/// 아래에 데이터 검증 오류(<see cref="TraitDefinition.Validate"/>)를 보여 준다.
/// </summary>
[CustomEditor(typeof(TraitDefinition))]
internal sealed class TraitDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("효과 추가"))
            ShowAddMenu();

        var errors = new List<string>();
        ((TraitDefinition)target).Validate(errors);
        if (errors.Count > 0)
            EditorGUILayout.HelpBox(string.Join("\n", errors), MessageType.Warning);
    }

    private void ShowAddMenu()
    {
        var menu = new GenericMenu();
        foreach (Type type in TypeCache.GetTypesDerivedFrom<TraitEffect>())
        {
            if (type.IsAbstract || type.IsGenericType || type.GetConstructor(Type.EmptyTypes) == null)
                continue;

            Type captured = type;
            menu.AddItem(new GUIContent(type.Name), false, () => AddEffect(captured));
        }

        menu.ShowAsContext();
    }

    private void AddEffect(Type type)
    {
        serializedObject.Update();
        SerializedProperty effects = serializedObject.FindProperty("effects");
        effects.arraySize++;
        effects.GetArrayElementAtIndex(effects.arraySize - 1).managedReferenceValue = Activator.CreateInstance(type);
        serializedObject.ApplyModifiedProperties();
    }
}
