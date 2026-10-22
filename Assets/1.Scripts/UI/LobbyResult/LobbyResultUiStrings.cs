using System.Collections.Generic;

/// <summary>
/// 로비 결과 패널(할 일 48.1) 정적 라벨 키(UI.LobbyResult.*) — <c>LobbyResultPanel.prefab</c> 라벨의 <see cref="LocalizedText"/>.
/// ko = 프리팹 저작 글자(시트에 행이 없을 때 폴백). 5.ResultScene 열 제목(<c>UI.Result.*</c>)과 화면·문구가 달라 키를 나눈다.
/// 저작 메뉴(<c>Tools/UI/Authoring/Lobby Result Panel</c>)와 L3 키 부착 메뉴가 이 목록을 쓴다. <see cref="UiStrings.All"/> 이 모은다.
/// </summary>
public static class LobbyResultUiStrings
{
    private const string Where = "LobbyResultPanel.prefab";

    public static readonly UiString Title = new UiString("UI.LobbyResult.Title", "전투결과", Where + " › Text_Title — 탭 제목");
    public static readonly UiString PlayTimeLabel = new UiString("UI.LobbyResult.PlayTimeLabel", "소요시간  :", Where + " › Text_PlayTimeLabel — 소요 시간 라벨(값 mm:ss 는 옆 칸)");
    public static readonly UiString Close = new UiString("UI.LobbyResult.Close", "닫기", Where + " › Button_Close/Label");

    // 열 제목 — Header_<열>/Label. 순서 = 패널 열 순서
    public static readonly UiString HeaderRank = new UiString("UI.LobbyResult.Header.Rank", "순위", Where + " › Header_Rank/Label");
    public static readonly UiString HeaderPlayer = new UiString("UI.LobbyResult.Header.Player", "플레이어", Where + " › Header_Player/Label");
    public static readonly UiString HeaderDamage = new UiString("UI.LobbyResult.Header.Damage", "가한피해", Where + " › Header_Damage/Label");
    public static readonly UiString HeaderHealing = new UiString("UI.LobbyResult.Header.Healing", "회복량", Where + " › Header_Healing/Label");
    public static readonly UiString HeaderCounters = new UiString("UI.LobbyResult.Header.Counters", "간파 성공", Where + " › Header_Counters/Label");
    public static readonly UiString HeaderDamageTaken = new UiString("UI.LobbyResult.Header.DamageTaken", "입은 피해", Where + " › Header_DamageTaken/Label");
    public static readonly UiString HeaderDeaths = new UiString("UI.LobbyResult.Header.Deaths", "사망", Where + " › Header_Deaths/Label");

    public static readonly IReadOnlyList<UiString> All = new[]
    {
        Title, PlayTimeLabel, Close,
        HeaderRank, HeaderPlayer, HeaderDamage, HeaderHealing, HeaderCounters, HeaderDamageTaken, HeaderDeaths,
    };
}
