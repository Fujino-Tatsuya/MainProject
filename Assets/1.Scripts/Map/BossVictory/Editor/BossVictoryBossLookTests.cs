using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>보스 표현(11-12) — 노이즈 / 역림라이트 선택·기본값·팔레트 칸·림 가중치.</summary>
public sealed class BossVictoryBossLookTests
{
    const string SettingsPath = "Assets/Resources/BossVictory/BossVictoryGlitchSettings.asset";
    const float Tol = 1e-4f;

    [Test]
    public void 코드_기본값은_노이즈()
    {
        Assert.AreEqual(BossVictoryBossLookMode.Noise, BossVictoryBossLook.From(null).Mode);
        Assert.IsFalse(BossVictoryBossLook.Default.IsRim);
        BossVictoryProfile p = BossVictoryProfile.CreateDefault();
        try
        {
            BossVictoryBossLook look = BossVictoryBossLook.From(p);
            Assert.AreEqual(BossVictoryBossLookMode.Noise, look.Mode);
            Assert.AreEqual(BossVictoryBossLook.DefaultRimWidth, look.RimWidth, Tol);
            Assert.AreEqual(BossVictoryBossLook.DefaultRimSoftness, look.RimSoftness, Tol);
            Assert.AreEqual(BossVictoryBossLook.DefaultInsideIntensity, look.InsideIntensity, Tol);
            Assert.Greater(look.InsideIntensity, 1f, "기본 안쪽 = 과노출(블룸)");
        }
        finally { Object.DestroyImmediate(p); }
    }

    [Test]
    public void 어쌔신만_림라이트_팔라딘_거너는_노이즈()
    {
        var s = AssetDatabase.LoadAssetAtPath<BossVictoryGlitchSettings>(SettingsPath);
        Assert.IsNotNull(s, SettingsPath);
        Assert.AreEqual(BossVictoryBossLookMode.RimLight, s.ProfileFor(TutorialCharacter.Assassin).bossLook, "어쌔신 = 림라이트");
        Assert.AreEqual(BossVictoryBossLookMode.Noise, s.ProfileFor(TutorialCharacter.Paladin).bossLook, "팔라딘 = 노이즈");
        Assert.AreEqual(BossVictoryBossLookMode.Noise, s.ProfileFor(TutorialCharacter.Gunner).bossLook, "거너 = 노이즈");
    }

    [Test]
    public void 림_색_기본은_보스_노이즈_두_색이고_기준_색을_따라간다()
    {
        var s = ScriptableObject.CreateInstance<BossVictoryGlitchSettings>();
        try
        {
            Color[] reference = BossVictoryPaletteCalc.ReferenceColors(s);
            Assert.AreEqual(s.bossNoiseBrightColor, reference[(int)BossVictoryPaletteSlot.RimInside]);
            Assert.AreEqual(s.bossNoiseDarkColor, reference[(int)BossVictoryPaletteSlot.RimEdge]);

            var inputs = BossVictoryPaletteCalc.Inputs.Default;
            inputs.BaseColor = new Color(0.3f, 0.6f, 0.9f, 1f);
            BossVictoryPalette palette = BossVictoryPaletteCalc.Compute(reference, inputs);
            Assert.AreEqual(palette[BossVictoryPaletteSlot.NoiseBright], palette[BossVictoryPaletteSlot.RimInside], "기준 색 따라감 = 노이즈 밝은 색과 같은 규칙");
            Assert.AreEqual(palette[BossVictoryPaletteSlot.NoiseDark], palette[BossVictoryPaletteSlot.RimEdge]);

            inputs.Overrides = new[] { new BossVictoryPaletteOverride { slot = BossVictoryPaletteSlot.RimEdge, manual = true, color = Color.black } };
            palette = BossVictoryPaletteCalc.Compute(reference, inputs);
            Assert.AreEqual(Color.black, palette[BossVictoryPaletteSlot.RimEdge], "직접 지정");
            Assert.IsTrue(palette.IsManual(BossVictoryPaletteSlot.RimEdge));
            Assert.AreEqual(BossVictoryPalette.Count, BossVictoryPalette.Slots.Length, "칸 수 = enum 수");
        }
        finally { Object.DestroyImmediate(s); }
    }

    [Test]
    public void 마스크_합성_셰이더가_설정_칸에_있고_컴파일_오류_없음()
    {
        var s = AssetDatabase.LoadAssetAtPath<BossVictoryGlitchSettings>(SettingsPath);
        Assert.IsNotNull(s.maskShader, "마스크 셰이더 칸(빌드 포함 참조)");
        Assert.IsNotNull(s.compositeShader, "합성 셰이더 칸(빌드 포함 참조)");
        Assert.AreEqual(BossVictoryGlitchRender.MaskShaderName, s.maskShader.name);
        Assert.AreEqual(BossVictoryGlitchRender.CompositeShaderName, s.compositeShader.name);
        Assert.IsFalse(ShaderUtil.ShaderHasError(s.maskShader), "마스크 셰이더 오류");
        Assert.IsFalse(ShaderUtil.ShaderHasError(s.compositeShader), "합성 셰이더 오류");
    }

    [Test]
    public void 림_가중치는_정면_0_가장자리_1_문턱은_1빼기_굵기()
    {
        var look = new BossVictoryBossLook(BossVictoryBossLookMode.RimLight, 0.3f, 0f, 2f);
        Assert.AreEqual(0.7f, look.RimThreshold, Tol);
        Assert.AreEqual(0f, look.RimWeight(0f), Tol, "정면 = 안쪽(밝음)");
        Assert.AreEqual(1f, look.RimWeight(1f), Tol, "가장자리 = 테두리(어두움)");
        Assert.AreEqual(0f, look.RimWeight(0.69f), Tol);
        Assert.AreEqual(1f, look.RimWeight(0.71f), Tol);
        var soft = new BossVictoryBossLook(BossVictoryBossLookMode.RimLight, 0.3f, 0.1f, 2f);
        float mid = soft.RimWeight(0.7f);
        Assert.Greater(mid, 0.3f);
        Assert.Less(mid, 0.7f, "부드러움 = 문턱 근처 중간값");
        Assert.AreEqual(1f, new BossVictoryBossLook(BossVictoryBossLookMode.RimLight, 3f, -1f, -2f).RimWidth, Tol, "굵기 0~1 로 자름");
        Assert.AreEqual(0f, new BossVictoryBossLook(BossVictoryBossLookMode.RimLight, 0.3f, 0f, -2f).InsideIntensity, Tol);
    }
}
