using System;
using UnityEngine;

/// <summary>표시 언어(PLAN-settings S15·S16). 지금은 설정창 문구만 바뀐다.</summary>
public enum GameLanguage
{
    English = 0,
    Korean = 1,
}

/// <summary>
/// 표시 언어 저장·변경 알림. 첫 실행 = 시스템 언어(한국어면 한국어, 그 외 English), 이후 저장된 선택(S16).
/// 문구 자체는 <see cref="SettingsStrings"/>.
/// </summary>
public static class LanguageConfig
{
    const string Key = "Settings.Language";

    /// <summary>언어가 바뀌었다 — <see cref="LocalizedText"/>·설정 컨트롤러가 문구를 다시 채운다.</summary>
    public static event Action Changed;

    public static GameLanguage Current
    {
        get
        {
            if (!PlayerPrefs.HasKey(Key)) return DefaultFor(Application.systemLanguage);
            int v = PlayerPrefs.GetInt(Key);
            return Enum.IsDefined(typeof(GameLanguage), v) ? (GameLanguage)v : DefaultFor(Application.systemLanguage);
        }
    }

    public static GameLanguage DefaultFor(SystemLanguage system) =>
        system == SystemLanguage.Korean ? GameLanguage.Korean : GameLanguage.English;

    public static void Set(GameLanguage language)
    {
        bool changed = language != Current || !PlayerPrefs.HasKey(Key);
        PlayerPrefs.SetInt(Key, (int)language);
        PlayerPrefs.Save();
        if (changed) Changed?.Invoke();
    }

    /// <summary>다음 언어(버튼 한 번 = 한 칸). 언어가 2개뿐이라 English ↔ 한국어 전환이다.</summary>
    public static GameLanguage Next(GameLanguage language)
    {
        int count = Enum.GetValues(typeof(GameLanguage)).Length;
        return (GameLanguage)(((int)language + 1) % count);
    }

    /// <summary>언어 이름은 번역하지 않는다 — 각자 자기 언어로 써야 못 읽는 사람도 찾아 돌아온다.</summary>
    public static string NativeName(GameLanguage language) =>
        language == GameLanguage.Korean ? "한국어" : "English";
}
