using UnityEngine;
using UnityEditor;
using System;
using INab.Common;
using static INab.Common.EditorUtilties;

namespace INab.VFXAssets
{

    [CustomEditor(typeof(UniformMeshSample))]
    [CanEditMultipleObjects]
    public abstract class UniformMeshSampleEditor : Editor
    {
        #region Properties

        // Prefabs
        private SerializedProperty debugMode;
        private SerializedProperty effectName;
        private SerializedProperty effectPrefab;
        private SerializedProperty instantiatedEffectPrefab;

        // Uniform Mesh Sample
        private SerializedProperty vfxComponent;
        private SerializedProperty vfxBinder;
        private SerializedProperty meshRenderer;
        private SerializedProperty sampleCountMultiplier;

        private SerializedProperty usePerSubmeshBaking;
        private SerializedProperty submeshIndex;


        // Flags to control delayed updates
        public bool waitForUniformBufferSettings = false;

        [NonSerialized]
        private UniformMeshSample ourTarget;
        protected SerializedProperty propertiesScaleRate;

        #endregion

        #region Methods

        public virtual void OnEnable()
        {
            effectName = serializedObject.FindProperty("effectName");
            debugMode = serializedObject.FindProperty("debugMode");
            effectPrefab = serializedObject.FindProperty("effectPrefab");
            instantiatedEffectPrefab = serializedObject.FindProperty("instantiatedEffectPrefab");

            vfxComponent = serializedObject.FindProperty("vfxComponent");
            vfxBinder = serializedObject.FindProperty("vfxBinder");
            meshRenderer = serializedObject.FindProperty("meshRenderer");
            sampleCountMultiplier = serializedObject.FindProperty("sampleCountMultiplier");

            usePerSubmeshBaking = serializedObject.FindProperty("usePerSubmeshBaking");
            submeshIndex = serializedObject.FindProperty("submeshIndex");

            propertiesScaleRate = serializedObject.FindProperty("propertiesScaleRate");


            ourTarget = target as UniformMeshSample;
        }


        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (var foldout = new FoldoutHeaderScope(ref ourTarget._Foldout_2, "Setup"))
            {
                if (foldout.IsExpanded)
                {
                    Setup();
                }
            }

            using (var foldout = new FoldoutHeaderScope(ref ourTarget._Foldout_3, "Effect Prefab"))
            {
                if (foldout.IsExpanded)
                {
                    EffectsLoading();
                }
            }


            using (var foldout = new FoldoutHeaderScope(ref ourTarget._Foldout_4, "Testing"))
            {
                if (foldout.IsExpanded)
                {
                    APITesting();
                }
            }



            //DrawDefaultInspector();

            serializedObject.ApplyModifiedProperties();
        }

        #endregion


