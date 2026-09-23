using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Dev Boot 진입 전에 실효 빌드 씬 목록을 보정하고, Play 종료 뒤 원본을 복원한다.
/// </summary>
[InitializeOnLoad]
public static class DevBootLauncher
{
    internal enum BuildSceneSourceKind
    {
        Global = 0,
        ActiveProfile = 1,
    }

    internal sealed class BuildSceneAdjustment
    {
        public BuildSceneAdjustment(EditorBuildSettingsScene[] scenes, bool changed)
        {
            Scenes = scenes;
            Changed = changed;
        }

        public EditorBuildSettingsScene[] Scenes { get; }
        public bool Changed { get; }
    }

    internal sealed class SceneListSource
    {
        public BuildSceneSourceKind Kind { get; set; }
        public BuildProfile Profile { get; set; }
        public string ProfileAssetPath { get; set; }
        public EditorBuildSettingsScene[] Scenes { get; set; }
    }

    [Serializable]
    private sealed class SceneEntry
    {
        public string path;
        public bool enabled;
    }

    [Serializable]
    private sealed class BuildSceneSnapshot
    {
        public BuildSceneSourceKind sourceKind;
        public string profileAssetPath;
        public bool listChanged;
        public SceneEntry[] scenes;
    }

    private const string SnapshotSessionKey = "MainProject.DevBoot.BuildSceneSnapshot";
    private const string DevBootSceneGuid = "180a2dd6e0939fed247ab6908eb0ec7d";
    private const string DevBootScenePathFallback = "Assets/0.Scenes/Dev_Boot.unity";

    /// <summary>
    /// Dev_Boot 씬의 현재 경로. <b>GUID 로 푼다</b> — 경로를 상수로 박아두면 씬을 옮기는 순간
    /// 조용히 안 맞게 되고, 그게 이 도구가 애초에 고친 버그다(PLAN 1절 문제 #4).
    /// 위 상수는 GUID 조회가 실패했을 때의 최후 폴백일 뿐이다.
    /// </summary>
    internal static string DevBootScenePath
    {
        get
        {
            string path = AssetDatabase.GUIDToAssetPath(DevBootSceneGuid);
            return string.IsNullOrEmpty(path) ? DevBootScenePathFallback : path;
        }
    }

    static DevBootLauncher()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    public static bool CanLaunch =>
        !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode;

    public static void Launch(string targetScenePath)
    {
        if (!CanLaunch)
        {
            Debug.LogWarning("[DevBoot] Play 진입 또는 실행 중에는 새 Dev Boot를 시작할 수 없다.");
            return;
        }

        SceneAsset targetScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(targetScenePath);
        if (targetScene == null)
        {
            Debug.LogError($"[DevBoot] 타겟 씬 에셋을 찾지 못했다: {targetScenePath}");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("[DevBoot] 수정 씬 저장이 취소되어 Play 진입을 중단했다.");
            return;
        }

        SceneAsset devBootScene = LoadDevBootScene();
        if (devBootScene == null)
        {
            Debug.LogError($"[DevBoot] Dev_Boot 씬을 찾지 못했다. guid={DevBootSceneGuid} fallback={DevBootScenePathFallback}");
            return;
        }

        SceneListSource source = ResolveSceneListSource();
        if (source.Kind == BuildSceneSourceKind.ActiveProfile && string.IsNullOrEmpty(source.ProfileAssetPath))
        {
            Debug.LogError("[DevBoot] 활성 빌드 프로필의 에셋 경로를 찾지 못해 안전하게 복원할 수 없다.");
            return;
        }

        BuildSceneAdjustment adjustment = EnsureSceneEnabled(source.Scenes, targetScenePath);
        SaveSnapshot(source, adjustment.Changed);

        try
        {
            if (adjustment.Changed)
            {
                WriteScenes(source, adjustment.Scenes);
            }

            DevBootTarget.ScenePath = targetScenePath;
            DevBootSceneCatalog.RecordRecentScene(targetScenePath);
            EditorSceneManager.playModeStartScene = devBootScene;
            AssetDatabase.SaveAssets();
            EditorApplication.EnterPlaymode();
        }
        catch
        {
            RestorePendingChanges();
            throw;
        }
    }

