using System;
using UnityEngine;

/// <summary>
/// 처치 연출 팔레트 칸(할 일 271). 🔴 직렬화 순서(프로필 <see cref="BossVictoryProfile.paletteColors"/> 의 칸 번호)라 순서를 바꾸거나 중간에 끼우지 않는다 — 새 칸은 끝에.
/// </summary>
public enum BossVictoryPaletteSlot
{
    Amber = 0,          // 배경 호박색 틴트
    NoiseBright = 1,    // 보스 노이즈 밝은 색
    NoiseDark = 2,      // 보스 노이즈 어두운 색
    RectifyOutline = 3, // RECTIFY 외곽선
    Circle1Left = 4,    // ① 원 왼쪽
    Circle1Right = 5,   // ① 원 오른쪽
    Circle2Left = 6,    // ② 원 왼쪽
    Circle2Right = 7,   // ② 원 오른쪽
    Circle3Left = 8,    // ③ 흰 원 왼쪽
    Circle3Right = 9,   // ③ 흰 원 오른쪽
    CircleGray = 10,    // ③ 회색 단계
    OrangeLeft = 11,    // 주황 채움 왼쪽
    OrangeRight = 12,   // 주황 채움 오른쪽
    Glow = 13,          // RECTIFY 글로우 색
    CrtGlow = 14,       // TV 꺼짐 발광
    RimInside = 15,     // 보스 림라이트 안쪽 밝은 색(11-12)
    RimEdge = 16,       // 보스 림라이트 테두리 어두운 색(11-12)
}

/// <summary>
/// 처치 연출 팔레트 — 칸마다 sRGB 색 하나, 칸끼리 관계 없음(271 후속 11-16: 기준 색·대비·글자 기준 색으로 계산하던 관계를 끊고
/// 그때 화면에 보이던 색을 프로필 <see cref="BossVictoryProfile.paletteColors"/> 에 그대로 구워 넣었다). 문서 Docs/tech/boss-victory-character-profiles.md §4.11.
/// </summary>
public sealed class BossVictoryPalette
{
    public const int Count = 17;
    public static readonly BossVictoryPaletteSlot[] Slots = (BossVictoryPaletteSlot[])Enum.GetValues(typeof(BossVictoryPaletteSlot));

    readonly Color[] _colors = new Color[Count];

    public Color this[BossVictoryPaletteSlot slot] => _colors[(int)slot];

    /// <summary>
    /// 코드 기본 색(프로필이 없거나 칸이 모자랄 때) = 11-16 굽기 때 팔라딘 프로필 색. 칸 순서 = <see cref="BossVictoryPaletteSlot"/>.
    /// </summary>
    public static Color[] CreateDefaultColors() => new[]
    {
        new Color(0.9169811f, 0.67380774f, 0.29585615f, 1f),  // Amber
        new Color(1f, 0.9533913f, 0.8904821f, 1f),            // NoiseBright
        new Color(0.24852759f, 0.049765483f, 0.020591823f, 1f), // NoiseDark
        new Color(1f, 1f, 1f, 1f),                            // RectifyOutline
        new Color(0f, 1f, 0.62214947f, 1f),                   // Circle1Left
        new Color(0.6003031f, 1f, 0.003908813f, 1f),          // Circle1Right
        new Color(0.13950235f, 1f, 0.9874802f, 1f),           // Circle2Left
        new Color(0.27361375f, 0f, 0.65882355f, 1f),          // Circle2Right
        new Color(0f, 0.2278922f, 0.44992062f, 1f),           // Circle3Left
        new Color(0.4877374f, 0f, 0.9937061f, 1f),            // Circle3Right
        new Color(0.5411765f, 0.5411765f, 0.5411765f, 1f),    // CircleGray
        new Color(1f, 0.9899942f, 0f, 1f),                    // OrangeLeft
        new Color(1f, 0.41441518f, 0f, 1f),                   // OrangeRight
        new Color(1f, 1f, 1f, 1f),                            // Glow
        new Color(0.002605021f, 0.49167076f, 1f, 1f),         // CrtGlow
        new Color(1f, 0.9533913f, 0.8904821f, 1f),            // RimInside
        new Color(0.24852759f, 0.049765483f, 0.020591823f, 1f), // RimEdge
    };

    /// <summary>처치자 프로필 팔레트 — 프로필 칸 그대로(null·모자란 칸 = <see cref="CreateDefaultColors"/>).</summary>
    public static BossVictoryPalette From(BossVictoryProfile p)
    {
        var result = new BossVictoryPalette();
        Color[] defaults = CreateDefaultColors();
        Color[] src = p != null ? p.paletteColors : null;
        for (int i = 0; i < Count; i++)
            result._colors[i] = src != null && i < src.Length ? src[i] : defaults[i];
        return result;
    }
}
