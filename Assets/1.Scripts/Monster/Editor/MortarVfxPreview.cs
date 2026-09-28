using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Editor-only material and particle-shape checks on an isolated Mortar copy.</summary>
public static class MortarVfxPreview
{
    const string PrefabPath = "Assets/2.Prefabs/Monster/MortarBot.prefab";
    const string MeshPath = "Assets/50.Art/Char/Monster/Redesign/MortarBot/MortarBot_DieselPreview.asset";
    const string Output = "Generated/monster-remodel-20260926/MortarBot/unity-vfx-preview";
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Serializable] public class ShaderProperty { public string name, type, value; }
    [Serializable] public class Report
    {
        public string capturedAtUtc, error, sourceShader, dissolveShader, dissolveTemplatePath, particlePrefabPath;
        public string scope = "Editor Preview Scene only: invokes the existing material/MPB/particle-shape methods directly, manually simulates particles, and samples clips. Material screenshots use frozen BakeMesh geometry; particles bind to the real preview SkinnedMeshRenderer. No damage event, death coroutine, RPC, gameplay controller, despawn, pooling, or multiplayer execution.";
        public bool runtimeVfxVerified = false, gameplayPrefabChanged, templateMaterialChanged;
        public bool hitFlashIncludesBody, hitTintApplied, hitTintCleared, textureCopiedByExistingDeathMethod;
        public bool sourceHasBaseColor, dissolveHasBaseColor, dissolveHasMainColor, deathTintSentinelCopied;
        public bool currentSourceTintIsWhite, particleSurfaceBoundToNewMesh, particleUsesMeshMaterialIndex, particleUsesMeshColors;
        public bool originalTransformsRestored, savedBoundsContainFrameSamples;
        public int bodySubmeshes, materialSlots, particleMeshMaterialIndex, particleCountAfterSimulation, sampledAnimationFrames;
        public float boundsMaximumOverflow, dissolveDuration, despawnGrace;
        public Bounds savedMeshBounds, sampledAnimationBounds;
        public Color sourceBaseColor, actualDeathMainColor, tintSentinel;
        public ShaderProperty[] sourceProperties, dissolveProperties;
        public string[] screenshots, gaps;
    }

    [MenuItem("Tools/Monster Remodel/Preview Mortar VFX Compatibility")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("VFX preview requires edit mode after compilation.");
        Directory.CreateDirectory(Output);
        var report = new Report { capturedAtUtc = DateTime.UtcNow.ToString("O") };
        var screenshots = new List<string>();
        var gaps = new List<string>();
        var owned = new List<Object>();
        PreviewRenderUtility preview = null;
        DissolveDeath death = null;
        Material template = null;
        string templateBefore = null;
        string prefabHash = Hash(PrefabPath);
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (!prefab || !mesh) throw new InvalidOperationException("Run Mortar candidate validation first.");
            preview = new PreviewRenderUtility();
            var root = preview.InstantiatePrefabInScene(prefab);
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) if (behaviour) behaviour.enabled = false;
            var animator = root.GetComponentsInChildren<Animator>(true).Single(); animator.enabled = false;
            var skin = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
            skin.sharedMesh = mesh; skin.localBounds = mesh.bounds; skin.updateWhenOffscreen = true;
            MortarRemodelPreview.ApplyPreviewMaterial(skin, owned);
            var source = skin.sharedMaterial;
            report.sourceShader = source.shader.name; report.sourceProperties = Properties(source);
            report.bodySubmeshes = mesh.subMeshCount; report.materialSlots = skin.sharedMaterials.Length;
            report.sourceHasBaseColor = source.HasProperty("_BaseColor");
            report.sourceBaseColor = report.sourceHasBaseColor ? source.GetColor("_BaseColor") : Color.white;
            report.currentSourceTintIsWhite = ColorDistance(report.sourceBaseColor, Color.white) < 0.00001f;
            report.savedMeshBounds = mesh.bounds;

            var transforms = root.GetComponentsInChildren<Transform>(true);
            var positions = transforms.Select(t => t.localPosition).ToArray();
            var rotations = transforms.Select(t => t.localRotation).ToArray();
            var scales = transforms.Select(t => t.localScale).ToArray();
            Action reset = () =>
            {
                animator.Rebind();
                for (int i = 0; i < transforms.Length; i++)
                { transforms[i].localPosition = positions[i]; transforms[i].localRotation = rotations[i]; transforms[i].localScale = scales[i]; }
            };
            var bake = new Mesh(); owned.Add(bake);
            bool hasBounds = false; Bounds union = default;
            foreach (var clip in animator.runtimeAnimatorController.animationClips.Distinct())
            {
                int frames = Mathf.Max(1, Mathf.CeilToInt(clip.length * clip.frameRate));
                for (int frame = 0; frame <= frames; frame++)
                {
                    reset(); clip.SampleAnimation(animator.gameObject, Mathf.Min(clip.length, frame / clip.frameRate));
                    skin.BakeMesh(bake); report.sampledAnimationFrames++;
                    if (!hasBounds) { union = bake.bounds; hasBounds = true; } else union.Encapsulate(bake.bounds);
                    Vector3 lo = skin.sharedMesh.bounds.min - bake.bounds.min;
                    Vector3 hi = bake.bounds.max - skin.sharedMesh.bounds.max;
                    report.boundsMaximumOverflow = Mathf.Max(report.boundsMaximumOverflow, lo.x, lo.y, lo.z, hi.x, hi.y, hi.z);
                }
            }
            report.sampledAnimationBounds = union;
            report.savedBoundsContainFrameSamples = report.boundsMaximumOverflow <= 0.00001f;
            if (!report.savedBoundsContainFrameSamples) gaps.Add("Preview mesh bounds miss some animation frame samples. Expand using sampledAnimationBounds before gameplay integration.");
            reset();
            report.originalTransformsRestored = transforms.Select((t, i) => t.localPosition == positions[i] && t.localRotation == rotations[i] && t.localScale == scales[i]).All(v => v);
            var idle = animator.runtimeAnimatorController.animationClips.First(c => c.name == "Idle");
            idle.SampleAnimation(animator.gameObject, 0);
            skin.BakeMesh(bake); Bounds cameraBounds = bake.bounds;
            Capture(preview, skin, cameraBounds, "alive.png", screenshots);

            // Use HitFlash's actual cache and MPB methods, without starting its coroutine.
            var hit = root.GetComponent<HitFlash>();
            if (!hit) hit = root.AddComponent<HitFlash>();
            Invoke(hit, "Awake"); Invoke(hit, "CacheRenderers");
            var cached = (Renderer[])Field(hit, "_renderers");
            int bodyIndex = Array.IndexOf(cached, skin);
            report.hitFlashIncludesBody = bodyIndex >= 0;
            if (bodyIndex < 0) throw new InvalidOperationException("HitFlash does not collect the new body.");
            var flash = new Color(1, 0.25f, 0.25f, 1);
            Invoke(hit, "ApplyTint", bodyIndex, flash);
            var block = new MaterialPropertyBlock(); skin.GetPropertyBlock(block);
            report.hitTintApplied = ColorDistance(block.GetColor("_BaseColor"), flash) < 0.00001f;
            Capture(preview, skin, cameraBounds, "hit-flash.png", screenshots);
            Invoke(hit, "ClearTint"); skin.GetPropertyBlock(block);
            report.hitTintCleared = block.isEmpty;
            Capture(preview, skin, cameraBounds, "hit-restored.png", screenshots);

            death = root.GetComponent<DissolveDeath>();
            if (!death) throw new InvalidOperationException("Mortar has no DissolveDeath component.");
            template = (Material)Field(death, "dissolveTemplate");
            if (!template) throw new InvalidOperationException("Dissolve template is missing.");
            templateBefore = EditorJsonUtility.ToJson(template);
            report.dissolveTemplatePath = AssetDatabase.GetAssetPath(template);
            report.dissolveShader = template.shader.name; report.dissolveProperties = Properties(template);
            report.dissolveHasBaseColor = template.HasProperty("_BaseColor");
            report.dissolveHasMainColor = template.HasProperty("_Main_Color");
            report.dissolveDuration = (float)Field(death, "duration"); report.despawnGrace = (float)Field(death, "despawnGrace");
            var actualDeath = (Material)Invoke(death, "BuildDissolveMaterial", source); owned.Add(actualDeath);
            report.textureCopiedByExistingDeathMethod = actualDeath.HasProperty("_MainTexture") && actualDeath.GetTexture("_MainTexture") == source.GetTexture("_BaseMap");
            report.actualDeathMainColor = actualDeath.HasProperty("_Main_Color") ? actualDeath.GetColor("_Main_Color") : Color.clear;
            var sentinelSource = new Material(source); owned.Add(sentinelSource);
            report.tintSentinel = new Color(0.21f, 0.42f, 0.63f, 1);
            sentinelSource.SetColor("_BaseColor", report.tintSentinel);
            var sentinelDeath = (Material)Invoke(death, "BuildDissolveMaterial", sentinelSource); owned.Add(sentinelDeath);
            Color resultingTint = sentinelDeath.HasProperty("_BaseColor") ? sentinelDeath.GetColor("_BaseColor") : sentinelDeath.HasProperty("_Main_Color") ? sentinelDeath.GetColor("_Main_Color") : Color.clear;
            report.deathTintSentinelCopied = ColorDistance(report.tintSentinel, resultingTint) < 0.00001f;
            if (!report.deathTintSentinelCopied) gaps.Add("Existing death code does not transfer a nonwhite _BaseColor tint into the compiled dissolve shader. Current candidate uses white tint and baked base color, so its texture can still transfer. This is an existing shader/code mismatch.");

            skin.sharedMaterials = new[] { actualDeath };
            foreach (float cutoff in new[] { 1f, 0.5f, 0f })
            {
                actualDeath.SetFloat("_Cutoff", cutoff);
                Capture(preview, skin, cameraBounds, "death-cutoff-" + Mathf.RoundToInt(cutoff * 100).ToString("000") + ".png", screenshots);
            }

            // Spawn/bind through the production methods; advance the particle simulation explicitly in edit mode.
            Invoke(death, "CollectRenderers"); Invoke(death, "SpawnParticle"); Invoke(death, "BindParticleShape");
            var particle = (ParticleSystem)Field(death, "_particle");
            var particlePrefab = (ParticleSystem)Field(death, "particlePrefab");
            report.particlePrefabPath = AssetDatabase.GetAssetPath(particlePrefab);
            if (!particle) throw new InvalidOperationException("Death particle prefab did not instantiate.");
            var shape = particle.shape;
            report.particleSurfaceBoundToNewMesh = shape.shapeType == ParticleSystemShapeType.SkinnedMeshRenderer && shape.skinnedMeshRenderer == skin && shape.skinnedMeshRenderer.sharedMesh == mesh;
            report.particleMeshMaterialIndex = shape.meshMaterialIndex;
            report.particleUsesMeshMaterialIndex = shape.useMeshMaterialIndex;
            report.particleUsesMeshColors = shape.useMeshColors;
            actualDeath.SetFloat("_Cutoff", 0.5f);
            particle.useAutoRandomSeed = false; particle.randomSeed = 12345;
            particle.Simulate(0.35f, true, true, true);
            report.particleCountAfterSimulation = particle.GetComponentsInChildren<ParticleSystem>(true).Sum(p => p.particleCount);
            Capture(preview, skin, cameraBounds, "death-particles-editor-simulation.png", screenshots);
            if (report.particleCountAfterSimulation == 0) gaps.Add("Particle shape is bound but the edit-mode simulation emitted no particles; verify emission in real Play Mode.");
            if (report.particleUsesMeshMaterialIndex && report.particleMeshMaterialIndex >= mesh.subMeshCount) gaps.Add("Particle submesh selection is outside the candidate mesh.");
            reset();
            gaps.Add("Damage events, death coroutine timing, projectile trail/explosion, despawn tail, effect pooling, host/client RPC and gameplay camera remain unverified.");
            gaps.Add("Normal-map import and final gameplay material setup are not part of this transient preview.");
            Debug.Log("[MortarVfxPreview] Isolated material/particle checks complete. See vfx-report.json for actual results and runtime gaps.");
        }
        catch (Exception exception) { report.error = exception.ToString(); Debug.LogError("[MortarVfxPreview] " + exception); }
        finally
        {
            // Avoid DissolveDeath.OnDestroy using delayed Destroy in edit mode; these temporary materials are cleaned below.
            if (death && Field(death, "_created") is List<Material> created) created.Clear();
            report.gameplayPrefabChanged = Hash(PrefabPath) != prefabHash;
            report.templateMaterialChanged = template && templateBefore != null && EditorJsonUtility.ToJson(template) != templateBefore;
            report.screenshots = screenshots.ToArray(); report.gaps = gaps.ToArray();
            File.WriteAllText(Path.Combine(Output, "vfx-report.json"), JsonUtility.ToJson(report, true));
            if (preview != null) preview.Cleanup();
            foreach (var value in owned) if (value && !EditorUtility.IsPersistent(value)) Object.DestroyImmediate(value);
        }
    }

    static object Invoke(object instance, string method, params object[] args)
    {
        var member = instance.GetType().GetMethod(method, PrivateInstance);
        if (member == null) throw new MissingMethodException(instance.GetType().Name, method);
        return member.Invoke(instance, args);
    }
    static object Field(object instance, string name)
    {
        var field = instance.GetType().GetField(name, PrivateInstance);
        if (field == null) throw new MissingFieldException(instance.GetType().Name, name);
        return field.GetValue(instance);
    }
    static float ColorDistance(Color left, Color right) => Mathf.Abs(left.r - right.r) + Mathf.Abs(left.g - right.g) + Mathf.Abs(left.b - right.b) + Mathf.Abs(left.a - right.a);
    static string Hash(string path)
    {
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path)));
    }
    static ShaderProperty[] Properties(Material material)
    {
        var results = new List<ShaderProperty>();
        for (int i = 0; i < material.shader.GetPropertyCount(); i++)
        {
            string name = material.shader.GetPropertyName(i);
            var type = material.shader.GetPropertyType(i);
            string value = null;
            switch (type)
            {
                case ShaderPropertyType.Color: value = JsonUtility.ToJson(material.GetColor(name)); break;
                case ShaderPropertyType.Vector: value = JsonUtility.ToJson(material.GetVector(name)); break;
                case ShaderPropertyType.Float: case ShaderPropertyType.Range: value = material.GetFloat(name).ToString("R", System.Globalization.CultureInfo.InvariantCulture); break;
                case ShaderPropertyType.Texture: value = AssetDatabase.GetAssetPath(material.GetTexture(name)); break;
            }
            results.Add(new ShaderProperty { name = name, type = type.ToString(), value = value });
        }
        return results.ToArray();
    }
    static void Capture(PreviewRenderUtility preview, SkinnedMeshRenderer skin, Bounds bounds, string filename, List<string> files)
    {
        var pose = new Mesh(); skin.BakeMesh(pose);
        var frozen = MortarRemodelPreview.FrozenRenderer(skin, pose);
        bool enabled = skin.enabled; skin.enabled = false;
        bool asyncCompilation = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = new Color(0.15f, 0.17f, 0.19f);
        preview.camera.orthographic = true; preview.camera.allowHDR = false; preview.camera.allowMSAA = true;
        preview.camera.nearClipPlane = 0.01f; preview.camera.farClipPlane = 100;
        preview.ambientColor = new Color(0.55f, 0.55f, 0.55f);
        preview.lights[0].intensity = 1.25f; preview.lights[0].transform.rotation = Quaternion.Euler(35, 135, 0);
        preview.lights[1].intensity = 0.8f; preview.lights[1].transform.rotation = Quaternion.Euler(25, -45, 0);
        Vector3 center = skin.transform.TransformPoint(bounds.center);
        preview.camera.transform.position = center + new Vector3(0.35f, 0.22f, 1).normalized * 10;
        preview.camera.transform.LookAt(center); preview.camera.aspect = 1;
        preview.camera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x) * 1.35f;
        Texture2D image = null;
        try
        {
            preview.BeginStaticPreview(new Rect(0, 0, 900, 900)); preview.Render(true); preview.Render(true);
            image = preview.EndStaticPreview(); File.WriteAllBytes(Path.Combine(Output, filename), image.EncodeToPNG()); files.Add(filename);
        }
        finally
        {
            if (image) Object.DestroyImmediate(image);
            Object.DestroyImmediate(frozen); Object.DestroyImmediate(pose); skin.enabled = enabled;
            ShaderUtil.allowAsyncCompilation = asyncCompilation;
        }
    }
}
