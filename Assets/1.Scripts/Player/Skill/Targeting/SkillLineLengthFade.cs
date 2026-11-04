using UnityEngine;

/// <summary>
/// 직선 스킬 사각형의 길이 비율 감쇠(할 일 202 — 거너 Q 충전 레이저 조준·Q 미리보기).
///
/// t = 사각형 길이 대비 비율(시전자 쪽 끝 0 → 먼 끝 1).
/// 점 (0,1) → (p.x,a.x) → (p.y,a.y) → (p.z,a.z) → (1,a.w) 을 **단조 3차 곡선**(Fritsch–Butland)으로 부드럽게 잇는다.
/// 점을 정확히 지나고, 점 사이에서 값이 위아래로 튀지 않으며, 점에서 꺾이지 않는다(계단·꺾임 없음).
/// 셰이더 <c>SkillLineIndicator.shader</c> 의 <c>LineLengthFade</c> 와 같은 식 — EditMode 테스트 기준.
///
/// 점 값은 머티리얼 <c>SkillLineRange.mat</c> 칸 <c>_LineFadeT</c>(점 위치)·<c>_LineFadeAlpha</c>(알파, w = 끝)에서 튜닝한다.
/// 켜고 끄는 건 <c>_LineFadeOn</c>, 한 층이 전체 사각형의 어느 구간인지는 <c>_LineFadeSpan</c>(속성 블록, <see cref="Span"/>).
/// </summary>
public static class SkillLineLengthFade
{
    public const string PointsProperty = "_LineFadeT";
    public const string AlphaProperty = "_LineFadeAlpha";
    public const string EnabledProperty = "_LineFadeOn";
    public const string SpanProperty = "_LineFadeSpan";

    public static readonly Vector3 DefaultPoints = new(0.5f, 0.75f, 0.9f);
    public static readonly Vector4 DefaultAlpha = new(0.9f, 0.7f, 0.5f, 0.4f);

    const float MinSpan = 0.0001f;

    public static float Evaluate(float t, Vector3 points, Vector4 alpha)
    {
        t = Mathf.Clamp01(t);
        float[] x = new float[5];
        x[0] = 0f;
        x[1] = Mathf.Clamp01(points.x);
        x[2] = Mathf.Max(Mathf.Clamp01(points.y), x[1]);
        x[3] = Mathf.Max(Mathf.Clamp01(points.z), x[2]);
        x[4] = 1f;
        float[] y = { 1f, alpha.x, alpha.y, alpha.z, alpha.w };

        float[] h = new float[4];
        float[] d = new float[4];
        for (int i = 0; i < 4; i++)
        {
            h[i] = Mathf.Max(x[i + 1] - x[i], MinSpan);
            d[i] = (y[i + 1] - y[i]) / h[i];
        }

        float[] m = new float[5];
        m[0] = d[0];
        m[4] = d[3];
        for (int j = 1; j < 4; j++)
        {
            float d0 = d[j - 1], d1 = d[j];
            m[j] = d0 * d1 <= 0f
                ? 0f
                : 3f * (h[j - 1] + h[j]) / ((2f * h[j] + h[j - 1]) / d0 + (h[j] + 2f * h[j - 1]) / d1);
        }

        float result = y[4];
        for (int k = 3; k >= 0; k--)
            if (t <= x[k + 1])
                result = Hermite(t, x[k], h[k], y[k], y[k + 1], m[k], m[k + 1]);
        return Mathf.Clamp01(result);
    }

    public static float Evaluate(float t) => Evaluate(t, DefaultPoints, DefaultAlpha);

    /// <summary>
    /// 한 층(segment)이 전체 사각형 [start, end](m, 시전자 기준 앞쪽 거리)의 어느 구간인지 —
    /// 셰이더가 t = x + uv.y·y 로 전체 길이 비율을 얻는다. 층이 곧 전체면 (0, 1).
    /// </summary>
    public static Vector4 Span(SkillPreviewSegment segment, float start, float end)
    {
        float total = Mathf.Max(end - start, MinSpan);
        return new Vector4((segment.StartOffset - start) / total, segment.Length / total, 0f, 0f);
    }

    static float Hermite(float t, float x0, float h, float y0, float y1, float m0, float m1)
    {
        float s = Mathf.Clamp01((t - x0) / h);
        float s2 = s * s, s3 = s2 * s;
        return (2f * s3 - 3f * s2 + 1f) * y0 + (s3 - 2f * s2 + s) * h * m0
             + (-2f * s3 + 3f * s2) * y1 + (s3 - s2) * h * m1;
    }
}
