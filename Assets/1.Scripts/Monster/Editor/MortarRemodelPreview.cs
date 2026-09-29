using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Mortar-only mesh binding proof. Never saves a source prefab or changes its rig.</summary>
public static class MortarRemodelPreview
{
    const string Folder = "Generated/monster-remodel-20260926/MortarBot";
    const string PrefabPath = "Assets/2.Prefabs/Monster/MortarBot.prefab";
    const string MeshAssetPath = "Assets/50.Art/Char/Monster/Redesign/MortarBot/MortarBot_DieselPreview.asset";
    const float PositionTolerance = 0.0001f;
    static readonly float[] Samples = { 0f, 0.25f, 0.5f, 0.75f, 0.999f };

    [Serializable] public class Weight { public int groupIndex; public float weight; }
    [Serializable] public class BoneHead { public string name; public Vector3 position; }
    [Serializable] public class InputSubMesh { public int[] indices; }
    [Serializable] public class InputMesh
    {
        public int schemaVersion;
        public string coordinateSpace;
        public string[] groupNames;
        public Vector3[] vertices, normals;
        public Vector2[] uv;
        public Color[] colors;
        public int[] triangles, bonesPerVertex;
        public Weight[] weights;
        public BoneHead[] boneHeads;
        public InputSubMesh[] subMeshes;
    }
    [Serializable] public class Pose
    {
        public string clip, clipAssetPath, screenshot;
        public float time, normalizedTime, sourceRebindMaximumError;
        public Bounds originalBounds, candidateBounds;
        public Vector3 muzzleWorldPosition;
        public bool candidateVerticesFinite;
    }
    [Serializable] public class Report
    {
        public string capturedAtUtc, unityVersion, mode, error, meshAssetPath;
        public string coordinateConversion = "Blender world meters (x,y,z) -> Unity world (-x,z,-y) -> original renderer worldToLocal. Reflection reverses triangle winding. Normals use inverse transpose.";
        public string scope = "Static coordinate/bone binding proof and original clip sampling only. Screenshots render frozen BakeMesh results of each sampled pose; screenshots require visual review. Does not test gameplay controllers, collisions, runtime VFX, networking, or performance.";
        public bool sourceCoordinatesPassed, sourceAnimationsPassed, candidateSamplingCompleted;
        public bool productionPrefabChanged = false, runtimeVfxVerified = false;
        public int originalVertices, sourceVertices, candidateVertices, candidateTriangles, sampledClips, candidateBoundsFrameSamples;
        public float maximumBoneHeadError, maximumSourceRestError, maximumSourceAnimationError;
        public Bounds candidateAnimationLocalBounds;
        public Pose[] poses;
    }

    sealed class Instance
    {
        public GameObject root;
        public Animator animator;
        public SkinnedMeshRenderer skin;
        public Transform[] transforms;
        public Vector3[] positions, scales;
        public Quaternion[] rotations;
        public void ResetPose()
        {
            animator.Rebind();
            for (int i = 0; i < transforms.Length; i++)
            {
                transforms[i].localPosition = positions[i];
                transforms[i].localRotation = rotations[i];
                transforms[i].localScale = scales[i];
            }
        }
    }

    [MenuItem("Tools/Monster Remodel/Validate Mortar Source")]
    public static void ValidateSource() => Run(false);

    [MenuItem("Tools/Monster Remodel/Validate Mortar Candidate")]
    public static void ValidateCandidate() => Run(true);

    static void Run(bool candidateRequested)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Validation requires edit mode after compilation completes.");
        string output = Path.GetFullPath(Folder + "/unity-validation");
        Directory.CreateDirectory(output);
        var report = new Report
        {
            capturedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
            mode = candidateRequested ? "candidate" : "source"
        };
        PreviewRenderUtility preview = null;
        var disposable = new List<Object>();
        var poses = new List<Pose>();
        try
        {
            var sourceData = Read("source-mesh.json");
            var candidateData = candidateRequested ? Read("candidate-mesh.json") : null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) throw new InvalidOperationException("Mortar prefab missing.");
            preview = new PreviewRenderUtility();
            var original = CreateInstance(preview, prefab);
            var roundTrip = CreateInstance(preview, prefab);
            var candidate = candidateRequested ? CreateInstance(preview, prefab) : null;
            Mesh originalMesh = original.skin.sharedMesh;
            report.originalVertices = originalMesh.vertexCount;
            report.sourceVertices = sourceData.vertices.Length;

