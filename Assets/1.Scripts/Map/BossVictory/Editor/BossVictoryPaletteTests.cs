using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 처치 연출 기준 색 → 팔레트 자동(할 일 271) — 팔라딘(지금 값) = 공용 색 그대로 · 색조 회전 따라감 · 무채색 그대로 · 직접 지정 고정 · 회색 기준 대비 하한.
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictoryPalette).
/// </summary>
public sealed class BossVictoryPaletteTests
{
    const string SettingsPath = "Assets/Resources/BossVictory/BossVictoryGlitchSettings.asset";
    const float Tolerance = 1e-3f;

    static readonly BossVictoryPaletteSlot[] BaseChromatic =
    {
        BossVictoryPaletteSlot.NoiseBright, BossVictoryPaletteSlot.NoiseDark, BossVictoryPaletteSlot.Circle3Right,
        BossVictoryPaletteSlot.OrangeLeft, BossVictoryPaletteSlot.OrangeRight,
    };
    static readonly BossVictoryPaletteSlot[] Neutral =
    {
        BossVictoryPaletteSlot.RectifyOutline, BossVictoryPaletteSlot.Circle3Left, BossVictoryPaletteSlot.CircleGray, BossVictoryPaletteSlot.Glow,
    };
    static readonly BossVictoryPaletteSlot[] Contrast =
    {
        BossVictoryPaletteSlot.Circle1Left, BossVictoryPaletteSlot.Circle1Right, BossVictoryPaletteSlot.Circle2Left,
        BossVictoryPaletteSlot.Circle2Right, BossVictoryPaletteSlot.CrtGlow,
    };

    // 처음(11-12 굽기 전) 공용 설정 값 — 서로 맞물린 기준 팔레트 한 벌(기준 #D6AB69 · 대비 파랑, 색조 차이 = DefaultContrastHueOffset). 관계 테스트용, 에셋과 무관.
    static readonly Color OldBase = new Color32(0xD6, 0xAB, 0x69, 0xFF);

    static BossVictoryPaletteCalc.Inputs RefInputs()
    {
        BossVictoryPaletteCalc.Inputs inputs = BossVictoryPaletteCalc.Inputs.Default;
        inputs.BaseColor = OldBase;
        inputs.ContrastColor = BossVictoryPaletteCalc.DefaultContrastColor;
        return inputs;
    }

    static Color[] Reference()
    {
        var c = new Color[BossVictoryPalette.Count];
        c[(int)BossVictoryPaletteSlot.Amber] = OldBase;
        c[(int)BossVictoryPaletteSlot.NoiseBright] = new Color32(0xFF, 0xF6, 0xEA, 0xFF);
        c[(int)BossVictoryPaletteSlot.NoiseDark] = new Color32(0x3A, 0x17, 0x12, 0xFF);
        c[(int)BossVictoryPaletteSlot.RectifyOutline] = Color.white;
        c[(int)BossVictoryPaletteSlot.Circle1Left] = new Color32(0xFF, 0x2D, 0xB4, 0xFF);
        c[(int)BossVictoryPaletteSlot.Circle1Right] = new Color32(0xA0, 0x40, 0xFF, 0xFF);
        c[(int)BossVictoryPaletteSlot.Circle2Left] = new Color32(0x5A, 0xC8, 0xFF, 0xFF);
        c[(int)BossVictoryPaletteSlot.Circle2Right] = new Color32(0x24, 0x38, 0xA8, 0xFF);
        c[(int)BossVictoryPaletteSlot.Circle3Left] = Color.white;
        c[(int)BossVictoryPaletteSlot.Circle3Right] = new Color32(0xFF, 0xF6, 0xE0, 0xFF);
        c[(int)BossVictoryPaletteSlot.CircleGray] = new Color32(0x8A, 0x8A, 0x8A, 0xFF);
        c[(int)BossVictoryPaletteSlot.OrangeLeft] = new Color32(0xFF, 0xA5, 0x3A, 0xFF);
        c[(int)BossVictoryPaletteSlot.OrangeRight] = new Color32(0xF8, 0xE8, 0x62, 0xFF);
        c[(int)BossVictoryPaletteSlot.Glow] = Color.white;
        c[(int)BossVictoryPaletteSlot.CrtGlow] = BossVictoryPaletteCalc.DefaultContrastColor;
        return c;
    }

