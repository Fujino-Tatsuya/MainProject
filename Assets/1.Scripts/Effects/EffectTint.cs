using UnityEngine;

/// <summary>
/// "이 색으로 재생" 의 색(2026-11-03 민경 — 한계 돌파 과열 단계 연출).
///
/// <b>곱하기가 아니라 색상(hue) 교체다.</b> 원래 색의 밝기(V)·알파는 그대로 두고 색상만 이 값으로 바꾼다 —
/// 파랑 이펙트에 빨강을 곱하면 거의 검정이 되지만, 색상만 바꾸면 빛나는 정도·흐름은 원본 그대로 빨강이 된다.
/// 채도(S)는 원래 값 × <see cref="Saturation"/> — 흰 심(채도 0)은 흰 채로, 진한 파랑은 진한 빨강으로 간다.
/// HDR(1 넘는) 값도 그대로 유지한다(V 가 1 을 넘어도 된다).
///
/// </summary>
public readonly struct EffectTint
{
    public readonly bool IsSet;
    public readonly float Hue;          // 0~1
    public readonly float Saturation;   // 원래 채도에 곱한다(1 = 그대로)

    public EffectTint(float hue, float saturation = 1f)
    {
        IsSet = true;
        Hue = Mathf.Repeat(hue, 1f);
        Saturation = Mathf.Max(0f, saturation);
    }

    /// <summary>색 없음 — 원래 색 그대로 재생.</summary>
    public static EffectTint None => default;

    /// <summary>대표 색의 색상만 쓴다(밝기·채도는 원래 이펙트 것).</summary>
    public static EffectTint FromColor(Color color, float saturation = 1f)
    {
        Color.RGBToHSV(color, out float h, out _, out _);
        return new EffectTint(h, saturation);
    }

    /// <summary>한 색에 적용 — 색상 교체, 채도 × 배율, 밝기(V)·알파 유지.</summary>
    public Color Apply(Color original)
    {
        if (!IsSet) return original;

        Color.RGBToHSV(original, out _, out float s, out float v);
        Color result = Color.HSVToRGB(Hue, Mathf.Clamp01(s * Saturation), v, true);
        result.a = original.a;
        return result;
    }

    public bool SameAs(in EffectTint other) =>
        IsSet == other.IsSet && (!IsSet || (Mathf.Approximately(Hue, other.Hue) && Mathf.Approximately(Saturation, other.Saturation)));
}
