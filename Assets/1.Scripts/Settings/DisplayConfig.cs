using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 해상도·창 모드·프레임 제한·VSync — 로컬 그래픽 설정의 단일 창구(PLAN-settings.md S2~S6·S13).
/// 저장은 PlayerPrefs(<see cref="UserInputConfig"/> 와 같은 방식). 네트워크와 무관하다.
///
/// 🔴 해상도는 "첫 실행만 2560×1440, 이후엔 저장된 선택"이다(S3). 예전 <c>DisplayResolutionBoot</c> 는
///    실행마다 2560×1440 을 강제해 옵션이 있어도 다음 실행에 덮였다.
/// 후보 계산(<see cref="Candidates"/>·<see cref="Resolve"/>)은 모니터 크기를 인자로 받는 순수 함수라 EditMode 로 검증한다.
/// </summary>
public static class DisplayConfig
{
    public const int DefaultFrameLimit = 144;
    public static readonly int[] FrameLimitOptions = { 60, 120, 144, 0 };   // 0 = 무제한

    const string ResolutionKey = "Settings.Display.Resolution";
    const string WindowModeKey = "Settings.Display.WindowMode";
    const string FrameLimitKey = "Settings.Display.FrameLimit";
    const string VSyncKey = "Settings.Display.VSync";

    static bool _loaded;
    static DisplaySelection _current;

    /// <summary>지금 저장된(적용된) 선택. 첫 실행이면 모니터에 맞춘 기본값.</summary>
    public static DisplaySelection Current
    {
        get { EnsureLoaded(); return _current; }
    }

    /// <summary>모니터 네이티브 해상도(주 모니터). 창 모드에서도 바탕화면 크기를 준다.</summary>
    public static Vector2Int NativeSize
    {
        get
        {
            Display d = Display.main;
            int w = d != null && d.systemWidth > 0 ? d.systemWidth : Screen.currentResolution.width;
            int h = d != null && d.systemHeight > 0 ? d.systemHeight : Screen.currentResolution.height;
            return new Vector2Int(Mathf.Max(1, w), Mathf.Max(1, h));
        }
    }

    // ─── 순수 규칙 (EditMode 테스트 대상) ─────────────────────────────

    /// <summary>
    /// 이 모니터에서 보여 줄 해상도 후보. 네이티브보다 큰 권장 후보는 숨긴다(S13). "모니터 기본"은 항상 있다.
    /// </summary>
    public static List<ResolutionChoice> Candidates(Vector2Int native)
    {
        var list = new List<ResolutionChoice>(3);
        if (Fits(ResolutionChoice.Recommended2560x1440, native)) list.Add(ResolutionChoice.Recommended2560x1440);
        if (Fits(ResolutionChoice.Recommended1920x1080, native)) list.Add(ResolutionChoice.Recommended1920x1080);
        list.Add(ResolutionChoice.MonitorNative);
        return list;
    }

    /// <summary>첫 실행 기본값 — 2560×1440(S3), 모니터가 그보다 작으면 모니터 기본(S13).</summary>
    public static ResolutionChoice DefaultResolution(Vector2Int native) =>
        Fits(ResolutionChoice.Recommended2560x1440, native) ? ResolutionChoice.Recommended2560x1440 : ResolutionChoice.MonitorNative;

    /// <summary>선택을 실제 픽셀 크기로. 모니터에 안 맞는 선택(저장 뒤 모니터가 바뀐 경우)은 모니터 기본으로 내린다.</summary>
    public static Vector2Int Resolve(ResolutionChoice choice, Vector2Int native)
    {
        if (!Fits(choice, native)) return native;
        switch (choice)
        {
            case ResolutionChoice.Recommended2560x1440: return new Vector2Int(2560, 1440);
            case ResolutionChoice.Recommended1920x1080: return new Vector2Int(1920, 1080);
            default: return native;
        }
    }

    public static bool Fits(ResolutionChoice choice, Vector2Int native)
    {
        switch (choice)
        {
            case ResolutionChoice.Recommended2560x1440: return native.x >= 2560 && native.y >= 1440;
            case ResolutionChoice.Recommended1920x1080: return native.x >= 1920 && native.y >= 1080;
            default: return true;
        }
    }

    /// <summary>저장값 → 유효한 프레임 제한. 목록에 없는 값은 기본(144).</summary>
    public static int SanitizeFrameLimit(int value)
    {
        for (int i = 0; i < FrameLimitOptions.Length; i++)
            if (FrameLimitOptions[i] == value) return value;
        return DefaultFrameLimit;
    }

