using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Read-only source evidence for the monster mesh replacement workflow.</summary>
public static class MonsterRemodelAudit
{
    const string OutputDirectory = "Generated/monster-remodel-20260926/baseline";
    static readonly string[] Names =
    {
        "MortarBot", "HumanoidBot", "PeekABot", "TeslaBot", "ChompBot",
        "GauntletBot", "WallBot", "SpinnerBot"
    };

    [Serializable] public class ObjectRef
    {
        public string name, type, assetPath, guid, localFileId, prefabPath, siblingPath;
        public bool isNull;
    }
    [Serializable] public class TransformRecord
    {
        public string path, siblingPath, parentPath;
        public bool activeSelf;
        public Vector3 localPosition, localScale;
        public Quaternion localRotation;
        public Matrix4x4 localToWorldMatrix, worldToLocalMatrix;
    }
    [Serializable] public class BoneRecord
    {
        public int index;
        public ObjectRef reference;
        public string animatorRelativePath;
        public TransformRecord transform;
    }
    [Serializable] public class WeightRecord { public int boneIndex; public float weight; }
    [Serializable] public class UVRecord { public int channel; public Vector4[] values; }
    [Serializable] public class SubMeshRecord
    {
        public int index, baseVertex;
        public string topology;
        public int[] indices;
    }
    [Serializable] public class MeshRecord
    {
        public ObjectRef reference;
        public int vertexCount, subMeshCount, blendShapeCount;
        public bool isReadable;
        public string indexFormat, error;
        public Bounds bounds;
        public Vector3[] vertices, normals;
        public Vector4[] tangents;
        public Color[] colors;
        public UVRecord[] uvs;
        public SubMeshRecord[] subMeshes;
        public int[] bonesPerVertex;
        public WeightRecord[] weights;
        public Matrix4x4[] bindposes;
    }
    [Serializable] public class MaterialRecord
    {
        public ObjectRef reference, shader, mainTexture;
        public string mainTextureProperty;
        public int renderQueue;
        public string[] keywords;
        public bool hasBaseColor, hasColor;
        public Color baseColor, color;
        public Vector2 mainTextureScale, mainTextureOffset;
    }
    [Serializable] public class RendererRecord
    {
        public string path, siblingPath, type, animatorPath;
        public bool enabled, activeInHierarchy, updateWhenOffscreen, receiveShadows;
        public string quality, shadowCastingMode;
        public uint renderingLayerMask;
        public TransformRecord transform;
        public Bounds bounds, localBounds;
        public MeshRecord mesh;
        public MaterialRecord[] materials;
        public BoneRecord[] bones;
        public ObjectRef rootBone;
        public float[] blendShapeWeights;
    }
    [Serializable] public class EventRecord
    {
        public float time, floatParameter;
        public string functionName, stringParameter;
        public int intParameter;
        public ObjectRef objectReferenceParameter;
    }
    [Serializable] public class BindingRecord { public string path, propertyName, type; }
    [Serializable] public class ClipRecord
    {
        public ObjectRef reference;
        public float length, frameRate;
        public bool legacy, isLooping;
        public EventRecord[] events;
        public BindingRecord[] floatCurveBindings, objectCurveBindings;
    }
    [Serializable] public class AnimatorRecord
    {
        public string path;
        public bool enabled, applyRootMotion;
        public string cullingMode, updateMode;
        public ObjectRef avatar, controller;
        public ClipRecord[] clips;
    }
    [Serializable] public class PropertyRecord
    {
        public string path, type, value;
        public ObjectRef objectReference;
    }
    [Serializable] public class WiringRecord
    {
        public string path, type;
        public PropertyRecord[] properties;
    }
    [Serializable] public class Baseline
    {
        public int schemaVersion = 1;
        public string monster, prefabPath, capturedAtUtc, unityVersion;
        public string coordinateNote = "Geometry is in the ORIGINAL renderer mesh local space. Matrices are from the loaded prefab saved pose, not a guarantee of animation bind pose. Bindposes preserve the original mesh data. weights is flattened; bonesPerVertex gives each vertex's consecutive weight count. Submesh indices include baseVertex. UV values use Vector4 for all channels.";
        public bool sourceAssetsSaved = false, success;
        public TransformRecord[] transforms;
        public RendererRecord[] renderers;
        public AnimatorRecord[] animators;
        public WiringRecord[] wiring;
        public string[] errors;
    }
    [Serializable] public class ExportEntry { public string monster, file, error; public bool success; }
    [Serializable] public class ExportSummary
    {
        public string capturedAtUtc, unityVersion;
        public bool success, sourceAssetsSaved = false;
        public ExportEntry[] entries;
    }

