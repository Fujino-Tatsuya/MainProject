using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Fail-closed body mesh proof for the other seven existing monster rigs.</summary>
public static class MonsterRemodelBatchPreview
{
    const string BaseFolder = "Generated/monster-remodel-20260926";
    const float ToleranceMeters = 0.0001f;
    static readonly float[] CaptureFractions = { 0, 0.5f, 0.999f };

    [Serializable] public class PoseResult
    {
        public string clip, clipPath, screenshot;
        public float time, sourceErrorMeters;
        public Bounds candidateLocalBounds;
    }
    [Serializable] public class VfxContract
    {
        public string scope = "Serialized wiring and mesh/material contract audit only; no runtime effect playback.";
        public bool hierarchyAndLocalTransformsPreserved, bodyBonePathsPreserved, materialSlotCountPreserved;
        public bool hitFlashBaseColorSupported, existingInterruptSlotPreserved, deathTexturePropertySupported;
        public bool deathParticleSelectsMainBody, particleSubmeshIndexValid;
        public int deathParticleSubmeshIndex;
        public string deathParticleRendererPath, dissolveTemplatePath, particlePrefabPath;
    }
    [Serializable] public class Report
    {
        public string monster, capturedAtUtc, error, bodyPath, meshAssetPath;
        public string scope = "Main body mesh only. Source bone heads fit a positive uniform scale and translation after Blender(x,y,z)->Unity(-x,z,-y). Rotation is never guessed. Existing rig, Avatar, controllers, extra renderers, static weapons and VFX references remain in the preview clone. Images freeze BakeMesh results; full gameplay/NGO/VFX is unverified.";
        public bool coordinatesPassed, sourceAnimationSamplesPassed, candidateSamplesPassed, originalPrefabChanged, runtimeVfxVerified = false;
        public float fittedScale, boneHeadErrorMeters, restErrorMeters, animationErrorMeters;
        public float originalImporterMinimumBoneWeight;
        public int sourceDroppedInfluences, candidateDroppedInfluences;
        public string weightPolicy = "Apply the original FBX importer's minimum bone weight, then renormalize. Blender JSON and source FBX are unchanged.";
        public Vector3 fittedTranslation;
        public Matrix4x4 blenderWorldToRendererLocal;
        public int originalVertices, candidateVertices, candidateTriangles, originalSubmeshes, candidateSubmeshes, materialSlots, preservedOtherRenderers, clips, fullFrameSamples;
        public Bounds candidateFullFrameBounds;
        public string[] controllerPaths, warnings;
        public PoseResult[] poses;
        public VfxContract vfxContract;
        public StaticMeshResult[] staticMeshes;
    }
    [Serializable] public class StaticMeshResult
    {
        public string side, path, meshAssetPath;
        public bool coordinatesPassed, animationSamplesPassed;
        public int candidateVertices, candidateTriangles;
        public float restErrorMeters, animationErrorMeters;
    }
    sealed class StaticProof
    {
        public StaticMeshResult result;
        public MeshFilter original, candidate;
        public Mesh sourceMesh, candidateMesh;
        public int[] mapping;
    }
    sealed class Instance
    {
        public GameObject root;
        public Animator animator;
        public SkinnedMeshRenderer body;
        public Transform[] transforms;
        public Vector3[] positions, scales;
        public Quaternion[] rotations;
        public void Reset()
        {
            animator.Rebind();
            for (int i = 0; i < transforms.Length; i++)
            { transforms[i].localPosition = positions[i]; transforms[i].localRotation = rotations[i]; transforms[i].localScale = scales[i]; }
        }
    }

    [MenuItem("Tools/Monster Remodel/Validate ChompBot")] public static void Chomp() => Run("ChompBot");
    [MenuItem("Tools/Monster Remodel/Validate HumanoidBot")] public static void Humanoid() => Run("HumanoidBot");
    [MenuItem("Tools/Monster Remodel/Validate PeekABot")] public static void Peek() => Run("PeekABot");
    [MenuItem("Tools/Monster Remodel/Validate TeslaBot")] public static void Tesla() => Run("TeslaBot");
    [MenuItem("Tools/Monster Remodel/Validate GauntletBot")] public static void Gauntlet() => Run("GauntletBot");
    [MenuItem("Tools/Monster Remodel/Validate WallBot")] public static void Wall() => Run("WallBot");
    [MenuItem("Tools/Monster Remodel/Validate SpinnerBot")] public static void Spinner() => Run("SpinnerBot");

