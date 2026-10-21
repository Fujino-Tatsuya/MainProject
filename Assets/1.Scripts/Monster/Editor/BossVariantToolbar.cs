using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

/// <summary>
/// 툴바 "보스: 기존 ▾ / 레이드 ▾" — 보스방에서 스폰할 23호를 고른다(PLAN-boss-raid.md §3, 팀장 10-14 "툴바 토글이면 충분").
/// 선택만 저장하고 적용은 <see cref="BossEncounterDirector"/> 가 스폰 때 한다. 빌드는 항상 기존.
/// </summary>
[InitializeOnLoad]
public static class BossVariantToolbar
{
    const string ElementPath = "Dev/Boss Variant";
    const string UssName = "boss-variant";

    static BossVariantToolbar()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MainToolbarElement(
        ElementPath,
        defaultDockPosition = MainToolbarDockPosition.Middle,
        defaultDockIndex = 4,
        menuPriority = 104,
        ussName = UssName)]
    static MainToolbarElement CreateDropdown()
    {
        var content = new MainToolbarContent(
            "보스: " + (BossVariantSelection.UseRaid ? "레이드" : "기존"),
            null,
            "보스방에서 스폰할 23호. 기존 = TwentyThree / 레이드 = TwentyThree_Raid(비교용 변형). 빌드는 항상 기존.");
        return new MainToolbarDropdown(content, ShowDropdown);
    }

    static void ShowDropdown(Rect buttonRect)
    {
        var menu = new GenericMenu();
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            menu.AddDisabledItem(new GUIContent("Play 중에는 바꿀 수 없다"));
            menu.DropDown(buttonRect);
            return;
        }

        menu.AddItem(new GUIContent("기존 (TwentyThree)"), !BossVariantSelection.UseRaid, () => Set(false));
        menu.AddItem(new GUIContent("레이드 (TwentyThree_Raid)"), BossVariantSelection.UseRaid, () => Set(true));
        menu.AddSeparator(string.Empty);
        menu.AddItem(new GUIContent("레이드 변형 만들기·맞추기 (멱등)"), false,
            () => EditorApplication.ExecuteMenuItem(BossRaidVariantAuthoring.BuildMenu));
        menu.DropDown(buttonRect);
    }

    static void Set(bool raid)
    {
        BossVariantSelection.UseRaid = raid;
        MainToolbar.Refresh(ElementPath);
    }

    static void OnPlayModeStateChanged(PlayModeStateChange _) => MainToolbar.Refresh(ElementPath);
}
