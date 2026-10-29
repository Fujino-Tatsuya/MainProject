using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 141 보스 쉴드 피해 숫자 — 저작 에셋의 ShieldDamage 스타일이 하늘색이고,
/// 한 타격이 실드와 체력을 함께 깎아 두 숫자가 같은 자리에 떠도 겹치지 않는 간격인지 본다.
/// </summary>
public sealed class FloatingDamageShieldStyleTests
{
    const string SettingsPath = "Assets/9.ScriptableObject/UI/FloatingDamageSettings.asset";

    FloatingDamageSettings _settings;

    [SetUp]
    public void SetUp()
    {
        _settings = AssetDatabase.LoadAssetAtPath<FloatingDamageSettings>(SettingsPath);
        Assert.That(_settings, Is.Not.Null, SettingsPath);
    }

    [Test]
    public void ShieldDamage_IsLightSkyBlue()
    {
        Assert.That(_settings.TryGetStyle(PopupKind.ShieldDamage, out FloatingPopupStyle shield), Is.True);

        Color32 expected = new Color32(0x7F, 0xD8, 0xFF, 0xFF);
        Color32 actual = shield.color;
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void ShieldAndHpNumbers_DoNotOverlapVertically()
    {
        Assert.That(_settings.TryGetStyle(PopupKind.Damage, out FloatingPopupStyle hp), Is.True);
        Assert.That(_settings.TryGetStyle(PopupKind.ShieldDamage, out FloatingPopupStyle shield), Is.True);

        // 유지 크기 기준 최악의 경우 — 두 숫자가 모두 가장 큰 구간 + 무작위 분산이 서로 반대로 쏠린 경우.
        float maxHold = 0f;
        foreach (FloatingDamageTier tier in System.Enum.GetValues(typeof(FloatingDamageTier)))
            maxHold = Mathf.Max(maxHold, _settings.GetLook(tier).holdScale);

        float required = (hp.height + shield.height) * 0.5f * maxHold + _settings.SpawnScatter.y * 2f;
        float separation = Mathf.Abs(shield.offset.y - hp.offset.y);

        Assert.That(separation, Is.GreaterThanOrEqualTo(required),
            $"실드·체력 숫자 세로 간격 {separation}px < 필요 {required}px");
    }

    [Test]
    public void ShieldAndHp_UseDifferentAccumulationKeys()
    {
        FloatingDamageAccumulationPolicy.TryCreateKey(
            1UL, AttackType.Default, 10, PopupKind.Damage, AttackHitPattern.Multi, out FloatingDamageAccumulationKey hp);
        FloatingDamageAccumulationPolicy.TryCreateKey(
            1UL, AttackType.Default, 10, PopupKind.ShieldDamage, AttackHitPattern.Multi, out FloatingDamageAccumulationKey shield);

        Assert.That(shield, Is.Not.EqualTo(hp));
    }
}
