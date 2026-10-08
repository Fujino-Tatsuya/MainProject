using UnityEngine;

/// <summary>
/// 빌드 실행 해상도 고정(10-07 은희 — 전체화면 2560×1440). 첫 씬 로드 전에 한 번 건다.
/// 전체 화면 창(FullScreenWindow)이라 모니터가 다르면 이 해상도로 그린 뒤 화면에 맞춰 늘린다.
/// 에디터 Play 에는 걸지 않는다(Game 뷰 크기를 바꾸지 않음).
/// </summary>
public static class DisplayResolutionBoot
{
    public const int Width = 2560;
    public const int Height = 1440;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        if (Application.isEditor)
            return;

        Screen.SetResolution(Width, Height, FullScreenMode.FullScreenWindow);
    }
}
