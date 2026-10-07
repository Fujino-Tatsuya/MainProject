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
    /// 메인 툴바의 Dev Boot 캐릭터 드롭다운(DevBootToolbar, CharacterRoster 목록)에서 고른다.
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

    /// <summary>
    /// 이 워크트리 전용 EditorPrefs 키. 다른 개인 설정(예: 데이터 테이블의 데이터 출처)도 같은 범위로 저장할 때 쓴다.
    /// MPPM 가상 플레이어(클론)도 메인 에디터와 같은 키가 나온다.
    /// </summary>
    public static string ScopedKey(string name) => KeyPrefix + WorkspaceKey + "." + name;

    public static string ComputeWorkspaceKey(string dataPath)
    {
        string normalized = (dataPath ?? string.Empty).Replace('\\', '/').ToLowerInvariant();

        // MPPM 클론은 <메인>/Library/VP/<mppm…>/Assets(메인 Assets 로 가는 심볼릭 링크)에서 돈다.
        // 클론이 메인과 다른 설정을 읽으면 호스트·클라이언트가 다른 조건으로 돌므로 메인 경로로 접는다.
        int clone = normalized.IndexOf("/library/vp/", StringComparison.Ordinal);
        if (clone >= 0)
        {
            normalized = normalized.Substring(0, clone) + "/assets";
        }

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
