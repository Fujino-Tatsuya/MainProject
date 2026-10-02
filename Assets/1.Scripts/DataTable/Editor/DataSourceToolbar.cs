using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

/// <summary>툴바 "데이터: 테이블 ▾" — 플레이 시작 옵션. 선택만 저장하고 적용은 <see cref="DataSourcePlayMode"/> 가 Play 진입 때 한다.</summary>
[InitializeOnLoad]
public static class DataSourceToolbar
{
    internal const string ElementPath = "Dev/Data Source";
    internal const string UssName = "datatable-source";

    static DataSourceToolbar()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MainToolbarElement(
        ElementPath,
        defaultDockPosition = MainToolbarDockPosition.Middle,
        defaultDockIndex = 12,
        menuPriority = 102,
        ussName = UssName)]
    private static MainToolbarElement CreateDropdown()
    {
        var content = new MainToolbarContent(
            "데이터: " + DataSourcePlayMode.Label(DataSourcePlayMode.Current),
            null,
            "Play 할 때 쓸 수치. 테이블 = xlsx(빌드와 같음) / 인스펙터 = SO·프리팹 값 그대로. 어떤 Play 버튼이든 이 설정을 따른다.");
        return new MainToolbarDropdown(content, ShowDropdown);
    }

    private static void ShowDropdown(Rect buttonRect)
    {
        var menu = new GenericMenu();
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            menu.AddDisabledItem(new GUIContent("Play 중에는 바꿀 수 없다"));
            menu.DropDown(buttonRect);
            return;
        }

        AddItem(menu, DataSource.Table, "테이블 (xlsx · 빌드와 같음)");
        AddItem(menu, DataSource.Inspector, "인스펙터 (SO·프리팹 값 그대로)");
        menu.AddSeparator(string.Empty);
        menu.AddItem(new GUIContent("Verify — SO 와 테이블 차이 보기"), false,
            () => EditorApplication.ExecuteMenuItem("Tools/Data/Verify (SO 와 테이블 차이 보기)"));
        menu.DropDown(buttonRect);
    }

    private static void AddItem(GenericMenu menu, DataSource source, string label)
    {
        menu.AddItem(new GUIContent(label), DataSourcePlayMode.Current == source, () =>
        {
            DataSourcePlayMode.Current = source;
            Refresh();
        });
    }

    private static void Refresh()
    {
        MainToolbar.Refresh(ElementPath);
        MainToolbar.Refresh(DevBootToolbar.LaunchElementPath); // Dev Boot 툴팁에 데이터 출처가 들어 있다
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange _) => MainToolbar.Refresh(ElementPath);
}