    public static void ForceCleanup()
    {
        if (RestorePendingChanges())
        {
            Debug.Log("[DevBoot] 저장된 스냅샷으로 빌드 씬 목록을 강제 복원했다.");
            return;
        }

        SceneListSource source = ResolveSceneListSource();
        string targetPath = DevBootTarget.ScenePath;
        bool removeTarget = EditorUtility.DisplayDialog(
            "Dev Boot 빌드 목록 강제 정리",
            "복원 스냅샷이 없다. Dev_Boot과 마지막 타겟을 현재 실효 빌드 목록에서 제거할까?\n\n" +
            $"마지막 타겟: {targetPath}\n\n원래 등록돼 있던 씬일 수도 있으므로 확인 후 선택할 것.",
            "둘 다 제거",
            "Dev_Boot만 제거");

        string[] pathsToRemove = removeTarget
            ? new[] { DevBootScenePath, targetPath }
            : new[] { DevBootScenePath };
        EditorBuildSettingsScene[] cleaned = RemoveScenes(source.Scenes, pathsToRemove);
        if (cleaned.Length != source.Scenes.Length)
        {
            WriteScenes(source, cleaned);
            AssetDatabase.SaveAssets();
        }

        EditorSceneManager.playModeStartScene = null;
        Debug.Log($"[DevBoot] 강제 정리 완료. source={source.Kind} removed={source.Scenes.Length - cleaned.Length}");
    }

    internal static BuildSceneSourceKind SelectBuildSceneSource(bool hasActiveProfile, bool overrideGlobalScenes)
    {
        return hasActiveProfile && overrideGlobalScenes
            ? BuildSceneSourceKind.ActiveProfile
            : BuildSceneSourceKind.Global;
    }

    internal static BuildSceneAdjustment EnsureSceneEnabled(
        IReadOnlyList<EditorBuildSettingsScene> scenes,
        string targetScenePath)
    {
        EditorBuildSettingsScene[] source = scenes?.ToArray() ?? Array.Empty<EditorBuildSettingsScene>();
        int index = Array.FindIndex(
            source,
            scene => string.Equals(scene.path, targetScenePath, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            EditorBuildSettingsScene[] added = new EditorBuildSettingsScene[source.Length + 1];
            Array.Copy(source, added, source.Length);
            added[source.Length] = new EditorBuildSettingsScene(targetScenePath, true);
            return new BuildSceneAdjustment(added, true);
        }

        if (source[index].enabled)
        {
            return new BuildSceneAdjustment(source, false);
        }

        EditorBuildSettingsScene[] enabled = CloneScenes(source);
        enabled[index] = new EditorBuildSettingsScene(enabled[index].path, true);
        return new BuildSceneAdjustment(enabled, true);
    }

    internal static SceneListSource ResolveSceneListSource()
    {
        BuildProfile activeProfile = BuildProfile.GetActiveBuildProfile();
        BuildSceneSourceKind kind = SelectBuildSceneSource(
            activeProfile != null,
            activeProfile != null && activeProfile.overrideGlobalScenes);

        if (kind == BuildSceneSourceKind.ActiveProfile)
        {
            return new SceneListSource
            {
                Kind = kind,
                Profile = activeProfile,
                ProfileAssetPath = AssetDatabase.GetAssetPath(activeProfile),
                Scenes = activeProfile.scenes ?? Array.Empty<EditorBuildSettingsScene>(),
            };
        }

        return new SceneListSource
        {
            Kind = kind,
            Scenes = EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>(),
        };
    }

    internal static void WriteScenes(SceneListSource source, EditorBuildSettingsScene[] scenes)
    {
        if (source.Kind == BuildSceneSourceKind.ActiveProfile)
        {
            source.Profile.scenes = scenes;
            EditorUtility.SetDirty(source.Profile);
        }
        else
        {
            EditorBuildSettings.scenes = scenes;
        }
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            PrepareDirectDevBootIfNeeded();
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            RestorePendingChanges();
        }
    }

