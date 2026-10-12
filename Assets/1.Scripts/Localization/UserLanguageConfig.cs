using UnityEngine;

/// <summary>
/// 로컬 유저의 표시 언어 설정. 네트워크에 실리지 않는다 — 각 클라가 자기 언어로 본다.
/// 저장은 PlayerPrefs(<see cref="PrefsKey"/> = "ko"/"en"). 저장값이 없으면 OS 언어가 한국어면 ko, 아니면 en.
/// 옵션 UI = TitleOptionsPanel Gameplay 탭의 LanguageOption. 바꿀 땐 <see cref="Loc.Current"/> 를 쓴다(Changed 이벤트가 나간다).
/// 패턴은 <see cref="UserInputConfig"/> 를 따랐다.
/// </summary>
public static class UserLanguageConfig
{
    public const string PrefsKey = "Settings.Language";

    private static bool isLoaded;
    private static GameLanguage language;

    public static GameLanguage Language
    {
        get
        {
            EnsureLoaded();
            return language;
        }
        set
        {
            EnsureLoaded();

            if (language == value)
                return;

            language = value;
            PlayerPrefs.SetString(PrefsKey, LocText.Code(value));
            PlayerPrefs.Save();
        }
    }

    /// <summary>저장값이 없을 때의 언어 — 한국어 OS 면 ko, 그 밖은 en.</summary>
    public static GameLanguage DefaultFor(SystemLanguage systemLanguage) =>
        systemLanguage == SystemLanguage.Korean ? GameLanguage.Ko : GameLanguage.En;

    private static void EnsureLoaded()
    {
        if (isLoaded)
            return;

        isLoaded = true;
        language = LocText.TryParse(PlayerPrefs.GetString(PrefsKey, string.Empty), out GameLanguage saved)
            ? saved
            : DefaultFor(Application.systemLanguage);
    }

    // "도메인 리로드 없이 플레이 모드 진입"에서는 static 값이 세션을 넘어 살아남는다.
    // 캐시를 버려 다음 조회가 PlayerPrefs를 다시 읽게 한다. 테스트도 저장값을 다시 읽힐 때 부른다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetCache()
    {
        isLoaded = false;
        language = GameLanguage.Ko;
    }
}
