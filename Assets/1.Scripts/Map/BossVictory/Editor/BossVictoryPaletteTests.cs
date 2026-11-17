using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 처치 연출 팔레트(할 일 271, 11-16 관계 끊음) — 칸마다 독립 색: 프로필 칸 그대로 · 한 칸을 바꿔도 다른 칸 그대로 · 모자란 칸·프로필 없음 = 코드 기본 색.
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictoryPalette).
/// </summary>
public sealed class BossVictoryPaletteTests
{
    const string ProfileDir = "Assets/Resources/BossVictory/Profiles/";

    [Test]
    public void 프로필_3개는_칸이_모두_있다()
    {
        foreach (string ch in new[] { "Paladin", "Gunner", "Assassin" })
        {
            var p = AssetDatabase.LoadAssetAtPath<BossVictoryProfile>($"{ProfileDir}BossVictoryProfile_{ch}.asset");
            Assert.IsNotNull(p, ch);
            Assert.IsNotNull(p.paletteColors, $"{ch} paletteColors");
            Assert.AreEqual(BossVictoryPalette.Count, p.paletteColors.Length, $"{ch} 칸 수");
        }
    }

    [Test]
    public void 팔레트는_프로필_칸_그대로이고_칸끼리_관계_없다()
    {
        var p = ScriptableObject.CreateInstance<BossVictoryProfile>();
        try
        {
            Color[] before = (Color[])p.paletteColors.Clone();
            p.paletteColors[(int)BossVictoryPaletteSlot.Amber] = Color.green;   // 예전 '기준 색' 칸
            BossVictoryPalette palette = BossVictoryPalette.From(p);
            Assert.AreEqual(Color.green, palette[BossVictoryPaletteSlot.Amber]);
            foreach (BossVictoryPaletteSlot slot in BossVictoryPalette.Slots)
                if (slot != BossVictoryPaletteSlot.Amber)
                    Assert.AreEqual(before[(int)slot], palette[slot], $"{slot} 는 그대로");
        }
        finally { Object.DestroyImmediate(p); }
    }

    [Test]
    public void 프로필_없음과_모자란_칸은_코드_기본_색()
    {
        Color[] defaults = BossVictoryPalette.CreateDefaultColors();
        Assert.AreEqual(BossVictoryPalette.Count, defaults.Length, "기본 색 칸 수");
        Assert.AreEqual(BossVictoryPalette.Count, BossVictoryPalette.Slots.Length, "칸 수 = enum 수");

        BossVictoryPalette none = BossVictoryPalette.From(null);
        foreach (BossVictoryPaletteSlot slot in BossVictoryPalette.Slots)
            Assert.AreEqual(defaults[(int)slot], none[slot], $"프로필 없음 {slot}");

        var p = ScriptableObject.CreateInstance<BossVictoryProfile>();
        try
        {
            p.paletteColors = new[] { Color.red, Color.blue };
            BossVictoryPalette shortP = BossVictoryPalette.From(p);
            Assert.AreEqual(Color.red, shortP[BossVictoryPaletteSlot.Amber]);
            Assert.AreEqual(Color.blue, shortP[BossVictoryPaletteSlot.NoiseBright]);
            Assert.AreEqual(defaults[(int)BossVictoryPaletteSlot.RimEdge], shortP[BossVictoryPaletteSlot.RimEdge], "모자란 칸 = 기본 색");
        }
        finally { Object.DestroyImmediate(p); }
    }

    [Test]
    public void 코드_기본_색은_매번_새_배열()
    {
        Color[] a = BossVictoryPalette.CreateDefaultColors();
        a[0] = Color.black;
        Assert.AreNotEqual(Color.black, BossVictoryPalette.CreateDefaultColors()[0], "프로필끼리 배열을 공유하지 않는다");
    }
}
