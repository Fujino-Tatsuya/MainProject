using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.VFX;
using UnityEngine.VFX.Utility;
using UnityEditor;
using System.Linq;
using INab.CommonVFX;


namespace INab.VFXAssets
{
    [ExecuteAlways]
    public abstract class UniformMeshSample : MonoBehaviour
    {
        // todo think on custom automatic scale maybe

        #region Enums and Constants

        /// <summary>
        /// Default folder path to search for trail trailPrefab assets.
        /// </summary>
        public abstract string DefaultPrefabPath { get; }  // "Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Effect FX/Effect Prefabs/"

        /// <summary>
        /// States for effect effect, whether it is on or off.
        /// </summary>
        public enum EffectState { On, Off }

        #endregion

        #region Uniform Mesh Baking Properties

        public Transform meshTransform
        {
            get
            {
                if (meshRenderer != null)
                {
                    if (IsSkinnedMesh)
                    {
                        var skinned = meshRenderer as SkinnedMeshRenderer;
                        // FIXED rotaion issues: return skinned.rootBone.parent.transform;
                        return skinned.transform;
                    }
                    else
                    {
                        return meshRenderer.transform;
                    }
                }

                return null;
            }
        }

        [SerializeField, Tooltip("Renderer of the mesh used for the effect.")]
        public Renderer meshRenderer;

        /// <summary>
        /// Determines if the mesh renderer is a skinned mesh renderer.
        /// </summary>
        public bool IsSkinnedMesh
        {
            get
            {
                return meshRenderer is SkinnedMeshRenderer;
            }
            private set { }
        }

        /// <summary>
        /// Determines if the mesh in the mesh renderer is Readable.
        /// </summary>
        public bool IsMeshReadable
        {
            get
            {
                bool readable;

                if (meshRenderer == null) return true;

                if (IsSkinnedMesh)
                {
                    var renderer = meshRenderer as SkinnedMeshRenderer;
                    readable = renderer.sharedMesh.isReadable;
                }
                else
                {
                    var filter = meshRenderer.GetComponent<MeshFilter>();
                    readable = filter.sharedMesh.isReadable;
                }

                return readable;
            }
            private set { }
        }

        [SerializeField, Tooltip("Instance of the VFX Uniform Mesh Baker.")]
        public UniformMeshBaker meshBaker = new UniformMeshBaker();

        [Range(0.01f, 10f), SerializeField, Tooltip("Muleffectly sample count by this value to control density of the particles. Keep this as low as possible.")]
        public float sampleCountMultiplier = 1f;

        [SerializeField, Tooltip("Whether to use the effect only on one submesh.")]
        public bool usePerSubmeshBaking = false;
        [SerializeField, Tooltip("Submesh index.")]
        public int submeshIndex = 0;

        #endregion

        #region Setup Properties

        /// <summary>
        /// Prefab game object containing the weapon effect visual effect.
        /// </summary>
        [Tooltip("Effect prefab game object.")]
        public GameObject effectPrefab;

        /// <summary>
        /// Returns the name of the assigned effect prefab or "None".
        /// </summary>
        public string PrefabName => effectPrefab ? effectPrefab.name : "None";

#if UNITY_EDITOR
        /// <summary>
        /// Returns the asset path of the assigned prefab in the Unity project. Editor only.
        /// </summary>
        public string PrefabAssetPath => effectPrefab ? AssetDatabase.GetAssetPath(effectPrefab) : "None";
#else
        public string PrefabAssetPath => "None";
#endif

        /// <summary>
        /// Instantiated prefab instance currently active.
        /// </summary>
        [Tooltip("Instantiated effect prefab game object.")]
        public GameObject instantiatedEffectPrefab;

        /// <summary>
        /// Enable debug logging for development and troubleshooting.
        /// </summary>
        [Tooltip("Enable debug mode for detailed logs.")]
        public bool debugMode = false;

        /// <summary>
        /// User-defined effect name for display and identification.
        /// </summary>
        [Tooltip("User defined name for the effect.")]
        public string effectName = "Effect 1"; // TODO


        /// <summary>
        /// Reference to VisualEffect component controlling the effect VFX.
        /// </summary>
        public VisualEffect vfxComponent;

        /// <summary>
        /// Reference to the VFXPropertyBinder managing bindings for VFX parameters.
        /// </summary>
        public VFXPropertyBinder vfxBinder;

        /// <summary>
        /// Current effect state whether On or Off.
        /// </summary>
        public EffectState currentEffectState = EffectState.Off;

        #endregion

        #region Editor Only Properties

        // --------------------------------------------------
        // Inspector Foldouts 
        // --------------------------------------------------

        [HideInInspector] public bool _Foldout_1 = true;
        [HideInInspector] public bool _Foldout_2 = true;
        [HideInInspector] public bool _Foldout_3 = true;
        [HideInInspector] public bool _Foldout_4 = true;
        [HideInInspector] public bool _Foldout_5 = true;
        #endregion

        // ---------------------------------------------------
        // Methods
        // ---------------------------------------------------

        #region Editor Utilities: Prefab Management + Uniform Baking