    public static FullScreenMode ToUnity(WindowModeChoice mode)
    {
        switch (mode)
        {
            case WindowModeChoice.Fullscreen: return FullScreenMode.ExclusiveFullScreen;
            case WindowModeChoice.Windowed: return FullScreenMode.Windowed;
            default: return FullScreenMode.FullScreenWindow;
        }
    }

    // ─── 저장·적용 ────────────────────────────────────────────────────

    /// <summary>선택을 저장하고 적용한다. 확인 창의 "되돌림"도 이전 값으로 이걸 부른다.</summary>
    public static void Set(DisplaySelection selection)
    {
        EnsureLoaded();
        _current = Sanitize(selection);
        PlayerPrefs.SetInt(ResolutionKey, (int)_current.resolution);
        PlayerPrefs.SetInt(WindowModeKey, (int)_current.windowMode);
        PlayerPrefs.SetInt(FrameLimitKey, _current.frameLimit);
        PlayerPrefs.SetInt(VSyncKey, _current.vSync ? 1 : 0);
        PlayerPrefs.Save();
        Apply(_current);
    }

    /// <summary>해상도·창 모드가 바뀌는가 — 바뀌면 UI 가 10초 확인 창을 띄운다(S5).</summary>
    public static bool ChangesScreen(DisplaySelection a, DisplaySelection b) =>
        a.resolution != b.resolution || a.windowMode != b.windowMode;

    public static void Apply(DisplaySelection s)
    {
        // VSync 를 켜면 targetFrameRate 는 무시된다 — 상한은 끈다(S6).
        QualitySettings.vSyncCount = s.vSync ? 1 : 0;
        FrameRateCap.Apply(s.vSync ? 0 : s.frameLimit);

        // 에디터 Play 에는 해상도를 걸지 않는다(Game 뷰 크기를 바꾸지 않음 — 예전 DisplayResolutionBoot 와 같은 규칙).
        if (Application.isEditor) return;
        Vector2Int size = Resolve(s.resolution, NativeSize);
        Screen.SetResolution(size.x, size.y, ToUnity(s.windowMode));
    }

    static DisplaySelection Sanitize(DisplaySelection s)
    {
        if (!System.Enum.IsDefined(typeof(ResolutionChoice), s.resolution)) s.resolution = DefaultResolution(NativeSize);
        if (!System.Enum.IsDefined(typeof(WindowModeChoice), s.windowMode)) s.windowMode = WindowModeChoice.Borderless;
        s.frameLimit = SanitizeFrameLimit(s.frameLimit);
        return s;
    }

    static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        Vector2Int native = NativeSize;
        _current = Sanitize(new DisplaySelection
        {
            resolution = (ResolutionChoice)PlayerPrefs.GetInt(ResolutionKey, (int)DefaultResolution(native)),
            windowMode = (WindowModeChoice)PlayerPrefs.GetInt(WindowModeKey, (int)WindowModeChoice.Borderless),
            frameLimit = PlayerPrefs.GetInt(FrameLimitKey, DefaultFrameLimit),
            vSync = PlayerPrefs.GetInt(VSyncKey, 0) != 0,
        });
    }

    // 첫 씬 로드 전에 저장값(없으면 기본값)을 건다. 예전 DisplayResolutionBoot·FrameRateCap.Init 자리.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplyAtBoot() => Apply(Current);

    // 도메인 리로드 없는 Play 진입에서 static 이 남는다 — 다음 조회가 PlayerPrefs 를 다시 읽게.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache() => _loaded = false;
}

/// <summary>그래픽 설정 한 벌. 확인 창이 "이전 값"으로 들고 있다가 되돌린다.</summary>
public struct DisplaySelection
{
    public ResolutionChoice resolution;
    public WindowModeChoice windowMode;
    public int frameLimit;   // 0 = 무제한
    public bool vSync;
}

/// <summary>해상도 후보. PlayerPrefs 에 int 로 저장되므로 값을 바꾸지 말 것.</summary>
public enum ResolutionChoice
{
    Recommended2560x1440 = 0,
    Recommended1920x1080 = 1,
    MonitorNative = 2,
}

/// <summary>창 모드(S4). PlayerPrefs 에 int 로 저장되므로 값을 바꾸지 말 것.</summary>
public enum WindowModeChoice
{
    Fullscreen = 0,   // 독점 전체화면
    Borderless = 1,   // 테두리 없는 전체 창(기본 — 지금까지의 동작)
    Windowed = 2,
}
