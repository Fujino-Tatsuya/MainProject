using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

/// <summary>
/// Unity 6.3 메인 툴바의 Dev Boot 요소 두 개.
/// 버튼 = 선택된 씬으로 바로 Play, 드롭다운 = 타겟 씬 선택만(실행하지 않는다).
/// </summary>
[InitializeOnLoad]
public static class DevBootToolbar
{
    internal const string LaunchElementPath = "Dev/Dev Boot";
    private const string SceneElementPath = "Dev/Dev Boot Scene";
    private const string SceneRootPrefix = "Assets/0.Scenes/";

    // DevBootToolbarStyle 이 툴바 UI 트리에서 두 요소를 찾는 이름(MainToolbarElement.ussName).
    internal const string LaunchUssName = "devboot-launch";
    internal const string SceneUssName = "devboot-scene";

    static DevBootToolbar()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MainToolbarElement(
        LaunchElementPath,
        defaultDockPosition = MainToolbarDockPosition.Middle,
        defaultDockIndex = 10,
        menuPriority = 100,
        ussName = LaunchUssName)]
    private static MainToolbarElement CreateLaunchButton()
    {
        string targetName = DevBootSceneCatalog.GetDisplayName(DevBootTarget.ScenePath);
        var content = new MainToolbarContent(
            "Dev Boot",
            EditorGUIUtility.IconContent("PlayButton").image as Texture2D,
            $"Dev_Boot를 통해 '{targetName}' 씬으로 Play한다(데이터: {DataSourcePlayMode.Label(DataSourcePlayMode.Current)}). 씬은 오른쪽 드롭다운에서 고른다.");
        return new MainToolbarButton(content, () => DevBootLauncher.Launch(DevBootTarget.ScenePath))
        {
            enabled = DevBootLauncher.CanLaunch,
        };
    }

    [MainToolbarElement(
        SceneElementPath,
        defaultDockPosition = MainToolbarDockPosition.Middle,
        defaultDockIndex = 11,
        menuPriority = 101,
        ussName = SceneUssName)]
    private static MainToolbarElement CreateSceneDropdown()
    {
        string targetName = DevBootSceneCatalog.GetDisplayName(DevBootTarget.ScenePath);
        var content = new MainToolbarContent(
            targetName,
            null,
            "Dev Boot가 실행할 씬을 고른다(실행은 왼쪽 Dev Boot 버튼). 위쪽 = 최근 실행한 씬.");
        return new MainToolbarDropdown(content, ShowSceneDropdown);
    }

    private static void ShowSceneDropdown(Rect buttonRect)
    {
        var menu = new GenericMenu();
        string selectedPath = DevBootTarget.ScenePath;

        IReadOnlyList<string> recentPaths = DevBootSceneCatalog.GetRecentScenePaths();
        if (recentPaths.Count == 0)
        {
            menu.AddDisabledItem(new GUIContent("최근 실행 없음"));
        }
        else
        {
            foreach (string scenePath in recentPaths)
            {
                AddSceneItem(menu, DevBootSceneCatalog.GetDisplayName(scenePath), scenePath, selectedPath);
            }
        }

        menu.AddSeparator(string.Empty);

        IReadOnlyList<string> allPaths = DevBootSceneCatalog.GetAllScenePaths();
        if (allPaths.Count == 0)
        {
            menu.AddDisabledItem(new GUIContent("전체/씬 없음"));
        }
        else
        {
            foreach (string scenePath in allPaths)
            {
                AddSceneItem(menu, "전체/" + GetCatalogMenuPath(scenePath), scenePath, selectedPath);
            }
        }

        menu.DropDown(buttonRect);
    }

    private static void AddSceneItem(GenericMenu menu, string label, string scenePath, string selectedPath)
    {
        bool selected = string.Equals(scenePath, selectedPath, StringComparison.OrdinalIgnoreCase);
        menu.AddItem(new GUIContent(label), selected, () =>
        {
            // 선택만 한다. 최근 목록은 실제로 실행했을 때(DevBootLauncher.Launch) 기록된다.
            DevBootTarget.ScenePath = scenePath;
            RefreshAll();
        });
    }

    private static string GetCatalogMenuPath(string scenePath)
    {
        string relative = scenePath.StartsWith(SceneRootPrefix, StringComparison.OrdinalIgnoreCase)
            ? scenePath.Substring(SceneRootPrefix.Length)
            : scenePath;
        return Path.ChangeExtension(relative, null).Replace('\\', '/');
    }

    private static void RefreshAll()
    {
        MainToolbar.Refresh(LaunchElementPath);
        MainToolbar.Refresh(SceneElementPath);
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange _)
    {
        RefreshAll();
    }
}
