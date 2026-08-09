#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VeyTrace.Rendering.Occlusion;

public static class WallOcclusionAuthoring
{
    private const string SettingsPath = "Assets/99.Settings/WallOcclusionSettings.asset";
    private static readonly string[] SupportedShaderGraphPaths =
    {
        "Assets/50.Art/MapGen/MapObj/material/Generic_Standard.shadergraph",
        "Assets/50.Art/MapGen/MapObj/material/Generic_Basic.shadergraph",
        "Assets/3.Materials/ConvayorBelt/ConvayorBelt_Graph.shadergraph",
        "Assets/3.Materials/ConvayorBelt/ConvayorBelt_Corner_Graph.shadergraph"
    };

    private static readonly Regex StackName = new("^ElevationStack_[0-9]{2}$");
    private static readonly Regex LevelName = new("^Level_(B|L)[0-9]{2}$");
    private static readonly Regex PropName = new("^PF_Prop_[A-Za-z0-9_]+$");
    private static readonly Regex StageOrZoneName = new("^PF_(Stage|Zone)_[A-Za-z0-9_]+$");
    private static readonly Regex StandaloneSectionName =
        new("^PF_(Prop|Wall|Hallway)_[A-Za-z0-9_]+$");
    private static readonly Regex SafeHierarchyName = new("^[A-Za-z0-9_]+$");

