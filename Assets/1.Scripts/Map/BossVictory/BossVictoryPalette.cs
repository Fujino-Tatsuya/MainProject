using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 처치 연출 팔레트 칸(할 일 271). 🔴 직렬화 값(프로필 <see cref="BossVictoryPaletteOverride.slot"/>)이라 순서를 바꾸거나 중간에 끼우지 않는다 — 새 칸은 끝에.
/// </summary>
public enum BossVictoryPaletteSlot
{
    Amber = 0,          // 배경 호박색 = 기준 색 자체
    NoiseBright = 1,    // 보스 노이즈 밝은 색
    NoiseDark = 2,      // 보스 노이즈 어두운 색
    RectifyOutline = 3, // RECTIFY 외곽선(흰색 — 채도 0 이라 그대로)
    Circle1Left = 4,    // ① 마젠타(대비)
    Circle1Right = 5,   // ① 보라(대비)
    Circle2Left = 6,    // ② 하늘(대비)
    Circle2Right = 7,   // ② 남색(대비)
    Circle3Left = 8,    // ③ 흰(그대로)
    Circle3Right = 9,   // ③ 연크림
    CircleGray = 10,    // ③ 회색 단계(그대로)
    OrangeLeft = 11,    // 주황 채움 왼쪽
    OrangeRight = 12,   // 주황 채움 오른쪽
    Glow = 13,          // RECTIFY 글로우 색(흰색 — 그대로)
    CrtGlow = 14,       // TV 꺼짐 발광 = 대비 기준 색 자체
}

/// <summary>대비 기준 색 고르기 — 자동(기준 색 색조 + 색조 차이) / 직접 지정.</summary>
public enum BossVictoryContrastMode { Auto = 0, Manual = 1 }

/// <summary>프로필의 '직접 지정' 한 줄(271). <see cref="manual"/> 이 꺼져 있으면 자동 — 색은 다시 켤 때를 위해 남겨 둔다.</summary>
[Serializable]
public struct BossVictoryPaletteOverride
{
    public BossVictoryPaletteSlot slot;
    [Tooltip("켬 = 이 칸은 아래 색 그대로(기준 색을 바꿔도 안 따라감).")]
    public bool manual;
    [ColorUsage(true, true)] public Color color;
}

/// <summary>계산된 처치 연출 팔레트 — 칸마다 sRGB 색 + 직접 지정 여부. <see cref="BossVictoryPaletteCalc.Compute(BossVictoryGlitchSettings, BossVictoryProfile)"/> 가 만든다.</summary>
public sealed class BossVictoryPalette
{
    public const int Count = 15;
    public static readonly BossVictoryPaletteSlot[] Slots = (BossVictoryPaletteSlot[])Enum.GetValues(typeof(BossVictoryPaletteSlot));

    internal readonly Color[] Colors = new Color[Count];
    internal readonly bool[] Manual = new bool[Count];

    /// <summary>대비 그룹이 따라간 대비 기준 색(자동이면 하한까지 적용한 값).</summary>
    public Color ContrastBase { get; internal set; }

    public Color this[BossVictoryPaletteSlot slot] => Colors[(int)slot];
    public bool IsManual(BossVictoryPaletteSlot slot) => Manual[(int)slot];
}

