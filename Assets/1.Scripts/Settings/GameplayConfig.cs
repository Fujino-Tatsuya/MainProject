using UnityEngine;

/// <summary>성능 표시 단계(드롭다운 순서와 같다).</summary>
public enum PerformanceDisplay
{
    Off = 0,
    Fps = 1,
    FpsAndPing = 2,
}

/// <summary>
/// 데미지 숫자·화면 흔들림 — 로컬 게임플레이 표시 설정(PLAN-settings.md S8·S9). 저장은 PlayerPrefs.
/// 둘 다 **각 피어 로컬**이다 — 네트워크로 실리지 않고, 내 화면에만 영향을 준다.
/// </summary>
public static class GameplayConfig
{
    const string DamageNumbersKey = "Settings.Gameplay.DamageNumbers";
    const string ScreenShakeKey = "Settings.Gameplay.ScreenShake";

    /// <summary>
    /// 데미지 숫자 표시 방식. 저장값이 없으면 <paramref name="authoredDefault"/>(SO 저작값 — FloatingDamageSettings.asset 은 AllWithOwnEmphasis)를 그대로 쓴다.
    /// 🔴 SO 에셋은 고치지 않는다 — 런타임에 이 값으로 덮어 읽을 뿐이다.
    /// </summary>
    public static FloatingDamageDisplayFilter DamageNumbers(FloatingDamageDisplayFilter authoredDefault)
    {
        if (!PlayerPrefs.HasKey(DamageNumbersKey)) return authoredDefault;
        int v = PlayerPrefs.GetInt(DamageNumbersKey);
        return System.Enum.IsDefined(typeof(FloatingDamageDisplayFilter), v) ? (FloatingDamageDisplayFilter)v : authoredDefault;
    }

    public static void SetDamageNumbers(FloatingDamageDisplayFilter value)
    {
        PlayerPrefs.SetInt(DamageNumbersKey, (int)value);
        PlayerPrefs.Save();
    }

    const string PerformanceKey = "Settings.Gameplay.Performance";

    /// <summary>우측 상단 성능 표시(S17, 기본 끔). <see cref="PerformanceOverlay"/> 가 읽는다.</summary>
    public static PerformanceDisplay Performance
    {
        get
        {
            int v = PlayerPrefs.GetInt(PerformanceKey, (int)PerformanceDisplay.Off);
            return System.Enum.IsDefined(typeof(PerformanceDisplay), v) ? (PerformanceDisplay)v : PerformanceDisplay.Off;
        }
        set
        {
            PlayerPrefs.SetInt(PerformanceKey, (int)value);
            PlayerPrefs.Save();
        }
    }

    /// <summary>화면 흔들림 배율 0~1(기본 1, 0 = 끔). <see cref="CameraFeedback"/> 의 진폭에 곱한다.</summary>
    public static float ScreenShake
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(ScreenShakeKey, 1f));
        set
        {
            PlayerPrefs.SetFloat(ScreenShakeKey, Mathf.Clamp01(value));
            PlayerPrefs.Save();
        }
    }
}
