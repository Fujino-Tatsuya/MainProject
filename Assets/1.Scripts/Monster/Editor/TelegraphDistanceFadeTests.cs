using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 202 — 바닥 예고·표시 데칼의 거리 감쇠 그라데이션과 바닥 섞임(블렌딩) 설정.
///   감쇠 = 1 / (att0 + att1·d + att2·d²) → 0~1, 기본 (1, 0, 0) = 감쇠 없음(202 이전 화면).
///   셰이더 식(DecalDistanceFade.hlsl)은 C# <see cref="DecalDistanceFade.Evaluate"/> 와 같은 줄 구성이다.
///   계수 칸: C3 = EnemyC3Selected.json 항목별 distanceFade, 플레이어 = 재질 칸 _DecalFade.
///   블렌딩: 데칼 6종 그래프는 노멀을 쓰지 않는다(바닥 노멀맵·명암을 덮지 않게) — 아래 그래프 설정 테스트.
/// </summary>
public sealed class TelegraphDistanceFadeTests
{
    const float Eps = 1e-5f;

    static readonly string[] DecalGraphs =
    {
        "Assets/1.Scripts/Rendering/Telegraph/SG_EnemyC3Decal.shadergraph",
        "Assets/3.Materials/SG_TelegraphDecal.shadergraph",
        "Assets/3.Materials/SG_DecalFloorOnly.shadergraph",
        "Assets/3.Materials/SG_ColoredDecal.shadergraph",
        "Assets/3.Materials/Player/SkillRangeIndicator/SkillRangeIndicator.shadergraph",
        "Assets/3.Materials/Player/AimIndicator/AimIndicator.shadergraph",
    };

    static readonly string[] FadeMaterials =
    {
        "Assets/Resources/Telegraph/MA_EnemyC3Decal.mat",
        "Assets/Resources/Telegraph/MA_EnemyC3Plate.mat",
        "Assets/3.Materials/Player/SkillRangeIndicator/SkillRangeIndicator.mat",
        "Assets/3.Materials/Player/SkillRangeIndicator/SelectedTargetCircleC.mat",
        "Assets/3.Materials/Player/AimIndicator/AimIndicator.mat",
    };

    [TestCase(0f)]
    [TestCase(1f)]
    [TestCase(7.5f)]
    [TestCase(100f)]
    public void DefaultCoefficients_NoFade(float d)
    {
        Assert.AreEqual(new Vector3(1f, 0f, 0f), DecalDistanceFade.Default);
        Assert.AreEqual(1f, DecalDistanceFade.Evaluate(d, DecalDistanceFade.Default), Eps);
    }

    [Test]
    public void Formula_IsInverseQuadratic()
    {
        var att = new Vector3(1f, 0.5f, 0.1f);
        Assert.AreEqual(1f / (1f + 0.5f * 2f + 0.1f * 4f), DecalDistanceFade.Evaluate(2f, att), Eps);
        Assert.AreEqual(1f / (1f + 0.5f * 10f + 0.1f * 100f), DecalDistanceFade.Evaluate(10f, att), Eps);
        Assert.AreEqual(1f / (1f + 3f), DecalDistanceFade.Evaluate(3f, new Vector3(1f, 1f, 0f)), Eps);
        Assert.AreEqual(1f / (1f + 9f), DecalDistanceFade.Evaluate(3f, new Vector3(1f, 0f, 1f)), Eps);
    }

    [Test]
    public void Denominator_Guarded_AndClampedToOne()
    {
        Assert.AreEqual(1f, DecalDistanceFade.Evaluate(5f, Vector3.zero), Eps, "all zero → 1, not infinity/NaN");
        Assert.AreEqual(1f, DecalDistanceFade.Evaluate(5f, new Vector3(-1f, 0f, 0f)), Eps, "negative → 1");
        Assert.AreEqual(1f, DecalDistanceFade.Evaluate(0f, new Vector3(0.25f, 0f, 0f)), Eps, "near side never brighter than the original");
        Assert.IsFalse(float.IsNaN(DecalDistanceFade.Evaluate(0f, new Vector3(0f, 1f, 1f))));
    }

