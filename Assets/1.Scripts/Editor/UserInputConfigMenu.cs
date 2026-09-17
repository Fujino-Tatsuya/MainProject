using UnityEditor;

/// <summary>
/// 조작 설정 QA용 메뉴. 옵션 UI(TitleOptionsPanel의 Controls 탭)가 붙기 전까지
/// 에디터에서 Hold/Toggle을 바꿔 보는 유일한 창구다. UI가 생기면 지워도 된다.
/// </summary>
public static class UserInputConfigMenu
{
    private const string HoldSkillAsToggleMenuPath = "Tools/Input/Hold 스킬을 Toggle로 조작";

    [MenuItem(HoldSkillAsToggleMenuPath)]
    private static void ToggleHoldSkillAsToggle()
    {
        UserInputConfig.HoldSkillAsToggle = !UserInputConfig.HoldSkillAsToggle;
    }

    [MenuItem(HoldSkillAsToggleMenuPath, true)]
    private static bool ValidateHoldSkillAsToggle()
    {
        Menu.SetChecked(HoldSkillAsToggleMenuPath, UserInputConfig.HoldSkillAsToggle);
        return true;
    }
}
