using System.Collections.Generic;

/// <summary>
/// 로비 캐릭터 선택 패널(할 일 48.2) 문구 — <see cref="CharacterSelectPanel"/> 이 런타임에 쓴다.
/// 캐릭터 이름(카드·이름판)은 언어와 무관한 고정 영어 <see cref="CharacterName"/>(할 일 162) — 다른 화면은 그대로 로스터 <c>Character.*.Name</c>.
/// </summary>
public static class CharacterSelectUiStrings
{
    private const string Where = "CharacterSelectPanel.prefab";

    public static readonly UiString LockedNotice = new UiString("UI.CharacterSelect.LockedNotice", "준비를 해제해야 바꿀 수 있습니다", Where + " › Text_Locked — 준비 중 안내(코드)");
    public static readonly UiString CurrentTag = new UiString("UI.CharacterSelect.CurrentTag", "현재", Where + " › 카드 Text_Current — 지금 내 캐릭터 표식(코드)");
    public static readonly UiString Unavailable = new UiString("UI.CharacterSelect.Unavailable", "선택 불가", Where + " › 카드 Text_Unavailable — 로스터 Available 꺼짐(코드)");
    public static readonly UiString Hint = new UiString("UI.CharacterSelect.Hint", "W/S·방향키 이동   Enter 선택   Esc 뒤로", Where + " › Text_Hint — 조작 안내(코드)");

    public static readonly IReadOnlyList<UiString> All = new[] { LockedNotice, CurrentTag, Unavailable, Hint };

    /// <summary>플레이어 프리팹 이름 → 캐릭터 선택 화면 이름(고정 영어, 번역 안 함). 이 표가 유일한 출처.</summary>
    private static readonly Dictionary<string, string> EnglishNames = new Dictionary<string, string>
    {
        { "Player_Paladin", "Paladin" },
        { "Player_Gunner", "Gunner" },
        { "Player_Assassin", "Assassin" },
    };

    /// <summary>캐릭터 선택 화면에 쓸 이름 — 표에 있으면 고정 영어, 모르는 캐릭터면 로스터 <c>DisplayName</c>, 칸이 없으면 빈 문자열.</summary>
    public static string CharacterName(CharacterRoster.Entry entry)
    {
        if (entry == null)
            return string.Empty;
        string prefabName = entry.PlayerPrefab != null ? entry.PlayerPrefab.name : null;
        return prefabName != null && EnglishNames.TryGetValue(prefabName, out string english) ? english : entry.DisplayName;
    }
}