    [Test]
    public void PositiveCoefficients_DecreaseWithDistance()
    {
        var att = new Vector3(1f, 0.2f, 0.05f);
        float prev = DecalDistanceFade.Evaluate(0f, att);
        Assert.AreEqual(1f, prev, Eps, "at the player = original alpha");
        for (float d = 0.5f; d <= 40f; d += 0.5f)
        {
            float a = DecalDistanceFade.Evaluate(d, att);
            Assert.Less(a, prev, $"d={d}");
            Assert.GreaterOrEqual(a, 0f);
            prev = a;
        }
    }

    [Test]
    public void GroundDistance_IgnoresHeight()
    {
        Assert.AreEqual(5f, DecalDistanceFade.GroundDistance(new Vector3(0f, 0f, 0f), new Vector3(3f, 10f, 4f)), Eps);
    }

    [Test]
    public void GlobalValue_NoLocalPlayer_DisablesFade()
    {
        Assert.AreEqual(Vector4.zero, DecalDistanceFade.GlobalValue(null));
        Assert.AreEqual(new Vector4(2f, 0f, -3f, 1f), DecalDistanceFade.GlobalValue(new Vector3(2f, 5f, -3f)));
    }

    [Test]
    public void C3Preset_EveryEntry_DefaultsToNoFade()
    {
        foreach (string id in new[] { "01", "02", "03", "04", "05", "06", "07", "08", "09", "10", "11", "12", "14", "15", "16" })
            Assert.AreEqual(DecalDistanceFade.Default, EnemyC3Visuals.DistanceFade(id), id);
    }

    [Test]
    public void C3Plate_And_DecalValues_CarryFadeCoefficients()
    {
        var block = new MaterialPropertyBlock();
        EnemyC3Visuals.SetPlateProperties(block, "12", false, false);
        Assert.AreEqual(DecalDistanceFade.ToVector(EnemyC3Visuals.DistanceFade("12")), block.GetVector(DecalDistanceFade.MaterialPropertyId));

        Material shape = EnemyC3Visuals.DecalMaterial;
        Assume.That(shape, Is.Not.Null);
        var m = new Material(shape);
        try
        {
            EnemyC3Visuals.SetDecalProperties(m, "03", new Vector2(8f, 8f), 4f, 0f, 60f, 0f, 0f);
            Assert.AreEqual(DecalDistanceFade.ToVector(EnemyC3Visuals.DistanceFade("03")), m.GetVector(DecalDistanceFade.MaterialPropertyId));
        }
        finally { Object.DestroyImmediate(m); }
    }

    [Test]
    public void FadeMaterials_DeclareProperty_DefaultNoFade()
    {
        foreach (string path in FadeMaterials)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.IsNotNull(m, path);
            Assert.IsTrue(m.HasProperty(DecalDistanceFade.MaterialPropertyId), path + " must declare _DecalFade");
            Vector4 v = m.GetVector(DecalDistanceFade.MaterialPropertyId);
            Assert.AreEqual(DecalDistanceFade.Default, new Vector3(v.x, v.y, v.z), path);
        }
    }

    // 블렌딩(202) — 데칼이 노멀을 쓰면 바닥 노멀맵·명암이 데칼 영역(플레이어 그래프는 투영 상자 전체, NormalAlpha 1)에서 평평해져
    // 스티커처럼 떠 보였다. 6종 모두 Albedo 만 쓴다.
    [Test]
    public void DecalGraphs_DoNotOverwriteFloorNormals()
    {
        foreach (string path in DecalGraphs)
        {
            Assert.IsTrue(File.Exists(path), path);
            string text = File.ReadAllText(path);
            StringAssert.Contains("\"affectsAlbedo\": true", text, path);
            StringAssert.Contains("\"affectsNormal\": false", text, path);
            StringAssert.Contains("\"affectsEmission\": false", text, path + " (bloom is on hold)");
        }
    }

    [Test]
    public void PlayerGraphs_UseDistanceFade_AndAngleFade()
    {
        foreach (string path in new[] { DecalGraphs[4], DecalGraphs[5] })
        {
            string text = File.ReadAllText(path);
            // 할 일 221 — 스킬 범위 그래프는 거리 감쇠 × 바닥 블렌딩을 한 함수(SkillRangeDecalFade, 안에서 DecalDistanceFadeAt)로 부른다.
            string fn = path == DecalGraphs[4] ? "SkillRangeDecalFade" : "DecalDistanceFade";
            StringAssert.Contains("\"m_FunctionName\": \"" + fn + "\"", text, path);
            StringAssert.Contains("\"angleFade\": true", text, path);
        }
    }
}
