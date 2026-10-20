using System;
using UnityEngine;

/// <summary>
/// 호버 림 외곽선 튜닝 값(할 일 58) — <c>Project/Enemy Hover Rim</c> 셰이더 프로퍼티와 1:1.
/// 값 범위는 <see cref="Sanitized"/> 한 곳에서 정하고, 셰이더 식은 <see cref="RimAlpha"/> 가 그대로 옮겨 적는다.
/// </summary>
public readonly struct HoverRimSettings : IEquatable<HoverRimSettings>
{
    public const float MaxSoftness = 1f;
    public const float MaxIntensity = 8f;

    public static readonly int ColorId = Shader.PropertyToID("_RimColor");
    public static readonly int ThresholdId = Shader.PropertyToID("_RimThreshold");
    public static readonly int SoftnessId = Shader.PropertyToID("_RimSoftness");
    public static readonly int IntensityId = Shader.PropertyToID("_RimIntensity");

    public readonly Color Color;
    public readonly float Threshold;
    public readonly float Softness;
    public readonly float Intensity;

    public HoverRimSettings(Color color, float threshold, float softness, float intensity)
    {
        Color = color;
        Threshold = threshold;
        Softness = softness;
        Intensity = intensity;
    }

    /// <summary>임계값 0~1, 부드러움 0~<see cref="MaxSoftness"/>, 강도 0~<see cref="MaxIntensity"/>, 색 알파 0~1. NaN 은 0.</summary>
    public HoverRimSettings Sanitized()
    {
        Color c = Color;
        c.a = Clamp(c.a, 0f, 1f);
        return new HoverRimSettings(
            c,
            Clamp(Threshold, 0f, 1f),
            Clamp(Softness, 0f, MaxSoftness),
            Clamp(Intensity, 0f, MaxIntensity));
    }

    public void ApplyTo(Material material)
    {
        material.SetColor(ColorId, Color);
        material.SetFloat(ThresholdId, Threshold);
        material.SetFloat(SoftnessId, Softness);
        material.SetFloat(IntensityId, Intensity);
    }

    /// <summary>셰이더 Frag 와 같은 식 — |N·V| 가 임계값보다 작을수록 1, 임계값 ± 부드러움/2 구간에서 부드럽게 0.</summary>
    public static float RimAlpha(float absNdotV, float threshold, float softness)
    {
        float half = Mathf.Max(softness * 0.5f, 1e-4f);
        return 1f - SmoothStep(threshold - half, threshold + half, absNdotV);
    }

    // HLSL smoothstep 과 같은 정의(Mathf.SmoothStep 은 보간이라 다르다).
    static float SmoothStep(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    static float Clamp(float v, float min, float max) => float.IsNaN(v) ? min : Mathf.Clamp(v, min, max);

    public bool Equals(HoverRimSettings other) =>
        Color == other.Color && Threshold == other.Threshold && Softness == other.Softness && Intensity == other.Intensity;

    public override bool Equals(object obj) => obj is HoverRimSettings other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Color, Threshold, Softness, Intensity);
}
