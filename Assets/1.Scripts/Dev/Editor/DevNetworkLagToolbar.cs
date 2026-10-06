using System.Linq;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

/// <summary>
/// 툴바 [지연: …] 드롭다운 — MPPM 인스턴스별 네트워크 지연을 한 번에 고른다(<see cref="DevNetworkLagRunner"/>).
/// 고른 값은 다음 Play 부터 적용된다(Play 중 바꾸면 그 판에는 반영 안 됨 — 각 인스턴스가 시작할 때 한 번 읽는다).
/// 보통: Main Editor(호스트) = 없음, 가상 플레이어(클라) = 핫스팟. MPPM 태그 `Lag250_100_3` 이 있으면 태그가 우선.
/// </summary>
[InitializeOnLoad]
public static class DevNetworkLagToolbar
{
    const string ElementPath = "Dev/Network Lag";

    static DevNetworkLagToolbar()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange _) => MainToolbar.Refresh(ElementPath);

    [MainToolbarElement(
        ElementPath,
        defaultDockPosition = MainToolbarDockPosition.Middle,
        defaultDockIndex = 12,
        menuPriority = 102)]
    static MainToolbarElement Create()
    {
        var d = DevNetworkLagSettings.Load();
        var content = new MainToolbarContent(
            Summary(d),
            EditorGUIUtility.IconContent("d_Profiler.NetworkMessages").image as Texture2D,
            "MPPM 인스턴스별 네트워크 지연(Network Simulator). 다음 Play 부터 적용.\n" +
            string.Join("\n", Enumerable.Range(0, DevNetworkLagSettings.SlotCount).Select(i => $"{SlotName(i + 1)}: {d.slots[i]}")) +
            $"\n진단 로그 [LagDiag]: {(d.diagnostics ? "켬" : "끔")}");
        return new MainToolbarDropdown(content, ShowMenu);
    }

    static string SlotName(int slot) => slot == 1 ? "Main Editor(호스트)" : $"Player {slot}";

    static string Summary(DevNetworkLagSettings.Data d)
    {
        if (d.slots.All(s => s.IsNone)) return "지연: 없음";
        var v = d.slots.Skip(1).ToArray();
        if (d.slots[0].IsNone && v.All(s => s.Equals(v[0]))) return $"지연: 클라 {v[0].delayMs}ms";
        return "지연: 칸별";
    }

    static void ShowMenu(Rect rect)
    {
        var d = DevNetworkLagSettings.Load();
        var menu = new UnityEditor.GenericMenu();

        foreach (var (name, lag) in DevNetworkLagSettings.Presets)
        {
            bool on = d.slots[0].IsNone && d.slots.Skip(1).All(s => s.Equals(lag));
            menu.AddItem(new GUIContent("가상 플레이어 전체 (Player 2~4)/" + name), on, () => Apply(x =>
            {
                x.slots[0] = default;
                for (int i = 1; i < DevNetworkLagSettings.SlotCount; i++) x.slots[i] = lag;
            }));
        }

        for (int slot = 1; slot <= DevNetworkLagSettings.SlotCount; slot++)
        {
            int idx = slot - 1;
            foreach (var (name, lag) in DevNetworkLagSettings.Presets)
            {
                menu.AddItem(new GUIContent($"칸별/{SlotName(slot)}/{name}"), d.slots[idx].Equals(lag),
                    () => Apply(x => x.slots[idx] = lag));
            }
        }

        menu.AddSeparator(string.Empty);
        menu.AddItem(new GUIContent("진단 로그 [LagDiag] (맵 진입 후 30초, 씬별 몬스터 수)"), d.diagnostics,
            () => Apply(x => x.diagnostics = !x.diagnostics));
        menu.AddItem(new GUIContent("모두 없음으로"), false, () => Apply(x =>
        {
            for (int i = 0; i < DevNetworkLagSettings.SlotCount; i++) x.slots[i] = default;
        }));
        menu.AddSeparator(string.Empty);
        menu.AddDisabledItem(new GUIContent("MPPM 태그로도 지정 가능: Lag250 · Lag250_100 · Lag250_100_3 · NoLag (태그 우선)"));

        menu.DropDown(rect);
    }

    // ───────── 메뉴바 Dev/네트워크 지연 ─────────
    // Unity 6.3 툴바는 새 요소를 숨긴 채 시작할 수 있다(툴바 우클릭 → Dev/Network Lag 체크) → 메뉴로도 같은 기능을 둔다.
    // 칸별 세부 지정은 툴바 드롭다운 또는 MPPM 태그(Lag250_100_3)로.
    const string Menu = "Dev/네트워크 지연 (MPPM)/";

    [MenuItem(Menu + "클라 전체 = 없음", false, 1)] static void M0() => SetClients(0);
    [MenuItem(Menu + "클라 전체 = 집 와이파이 (32ms ±12 · 2%)", false, 2)] static void M1() => SetClients(1);
    [MenuItem(Menu + "클라 전체 = 핫스팟 (250ms ±100 · 3%)", false, 3)] static void M2() => SetClients(2);
    [MenuItem(Menu + "클라 전체 = 혼잡한 핫스팟 (400ms ±200 · 5%)", false, 4)] static void M3() => SetClients(3);
    [MenuItem(Menu + "클라 전체 = 모바일 2.5G (480ms ±40 · 7%)", false, 5)] static void M4() => SetClients(4);

    [MenuItem(Menu + "클라 전체 = 없음", true)] static bool V0() => Check(0);
    [MenuItem(Menu + "클라 전체 = 집 와이파이 (32ms ±12 · 2%)", true)] static bool V1() => Check(1);
    [MenuItem(Menu + "클라 전체 = 핫스팟 (250ms ±100 · 3%)", true)] static bool V2() => Check(2);
    [MenuItem(Menu + "클라 전체 = 혼잡한 핫스팟 (400ms ±200 · 5%)", true)] static bool V3() => Check(3);
    [MenuItem(Menu + "클라 전체 = 모바일 2.5G (480ms ±40 · 7%)", true)] static bool V4() => Check(4);

    const string DiagMenu = Menu + "진단 로그 [LagDiag]";
    [MenuItem(DiagMenu, false, 20)] static void ToggleDiag() => Apply(x => x.diagnostics = !x.diagnostics);
    [MenuItem(DiagMenu, true)] static bool ToggleDiagV() { Menu_SetChecked(DiagMenu, DevNetworkLagSettings.Load().diagnostics); return true; }

    static void SetClients(int preset) => Apply(x =>
    {
        x.slots[0] = default;   // Main Editor = 호스트 = 지연 없음
        for (int i = 1; i < DevNetworkLagSettings.SlotCount; i++) x.slots[i] = DevNetworkLagSettings.Presets[preset].lag;
    });

    static readonly string[] PresetMenus =
    {
        Menu + "클라 전체 = 없음",
        Menu + "클라 전체 = 집 와이파이 (32ms ±12 · 2%)",
        Menu + "클라 전체 = 핫스팟 (250ms ±100 · 3%)",
        Menu + "클라 전체 = 혼잡한 핫스팟 (400ms ±200 · 5%)",
        Menu + "클라 전체 = 모바일 2.5G (480ms ±40 · 7%)",
    };

    static bool Check(int preset)
    {
        var d = DevNetworkLagSettings.Load();
        var lag = DevNetworkLagSettings.Presets[preset].lag;
        Menu_SetChecked(PresetMenus[preset], d.slots[0].IsNone && d.slots.Skip(1).All(s => s.Equals(lag)));
        return !EditorApplication.isPlayingOrWillChangePlaymode;   // Play 중엔 바꿔도 그 판에 반영 안 되므로 막는다
    }

    static void Menu_SetChecked(string path, bool on) => UnityEditor.Menu.SetChecked(path, on);

    static void Apply(System.Action<DevNetworkLagSettings.Data> change)
    {
        var d = DevNetworkLagSettings.Load();
        change(d);
        DevNetworkLagSettings.Save(d);
        MainToolbar.Refresh(ElementPath);
        Debug.Log("[NetLag] 설정 저장 — 다음 Play 부터: " +
                  string.Join(" / ", Enumerable.Range(0, DevNetworkLagSettings.SlotCount).Select(i => $"{SlotName(i + 1)} {d.slots[i]}")));
    }
}