    static void Run(string monster)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Use edit mode after compilation.");
        string folder = Path.Combine(BaseFolder, monster);
        string output = Path.Combine(folder, "unity-validation"); Directory.CreateDirectory(output);
        string prefabPath = "Assets/2.Prefabs/Monster/" + monster + ".prefab";
        byte[] before = File.ReadAllBytes(prefabPath);
        var report = new Report { monster = monster, capturedAtUtc = DateTime.UtcNow.ToString("O") };
        var poses = new List<PoseResult>(); var warnings = new List<string>(); var owned = new List<Object>();
        var staticProofs = new List<StaticProof>();
        PreviewRenderUtility preview = null;
        try
        {
            var source = Read(Path.Combine(folder, "source-mesh.json"));
            var candidateData = Read(Path.Combine(folder, "candidate-mesh.json"));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (!prefab) throw new InvalidOperationException("Prefab missing.");
            preview = new PreviewRenderUtility();
            var original = Create(preview, prefab);
            var rebound = Create(preview, prefab);
            var candidate = Create(preview, prefab);
            report.bodyPath = AnimationUtility.CalculateTransformPath(original.body.transform, original.root.transform);
            report.originalVertices = original.body.sharedMesh.vertexCount;
            report.originalSubmeshes = original.body.sharedMesh.subMeshCount;
            report.materialSlots = original.body.sharedMaterials.Length;
            report.preservedOtherRenderers = original.root.GetComponentsInChildren<Renderer>(true).Length - 1;
            var originalImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(original.body.sharedMesh)) as ModelImporter;
            report.originalImporterMinimumBoneWeight = originalImporter ? originalImporter.minBoneWeight : 0;
            report.sourceDroppedInfluences = ApplyImporterWeightCutoff(source, report.originalImporterMinimumBoneWeight);
            report.candidateDroppedInfluences = ApplyImporterWeightCutoff(candidateData, report.originalImporterMinimumBoneWeight);

            var matrix = Fit(source, original.body, report);
            report.blenderWorldToRendererLocal = matrix;
            Mesh sourceMesh = MortarRemodelPreview.BuildMesh(source, original.body, monster + "_SourceProof", matrix); owned.Add(sourceMesh);
            rebound.body.sharedMesh = sourceMesh;
            float localToMeters = MaxScale(original.body.transform.lossyScale);
            int[] mapping;
            report.restErrorMeters = MortarRemodelPreview.ProveRestVertices(sourceMesh, original.body.sharedMesh, out mapping) * localToMeters;
            report.coordinatesPassed = report.boneHeadErrorMeters <= ToleranceMeters && report.restErrorMeters <= ToleranceMeters;
            if (!report.coordinatesPassed) throw new InvalidOperationException("Source coordinate proof failed; candidate was not built.");

