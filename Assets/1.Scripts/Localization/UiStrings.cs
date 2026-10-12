using System.Collections.Generic;

/// <summary>
/// 코드가 아는 UI 문구 키 하나(로컬라이징 L3). <see cref="Ko"/> = 지금 화면 문구 — Export 때 Strings 시트 ko 로 들어가고,
/// 시트에 행이 아직 없을 때 화면에 나오는 폴백이다(<c>#키</c> 를 내지 않는다).
/// </summary>
public readonly struct UiString
{
    public UiString(string key, string ko, string where)
    {
        Key = key;
        Ko = ko;
        Where = where;
    }

    public string Key { get; }
    public string Ko { get; }

    /// <summary>쓰는 곳 — Export 때 Strings 시트 <c>#설명</c> 칸.</summary>
    public string Where { get; }

    /// <summary>현재 언어 문구. 시트에 행이 없으면 <see cref="Ko"/>.</summary>
    public string Text => Loc.Get(Key, Ko);

    /// <summary><see cref="Text"/> + <c>{이름}</c> 치환(<see cref="Loc.Format"/> 과 같은 규칙).</summary>
    public string Format(IReadOnlyDictionary<string, string> args) => TutorialGuideText.Fill(Text, args);
}

/// <summary>
/// L3 — 씬·프리팹 정적 라벨(<see cref="LocalizedText"/>)과 런타임 포맷 문구의 키 목록. Export 는 <see cref="All"/> 을 Strings 시트에 모은다.
/// 정적 라벨은 저작 메뉴 <c>Tools/Localization/L3 — 씬·프리팹 라벨 키 부착</c> 이 LocalizedText 를 붙인다.
/// </summary>
public static class UiStrings
{
    // 결과 화면 — 5.ResultScene › PlayerRowsHeader
    public static readonly UiString ResultCharacter = new UiString("UI.Result.Character", "캐릭터", "5.ResultScene › Header_Character");
    public static readonly UiString ResultDamage = new UiString("UI.Result.Damage", "데미지", "5.ResultScene › Header_Damage");
    public static readonly UiString ResultKills = new UiString("UI.Result.Kills", "처치", "5.ResultScene › Header_Kills");
    public static readonly UiString ResultCounters = new UiString("UI.Result.Counters", "간파", "5.ResultScene › Header_Counters");
    public static readonly UiString ResultDeaths = new UiString("UI.Result.Deaths", "사망", "5.ResultScene › Header_Deaths");

    // 어쌔신 HUD — 정적 라벨 + AssassinHUD 가 매 프레임 쓰는 변신 라벨({sec} = 남은 초, 소수 한 자리)
    public static readonly UiString AssassinEnhancedReady = new UiString("UI.AssassinHUD.EnhancedReady", "E 강화 준비", "AssassinHUD.prefab › EnhancedReady");
    public static readonly UiString AssassinTransformRemaining = new UiString("UI.AssassinHUD.TransformRemaining", "변신 {sec}초", "AssassinHUD — 변신 남은 시간(코드)");
    public static readonly UiString AssassinTransformReleasable = new UiString("UI.AssassinHUD.TransformReleasable", "변신 {sec}초 · R 해제 가능", "AssassinHUD — 변신 남은 시간·해제 가능(코드)");
    public static readonly UiString AssassinTransformEndPending = new UiString("UI.AssassinHUD.TransformEndPending", "변신 종료 대기", "AssassinHUD — 변신 종료 대기(코드)");

    // 거너 과열 게이지 — GunnerHeatGauge 가 매 프레임 쓴다({stage} = 0부터 세는 과열 단계)
    public static readonly UiString GunnerHeatStage = new UiString("UI.GunnerHeat.Stage", "과열 {stage}단계", "GunnerHeatGauge — 과열 단계(코드)");
    public static readonly UiString GunnerHeatOverheated = new UiString("UI.GunnerHeat.Overheated", "과열!", "GunnerHeatGauge — 과열(코드)");

    // 상호작용 안내 — InteractPrompt.prefab
    public static readonly UiString InteractPrompt = new UiString("UI.Interact.Prompt", "상호 작용", "InteractPrompt.prefab › InteractText");

    // 머리 위 체력바 이름 — PlayerOverheadHealthBarRule({n} = 1부터 세는 플레이어 번호)
    public static readonly UiString OverheadPlayerLabel = new UiString("UI.Overhead.PlayerLabel", "P{n}", "PlayerOverheadHealthBarRule — 머리 위 이름(코드)");

    // 타이틀 옵션 — Gameplay › LanguageOption › LanguageLabel(언어 이름 한국어/English 는 번역하지 않는다)
    public static readonly UiString OptionsLanguage = new UiString("UI.Options.Language", "Language", "1.TitleScene › LanguageOption/LanguageLabel");

    public static readonly IReadOnlyList<UiString> All = new[]
    {
        ResultCharacter, ResultDamage, ResultKills, ResultCounters, ResultDeaths,
        AssassinEnhancedReady, AssassinTransformRemaining, AssassinTransformReleasable, AssassinTransformEndPending,
        GunnerHeatStage, GunnerHeatOverheated,
        InteractPrompt,
        OverheadPlayerLabel,
        OptionsLanguage,
    };
}
