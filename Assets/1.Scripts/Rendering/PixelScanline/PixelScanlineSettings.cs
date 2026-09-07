using UnityEngine;

// 블러 마스크와 무관하게 월드 화면 전체에 적용하는 픽셀레이트·스캔라인 설정.
// 모든 크기 값은 UV 비율이 아니라 현재 렌더 타깃의 픽셀 단위다.
[CreateAssetMenu(
    fileName = "PixelScanlineSettings",
    menuName = "Rendering/Pixel Scanline Settings")]
public sealed class PixelScanlineSettings : ScriptableObject
{
    [Header("On / Off")]
    [Tooltip("끄면 렌더 패스가 통째로 빠진다(비용 0).")]
    public bool enabled = true;

    [Header("픽셀레이트")]
    [Tooltip("월드 화면 전체 픽셀레이트 활성 여부. UI는 이 패스 뒤에 그려져 제외된다.")]
    public bool pixelateEnabled = true;

    [Tooltip("픽셀 블록 한 변의 크기(렌더 픽셀). 해상도가 바뀌어도 이 픽셀 수를 유지한다.")]
    [Range(1, 128)] public int pixelBlockSize = 8;

    [Header("스캔라인")]
    [Tooltip("화면 V 좌표만 사용하는 가로 스캔라인 활성 여부.")]
    public bool scanlineEnabled = true;

    [Tooltip("색 띠 한 줄의 두께(렌더 픽셀). 픽셀 블록 크기와 독립적이다.")]
    [Range(1, 64)] public int scanlineThicknessPx = 2;

    [Tooltip("색 띠와 다음 색 띠 사이의 원본 화면 간격(렌더 픽셀). 두께와 독립적이다.")]
    [Range(0, 128)] public int scanlineSpacingPx = 2;

    [Tooltip("스캔라인 RGB 색상. Alpha는 사용하지 않으며 아래 불투명도로 강도를 정한다.")]
    [ColorUsage(false, false)] public Color scanlineColor = Color.black;

    [Tooltip("스캔라인 색 적용 강도. 0=원본, 1=지정 색.")]
    [Range(0f, 1f)] public float scanlineOpacity = 0.2f;

    public bool HasActiveEffect =>
        enabled &&
        (pixelateEnabled || (scanlineEnabled && scanlineOpacity > 0f));

    private void OnValidate()
    {
        pixelBlockSize = Mathf.Max(1, pixelBlockSize);
        scanlineThicknessPx = Mathf.Max(1, scanlineThicknessPx);
        scanlineSpacingPx = Mathf.Max(0, scanlineSpacingPx);
        scanlineOpacity = Mathf.Clamp01(scanlineOpacity);
    }
}
