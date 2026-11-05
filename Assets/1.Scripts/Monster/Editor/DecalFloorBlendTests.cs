using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 221 — 스킬 범위 데칼·공격 예고 바닥 블렌딩(채움 알파 배율·바깥 끝 소프트).
///   식 = DecalFloorBlend.cs(셰이더 DecalFloorBlend.hlsl 와 같은 줄 구성). 1.0·0 = 221 이전 화면.
///   설정 SO → 전역 값 반영(DecalFloorBlendDriver.Apply), 셰이더가 공용 hlsl 을 쓰는지 텍스트로 확인.
/// </summary>
public sealed class DecalFloorBlendTests
{
    const float Eps = 1e-5f;

    static readonly string[] BlendShaders =
    {
        "Assets/1.Scripts/Rendering/Telegraph/EnemyC3Decal.hlsl",
        "Assets/1.Scripts/Rendering/Telegraph/EnemyC3Plate.shader",
        "Assets/1.Scripts/Rendering/Telegraph/TelegraphQuad.shader",
        "Assets/3.Materials/Player/SkillLineIndicator/SkillLineIndicator.shader",
        "Assets/1.Scripts/Rendering/Telegraph/SkillRangeDecalAlpha.hlsl",
    };

    [Test]
    public void Identity_IsNoChange()
    {
        Vector3 r = DecalFloorBlend.Resolve(DecalFloorBlend.Identity);
        Assert.AreEqual(new Vector3(1f, 0f, 1f), r);
        for (float e = 0f; e <= 1f; e += 0.25f)
            Assert.AreEqual(1f, DecalFloorBlend.Fill(e, r.x), Eps, $"edge {e}");
        for (float o = -1f; o <= 2f; o += 0.25f)
            Assert.AreEqual(1f, DecalFloorBlend.OuterSoft(o, r.y), Eps, $"outer {o}");
        Assert.AreEqual(1f, DecalFloorBlend.PlateFill(DecalFloorBlend.Identity), Eps);
    }

    [Test]
    public void UnsetGlobal_ResolvesToNoChange()
    {
        Assert.AreEqual(new Vector3(1f, 0f, 1f), DecalFloorBlend.Resolve(Vector4.zero), "w = 0 → 221 이전 화면");
        Assert.AreEqual(new Vector3(1f, 0f, 1f), DecalFloorBlend.Resolve(new Vector4(0.2f, 0.9f, 0.3f, 0f)));
        Assert.AreEqual(1f, DecalFloorBlend.PlateFill(Vector4.zero), Eps);
    }

    [Test]
    public void Fill_ScalesInteriorOnly()
    {
        Assert.AreEqual(0.6f, DecalFloorBlend.Fill(0f, 0.6f), Eps, "채움");
        Assert.AreEqual(1f, DecalFloorBlend.Fill(1f, 0.6f), Eps, "가장자리는 그대로");
        Assert.AreEqual(0.8f, DecalFloorBlend.Fill(0.5f, 0.6f), Eps);
        Assert.AreEqual(0.6f, DecalFloorBlend.Fill(-3f, 0.6f), Eps, "가중치 자름");
    }

    [Test]
    public void OuterSoft_FadesOnlyTheOuterPart()
    {
        Assert.AreEqual(0f, DecalFloorBlend.OuterSoft(0f, 0.15f), Eps, "바깥 끝 = 0");
        Assert.AreEqual(1f, DecalFloorBlend.OuterSoft(0.15f, 0.15f), Eps, "소프트 폭 안쪽부터 그대로");
        Assert.AreEqual(1f, DecalFloorBlend.OuterSoft(0.5f, 0.15f), Eps, "외곽선 나머지는 또렷");
        Assert.AreEqual(0.5f, DecalFloorBlend.OuterSoft(0.075f, 0.15f), Eps, "smoothstep 가운데");
        float prev = 0f;
        for (float o = 0.01f; o <= 0.15f; o += 0.01f)
        {
            float v = DecalFloorBlend.OuterSoft(o, 0.15f);
            Assert.GreaterOrEqual(v, prev, $"o={o}");
            prev = v;
        }
    }

