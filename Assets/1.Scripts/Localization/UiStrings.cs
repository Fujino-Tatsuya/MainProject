using System.Collections.Generic;
using System.Globalization;

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
    public string Format(IReadOnlyDictionary<string, string> args) => Loc.Format(Key, Ko, args);

    /// <summary>시트에 행이 있으면 현재 언어 문구, 없으면 <paramref name="fallback"/>(직렬화된 라벨 등) — <see cref="Ko"/> 대신 씬·프리팹 값을 폴백으로 쓴다.</summary>
    public string TextOr(string fallback) => Loc.Get(Key, fallback);

    /// <summary><see cref="TextOr"/> + <c>{이름}</c> 치환.</summary>
    public string FormatOr(string fallback, IReadOnlyDictionary<string, string> args) => Loc.Format(Key, fallback, args);
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

    // 어쌔신 HUD — 정적 라벨 + AssassinHUD 가 매 프레임 쓰는 변신 라벨({time} = UnitSeconds 로 만든 남은 시간, 소수 한 자리)
    public static readonly UiString AssassinEnhancedReady = new UiString("UI.AssassinHUD.EnhancedReady", "E 강화 준비", "AssassinHUD.prefab › EnhancedReady");
    public static readonly UiString AssassinTransformRemaining = new UiString("UI.AssassinHUD.TransformRemaining", "변신 {time}", "AssassinHUD — 변신 남은 시간({time} = UI.Unit.Seconds)(코드)");
    public static readonly UiString AssassinTransformReleasable = new UiString("UI.AssassinHUD.TransformReleasable", "변신 {time} · R 해제 가능", "AssassinHUD — 변신 남은 시간·해제 가능({time} = UI.Unit.Seconds)(코드)");
    public static readonly UiString AssassinTransformEndPending = new UiString("UI.AssassinHUD.TransformEndPending", "변신 종료 대기", "AssassinHUD — 변신 종료 대기(코드)");

    // 거너 과열 게이지 — GunnerHeatGauge 가 매 프레임 쓴다({stage} = 0부터 세는 과열 단계)
    public static readonly UiString GunnerHeatStage = new UiString("UI.GunnerHeat.Stage", "과열 {stage}단계", "GunnerHeatGauge — 과열 단계(코드)");
    public static readonly UiString GunnerHeatOverheated = new UiString("UI.GunnerHeat.Overheated", "과열!", "GunnerHeatGauge — 과열(코드)");

    // 상호작용 안내 — InteractPrompt.prefab
    public static readonly UiString InteractPrompt = new UiString("UI.Interact.Prompt", "상호 작용", "InteractPrompt.prefab › InteractText");

    // 머리 위 체력바 이름 — PlayerOverheadHealthBarRule({n} = 1부터 세는 플레이어 번호)
    public static readonly UiString OverheadPlayerLabel = new UiString("UI.Overhead.PlayerLabel", "P{n}", "PlayerOverheadHealthBarRule — 머리 위 이름(코드)");

    // ── L4b — 코드에 박혀 있던 UI 문구 ──

    // 공용 단위 — 초({sec} = 숫자만, 소수 자릿수는 쓰는 곳이 정한다). 초를 보여 주는 문구는 이 키로 숫자를 만든 뒤 {time} 에 넣는다.
    public static readonly UiString UnitSeconds = new UiString("UI.Unit.Seconds", "{sec}초", "공용 — 초 단위(스킬 툴팁 쿨타임·상태이상 남은 시간·어쌔신 변신)");

    // 로딩 화면 — NetworkLoadingScreenView. 팁·준비 문구 ko 는 2.LoadingScene 직렬화 값(행이 없으면 씬 값이 보인다)
    public static readonly UiString LoadingPreparing = new UiString("UI.Loading.Preparing", "Preparing...", "2.LoadingScene — 상태: 씬 로딩(코드)");
    public static readonly UiString LoadingLoading = new UiString("UI.Loading.Loading", "Loading...", "2.LoadingScene — 상태: 게임 로딩(코드)");
    public static readonly UiString LoadingWaitingForPlayers = new UiString("UI.Loading.WaitingForPlayers", "Waiting for players...", "2.LoadingScene — 상태: 플레이어 대기(코드)");
    public static readonly UiString LoadingActivating = new UiString("UI.Loading.Activating", "Starting...", "2.LoadingScene — 상태: 씬 활성화(코드)");
    public static readonly UiString LoadingStandBy = new UiString("UI.Loading.StandBy", "Stand by...", "2.LoadingScene — 상태: 그 밖(코드)");
    public static readonly UiString LoadingTip1 = new UiString("UI.Loading.Tip1", "으아ㅏㅏㅏㅏ", "2.LoadingScene — 팁 1번째(tooltipTable[0]) · 문구 교체 후보");
    public static readonly UiString LoadingTip2 = new UiString("UI.Loading.Tip2", "커피ㅣㅣㅣㅣ", "2.LoadingScene — 팁 2번째(tooltipTable[1]) · 문구 교체 후보");
    public static readonly UiString LoadingTip3 = new UiString("UI.Loading.Tip3", "밥 뭐 먹지....", "2.LoadingScene — 팁 3번째(tooltipTable[2]) · 문구 교체 후보");
    public static readonly UiString LoadingReady1 = new UiString("UI.Loading.Ready1", "Enter...", "2.LoadingScene — 전원 준비 순환 문구 1(readyMessages[0])");
    public static readonly UiString LoadingReady2 = new UiString("UI.Loading.Ready2", "All players are ready.", "2.LoadingScene — 전원 준비 순환 문구 2(readyMessages[1])");
    public static readonly UiString LoadingReady3 = new UiString("UI.Loading.Ready3", "Starting...", "2.LoadingScene — 전원 준비 순환 문구 3(readyMessages[2])");

    /// <summary>로딩 팁 — 순서 = 씬 <c>tooltipTable</c> 칸 순서.</summary>
    public static readonly IReadOnlyList<UiString> LoadingTips = new[] { LoadingTip1, LoadingTip2, LoadingTip3 };

    /// <summary>전원 준비 순환 문구 — 순서 = 씬 <c>readyMessages</c> 칸 순서.</summary>
    public static readonly IReadOnlyList<UiString> LoadingReadyMessages = new[] { LoadingReady1, LoadingReady2, LoadingReady3 };

    // 스킬 툴팁 — SkillTooltipView(CombatHUD). 쿨타임 숫자는 UnitSeconds
    public static readonly UiString SkillTooltipShiftHint = new UiString("UI.SkillTooltip.ShiftHint", "자세한 정보를 보려면 [Shift] 키를 누르세요", "CombatHUD › SkillTooltip — Shift 상세 안내(코드)");
    public static readonly UiString SkillTooltipError = new UiString("UI.SkillTooltip.Error", "[오류: {error}]", "CombatHUD › SkillTooltip — 툴팁 문구 오류({error} = 포매터 오류, 개발용)");
    public static readonly UiString SkillTooltipPassiveBadge = new UiString("UI.SkillTooltip.PassiveBadge", "Passive", "CombatHUD › SkillTooltip — 패시브 칸 키 배지(코드)");

    // 결과 화면 — ResultStatsView·ResultPlayerRowView. 행이 없으면 씬·프리팹 직렬화 라벨이 보인다
    public static readonly UiString ResultCleared = new UiString("UI.Result.Cleared", "CLEAR", "5.ResultScene › Text_Outcome — 클리어(코드)");
    public static readonly UiString ResultFailed = new UiString("UI.Result.Failed", "FAILED", "5.ResultScene › Text_Outcome — 실패(코드)");
    public static readonly UiString ResultAborted = new UiString("UI.Result.Aborted", "ABORTED", "5.ResultScene › Text_Outcome — 중단(코드)");
    public static readonly UiString ResultSurvival = new UiString("UI.Result.Survival", "생존 시간  {time}", "5.ResultScene › Text_Survival({time} = mm:ss)");
    public static readonly UiString ResultPlayerSlot = new UiString("UI.Result.PlayerSlot", "P{n}", "ResultPlayerRow.prefab › Text_Slot({n} = 1부터 세는 플레이어 번호)");

    // 허수아비 체력 — TrainingDummyHealthBar
    public static readonly UiString TrainingDummyHp = new UiString("UI.TrainingDummy.Hp", "{hp} / {max}", "TrainingDummy.prefab — 머리 위 체력({hp} = 현재, {max} = 최대)");

    // 상태이상 이름 — StatusEffectHUD·머리 위 라벨. 키 = UI.StatusEffect.<enum 이름>.
    // ko = 차단류 CC 6종(BlockedCrowdControl)은 아래 한국어(할 일 50 확정), 나머지는 enum 이름에서 Modifier 를 뺀 것
    private static readonly Dictionary<StatusEffectType, string> StatusEffectHints = new Dictionary<StatusEffectType, string>
    {
        [StatusEffectType.Airborne] = "공중에 뜸",
        [StatusEffectType.Stunned] = "기절",
        [StatusEffectType.Slowed] = "둔화",
        [StatusEffectType.Rooted] = "속박",
        [StatusEffectType.Silenced] = "침묵",
        [StatusEffectType.Debilitated] = "약화",
        [StatusEffectType.SuperArmor] = "슈퍼아머",
        [StatusEffectType.MoveSpeedModifier] = "이동 속도 증감",
        [StatusEffectType.AttackDamageModifier] = "공격력 증감",
        [StatusEffectType.AttackSpeedModifier] = "공격 속도 증감",
        [StatusEffectType.DefenseModifier] = "방어력 증감",
        [StatusEffectType.MaxHpModifier] = "최대 체력 증감",
        [StatusEffectType.PassiveCharge] = "패시브 충전",
        [StatusEffectType.Focus] = "정신 집중",
    };

    private static readonly Dictionary<StatusEffectType, UiString> StatusEffectNames = BuildStatusEffectNames();

    public const string StatusEffectKeyPrefix = "UI.StatusEffect.";

    // 붙잡힘(구속 — 잡기·돌진 밀기) — 상태이상 enum 이 아니라 따로 둔다. 머리 위 라벨만 쓴다(할 일 50)
    public static readonly UiString StatusEffectGrabbed = new UiString(StatusEffectKeyPrefix + "Grabbed", "붙잡힘", "PlayerOverheadHealthBarRule — 머리 위 붙잡힘(구속)(코드)");

    /// <summary>상태이상 하나의 표시 이름 키. <c>None</c>·조합 값처럼 단일 이름이 아니면 false.</summary>
    public static bool TryGetStatusEffect(StatusEffectType type, out UiString name) => StatusEffectNames.TryGetValue(type, out name);

    private static Dictionary<StatusEffectType, UiString> BuildStatusEffectNames()
    {
        var names = new Dictionary<StatusEffectType, UiString>();
        foreach (StatusEffectType type in (StatusEffectType[])System.Enum.GetValues(typeof(StatusEffectType)))
        {
            if (type == StatusEffectType.None)
                continue;

            string enumName = type.ToString();
            string where = "StatusEffectHUD — 상태이상 이름(코드)";
            if (StatusEffectHints.TryGetValue(type, out string hint))
                where += " · " + hint;
            string ko = (type & StatusEffectImmunityPolicy.BlockedCrowdControl) != 0 && hint != null
                ? hint
                : enumName.Replace("Modifier", string.Empty);
            names[type] = new UiString(StatusEffectKeyPrefix + enumName, ko, where);
        }

        return names;
    }

    public static readonly IReadOnlyList<UiString> All = BuildAll();

    private static IReadOnlyList<UiString> BuildAll()
    {
        var all = new List<UiString>
        {
            ResultCharacter, ResultDamage, ResultKills, ResultCounters, ResultDeaths,
            AssassinEnhancedReady, AssassinTransformRemaining, AssassinTransformReleasable, AssassinTransformEndPending,
            GunnerHeatStage, GunnerHeatOverheated,
            InteractPrompt,
            OverheadPlayerLabel,
            UnitSeconds,
            LoadingPreparing, LoadingLoading, LoadingWaitingForPlayers, LoadingActivating, LoadingStandBy,
            SkillTooltipShiftHint, SkillTooltipError, SkillTooltipPassiveBadge,
            ResultCleared, ResultFailed, ResultAborted, ResultSurvival, ResultPlayerSlot,
            TrainingDummyHp,
        };
        all.AddRange(LoadingTips);
        all.AddRange(LoadingReadyMessages);
        all.AddRange(StatusEffectNames.Values);
        all.Add(StatusEffectGrabbed);
        all.AddRange(SettingsUiStrings.All);   // 설정창(UI.Settings.*) — 목록은 Settings 쪽 한 곳
        all.AddRange(DemoConnectUiStrings.All); // 부스 데모 연결 오버레이(UI.DemoConnect.*)
        return all;
    }

    /// <summary>초 단위 표기 — <see cref="UnitSeconds"/>({sec} = <paramref name="numberFormat"/> 로 만든 숫자, 소수점은 항상 '.').</summary>
    public static string Seconds(float seconds, string numberFormat) =>
        UnitSeconds.Format(new Dictionary<string, string> { ["sec"] = seconds.ToString(numberFormat, CultureInfo.InvariantCulture) });
}
