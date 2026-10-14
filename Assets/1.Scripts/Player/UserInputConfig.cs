using UnityEngine;

/// <summary>
/// 로컬 유저의 조작 취향 설정. 네트워크에 실리지 않는다 — 각 클라가 자기 값만 읽고,
/// 서버는 이 설정의 존재를 모른다(종료 신호는 기존 릴리즈 경로로 동일하게 도착한다).
/// 저장은 PlayerPrefs. 옵션 UI = TitleOptionsPanel Controls 탭의 HoldSkillToggleOption·DashDirectionOption.
/// 이 프로퍼티들이 설정의 단일 창구다.
/// </summary>
public static class UserInputConfig
{
    private const string HoldSkillAsTogglePrefsKey = "Input.HoldSkillAsToggle";
    private const string DashDirectionModePrefsKey = "Input.DashDirectionMode";

    private static bool isLoaded;
    private static bool holdSkillAsToggle;
    private static DashDirectionMode dashDirectionMode;

    /// <summary>
    /// Hold 스킬(현재 Q 진격의 방패)을 토글로 조작한다.
    /// false(기본) = 키를 누르고 있는 동안 유지, 떼면 종료.
    /// true = 눌러서 진입, 다시 누르거나 지속시간(MaxActiveDuration)이 끝나면 종료.
    /// 스킬의 설계값(지속시간·쿨타임·피해·전진속도)은 두 방식이 완전히 같다 — 조작만 바뀐다.
    /// </summary>
    public static bool HoldSkillAsToggle
    {
        get
        {
            EnsureLoaded();
            return holdSkillAsToggle;
        }
        set
        {
            EnsureLoaded();

            if (holdSkillAsToggle == value)
                return;

            holdSkillAsToggle = value;
            PlayerPrefs.SetInt(HoldSkillAsTogglePrefsKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// 대시가 나가는 방향. 기본 = <see cref="DashDirectionMode.CharacterFacing"/>(이동 입력 → 캐릭터 정면).
    /// 오너만 읽는다 — 방향은 오너가 계산해 대시 요청 RPC 로 보내므로 서버·다른 클라는 이 값을 몰라도 된다.
    /// 선택 규칙 = <see cref="DashDirectionRules"/>.
    /// </summary>
    public static DashDirectionMode DashDirection
    {
        get
        {
            EnsureLoaded();
            return dashDirectionMode;
        }
        set
        {
            EnsureLoaded();

            if (dashDirectionMode == value)
                return;

            dashDirectionMode = value;
            PlayerPrefs.SetInt(DashDirectionModePrefsKey, (int)value);
            PlayerPrefs.Save();
        }
    }

    private static void EnsureLoaded()
    {
        if (isLoaded)
            return;

        isLoaded = true;
        holdSkillAsToggle = PlayerPrefs.GetInt(HoldSkillAsTogglePrefsKey, 0) != 0;

        // 모르는 값(이후 버전에서 지운 모드 등)은 기본값으로 읽는다.
        int storedDashMode = PlayerPrefs.GetInt(DashDirectionModePrefsKey, (int)DashDirectionMode.CharacterFacing);
        dashDirectionMode = System.Enum.IsDefined(typeof(DashDirectionMode), storedDashMode)
            ? (DashDirectionMode)storedDashMode
            : DashDirectionMode.CharacterFacing;
    }

    // "도메인 리로드 없이 플레이 모드 진입"에서는 static 값이 세션을 넘어 살아남는다.
    // 캐시를 버려 다음 조회가 PlayerPrefs를 다시 읽게 한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        isLoaded = false;
        holdSkillAsToggle = false;
        dashDirectionMode = DashDirectionMode.CharacterFacing;
    }
}

/// <summary>대시 방향 옵션. PlayerPrefs 에 int 로 저장되므로 값을 바꾸지 말 것.</summary>
public enum DashDirectionMode
{
    /// <summary>이동 입력 방향, 입력이 없으면 캐릭터 정면(기본).</summary>
    CharacterFacing = 0,
    /// <summary>마우스 커서(조준) 방향. 조준이 없을 때만 이동 입력 → 정면.</summary>
    Cursor = 1,
}