            var candidateCheck = new Report(); Fit(candidateData, original.body, candidateCheck);
            if (candidateCheck.boneHeadErrorMeters > ToleranceMeters || Mathf.Abs(candidateCheck.fittedScale - report.fittedScale) > 0.00001f || Vector3.Distance(candidateCheck.fittedTranslation, report.fittedTranslation) > ToleranceMeters)
                throw new InvalidOperationException("Candidate armature differs from proven source armature.");
            Mesh mesh = MortarRemodelPreview.BuildMesh(candidateData, original.body, monster + "_DieselPreview", matrix); owned.Add(mesh);
            report.candidateSubmeshes = mesh.subMeshCount;
            if (mesh.subMeshCount != original.body.sharedMesh.subMeshCount)
                throw new InvalidOperationException("Candidate submesh count differs. Export explicit subMeshes before proceeding; material slot structure must be preserved.");
            candidate.body.sharedMesh = mesh;
            report.candidateVertices = mesh.vertexCount; report.candidateTriangles = mesh.triangles.Length / 3;
            MortarRemodelPreview.ApplyPreviewMaterial(candidate.body, owned, Path.Combine(folder, "textures"), true, "Assets/50.Art/Char/Monster/Redesign/" + monster + "/" + monster + "_Diesel.mat");
            if (monster == "GauntletBot")
            {
                BuildStaticProofs(folder, original, candidate, matrix, owned, staticProofs);
                report.preservedOtherRenderers -= staticProofs.Count;
                if (staticProofs.Count > 0) report.scope = "Main body and explicitly proven static gauntlet meshes. Bone heads establish positive uniform scale and translation after Blender(x,y,z)->Unity(-x,z,-y); the same conversion proves original static attachment geometry. Existing rig, Avatar, controllers, Faces, TrainingBot and VFX references remain unchanged. Images freeze BakeMesh results; full gameplay/NGO/VFX is unverified.";
            }
            report.vfxContract = AuditContract(original, candidate);
            HideInterruptForBaseReview(original.root, owned); HideInterruptForBaseReview(rebound.root, owned); HideInterruptForBaseReview(candidate.root, owned);
            if (report.materialSlots > report.originalSubmeshes) warnings.Add("Interrupt overlay retained in its existing slot; hidden only in base-surface preview images. Runtime overlay remains to be tested.");
            if (report.preservedOtherRenderers > 0) warnings.Add(staticProofs.Count > 0 ? "Body and separately proven gauntlets are replaced; Faces, TrainingBot and their original materials remain intact." : "Only the main skinned body is replaced. Other renderers and static weapons remain original.");
            var controllers = Controllers(original, monster).Distinct().ToArray();
            report.controllerPaths = controllers.Select(AssetDatabase.GetAssetPath).ToArray();
            var clips = controllers.SelectMany(c => c.animationClips).Where(c => c).Distinct().OrderBy(c => c.name).ToArray();
            report.clips = clips.Length;
            if (clips.Length == 0) throw new InvalidOperationException("No original clips found.");
            var originalBake = new Mesh(); var sourceBake = new Mesh(); var candidateBake = new Mesh();
            owned.Add(originalBake); owned.Add(sourceBake); owned.Add(candidateBake);
            Bounds union = default; bool hasBounds = false;
            foreach (var clip in clips)
            {
                foreach (float fraction in CaptureFractions)
                {
                    original.root.SetActive(true); rebound.root.SetActive(true); candidate.root.SetActive(true);
                    original.Reset(); rebound.Reset(); candidate.Reset();
                    float time = clip.length * fraction;
                    clip.SampleAnimation(original.animator.gameObject, time); clip.SampleAnimation(rebound.animator.gameObject, time); clip.SampleAnimation(candidate.animator.gameObject, time);
                    original.body.BakeMesh(originalBake); rebound.body.BakeMesh(sourceBake); candidate.body.BakeMesh(candidateBake);
                    float error = MortarRemodelPreview.MaximumMappedError(sourceBake.vertices, originalBake.vertices, mapping) * localToMeters;
                    report.animationErrorMeters = Mathf.Max(report.animationErrorMeters, error);
                    foreach (var proof in staticProofs) SampleStatic(proof);
                    if (candidateBake.vertices.Any(v => !Finite(v))) throw new InvalidOperationException("Non-finite deformed candidate vertex.");
                    if (!hasBounds) { union = candidateBake.bounds; hasBounds = true; } else union.Encapsulate(candidateBake.bounds);
                    rebound.root.SetActive(false);
                    string clipGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clip));
                    string file = Clean(clip.name) + "-" + clipGuid.Substring(0, Mathf.Min(8, clipGuid.Length)) + "-" + Mathf.RoundToInt(fraction * 1000).ToString("0000") + ".png";
                    Capture(preview, original, candidate, originalBake, candidateBake, Path.Combine(output, file));
                    poses.Add(new PoseResult { clip = clip.name, clipPath = AssetDatabase.GetAssetPath(clip), time = time, sourceErrorMeters = error, candidateLocalBounds = candidateBake.bounds, screenshot = file });
                }
                int frames = Mathf.Max(1, Mathf.CeilToInt(clip.length * clip.frameRate));
                for (int frame = 0; frame <= frames; frame++)
                {
                    candidate.root.SetActive(true); candidate.Reset();
                    clip.SampleAnimation(candidate.animator.gameObject, Mathf.Min(clip.length, frame / clip.frameRate));
                    candidate.body.BakeMesh(candidateBake);
                    if (candidateBake.vertices.Any(v => !Finite(v))) throw new InvalidOperationException("Non-finite candidate bounds sample.");
                    union.Encapsulate(candidateBake.bounds); report.fullFrameSamples++;
                }
            }
            report.sourceAnimationSamplesPassed = report.animationErrorMeters <= ToleranceMeters;
            if (!report.sourceAnimationSamplesPassed) throw new InvalidOperationException("Unchanged source failed original clip skinning proof; no candidate asset saved.");
            union.Expand(0.02f / Mathf.Max(localToMeters, 0.00001f)); mesh.bounds = union;
            report.candidateFullFrameBounds = union; report.candidateSamplesPassed = true;
            report.meshAssetPath = Save(mesh, monster);
            foreach (var proof in staticProofs)
            {
                proof.result.animationSamplesPassed = proof.result.animationErrorMeters <= ToleranceMeters;
                if (!proof.result.animationSamplesPassed) throw new InvalidOperationException("Static attachment animation proof failed: " + proof.result.side);
                proof.result.meshAssetPath = Save(proof.candidateMesh, monster, monster + "_Static" + proof.result.side + "_DieselPreview");
            }
            warnings.Add("Skinning proof and sampled poses do not verify runtime Animator layer masks, aiming code, sockets in gameplay, VFX lifecycle, collisions, crowd cost, or MPPM.");
            Debug.Log("[MonsterRemodelBatchPreview] " + monster + " source proof and candidate samples PASS.");
        }
        catch (Exception exception) { report.error = exception.ToString(); Debug.LogError("[MonsterRemodelBatchPreview] " + monster + ": " + exception); }
        finally
        {
            report.originalPrefabChanged = !before.SequenceEqual(File.ReadAllBytes(prefabPath));
            report.poses = poses.ToArray(); report.warnings = warnings.ToArray();
            report.staticMeshes = staticProofs.Select(p => p.result).ToArray();
            File.WriteAllText(Path.Combine(output, "candidate-report.json"), JsonUtility.ToJson(report, true));
            if (preview != null) preview.Cleanup();
            foreach (var value in owned) if (value && !EditorUtility.IsPersistent(value)) Object.DestroyImmediate(value);
        }
    }

    static void BuildStaticProofs(string folder, Instance original, Instance candidate, Matrix4x4 bodyConversion, List<Object> owned, List<StaticProof> proofs)
    {
        Matrix4x4 worldConversion = original.body.transform.localToWorldMatrix * bodyConversion;
        foreach (string side in new[] { "L", "R" })
        {
            string sourcePath = Path.Combine(folder, "static-" + side + "-source-mesh.json");
            string candidatePath = Path.Combine(folder, "static-" + side + "-candidate-mesh.json");
            if (!File.Exists(sourcePath) && !File.Exists(candidatePath)) continue;
            if (!File.Exists(sourcePath) || !File.Exists(candidatePath)) throw new InvalidOperationException("Incomplete static gauntlet export: " + side);
            var oldFilter = original.root.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == "G_Gauntlet." + side);
            string path = AnimationUtility.CalculateTransformPath(oldFilter.transform, original.root.transform);
            var newFilter = candidate.root.transform.Find(path).GetComponent<MeshFilter>();
            var matrix = oldFilter.transform.worldToLocalMatrix * worldConversion;
            var source = BuildStaticMesh(sourcePath, matrix, "Gauntlet" + side + "_SourceProof"); owned.Add(source);
            var result = new StaticMeshResult { side = side, path = path };
            int[] mapping;
            result.restErrorMeters = MortarRemodelPreview.ProveRestVertices(source, oldFilter.sharedMesh, out mapping) * MaxScale(oldFilter.transform.lossyScale);
            result.coordinatesPassed = result.restErrorMeters <= ToleranceMeters;
            if (!result.coordinatesPassed) throw new InvalidOperationException("Static gauntlet source coordinates failed: " + side + " error=" + result.restErrorMeters);
            var mesh = BuildStaticMesh(candidatePath, matrix, "GauntletBot_Static" + side + "_DieselPreview"); owned.Add(mesh);
            if (mesh.subMeshCount != oldFilter.sharedMesh.subMeshCount) throw new InvalidOperationException("Static gauntlet material submesh count changed: " + side);
            newFilter.sharedMesh = mesh;
            var renderer = newFilter.GetComponent<MeshRenderer>(); var materials = renderer.sharedMaterials;
            materials[0] = candidate.body.sharedMaterial; renderer.sharedMaterials = materials;
            result.candidateVertices = mesh.vertexCount; result.candidateTriangles = mesh.triangles.Length / 3;
            proofs.Add(new StaticProof { result = result, original = oldFilter, candidate = newFilter, sourceMesh = source, candidateMesh = mesh, mapping = mapping });
        }
    }
    static Mesh BuildStaticMesh(string path, Matrix4x4 matrix, string name)
    {
        var data = JsonUtility.FromJson<MortarRemodelPreview.InputMesh>(File.ReadAllText(path));
        int count = data?.vertices?.Length ?? 0;
        if (data == null || data.schemaVersion != 1 || data.coordinateSpace != "BLENDER_WORLD_METERS" || count == 0 || data.normals?.Length != count || data.uv?.Length != count || data.vertices.Any(v => !Finite(v)))
            throw new InvalidOperationException("Invalid static geometry schema: " + path);
        var normalMatrix = matrix.inverse.transpose;
        var mesh = new Mesh { name = name, indexFormat = count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        mesh.vertices = data.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
        mesh.normals = data.normals.Select(v => normalMatrix.MultiplyVector(v).normalized).ToArray(); mesh.uv = data.uv;
        mesh.colors = data.colors?.Length == count ? data.colors : Enumerable.Repeat(Color.white, count).ToArray();
        var submeshes = data.subMeshes != null && data.subMeshes.Length > 0 ? data.subMeshes.Select(s => s.indices).ToArray() : new[] { data.triangles };
        mesh.subMeshCount = submeshes.Length;
        for (int s = 0; s < submeshes.Length; s++)
        {
            int[] indices = (int[])submeshes[s].Clone();
            if (indices.Length % 3 != 0 || indices.Any(i => i < 0 || i >= count)) throw new InvalidOperationException("Invalid static triangles: " + path);
            if (matrix.determinant < 0) for (int i = 0; i < indices.Length; i += 3) { int t = indices[i + 1]; indices[i + 1] = indices[i + 2]; indices[i + 2] = t; }
            mesh.SetTriangles(indices, s);
        }
        mesh.RecalculateBounds(); mesh.RecalculateTangents(); return mesh;
    }
    static void SampleStatic(StaticProof proof)
    {
        var source = proof.sourceMesh.vertices; var reference = proof.original.sharedMesh.vertices;
        float error = 0;
        for (int i = 0; i < source.Length; i++) error = Mathf.Max(error, Vector3.Distance(proof.candidate.transform.TransformPoint(source[i]), proof.original.transform.TransformPoint(reference[proof.mapping[i]])));
        proof.result.animationErrorMeters = Mathf.Max(proof.result.animationErrorMeters, error);
        if (proof.candidateMesh.vertices.Any(v => !Finite(proof.candidate.transform.TransformPoint(v)))) throw new InvalidOperationException("Non-finite static attachment pose.");
    }

    static MortarRemodelPreview.InputMesh Read(string path)
    {
        var data = JsonUtility.FromJson<MortarRemodelPreview.InputMesh>(File.ReadAllText(path));
        int n = data?.vertices?.Length ?? 0;
        if (data == null || data.schemaVersion != 1 || data.coordinateSpace != "BLENDER_WORLD_METERS" || n == 0 || data.normals?.Length != n || data.uv?.Length != n || data.bonesPerVertex?.Length != n)
            throw new InvalidOperationException("Invalid mesh schema or channel lengths: " + path);
        if (data.triangles == null || data.triangles.Length % 3 != 0 || data.triangles.Any(i => i < 0 || i >= n) || data.vertices.Any(v => !Finite(v)))
            throw new InvalidOperationException("Invalid geometry: " + path);
        if (data.weights == null || data.bonesPerVertex.Sum() != data.weights.Length || data.bonesPerVertex.Any(v => v < 1 || v > 4)) throw new InvalidOperationException("Invalid weight counts: " + path);
        if (data.groupNames == null || data.groupNames.Distinct().Count() != data.groupNames.Length || data.boneHeads == null) throw new InvalidOperationException("Ambiguous or missing bone names: " + path);
        return data;
    }
    static int ApplyImporterWeightCutoff(MortarRemodelPreview.InputMesh data, float cutoff)
    {
        int offset = 0, removed = 0;
        var weights = new List<MortarRemodelPreview.Weight>();
        for (int vertex = 0; vertex < data.bonesPerVertex.Length; vertex++)
        {
            int count = data.bonesPerVertex[vertex];
            var original = data.weights.Skip(offset).Take(count).ToArray(); offset += count;
            if (original.Any(w => float.IsNaN(w.weight) || float.IsInfinity(w.weight) || w.weight <= 0) || Mathf.Abs(original.Sum(w => w.weight) - 1) > 0.0001f)
                throw new InvalidOperationException("Input weights must be finite, positive and normalized before the original importer cutoff is applied.");
            var kept = original.Where(w => w.weight >= cutoff).ToArray();
            if (kept.Length == 0) throw new InvalidOperationException("Original importer threshold removes all influences.");
            removed += original.Length - kept.Length; data.bonesPerVertex[vertex] = kept.Length;
            float sum = kept.Sum(w => w.weight);
            foreach (var weight in kept) weights.Add(new MortarRemodelPreview.Weight { groupIndex = weight.groupIndex, weight = weight.weight / sum });
        }
        data.weights = weights.ToArray(); return removed;
    }
    static Instance Create(PreviewRenderUtility preview, GameObject prefab)
    {
        var root = preview.InstantiatePrefabInScene(prefab); root.transform.position = Vector3.zero;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true)) if (component) component.enabled = false;
        foreach (var p in root.GetComponentsInChildren<ParticleSystem>(true)) p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (var a in root.GetComponentsInChildren<Animator>(true)) a.enabled = false;
        var body = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(s => s.bones.Length).ThenByDescending(s => s.sharedMesh ? s.sharedMesh.vertexCount : 0).First();
        var animator = body.GetComponentInParent<Animator>(true);
        if (!animator) throw new InvalidOperationException("Main body has no Animator ancestor.");
        body.updateWhenOffscreen = true;
        var transforms = root.GetComponentsInChildren<Transform>(true);
        return new Instance { root = root, animator = animator, body = body, transforms = transforms, positions = transforms.Select(t => t.localPosition).ToArray(), rotations = transforms.Select(t => t.localRotation).ToArray(), scales = transforms.Select(t => t.localScale).ToArray() };
    }
    static Vector3 Axis(Vector3 value) => new Vector3(-value.x, value.z, -value.y);
    static Matrix4x4 Fit(MortarRemodelPreview.InputMesh data, SkinnedMeshRenderer target, Report report)
    {
        var source = new List<Vector3>(); var destination = new List<Vector3>();
        foreach (var bone in target.bones)
        {
            var matches = data.boneHeads.Where(b => b.name == bone.name).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Missing or ambiguous bone head: " + bone.name);
            source.Add(Axis(matches[0].position)); destination.Add(bone.position);
        }
        Vector3 sourceMean = source.Aggregate(Vector3.zero, (a, b) => a + b) / source.Count;
        Vector3 targetMean = destination.Aggregate(Vector3.zero, (a, b) => a + b) / destination.Count;
        float numerator = 0, denominator = 0;
        for (int i = 0; i < source.Count; i++) { numerator += Vector3.Dot(source[i] - sourceMean, destination[i] - targetMean); denominator += (source[i] - sourceMean).sqrMagnitude; }
        if (denominator < 0.00000001f) throw new InvalidOperationException("Bone positions cannot establish a uniform scale.");
        float scale = numerator / denominator;
        if (scale <= 0 || float.IsNaN(scale) || float.IsInfinity(scale)) throw new InvalidOperationException("Positive uniform source scale was not established.");
        Vector3 offset = targetMean - sourceMean * scale;
        float error = 0;
        for (int i = 0; i < source.Count; i++) error = Mathf.Max(error, Vector3.Distance(source[i] * scale + offset, destination[i]));
        report.fittedScale = scale; report.fittedTranslation = offset; report.boneHeadErrorMeters = error;
        if (error > ToleranceMeters) throw new InvalidOperationException("Bone geometry requires rotation/nonuniform changes or differs from source; do not guess conversion.");
        var axis = Matrix4x4.zero; axis.m00 = -1; axis.m12 = 1; axis.m21 = -1; axis.m33 = 1;
        return target.transform.worldToLocalMatrix * Matrix4x4.TRS(offset, Quaternion.identity, Vector3.one * scale) * axis;
    }
    static IEnumerable<RuntimeAnimatorController> Controllers(Instance instance, string monster)
    {
        if (instance.animator.runtimeAnimatorController) yield return instance.animator.runtimeAnimatorController;
        var data = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/2.Prefabs/Monster/Data/" + monster + "Data.asset");
        if (data)
        {
            using (var serialized = new SerializedObject(data))
            {
                var property = serialized.GetIterator();
                while (property.Next(true)) if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue is RuntimeAnimatorController controller) yield return controller;
            }
        }
    }
    static void HideInterruptForBaseReview(GameObject root, List<Object> owned)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] && materials[i].shader && materials[i].shader.name == "Monster/InterruptOverlay")
                {
                    var material = new Material(materials[i]); owned.Add(material); material.SetFloat("_Dissolve", 1); materials[i] = material;
                }
            }
            renderer.sharedMaterials = materials;
        }
    }
    static VfxContract AuditContract(Instance original, Instance candidate)
    {
        var contract = new VfxContract();
        var oldPaths = original.transforms.Select(t => AnimationUtility.CalculateTransformPath(t, original.root.transform)).ToArray();
        var newPaths = candidate.transforms.Select(t => AnimationUtility.CalculateTransformPath(t, candidate.root.transform)).ToArray();
        contract.hierarchyAndLocalTransformsPreserved = oldPaths.SequenceEqual(newPaths) && original.transforms.Select((t, i) =>
            t.localPosition == candidate.transforms[i].localPosition && t.localRotation == candidate.transforms[i].localRotation && t.localScale == candidate.transforms[i].localScale).All(v => v);
        contract.bodyBonePathsPreserved = original.body.bones.Select(t => AnimationUtility.CalculateTransformPath(t, original.animator.transform))
            .SequenceEqual(candidate.body.bones.Select(t => AnimationUtility.CalculateTransformPath(t, candidate.animator.transform)));
        contract.materialSlotCountPreserved = original.body.sharedMaterials.Length == candidate.body.sharedMaterials.Length;
        contract.hitFlashBaseColorSupported = candidate.body.sharedMaterial.HasProperty("_BaseColor") || candidate.body.sharedMaterial.HasProperty("_Color");
        var oldMaterials = original.body.sharedMaterials; var newMaterials = candidate.body.sharedMaterials;
        contract.existingInterruptSlotPreserved = Enumerable.Range(0, oldMaterials.Length).Where(i => oldMaterials[i] && oldMaterials[i].shader.name == "Monster/InterruptOverlay")
            .All(i => newMaterials[i] == oldMaterials[i] && original.body.sharedMesh.subMeshCount == candidate.body.sharedMesh.subMeshCount);
        var death = candidate.root.GetComponent<DissolveDeath>();
        if (death)
        {
            using (var serialized = new SerializedObject(death))
            {
                var template = serialized.FindProperty("dissolveTemplate").objectReferenceValue as Material;
                var particle = serialized.FindProperty("particlePrefab").objectReferenceValue as ParticleSystem;
                contract.dissolveTemplatePath = AssetDatabase.GetAssetPath(template); contract.particlePrefabPath = AssetDatabase.GetAssetPath(particle);
                contract.deathTexturePropertySupported = template && template.HasProperty("_MainTexture") && candidate.body.sharedMaterial.HasProperty("_BaseMap");
                var configured = serialized.FindProperty("renderers");
                var renderers = new List<Renderer>();
                if (configured.arraySize > 0) for (int i = 0; i < configured.arraySize; i++) renderers.Add(configured.GetArrayElementAtIndex(i).objectReferenceValue as Renderer);
                else renderers.AddRange(candidate.root.GetComponentsInChildren<Renderer>(true).Where(r => r is MeshRenderer || r is SkinnedMeshRenderer));
                var firstSkin = renderers.OfType<SkinnedMeshRenderer>().FirstOrDefault();
                contract.deathParticleSelectsMainBody = firstSkin == candidate.body;
                contract.deathParticleRendererPath = firstSkin ? AnimationUtility.CalculateTransformPath(firstSkin.transform, candidate.root.transform) : null;
                if (particle)
                {
                    var shape = particle.shape; contract.deathParticleSubmeshIndex = shape.meshMaterialIndex;
                    contract.particleSubmeshIndexValid = !shape.useMeshMaterialIndex || (firstSkin && shape.meshMaterialIndex < firstSkin.sharedMesh.subMeshCount);
                }
            }
        }
        return contract;
    }
    static void Capture(PreviewRenderUtility preview, Instance original, Instance candidate, Mesh oldPose, Mesh newPose, string path)
    {
        var oldStatic = MortarRemodelPreview.FrozenRenderer(original.body, oldPose);
        var newStatic = MortarRemodelPreview.FrozenRenderer(candidate.body, newPose);
        bool oldEnabled = original.body.enabled, newEnabled = candidate.body.enabled;
        original.body.enabled = false; candidate.body.enabled = false;
        float width = Mathf.Max(WorldBounds(original.body.transform, oldPose.bounds).size.x, WorldBounds(candidate.body.transform, newPose.bounds).size.x);
        float spacing = Mathf.Max(0.8f, width * 0.72f);
        original.root.transform.position = new Vector3(-spacing, 0, 0); candidate.root.transform.position = new Vector3(spacing, 0, 0);
        Bounds bounds = WorldBounds(original.body.transform, oldPose.bounds); bounds.Encapsulate(WorldBounds(candidate.body.transform, newPose.bounds));
        foreach (var renderer in original.root.GetComponentsInChildren<Renderer>(true).Concat(candidate.root.GetComponentsInChildren<Renderer>(true)))
            if (renderer.enabled && renderer.gameObject.activeInHierarchy && !(renderer is ParticleSystemRenderer) && !renderer.name.Contains("Blades")) bounds.Encapsulate(renderer.bounds);
        preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = new Color(0.15f, 0.17f, 0.19f);
        preview.camera.orthographic = true; preview.camera.allowHDR = false; preview.camera.allowMSAA = true;
        preview.camera.nearClipPlane = 0.01f; preview.camera.farClipPlane = 1000;
        preview.ambientColor = new Color(0.55f, 0.55f, 0.55f); preview.lights[0].intensity = 1.25f; preview.lights[1].intensity = 0.8f;
        preview.lights[0].transform.rotation = Quaternion.Euler(35, 135, 0); preview.lights[1].transform.rotation = Quaternion.Euler(25, -45, 0);
        preview.camera.transform.position = bounds.center + new Vector3(0.25f, 0.3f, 1).normalized * Mathf.Max(20, bounds.size.magnitude * 2);
        preview.camera.transform.LookAt(bounds.center); preview.camera.aspect = 1.6f;
        preview.camera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x / 1.6f) * 1.4f;
        Texture2D texture = null; bool asyncCompilation = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        try
        {
            preview.BeginStaticPreview(new Rect(0, 0, 1280, 800)); preview.Render(true); preview.Render(true);
            texture = preview.EndStaticPreview(); File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            if (texture) Object.DestroyImmediate(texture); Object.DestroyImmediate(oldStatic); Object.DestroyImmediate(newStatic);
            original.body.enabled = oldEnabled; candidate.body.enabled = newEnabled;
            original.root.transform.position = Vector3.zero; candidate.root.transform.position = Vector3.zero; ShaderUtil.allowAsyncCompilation = asyncCompilation;
        }
    }
    static Bounds WorldBounds(Transform transform, Bounds local)
    {
        var bounds = new Bounds(transform.TransformPoint(local.center), Vector3.zero);
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            bounds.Encapsulate(transform.TransformPoint(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z))));
        return bounds;
    }
    static float MaxScale(Vector3 scale) => Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
    static bool Finite(Vector3 value) => !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z));
    static string Clean(string value) { foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_'); return value; }
    static string Save(Mesh mesh, string monster, string name = null)
    {
        string directory = "Assets/50.Art/Char/Monster/Redesign/" + monster; string current = "Assets";
        foreach (string part in directory.Split('/').Skip(1)) { string next = current + "/" + part; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, part); current = next; }
        string path = directory + "/" + (name ?? monster + "_DieselPreview") + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing) { EditorUtility.CopySerialized(mesh, existing); EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing); }
        else { var clone = Object.Instantiate(mesh); clone.name = mesh.name; AssetDatabase.CreateAsset(clone, path); }
        return path;
    }
}