/// <summary>
/// 기준 색 → 팔레트 자동 계산(할 일 271, 순수 함수 — EditMode <c>BossVictoryPaletteTests</c>). 문서 Docs/tech/boss-victory-character-profiles.md §4.11.
/// <list type="bullet">
/// <item>공용 설정(<see cref="BossVictoryGlitchSettings"/>)의 색 = '기준(팔라딘) 팔레트'. 기준 색의 기준 = 설정 <c>amberColor</c>, 대비 기준 색의 기준 = 설정 <c>crtOffGlowColor</c>.</item>
/// <item>기조 그룹: 각 색 = 기준 색과의 관계 유지 — 색조 = 원래 색조 + (새 기준 색조 − 옛 기준 색조), 채도·밝기 = 원래 값 × (새/옛 기준 비율). HSV(sRGB) 계산, HDR 밝기(1 초과)는 유지.</item>
/// <item>채도 ≈ 0(흰·회색)은 색조가 없어 그대로.</item>
/// <item>대비 그룹(①·② 원 좌우·TV 꺼짐 발광): 대비 기준 색 = 자동(새 기준 색조 + <c>contrastHueOffset</c>, 채도·밝기는 기조와 같은 비율 뒤 하한) 또는 직접 지정. 그룹 안 색 = 대비 기준 색에서 같은 규칙.</item>
/// <item>직접 지정 칸은 계산을 건너뛰고 그 색.</item>
/// </list>
/// </summary>
public static class BossVictoryPaletteCalc
{
    /// <summary>이 채도 미만 = 무채색(흰·회색) — 색조가 없어 기준 색을 바꿔도 그대로.</summary>
    public const float NeutralSaturation = 0.02f;
    const float Epsilon = 1e-4f;

    /// <summary>팔라딘·거너·어쌔신·기본 기준 색(= 지금 공용 설정 amberColor #D6AB69).</summary>
    public static readonly Color DefaultBaseColor = new Color32(0xD6, 0xAB, 0x69, 0xFF);
    /// <summary>대비 기준 색 직접 지정 기본(= 지금 TV 꺼짐 발광 파랑).</summary>
    public static readonly Color DefaultContrastColor = new Color(0.25f, 0.62f, 1f, 1f);
    /// <summary>기본 색조 차이(도) = 색조(#3FA0FF 계열 TV 꺼짐 파랑) − 색조(#D6AB69) — 팔라딘 결과가 지금과 같게 역산한 값.</summary>
    public const float DefaultContrastHueOffset = 174.0697f;
    public const float DefaultContrastMinSaturation = 0.45f;
    public const float DefaultContrastMinValue = 0.6f;

    /// <summary>대비 그룹 칸 — 대비 기준 색을 따라간다.</summary>
    public static bool IsContrast(BossVictoryPaletteSlot slot) =>
        slot == BossVictoryPaletteSlot.Circle1Left || slot == BossVictoryPaletteSlot.Circle1Right ||
        slot == BossVictoryPaletteSlot.Circle2Left || slot == BossVictoryPaletteSlot.Circle2Right ||
        slot == BossVictoryPaletteSlot.CrtGlow;

    /// <summary>계산 입력(프로필 칸 묶음 — 테스트가 SO 없이 만든다).</summary>
    public struct Inputs
    {
        public Color BaseColor;
        public BossVictoryContrastMode ContrastMode;
        public float ContrastHueOffset;     // 도
        public Color ContrastColor;         // 직접 지정일 때
        public float ContrastMinSaturation; // 자동일 때 대비 기준 색 채도 하한
        public float ContrastMinValue;      // 자동일 때 대비 기준 색 밝기 하한
        public IReadOnlyList<BossVictoryPaletteOverride> Overrides;

        public static Inputs Default => new Inputs
        {
            BaseColor = DefaultBaseColor,
            ContrastMode = BossVictoryContrastMode.Auto,
            ContrastHueOffset = DefaultContrastHueOffset,
            ContrastColor = DefaultContrastColor,
            ContrastMinSaturation = DefaultContrastMinSaturation,
            ContrastMinValue = DefaultContrastMinValue,
        };

        public static Inputs From(BossVictoryProfile p) => p == null ? Default : new Inputs
        {
            BaseColor = p.baseColor,
            ContrastMode = p.contrastMode,
            ContrastHueOffset = p.contrastHueOffset,
            ContrastColor = p.contrastColor,
            ContrastMinSaturation = p.contrastMinSaturation,
            ContrastMinValue = p.contrastMinValue,
            Overrides = p.paletteOverrides,
        };
    }

