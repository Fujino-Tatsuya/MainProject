using UnityEngine;

/// <summary>
/// 직선 스킬 사각형의 길이 비율 감쇠(할 일 202 — 거너 Q 충전 레이저 조준·Q 미리보기).
///
/// t = 사각형 길이 대비 비율(시전자 쪽 끝 0 → 먼 끝 1), y = 진하기 배율.
/// 모양은 <see cref="SkillLineIndicator"/> 의 AnimationCurve 칸(인스펙터 그래프)이 원본이다.
/// 커브를 <see cref="SampleCount"/> 개 고른 간격 샘플로 구워(<see cref="Bake"/>) 속성 블록 배열
/// <c>_LineFadeCurve</c> 로 넘기고, 셰이더 <c>SkillLineIndicator.shader</c> 의 <c>LineLengthFade</c> 가
/// 샘플 사이를 직선 보간한다(<see cref="Sample"/> 와 같은 식 — EditMode 테스트 기준). 텍스처 생성 없음.
///
/// 켜고 끄는 건 <c>_LineFadeOn</c>, 한 층이 전체 사각형의 어느 구간인지는 <c>_LineFadeSpan</c>(<see cref="Span"/>).
/// </summary>
public static class SkillLineLengthFade
{
    public const string CurveProperty = "_LineFadeCurve";
    public const string EnabledProperty = "_LineFadeOn";
    public const string SpanProperty = "_LineFadeSpan";

    /// <summary>셰이더 <c>LINE_FADE_SAMPLES</c> 와 같아야 한다.</summary>
    public const int SampleCount = 32;

    /// <summary>202 기본 모양 — (0,1) (0.5,0.9) (0.75,0.7) (0.9,0.5) (1,0.4) 를 매끄럽게 잇는다.</summary>
    public static AnimationCurve DefaultCurve()
    {
        float[] x = { 0f, 0.5f, 0.75f, 0.9f, 1f };
        float[] y = { 1f, 0.9f, 0.7f, 0.5f, 0.4f };
        var keys = new Keyframe[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            int prev = Mathf.Max(i - 1, 0);
            int next = Mathf.Min(i + 1, x.Length - 1);
            // 이웃 두 점의 기울기 — 점에서 꺾이지 않는 부드러운 접선(에디터 전용 API 없이).
            float tangent = (y[next] - y[prev]) / (x[next] - x[prev]);
            keys[i] = new Keyframe(x[i], y[i], tangent, tangent);
        }
        return new AnimationCurve(keys);
    }

    /// <summary>커브를 고른 간격 <see cref="SampleCount"/> 개로 굽는다(0~1 로 자름). <paramref name="destination"/> 재사용 — 할당 없음.</summary>
    public static void Bake(AnimationCurve curve, float[] destination)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            float t = destination.Length > 1 ? i / (float)(destination.Length - 1) : 0f;
            destination[i] = curve != null && curve.length > 0 ? Mathf.Clamp01(curve.Evaluate(t)) : 1f;
        }
    }

    /// <summary>셰이더 <c>LineLengthFade</c> 와 같은 직선 보간.</summary>
    public static float Sample(float[] samples, float t)
    {
        int last = samples.Length - 1;
        float x = Mathf.Clamp01(t) * last;
        int i = Mathf.Min((int)x, last - 1);
        return Mathf.Lerp(samples[i], samples[i + 1], x - i);
    }

    /// <summary>
    /// 한 층(segment)이 전체 사각형 [start, end](m, 시전자 기준 앞쪽 거리)의 어느 구간인지 —
    /// 셰이더가 t = x + uv.y·y 로 전체 길이 비율을 얻는다. 층이 곧 전체면 (0, 1).
    /// </summary>
    public static Vector4 Span(SkillPreviewSegment segment, float start, float end)
    {
        float total = Mathf.Max(end - start, 0.0001f);
        return new Vector4((segment.StartOffset - start) / total, segment.Length / total, 0f, 0f);
    }
}