    [Test]
    public void ToGlobal_ClampsAndMarksValid()
    {
        Assert.AreEqual(new Vector4(0.6f, 0.15f, 1f, 1f), DecalFloorBlend.ToGlobal(0.6f, 0.15f, 1f));
        Assert.AreEqual(new Vector4(1f, 0f, 0f, 1f), DecalFloorBlend.ToGlobal(2f, -1f, -5f));
        Assert.AreEqual(DecalFloorBlend.Identity, DecalFloorBlend.ToGlobal(1f, 0f, 1f));
    }

    [Test]
    public void Settings_CodeDefaults_AreNoChange()
    {
        var s = ScriptableObject.CreateInstance<DecalFloorBlendSettings>();
        try
        {
            Assert.AreEqual(DecalFloorBlend.Identity, s.SkillGlobal());
            Assert.AreEqual(DecalFloorBlend.Identity, s.TelegraphGlobal());
            Assert.AreEqual(DecalFloorBlend.EdgeIdentity, s.SkillEdgeGlobal(), "외곽선 코드 기본 = 지금 화면");
            Assert.AreEqual(DecalFloorBlend.EdgeIdentity, s.TelegraphEdgeGlobal());
        }
        finally { Object.DestroyImmediate(s); }
    }

    [Test]
    public void Driver_AppliesSettingsToGlobals_AndClearRestoresNoChange()
    {
        Vector4 skillBefore = Shader.GetGlobalVector(DecalFloorBlend.SkillGlobalId);
        Vector4 telegraphBefore = Shader.GetGlobalVector(DecalFloorBlend.TelegraphGlobalId);
        Vector4 skillEdgeBefore = Shader.GetGlobalVector(DecalFloorBlend.SkillEdgeGlobalId);
        Vector4 telegraphEdgeBefore = Shader.GetGlobalVector(DecalFloorBlend.TelegraphEdgeGlobalId);
        var s = ScriptableObject.CreateInstance<DecalFloorBlendSettings>();
        try
        {
            s.skillFillAlphaScale = 0.5f; s.skillEdgeSoftness = 0.2f; s.skillPlateFillAlphaScale = 0.9f;
            s.telegraphFillAlphaScale = 0.7f; s.telegraphEdgeSoftness = 0.1f; s.telegraphPlateFillAlphaScale = 0.8f;
            s.skillOutlineAlphaScale = 0.6f; s.skillOutlineStain = 0.4f; s.skillStainMode = DecalFloorStainMode.Multiply;
            s.telegraphOutlineAlphaScale = 0.9f; s.telegraphOutlineStain = 0.3f;
            DecalFloorBlendDriver.Apply(s);
            Assert.AreEqual(new Vector4(0.6f, 0.4f, 1f, 1f), Shader.GetGlobalVector(DecalFloorBlend.SkillEdgeGlobalId));
            Assert.AreEqual(new Vector4(0.9f, 0.3f, 0f, 1f), Shader.GetGlobalVector(DecalFloorBlend.TelegraphEdgeGlobalId));
            Assert.AreEqual(new Vector4(0.5f, 0.2f, 0.9f, 1f), Shader.GetGlobalVector(DecalFloorBlend.SkillGlobalId));
            Assert.AreEqual(new Vector4(0.7f, 0.1f, 0.8f, 1f), Shader.GetGlobalVector(DecalFloorBlend.TelegraphGlobalId));

            DecalFloorBlendDriver.Apply(null);
            Assert.AreEqual(new Vector3(1f, 0f, 1f), DecalFloorBlend.Resolve(Shader.GetGlobalVector(DecalFloorBlend.SkillGlobalId)));
            Assert.AreEqual(new Vector3(1f, 0f, 1f), DecalFloorBlend.Resolve(Shader.GetGlobalVector(DecalFloorBlend.TelegraphGlobalId)));
            Assert.AreEqual(new Vector3(1f, 0f, 0f), DecalFloorBlend.ResolveEdge(Shader.GetGlobalVector(DecalFloorBlend.SkillEdgeGlobalId)));
            Assert.AreEqual(new Vector3(1f, 0f, 0f), DecalFloorBlend.ResolveEdge(Shader.GetGlobalVector(DecalFloorBlend.TelegraphEdgeGlobalId)));
        }
        finally
        {
            Object.DestroyImmediate(s);
            Shader.SetGlobalVector(DecalFloorBlend.SkillGlobalId, skillBefore);
            Shader.SetGlobalVector(DecalFloorBlend.TelegraphGlobalId, telegraphBefore);
            Shader.SetGlobalVector(DecalFloorBlend.SkillEdgeGlobalId, skillEdgeBefore);
            Shader.SetGlobalVector(DecalFloorBlend.TelegraphEdgeGlobalId, telegraphEdgeBefore);
        }
    }

