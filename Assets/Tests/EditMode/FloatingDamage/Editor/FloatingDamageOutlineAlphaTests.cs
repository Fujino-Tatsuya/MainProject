using NUnit.Framework;
using UnityEditor;

/// <summary>외곽선 알파(할 일 268) — DigitSet 외곽선 알파 × Settings 외곽선 알파 × 숫자 알파(팀원 최대 알파·페이드).</summary>
public sealed class FloatingDamageOutlineAlphaTests
{
    private const string SettingsPath = "Assets/9.ScriptableObject/UI/FloatingDamageSettings.asset";

    [Test]
    public void 내_숫자는_외곽선_알파만큼()
    {
        Assert.AreEqual(0.8f, FloatingDamageOutlinePolicy.Alpha(1f, 0.8f, 1f), 1e-5f);
    }

    [Test]
    public void 팀원_최대_알파와_곱한다()
    {
        float teammate = 200f / 255f;
        Assert.AreEqual(0.8f * teammate, FloatingDamageOutlinePolicy.Alpha(1f, 0.8f, teammate), 1e-5f);
    }

    [Test]
    public void DigitSet_알파와_페이드도_곱한다()
    {
        Assert.AreEqual(0.5f * 0.8f * 0.25f, FloatingDamageOutlinePolicy.Alpha(0.5f, 0.8f, 0.25f), 1e-5f);
        Assert.AreEqual(0f, FloatingDamageOutlinePolicy.Alpha(1f, 0.8f, 0f));
    }

    [Test]
    public void 에셋_외곽선_알파는_0_8()
    {
        var settings = AssetDatabase.LoadAssetAtPath<FloatingDamageSettings>(SettingsPath);
        Assert.IsNotNull(settings, SettingsPath);
        Assert.AreEqual(0.8f, settings.OutlineAlpha, 1e-5f);
    }
}