        //public bool _CheckSelectedPrefab()
        //{
        //    if (effectPrefab == null) return true;
        //    var vfx = effectPrefab.GetComponent<VisualEffect>();
        //    if (vfx == null) return false;
        //
        //
        //    if (vfx.visualEffectAsset != null &&
        //        visualEffectAssetNames.Contains(vfx.visualEffectAsset.name))
        //    {
        //        return true;
        //    }
        //
        //    return false;
        //}

        public bool _SaveAsNewPrefab()
        {
#if UNITY_EDITOR
            // Ask user where to save
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Prefab As",            // Window title
                "Weapon Effect",              // Default file name
                "prefab",                    // Extension
                "Choose where to save the prefab",
                DefaultPrefabPath
            );

            if (string.IsNullOrEmpty(path))
                return false;

            PrefabUtility.UnpackPrefabInstance(instantiatedEffectPrefab, PrefabUnpackMode.Completely, InteractionMode.UserAction);
            effectPrefab = PrefabUtility.SaveAsPrefabAsset(instantiatedEffectPrefab, path, out bool success);

            _InstantiateEffectPrefab();

            return success;
#else
            return false;
#endif
        }

        public void _ApplyPrefabChanges()
        {
#if UNITY_EDITOR
            PrefabUtility.ApplyPrefabInstance(instantiatedEffectPrefab, InteractionMode.UserAction);
#endif
            // AutoPreviewStart();
        }

        public bool _LoadPrefab()
        {
#if UNITY_EDITOR
            string path = EditorUtility.OpenFilePanelWithFilters(
                "Select Prefab",
                DefaultPrefabPath,
                new string[] { "Prefab files", "prefab" }
            );
            if (string.IsNullOrEmpty(path))
                return false;

            string relativePath = "Assets" + path.Substring(Application.dataPath.Length);
            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(relativePath);
            if (loaded == null) return false;

            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Load and Instantiate Effect Prefab");

            Undo.RecordObject(this, "Load Effect Prefab Asset");
            effectPrefab = loaded;
            EditorUtility.SetDirty(this);

            _InstantiateEffectPrefab();

            Undo.CollapseUndoOperations(group);


            return true;
#else
    return false;
#endif
        }

        public bool _InstantiateEffectPrefab(bool autoStartEffect = true)
        {
#if UNITY_EDITOR
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Instantiate Effect Prefab");

            if (instantiatedEffectPrefab != null)
            {
                var toDestroy = instantiatedEffectPrefab;
                Undo.DestroyObjectImmediate(toDestroy);
                Undo.RecordObject(this, "Clear Effect Instance Ref");
                instantiatedEffectPrefab = null;
                EditorUtility.SetDirty(this);
            }

            if (effectPrefab != null)
            {
                var instance = PrefabUtility.InstantiatePrefab(effectPrefab, transform) as GameObject;
                if (instance != null)
                {
                    Undo.RegisterCreatedObjectUndo(instance, "Create Effect Instance");

                    var t = instance.transform;
                    Undo.RecordObject(t, "Setup Effect Transform");
                    t.localPosition = Vector3.zero;
                    t.localRotation = Quaternion.identity;
                    t.localScale = Vector3.one;
                    instance.name = effectPrefab.name + " (Instance) [" + effectName + "]";

                    Undo.RecordObject(this, "Assign Effect Instance & VFX refs");
                    instantiatedEffectPrefab = instance;
                    vfxComponent = instance.GetComponent<VisualEffect>();
                    vfxBinder = instance.GetComponent<VFXPropertyBinder>();
                    EditorUtility.SetDirty(this);

                    ConfigureVFXBinders();
                    if (vfxComponent) EditorUtility.SetDirty(vfxComponent);
                    if (vfxBinder) EditorUtility.SetDirty(vfxBinder);
                }
            }

            Undo.CollapseUndoOperations(group);

#else

            if (instantiatedEffectPrefab != null)
            {
                Destroy(instantiatedEffectPrefab);
                instantiatedEffectPrefab = null;
            }

            if (effectPrefab != null)
            {
                var instance = Instantiate(effectPrefab, transform);
                if (instance != null)
                {
                    var t = instance.transform;
                    t.localPosition = Vector3.zero;
                    t.localRotation = Quaternion.identity;
                    t.localScale = Vector3.one;
                    instance.name = effectPrefab.name + " (Instance) [" + effectName + "]";

                    instantiatedEffectPrefab = instance;
                    vfxComponent = instance.GetComponent<VisualEffect>();
                    vfxBinder = instance.GetComponent<VFXPropertyBinder>();

                    ConfigureVFXBinders();
                }
            }


#endif

            SetupVfxGraph();
            if (autoStartEffect) StartEffect();

            return true;
        }


        /// <summary>
        /// Editor only method to bake the mesh for the VFX Graph.
        /// </summary>
        public void _BakeUniformMesh()
        {
            if (meshRenderer && vfxComponent) meshBaker.Bake(vfxComponent, meshRenderer);
        }