    [Test]
    public void AimIndicatorColor_ScalesAlphaOnly()
    {
        var baseColor = new Color(0.2f, 0.4f, 0.6f, 0.8f);
        Assert.AreEqual(baseColor, PlayerAimIndicator.AimIndicatorColor(baseColor, Color.white, 1f), "1 = 그대로");
        Color c = PlayerAimIndicator.AimIndicatorColor(baseColor, Color.white, 0.5f);
        Assert.AreEqual(0.4f, c.a, Eps);
        Assert.AreEqual(baseColor.r, c.r, Eps);
    }

    [Test]
    public void ResourceAsset_Exists_WithComparisonStartValues()
    {
        var s = Resources.Load<DecalFloorBlendSettings>(DecalFloorBlendSettings.ResourcePath);
        Assert.IsNotNull(s, "Resources/" + DecalFloorBlendSettings.ResourcePath);
        Assert.Greater(s.telegraphFillAlphaScale, 0.3f, "예고는 회피 신호 — 너무 옅게 하지 말 것");
        Assert.Greater(s.skillFillAlphaScale, 0f);
        Assert.GreaterOrEqual(s.telegraphOutlineAlphaScale, 0.7f, "예고 외곽선은 회피 신호 — 너무 낮추지 말 것");
        Assert.LessOrEqual(s.telegraphOutlineStain, 0.8f, "완전 물들임이면 어두운 바닥에서 외곽선이 묻힌다");
    }

    // ── 221 후속 — 외곽선 물들임·양쪽 소프트 ─────────────────────────────

    static readonly Color White = new Color(1f, 1f, 1f, 0.95f);

    [Test]
    public void Stain_Identity_IsNoChange()
    {
        foreach (Color c in new[] { White, new Color(0.74f, 0.5f, 0.47f, 1f), new Color(0f, 0f, 0f, 0.3f) })
        {
            Color o = DecalFloorBlend.Stain(c, DecalFloorBlend.ResolveEdge(DecalFloorBlend.EdgeIdentity), DecalFloorBlend.RefFloorGamma);
            Assert.AreEqual(c.r, o.r, Eps); Assert.AreEqual(c.g, o.g, Eps); Assert.AreEqual(c.b, o.b, Eps); Assert.AreEqual(c.a, o.a, Eps);
            Color u = DecalFloorBlend.Stain(c, DecalFloorBlend.ResolveEdge(Vector4.zero), DecalFloorBlend.RefFloorGamma);
            Assert.AreEqual(c.a, u.a, Eps, "값 없음 = 그대로");
        }
    }

    [Test]
    public void Stain_AlphaScale_OnlyScalesAlpha()
    {
        Color o = DecalFloorBlend.Stain(White, new Vector3(0.5f, 0f, 0f), DecalFloorBlend.RefFloorGamma);
        Assert.AreEqual(0.475f, o.a, Eps);
        Assert.AreEqual(1f, o.r, Eps);
    }

    [TestCase(DecalFloorStainMode.LuminanceKeep)]
    [TestCase(DecalFloorStainMode.Multiply)]
    public void Stain_Full_KeepsHalfTheFloor(DecalFloorStainMode mode)
    {
        Color o = DecalFloorBlend.Stain(White, new Vector3(1f, 1f, (float)mode), DecalFloorBlend.RefFloorGamma);
        Assert.AreEqual(White.a * (1f - DecalFloorBlend.StainKeep), o.a, Eps, "완전 물들임 = 알파 × (1 − 남김)");
    }

