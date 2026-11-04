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
    [TestCase(0.5f, 0.85f)]
    [TestCase(0.75f, 0.55f)]
    [TestCase(0.9f, 0.2f)]
    [TestCase(1f, 0f)]
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
        Assert.AreEqual(0f, samples[samples.Length - 1], Eps);   // 먼 끝 = 완전히 투명
    }

    [Test]
    public void Sample_ClampsT()
    {
        float[] samples = BakedDefault();
        Assert.AreEqual(1f, SkillLineLengthFade.Sample(samples, -1f), Eps);
        Assert.AreEqual(0f, SkillLineLengthFade.Sample(samples, 2f), Eps);
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
        // 감쇠는 최종 알파 한 곳에 곱한다 — 채움·외곽선·안쪽선·화살표 무늬·끝선이 모두 result 안에 합쳐진 뒤라 커브 0 = 완전 투명.
        StringAssert.Contains("result.a*inside*_Color.a*lengthFade*distanceFade", text);
        StringAssert.Contains("Blend SrcAlpha OneMinusSrcAlpha", text);
        // 기존 C3 채움 램프(먼 끝일수록 진함)가 감쇠를 상쇄하지 않게 — 감쇠가 켜지면 램프 대신 감쇠만.
        StringAssert.Contains("lerp(lerp(0.04h/0.145h,1.0h,saturate(input.uv.y)),1.0h,lengthFadeOn)", text);
    }

    [Test]
    public void Shader_SideEdgeLines_OwnCurve_OnTop()
    {
        string text = File.ReadAllText(ShaderPath);
        StringAssert.Contains(SkillLineLengthFade.EdgeEnabledProperty, text);
        StringAssert.Contains(SkillLineLengthFade.EdgeCurveProperty, text);
        StringAssert.Contains(SkillLineLengthFade.EdgeWidthProperty, text);
        StringAssert.Contains(SkillLineLengthFade.EdgeColorProperty, text);
        StringAssert.Contains("float _EdgeLineCurve[LINE_FADE_SAMPLES];", text);
        StringAssert.Contains("EdgeLineFade(_LineFadeSpan.x+input.uv.y*_LineFadeSpan.y)", text);
    }

    [Test]
    public void RangeMaterial_EdgeTextureIsWhite512_AndEdgeOffByDefault()
    {
        var material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Assert.IsNotNull(material, MaterialPath);
        Texture edge = material.GetTexture("_EdgeTex");
        Assert.IsNotNull(edge, "_EdgeTex");
        Assert.AreEqual("white_512", edge.name);
        Assert.AreEqual(0f, material.GetFloat(SkillLineLengthFade.EdgeEnabledProperty), Eps);
    }

    // ── 213 어쌔신 대시 일격 화살표 ──────────────────────────────────────

    [Test]
    public void Arrow_LengthFadeIsOptIn()
    {
        Assert.IsFalse(SkillPreviewShapes.Arrow(-1f, 3f, 3f).LengthFade);   // 거너 쿨 백스텝 등 기존 화살표 그대로
        SkillPreviewShape dash = SkillPreviewShapes.Arrow(1f, 8f, 5f, lengthFade: true);
        Assert.IsTrue(dash.LengthFade);
        Assert.IsTrue(dash.HasArrow);
        Assert.AreEqual(5f, dash.ArrowLength, Eps);                          // 벽에 막힌 거리
    }

    [Test]
    public void AssassinDashStrike_IsPreviewSource()
    {
        Assert.IsTrue(typeof(ISkillPreviewSource).IsAssignableFrom(typeof(AssassinDashStrikeSkill)));
    }

    [Test]
    public void Shader_ArrowUsesLengthCurveAndEdgeLine()
    {
        string text = File.ReadAllText(ShaderPath);
        int arrow = text.IndexOf("half4 ArrowC3(", System.StringComparison.Ordinal);
        int frag = text.IndexOf("half4 Frag(", System.StringComparison.Ordinal);
        Assert.That(arrow, Is.GreaterThan(0));
        string body = text.Substring(arrow, frag - arrow);
        StringAssert.Contains("LineLengthFade(t)", body);
        StringAssert.Contains("EdgeLineFade(t)", body);
        StringAssert.Contains("lerp(lerp(0.55h,1.0h,saturate(uv.y)),1.0h,lengthFadeOn)", body);
    }

    [Test]
    public void ArrowMaterial_EdgeTextureIsWhite512()
    {
        var material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/3.Materials/Player/SkillLineIndicator/SkillLineArrow.mat");
        Assert.IsNotNull(material);
        Texture edge = material.GetTexture("_EdgeTex");
        Assert.IsNotNull(edge, "_EdgeTex");
        Assert.AreEqual("white_512", edge.name);
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
