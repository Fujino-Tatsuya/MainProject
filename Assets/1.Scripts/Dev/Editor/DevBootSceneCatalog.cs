using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Dev Boot 드롭다운에 표시할 씬과 워크트리별 최근 사용 목록을 제공한다.</summary>
public static class DevBootSceneCatalog
{
    private const string SceneRoot = "Assets/0.Scenes";
    // 경로 상수를 박지 않는다 — 씬을 옮기면 조용히 안 맞게 된다. GUID 로 푸는 단일 원본을 쓴다.
    private static string DevBootScenePath => DevBootLauncher.DevBootScenePath;
    private const int RecentLimit = 5;

    [Serializable]
    private sealed class RecentSceneData
    {
        public List<string> paths = new List<string>();
    }

    public static IReadOnlyList<string> GetAllScenePaths()
    {
        return AssetDatabase.FindAssets("t:Scene", new[] { SceneRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(IsBootableScenePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> GetRecentScenePaths()
    {
        HashSet<string> available = new HashSet<string>(GetAllScenePaths(), StringComparer.OrdinalIgnoreCase);
        return ReadRecentPaths()
            .Where(available.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(RecentLimit)
            .ToArray();
    }

    public static void RecordRecentScene(string scenePath)
    {
        if (!IsBootableScenePath(scenePath))
        {
            return;
        }

        List<string> paths = ReadRecentPaths()
            .Where(path => !string.Equals(path, scenePath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        paths.Insert(0, scenePath);

        var data = new RecentSceneData { paths = paths.Take(RecentLimit).ToList() };
        DevBootTarget.RecentScenePathsJson = JsonUtility.ToJson(data);
    }

    public static string GetDisplayName(string scenePath)
    {
        return Path.GetFileNameWithoutExtension(scenePath);
    }

    private static IEnumerable<string> ReadRecentPaths()
    {
        string json = DevBootTarget.RecentScenePathsJson;
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            RecentSceneData data = JsonUtility.FromJson<RecentSceneData>(json);
            return data?.paths ?? Enumerable.Empty<string>();
        }
        catch (ArgumentException)
        {
            DevBootTarget.RecentScenePathsJson = string.Empty;
            return Array.Empty<string>();
        }
    }

    private static bool IsBootableScenePath(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath) ||
            !scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scenePath, DevBootScenePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string normalized = scenePath.Replace('\\', '/');
        return normalized.StartsWith(SceneRoot + "/", StringComparison.OrdinalIgnoreCase) &&
               normalized.IndexOf("/Art/", StringComparison.OrdinalIgnoreCase) < 0 &&
               normalized.IndexOf("/Lagacy/", StringComparison.OrdinalIgnoreCase) < 0;
    }
}
