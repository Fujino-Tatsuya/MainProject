using UnityEngine;

/// <summary>직선 인디케이터 한 층의 순수 기하 데이터(m).</summary>
public readonly struct SkillPreviewSegment
{
    public SkillPreviewSegment(float startOffset, float length, float width)
    {
        StartOffset = startOffset;
        Length = Mathf.Max(0f, length);
        Width = Mathf.Max(0f, width);
    }

    public float StartOffset { get; }
    public float Length { get; }
    public float Width { get; }
    public bool IsVisible => Length > 0.0001f && Width > 0.0001f;
}

/// <summary>
/// 스킬 직선 미리보기의 순수 결과. Physics 호출은 스킬 구현이 끝낸 뒤 클립 길이만 넘긴다.
/// </summary>
public readonly struct SkillPreviewShape
{
    public SkillPreviewShape(
        SkillPreviewSegment fill,
        SkillPreviewSegment ghost,
        bool hasGhost,
        float arrowDirection,
        float arrowLength,
        bool hasArrow,
        bool lengthFade = false)
    {
        Fill = fill;
        Ghost = ghost;
        HasGhost = hasGhost && ghost.IsVisible;
        ArrowDirection = arrowDirection < 0f ? -1f : 1f;
        ArrowLength = Mathf.Max(0f, arrowLength);
        HasArrow = hasArrow && ArrowLength > 0.0001f;
        LengthFade = lengthFade;
    }

    public SkillPreviewSegment Fill { get; }
    public SkillPreviewSegment Ghost { get; }
    public bool HasFill => Fill.IsVisible;
    public bool HasGhost { get; }
    public float ArrowDirection { get; }
    public float ArrowLength { get; }
    public bool HasArrow { get; }
    /// <summary>202: 채움·고스트를 한 사각형으로 보고 길이 비율로 옅게(거너 Q). <see cref="SkillLineLengthFade"/>.</summary>
    public bool LengthFade { get; }
}

/// <summary>스킬별 표시 모양을 만드는 물리 없는 순수 계산.</summary>
public static class SkillPreviewShapes
{
    public static SkillPreviewShape ChargeLaser(
        float minimumLength,
        float maximumLength,
        float width,
        float clippedMinimumLength,
        float clippedMaximumLength)
    {
        float min = Mathf.Max(0f, minimumLength);
        float max = Mathf.Max(min, maximumLength);
        float clippedMin = Mathf.Clamp(clippedMinimumLength, 0f, min);
        float clippedMax = Mathf.Clamp(clippedMaximumLength, 0f, max);

        return new SkillPreviewShape(
            new SkillPreviewSegment(0f, clippedMin, width),
            new SkillPreviewSegment(0f, clippedMax, width),
            hasGhost: true,
            arrowDirection: 1f,
            arrowLength: 0f,
            hasArrow: false,
            lengthFade: true);
    }

    public static SkillPreviewShape Hitbox(Vector3 localCenter, Vector3 localSize)
    {
        float length = Mathf.Abs(localSize.z);
        float width = Mathf.Abs(localSize.x);
        float startOffset = localCenter.z - length * 0.5f;

        return new SkillPreviewShape(
            new SkillPreviewSegment(startOffset, length, width),
            default,
            hasGhost: false,
            arrowDirection: 1f,
            arrowLength: 0f,
            hasArrow: false);
    }

    public static SkillPreviewShape HitboxWithArrow(
        Vector3 localCenter,
        Vector3 localSize,
        float arrowDirection,
        float arrowLength,
        float clippedArrowLength)
    {
        SkillPreviewShape hitbox = Hitbox(localCenter, localSize);
        float clipped = Mathf.Clamp(clippedArrowLength, 0f, Mathf.Max(0f, arrowLength));

        return new SkillPreviewShape(
            hitbox.Fill,
            default,
            hasGhost: false,
            arrowDirection,
            clipped,
            hasArrow: true);
    }

    /// <param name="lengthFade">202·213: 화살표도 길이 비율 커브·양옆 흰 라인(SkillLineIndicator 칸)으로 그린다 — 어쌔신 대시 일격.</param>
    public static SkillPreviewShape Arrow(float arrowDirection, float arrowLength, float clippedArrowLength, bool lengthFade = false)
    {
        float clipped = Mathf.Clamp(clippedArrowLength, 0f, Mathf.Max(0f, arrowLength));
        return new SkillPreviewShape(
            default,
            default,
            hasGhost: false,
            arrowDirection,
            clipped,
            hasArrow: true,
            lengthFade: lengthFade);
    }
}