    static void AssertColor(Color expected, Color actual, string what) =>
        Assert.Less(Vector4.Distance(expected, actual), Tolerance, $"{what}: 기대 {expected} · 실제 {actual}");

    static float Hue(Color c) { Color.RGBToHSV(c, out float h, out _, out _); return h * 360f; }
    static float Sat(Color c) { Color.RGBToHSV(c, out _, out float s, out _); return s; }
    static float Val(Color c) { Color.RGBToHSV(c, out _, out _, out float v); return v; }

    static Color RotateHue(Color c, float degrees)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        return Color.HSVToRGB(Mathf.Repeat(h + degrees / 360f, 1f), s, v);
    }

    [Test]
    // 세 캐릭터 프로필 기준 색은 사용자가 튜닝한다(11-12) — 값은 고정하지 않고, '기준 색 = 공용 호박색이면 공용 색 그대로' 관계만 본다.
    // 코드 기본값(처치자 없음·표에 없는 캐릭터 — 기본 프로필 에셋은 11-12 삭제)은 값 그대로 DefaultBaseColor(= 팔라딘 기준 색, 11-12 굽기) → 공용 색 그대로.
    public void 기준_색이_공용_호박색이면_공용_색_그대로_에셋()
    {
        var s = AssetDatabase.LoadAssetAtPath<BossVictoryGlitchSettings>(SettingsPath);
        Assert.IsNotNull(s, SettingsPath);
        Color[] reference = BossVictoryPaletteCalc.ReferenceColors(s);

        BossVictoryProfile code = s.ProfileFor(TutorialCharacter.None);
        AssertColor(BossVictoryPaletteCalc.DefaultBaseColor, code.baseColor, "코드 기본값 기준 색 = DefaultBaseColor");
        AssertSameAsReference(reference, BossVictoryPaletteCalc.Compute(s, code), "코드 기본값", true);

        foreach (TutorialCharacter character in new[] { TutorialCharacter.Paladin, TutorialCharacter.Gunner, TutorialCharacter.Assassin })
        {
            BossVictoryProfile p = s.ProfileFor(character);
            BossVictoryPaletteCalc.Inputs inputs = BossVictoryPaletteCalc.Inputs.Default;   // 그 프로필의 직접 지정만 살리고 기준·대비는 공용 기준
            inputs.BaseColor = s.amberColor;
            inputs.ContrastHueOffset = BossVictoryPaletteCalc.ReferenceContrastHueOffset(s);
            inputs.Overrides = p.paletteOverrides;
            AssertSameAsReference(reference, BossVictoryPaletteCalc.Compute(reference, inputs), character.ToString(), false);
        }
    }

    static void AssertSameAsReference(Color[] reference, BossVictoryPalette palette, string who, bool allAuto)
    {
        foreach (BossVictoryPaletteSlot slot in BossVictoryPalette.Slots)
        {
            if (allAuto) Assert.IsFalse(palette.IsManual(slot), $"{who} {slot} = 자동");
            if (palette.IsManual(slot)) continue;
            AssertColor(reference[(int)slot], palette[slot], $"{who} {slot}");
        }
    }

    [Test]
    public void 공용_에셋에서_역산한_색조_차이_기본값과_같다()
    {
        var s = AssetDatabase.LoadAssetAtPath<BossVictoryGlitchSettings>(SettingsPath);
        Assert.IsNotNull(s, SettingsPath);
        Assert.AreEqual(BossVictoryPaletteCalc.DefaultContrastHueOffset, BossVictoryPaletteCalc.ReferenceContrastHueOffset(s), Tolerance);
    }

    [Test]
    public void 코드_기본_입력이면_기준_팔레트_그대로()
    {
        Color[] reference = Reference();
        BossVictoryPalette palette = BossVictoryPaletteCalc.Compute(reference, RefInputs());
        foreach (BossVictoryPaletteSlot slot in BossVictoryPalette.Slots) AssertColor(reference[(int)slot], palette[slot], slot.ToString());
        AssertColor(reference[(int)BossVictoryPaletteSlot.CrtGlow], palette.ContrastBase, "대비 기준 색");
    }

    [Test]
    public void 기준_색조를_돌리면_기조와_대비_그룹이_같이_돈다_무채색은_그대로()
    {
        Color[] reference = Reference();
        BossVictoryPaletteCalc.Inputs inputs = RefInputs();
        inputs.BaseColor = RotateHue(OldBase, 60f);   // 채도·밝기 같음 → 비율 1
        BossVictoryPalette palette = BossVictoryPaletteCalc.Compute(reference, inputs);

        AssertColor(inputs.BaseColor, palette[BossVictoryPaletteSlot.Amber], "호박색 = 기준 색");
        foreach (BossVictoryPaletteSlot slot in BossVictoryPaletteTests.BaseChromatic)
        {
            Color before = reference[(int)slot], after = palette[slot];
            Assert.AreEqual(60f, Mathf.DeltaAngle(Hue(before), Hue(after)), 0.5f, $"{slot} 색조 +60°");
            Assert.AreEqual(Sat(before), Sat(after), Tolerance, $"{slot} 채도 그대로");
            Assert.AreEqual(Val(before), Val(after), Tolerance, $"{slot} 밝기 그대로");
        }
        foreach (BossVictoryPaletteSlot slot in Contrast)
            Assert.AreEqual(60f, Mathf.DeltaAngle(Hue(reference[(int)slot]), Hue(palette[slot])), 0.5f, $"{slot}(대비) 색조 +60°");
        foreach (BossVictoryPaletteSlot slot in Neutral)
            AssertColor(reference[(int)slot], palette[slot], $"{slot}(무채색) 그대로");
    }

    [Test]
    public void 기준_채도_밝기는_비율로_따라간다()
    {
        Color[] reference = Reference();
        BossVictoryPaletteCalc.Inputs inputs = RefInputs();
        Color.RGBToHSV(OldBase, out float h, out float s, out float v);
        inputs.BaseColor = Color.HSVToRGB(h, s * 0.5f, v * 0.5f);
        BossVictoryPalette palette = BossVictoryPaletteCalc.Compute(reference, inputs);
        Color orange = reference[(int)BossVictoryPaletteSlot.OrangeLeft];
        Assert.AreEqual(Sat(orange) * 0.5f, Sat(palette[BossVictoryPaletteSlot.OrangeLeft]), Tolerance, "채도 × 0.5");
        Assert.AreEqual(Val(orange) * 0.5f, Val(palette[BossVictoryPaletteSlot.OrangeLeft]), Tolerance, "밝기 × 0.5");
        Assert.AreEqual(Hue(orange), Hue(palette[BossVictoryPaletteSlot.OrangeLeft]), 0.5f, "색조 그대로");
    }

    [Test]
    public void 직접_지정_칸은_기준_색을_바꿔도_안_바뀐다()
    {
        Color[] reference = Reference();
        Color green = new Color(0.1f, 0.9f, 0.2f, 1f);
        Color hdrBlue = new Color(0f, 0.5f, 3f, 1f);
        var overrides = new List<BossVictoryPaletteOverride>
        {
            new BossVictoryPaletteOverride { slot = BossVictoryPaletteSlot.OrangeLeft, manual = true, color = green },
            new BossVictoryPaletteOverride { slot = BossVictoryPaletteSlot.Circle1Left, manual = true, color = hdrBlue },
            new BossVictoryPaletteOverride { slot = BossVictoryPaletteSlot.OrangeLeft, manual = true, color = Color.black },   // 같은 칸 두 줄 = 앞 줄
            new BossVictoryPaletteOverride { slot = BossVictoryPaletteSlot.NoiseDark, manual = false, color = Color.black },   // 꺼진 줄 = 자동
        };
        foreach (float rotate in new[] { 0f, 90f, 200f })
        {
            BossVictoryPaletteCalc.Inputs inputs = RefInputs();
            inputs.BaseColor = RotateHue(OldBase, rotate);
            inputs.Overrides = overrides;
            BossVictoryPalette palette = BossVictoryPaletteCalc.Compute(reference, inputs);
            Assert.AreEqual(green, palette[BossVictoryPaletteSlot.OrangeLeft], $"회전 {rotate} — 직접 지정 그대로");
            Assert.AreEqual(hdrBlue, palette[BossVictoryPaletteSlot.Circle1Left], $"회전 {rotate} — HDR 직접 지정 그대로");
            Assert.IsTrue(palette.IsManual(BossVictoryPaletteSlot.OrangeLeft));
            Assert.IsFalse(palette.IsManual(BossVictoryPaletteSlot.NoiseDark), "manual 끔 = 자동");
            Assert.AreNotEqual(Color.black, palette[BossVictoryPaletteSlot.NoiseDark]);
        }
    }

    [Test]
    public void 회색_기준이면_기조는_회색_대비는_하한_덕에_산다()
    {
        Color[] reference = Reference();
        BossVictoryPaletteCalc.Inputs inputs = RefInputs();
        inputs.BaseColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        BossVictoryPalette palette = BossVictoryPaletteCalc.Compute(reference, inputs);

        foreach (BossVictoryPaletteSlot slot in BaseChromatic)
            Assert.Less(Sat(palette[slot]), Tolerance, $"{slot} 기준이 회색이면 회색");
        Assert.GreaterOrEqual(Sat(palette.ContrastBase), BossVictoryPaletteCalc.DefaultContrastMinSaturation - Tolerance, "대비 기준 채도 ≥ 하한");
        Assert.GreaterOrEqual(Val(palette.ContrastBase), BossVictoryPaletteCalc.DefaultContrastMinValue - Tolerance, "대비 기준 밝기 ≥ 하한");
        foreach (BossVictoryPaletteSlot slot in Contrast)
            Assert.Greater(Sat(palette[slot]), 0.3f, $"{slot} 대비색이 죽지 않음");

        inputs.ContrastMinSaturation = 0f;   // 하한이 없으면 대비색도 회색이 된다 — 하한이 하는 일
        BossVictoryPalette dead = BossVictoryPaletteCalc.Compute(reference, inputs);
        Assert.Less(Sat(dead.ContrastBase), Tolerance, "하한 0 = 대비 기준도 회색");
    }

    [Test]
    public void 대비_직접_지정이면_TV_꺼짐_발광은_그_색_원은_그_색에서_따라간다()
    {
        Color[] reference = Reference();
        BossVictoryPaletteCalc.Inputs inputs = RefInputs();
        inputs.ContrastMode = BossVictoryContrastMode.Manual;
        inputs.ContrastColor = RotateHue(BossVictoryPaletteCalc.DefaultContrastColor, -90f);
        inputs.ContrastMinSaturation = 1f;   // 직접 지정엔 하한이 안 걸린다
        BossVictoryPalette palette = BossVictoryPaletteCalc.Compute(reference, inputs);
        AssertColor(inputs.ContrastColor, palette[BossVictoryPaletteSlot.CrtGlow], "TV 꺼짐 = 대비 색");
        foreach (BossVictoryPaletteSlot slot in Contrast)
            Assert.AreEqual(-90f, Mathf.DeltaAngle(Hue(reference[(int)slot]), Hue(palette[slot])), 0.5f, $"{slot} 색조 −90°");
        foreach (BossVictoryPaletteSlot slot in BaseChromatic)
            AssertColor(reference[(int)slot], palette[slot], $"{slot} 기조는 그대로(기준 색 안 바뀜)");
    }
}
