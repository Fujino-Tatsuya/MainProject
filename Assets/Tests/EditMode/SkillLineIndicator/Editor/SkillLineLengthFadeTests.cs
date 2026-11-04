using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 직선 스킬 사각형 길이 비율 감쇠(할 일 202 — 거너 Q 충전 레이저 조준·Q 미리보기).
/// 원본 = <see cref="SkillLineIndicator"/> AnimationCurve 칸, 셰이더에는 굽힌 샘플 배열 + 직선 보간
/// (<see cref="SkillLineLengthFade.Bake"/>·<see cref="SkillLineLengthFade.Sample"/>).
/// </summary>
public sealed class SkillLineLengthFadeTests
{
    const float Eps = 1e-4f;
    const string ShaderPath = "Assets/3.Materials/Player/SkillLineIndicator/SkillLineIndicator.shader";
    const string MaterialPath = "Assets/3.Materials/Player/SkillLineIndicator/SkillLineRange.mat";

    static float[] BakedDefault()
    {
        var samples = new float[SkillLineLengthFade.SampleCount];
        SkillLineLengthFade.Bake(SkillLineLengthFade.DefaultCurve(), samples);
        return samples;
    }

    [TestCase(0f, 1f)]
    [TestCase(0.5f, 0.9f)]
    [TestCase(0.75f, 0.7f)]
    [TestCase(0.9f, 0.5f)]
    [TestCase(1f, 0.4f)]
    public void DefaultCurve_PassesThroughRequestedPoints(float t, float expected)
    {
        Assert.AreEqual(expected, SkillLineLengthFade.DefaultCurve().Evaluate(t), Eps);
    }

    [Test]
    public void DefaultCurve_NeverIncreasesTowardFarEnd()
    {
        AnimationCurve curve = SkillLineLengthFade.DefaultCurve();
        float previous = curve.Evaluate(0f);
        for (int i = 1; i <= 200; i++)
        {
            float current = curve.Evaluate(i / 200f);
            Assert.LessOrEqual(current, previous + 1e-3f, $"t={i / 200f}");
            previous = current;
        }
    }

    [Test]
    public void BakedSamples_MatchCurveClosely()
    {
        AnimationCurve curve = SkillLineLengthFade.DefaultCurve();
        float[] samples = BakedDefault();
        for (int i = 0; i <= 100; i++)
        {
            float t = i / 100f;
            Assert.AreEqual(curve.Evaluate(t), SkillLineLengthFade.Sample(samples, t), 0.01f, $"t={t}");
        }
        Assert.AreEqual(1f, samples[0], Eps);
        Assert.AreEqual(0.4f, samples[samples.Length - 1], Eps);
    }

    [Test]
    public void Sample_ClampsT()
    {
        float[] samples = BakedDefault();
        Assert.AreEqual(1f, SkillLineLengthFade.Sample(samples, -1f), Eps);
        Assert.AreEqual(0.4f, SkillLineLengthFade.Sample(samples, 2f), Eps);
    }

    [Test]
    public void Bake_ClampsTo01_AndNullOrEmptyCurveMeansNoFade()
    {
        var samples = new float[SkillLineLengthFade.SampleCount];
        SkillLineLengthFade.Bake(AnimationCurve.Linear(0f, 2f, 1f, -1f), samples);
        foreach (float v in samples) Assert.That(v, Is.InRange(0f, 1f));

        SkillLineLengthFade.Bake(null, samples);
        foreach (float v in samples) Assert.AreEqual(1f, v, Eps);
        SkillLineLengthFade.Bake(new AnimationCurve(), samples);
        foreach (float v in samples) Assert.AreEqual(1f, v, Eps);
    }

    [Test]
    public void Span_MapsSegmentIntoWholeRectangle()
    {
        var fill = new SkillPreviewSegment(0f, 6f, 1f);
        var ghost = new SkillPreviewSegment(0f, 8f, 1f);
        Vector4 fillSpan = SkillLineLengthFade.Span(fill, 0f, 8f);
        Vector4 ghostSpan = SkillLineLengthFade.Span(ghost, 0f, 8f);
        Assert.AreEqual(0f, fillSpan.x, Eps);
        Assert.AreEqual(0.75f, fillSpan.y, Eps);   // 채움 끝(uv.y=1) = 전체 t 0.75
        Assert.AreEqual(0f, ghostSpan.x, Eps);
        Assert.AreEqual(1f, ghostSpan.y, Eps);
        // 같은 월드 지점 = 같은 t → 채움·고스트가 한 그라데이션.
        float tFill = fillSpan.x + 0.5f * fillSpan.y;
        float tGhost = ghostSpan.x + (3f / 8f) * ghostSpan.y;
        Assert.AreEqual(tFill, tGhost, Eps);
    }

    [Test]
    public void ChargeLaserPreview_OptsIntoLengthFade_OthersDoNot()
    {
        Assert.IsTrue(SkillPreviewShapes.ChargeLaser(6f, 8f, 1f, 6f, 8f).LengthFade);
        Assert.IsFalse(SkillPreviewShapes.Hitbox(Vector3.forward, Vector3.one).LengthFade);
    }

    [Test]
    public void Shader_UsesSampledCurve_OneMechanism()
    {
        string text = File.ReadAllText(ShaderPath);
        StringAssert.Contains(SkillLineLengthFade.CurveProperty, text);
        StringAssert.Contains(SkillLineLengthFade.EnabledProperty, text);
        StringAssert.Contains(SkillLineLengthFade.SpanProperty, text);
        StringAssert.Contains($"#define LINE_FADE_SAMPLES {SkillLineLengthFade.SampleCount}", text);
        StringAssert.Contains("DecalDistanceFadeAt(input.positionWS,_DecalFade.xyz)", text);
        // 옛 점 칸·3차 보간·진단 토글은 없다.
        StringAssert.DoesNotContain("_LineFadeT", text);
        StringAssert.DoesNotContain("_LineFadeAlpha", text);
        StringAssert.DoesNotContain("_DiagPlain", text);
        // 기존 C3 채움 램프(먼 끝일수록 진함)가 감쇠를 상쇄하지 않게 — 감쇠가 켜지면 램프 대신 감쇠만.
        StringAssert.Contains("lerp(lerp(0.04h/0.145h,1.0h,saturate(input.uv.y)),1.0h,lengthFadeOn)", text);
    }

    [Test]
    public void RangeMaterial_FadeOffByDefault()
    {
        var material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Assert.IsNotNull(material, MaterialPath);
        // 켜기는 거너 Q 속성 블록만 — 다른 스킬 미리보기는 기존 화면 그대로.
        Assert.AreEqual(0f, material.GetFloat(SkillLineLengthFade.EnabledProperty), Eps);
    }
}
