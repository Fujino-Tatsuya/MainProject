using UnityEditor;
using System;
using INab.Common;

namespace INab.VFXAssets
{
    [CustomEditor(typeof(CharacterEffect))]
    [CanEditMultipleObjects]
    public class CharacterEffectEditor : UniformMeshSampleEditor
    {
        private SerializedProperty onDuration;
        private SerializedProperty offDuration;

        [NonSerialized]
        private CharacterEffect ourTarget;


        public override void OnEnable()
        {
            base.OnEnable();

            onDuration = serializedObject.FindProperty("onDuration");
            offDuration = serializedObject.FindProperty("offDuration");

        }

        public override void OnInspectorGUI()
        {
            ourTarget = target as CharacterEffect;
            base.OnInspectorGUI();
        }

        protected virtual void DrawEffectSettings()
        {
            EditorUtilties.SetFoldoutEffectSettings(ourTarget, EditorGUILayout.BeginFoldoutHeaderGroup(EditorUtilties.FoldoutEffectSettings(ourTarget), "Effect Settings", EditorStyles.foldoutHeader));
            if (EditorUtilties.FoldoutEffectSettings(ourTarget))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(onDuration);
                EditorGUILayout.PropertyField(offDuration);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

    }
}

