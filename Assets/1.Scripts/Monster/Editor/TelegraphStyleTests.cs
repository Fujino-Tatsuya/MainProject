using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 공격 예고 스타일(PLAN-telegraph-style) — 기본값이 2026-10-13 화면(옛 코드 상수·드론/전기 장판 데이터 색)과 같은지,
/// 빈 칸 해석·팔레트 매핑, 몬스터 데이터 연결을 고정한다.
/// 🔴 기본값이 1 비트라도 바뀌면 아트가 아무것도 안 건드렸는데 화면이 바뀐다 — 여기서 잡는다.
/// </summary>
public sealed class TelegraphStyleTests
{
    static void AssertColor(Color expected, Color actual, string what)
    {
        Assert.That(actual.r, Is.EqualTo(expected.r).Within(1e-6f), what + ".r");
        Assert.That(actual.g, Is.EqualTo(expected.g).Within(1e-6f), what + ".g");
        Assert.That(actual.b, Is.EqualTo(expected.b).Within(1e-6f), what + ".b");
        Assert.That(actual.a, Is.EqualTo(expected.a).Within(1e-6f), what + ".a");
    }

    [Test]
    public void CodeDefaults_MatchPreviousScreen()
    {
        TelegraphStyleSO s = TelegraphStyleSO.Default;
        // 옛 BossAttackConeTelegraph 상수
        AssertColor(new Color(1f, 0.12f, 0.08f, 0.85f), s.attack.outline.color, "attack.outline");
        AssertColor(new Color(1f, 0.10f, 0.05f, 0.55f), s.attack.fill.color, "attack.fill");
        AssertColor(new Color(0.021842957f, 0f, 1f, 0.85f), s.interrupt.outline.color, "interrupt.outline");
        AssertColor(new Color(0.021842957f, 0f, 1f, 0.55f), s.interrupt.fill.color, "interrupt.fill");
        Assert.AreEqual(0.25f, s.attack.outlineWidth, "OutlineRimWorld 0.25");
        Assert.AreEqual(0.25f, s.interrupt.outlineWidth);
        // 옛 WellsDroneData · BossElectricFloorData 저작값 + BossPatternVisuals 텍스처 테두리
        AssertColor(new Color(1f, 0.15f, 0.1f, 0.3f), s.circle.outline.color, "circle.outline");
        AssertColor(new Color(1f, 0.05f, 0.02f, 0.6f), s.circle.fill.color, "circle.fill");
        AssertColor(new Color(1f, 0.15f, 0.1f, 0.28f), s.floorTile.outline.color, "floorTile.outline");
        AssertColor(new Color(1f, 0.05f, 0.02f, 0.55f), s.floorTile.fill.color, "floorTile.fill");
        Assert.AreEqual(0.08f, s.circle.rimRatio, "BuildDisc rim 0.08");
        Assert.AreEqual(0.1f, s.floorTile.rimRatio, "BuildSquare rim 0.1");
    }

    [Test]
    public void CodeDefaults_HaveNoMask()
    {
        TelegraphStyleSO s = TelegraphStyleSO.Default;
        foreach (TelegraphLayer l in new[] { s.attack.outline, s.attack.fill, s.interrupt.outline, s.interrupt.fill,
                                             s.circle.outline, s.circle.fill, s.floorTile.outline, s.floorTile.fill })
            Assert.IsFalse(l.mask.Active, "기본값에 마스크가 있으면 화면이 바뀐다");
    }

    [Test]
    public void Resolve_NullFallsBackToDefault_AndPaletteMaps()
    {
        Assert.AreSame(TelegraphStyleSO.Default, TelegraphStyleSO.Resolve(null));
        TelegraphStyleSO s = TelegraphStyleSO.Default;
        Assert.AreSame(s.attack, s.Decal(BossAttackConeTelegraph.Palette.Attack));
        Assert.AreSame(s.interrupt, s.Decal(BossAttackConeTelegraph.Palette.Interrupt));
    }

    [Test]
    public void MaskActive_NeedsTextureAndStrength()
    {
        var m = new TelegraphMask { texture = null, strength = 1f };
        Assert.IsFalse(m.Active);
        m.texture = Texture2D.whiteTexture;
        Assert.IsTrue(m.Active);
        m.strength = 0f;
        Assert.IsFalse(m.Active);
    }

    // 몬스터 데이터가 실제로 기본 스타일 에셋을 물고 있고, 그 에셋 값이 코드 기본값과 같다(아트가 고치기 전 상태).
    [TestCase("Assets/2.Prefabs/Monster/Data/No23.asset")]
    [TestCase("Assets/2.Prefabs/Monster/Data/No23_Solo.asset")]
    [TestCase("Assets/2.Prefabs/Monster/Data/GauntletBotData.asset")]
    [TestCase("Assets/2.Prefabs/Monster/Data/SpinnerBotData.asset")]
    public void MonsterData_IsWiredToAStyle(string path)
    {
        var data = AssetDatabase.LoadAssetAtPath<MonsterDataSO>(path);
        Assert.That(data, Is.Not.Null, path);
        Assert.That(data.telegraphStyle, Is.Not.Null, $"{path} 의 telegraphStyle 이 비었다 — Tools/Monster/공격 예고 스타일 준비");
    }

    [Test]
    public void TelegraphMaterials_LoadFromResources()
    {
        Material decal = Resources.Load<Material>("Telegraph/MA_TelegraphDecal");
        Material quad = Resources.Load<Material>("Telegraph/MA_TelegraphQuad");
        Assert.That(decal, Is.Not.Null, "공격 예고 데칼 재질 — 없으면 색만 구워 그리고 마스크가 안 먹는다");
        Assert.That(quad, Is.Not.Null, "드론·전기 장판 판 재질 — 없으면 Sprites/Default(마스크 없음)");
        Assert.IsTrue(decal.HasProperty("_BaseColor") && decal.HasProperty("_MaskTex") && decal.HasProperty("Base_Map"));
        Assert.IsTrue(quad.HasProperty("_Color") && quad.HasProperty("_MaskTex") && quad.HasProperty("_MainTex"));
    }
}
