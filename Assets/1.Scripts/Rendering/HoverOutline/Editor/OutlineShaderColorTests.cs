using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 외곽선 색 선형 변환(할 일 58 후속 6) — 인스펙터 값 = 화면 sRGB 값이 되도록 Linear 프로젝트에서만 바꾼다.
/// </summary>
public sealed class OutlineShaderColorTests
{
    const float Eps = 0.002f;

    [Test]
    public void Linear_이면_회색_022가_약_004로()
    {
        Color c = EnemyOutlineFeature.ToShaderColor(new Color(0.22f, 0.22f, 0.22f, 1f), ColorSpace.Linear);

        Assert.AreEqual(0.0397f, c.r, Eps);
        Assert.AreEqual(0.0397f, c.g, Eps);
        Assert.AreEqual(0.0397f, c.b, Eps);
    }

    [Test]
    public void Linear_이어도_알파는_그대로()
    {
        Color c = EnemyOutlineFeature.ToShaderColor(new Color(0.22f, 0.22f, 0.22f, 0.5f), ColorSpace.Linear);

        Assert.AreEqual(0.5f, c.a, 1e-6f);
    }

    [Test]
    public void Linear_이면_0과_1은_그대로()
    {
        Color c = EnemyOutlineFeature.ToShaderColor(new Color(1f, 0f, 0f, 1f), ColorSpace.Linear);

        Assert.AreEqual(1f, c.r, 1e-5f);
        Assert.AreEqual(0f, c.g, 1e-5f);
        Assert.AreEqual(0f, c.b, 1e-5f);
    }

    [TestCase(ColorSpace.Gamma)]
    [TestCase(ColorSpace.Uninitialized)]
    public void Linear_가_아니면_그대로(ColorSpace space)
    {
        var input = new Color(0.22f, 0.08f, 0.5f, 0.7f);

        Assert.AreEqual(input, EnemyOutlineFeature.ToShaderColor(input, space));
    }
}
