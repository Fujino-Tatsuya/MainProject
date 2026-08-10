using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 개발용 씬 목록 관리. <c>DevSceneBooter</c>가 테스트 씬을 부팅할 수 있게 하려면
/// 그 씬들이 빌드 씬 목록에 <b>enabled</b> 상태로 있어야 한다 — 런타임 <c>LoadScene</c>은
/// 비활성 씬을 로드하지 못한다.
///
/// 팀원이 클론한 직후에도 같은 상태를 만들 수 있도록 메뉴로 노출한다.
/// (에디터 전용 폴더라 플레이어 빌드에는 포함되지 않는다.)
///
/// ⚠️ <c>BuildWindowsPlayer.Build()</c>는 EditorBuildSettings의 enabled 씬을 그대로 플레이어에 담는다.
/// 즉 이 메뉴를 쓰면 테스트 씬도 제출 빌드에 실린다. 빌드 스크립트 쪽 정리는 은희가 별도로 처리한다.
/// </summary>
public static class DevBuildSceneList
{
    private const string MenuRoot = "Dev/빌드 씬 목록/";

    /// <summary>Dev 부팅 씬이 있는 폴더. 이 아래 씬은 빌드 목록에 있어선 안 된다.</summary>
    private const string DevBootSceneFolder = "Assets/0.Scenes/Dev/";

    /// <summary>DevSceneBooter로 부팅하고 싶은 개발·테스트 씬.</summary>
    private static readonly string[] DevScenes =
    {
        "Assets/0.Scenes/BossScene.unity",
        "Assets/0.Scenes/MonsterScene.unity",
        "Assets/0.Scenes/PlayerScene.unity",
        "Assets/0.Scenes/PlayerBossTest.unity",
        "Assets/0.Scenes/PlayerDashTest.unity",
        "Assets/0.Scenes/CamaraScene.unity",
    };

    [MenuItem(MenuRoot + "테스트 씬 활성화 (DevSceneBooter 용)")]
    public static void EnableDevScenes()
    {
        SetDevScenesEnabled(true);
    }

    [MenuItem(MenuRoot + "테스트 씬 비활성화 (제출 빌드 전)")]
    public static void DisableDevScenes()
    {
        SetDevScenesEnabled(false);
    }

    /// <summary>
    /// 부팅 씬은 빌드에 들어가면 안 된다. 그런데 에디터가 Play 시 열려 있던 씬을 빌드 목록에 자동 추가하는
    /// 경우가 있어(실측: Dev_Boot 이 buildIndex 12 로 등록됐다) 주기적으로 걷어낼 수단이 필요하다.
    /// </summary>
    [MenuItem(MenuRoot + "Dev 부팅 씬을 목록에서 제거")]
    public static void RemoveDevBootScene()
    {
        var kept = EditorBuildSettings.scenes.Where(s => !s.path.StartsWith(DevBootSceneFolder)).ToArray();
        int removed = EditorBuildSettings.scenes.Length - kept.Length;
        if (removed == 0)
        {
            Debug.Log($"[DevBuildSceneList] 제거 대상 없음 — '{DevBootSceneFolder}' 아래 씬이 목록에 없다.");
            return;
        }

        EditorBuildSettings.scenes = kept;
        Debug.Log($"[DevBuildSceneList] 부팅 씬 {removed}건 제거.");
        LogCurrentList();
    }

    [MenuItem(MenuRoot + "현재 목록 출력")]
    public static void LogCurrentList()
    {
        var lines = EditorBuildSettings.scenes.Select(
            (s, i) => $"  [{i}] enabled={(s.enabled ? "1" : "0")} {s.path}");
        Debug.Log($"[DevBuildSceneList] 빌드 씬 목록:\n{string.Join("\n", lines)}");
    }

    private static void SetDevScenesEnabled(bool enabled)
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
        var changed = new List<string>();
        var missing = new List<string>();

        foreach (string path in DevScenes)
        {
            if (AssetDatabase.AssetPathToGUID(path) == string.Empty)
            {
                missing.Add(path);
                continue;
            }

            int index = scenes.FindIndex(s => s.path == path);
            if (index < 0)
            {
                if (!enabled)
                {
                    continue; // 목록에 없으면 비활성화할 것도 없다.
                }

                scenes.Add(new EditorBuildSettingsScene(path, true));
                changed.Add($"추가+활성 {path}");
                continue;
            }

            if (scenes[index].enabled == enabled)
            {
                continue;
            }

            scenes[index].enabled = enabled;
            changed.Add($"{(enabled ? "활성" : "비활성")} {path}");
        }

        if (missing.Count > 0)
        {
            Debug.LogWarning($"[DevBuildSceneList] 씬 에셋을 찾지 못했다(목록에서 제외):\n  {string.Join("\n  ", missing)}");
        }

        if (changed.Count == 0)
        {
            Debug.Log($"[DevBuildSceneList] 변경 없음 — 이미 전부 {(enabled ? "활성" : "비활성")} 상태다.");
            return;
        }

        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log($"[DevBuildSceneList] {changed.Count}건 변경:\n  {string.Join("\n  ", changed)}");
        LogCurrentList();
    }
}
