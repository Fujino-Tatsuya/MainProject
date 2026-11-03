using UnityEngine;

/// <summary>
/// "이 색으로 재생" 의 색(2026-11-03 민경 — 한계 돌파 과열 단계 연출).
///
/// <b>곱하기가 아니라 색상(hue) 교체다.</b> 원래 색의 밝기(V)·알파는 그대로 두고 색상만 이 값으로 바꾼다 —
/// 파랑 이펙트에 빨강을 곱하면 거의 검정이 되지만, 색상만 바꾸면 빛나는 정도·흐름은 원본 그대로 빨강이 된다.
/// 채도(S)는 원래 값 × <see cref="Saturation"/> — 흰 심(채도 0)은 흰 채로, 진한 파랑은 진한 빨강으로 간다.
/// HDR(1 넘는) 값도 그대로 유지한다(V 가 1 을 넘어도 된다).
///
/// <b>휘도 보정(<see cref="LumaMatch"/>)</b>(11-03 민경 — "FX_LaserEnd 가 빨강일 때 더 커 보인다"):
/// V 가 같아도 눈이 느끼는 밝기(휘도)는 색상마다 다르다 — 파랑은 휘도 기여가 가장 작아(선형 0.07) 같은 V 의 주황·빨강은 2~3배 밝다.
/// 가산 블렌드 + 가장자리가 옅어지는 원판은 밝아진 만큼 흐리던 가장자리까지 보여(블룸도 번져) 커 보인다.
/// 그래서 바꾼 색의 휘도를 원래 색 휘도 쪽으로 이 비율만큼 당긴다(선형 공간) — 0 = V 유지(보정 없음), 1 = 휘도까지 원래와 같게.
/// </summary>
public readonly struct EffectTint
{
    public readonly bool IsSet;
    public readonly float Hue;          // 0~1
    public readonly float Saturation;   // 원래 채도에 곱한다(1 = 그대로)
    public readonly float LumaMatch;    // 0~1 — 원래 휘도 쪽으로 당기는 비율

    public EffectTint(float hue, float saturation = 1f, float lumaMatch = 0f)
    {
        IsSet = true;
        Hue = Mathf.Repeat(hue, 1f);
        Saturation = Mathf.Max(0f, saturation);
        LumaMatch = Mathf.Clamp01(lumaMatch);
    }

    /// <summary>색 없음 — 원래 색 그대로 재생.</summary>
    public static EffectTint None => default;

    /// <summary>대표 색의 색상만 쓴다(밝기·채도는 원래 이펙트 것).</summary>
    public static EffectTint FromColor(Color color, float saturation = 1f, float lumaMatch = 0f)
    {
        Color.RGBToHSV(color, out float h, out _, out _);
        return new EffectTint(h, saturation, lumaMatch);
    }

    /// <summary>한 색에 적용 — 색상 교체, 채도 × 배율, 밝기(V)·알파 유지 → 휘도 보정.</summary>
    public Color Apply(Color original)
    {
        if (!IsSet) return original;

        Color.RGBToHSV(original, out _, out float s, out float v);
        Color result = Color.HSVToRGB(Hue, Mathf.Clamp01(s * Saturation), v, true);

        if (LumaMatch > 0f)
        {
            // 저작 색은 감마(sRGB) 값 — 휘도는 선형으로 잰다. HDR(1 넘음)도 pow 라 그대로 된다.
            Color originalLinear = original.linear, resultLinear = result.linear;
            float before = Luminance(originalLinear), after = Luminance(resultLinear);
            if (after > 1e-5f)
            {
                float k = Mathf.Lerp(1f, before / after, LumaMatch);
                result = new Color(resultLinear.r * k, resultLinear.g * k, resultLinear.b * k).gamma;
            }
        }

        result.a = original.a;
        return result;
    }

    // Rec.709 상대 휘도(선형 RGB).
    static float Luminance(Color linear) => 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;

    public bool SameAs(in EffectTint other) =>
        IsSet == other.IsSet && (!IsSet || (Mathf.Approximately(Hue, other.Hue) && Mathf.Approximately(Saturation, other.Saturation)
                                            && Mathf.Approximately(LumaMatch, other.LumaMatch)));
}
