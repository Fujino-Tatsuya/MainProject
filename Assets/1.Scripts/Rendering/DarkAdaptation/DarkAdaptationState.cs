using UnityEngine;

/// <summary>
/// <see cref="InterruptDarkAdaptation"/> → <see cref="DarkAdaptationFeature"/> 값 통로. 할 일 87 C3.
/// 강도가 0 이면 렌더러 기능이 패스를 넣지 않는다.
/// </summary>
public static class DarkAdaptationState
{
    /// <summary>효과 강도 [0, 1]. 0 = 꺼짐.</summary>
    public static float Intensity;
    /// <summary>이 휘도(HDR)보다 어두운 픽셀만 어둡힌다.</summary>
    public static float LuminanceThreshold = 0.8f;
    /// <summary>임계 아래로 이만큼 부드럽게 섞는다.</summary>
    public static float ThresholdSoftness = 0.3f;
    /// <summary>강도 1 에서 어두운 픽셀을 낮추는 최대 비율 [0, 1].</summary>
    public static float MaxDarken = 0.85f;
    /// <summary>원형 중심(뷰포트 0~1).</summary>
    public static Vector2 RevealCenter = new Vector2(0.5f, 0.5f);
    /// <summary>원형 반경(화면 높이 비율). 0 = 원형 끔.</summary>
    public static float RevealRadius;
    /// <summary>원형 가장자리 부드러움(화면 높이 비율).</summary>
    public static float RevealSoftness = 0.1f;

    public static void Clear()
    {
        Intensity = 0f;
        RevealRadius = 0f;
    }

    // 도메인 리로드를 끈 Play 진입에서도 이전 값이 남지 않게.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad() => Clear();
}
