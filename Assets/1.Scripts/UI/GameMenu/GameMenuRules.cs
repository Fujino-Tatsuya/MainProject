/// <summary>설정창·ESC 메뉴가 어느 씬 규칙으로 동작하는가(PLAN-settings-ddol).</summary>
public enum GameMenuMode
{
    /// <summary>로딩·데모 연결 대기 등 — 메뉴를 열지 않는다.</summary>
    None,
    /// <summary>타이틀 — ESC 메뉴 없음. 옵션 버튼으로 설정창만(모니터 안).</summary>
    Title,
    /// <summary>로비 — 계속 · 설정 · 그만두기(타이틀 / 바탕화면).</summary>
    Lobby,
    /// <summary>인게임(맵) — 계속 · 설정 · 그만두기(타이틀 / 바탕화면).</summary>
    InGame,
}

/// <summary>메뉴가 지금 보여 주는 화면.</summary>
public enum GameMenuScreen
{
    Closed,
    Menu,
    Settings,
    Confirm,
}

/// <summary>ESC 한 번이 할 일.</summary>
public enum GameMenuEscape
{
    Ignore,
    Open,
    BackToMenu,
    Close,
}

/// <summary>그만두기 팝업 가운데 문구. 팝업은 늘 타이틀 / 바탕화면 두 갈래(경석 10-22).</summary>
public enum GameMenuExitMessage
{
    /// <summary>"정말 종료하시겠습니까?"(클라·오프라인).</summary>
    Default,
    /// <summary>로비 호스트 — 나가면 클라 전원이 "호스트 대기 중"으로 간다.</summary>
    HostDisconnectsOthers,
    /// <summary>인게임 호스트 — 나가면 다른 플레이어의 판도 끝난다.</summary>
    HostEndsOthersGame,
}

/// <summary>
/// 설정창·ESC 메뉴 규칙(순수 — EditMode 테스트 대상). 화면 처리는 <see cref="GameMenuController"/> 가 한다.
/// </summary>
public static class GameMenuRules
{
    public const string TitleSceneName = "1.TitleScene";
    public const string LobbySceneName = "3.LobbyScene";
    public const string MapSceneName = "4.MapScene";

    /// <summary>활성 씬 이름 → 모드. 목록 밖(로딩·데모 연결 대기·결과·부트스트랩)은 None.</summary>
    public static GameMenuMode ResolveMode(string activeSceneName)
    {
        switch (activeSceneName)
        {
            case TitleSceneName: return GameMenuMode.Title;
            case LobbySceneName: return GameMenuMode.Lobby;
            case MapSceneName: return GameMenuMode.InGame;
            default: return GameMenuMode.None;
        }
    }

    /// <summary>
    /// ESC 한 번의 처리. 타이틀은 TitleFlowDirector 가 ESC 를 직접 다룬다(연출 잠금 계약) — 여기선 Ignore.
    /// </summary>
    /// <param name="claimedByOthers">이 프레임 ESC 를 다른 창이 썼다(로비 결과·캐릭터 선택, 스킬 조준 취소).</param>
    /// <param name="rebinding">키 변경 대기 중 — ESC = 변경 취소(KeyRebindRow 가 처리).</param>
    /// <param name="dropdownOpen">이 프레임 시작 때 설정창 드롭다운 목록이 펼쳐져 있었다 — ESC = 목록만 닫기(TMP_Dropdown 이 처리).</param>
    public static GameMenuEscape ResolveEscape(GameMenuMode mode, GameMenuScreen screen, bool claimedByOthers, bool rebinding, bool dropdownOpen = false)
    {
        if (mode == GameMenuMode.None || mode == GameMenuMode.Title || rebinding || dropdownOpen)
            return GameMenuEscape.Ignore;

        switch (screen)
        {
            case GameMenuScreen.Closed:
                return claimedByOthers ? GameMenuEscape.Ignore : GameMenuEscape.Open;
            case GameMenuScreen.Settings:
            case GameMenuScreen.Confirm:
                return GameMenuEscape.BackToMenu;
            default:
                return GameMenuEscape.Close;
        }
    }

    /// <summary>그만두기 팝업 문구. 호스트 = 리슨 서버(다른 플레이어가 이 PC 에 붙어 있다) — 타이틀이든 바탕화면이든 다른 사람도 끊긴다.</summary>
    public static GameMenuExitMessage ResolveExitMessage(GameMenuMode mode, bool isHost)
    {
        if (!isHost) return GameMenuExitMessage.Default;
        return mode == GameMenuMode.InGame ? GameMenuExitMessage.HostEndsOthersGame : GameMenuExitMessage.HostDisconnectsOthers;
    }

    /// <summary>모드에 ESC 메뉴(계속·설정·그만두기)가 있는가. 타이틀은 설정창만.</summary>
    public static bool HasPauseMenu(GameMenuMode mode) => mode == GameMenuMode.Lobby || mode == GameMenuMode.InGame;
}
