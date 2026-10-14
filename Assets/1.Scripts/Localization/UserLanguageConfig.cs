using UnityEngine;

/// <summary>
/// 로컬 유저의 표시 언어 설정. 네트워크에 실리지 않는다 — 각 클라가 자기 언어로 본다.
/// 저장은 PlayerPrefs(<see cref="PrefsKey"/> = "ko"/"en"). 저장값이 없으면 OS 언어가 한국어면 ko, 아니면 en.
/// 옵션 UI = 설정창(SettingsBody) 게임플레이 탭의 언어 버튼(<c>GameplaySettingsController</c>). 바꿀 땐 <see cref="Loc.Current"/> 를 쓴다(Changed 이벤트가 나간다).
/// 옛 설정창 언어 시스템이 같은 키에 정수(0 = English, 1 = 한국어)로 저장했다 — 처음 읽을 때 한 번 문자열로 옮긴다(<see cref="LegacyIndexToLanguage"/>).
/// 패턴은 <see cref="UserInputConfig"/> 를 따랐다.
/// </summary>
public static class UserLanguageConfig
{
    public const string PrefsKey = "Settings.Language";

    private const int NoLegacyValue = int.MinValue;

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
            Save(value);
        }
    }

    /// <summary>저장값이 없을 때의 언어 — 한국어 OS 면 ko, 그 밖은 en.</summary>
    public static GameLanguage DefaultFor(SystemLanguage systemLanguage) =>
        systemLanguage == SystemLanguage.Korean ? GameLanguage.Ko : GameLanguage.En;

    /// <summary>옛 정수 저장값(0 = English, 1 = 한국어) → 언어. 그 밖의 값이면 false.</summary>
    public static bool LegacyIndexToLanguage(int index, out GameLanguage legacy)
    {
        switch (index)
        {
            case 0:
                legacy = GameLanguage.En;
                return true;
            case 1:
                legacy = GameLanguage.Ko;
                return true;
            default:
                legacy = GameLanguage.Ko;
                return false;
        }
    }

    private static void EnsureLoaded()
    {
        if (isLoaded)
            return;

        isLoaded = true;
        if (LocText.TryParse(PlayerPrefs.GetString(PrefsKey, string.Empty), out GameLanguage saved))
        {
            language = saved;
            return;
        }

        // 정수로 저장된 키는 GetString 이 기본값을 준다 — 그때만 정수로 읽어 문자열로 다시 저장한다(한 번).
        if (PlayerPrefs.HasKey(PrefsKey) && LegacyIndexToLanguage(PlayerPrefs.GetInt(PrefsKey, NoLegacyValue), out GameLanguage legacy))
        {
            language = legacy;
            Save(legacy);
            return;
        }

        language = DefaultFor(Application.systemLanguage);
    }

    private static void Save(GameLanguage value)
    {
        PlayerPrefs.SetString(PrefsKey, LocText.Code(value));
        PlayerPrefs.Save();
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
