using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dev Boot 관련 빌드 씬 목록 점검·복구 메뉴.
/// 임시 타겟 등록과 원복은 <see cref="DevBootLauncher"/>가 담당한다.
/// </summary>
public static class DevBuildSceneList
{
    private const string MenuRoot = "Dev/빌드 씬 목록/";
    // 경로 상수를 박지 않는다 — 씬을 옮기면 조용히 안 맞게 된다. GUID 로 푸는 단일 원본을 쓴다.
    private static string DevBootScenePath => DevBootLauncher.DevBootScenePath;

    [MenuItem(MenuRoot + "Dev 부팅 씬을 목록에서 제거")]
    public static void RemoveDevBootScene()
    {
        DevBootLauncher.SceneListSource source = DevBootLauncher.ResolveSceneListSource();
        EditorBuildSettingsScene[] kept = source.Scenes
            .Where(scene => !string.Equals(scene.path, DevBootScenePath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        int removed = source.Scenes.Length - kept.Length;
        if (removed == 0)
        {
            Debug.Log($"[DevBuildSceneList] 제거 대상 없음 — '{DevBootScenePath}'이 실효 목록에 없다.");
            return;
        }

        DevBootLauncher.WriteScenes(source, kept);
        AssetDatabase.SaveAssets();
        Debug.Log($"[DevBuildSceneList] 부팅 씬 {removed}건 제거.");
        LogCurrentList();
    }

    [MenuItem("Dev/Dev Boot/빌드 목록 강제 정리")]
    public static void ForceCleanup()
    {
        DevBootLauncher.ForceCleanup();
    }

    [MenuItem(MenuRoot + "현재 목록 출력")]
    public static void LogCurrentList()
    {
        DevBootLauncher.SceneListSource source = DevBootLauncher.ResolveSceneListSource();
        var lines = source.Scenes.Select(
            (scene, index) => $"  [{index}] enabled={(scene.enabled ? "1" : "0")} {scene.path}");
        Debug.Log($"[DevBuildSceneList] 빌드 씬 목록 source={source.Kind}:\n{string.Join("\n", lines)}");
    }
}
