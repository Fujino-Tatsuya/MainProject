using UnityEngine;

/// <summary>
/// <see cref="InterruptDarkAdaptation"/>(휘도)·<see cref="InterruptOwnerDimOutput"/>(마스크) → <see cref="DarkAdaptationFeature"/> 값 통로. 할 일 87 C3·132.
/// 강도가 0 이면 렌더러 기능이 그 모드의 패스를 넣지 않는다.
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

    // ---- 마스크 암전(오너, 할 일 132) — InterruptOwnerDimOutput 이 쓴다 ----

    /// <summary>오너 마스크 암전 강도 [0, 1]. 0 = 꺼짐(마스크 패스 없음).</summary>
    public static float MaskIntensity;
    /// <summary>
    /// 마스크 패스가 덮개 알파를 써 넣을 대상 — 흰색 RGB + 알파 = 1 − 남김. 최상위 오버레이 캔버스(HUD 덮개)가 이것을 화면에 깐다.
    /// 없으면 마스크 패스를 넣지 않는다.
    /// </summary>
    public static RenderTexture MaskCoverTarget;

    /// <summary>휘도 암순응만 끈다(마스크 암전은 오너 출력이 따로 끈다).</summary>
    public static void Clear()
    {
        Intensity = 0f;
        RevealRadius = 0f;
    }

    public static void ClearMask()
    {
        MaskIntensity = 0f;
        MaskCoverTarget = null;
    }

    // ---- 적중 섬광(오너, 할 일 132 §15) — InterruptHitFlashOverlay 가 쓴다 ----

    /// <summary>적중 지점 중심 방사형 블러 세기 [0, 1] — 픽셀에서 중심까지 거리 중 샘플이 덮는 비율. 0 = 꺼짐(패스 없음).</summary>
    public static float HitFlashBlurStrength;
    /// <summary>방사형 블러 중심(뷰포트 0~1).</summary>
    public static Vector2 HitFlashBlurCenter = new Vector2(0.5f, 0.5f);
    /// <summary>
    /// 렌즈 플레어 층 — 켜진 LensFlareComponentSRP 들을 투명 검정 위에 따로 그려 넣을 대상(②). 오버레이 캔버스가 오너 암전 위에 더한다.
    /// 없으면 플레어 층 패스를 넣지 않는다.
    /// </summary>
    public static RenderTexture HitFlashFlareTarget;

    public static void ClearHitFlash()
    {
        HitFlashBlurStrength = 0f;
        HitFlashFlareTarget = null;
    }

    // 도메인 리로드를 끈 Play 진입에서도 이전 값이 남지 않게.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad()
    {
        Clear();
        ClearMask();
        ClearHitFlash();
    }
}

/// <summary><see cref="DarkAdaptationFeature"/> 의 모드(할 일 132) — 한 기능에 두 패스, 강도가 0 인 모드는 넣지 않는다.</summary>
[System.Flags]
public enum DarkAdaptationMode
{
    None = 0,
    /// <summary>휘도 암순응(팀원·기존 A2) — 어두운 픽셀만 낮춘다.</summary>
    Luminance = 1,
    /// <summary>마스크 암전(오너) — 비트 4 렌더러만 남기고 화면 전체(HUD 포함)를 덮는다.</summary>
    Mask = 2,
    /// <summary>적중 섬광 방사형 블러(오너, 할 일 132 §15) — 카메라 컬러(HDR, 후처리 전).</summary>
    RadialBlur = 4,
    /// <summary>적중 섬광 렌즈 플레어 층(오너, 할 일 132 §15) — 플레어만 외부 RT 에.</summary>
    FlareLayer = 8,
}

/// <summary>모드 선택·덮개 색 순수 로직(EditMode 테스트 대상).</summary>
public static class DarkAdaptationModes
{
    /// <summary>강도가 0 보다 큰 모드만. 마스크는 덮개 대상이 있어야 한다. 둘 다 0 이면 None — 패스 없음(평소 비용 0).</summary>
    public static DarkAdaptationMode Select(float luminanceIntensity, float maskIntensity, bool hasMaskTarget)
    {
        DarkAdaptationMode mode = DarkAdaptationMode.None;
        if (luminanceIntensity > 0f)
            mode |= DarkAdaptationMode.Luminance;
        if (maskIntensity > 0f && hasMaskTarget)
            mode |= DarkAdaptationMode.Mask;
        return mode;
    }

    /// <summary>적중 섬광 모드까지 — 블러 세기가 양수면 방사형 블러, 플레어 대상이 있으면 플레어 층. 평소엔 둘 다 없다.</summary>
    public static DarkAdaptationMode Select(float luminanceIntensity, float maskIntensity, bool hasMaskTarget,
        float blurStrength, bool hasFlareTarget)
    {
        DarkAdaptationMode mode = Select(luminanceIntensity, maskIntensity, hasMaskTarget);
        if (blurStrength > 0f)
            mode |= DarkAdaptationMode.RadialBlur;
        if (hasFlareTarget)
            mode |= DarkAdaptationMode.FlareLayer;
        return mode;
    }

    /// <summary>덮개 알파 = 불투명도 × 강도(둘 다 [0, 1]로 자른다).</summary>
    public static float CoverAlpha(float opacity, float intensity) => Mathf.Clamp01(opacity) * Mathf.Clamp01(intensity);

    /// <summary>덮개 색 — RGB 는 칸 색, 알파는 <see cref="CoverAlpha"/>(칸 색의 알파는 쓰지 않는다 — 불투명도 칸 하나로).</summary>
    public static Color CoverColor(Color color, float opacity, float intensity) =>
        new Color(color.r, color.g, color.b, CoverAlpha(opacity, intensity));
}
