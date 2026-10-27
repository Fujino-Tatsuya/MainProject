using System.Collections.Generic;

/// <summary>로비 캐릭터 선택 패널(할 일 48.2) 문구 — <see cref="CharacterSelectPanel"/> 이 런타임에 쓴다. 캐릭터 이름은 로스터 <c>Character.*.Name</c>.</summary>
public static class CharacterSelectUiStrings
{
    private const string Where = "CharacterSelectPanel.prefab";

    public static readonly UiString LockedNotice = new UiString("UI.CharacterSelect.LockedNotice", "준비를 해제해야 바꿀 수 있습니다", Where + " › Text_Locked — 준비 중 안내(코드)");
    public static readonly UiString CurrentTag = new UiString("UI.CharacterSelect.CurrentTag", "현재", Where + " › 카드 Text_Current — 지금 내 캐릭터 표식(코드)");
    public static readonly UiString Unavailable = new UiString("UI.CharacterSelect.Unavailable", "선택 불가", Where + " › 카드 Text_Unavailable — 로스터 Available 꺼짐(코드)");
    public static readonly UiString Hint = new UiString("UI.CharacterSelect.Hint", "W/S·방향키 이동   Enter 선택   Esc 뒤로", Where + " › Text_Hint — 조작 안내(코드)");
    public static readonly UiString ConfirmButton = new UiString("UI.CharacterSelect.ConfirmButton", "선택", Where + " › Button_Confirm/Label — 선택 버튼(코드, 할 일 109)");
    public static readonly UiString ConfirmKey = new UiString("UI.CharacterSelect.ConfirmKey", "Space", Where + " › Button_Confirm/Text_Key — 선택 버튼 키 안내(코드, 할 일 109)");

    public static readonly IReadOnlyList<UiString> All = new[] { LockedNotice, CurrentTag, Unavailable, Hint, ConfirmButton, ConfirmKey };
}
