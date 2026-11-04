using UnityEngine;

/// <summary>
/// 직선 스킬 사각형의 길이 비율 감쇠(할 일 202 — 거너 Q 충전 레이저 조준).
///
/// t = 사각형 길이 대비 비율(시전자 쪽 끝 0 → 먼 끝 1, 셰이더의 uv.y).
/// 점 (0,1) → (p.x,a.x) → (p.y,a.y) → (p.z,a.z) → (1,a.w) 을 직선으로 잇는다(구간 지정).
/// 셰이더 <c>SkillLineIndicator.shader</c> 의 <c>LineLengthFade</c> 와 같은 식 — EditMode 테스트 기준.
///
/// 점 값은 머티리얼 <c>SkillLineRange.mat</c> 칸 <c>_LineFadeT</c>(점 위치)·<c>_LineFadeAlpha</c>(알파, w = 끝)에서 튜닝한다.
/// 켜고 끄는 건 <c>_LineFadeOn</c>(속성 블록) — <see cref="SkillLineIndicator.ShowCharge"/> 만 켠다.
/// </summary>
public static class SkillLineLengthFade
{
    public const string PointsProperty = "_LineFadeT";
    public const string AlphaProperty = "_LineFadeAlpha";
    public const string EnabledProperty = "_LineFadeOn";

    public static readonly Vector3 DefaultPoints = new(0.5f, 0.75f, 0.9f);
    public static readonly Vector4 DefaultAlpha = new(0.9f, 0.7f, 0.5f, 0.4f);

    const float MinSpan = 0.0001f;

    public static float Evaluate(float t, Vector3 points, Vector4 alpha)
    {
        t = Mathf.Clamp01(t);
        float p1 = Mathf.Clamp01(points.x);
        float p2 = Mathf.Max(Mathf.Clamp01(points.y), p1);
        float p3 = Mathf.Max(Mathf.Clamp01(points.z), p2);

        float result;
        if (t <= p1) result = Mathf.LerpUnclamped(1f, alpha.x, t / Mathf.Max(p1, MinSpan));
        else if (t <= p2) result = Mathf.LerpUnclamped(alpha.x, alpha.y, (t - p1) / Mathf.Max(p2 - p1, MinSpan));
        else if (t <= p3) result = Mathf.LerpUnclamped(alpha.y, alpha.z, (t - p2) / Mathf.Max(p3 - p2, MinSpan));
        else result = Mathf.LerpUnclamped(alpha.z, alpha.w, (t - p3) / Mathf.Max(1f - p3, MinSpan));
        return Mathf.Clamp01(result);
    }

    public static float Evaluate(float t) => Evaluate(t, DefaultPoints, DefaultAlpha);
}
