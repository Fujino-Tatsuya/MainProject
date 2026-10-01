#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 이 워크트리에서 Dev Boot가 사용할 타겟과 최근 목록의 EditorPrefs 저장소.
/// 공유 씬이나 ProjectSettings를 개인 선택값의 원본으로 사용하지 않는다.
/// </summary>
public static class DevBootTarget
{
    public const string DefaultScenePath = "Assets/0.Scenes/MainFlow/4.MapScene.unity";

    private const string KeyPrefix = "MainProject.DevBoot.";
    private static readonly string WorkspaceKey = ComputeWorkspaceKey(Application.dataPath);
    private static readonly string TargetScenePathKey = KeyPrefix + WorkspaceKey + ".TargetScenePath";
    private static readonly string RecentScenePathsKey = KeyPrefix + WorkspaceKey + ".RecentScenePaths";
    private static readonly string PlayerPrefabPathKey = KeyPrefix + WorkspaceKey + ".PlayerPrefabPath";

    /// <summary>
    /// Dev Boot 가 스폰할 플레이어 프리팹 경로(개인 선택). 비어 있으면 NetworkManager 기본값.
    /// 캐릭터 선택 UI 가 생기기 전까지의 개발용 경로다(메뉴 Dev/Dev Boot/캐릭터).
    /// </summary>
    public static string PlayerPrefabPath
    {
        get => EditorPrefs.GetString(PlayerPrefabPathKey, string.Empty);
        set => EditorPrefs.SetString(PlayerPrefabPathKey, value ?? string.Empty);
    }

    public static string ScenePath
    {
        get => NormalizeScenePath(EditorPrefs.GetString(TargetScenePathKey, DefaultScenePath));
        set => EditorPrefs.SetString(TargetScenePathKey, NormalizeScenePath(value));
    }

    public static string SceneName => Path.GetFileNameWithoutExtension(ScenePath);

    public static string RecentScenePathsJson
    {
        get => EditorPrefs.GetString(RecentScenePathsKey, string.Empty);
        set => EditorPrefs.SetString(RecentScenePathsKey, value ?? string.Empty);
    }

    private static string NormalizeScenePath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? DefaultScenePath
            : path.Trim().Replace('\\', '/');
    }

    private static string ComputeWorkspaceKey(string dataPath)
    {
        string normalized = (dataPath ?? string.Empty).Replace('\\', '/').ToLowerInvariant();

        // string.GetHashCode는 런타임별 안정성이 보장되지 않는다. FNV-1a로 머신 내 워크트리 키를 고정한다.
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        for (int i = 0; i < normalized.Length; i++)
        {
            hash ^= normalized[i];
            hash *= prime;
        }

        return hash.ToString("x16");
    }
}
#endif
