using System;

/// <summary>
/// 특성 선택 라운드의 표시 옵션(할 일 15 T2). 라운드를 여는 쪽(복도 트리거)이 정하고, 디렉터가 클라까지 복제한다.
/// </summary>
[Flags]
public enum TutorialTraitRoundDisplay : byte
{
    None = 0,

    /// <summary>창 상단 설명(<see cref="TutorialTraitTexts.IntroId"/>) — 복도1.</summary>
    Intro = 1 << 0,

    /// <summary>창 아래 한 줄(<see cref="TutorialTraitTexts.LastNoticeId"/>) — 복도4.</summary>
    LastNotice = 1 << 1,
}

/// <summary>
/// 특성 선택 UI 문구 ID 와 상수 기본값(할 일 15 T2) — Unity 비의존.
/// 실제 표시 문구는 <see cref="TutorialTraitTextTable"/> 에셋이 우선이고, 에셋·행이 없을 때 여기 값으로 폴백한다(T3).
/// </summary>
public static class TutorialTraitTexts
{
    public const string TitleId = "TUT_C1_TRAIT_TITLE";
    public const string IntroId = "TUT_C1_TRAIT_INTRO";
    public const string LastNoticeId = "TUT_C4_TRAIT_LAST";
    public const string PickedId = "TUT_C1_TRAIT_PICKED";

    /// <summary>획득 토스트 <see cref="LocationToken"/> 자리에 들어갈 "다시 보기 위치" 문구.</summary>
    public const string PickedLocationId = "TUT_C1_TRAIT_PICKED_LOCATION";

    /// <summary>모든 문구 ID(문구 테이블 기본 행 순서).</summary>
    public static readonly string[] AllIds = { TitleId, IntroId, LastNoticeId, PickedId, PickedLocationId };

    /// <summary><see cref="PickedId"/> 문구 안에서 다시 보기 UI 위치 문구로 바뀌는 자리.</summary>
    public const string LocationToken = "{location}";

    public const string Title = "특성 선택";

    public const string Intro =
        "특성은 <b><color=#F2C14E>이번 작전 동안만</color></b> 유지되는 강화입니다. " +
        "구역을 돌파할 때마다 복도에서 하나씩 고를 수 있고, 작전이 끝나면 사라집니다.";

    public const string LastNotice = "원흉과 싸우기 전 마지막 특성입니다. 게이지는 계속 차오릅니다.";

    public const string Picked = "특성을 얻었습니다. 획득한 특성은 " + LocationToken + "에서 다시 볼 수 있습니다.";

    public const string PickedLocation = "화면 왼쪽 위";

    /// <summary>문구 ID → 기본 문구. 모르는 ID 는 빈 문자열.</summary>
    public static string Get(string id)
    {
        switch (id)
        {
            case TitleId: return Title;
            case IntroId: return Intro;
            case LastNoticeId: return LastNotice;
            case PickedId: return Picked;
            case PickedLocationId: return PickedLocation;
            default: return string.Empty;
        }
    }

    /// <summary>획득 토스트 — <see cref="LocationToken"/> 을 다시 보기 UI 위치 문구로 채운다.</summary>
    public static string FormatPicked(string template, string location)
    {
        return (template ?? string.Empty).Replace(LocationToken, location ?? string.Empty);
    }
}
