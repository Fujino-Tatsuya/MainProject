using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// RECTIFY 주황 채움 = 캐릭터별 프로필(11-12) — 세 프로필 값 = 옮기기 전 공용 값 · 렌더 값(<see cref="BossVictoryOrangeLook"/>)·팔레트 기준 색은 처치자 프로필에서.
/// </summary>
public sealed class BossVictoryOrangeProfileTests
{
    const string ProfileDir = "Assets/Resources/BossVictory/Profiles/";
    const float Tolerance = 1e-5f;

    [Test]
    public void 프로필_3개_주황_값은_옮기기_전_공용_값()
    {
        foreach (string ch in new[] { "Paladin", "Gunner", "Assassin" })
        {
            var p = AssetDatabase.LoadAssetAtPath<BossVictoryProfile>($"{ProfileDir}BossVictoryProfile_{ch}.asset");
            Assert.IsNotNull(p, ch);
            Assert.AreEqual(0.05f, p.rectifyOrangeHold, Tolerance, $"{ch} Hold");
            Assert.AreEqual(0.1f, p.rectifyOrangeWipe, Tolerance, $"{ch} Wipe");
            Assert.IsFalse(p.rectifyOrangeRightToLeft, $"{ch} RightToLeft");
            Assert.AreEqual(0.2f, p.rectifyOrangeJitter, Tolerance, $"{ch} Jitter");
            Assert.AreEqual(4f, p.rectifyOrangeRowPixels, Tolerance, $"{ch} RowPixels");
            AssertColor(new Color(1f, 0.2696447f, 0f, 1f), p.rectifyOrangeLeft, $"{ch} Left");
            AssertColor(new Color(1f, 0f, 0.455451f, 1f), p.rectifyOrangeRight, $"{ch} Right");
            Assert.AreEqual(1f, p.rectifyOrangeGradient, Tolerance, $"{ch} Gradient");
            Assert.AreEqual(0.003f, p.rectifyScanlineSpacing, Tolerance, $"{ch} ScanlineSpacing");
            Assert.AreEqual(0f, p.rectifyScanlineDark, Tolerance, $"{ch} ScanlineDark");
        }
    }

    [Test]
    public void 렌더_주황_값은_처치자_프로필_값()
    {
        var s = ScriptableObject.CreateInstance<BossVictoryGlitchSettings>();
        var paladin = ScriptableObject.CreateInstance<BossVictoryProfile>();
        var gunner = ScriptableObject.CreateInstance<BossVictoryProfile>();
        try
        {
            gunner.rectifyOrangeHold = 0.3f;
            gunner.rectifyOrangeRightToLeft = true;
            gunner.rectifyOrangeRowPixels = 0.2f;   // 하한 1 로 잘림
            gunner.rectifyOrangeLeft = Color.green;
            gunner.rectifyOrangeRight = Color.blue;
            gunner.rectifyScanlineDark = 0.4f;
            s.characterProfiles = new List<BossVictoryGlitchSettings.CharacterProfile>
            {
                new BossVictoryGlitchSettings.CharacterProfile { character = TutorialCharacter.Paladin, profile = paladin },
                new BossVictoryGlitchSettings.CharacterProfile { character = TutorialCharacter.Gunner, profile = gunner },
            };

            BossVictoryOrangeLook look = BossVictoryOrangeLook.From(s.ProfileFor(TutorialCharacter.Gunner));
            Assert.AreEqual(0.3f, look.Hold, Tolerance);
            Assert.IsTrue(look.RightToLeft);
            Assert.AreEqual(1f, look.RowPixels, Tolerance);
            Assert.AreEqual(0.4f, look.ScanlineDark, Tolerance);
            Color[] reference = BossVictoryPaletteCalc.ReferenceColors(s, gunner);
            AssertColor(Color.green, reference[(int)BossVictoryPaletteSlot.OrangeLeft], "팔레트 기준 왼쪽 = 처치자 프로필");
            AssertColor(Color.blue, reference[(int)BossVictoryPaletteSlot.OrangeRight], "팔레트 기준 오른쪽 = 처치자 프로필");

            BossVictoryOrangeLook pal = BossVictoryOrangeLook.From(s.ProfileFor(TutorialCharacter.Paladin));
            Assert.AreEqual(BossVictoryOrangeLook.DefaultHold, pal.Hold, Tolerance, "다른 캐릭터 값은 섞이지 않음");
            Assert.IsFalse(pal.RightToLeft);
            BossVictoryOrangeLook none = BossVictoryOrangeLook.From(null);
            AssertColor(BossVictoryOrangeLook.DefaultLeft, none.Left, "프로필 없음 = 코드 기본값");
        }
        finally
        {
            Object.DestroyImmediate(gunner);
            Object.DestroyImmediate(paladin);
            Object.DestroyImmediate(s);
        }
    }

    static void AssertColor(Color expected, Color actual, string what)
    {
        Assert.AreEqual(expected.r, actual.r, 1e-4f, what + " r");
        Assert.AreEqual(expected.g, actual.g, 1e-4f, what + " g");
        Assert.AreEqual(expected.b, actual.b, 1e-4f, what + " b");
    }
}
