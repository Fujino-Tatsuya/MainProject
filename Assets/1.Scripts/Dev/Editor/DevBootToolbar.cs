using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

/// <summary>
/// Unity 6.3 메인 툴바의 Dev Boot 요소 세 개.
/// 버튼 = 선택된 씬으로 바로 Play, 드롭다운 = 타겟 씬·스폰할 캐릭터 선택만(실행하지 않는다).
/// </summary>
[InitializeOnLoad]
public static class DevBootToolbar
{
    internal const string LaunchElementPath = "Dev/Dev Boot";
    private const string SceneElementPath = "Dev/Dev Boot Scene";
    private const string CharacterElementPath = "Dev/Dev Boot Character";
    private const string SceneRootPrefix = "Assets/0.Scenes/";

    // DevBootToolbarStyle 이 툴바 UI 트리에서 요소를 찾는 이름(MainToolbarElement.ussName).
    internal const string LaunchUssName = "devboot-launch";
    internal const string SceneUssName = "devboot-scene";
    internal const string CharacterUssName = "devboot-character";

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
            $"Dev_Boot를 통해 '{targetName}' 씬으로 Play한다(캐릭터: {DevBootCharacterCatalog.GetDisplayName(DevBootTarget.PlayerPrefabPath)}, " +
            $"데이터: {DataSourcePlayMode.Label(DataSourcePlayMode.Current)}). 씬·캐릭터는 오른쪽 드롭다운에서 고른다.");
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

    [MainToolbarElement(
        CharacterElementPath,
        defaultDockPosition = MainToolbarDockPosition.Middle,
        defaultDockIndex = 12,
        menuPriority = 102,
        ussName = CharacterUssName)]
    private static MainToolbarElement CreateCharacterDropdown()
    {
        var content = new MainToolbarContent(
            "캐릭터: " + DevBootCharacterCatalog.GetDisplayName(DevBootTarget.PlayerPrefabPath),
            null,
            "Dev Boot가 스폰할 캐릭터를 고른다(CharacterRoster 목록, 개인 설정). " +
            "씬의 DevSceneBooter.playerPrefabOverride 가 채워져 있으면 그쪽이 우선한다.");
        return new MainToolbarDropdown(content, ShowCharacterDropdown);
    }

    private static void ShowCharacterDropdown(Rect buttonRect)
    {
        var menu = new GenericMenu();
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            menu.AddDisabledItem(new GUIContent("Play 중에는 바꿀 수 없다"));
            menu.DropDown(buttonRect);
            return;
        }

        string selectedPath = DevBootTarget.PlayerPrefabPath;
        AddCharacterItem(menu, "기본값 (NetworkManager)", string.Empty, string.IsNullOrEmpty(selectedPath));
        menu.AddSeparator(string.Empty);

        IReadOnlyList<DevBootCharacterCatalog.Character> characters = DevBootCharacterCatalog.GetCharacters();
        if (characters.Count == 0)
        {
            menu.AddDisabledItem(new GUIContent("CharacterRoster 없음"));
        }

        foreach (DevBootCharacterCatalog.Character character in characters)
        {
            bool selected = !string.IsNullOrEmpty(selectedPath)
                && string.Equals(character.PrefabPath, selectedPath, StringComparison.OrdinalIgnoreCase);
            if (character.Selectable)
            {
                AddCharacterItem(menu, character.DisplayName, character.PrefabPath, selected);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(character.DisplayName), selected);
            }
        }

        menu.DropDown(buttonRect);
    }

    private static void AddCharacterItem(GenericMenu menu, string label, string prefabPath, bool selected)
    {
        menu.AddItem(new GUIContent(label), selected, () =>
        {
            DevBootTarget.PlayerPrefabPath = prefabPath;
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
        MainToolbar.Refresh(CharacterElementPath);
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange _)
    {
        RefreshAll();
    }
}