    [Test]
    public void Stain_Luminance_PullsBrightnessTowardFloor_KeepsHue()
    {
        Color o = DecalFloorBlend.Stain(White, new Vector3(1f, 1f, 0f), DecalFloorBlend.RefFloorGamma);
        float expected = Mathf.Lerp(1f, DecalFloorBlend.RefFloorGamma, 0.5f);
        Assert.AreEqual(expected, o.r, Eps, "흰 선 → 기준 바닥과 흰색의 중간 밝기");
        Assert.Greater(o.r, DecalFloorBlend.RefFloorGamma + 0.2f, "어두운 바닥보다 충분히 밝게 남는다(회피 신호)");
        Color red = DecalFloorBlend.Stain(new Color(0.8f, 0.2f, 0.2f, 1f), new Vector3(1f, 1f, 0f), DecalFloorBlend.RefFloorGamma);
        Assert.Greater(red.r, red.g * 2f, "색조 유지");
    }

    [Test]
    public void Stain_Multiply_MatchesFloorTimesColor_AtReferenceFloor()
    {
        var c = new Color(0.74f, 0.5f, 0.47f, 1f);
        Color o = DecalFloorBlend.Stain(c, new Vector3(1f, 1f, 1f), DecalFloorBlend.RefFloorGamma);
        float f = DecalFloorBlend.RefFloorGamma;
        // 기준 바닥 위 DBuffer lerp 결과 = F·(1 − a) + D·a 가 F·c 와 같다(자름이 안 걸리는 채널).
        Assert.AreEqual(f * c.r, f * (1f - o.a) + o.r * o.a, 1e-4f);
        Assert.AreEqual(f * c.g, f * (1f - o.a) + o.g * o.a, 1e-4f);
    }

    [Test]
    public void LineSoft_SoftensBothEdges()
    {
        Assert.AreEqual(1f, DecalFloorBlend.LineSoft(0f, 0f), Eps, "소프트 0 = 그대로");
        Assert.AreEqual(0f, DecalFloorBlend.LineSoft(0f, 0.5f), Eps, "안쪽 끝");
        Assert.AreEqual(0f, DecalFloorBlend.LineSoft(1f, 0.5f), Eps, "바깥 끝");
        Assert.AreEqual(1f, DecalFloorBlend.LineSoft(0.5f, 0.5f), Eps, "가운데는 그대로");
        Assert.AreEqual(DecalFloorBlend.LineSoft(0.2f, 0.5f), DecalFloorBlend.LineSoft(0.8f, 0.5f), Eps, "대칭");
    }

    [Test]
    public void Shaders_UseSharedFloorBlend()
    {
        foreach (string path in BlendShaders)
        {
            Assert.IsTrue(File.Exists(path), path);
            StringAssert.Contains("DecalFloorBlend", File.ReadAllText(path), path);
        }
        string range = File.ReadAllText("Assets/3.Materials/Player/SkillRangeIndicator/SkillRangeIndicator.shadergraph");
        StringAssert.Contains("\"m_FunctionName\": \"SkillRangeDecalFade\"", range);
        StringAssert.Contains("\"m_ShaderOutputName\": \"ColorIn\"", range, "221 후속 — 색도 함수를 거친다(외곽선 물들임)");
        string guid = AssetDatabase.AssetPathToGUID("Assets/1.Scripts/Rendering/Telegraph/SkillRangeDecalAlpha.hlsl");
        Assert.IsNotEmpty(guid);
        StringAssert.Contains("\"m_FunctionSource\": \"" + guid + "\"", range);
    }

    [Test]
    public void BlendShaders_Compile()
    {
        foreach (string name in new[] { "Monster/EnemyC3Plate", "Monster/TelegraphQuad", "VeyTrace/Player/Skill Line Indicator" })
        {
            Shader sh = Shader.Find(name);
            Assert.IsNotNull(sh, name);
            Assert.IsFalse(ShaderUtil.ShaderHasError(sh), name + " has compile errors");
        }
        foreach (string path in new[]
                 {
                     "Assets/3.Materials/Player/SkillRangeIndicator/SkillRangeIndicator.shadergraph",
                     "Assets/1.Scripts/Rendering/Telegraph/SG_EnemyC3Decal.shadergraph",
                 })
        {
            var sh = AssetDatabase.LoadAssetAtPath<Shader>(path);
            Assert.IsNotNull(sh, path);
            Assert.IsFalse(ShaderUtil.ShaderHasError(sh), path + " has compile errors");
        }
    }
}
