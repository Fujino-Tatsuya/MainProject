using NUnit.Framework;
using UnityEditor;

/// <summary>
/// 해상도 하한(할 일 154) — 실제 px = 기획 높이 × 유지 크기 × 팀원 배율 × (화면 높이 ÷ 1080) 이 하한보다 작을 때만 하한까지 키운다.
/// </summary>
public sealed class FloatingDamageMinPixelHeightTests
{
    private const string SettingsPath = "Assets/9.ScriptableObject/UI/FloatingDamageSettings.asset";

    [Test]
    public void 하한보다_크면_그대로()
    {
        // 1080p 내 낮음 36×0.5 = 18px ≥ 16
        Assert.AreEqual(1f, FloatingDamageMinPixelPolicy.Multiplier(16f, 18f, 1f));
        // 1080p 중간 36px
        Assert.AreEqual(1f, FloatingDamageMinPixelPolicy.Multiplier(16f, 36f, 1f));
    }

    [Test]
    public void 낮은_해상도_낮음_구간은_하한까지_키운다()
    {
        // 720p 낮음 18 × (720/1080) = 12px → 16px
        float m = FloatingDamageMinPixelPolicy.Multiplier(16f, 18f, 720f / 1080f);
        Assert.AreEqual(16f, 18f * (720f / 1080f) * m, 1e-3f);

        // 1080p 팀원 낮음 36×0.5×0.75 = 13.5px → 16px
        m = FloatingDamageMinPixelPolicy.Multiplier(16f, 13.5f, 1f);
        Assert.AreEqual(16f, 13.5f * m, 1e-3f);
    }

    [Test]
    public void 하한_0_이하나_잘못된_값은_1()
    {
        Assert.AreEqual(1f, FloatingDamageMinPixelPolicy.Multiplier(0f, 1f, 0.1f));
        Assert.AreEqual(1f, FloatingDamageMinPixelPolicy.Multiplier(16f, 0f, 1f));
        Assert.AreEqual(1f, FloatingDamageMinPixelPolicy.Multiplier(16f, 18f, 0f));
    }

    [Test]
    public void 에셋_하한은_16()
    {
        var settings = AssetDatabase.LoadAssetAtPath<FloatingDamageSettings>(SettingsPath);
        Assert.IsNotNull(settings, SettingsPath);
        Assert.AreEqual(16f, settings.MinScreenPixelHeight);
    }
}