        /// <summary>
        /// Editor only method to set the graphics buffer for the VFX Graph.
        /// </summary>
        public void _SetGraphicsBuffer()
        {
            if (vfxComponent) meshBaker.SetGraphicsBuffer(vfxComponent);
        }

        /// <summary>
        /// Editor only method to find the renderer in the parent.
        /// </summary>
        public void _FindRenderer()
        {
            // FIXED wrong FindRenderer renderers: return skinned.rootBone.parent.transform;
            meshRenderer = GetComponentInChildren<Renderer>();
            if (meshRenderer == null)
            {
                if (gameObject.transform.parent) meshRenderer = gameObject.transform.parent.GetComponentInChildren<Renderer>();
                else meshRenderer = gameObject.GetComponentInParent<Renderer>();
            }

            if (meshRenderer == null)
            {
                Debug.LogWarning("No renderer could be found.");
            }

            if (!(meshRenderer is SkinnedMeshRenderer) && !(meshRenderer is MeshRenderer))
            {
                meshRenderer = null;
                Debug.LogWarning("Found renderer different than SkinnedMeshRenderer or MeshRenderer.");
            }
        }


        #endregion

        #region Visual Effect Graph Utilities

        public void ConfigureVFXBinders()
        {
            if (vfxBinder == null) return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.RecordObject(vfxBinder, "Configure VFX Binders");

                var binders = vfxBinder.GetPropertyBinders<VFXLossyTransformBinder>().ToList();
                while (binders.Count < 1)
                {
                    var b = Undo.AddComponent<VFXLossyTransformBinder>(vfxBinder.gameObject);
                    binders.Add(b);
                }

                var transform = binders[0];

                Undo.RecordObject(transform, "Bind Effect");
                transform.Target = meshTransform;
                transform.Property = "Transform";

                EditorUtility.SetDirty(vfxBinder);
                EditorUtility.SetDirty(transform);
                return;
            }
#endif

            {
                var binders = vfxBinder.GetPropertyBinders<VFXLossyTransformBinder>().ToList();
                while (binders.Count < 1)
                    binders.Add(vfxBinder.AddPropertyBinder<VFXLossyTransformBinder>());

                binders[0].Target = meshTransform;
                binders[0].Property = "Transform";
            }
        }

        public void SetProperty_EffectActive(bool isActive)
        {
            if (vfxComponent && vfxComponent.HasBool("Effect Active"))
                vfxComponent.SetBool("Effect Active", isActive);
        }

        public void SendPlayEvent() => vfxComponent?.Play();
        public void SendStopEvent() => vfxComponent?.Stop();

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            bool isNull = false;

            if (vfxBinder == null) return;
            var list = vfxBinder.GetPropertyBinders<VFXLossyTransformBinder>();
            foreach (var item in list)
            {
                if (item.Target == null) isNull = true;
            }

            if (isNull)
            {
                ConfigureVFXBinders();
            }

            SetupVfxGraph();
        }

        protected virtual void Start()
        {
            SetupVfxGraph();
        }

        protected virtual void Update()
        {
            if (vfxComponent && meshRenderer) meshBaker.Update(vfxComponent, meshRenderer);
            //vfxComponent.Reinit();
        }

        protected virtual void OnDisable()
        {
            meshBaker.OnDisable();
        }

        protected virtual void OnValidate()
        {
            if (enabled == false || gameObject.activeSelf == false) return;

            meshBaker.SampleCountMultiplier = sampleCountMultiplier;
            meshBaker.UsePerSubmeshBaking = usePerSubmeshBaking;
            meshBaker.SubmeshIndex = submeshIndex;

            _SetGraphicsBuffer();

        }

        #endregion

        #region Public API Methods

        /// <summary>Change effect prefab to the given GameObject and instantiate it.</summary>
        public void SetNewEffectPrefab(GameObject newEffectPrefab)
        {
            StopEffect();

            effectPrefab = newEffectPrefab;
            _InstantiateEffectPrefab(false);
        }


        /// <summary>Start the effect effect.</summary>
        public void StartEffect()
        {
            //vfxComponent.SetBool("Effect Active", true);
            /////

            SendPlayEvent();
            SetProperty_EffectActive(true);
            currentEffectState = EffectState.On;
        }

        /// <summary>Stop the effect effect.</summary>
        public void StopEffect()
        {
            //vfxComponent.SetBool("Effect Active", false);
            ////

            SetProperty_EffectActive(false);
            SendStopEvent();
            currentEffectState = EffectState.Off;
        }


        /// <summary>
        /// Bakes uniform mesh buffer, setups propery binder and mesh renderer properties in the vfx grap.
        /// Calls OnValidate().
        /// </summary>
        public virtual void SetupVfxGraph()
        {
            if (meshRenderer)
            {
                MeshSetup.SetupRenderer(meshRenderer, vfxComponent);
                //MeshSetup.SetupPropertyBinder(vfxBinder, meshTransform);
            }

            OnValidate();

            _BakeUniformMesh();

            if (vfxComponent)
            {
                vfxComponent.Reinit();
            }
        }

        #endregion

    }
}