    private static void PrepareDirectDevBootIfNeeded()
    {
        if (!string.IsNullOrEmpty(SessionState.GetString(SnapshotSessionKey, string.Empty)))
        {
            return; // 툴바 Launch가 이미 Play 진입 전에 준비했다.
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.Equals(activeScene.path, DevBootScenePath, StringComparison.OrdinalIgnoreCase))
        {
            return; // 정식 Play 흐름은 건드리지 않는다.
        }

        string targetScenePath = DevBootTarget.ScenePath;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(targetScenePath) == null)
        {
            Debug.LogError($"[DevBoot] 마지막 타겟 씬 에셋을 찾지 못해 빌드 목록을 준비하지 못했다: {targetScenePath}");
            return;
        }

        SceneListSource source = ResolveSceneListSource();
        if (source.Kind == BuildSceneSourceKind.ActiveProfile && string.IsNullOrEmpty(source.ProfileAssetPath))
        {
            Debug.LogError("[DevBoot] 활성 빌드 프로필의 에셋 경로를 찾지 못해 직접 Play용 목록을 준비하지 못했다.");
            return;
        }

        BuildSceneAdjustment adjustment = EnsureSceneEnabled(source.Scenes, targetScenePath);
        SaveSnapshot(source, adjustment.Changed);
        if (adjustment.Changed)
        {
            WriteScenes(source, adjustment.Scenes);
            AssetDatabase.SaveAssets();
        }
    }

    private static SceneAsset LoadDevBootScene()
    {
        string path = AssetDatabase.GUIDToAssetPath(DevBootSceneGuid);
        if (string.IsNullOrEmpty(path))
        {
            path = DevBootScenePathFallback;
        }

        return AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
    }

    private static void SaveSnapshot(SceneListSource source, bool listChanged)
    {
        var snapshot = new BuildSceneSnapshot
        {
            sourceKind = source.Kind,
            profileAssetPath = source.ProfileAssetPath,
            listChanged = listChanged,
            scenes = source.Scenes.Select(scene => new SceneEntry
            {
                path = scene.path,
                enabled = scene.enabled,
            }).ToArray(),
        };

        SessionState.SetString(SnapshotSessionKey, JsonUtility.ToJson(snapshot));
    }

    private static bool RestorePendingChanges()
    {
        string json = SessionState.GetString(SnapshotSessionKey, string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            EditorSceneManager.playModeStartScene = null;
            return false;
        }

        BuildSceneSnapshot snapshot = JsonUtility.FromJson<BuildSceneSnapshot>(json);
        if (snapshot == null)
        {
            Debug.LogError("[DevBoot] 빌드 씬 목록 스냅샷을 읽지 못했다. 강제 정리 메뉴를 사용할 것.");
            return false;
        }

        if (snapshot.listChanged && !RestoreScenes(snapshot))
        {
            EditorSceneManager.playModeStartScene = null;
            return false;
        }

        SessionState.EraseString(SnapshotSessionKey);
        EditorSceneManager.playModeStartScene = null;
        AssetDatabase.SaveAssets();
        return true;
    }

    private static bool RestoreScenes(BuildSceneSnapshot snapshot)
    {
        EditorBuildSettingsScene[] scenes = (snapshot.scenes ?? Array.Empty<SceneEntry>())
            .Select(entry => new EditorBuildSettingsScene(entry.path, entry.enabled))
            .ToArray();

        if (snapshot.sourceKind == BuildSceneSourceKind.ActiveProfile)
        {
            BuildProfile profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(snapshot.profileAssetPath);
            if (profile == null)
            {
                Debug.LogError($"[DevBoot] 원본 빌드 프로필을 찾지 못해 복원하지 못했다: {snapshot.profileAssetPath}");
                return false;
            }

            profile.scenes = scenes;
            EditorUtility.SetDirty(profile);
        }
        else
        {
            EditorBuildSettings.scenes = scenes;
        }

        return true;
    }

    private static EditorBuildSettingsScene[] CloneScenes(IEnumerable<EditorBuildSettingsScene> scenes)
    {
        return scenes.Select(scene => new EditorBuildSettingsScene(scene.path, scene.enabled)).ToArray();
    }

    private static EditorBuildSettingsScene[] RemoveScenes(
        IEnumerable<EditorBuildSettingsScene> scenes,
        IEnumerable<string> pathsToRemove)
    {
        HashSet<string> removals = new HashSet<string>(pathsToRemove, StringComparer.OrdinalIgnoreCase);
        return scenes.Where(scene => !removals.Contains(scene.path)).ToArray();
    }
}
