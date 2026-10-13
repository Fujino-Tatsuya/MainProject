/// <summary>
/// 튜토리얼 안내 문구 ID. 문구 자체는 <see cref="TutorialGuideTextTable"/> 행에 있다.
/// 캐릭터 변형은 ID 뒤에 <c>_PALADIN</c>/<c>_GUNNER</c>/<c>_ASSASSIN</c> 을 붙인 행 — 상수로 따로 두지 않고
/// <see cref="TutorialGuideText.SelectVariantId"/> 가 고른다(시드용 목록은 <see cref="All"/>).
/// </summary>
public static class TutorialGuideIds
{
    public const string WaitParty = "TUT_WAIT_PARTY";

    public const string S0Welcome = "TUT_S0_WELCOME";
    public const string ZoneClear = "TUT_ZONE_CLEAR";
    public const string TipRCancel = "TUT_TIP_R_CANCEL";
    public const string TimerExpired = "TUT_TIMER_EXPIRED";

    public const string Z1Move = "TUT_Z1_MOVE";
    public const string Z1Dash = "TUT_Z1_DASH";
    public const string Z1DashSub = "TUT_Z1_DASH_SUB";
    public const string Z1Attack = "TUT_Z1_ATTACK";
    public const string Z1Passive = "TUT_Z1_PASSIVE";
    public const string Z1Go = "TUT_Z1_GO";

    public const string Z2Intro = "TUT_Z2_INTRO";
    public const string Z2Goal = "TUT_Z2_GOAL";
    public const string Z2Nudge = "TUT_Z2_NUDGE";
    public const string Z2Q = "TUT_Z2_Q";
    public const string Z2E = "TUT_Z2_E";

    public const string Z3Intro = "TUT_Z3_INTRO";
    public const string Z3PracticeNote = "TUT_Z3_PRACTICE_NOTE";
    public const string Z3Goal = "TUT_Z3_GOAL";
    public const string Z3Gunner = "TUT_Z3_GUNNER";
    public const string Z3Miss = "TUT_Z3_MISS";
    public const string Z3Early = "TUT_Z3_EARLY";
    public const string Z3FirstSuccess = "TUT_Z3_FIRST_SUCCESS";
    public const string Z3Hint = "TUT_Z3_HINT";
    public const string Z3Real = "TUT_Z3_REAL";
    public const string Z3Boss = "TUT_Z3_BOSS";

    public const string Z4Lore = "TUT_Z4_LORE";
    public const string Z4TimerHint = "TUT_Z4_TIMER_HINT";
    public const string Z4Start = "TUT_Z4_START";
    public const string Z4PanelIntro = "TUT_Z4_PANEL_INTRO";
    public const string Z4PanelGoal = "TUT_Z4_PANEL_GOAL";
    public const string Z4PanelFirst = "TUT_Z4_PANEL_FIRST";
    public const string Z4Bridge = "TUT_Z4_BRIDGE";
    public const string Z4Midboss = "TUT_Z4_MIDBOSS";
    public const string Z4CounterRemind = "TUT_Z4_COUNTER_REMIND";
    public const string Z4Ult = "TUT_Z4_ULT";
    public const string Z4Clear = "TUT_Z4_CLEAR";
    public const string Z4GoBoss = "TUT_Z4_GO_BOSS";

    public const string BPadWarn = "TUT_B_PAD_WARN";
    public const string BPadCount = "TUT_B_PAD_COUNT";
    /// <summary>23호 정면 ±60° 에서만 간파 1줄(D19).</summary>
    public const string BBossFrontTip = "TUT_B_BOSS_FRONT_TIP";

    /// <summary>
    /// 문구 테이블 시드 행(저작 메뉴가 빠진 행만 추가한다). 변형 행 포함 — 빈 문구도 ID 자리는 만들어 둔다.
    /// </summary>
    public static readonly string[] All =
    {
        S0Welcome, ZoneClear, TipRCancel, TimerExpired, WaitParty,
        Z1Move, Z1Dash, Z1DashSub, Z1Attack, Z1Attack + "_GUNNER",
        Z1Passive, Z1Passive + "_PALADIN", Z1Passive + "_GUNNER", Z1Passive + "_ASSASSIN", Z1Go,
        Z2Intro, Z2Goal, Z2Nudge,
        Z2Q, Z2Q + "_PALADIN", Z2Q + "_GUNNER", Z2Q + "_ASSASSIN",
        Z2E, Z2E + "_PALADIN", Z2E + "_GUNNER", Z2E + "_ASSASSIN",
        Z3Intro, Z3PracticeNote, Z3Goal, Z3Gunner, Z3Miss, Z3Early, Z3FirstSuccess, Z3Hint, Z3Real, Z3Boss,
        Z4Lore, Z4TimerHint, Z4Start, Z4PanelIntro, Z4PanelGoal, Z4PanelFirst, Z4Bridge, Z4Midboss,
        Z4CounterRemind, Z4Ult, Z4Ult + "_ASSASSIN", Z4Clear, Z4GoBoss,
        BPadWarn, BPadCount, BBossFrontTip,
    };
}