    [MenuItem("Tools/Rendering/Wall Occlusion/Register-Wire Selected Prefabs")]
    public static void RegisterWireSelected()
    {
        string[] paths = GetSelectedPrefabPaths();
        if (paths.Length == 0)
        {
            Debug.LogError("[WallOcclusion] Select at least one prefab asset or prefab instance.");
            return;
        }

        WallOcclusionSettings settings = EnsureSettings();
        int totalErrors = 0;
        foreach (string path in paths)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var report = new ValidationReport(path);
                WirePrefab(root, report);
                ValidatePrefab(root, settings, report);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                totalErrors += report.ErrorCount;
                report.LogSummary("Register/Wire");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            $"[WallOcclusion] Register/Wire finished: prefabs={paths.Length}, errors={totalErrors}.");
    }

    [MenuItem("Tools/Rendering/Wall Occlusion/Validate Selected Prefabs")]
    public static void ValidateSelected()
    {
        string[] paths = GetSelectedPrefabPaths();
        if (paths.Length == 0)
        {
            Debug.LogError("[WallOcclusion] Select at least one prefab asset or prefab instance.");
            return;
        }

        WallOcclusionSettings settings = AssetDatabase.LoadAssetAtPath<WallOcclusionSettings>(SettingsPath);
        int totalErrors = 0;
        foreach (string path in paths)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var report = new ValidationReport(path);
                ValidatePrefab(root, settings, report);
                totalErrors += report.ErrorCount;
                report.LogSummary("Validate");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        Debug.Log(
            $"[WallOcclusion] Validation finished: prefabs={paths.Length}, errors={totalErrors}. " +
            "Validation errors do not block builds.");
    }

    [MenuItem("Tools/Rendering/Wall Occlusion/Dump Shader Messages")]
    public static void DumpShaderMessages()
    {
        int errorCount = 0;
        for (int shaderIndex = 0; shaderIndex < SupportedShaderGraphPaths.Length; shaderIndex++)
        {
            string path = SupportedShaderGraphPaths[shaderIndex];
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null)
            {
                Debug.LogError($"[WallOcclusion] Shader Graph not found: {path}");
                errorCount++;
                continue;
            }

            ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
            for (int i = 0; i < messages.Length; i++)
            {
                ShaderMessage message = messages[i];
                if (string.Equals(
                    message.severity.ToString(),
                    "Error",
                    StringComparison.OrdinalIgnoreCase))
                    errorCount++;
                Debug.Log(
                    $"[WallOcclusion] {message.severity}: {message.message} " +
                    $"({path}, {message.platform}, line {message.line})",
                    shader);
            }

            if (messages.Length == 0)
                Debug.Log($"[WallOcclusion] Shader has no compiler messages: {path}", shader);
        }

        Debug.Log($"[WallOcclusion] Shader message scan finished: errors={errorCount}.");
    }

    private static void WirePrefab(GameObject root, ValidationReport report)
    {
        if (IsStandaloneSectionPrefab(root))
        {
            OcclusionSection section = root.GetComponent<OcclusionSection>();
            if (section == null)
                section = root.AddComponent<OcclusionSection>();
            WireSection(section);
            return;
        }

        Transform occlusion = FindDirectChild(root.transform, "Occlusion");
        if (occlusion == null)
        {
            report.Error(root.transform, "Missing required direct child 'Occlusion'.");
            return;
        }

        for (int i = 0; i < occlusion.childCount; i++)
        {
            Transform stackTransform = occlusion.GetChild(i);
            ElevationStack stack = stackTransform.GetComponent<ElevationStack>();
            if (stack == null)
                stack = stackTransform.gameObject.AddComponent<ElevationStack>();

            for (int levelIndex = 0; levelIndex < stackTransform.childCount; levelIndex++)
            {
                Transform levelTransform = stackTransform.GetChild(levelIndex);
                ElevationLevel level = levelTransform.GetComponent<ElevationLevel>();
                if (level == null)
                    level = levelTransform.gameObject.AddComponent<ElevationLevel>();

                Transform content = FindDirectChild(levelTransform, "Content");
                if (content == null)
                {
                    report.Error(levelTransform, "Missing required direct child 'Content'.");
                    continue;
                }

                Transform occludableProps = FindDirectChild(content, "OccludableProps");
                if (occludableProps != null)
                {
                    for (int propIndex = 0; propIndex < occludableProps.childCount; propIndex++)
                        EnsureSectionOnAuthoringRoot(occludableProps.GetChild(propIndex), root, report);
                }

                AddNamedLocalSections(content, root, report);
                level.ConfigureAuthoring(
                    content,
                    content.GetComponentsInChildren<Renderer>(true),
                    content.GetComponentsInChildren<Collider>(true));
            }
        }

        OcclusionSection[] sections = root.GetComponentsInChildren<OcclusionSection>(true);
        for (int i = 0; i < sections.Length; i++)
        {
            if (!IsInsideNestedPrefab(sections[i].gameObject, root))
                WireSection(sections[i]);
        }
    }

    private static void AddNamedLocalSections(
        Transform current,
        GameObject loadedRoot,
        ValidationReport report)
    {
        for (int i = 0; i < current.childCount; i++)
        {
            Transform child = current.GetChild(i);
            if (child.name == "OccludableProps" || child.name == "LevelOnlyProps")
                continue;

            if (IsSectionName(child.name))
            {
                EnsureSectionOnAuthoringRoot(child, loadedRoot, report);
                continue;
            }

            AddNamedLocalSections(child, loadedRoot, report);
        }
    }

    private static void EnsureSectionOnAuthoringRoot(
        Transform target,
        GameObject loadedRoot,
        ValidationReport report)
    {
        OcclusionSection section = target.GetComponent<OcclusionSection>();
        if (section == null && IsInsideNestedPrefab(target.gameObject, loadedRoot))
        {
            report.Error(
                target,
                "Nested prefab root has no OcclusionSection. Select and register its source prefab first.");
            return;
        }

        if (section == null)
            section = target.gameObject.AddComponent<OcclusionSection>();
    }

    private static void WireSection(OcclusionSection section)
    {
        var renderers = new List<Renderer>();
        var colliders = new List<Collider>();
        CollectSectionOwnedComponents(section.transform, section.transform, renderers, colliders);
        section.ConfigureAuthoring(renderers.ToArray(), colliders.ToArray());
    }

    private static void CollectSectionOwnedComponents(
        Transform sectionRoot,
        Transform current,
        List<Renderer> renderers,
        List<Collider> colliders)
    {
        if (current != sectionRoot && current.GetComponent<OcclusionSection>() != null)
            return;

        renderers.AddRange(current.GetComponents<Renderer>());
        colliders.AddRange(current.GetComponents<Collider>());
        for (int i = 0; i < current.childCount; i++)
            CollectSectionOwnedComponents(sectionRoot, current.GetChild(i), renderers, colliders);
    }

    private static void ValidatePrefab(
        GameObject root,
        WallOcclusionSettings settings,
        ValidationReport report)
    {
        ValidateSafeName(root.transform, report);
        if (IsStandaloneSectionPrefab(root))
        {
            if (!StandaloneSectionName.IsMatch(root.name))
                report.Error(root.transform, "Standalone root must match PF_Prop_*, PF_Wall_*, or PF_Hallway_*.");
            ValidateStandaloneSection(root, settings, report);
            return;
        }

        if (!StageOrZoneName.IsMatch(root.name))
            report.Error(root.transform, "Root must match PF_Stage_* or PF_Zone_*.");

        Transform occlusion = FindDirectChild(root.transform, "Occlusion");
        if (occlusion == null)
        {
            report.Error(root.transform, "Missing required direct child 'Occlusion'.");
            return;
        }

        ValidateSafeNameRecursive(occlusion, report);
        int occlusionNameCount = CountDirectChildren(root.transform, "Occlusion");
        if (occlusionNameCount != 1)
            report.Error(root.transform, $"Expected exactly one direct 'Occlusion' child, found {occlusionNameCount}.");

        if (occlusion.childCount == 0)
            report.Error(occlusion, "Occlusion must contain at least one ElevationStack.");

        var sectionColliderOwners = new Dictionary<Collider, OcclusionSection>();
        var levelColliderOwners = new Dictionary<Collider, ElevationLevel>();
        for (int i = 0; i < occlusion.childCount; i++)
        {
            Transform stackTransform = occlusion.GetChild(i);
            if (!StackName.IsMatch(stackTransform.name))
                report.Error(stackTransform, "Stack name must match ElevationStack_00.");

            ElevationStack stack = stackTransform.GetComponent<ElevationStack>();
            if (stack == null)
            {
                report.Error(stackTransform, "ElevationStack component is missing.");
                continue;
            }

            if (!stack.HasValidTransform(out string transformReason))
                report.Error(stackTransform, transformReason);
            ValidateStack(stack, settings, report, sectionColliderOwners, levelColliderOwners);
        }
    }

    private static void ValidateStack(
        ElevationStack stack,
        WallOcclusionSettings settings,
        ValidationReport report,
        Dictionary<Collider, OcclusionSection> sectionColliderOwners,
        Dictionary<Collider, ElevationLevel> levelColliderOwners)
    {
        if (stack.transform.childCount == 0)
            report.Error(stack.transform, "ElevationStack must contain at least one direct Level.");

        var heights = new List<float>();
        for (int i = 0; i < stack.transform.childCount; i++)
        {
            Transform levelTransform = stack.transform.GetChild(i);
            if (!LevelName.IsMatch(levelTransform.name))
                report.Error(levelTransform, "Level name must match Level_B00 or Level_L00.");

            ElevationLevel level = levelTransform.GetComponent<ElevationLevel>();
            if (level == null)
            {
                report.Error(levelTransform, "ElevationLevel component is missing.");
                continue;
            }

            for (int h = 0; h < heights.Count; h++)
            {
                if (Mathf.Abs(heights[h] - level.ReferenceWorldY) < 0.001f)
                    report.Error(levelTransform, "Another Level in this Stack has the same reference Y.");
            }
            heights.Add(level.ReferenceWorldY);

            ValidateLevel(level, settings, report, sectionColliderOwners, levelColliderOwners);
        }
    }

    private static void ValidateLevel(
        ElevationLevel level,
        WallOcclusionSettings settings,
        ValidationReport report,
        Dictionary<Collider, OcclusionSection> sectionColliderOwners,
        Dictionary<Collider, ElevationLevel> levelColliderOwners)
    {
        if (!level.IsRuntimeValid(out string reason))
            report.Error(level.transform, reason);

        Transform content = FindDirectChild(level.transform, "Content");
        if (content == null)
            return;

        int contentCount = CountDirectChildren(level.transform, "Content");
        if (contentCount != 1)
            report.Error(level.transform, $"Expected exactly one direct Content child, found {contentCount}.");

        Transform occludable = FindDirectChild(content, "OccludableProps");
        Transform levelOnly = FindDirectChild(content, "LevelOnlyProps");
        if (occludable == null || CountDirectChildren(content, "OccludableProps") != 1)
            report.Error(content, "Exactly one direct OccludableProps child is required, even when empty.");
        if (levelOnly == null || CountDirectChildren(content, "LevelOnlyProps") != 1)
            report.Error(content, "Exactly one direct LevelOnlyProps child is required, even when empty.");

        if (occludable != null)
            ValidatePropContainer(occludable, true, report);
        if (levelOnly != null)
            ValidatePropContainer(levelOnly, false, report);

        IReadOnlyList<Collider> levelColliders = level.ContentColliders;
        for (int i = 0; i < levelColliders.Count; i++)
        {
            Collider collider = levelColliders[i];
            if (collider == null)
                continue;
            if (levelColliderOwners.TryGetValue(collider, out ElevationLevel existing) && existing != level)
                report.Error(collider.transform, $"Collider also belongs to Level '{existing.name}'.");
            else
                levelColliderOwners[collider] = level;
        }

        OcclusionSection[] sections = content.GetComponentsInChildren<OcclusionSection>(true);
        for (int i = 0; i < sections.Length; i++)
            ValidateSection(sections[i], level, settings, report, sectionColliderOwners);

        report.Info(
            level.transform,
            $"counts: renderers={level.ContentRenderers.Count}, colliders={level.ContentColliders.Count}, " +
            $"xzAreas={level.XZAreas.Count}, sections={sections.Length}");
    }

    private static void ValidatePropContainer(
        Transform container,
        bool shouldBeOccludable,
        ValidationReport report)
    {
        if (container.GetComponent<OcclusionSection>() != null)
            report.Error(container, "Prop category container must not have OcclusionSection.");

        for (int i = 0; i < container.childCount; i++)
        {
            Transform prop = container.GetChild(i);
            if (!PropName.IsMatch(prop.name))
                report.Error(prop, "Direct prop root name must match PF_Prop_*.");

            OcclusionSection rootSection = prop.GetComponent<OcclusionSection>();
            OcclusionSection[] allSections = prop.GetComponentsInChildren<OcclusionSection>(true);
            if (shouldBeOccludable)
            {
                if (rootSection == null || allSections.Length != 1)
                    report.Error(prop, "OccludableProps child needs exactly one OcclusionSection on its root.");
            }
            else if (allSections.Length > 0)
            {
                report.Error(prop, "LevelOnlyProps child and descendants must not have OcclusionSection.");
            }

            Transform[] descendants = prop.GetComponentsInChildren<Transform>(true);
            for (int d = 1; d < descendants.Length; d++)
            {
                if (PropName.IsMatch(descendants[d].name))
                    report.Error(descendants[d], "Nested PF_Prop_* is forbidden; use sibling prop roots.");
            }
        }
    }

    private static void ValidateSection(
        OcclusionSection section,
        ElevationLevel expectedLevel,
        WallOcclusionSettings settings,
        ValidationReport report,
        Dictionary<Collider, OcclusionSection> colliderOwners)
    {
        if (!section.IsRuntimeValid(out string reason))
            report.Error(section.transform, reason);
        if (section.Level != expectedLevel)
            report.Error(section.transform, "OcclusionSection resolves to a different ElevationLevel.");

        for (int i = 0; i < section.Colliders.Count; i++)
        {
            Collider collider = section.Colliders[i];
            if (collider == null)
                continue;
            if (colliderOwners.TryGetValue(collider, out OcclusionSection existing) && existing != section)
                report.Error(collider.transform, $"Collider also belongs to Section '{existing.name}'.");
            else
                colliderOwners[collider] = section;
        }

        ValidateMaterials(section.Renderers, settings, section.transform, report);
    }

    private static void ValidateStandaloneSection(
        GameObject root,
        WallOcclusionSettings settings,
        ValidationReport report)
    {
        OcclusionSection section = root.GetComponent<OcclusionSection>();
        if (section == null)
        {
            report.Error(root.transform, "Standalone wall/hallway/prop prefab needs OcclusionSection on root.");
            return;
        }

        if (section.Renderers.Count == 0)
            report.Error(root.transform, "OcclusionSection has no wired Renderer.");
        if (section.Colliders.Count == 0)
            report.Error(root.transform, "OcclusionSection has no wired Collider.");
        ValidateMaterials(section.Renderers, settings, root.transform, report);
    }

    private static void ValidateMaterials(
        IReadOnlyList<Renderer> renderers,
        WallOcclusionSettings settings,
        Transform context,
        ValidationReport report)
    {
        if (settings == null)
        {
            report.Error(context, $"Settings asset is missing: {SettingsPath}");
            return;
        }

        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;
            Material[] materials = renderer.sharedMaterials;
            for (int m = 0; m < materials.Length; m++)
            {
                Material material = materials[m];
                if (material == null ||
                    !material.HasProperty(WallOcclusionGlobals.StrengthPropertyId))
                {
                    string name = material != null ? material.name : "<null>";
                    report.Error(
                        renderer.transform,
                        $"Material does not support _WallOcclusionStrength: {name}.");
                }
            }
        }
    }

    private static WallOcclusionSettings EnsureSettings()
    {
        WallOcclusionSettings settings = AssetDatabase.LoadAssetAtPath<WallOcclusionSettings>(SettingsPath);
        if (settings != null)
            return settings;

        settings = ScriptableObject.CreateInstance<WallOcclusionSettings>();
        AssetDatabase.CreateAsset(settings, SettingsPath);
        return settings;
    }

    private static bool IsStandaloneSectionPrefab(GameObject root)
    {
        return root.name.StartsWith("PF_Prop_", StringComparison.Ordinal) ||
            root.name.StartsWith("PF_Wall_", StringComparison.Ordinal) ||
            root.name.StartsWith("PF_Hallway_", StringComparison.Ordinal);
    }

    private static bool IsSectionName(string name)
    {
        return name.StartsWith("Section_", StringComparison.Ordinal) ||
            name.StartsWith("WallSection_", StringComparison.Ordinal) ||
            name.StartsWith("HallwaySection_", StringComparison.Ordinal);
    }

    private static bool IsInsideNestedPrefab(GameObject target, GameObject loadedRoot)
    {
        if (target == loadedRoot || !PrefabUtility.IsPartOfPrefabInstance(target))
            return false;
        GameObject nearestRoot = PrefabUtility.GetNearestPrefabInstanceRoot(target);
        return nearestRoot != null && nearestRoot != loadedRoot;
    }

    private static Transform FindDirectChild(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == name)
                return child;
        }
        return null;
    }

    private static int CountDirectChildren(Transform parent, string name)
    {
        int count = 0;
        for (int i = 0; i < parent.childCount; i++)
        {
            if (parent.GetChild(i).name == name)
                count++;
        }
        return count;
    }

    private static void ValidateSafeNameRecursive(Transform root, ValidationReport report)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            ValidateSafeName(transforms[i], report);
    }

    private static void ValidateSafeName(Transform target, ValidationReport report)
    {
        if (!SafeHierarchyName.IsMatch(target.name))
            report.Error(target, "Name must use exact case-sensitive English ASCII letters, digits, and underscores.");
    }

    private static string[] GetSelectedPrefabPaths()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (UnityEngine.Object selected in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(selected);
            if (selected is GameObject gameObject && string.IsNullOrEmpty(path))
                path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                paths.Add(path);
        }
        return paths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    private sealed class ValidationReport
    {
        private readonly string prefabPath;
        public int ErrorCount { get; private set; }
        public int InfoCount { get; private set; }

        public ValidationReport(string prefabPath)
        {
            this.prefabPath = prefabPath;
        }

        public void Error(Transform target, string message)
        {
            ErrorCount++;
            Debug.LogError(
                $"[WallOcclusion] prefab='{prefabPath}', object='{GetHierarchyPath(target)}': " +
                $"{message}",
                target);
        }

        public void Info(Transform target, string message)
        {
            InfoCount++;
            Debug.Log(
                $"[WallOcclusion] prefab='{prefabPath}', object='{GetHierarchyPath(target)}': {message}",
                target);
        }

        public void LogSummary(string operation)
        {
            if (ErrorCount == 0)
                Debug.Log($"[WallOcclusion] {operation} passed: prefab='{prefabPath}', errors=0, info={InfoCount}.");
            else
                Debug.LogError(
                    $"[WallOcclusion] {operation} finished with errors: " +
                    $"prefab='{prefabPath}', errors={ErrorCount}, info={InfoCount}. Build is not blocked.");
        }

        private static string GetHierarchyPath(Transform target)
        {
            if (target == null)
                return "<null>";
            var names = new Stack<string>();
            Transform current = target;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names);
        }
    }
}
#endif