        private void Setup()
        {
            using (new LabeledSectionScope("General"))
            {
                EditorGUILayout.PropertyField(debugMode);
                if (debugMode.boolValue)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("m_BakedSampling Length " + ourTarget.meshBaker.m_BakedSampling.Length);
                    EditorGUILayout.LabelField("m_Buffer Count " + ourTarget.meshBaker.m_Buffer.count);
                }

                EditorGUILayout.PropertyField(effectName);

                EditorGUILayout.Space();

                EditorGUILayout.PropertyField(meshRenderer);
                if (!ourTarget.IsMeshReadable)
                {
                    EditorGUILayout.HelpBox("The mesh in the Mesh Renderer is not readable. Enable 'Read/Write' in the Mesh Inspector to fix this.", MessageType.Error);

                }
                if (ourTarget.meshRenderer == null)
                {
                    if (GUILayout.Button("Find Renderer", EditorUtilties.IndentedButtonStyleDouble))
                    {
                        ourTarget._FindRenderer();

                    }
                    EditorGUILayout.HelpBox("Please assign a Mesh Renderer.", MessageType.Error);
                    EditorGUILayout.Space();
                }


                //if (ourTarget.vfxComponent == null)
                //{
                //    if (GUILayout.Button("Setup Vfx Graph Game Object", EditorUtilties.IndentedButtonStyle))
                //    {
                //        ourTarget._SetupVfxGraphGameObject("OnPlay");
                //    }
                //    EditorGUILayout.HelpBox("Please assign Visual Effect Graph component.", MessageType.Error);
                //}


                EditorGUILayout.PropertyField(sampleCountMultiplier);


                Color oldGUIColor = GUI.backgroundColor;
                int sampleCount = ourTarget.meshBaker.SampleCount;

                float colorsIntensity = 2f;

                Gradient gradient = new Gradient();
                GradientColorKey[] colorKeys = new GradientColorKey[2];
                colorKeys[0] = new GradientColorKey(Color.green * colorsIntensity, 0.35f); // Lower count color
                colorKeys[1] = new GradientColorKey(Color.red * colorsIntensity, 1f);   // 20000 count color
                GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
                alphaKeys[0] = new GradientAlphaKey(1f, 0f);
                alphaKeys[1] = new GradientAlphaKey(1f, 1f);
                gradient.SetKeys(colorKeys, alphaKeys);

                float t = Mathf.Clamp01(sampleCount / 20000f);
                Color sampleCountColor = gradient.Evaluate(t);
                GUI.backgroundColor = sampleCountColor;

                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.IntField("Sample Count", sampleCount);
                EditorGUI.EndDisabledGroup();
                GUI.backgroundColor = oldGUIColor;

                if (sampleCount > 20000)
                {
                    EditorGUILayout.HelpBox("Sample count exceeds 20,000. Reduce the Sample Count Multiplier to lower the sample count.", MessageType.Warning);
                }

                EditorGUILayout.PropertyField(usePerSubmeshBaking);

                if (usePerSubmeshBaking.boolValue)
                {
                    var submeshCount = UniformMeshSamplingHelper.RendererToMesh(ourTarget.meshRenderer).subMeshCount;
                    EditorGUILayout.IntSlider(submeshIndex, 0, submeshCount - 1);
                }


                if (ourTarget.sampleCountMultiplier != sampleCountMultiplier.floatValue)
                {
                    waitForUniformBufferSettings = true;
                }

                if (ourTarget.usePerSubmeshBaking != usePerSubmeshBaking.boolValue)
                {
                    waitForUniformBufferSettings = true;
                }

                if (ourTarget.submeshIndex != submeshIndex.intValue)
                {
                    waitForUniformBufferSettings = true;
                }

                if (waitForUniformBufferSettings)
                {
                    if (GUILayout.Button("Bake", EditorUtilties.IndentedButtonStyle))
                    {
                        ourTarget._BakeUniformMesh();
                        waitForUniformBufferSettings = false;
                    }
                }

                if (ourTarget.effectPrefab != null && ourTarget.meshRenderer != null)
                {
                    EditorGUILayout.LabelField("Important", EditorStyles.boldLabel);

                    EditorGUILayout.HelpBox("After configuring the settings, or if something isn't working, click the button.", MessageType.Info);
                    if (GUILayout.Button("Setup Vfx Graph", EditorUtilties.IndentedButtonStyle))
                    {
                        ourTarget.SetupVfxGraph();

                    }
                }
            }
        }

        private void EffectsLoading()
        {

            //using (new LabeledSectionScope("Effect Prefabs"))
            {
                if (ourTarget.effectPrefab == null)
                {
                    EditorGUILayout.HelpBox("Choose effect from effect prefabs: " + ourTarget.DefaultPrefabPath, MessageType.Info);
                }
                EditorGUILayout.PropertyField(effectPrefab);

                GUI.enabled = false;
                EditorGUILayout.PropertyField(instantiatedEffectPrefab, new GUIContent("Prefab Instance"));
                GUI.enabled = true;

                //if (!ourTarget._CheckSelectedPrefab())
                //{
                //    EditorGUILayout.HelpBox("You have wrong prefab selected!", MessageType.Error);
                //    EditorGUILayout.HelpBox("Choose effect from effect prefabs: " + WeaponTipEffect.DefaultPrefabPath, MessageType.Info);
                //}

                float totalWidth = EditorGUIUtility.currentViewWidth - 60;
                float halfWidth = totalWidth / 2f;
                bool loadNew = false;
                bool saveAsNewButton = false;

                bool saveAsNew = true, loadAsNew = true, save = true, load = true;

                if (ourTarget.effectPrefab == null)
                {
                    saveAsNew = true;
                    loadAsNew = true;

                    save = false;
                    load = false;
                }

                if (ourTarget.instantiatedEffectPrefab == null)
                {
                    saveAsNew = false;
                    loadAsNew = true;

                    save = false;
                    load = true;
                }

                if (ourTarget.effectPrefab == null && ourTarget.instantiatedEffectPrefab == null)
                {
                    saveAsNew = false;
                    loadAsNew = true;

                    save = false;
                    load = false;
                }

                using (var scope = new EditorGUILayout.HorizontalScope())
                {
                    GUI.enabled = saveAsNew;
                    saveAsNewButton = GUILayout.Button("Save As New", EditorUtilties.IndentedButtonStyleDouble, GUILayout.Width(halfWidth));

                    GUI.enabled = true;

                    GUI.enabled = save;
                    if (GUILayout.Button("Save (Override Prefab)", EditorUtilties.DefaultButtonStyle, GUILayout.Width(halfWidth)))
                    {
                        Undo.RecordObjects(new UnityEngine.Object[] { ourTarget, ourTarget.effectPrefab }, "Saved Prefab");
                        ourTarget._ApplyPrefabChanges();
                        EditorUtility.SetDirty(ourTarget);
                    }
                    GUI.enabled = true;
                }

                using (var scope = new EditorGUILayout.HorizontalScope())
                {
                    GUI.enabled = loadAsNew;
                    loadNew = GUILayout.Button("Load New", EditorUtilties.IndentedButtonStyleDouble, GUILayout.Width(halfWidth));
                    GUI.enabled = true;

                    GUI.enabled = load;
                    if (GUILayout.Button("Load (Override Instance)", EditorUtilties.DefaultButtonStyle, GUILayout.Width(halfWidth)))
                    {
                        ourTarget._InstantiateEffectPrefab();
                        EditorUtility.SetDirty(ourTarget);
                    }
                    GUI.enabled = true;
                }

                // Helps with weird editor scope error after loading a prefab
                if (loadNew)
                {
                    ourTarget._LoadPrefab();
                    EditorUtility.SetDirty(ourTarget);
                }

                if (saveAsNewButton)
                {
                    ourTarget._SaveAsNewPrefab();
                }
            }

        }

        private void APITesting()
        {
            if (GUILayout.Button("Play Effect", EditorUtilties.MarginButtonStyle))
                ourTarget.StartEffect();

            if (GUILayout.Button("Stop Effect", EditorUtilties.MarginButtonStyle))
                ourTarget.StopEffect();
        }

    }
}