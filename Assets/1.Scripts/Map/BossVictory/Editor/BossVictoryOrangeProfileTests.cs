using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// RECTIFY 주황 채움 = 캐릭터별 프로필(11-12) — 세 프로필에 칸 존재·코드 기본값 = 옮기기 전 공용 값 · 렌더 값(<see cref="BossVictoryOrangeLook"/>)·팔레트 기준 색은 처치자 프로필에서.
/// </summary>
public sealed class BossVictoryOrangeProfileTests
{
    const string ProfileDir = "Assets/Resources/BossVictory/Profiles/";
    const float Tolerance = 1e-5f;

    // 세 프로필 값은 사용자 튜닝 대상이라 고정값으로 비교하지 않는다(272 후속) — 칸이 직렬화돼 있는지 + 코드 기본값 = 옮기기 전 공용 값(관계)만 고정.
    static readonly string[] OrangeFields =
    {
        nameof(BossVictoryProfile.rectifyOrangeHold), nameof(BossVictoryProfile.rectifyOrangeWipe), nameof(BossVictoryProfile.rectifyOrangeRightToLeft),
        nameof(BossVictoryProfile.rectifyOrangeJitter), nameof(BossVictoryProfile.rectifyOrangeRowPixels), nameof(BossVictoryProfile.rectifyOrangeLeft),
        nameof(BossVictoryProfile.rectifyOrangeRight), nameof(BossVictoryProfile.rectifyOrangeGradient), nameof(BossVictoryProfile.rectifyScanlineSpacing),
        nameof(BossVictoryProfile.rectifyScanlineDark),
    };

    [Test]
    public void 프로필_3개에_주황_칸이_있고_코드_기본값은_옮기기_전_공용_값()
    {
        foreach (string ch in new[] { "Paladin", "Gunner", "Assassin" })
        {
            var p = AssetDatabase.LoadAssetAtPath<BossVictoryProfile>($"{ProfileDir}BossVictoryProfile_{ch}.asset");
            Assert.IsNotNull(p, ch);
            var so = new SerializedObject(p);
            foreach (string field in OrangeFields)
                Assert.IsNotNull(so.FindProperty(field), $"{ch} {field} 칸");
        }

        var d = ScriptableObject.CreateInstance<BossVictoryProfile>();
        try
        {
            Assert.AreEqual(0.05f, d.rectifyOrangeHold, Tolerance, "Hold");
            Assert.AreEqual(0.1f, d.rectifyOrangeWipe, Tolerance, "Wipe");
            Assert.IsFalse(d.rectifyOrangeRightToLeft, "RightToLeft");
            Assert.AreEqual(0.2f, d.rectifyOrangeJitter, Tolerance, "Jitter");
            Assert.AreEqual(4f, d.rectifyOrangeRowPixels, Tolerance, "RowPixels");
            AssertColor(new Color(1f, 0.2696447f, 0f, 1f), d.rectifyOrangeLeft, "Left");
            AssertColor(new Color(1f, 0f, 0.455451f, 1f), d.rectifyOrangeRight, "Right");
            Assert.AreEqual(1f, d.rectifyOrangeGradient, Tolerance, "Gradient");
            Assert.AreEqual(0.003f, d.rectifyScanlineSpacing, Tolerance, "ScanlineSpacing");
            Assert.AreEqual(0f, d.rectifyScanlineDark, Tolerance, "ScanlineDark");
        }
        finally { Object.DestroyImmediate(d); }
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