    /// <summary>공용 설정 색 = 기준(팔라딘) 팔레트. 칸 순서 = <see cref="BossVictoryPaletteSlot"/>.</summary>
    public static Color[] ReferenceColors(BossVictoryGlitchSettings s)
    {
        var c = new Color[BossVictoryPalette.Count];
        BossVictoryGlitchSettings.RectifyCircle[] circles = s.rectifyCircles ?? Array.Empty<BossVictoryGlitchSettings.RectifyCircle>();
        BossVictoryGlitchSettings.RectifyCircle[] fallback = BossVictoryGlitchSettings.DefaultRectifyCircles();
        BossVictoryGlitchSettings.RectifyCircle Circle(int i) => i < circles.Length ? circles[i] : fallback[i];
        c[(int)BossVictoryPaletteSlot.Amber] = s.amberColor;
        c[(int)BossVictoryPaletteSlot.NoiseBright] = s.bossNoiseBrightColor;
        c[(int)BossVictoryPaletteSlot.NoiseDark] = s.bossNoiseDarkColor;
        c[(int)BossVictoryPaletteSlot.RectifyOutline] = s.rectifyColor;
        c[(int)BossVictoryPaletteSlot.Circle1Left] = Circle(0).leftColor;
        c[(int)BossVictoryPaletteSlot.Circle1Right] = Circle(0).rightColor;
        c[(int)BossVictoryPaletteSlot.Circle2Left] = Circle(1).leftColor;
        c[(int)BossVictoryPaletteSlot.Circle2Right] = Circle(1).rightColor;
        c[(int)BossVictoryPaletteSlot.Circle3Left] = Circle(2).leftColor;
        c[(int)BossVictoryPaletteSlot.Circle3Right] = Circle(2).rightColor;
        c[(int)BossVictoryPaletteSlot.CircleGray] = s.rectifyCircleGrayColor;
        c[(int)BossVictoryPaletteSlot.OrangeLeft] = s.rectifyOrangeLeft;
        c[(int)BossVictoryPaletteSlot.OrangeRight] = s.rectifyOrangeRight;
        c[(int)BossVictoryPaletteSlot.Glow] = s.rectifyGlowColor;
        c[(int)BossVictoryPaletteSlot.CrtGlow] = s.crtOffGlowColor;
        return c;
    }

    /// <summary>처치자 프로필 기준 팔레트(프로필 null = 코드 기본 입력).</summary>
    public static BossVictoryPalette Compute(BossVictoryGlitchSettings s, BossVictoryProfile p) =>
        Compute(ReferenceColors(s), Inputs.From(p));

    /// <summary>순수 계산 — <paramref name="reference"/> = 기준 팔레트(칸 순서, 기준 색 = Amber · 대비 기준 = CrtGlow).</summary>
    public static BossVictoryPalette Compute(Color[] reference, in Inputs inputs)
    {
        var result = new BossVictoryPalette();
        Color refBase = reference[(int)BossVictoryPaletteSlot.Amber];
        Color refContrast = reference[(int)BossVictoryPaletteSlot.CrtGlow];
        Color contrast = ContrastBase(refBase, inputs.BaseColor, refContrast, inputs);
        result.ContrastBase = contrast;

        for (int i = 0; i < BossVictoryPalette.Count; i++)
        {
            var slot = (BossVictoryPaletteSlot)i;
            Color c;
            if (slot == BossVictoryPaletteSlot.Amber) c = WithAlpha(inputs.BaseColor, reference[i].a);
            else if (slot == BossVictoryPaletteSlot.CrtGlow) c = WithAlpha(contrast, reference[i].a);
            else if (IsContrast(slot)) c = Follow(reference[i], refContrast, contrast);
            else c = Follow(reference[i], refBase, inputs.BaseColor);
            result.Colors[i] = c;
        }

        if (inputs.Overrides != null)
            for (int k = 0; k < inputs.Overrides.Count; k++)
            {
                BossVictoryPaletteOverride o = inputs.Overrides[k];
                int i = (int)o.slot;
                if (!o.manual || i < 0 || i >= BossVictoryPalette.Count || result.Manual[i]) continue;   // 같은 칸 두 줄이면 앞 줄
                result.Colors[i] = o.color;
                result.Manual[i] = true;
            }
        return result;
    }