            report.maximumBoneHeadError = ProveBoneHeads(sourceData, original.skin);
            var sourceMesh = BuildMesh(sourceData, original.skin, "Mortar_SourceRebindProof");
            disposable.Add(sourceMesh); roundTrip.skin.sharedMesh = sourceMesh;
            int[] sourceToOriginal;
            report.maximumSourceRestError = ProveRestVertices(sourceMesh, originalMesh, out sourceToOriginal);
            report.sourceCoordinatesPassed = report.maximumBoneHeadError <= PositionTolerance && report.maximumSourceRestError <= PositionTolerance;
            if (!report.sourceCoordinatesPassed) throw new InvalidOperationException("Source coordinate proof failed. Do not reuse original bindposes.");

            Mesh candidateMesh = null;
            if (candidate != null)
            {
                float candidateBoneError = ProveBoneHeads(candidateData, original.skin);
                if (candidateBoneError > PositionTolerance) throw new InvalidOperationException("Candidate bone heads differ from original rig.");
                candidateMesh = BuildMesh(candidateData, original.skin, "MortarBot_DieselPreview");
                disposable.Add(candidateMesh); candidate.skin.sharedMesh = candidateMesh;
                report.candidateVertices = candidateMesh.vertexCount;
                report.candidateTriangles = candidateMesh.triangles.Length / 3;
                ApplyPreviewMaterial(candidate.skin, disposable);
            }

