using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 직선 스킬 사각형 길이 비율 감쇠(할 일 202 — 거너 Q 충전 레이저 조준).
/// 식 = <see cref="SkillLineLengthFade.Evaluate(float, Vector3, Vector4)"/>(셰이더 LineLengthFade 와 같은 식).
/// </summary>
public sealed class SkillLineLengthFadeTests
{
    const float Eps = 1e-4f;
    const string ShaderPath = "Assets/3.Materials/Player/SkillLineIndicator/SkillLineIndicator.shader";
    const string MaterialPath = "Assets/3.Materials/Player/SkillLineIndicator/SkillLineRange.mat";

    [TestCase(0f, 1f)]
    [TestCase(0.5f, 0.9f)]
    [TestCase(0.75f, 0.7f)]
    [TestCase(0.9f, 0.5f)]
    [TestCase(1f, 0.4f)]
    public void Default_PassesThroughRequestedPoints(float t, float expected)
    {
        Assert.AreEqual(expected, SkillLineLengthFade.Evaluate(t), Eps);
    }

    [Test]
    public void Default_InterpolatesLinearlyBetweenPoints()
    {
        Assert.AreEqual(0.95f, SkillLineLengthFade.Evaluate(0.25f), Eps);
        Assert.AreEqual(0.8f, SkillLineLengthFade.Evaluate(0.625f), Eps);
        Assert.AreEqual(0.45f, SkillLineLengthFade.Evaluate(0.95f), Eps);
    }

    [Test]
    public void Default_NeverIncreasesTowardFarEnd()
    {
        float previous = SkillLineLengthFade.Evaluate(0f);
        for (int i = 1; i <= 100; i++)
        {
            float current = SkillLineLengthFade.Evaluate(i / 100f);
            Assert.LessOrEqual(current, previous + Eps, $"t={i / 100f}");
            previous = current;
        }
    }

    [Test]
    public void OutOfRangeT_IsClamped()
    {
        Assert.AreEqual(1f, SkillLineLengthFade.Evaluate(-1f), Eps);
        Assert.AreEqual(0.4f, SkillLineLengthFade.Evaluate(2f), Eps);
    }

    [Test]
    public void TunedEndValue_ChangesOnlyLastSegment()
    {
        var alpha = new Vector4(0.9f, 0.7f, 0.5f, 0.1f);
        Assert.AreEqual(0.5f, SkillLineLengthFade.Evaluate(0.9f, SkillLineLengthFade.DefaultPoints, alpha), Eps);
        Assert.AreEqual(0.1f, SkillLineLengthFade.Evaluate(1f, SkillLineLengthFade.DefaultPoints, alpha), Eps);
        Assert.AreEqual(0.9f, SkillLineLengthFade.Evaluate(0.5f, SkillLineLengthFade.DefaultPoints, alpha), Eps);
    }

    [Test]
    public void UnorderedOrCollapsedPoints_StayFiniteAndInRange()
    {
        var points = new Vector3(0.8f, 0.3f, 0.8f);
        for (int i = 0; i <= 20; i++)
        {
            float v = SkillLineLengthFade.Evaluate(i / 20f, points, SkillLineLengthFade.DefaultAlpha);
            Assert.IsFalse(float.IsNaN(v), $"t={i / 20f}");
            Assert.That(v, Is.InRange(0f, 1f));
        }
    }

    [Test]
    public void Shader_DeclaresFieldsAndUsesLengthAndDistanceFade()
    {
        string text = File.ReadAllText(ShaderPath);
        StringAssert.Contains(SkillLineLengthFade.PointsProperty, text);
        StringAssert.Contains(SkillLineLengthFade.AlphaProperty, text);
        StringAssert.Contains(SkillLineLengthFade.EnabledProperty, text);
        StringAssert.Contains("float LineLengthFade(", text);
        StringAssert.Contains("DecalDistanceFadeAt(input.positionWS,_DecalFade.xyz)", text);
    }

    [Test]
    public void RangeMaterial_DefaultsMatchCSharp_AndFadeOffByDefault()
    {
        var material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Assert.IsNotNull(material, MaterialPath);
        Vector4 points = material.GetVector(SkillLineLengthFade.PointsProperty);
        Vector4 alpha = material.GetVector(SkillLineLengthFade.AlphaProperty);
        Assert.AreEqual(SkillLineLengthFade.DefaultPoints.x, points.x, Eps);
        Assert.AreEqual(SkillLineLengthFade.DefaultPoints.y, points.y, Eps);
        Assert.AreEqual(SkillLineLengthFade.DefaultPoints.z, points.z, Eps);
        Assert.AreEqual(SkillLineLengthFade.DefaultAlpha, alpha);
        // 켜기는 차지 표시 속성 블록만 — HUD 호버 미리보기 등 다른 사용은 기존 화면 그대로.
        Assert.AreEqual(0f, material.GetFloat(SkillLineLengthFade.EnabledProperty), Eps);
    }
}
