using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

/// <summary>Unity 6.3 메인 툴바의 독립 Dev Boot 드롭다운.</summary>
[InitializeOnLoad]
public static class DevBootToolbar
{
    private const string ElementPath = "Dev/Dev Boot";
    private const string SceneRootPrefix = "Assets/0.Scenes/";

    static DevBootToolbar()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MainToolbarElement(
        ElementPath,
        defaultDockPosition = MainToolbarDockPosition.Middle,
        defaultDockIndex = 10,
        menuPriority = 100)]
    private static MainToolbarElement CreateDropdown()
    {
        string targetName = DevBootSceneCatalog.GetDisplayName(DevBootTarget.ScenePath);
        var content = new MainToolbarContent(
            "Dev Boot",
            null,
            $"Dev_Boot를 통해 선택한 씬으로 Play한다. 현재 타겟: {targetName}");
        return new MainToolbarDropdown(content, ShowDropdown)
        {
            enabled = DevBootLauncher.CanLaunch,
        };
    }

    private static void ShowDropdown(Rect buttonRect)
    {
        var menu = new GenericMenu();
        if (!DevBootLauncher.CanLaunch)
        {
            menu.AddDisabledItem(new GUIContent("Play 실행 중"));
            menu.DropDown(buttonRect);
            return;
        }

        string selectedPath = DevBootTarget.ScenePath;
        IReadOnlyList<string> recentPaths = DevBootSceneCatalog.GetRecentScenePaths();
        if (recentPaths.Count == 0)
        {
            menu.AddDisabledItem(new GUIContent("최근 사용 없음"));
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
            DevBootLauncher.Launch(scenePath);
            MainToolbar.Refresh(ElementPath);
        });
    }

    private static string GetCatalogMenuPath(string scenePath)
    {
        string relative = scenePath.StartsWith(SceneRootPrefix, StringComparison.OrdinalIgnoreCase)
            ? scenePath.Substring(SceneRootPrefix.Length)
            : scenePath;
        return Path.ChangeExtension(relative, null).Replace('\\', '/');
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange _)
    {
        MainToolbar.Refresh(ElementPath);
    }
}