    [MenuItem("Tools/Monster Remodel/Export Baseline")]
    public static void ExportBaseline()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Export requires edit mode after compilation completes.");

        var directory = Path.GetFullPath(OutputDirectory);
        Directory.CreateDirectory(directory);
        var entries = new List<ExportEntry>();
        foreach (string monster in Names)
        {
            var entry = new ExportEntry { monster = monster, file = monster + ".json" };
            GameObject root = null;
            try
            {
                string prefabPath = "Assets/2.Prefabs/Monster/" + monster + ".prefab";
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (!root) throw new InvalidOperationException("Missing prefab: " + prefabPath);
                var errors = new List<string>();
                var baseline = new Baseline
                {
                    monster = monster, prefabPath = prefabPath,
                    capturedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                    transforms = root.GetComponentsInChildren<Transform>(true).Select(t => TransformData(t, root.transform)).ToArray(),
                    renderers = root.GetComponentsInChildren<Renderer>(true)
                        .Where(r => r is SkinnedMeshRenderer || r is MeshRenderer)
                        .Select(r => RendererData(r, root.transform, errors)).ToArray(),
                    animators = root.GetComponentsInChildren<Animator>(true).Select(a => AnimatorData(a, root.transform)).ToArray(),
                    wiring = Wiring(root), errors = errors.ToArray(), success = errors.Count == 0
                };
                File.WriteAllText(Path.Combine(directory, entry.file), JsonUtility.ToJson(baseline, true));
                entry.success = baseline.success;
                entry.error = string.Join("\n", errors);
            }
            catch (Exception exception)
            {
                entry.error = exception.ToString();
                Debug.LogError("[MonsterRemodelAudit] " + monster + "\n" + exception);
            }
            finally
            {
                if (root) PrefabUtility.UnloadPrefabContents(root);
            }
            entries.Add(entry);
        }
        var summary = new ExportSummary
        {
            capturedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
            success = entries.All(e => e.success), entries = entries.ToArray()
        };
        File.WriteAllText(Path.Combine(directory, "summary.json"), JsonUtility.ToJson(summary, true));
        Debug.Log("[MonsterRemodelAudit] Baseline " + (summary.success ? "PASS" : "has errors") + ": " + directory);
    }

    static string Relative(Transform transform, Transform root) =>
        transform ? AnimationUtility.CalculateTransformPath(transform, root) : null;

    static string SiblingPath(Transform transform, Transform root)
    {
        if (!transform) return null;
        var pieces = new List<int>();
        for (var current = transform; current && current != root; current = current.parent)
            pieces.Add(current.GetSiblingIndex());
        pieces.Reverse();
        return string.Join("/", pieces);
    }

    static ObjectRef Reference(Object value, Transform root)
    {
        if (!value) return new ObjectRef { isNull = true };
        var result = new ObjectRef { name = value.name, type = value.GetType().FullName, assetPath = AssetDatabase.GetAssetPath(value) };
        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long localId))
        { result.guid = guid; result.localFileId = localId.ToString(); }
        Transform transform = value is GameObject go ? go.transform : (value as Component)?.transform;
        if (transform && (transform == root || transform.IsChildOf(root)))
        { result.prefabPath = Relative(transform, root); result.siblingPath = SiblingPath(transform, root); }
        return result;
    }

    static TransformRecord TransformData(Transform transform, Transform root) => new TransformRecord
    {
        path = Relative(transform, root), siblingPath = SiblingPath(transform, root),
        parentPath = transform == root ? null : Relative(transform.parent, root), activeSelf = transform.gameObject.activeSelf,
        localPosition = transform.localPosition, localRotation = transform.localRotation, localScale = transform.localScale,
        localToWorldMatrix = transform.localToWorldMatrix, worldToLocalMatrix = transform.worldToLocalMatrix
    };

    static RendererRecord RendererData(Renderer renderer, Transform root, List<string> errors)
    {
        var skin = renderer as SkinnedMeshRenderer;
        var animator = renderer.GetComponentInParent<Animator>(true);
        Mesh mesh = skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
        var result = new RendererRecord
        {
            path = Relative(renderer.transform, root), siblingPath = SiblingPath(renderer.transform, root), type = renderer.GetType().FullName,
            enabled = renderer.enabled, activeInHierarchy = renderer.gameObject.activeInHierarchy,
            animatorPath = animator ? Relative(animator.transform, root) : null,
            transform = TransformData(renderer.transform, root), bounds = renderer.bounds, localBounds = renderer.localBounds,
            receiveShadows = renderer.receiveShadows, shadowCastingMode = renderer.shadowCastingMode.ToString(), renderingLayerMask = renderer.renderingLayerMask,
            materials = renderer.sharedMaterials.Select(m => MaterialData(m, root)).ToArray(), mesh = MeshData(mesh, root)
        };
        if (!string.IsNullOrEmpty(result.mesh.error)) errors.Add(result.path + ": " + result.mesh.error);
        if (skin)
        {
            result.updateWhenOffscreen = skin.updateWhenOffscreen; result.quality = skin.quality.ToString();
            result.rootBone = Reference(skin.rootBone, root);
            result.bones = skin.bones.Select((bone, index) => new BoneRecord
            {
                index = index, reference = Reference(bone, root),
                animatorRelativePath = bone && animator && (bone == animator.transform || bone.IsChildOf(animator.transform)) ? Relative(bone, animator.transform) : null,
                transform = bone ? TransformData(bone, root) : null
            }).ToArray();
            result.blendShapeWeights = mesh ? Enumerable.Range(0, mesh.blendShapeCount).Select(skin.GetBlendShapeWeight).ToArray() : new float[0];
        }
        return result;
    }

    static MeshRecord MeshData(Mesh mesh, Transform root)
    {
        var result = new MeshRecord { reference = Reference(mesh, root) };
        if (!mesh) { result.error = "Renderer has no mesh."; return result; }
        result.vertexCount = mesh.vertexCount; result.subMeshCount = mesh.subMeshCount;
        result.blendShapeCount = mesh.blendShapeCount; result.isReadable = mesh.isReadable;
        result.indexFormat = mesh.indexFormat.ToString(); result.bounds = mesh.bounds;
        try
        {
            result.vertices = mesh.vertices; result.normals = mesh.normals; result.tangents = mesh.tangents;
            result.colors = mesh.colors; result.bindposes = mesh.bindposes;
            result.uvs = Enumerable.Range(0, 8).Select(channel =>
            {
                var values = new List<Vector4>(); mesh.GetUVs(channel, values);
                return new UVRecord { channel = channel, values = values.ToArray() };
            }).ToArray();
            result.subMeshes = Enumerable.Range(0, mesh.subMeshCount).Select(index => new SubMeshRecord
            {
                index = index, baseVertex = (int)mesh.GetBaseVertex(index), topology = mesh.GetTopology(index).ToString(), indices = mesh.GetIndices(index, true)
            }).ToArray();
            // These NativeArrays view data owned by Mesh. Do not dispose them.
            var counts = mesh.GetBonesPerVertex(); var weights = mesh.GetAllBoneWeights();
            result.bonesPerVertex = new int[counts.Length];
            for (int i = 0; i < counts.Length; i++) result.bonesPerVertex[i] = counts[i];
            result.weights = new WeightRecord[weights.Length];
            for (int i = 0; i < weights.Length; i++) result.weights[i] = new WeightRecord { boneIndex = weights[i].boneIndex, weight = weights[i].weight };
            if (result.vertices.Length != mesh.vertexCount) throw new InvalidOperationException("Vertex data was not readable in this Editor context.");
        }
        catch (Exception exception) { result.error = exception.ToString(); }
        return result;
    }

    static MaterialRecord MaterialData(Material material, Transform root)
    {
        var result = new MaterialRecord { reference = Reference(material, root) };
        if (!material) return result;
        result.shader = Reference(material.shader, root);
        var textureProperties = material.GetTexturePropertyNames();
        result.mainTextureProperty = new[] { "_BaseMap", "_MainTex", "_MainTexture" }.FirstOrDefault(textureProperties.Contains)
            ?? textureProperties.FirstOrDefault();
        if (!string.IsNullOrEmpty(result.mainTextureProperty))
        {
            result.mainTexture = Reference(material.GetTexture(result.mainTextureProperty), root);
            result.mainTextureScale = material.GetTextureScale(result.mainTextureProperty);
            result.mainTextureOffset = material.GetTextureOffset(result.mainTextureProperty);
        }
        result.renderQueue = material.renderQueue; result.keywords = material.shaderKeywords;
        result.hasBaseColor = material.HasProperty("_BaseColor"); result.hasColor = material.HasProperty("_Color");
        if (result.hasBaseColor) result.baseColor = material.GetColor("_BaseColor");
        if (result.hasColor) result.color = material.GetColor("_Color");
        return result;
    }

    static AnimatorRecord AnimatorData(Animator animator, Transform root) => new AnimatorRecord
    {
        path = Relative(animator.transform, root), enabled = animator.enabled, applyRootMotion = animator.applyRootMotion,
        cullingMode = animator.cullingMode.ToString(), updateMode = animator.updateMode.ToString(),
        avatar = Reference(animator.avatar, root), controller = Reference(animator.runtimeAnimatorController, root),
        clips = animator.runtimeAnimatorController ? animator.runtimeAnimatorController.animationClips.Where(c => c).Distinct().Select(clip => new ClipRecord
        {
            reference = Reference(clip, root), length = clip.length, frameRate = clip.frameRate, legacy = clip.legacy, isLooping = clip.isLooping,
            events = AnimationUtility.GetAnimationEvents(clip).Select(e => new EventRecord
            {
                time = e.time, functionName = e.functionName, stringParameter = e.stringParameter, intParameter = e.intParameter,
                floatParameter = e.floatParameter, objectReferenceParameter = Reference(e.objectReferenceParameter, root)
            }).ToArray(),
            floatCurveBindings = AnimationUtility.GetCurveBindings(clip).Select(BindingData).ToArray(),
            objectCurveBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip).Select(BindingData).ToArray()
        }).ToArray() : new ClipRecord[0]
    };

    static BindingRecord BindingData(EditorCurveBinding binding) => new BindingRecord
    { path = binding.path, propertyName = binding.propertyName, type = binding.type.FullName };

    static WiringRecord[] Wiring(GameObject root)
    {
        var records = new List<WiringRecord>();
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (!component) continue;
            var properties = new List<PropertyRecord>();
            using (var serialized = new SerializedObject(component))
            {
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    var record = new PropertyRecord { path = property.propertyPath, type = property.propertyType.ToString() };
                    switch (property.propertyType)
                    {
                        case SerializedPropertyType.ObjectReference: record.objectReference = Reference(property.objectReferenceValue, root.transform); break;
                        case SerializedPropertyType.String: record.value = property.stringValue; break;
                        case SerializedPropertyType.Integer: record.value = property.longValue.ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                        case SerializedPropertyType.Boolean: record.value = property.boolValue.ToString(); break;
                        case SerializedPropertyType.Float: record.value = property.doubleValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture); break;
                        case SerializedPropertyType.Enum: record.value = property.intValue.ToString(); break;
                        case SerializedPropertyType.Vector2: record.value = JsonUtility.ToJson(property.vector2Value); break;
                        case SerializedPropertyType.Vector3: record.value = JsonUtility.ToJson(property.vector3Value); break;
                        case SerializedPropertyType.Vector4: record.value = JsonUtility.ToJson(property.vector4Value); break;
                        case SerializedPropertyType.Quaternion: record.value = JsonUtility.ToJson(property.quaternionValue); break;
                        case SerializedPropertyType.Color: record.value = JsonUtility.ToJson(property.colorValue); break;
                        default: continue;
                    }
                    properties.Add(record);
                }
            }
            records.Add(new WiringRecord { path = Relative(component.transform, root.transform), type = component.GetType().FullName, properties = properties.ToArray() });
        }
        return records.ToArray();
    }
}
