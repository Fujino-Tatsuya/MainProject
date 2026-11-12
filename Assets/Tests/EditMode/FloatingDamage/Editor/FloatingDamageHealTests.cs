using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>힐 숫자(할 일 268) — 늘 힐한 사람·대상별 합산, 제압 가속 합산 안 탐, 피해 숫자와 키가 겹치지 않음, 초록 스타일.</summary>
public sealed class FloatingDamageHealTests
{
    private const string SettingsPath = "Assets/9.ScriptableObject/UI/FloatingDamageSettings.asset";

    [Test]
    public void 힐은_늘_합산()
    {
        Assert.AreEqual(FloatingDamageAccumulation.Multi,
            FloatingDamageAccumulationPolicy.Resolve(PopupKind.Heal, AttackHitPattern.Single, false));
        Assert.AreEqual(FloatingDamageAccumulation.Multi,
            FloatingDamageAccumulationPolicy.Resolve(PopupKind.Heal, AttackHitPattern.Single, true), "제압 가속 중에도 Multi");
    }

    [Test]
    public void 피해는_기존_규칙_그대로()
    {
        Assert.AreEqual(FloatingDamageAccumulation.None,
            FloatingDamageAccumulationPolicy.Resolve(PopupKind.Damage, AttackHitPattern.Single, false));
        Assert.AreEqual(FloatingDamageAccumulation.SuppressHaste,
            FloatingDamageAccumulationPolicy.Resolve(PopupKind.Damage, AttackHitPattern.Single, true));
    }

    [Test]
    public void 힐_키는_힐한_사람별로_갈리고_피해_키와_다르다()
    {
        Assert.IsTrue(FloatingDamageAccumulationPolicy.TryCreateKey(1UL, AttackType.Default, 7, PopupKind.Heal,
            FloatingDamageAccumulation.Multi, out FloatingDamageAccumulationKey healA));
        FloatingDamageAccumulationPolicy.TryCreateKey(2UL, AttackType.Default, 7, PopupKind.Heal,
            FloatingDamageAccumulation.Multi, out FloatingDamageAccumulationKey healB);
        FloatingDamageAccumulationPolicy.TryCreateKey(1UL, AttackType.Default, 7, PopupKind.Damage,
            FloatingDamageAccumulation.Multi, out FloatingDamageAccumulationKey damage);

        Assert.AreNotEqual(healA, healB);
        Assert.AreNotEqual(healA, damage);
    }

    [Test]
    public void 힐_스타일은_초록()
    {
        var settings = AssetDatabase.LoadAssetAtPath<FloatingDamageSettings>(SettingsPath);
        Assert.IsNotNull(settings, SettingsPath);
        Assert.IsTrue(settings.TryGetStyle(PopupKind.Heal, out FloatingPopupStyle style));
        Assert.Greater(style.color.g, style.color.r);
        Assert.Greater(style.color.g, style.color.b);
    }
}