            var clips = original.animator.runtimeAnimatorController.animationClips.Where(c => c).Distinct().OrderBy(c => c.name).ToArray();
            report.sampledClips = clips.Length;
            if (clips.Length != 6) throw new InvalidOperationException("Expected six existing Mortar clips; inspect changed controller before validating.");
            var bakedOriginal = new Mesh(); var bakedSource = new Mesh(); var bakedCandidate = new Mesh();
            disposable.Add(bakedOriginal); disposable.Add(bakedSource); disposable.Add(bakedCandidate);
            Bounds candidateBounds = default;
            bool haveCandidateBounds = false;
            foreach (var clip in clips)
            {
                foreach (float fraction in Samples)
                {
                    original.root.SetActive(true); roundTrip.root.SetActive(true);
                    original.ResetPose(); roundTrip.ResetPose();
                    if (candidate != null) { candidate.root.SetActive(true); candidate.ResetPose(); }
                    float time = clip.length * fraction;
                    clip.SampleAnimation(original.animator.gameObject, time);
                    clip.SampleAnimation(roundTrip.animator.gameObject, time);
                    if (candidate != null) clip.SampleAnimation(candidate.animator.gameObject, time);
                    original.skin.BakeMesh(bakedOriginal); roundTrip.skin.BakeMesh(bakedSource);
                    float error = MaximumMappedError(bakedSource.vertices, bakedOriginal.vertices, sourceToOriginal);
                    report.maximumSourceAnimationError = Mathf.Max(report.maximumSourceAnimationError, error);
                    var pose = new Pose
                    {
                        clip = clip.name, clipAssetPath = AssetDatabase.GetAssetPath(clip), time = time, normalizedTime = fraction,
                        sourceRebindMaximumError = error, originalBounds = bakedOriginal.bounds,
                        muzzleWorldPosition = original.root.transform.Find("Muzzle").position
                    };
                    if (candidate != null)
                    {
                        candidate.skin.BakeMesh(bakedCandidate);
                        var points = bakedCandidate.vertices;
                        pose.candidateVerticesFinite = points.All(Finite);
                        if (!pose.candidateVerticesFinite) throw new InvalidOperationException("Non-finite candidate vertex in " + clip.name);
                        pose.candidateBounds = bakedCandidate.bounds;
                        if (!haveCandidateBounds) { candidateBounds = bakedCandidate.bounds; haveCandidateBounds = true; }
                        else candidateBounds.Encapsulate(bakedCandidate.bounds);
                        string filename = clip.name + "-" + Mathf.RoundToInt(fraction * 1000).ToString("0000") + ".png";
                        roundTrip.root.SetActive(false);
                        CapturePair(preview, original, candidate, bakedOriginal, bakedCandidate, Path.Combine(output, filename));
                        pose.screenshot = filename;
                    }
                    poses.Add(pose);
                }
            }
            report.sourceAnimationsPassed = report.maximumSourceAnimationError <= PositionTolerance;
            if (!report.sourceAnimationsPassed) throw new InvalidOperationException("Source skinning differs after sampling original clips.");
            if (candidate != null)
            {
                // Capture poses are intentionally sparse; bounds must also cover every source frame.
                foreach (var clip in clips)
                {
                    int frames = Mathf.Max(1, Mathf.CeilToInt(clip.length * clip.frameRate));
                    for (int frame = 0; frame <= frames; frame++)
                    {
                        candidate.root.SetActive(true); candidate.ResetPose();
                        clip.SampleAnimation(candidate.animator.gameObject, Mathf.Min(clip.length, frame / clip.frameRate));
                        candidate.skin.BakeMesh(bakedCandidate);
                        candidateBounds.Encapsulate(bakedCandidate.bounds);
                        report.candidateBoundsFrameSamples++;
                    }
                }
                candidateBounds.Expand(0.02f);
                report.candidateAnimationLocalBounds = candidateBounds;
                candidateMesh.bounds = candidateBounds;
                SaveNewMesh(candidateMesh);
                report.meshAssetPath = MeshAssetPath;
                report.candidateSamplingCompleted = true;
            }
            Debug.Log("[MortarRemodelPreview] " + report.mode + " coordinate and source animation proof passed. Runtime VFX remains unverified.");
        }
        catch (Exception exception)
        {
            report.error = exception.ToString();
            Debug.LogError("[MortarRemodelPreview] " + exception);
        }
        finally
        {
            report.poses = poses.ToArray();
            File.WriteAllText(Path.Combine(output, candidateRequested ? "candidate-report.json" : "source-report.json"), JsonUtility.ToJson(report, true));
            if (preview != null) preview.Cleanup();
            foreach (var value in disposable) if (value && !EditorUtility.IsPersistent(value)) Object.DestroyImmediate(value);
        }
    }

    static InputMesh Read(string filename)
    {
        var data = JsonUtility.FromJson<InputMesh>(File.ReadAllText(Path.Combine(Folder, filename)));
        if (data == null || data.schemaVersion != 1 || data.coordinateSpace != "BLENDER_WORLD_METERS")
            throw new InvalidOperationException("Unsupported input schema: " + filename);
        int count = data.vertices?.Length ?? 0;
        if (count == 0 || data.normals?.Length != count || data.uv?.Length != count || data.bonesPerVertex?.Length != count)
            throw new InvalidOperationException("Vertex, normal, UV, and weight count arrays must align: " + filename);
        if (data.triangles == null || data.triangles.Length % 3 != 0 || data.triangles.Any(i => i < 0 || i >= count))
            throw new InvalidOperationException("Invalid triangles: " + filename);
        if (data.groupNames == null || data.groupNames.Distinct().Count() != data.groupNames.Length || data.boneHeads == null)
            throw new InvalidOperationException("Missing or ambiguous bone names: " + filename);
        if (data.weights == null || data.bonesPerVertex.Sum() != data.weights.Length || data.bonesPerVertex.Any(n => n < 1 || n > 4))
            throw new InvalidOperationException("Weights must have 1-4 influences per vertex: " + filename);
        if (data.vertices.Any(v => !Finite(v)) || data.normals.Any(v => !Finite(v)))
            throw new InvalidOperationException("Non-finite input geometry: " + filename);
        return data;
    }

    static Instance CreateInstance(PreviewRenderUtility preview, GameObject prefab)
    {
        var root = preview.InstantiatePrefabInScene(prefab);
        foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) if (behaviour) behaviour.enabled = false;
        foreach (var particle in root.GetComponentsInChildren<ParticleSystem>(true)) particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var animators = root.GetComponentsInChildren<Animator>(true);
        foreach (var animator in animators) animator.enabled = false;
        var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (animators.Length != 1 || skins.Length != 1) throw new InvalidOperationException("Mortar renderer/Animator layout changed.");
        skins[0].updateWhenOffscreen = true;
        skins[0].localBounds = new Bounds(Vector3.zero, Vector3.one * 20);
        var transforms = root.GetComponentsInChildren<Transform>(true);
        return new Instance
        {
            root = root, animator = animators[0], skin = skins[0], transforms = transforms,
            positions = transforms.Select(t => t.localPosition).ToArray(), rotations = transforms.Select(t => t.localRotation).ToArray(), scales = transforms.Select(t => t.localScale).ToArray()
        };
    }

    static Matrix4x4 Conversion(SkinnedMeshRenderer destination)
    {
        var axis = Matrix4x4.zero;
        axis.m00 = -1; axis.m12 = 1; axis.m21 = -1; axis.m33 = 1;
        return destination.transform.worldToLocalMatrix * axis;
    }

    static float ProveBoneHeads(InputMesh data, SkinnedMeshRenderer destination)
    {
        float maximum = 0;
        var axis = Conversion(destination);
        foreach (var bone in destination.bones)
        {
            var matching = data.boneHeads.Where(b => b.name == bone.name).ToArray();
            if (matching.Length != 1) throw new InvalidOperationException("Missing or duplicate bone head: " + bone.name);
            Vector3 expected = destination.transform.InverseTransformPoint(bone.position);
            maximum = Mathf.Max(maximum, Vector3.Distance(axis.MultiplyPoint3x4(matching[0].position), expected));
        }
        return maximum;
    }

    internal static Mesh BuildMesh(InputMesh data, SkinnedMeshRenderer destination, string name, Matrix4x4? explicitConversion = null)
    {
        var boneLookup = destination.bones.Select((bone, index) => new { bone.name, index }).ToDictionary(p => p.name, p => p.index);
        var matrix = explicitConversion ?? Conversion(destination);
        var normalMatrix = matrix.inverse.transpose;
        var mesh = new Mesh { name = name, indexFormat = data.vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.vertices = data.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
        mesh.normals = data.normals.Select(v => normalMatrix.MultiplyVector(v).normalized).ToArray();
        mesh.uv = data.uv;
        mesh.colors = data.colors != null && data.colors.Length == data.vertices.Length ? data.colors : Enumerable.Repeat(Color.white, data.vertices.Length).ToArray();
        var triangles = (int[])data.triangles.Clone();
        if (matrix.determinant < 0) for (int i = 0; i < triangles.Length; i += 3) { int t = triangles[i + 1]; triangles[i + 1] = triangles[i + 2]; triangles[i + 2] = t; }
        mesh.SetTriangles(triangles, 0);
        if (data.subMeshes != null && data.subMeshes.Length > 0)
        {
            mesh.subMeshCount = data.subMeshes.Length;
            for (int submesh = 0; submesh < data.subMeshes.Length; submesh++)
            {
                var indices = (int[])data.subMeshes[submesh].indices.Clone();
                if (indices.Length % 3 != 0 || indices.Any(index => index < 0 || index >= data.vertices.Length))
                    throw new InvalidOperationException("Invalid explicit submesh indices.");
                if (matrix.determinant < 0) for (int i = 0; i < indices.Length; i += 3) { int t = indices[i + 1]; indices[i + 1] = indices[i + 2]; indices[i + 2] = t; }
                mesh.SetTriangles(indices, submesh);
            }
        }
        mesh.bindposes = destination.sharedMesh.bindposes;
        var counts = new NativeArray<byte>(data.bonesPerVertex.Length, Allocator.Temp);
        var weights = new NativeArray<BoneWeight1>(data.weights.Length, Allocator.Temp);
        try
        {
            int offset = 0;
            for (int vertex = 0; vertex < data.bonesPerVertex.Length; vertex++)
            {
                int count = data.bonesPerVertex[vertex]; counts[vertex] = (byte)count;
                float sum = 0;
                var ordered = data.weights.Skip(offset).Take(count).OrderByDescending(w => w.weight).ToArray();
                for (int influence = 0; influence < count; influence++)
                {
                    var weight = ordered[influence];
                    if (weight.groupIndex < 0 || weight.groupIndex >= data.groupNames.Length || !boneLookup.TryGetValue(data.groupNames[weight.groupIndex], out int boneIndex))
                        throw new InvalidOperationException("An input vertex refers to a missing original deform bone.");
                    if (float.IsNaN(weight.weight) || float.IsInfinity(weight.weight) || weight.weight <= 0) throw new InvalidOperationException("Invalid vertex weight.");
                    sum += weight.weight;
                    weights[offset + influence] = new BoneWeight1 { boneIndex = boneIndex, weight = weight.weight };
                }
                if (Mathf.Abs(sum - 1) > 0.0001f) throw new InvalidOperationException("Vertex weights are not normalized.");
                offset += count;
            }
            mesh.SetBoneWeights(counts, weights);
        }
        catch { Object.DestroyImmediate(mesh); throw; }
        finally { counts.Dispose(); weights.Dispose(); }
        mesh.RecalculateBounds(); mesh.RecalculateTangents();
        return mesh;
    }

    internal static float ProveRestVertices(Mesh candidate, Mesh original, out int[] mapping)
    {
        var incoming = candidate.vertices; var reference = original.vertices;
        mapping = new int[incoming.Length];
        float maximumSquared = 0;
        for (int i = 0; i < incoming.Length; i++)
        {
            float nearest = float.PositiveInfinity; int closest = -1;
            for (int j = 0; j < reference.Length; j++)
            {
                float distance = (incoming[i] - reference[j]).sqrMagnitude;
                if (distance < nearest) { nearest = distance; closest = j; }
            }
            mapping[i] = closest; maximumSquared = Mathf.Max(maximumSquared, nearest);
        }
        // Check the reverse direction as well; a partial source mesh cannot pass.
        foreach (var point in reference)
        {
            float nearest = float.PositiveInfinity;
            foreach (var other in incoming) nearest = Mathf.Min(nearest, (point - other).sqrMagnitude);
            maximumSquared = Mathf.Max(maximumSquared, nearest);
        }
        return Mathf.Sqrt(maximumSquared);
    }

    internal static float MaximumMappedError(Vector3[] actual, Vector3[] expected, int[] mapping)
    {
        float maximumSquared = 0;
        for (int i = 0; i < actual.Length; i++) maximumSquared = Mathf.Max(maximumSquared, (actual[i] - expected[mapping[i]]).sqrMagnitude);
        return Mathf.Sqrt(maximumSquared);
    }

    static bool Finite(Vector3 value) => !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z));

    internal static void ApplyPreviewMaterial(SkinnedMeshRenderer renderer, List<Object> disposable, string textureFolder = null, bool preserveExtraSlots = false, string deliveredMaterialPath = null)
    {
        var materials = renderer.sharedMaterials;
        if (materials.Length == 0 || (!preserveExtraSlots && materials.Length != 1)) throw new InvalidOperationException("Original material slot layout changed.");
        var delivered = AssetDatabase.LoadAssetAtPath<Material>(deliveredMaterialPath ?? "Assets/50.Art/Char/Monster/Redesign/MortarBot/MortarBot_Diesel.mat");
        var material = new Material(delivered ? delivered : materials[0]) { name = "Diesel_PreviewOnly" }; disposable.Add(material);
        if (delivered) { materials[0] = material; renderer.sharedMaterials = materials; return; }
        string basePath = Path.Combine(textureFolder ?? Path.Combine(Folder, "textures"), "BaseColor.png");
        if (File.Exists(basePath))
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, false); disposable.Add(texture);
            if (!texture.LoadImage(File.ReadAllBytes(basePath))) throw new InvalidOperationException("BaseColor preview image could not be decoded.");
            material.SetTexture("_BaseMap", texture); material.SetColor("_BaseColor", Color.white);
            material.SetTextureScale("_BaseMap", Vector2.one); material.SetTextureOffset("_BaseMap", Vector2.zero);
        }
        string metalPath = Path.Combine(textureFolder ?? Path.Combine(Folder, "textures"), "MetallicSmoothness.png");
        if (File.Exists(metalPath))
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, true); disposable.Add(texture);
            if (!texture.LoadImage(File.ReadAllBytes(metalPath))) throw new InvalidOperationException("Metallic preview image could not be decoded.");
            material.SetTexture("_MetallicGlossMap", texture); material.SetFloat("_Metallic", 1); material.SetFloat("_Smoothness", 1);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        // Normal maps require Unity's normal-map importer; this transient preview uses base color and metallic/smoothness only.
        materials[0] = material;
        renderer.sharedMaterials = materials;
    }

    static void CapturePair(PreviewRenderUtility preview, Instance original, Instance candidate, Mesh originalPose, Mesh candidatePose, string file)
    {
        var originalStatic = FrozenRenderer(original.skin, originalPose);
        var candidateStatic = FrozenRenderer(candidate.skin, candidatePose);
        bool originalEnabled = original.skin.enabled, candidateEnabled = candidate.skin.enabled;
        original.skin.enabled = false; candidate.skin.enabled = false;
        original.root.transform.position = new Vector3(-0.85f, 0, 0);
        candidate.root.transform.position = new Vector3(0.85f, 0, 0);
        Bounds originalLocal = originalPose.bounds, candidateLocal = candidatePose.bounds;
        var bounds = new Bounds(original.skin.transform.TransformPoint(originalLocal.center), originalLocal.size);
        bounds.Encapsulate(new Bounds(candidate.skin.transform.TransformPoint(candidateLocal.center), candidateLocal.size));
        preview.camera.clearFlags = CameraClearFlags.SolidColor;
        preview.camera.backgroundColor = new Color(0.15f, 0.17f, 0.19f);
        preview.camera.orthographic = true; preview.camera.allowHDR = false; preview.camera.allowMSAA = true;
        preview.camera.nearClipPlane = 0.01f; preview.camera.farClipPlane = 100;
        preview.ambientColor = new Color(0.55f, 0.55f, 0.55f);
        preview.lights[0].intensity = 1.25f; preview.lights[0].transform.rotation = Quaternion.Euler(35, 135, 0);
        preview.lights[1].intensity = 0.8f; preview.lights[1].transform.rotation = Quaternion.Euler(25, -45, 0);
        preview.camera.transform.position = bounds.center + new Vector3(0.35f, 0.22f, 1).normalized * 10;
        preview.camera.transform.LookAt(bounds.center);
        preview.camera.aspect = 1.6f;
        preview.camera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x / 1.6f) * 1.4f;
        Texture2D image = null;
        try
        {
            preview.BeginStaticPreview(new Rect(0, 0, 1280, 800));
            preview.Render(true); preview.Render(true);
            image = preview.EndStaticPreview();
            File.WriteAllBytes(file, image.EncodeToPNG());
        }
        finally
        {
            if (image) Object.DestroyImmediate(image);
            Object.DestroyImmediate(originalStatic); Object.DestroyImmediate(candidateStatic);
            original.skin.enabled = originalEnabled; candidate.skin.enabled = candidateEnabled;
            original.root.transform.position = Vector3.zero; candidate.root.transform.position = Vector3.zero;
        }
    }

    internal static GameObject FrozenRenderer(SkinnedMeshRenderer source, Mesh pose)
    {
        var temporary = new GameObject("Mortar_FrozenPosePreview");
        temporary.transform.SetParent(source.transform, false);
        temporary.AddComponent<MeshFilter>().sharedMesh = pose;
        var renderer = temporary.AddComponent<MeshRenderer>(); renderer.sharedMaterials = source.sharedMaterials;
        var block = new MaterialPropertyBlock(); source.GetPropertyBlock(block); renderer.SetPropertyBlock(block);
        renderer.shadowCastingMode = source.shadowCastingMode; renderer.receiveShadows = source.receiveShadows;
        return temporary;
    }

    static void SaveNewMesh(Mesh mesh)
    {
        string directory = Path.GetDirectoryName(MeshAssetPath).Replace('\\', '/');
        string current = "Assets";
        foreach (string segment in directory.Split('/').Skip(1))
        {
            string next = current + "/" + segment;
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segment);
            current = next;
        }
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath);
        if (existing) { EditorUtility.CopySerialized(mesh, existing); EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing); }
        else { var saved = Object.Instantiate(mesh); saved.name = mesh.name; AssetDatabase.CreateAsset(saved, MeshAssetPath); }
    }
}
