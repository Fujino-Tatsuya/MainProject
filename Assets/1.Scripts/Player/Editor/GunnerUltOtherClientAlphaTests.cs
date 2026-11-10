using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 거너 R 다른 클라 화면 알파(2026-11-06, PLAN.md 「거너 R 다른 클라 화면 알파」) —
/// 시전자 판정(<see cref="GunnerTrackingLaserView.IsOtherClientView"/>)·배율 계산·머티리얼 교체와 반납 원복(<see cref="EffectMaterialAlpha"/>).
/// </summary>
public sealed class GunnerUltOtherClientAlphaTests
{
    readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in created)
            if (o != null) Object.DestroyImmediate(o);
        created.Clear();
    }

    [Test]
    public void CasterView_IsNotOther()
    {
        Assert.IsFalse(GunnerTrackingLaserView.IsOtherClientView(true, 2UL, 2UL));
    }

    [Test]
    public void NonCasterView_IncludingHost_IsOther()
    {
        Assert.IsTrue(GunnerTrackingLaserView.IsOtherClientView(true, 1UL, 2UL));   // 다른 클라
        Assert.IsTrue(GunnerTrackingLaserView.IsOtherClientView(true, 0UL, 2UL));   // 호스트(시전자 아님)
    }

    [Test]
    public void OfflineOrUnknownCaster_IsNotOther()
    {
        Assert.IsFalse(GunnerTrackingLaserView.IsOtherClientView(false, 0UL, 2UL));
        Assert.IsFalse(GunnerTrackingLaserView.IsOtherClientView(true, 1UL, GunnerTrackingLaserView.NoCaster));
    }

    [Test]
    public void Scaled_MultipliesAndClampsMultiplier()
    {
        Assert.That(EffectMaterialAlphaRule.Scaled(1f, 0.6f), Is.EqualTo(0.6f).Within(1e-5f));
        Assert.That(EffectMaterialAlphaRule.Scaled(0.5f, 0.6f), Is.EqualTo(0.3f).Within(1e-5f));
        Assert.That(EffectMaterialAlphaRule.Scaled(0.5f, 2f), Is.EqualTo(0.5f).Within(1e-5f));
    }

    [Test]
    public void ScaledColor_AlphaOrRgbOnly()
    {
        var c = new Color(2f, 4f, 8f, 0.5f);
        Color a = EffectMaterialAlphaRule.ScaledColor(c, 0.5f, EffectMaterialColorChannel.Alpha);
        Assert.That(a.a, Is.EqualTo(0.25f).Within(1e-5f));
        Assert.That(a.r, Is.EqualTo(2f).Within(1e-5f), "Alpha 는 RGB 를 안 건드림");

        Color rgb = EffectMaterialAlphaRule.ScaledColor(c, 0.5f, EffectMaterialColorChannel.Rgb);
        Assert.That(rgb.r, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(rgb.g, Is.EqualTo(2f).Within(1e-5f));
        Assert.That(rgb.b, Is.EqualTo(4f).Within(1e-5f));
        Assert.That(rgb.a, Is.EqualTo(0.5f).Within(1e-5f), "Rgb 는 a 를 안 건드림");
    }

    [Test]
    public void Apply_RgbRule_ScalesColorBrightnessOnCopy()
    {
        // 장판 바닥 선(HS_Blend_CG "Color" = _Color, a 미사용)처럼 Color 의 밝기만 낮춘다
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        Assume.That(shader, Is.Not.Null);

        var target = new Material(shader) { name = "AlphaTargetRgb" };
        created.Add(target);
        string colorProp = target.HasColor("_BaseColor") ? "_BaseColor" : "_Color";
        target.SetColor(colorProp, new Color(1f, 0.5f, 0.2f, 1f));

        var go = new GameObject("AlphaRgb");
        created.Add(go);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = target;

        EffectMaterialAlpha.Apply(go, new[] { new EffectMaterialAlphaRule(target, colorProp, 0.2f, EffectMaterialColorChannel.Rgb) });

        Color swapped = renderer.sharedMaterial.GetColor(colorProp);
        Assert.That(swapped.r, Is.EqualTo(0.2f).Within(1e-4f));
        Assert.That(swapped.g, Is.EqualTo(0.1f).Within(1e-4f));
        Assert.That(swapped.a, Is.EqualTo(1f).Within(1e-4f), "a 는 그대로");
        Assert.That(target.GetColor(colorProp).r, Is.EqualTo(1f).Within(1e-4f), "원본 에셋은 안 바뀜");
    }

    [Test]
    public void Apply_PropertyWithStrayWhitespace_StillApplies()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        Assume.That(shader, Is.Not.Null);

        var target = new Material(shader) { name = "AlphaTargetTrim" };
        created.Add(target);
        string colorProp = target.HasColor("_BaseColor") ? "_BaseColor" : "_Color";
        target.SetColor(colorProp, Color.white);

        var go = new GameObject("AlphaTrim");
        created.Add(go);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = target;

        EffectMaterialAlpha.Apply(go, new[] { new EffectMaterialAlphaRule(target, colorProp + "\t", 0.5f) });
        Assert.That(renderer.sharedMaterial.GetColor(colorProp).a, Is.EqualTo(0.5f).Within(1e-4f), "붙여 넣다 섞인 탭은 무시");
    }

    [Test]
    public void Apply_SwapsMatchingSlot_AndRestoreReturnsOriginal()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        Assume.That(shader, Is.Not.Null, "테스트용 셰이더를 찾지 못함");

        var target = new Material(shader) { name = "AlphaTarget" };
        var other = new Material(shader) { name = "AlphaOther" };
        created.Add(target);
        created.Add(other);
        string colorProp = target.HasColor("_BaseColor") ? "_BaseColor" : "_Color";
        target.SetColor(colorProp, new Color(1f, 0.5f, 0.2f, 1f));

        var go = new GameObject("AlphaInstance");
        created.Add(go);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { target, other };

        var rules = new[] { new EffectMaterialAlphaRule(target, colorProp, 0.6f) };
        EffectMaterialAlpha.Apply(go, rules);

        Material swapped = renderer.sharedMaterials[0];
        Assert.AreNotSame(target, swapped, "규칙 머티리얼 슬롯은 복사본으로");
        Assert.That(swapped.GetColor(colorProp).a, Is.EqualTo(0.6f).Within(1e-4f));
        Assert.That(swapped.GetColor(colorProp).r, Is.EqualTo(1f).Within(1e-4f), "색은 그대로");
        Assert.AreSame(other, renderer.sharedMaterials[1], "다른 슬롯은 그대로");
        Assert.That(target.GetColor(colorProp).a, Is.EqualTo(1f).Within(1e-4f), "원본 에셋은 안 바뀜");

        go.GetComponent<EffectMaterialAlpha>().Restore();
        Assert.AreSame(target, renderer.sharedMaterials[0], "반납 원복");
    }

    [Test]
    public void Apply_MultipleRulesOnSameMaterial_AllApplyToOneCopy()
    {
        // 장판 바닥 선처럼 알파(Color a) + 밝기(Float) 를 같은 머티리얼에 함께 건다
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        Assume.That(shader, Is.Not.Null, "URP Unlit 셰이더 필요");

        var target = new Material(shader) { name = "AlphaTargetMulti" };
        created.Add(target);
        Assume.That(target.HasColor("_BaseColor") && target.HasFloat("_Cutoff"), Is.True);
        target.SetColor("_BaseColor", new Color(1f, 1f, 1f, 1f));
        target.SetFloat("_Cutoff", 0.5f);

        var go = new GameObject("AlphaMulti");
        created.Add(go);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = target;

        var rules = new[]
        {
            new EffectMaterialAlphaRule(target, "_BaseColor", 0.5f),
            new EffectMaterialAlphaRule(target, "_Cutoff", 0.2f),
        };
        EffectMaterialAlpha.Apply(go, rules);

        Material swapped = renderer.sharedMaterial;
        Assert.AreNotSame(target, swapped);
        Assert.That(swapped.GetColor("_BaseColor").a, Is.EqualTo(0.5f).Within(1e-4f));
        Assert.That(swapped.GetFloat("_Cutoff"), Is.EqualTo(0.1f).Within(1e-4f));
    }

    [Test]
    public void Apply_SameRule_ReusesCachedCopy()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        Assume.That(shader, Is.Not.Null);

        var target = new Material(shader) { name = "AlphaTargetCache" };
        created.Add(target);
        string colorProp = target.HasColor("_BaseColor") ? "_BaseColor" : "_Color";
        var rules = new[] { new EffectMaterialAlphaRule(target, colorProp, 0.6f) };

        var a = new GameObject("A"); created.Add(a);
        var b = new GameObject("B"); created.Add(b);
        var ra = a.AddComponent<MeshRenderer>(); ra.sharedMaterial = target;
        var rb = b.AddComponent<MeshRenderer>(); rb.sharedMaterial = target;

        EffectMaterialAlpha.Apply(a, rules);
        EffectMaterialAlpha.Apply(b, rules);
        Assert.AreSame(ra.sharedMaterial, rb.sharedMaterial, "같은 (원본, 속성, 배율) = 같은 복사본");
    }
}