    /// <summary>
    /// 대비 기준 색. 직접 지정 = 그 색. 자동 = 색조 = 새 기준 색조 + 색조 차이 · 채도·밝기 = 옛 대비 기준 × (새/옛 기준 비율) 뒤 하한 —
    /// 기준 색이 회색·어두워도 대비 그룹(①·② 원·TV 꺼짐)이 같이 죽지 않게.
    /// </summary>
    public static Color ContrastBase(Color refBase, Color newBase, Color refContrast, in Inputs inputs)
    {
        if (inputs.ContrastMode == BossVictoryContrastMode.Manual) return inputs.ContrastColor;
        Color.RGBToHSV(refBase, out _, out float rs, out float rv);
        Color.RGBToHSV(newBase, out float nh, out float ns, out float nv);
        Color.RGBToHSV(refContrast, out _, out float cs, out float cv);
        float s = cs * Ratio(ns, rs);
        float v = cv * Ratio(nv, rv);
        s = Mathf.Clamp01(Mathf.Max(s, Mathf.Clamp01(inputs.ContrastMinSaturation)));
        v = Mathf.Max(v, Mathf.Max(0f, inputs.ContrastMinValue));
        if (Max3(refContrast) <= 1f) v = Mathf.Min(v, 1f);
        float h = Mathf.Repeat(nh + inputs.ContrastHueOffset / 360f, 1f);
        return WithAlpha(Color.HSVToRGB(h, s, v, true), refContrast.a);
    }

    /// <summary>
    /// <paramref name="c"/> 를 옛 앵커 → 새 앵커 관계로 옮긴다: 색조 += (새 − 옛), 채도·밝기 ×= (새/옛). 무채색(채도 &lt; <see cref="NeutralSaturation"/>)은 그대로.
    /// 1 이하(LDR) 색은 밝기 1 에서 자른다 — HDR(1 초과) 색은 세기 그대로 비율만.
    /// </summary>
    public static Color Follow(Color c, Color oldAnchor, Color newAnchor)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        if (s < NeutralSaturation) return c;
        Color.RGBToHSV(oldAnchor, out float oh, out float os, out float ov);
        Color.RGBToHSV(newAnchor, out float nh, out float ns, out float nv);
        float dh = os < NeutralSaturation ? 0f : nh - oh;   // 옛 앵커가 무채색이면 색조 기준이 없다 — 색조는 그대로
        float s2 = Mathf.Clamp01(s * Ratio(ns, os));
        float v2 = v * Ratio(nv, ov);
        if (Max3(c) <= 1f) v2 = Mathf.Min(v2, 1f);
        return WithAlpha(Color.HSVToRGB(Mathf.Repeat(h + dh, 1f), s2, v2, true), c.a);
    }

    /// <summary>팔라딘 결과가 공용 설정 그대로 나오는 색조 차이(도, −180~180) — 창 '역산' 버튼.</summary>
    public static float ReferenceContrastHueOffset(Color refBase, Color refContrast)
    {
        Color.RGBToHSV(refBase, out float bh, out _, out _);
        Color.RGBToHSV(refContrast, out float ch, out _, out _);
        return Mathf.DeltaAngle(0f, (ch - bh) * 360f);
    }

    public static float ReferenceContrastHueOffset(BossVictoryGlitchSettings s) =>
        ReferenceContrastHueOffset(s.amberColor, s.crtOffGlowColor);

    static float Ratio(float now, float reference) => reference > Epsilon ? now / reference : 1f;
    static float Max3(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));
    static Color WithAlpha(Color c, float a) { c.a = a; return c; }
}
