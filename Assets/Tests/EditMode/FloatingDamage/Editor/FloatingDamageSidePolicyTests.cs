using NUnit.Framework;
using UnityEditor;

/// <summary>맞은 반대쪽에 뜨기(할 일 268) — 화면 x 로 공격자 반대편, 기준점 없음·정면이면 위로만.</summary>
public sealed class FloatingDamageSidePolicyTests
{
    private const string SettingsPath = "Assets/9.ScriptableObject/UI/FloatingDamageSettings.asset";

    [Test]
    public void 왼쪽에서_때리면_오른쪽()
    {
        Assert.AreEqual(1, FloatingDamageSidePolicy.Side(true, 960f, 700f, 24f));
    }

    [Test]
    public void 오른쪽에서_때리면_왼쪽()
    {
        Assert.AreEqual(-1, FloatingDamageSidePolicy.Side(true, 960f, 1200f, 24f));
    }

    [Test]
    public void 정면이면_위로만()
    {
        Assert.AreEqual(0, FloatingDamageSidePolicy.Side(true, 960f, 960f, 24f));
        Assert.AreEqual(0, FloatingDamageSidePolicy.Side(true, 960f, 940f, 24f), "문턱 안");
        Assert.AreEqual(0, FloatingDamageSidePolicy.Side(true, 960f, 984f, 24f), "문턱 경계 포함");
    }

    [Test]
    public void 기준점_없으면_위로만()
    {
        Assert.AreEqual(0, FloatingDamageSidePolicy.Side(false, 960f, 0f, 24f));
    }

    [Test]
    public void 가로_오프셋_밀기와_흐름()
    {
        Assert.AreEqual(40f, FloatingDamageSidePolicy.OffsetX(1, 40f, 30f, 0f), 1e-5f, "생성 순간");
        Assert.AreEqual(70f, FloatingDamageSidePolicy.OffsetX(1, 40f, 30f, 1f), 1e-5f, "다 흐름");
        Assert.AreEqual(-55f, FloatingDamageSidePolicy.OffsetX(-1, 40f, 30f, 0.5f), 1e-5f, "왼쪽 절반");
        Assert.AreEqual(-70f, FloatingDamageSidePolicy.OffsetX(-1, 40f, 30f, 2f), 1e-5f, "진행 1 넘어도 멈춤");
        Assert.AreEqual(0f, FloatingDamageSidePolicy.OffsetX(0, 40f, 30f, 1f), "위로만");
    }

    [Test]
    public void 에셋_기본값()
    {
        var settings = AssetDatabase.LoadAssetAtPath<FloatingDamageSettings>(SettingsPath);
        Assert.IsNotNull(settings, SettingsPath);
        Assert.AreEqual(40f, settings.SidePush, 1e-5f);
        Assert.AreEqual(30f, settings.SideDrift, 1e-5f);
        Assert.AreEqual(24f, settings.SideFrontThreshold, 1e-5f);
    }
}
