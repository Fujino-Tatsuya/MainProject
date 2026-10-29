using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 거너 특성 정의 12개 + 1차 3개 수용 조건(할 일 127 1/4, 명세 11장 '단일 특성' — 단독 적용/미적용 비교).
/// 정의는 프로젝트 에셋(<c>9.ScriptableObject/Traits/Gunner/</c>)을 읽고, 수치는 서버·클라가 쓰는 순수 합성(<see cref="TraitModifierSet"/>)으로 본다.
/// </summary>
public class GunnerTraitTier1Tests
{
    private const string Folder = "Assets/9.ScriptableObject/Traits/Gunner/";

    private static readonly string[] SpecOrder =
    {
        "GUN_1_BASIC_DAMAGE", "GUN_1_CHARGE", "GUN_1_WIDTH",
        "GUN_2_OVERDRIVE", "GUN_2_HEAT_SHOT", "GUN_2_COOL_MODE",
        "GUN_3_Q_PUSH", "GUN_3_PIERCE", "GUN_3_VOLLEY",
        "GUN_4_R_FOCUS", "GUN_4_EXTRA_BASIC", "GUN_4_ECHO_Q",
    };

    private static readonly TraitAttackKey[] AllKeys =
    {
        new TraitAttackKey(AttackType.Default), new TraitAttackKey(AttackType.SkillQ), new TraitAttackKey(AttackType.SkillE),
        new TraitAttackKey(AttackType.SkillR), new TraitAttackKey(AttackType.Skill), new TraitAttackKey(AttackType.None),
    };

    private static GunnerTraitDefinition Load(string id)
    {
        var def = AssetDatabase.LoadAssetAtPath<GunnerTraitDefinition>(Folder + id + ".asset");
        Assert.IsNotNull(def, Folder + id + ".asset");
        return def;
    }

    private static TraitModifierSet With(params TraitDefinition[] defs)
    {
        var set = new TraitModifierSet();
        set.Rebuild(defs.Select(d => d.IdHash).ToList(), hash => defs.First(d => d.IdHash == hash));
        Assert.IsEmpty(set.Errors, string.Join("\n", set.Errors));
        return set;
    }

    private static TraitModifierSet None()
    {
        var set = new TraitModifierSet();
        set.Rebuild(new List<int>(), _ => null);
        return set;
    }

    private static GunnerChargeLaserData QData()
    {
        string guid = AssetDatabase.FindAssets("t:" + nameof(GunnerChargeLaserData), new[] { "Assets" }).FirstOrDefault();
        Assert.IsNotNull(guid, "GunnerChargeLaserData 에셋이 없다");
        return AssetDatabase.LoadAssetAtPath<GunnerChargeLaserData>(AssetDatabase.GUIDToAssetPath(guid));
    }

    // ── 정의·풀·문구 ─────────────────────────────────────────────────

    [Test]
    public void Pool_HasTwelveGunnerDefinitions_InSpecOrder_NamedById()
    {
        TraitCharacterPool pool = TraitContentTestKit.CharacterPool(TutorialCharacter.Gunner);
        Assert.IsNotNull(pool);
        CollectionAssert.AreEqual(SpecOrder, pool.Traits.Select(t => t.Id).ToArray());
        foreach (TraitDefinition def in pool.Traits)
        {
            Assert.IsInstanceOf<GunnerTraitDefinition>(def, def.Id);
            Assert.AreEqual(def.Id, def.name, "파일 이름 = 특성 ID(시트 열 이름)");
            Assert.IsFalse(string.IsNullOrWhiteSpace(def.InlineCategory), def.Id + " 분류");
            var errors = new List<string>();
            def.Validate(errors);
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }
    }

    [TestCase("GUN_1_BASIC_DAMAGE", "기본 공격 피해가 20% 증가합니다.")]
    [TestCase("GUN_1_CHARGE", "Q를 끝까지 충전하는 시간이 25% 줄어듭니다.")]
    [TestCase("GUN_1_WIDTH", "Q 레이저의 폭이 30% 넓어집니다.")]
    [TestCase("GUN_2_OVERDRIVE", "과열 단계가 높을수록 기본 공격과 Q, R이 더 강해집니다. 대신 기본 공격의 과열 증가량이 35% 늘어납니다.")]
    [TestCase("GUN_2_HEAT_SHOT", "Q를 발사하면 과열을 모두 소모합니다. 많이 소모할수록 Q 피해가 증가하며, 최대 60%까지 강해집니다.")]
    [TestCase("GUN_2_COOL_MODE", "과열과 과열 단계가 사라져 계속 사격할 수 있습니다. 대신 기본 공격 피해가 15% 감소하며, Q와 R의 과열 보너스도 사라집니다.")]
    [TestCase("GUN_3_Q_PUSH", "Q에 맞은 적을 레이저가 나아가는 방향으로 밀쳐냅니다.")]
    [TestCase("GUN_3_PIERCE", "기본 공격이 적을 관통합니다. 두 번째 적부터는 원래 피해의 65%를 줍니다.")]
    [TestCase("GUN_3_VOLLEY", "Q가 빠르게 5발을 연속 발사합니다. 한 발은 원래 피해의 28%, 모두 맞히면 총 140%의 피해를 줍니다.")]
    [TestCase("GUN_4_R_FOCUS", "자신의 R 레이저 범위 안에 있는 적에게 기본 공격과 Q 피해가 20% 증가합니다.")]
    public void CardText_MatchesSpecSelectionScreen(string id, string expected)
    {
        GunnerTraitDefinition def = Load(id);
        Assert.IsTrue(TraitTooltip.Format(def, def.InlineDescription, null, false, TraitContent.Build(), out string text, out string error), error);
        Assert.AreEqual(expected, text);
    }

    [TestCase("GUN_4_EXTRA_BASIC", "기본 공격을 3번 발사할 때마다 {0}의 피해를 주는 추가 레이저를 쏩니다. 추가 레이저는 과열을 올리지 않습니다.")]
    [TestCase("GUN_4_ECHO_Q", "Q 공격이 끝난 뒤 같은 방향으로 레이저를 한 번 더 쏴 {0}의 추가 피해를 줍니다.")]
    public void CardText_DamageToken_HasCalculator_AndShowsUnknownWithoutPlayer(string id, string format)
    {
        GunnerTraitDefinition def = Load(id);
        TraitContentRegistry registry = TraitContent.Build();
        Assert.IsTrue(registry.TryGetDamage(id, out _), id + " {dmg} 계산식");
        Assert.IsTrue(TraitTooltip.Format(def, def.InlineDescription, null, false, registry, out string text, out string error), error);
        Assert.AreEqual(string.Format(format, TraitUiStrings.DamageUnknown.Text), text);
    }

    [Test]
    public void Volley_TotalDamageRatio_IsShotCountTimesPerShot()
    {
        GunnerTraitDefinition def = Load("GUN_3_VOLLEY");
        Assert.AreEqual(5, GunnerTraitTooltip.VolleyShotCount(def));
        Assert.AreEqual(0.28f, GunnerTraitTooltip.VolleyPerShotRatio(def), 1e-6f);
        Assert.AreEqual(GunnerTraitTooltip.VolleyShotCount(def) * GunnerTraitTooltip.VolleyPerShotRatio(def), GunnerTraitTooltip.VolleyTotalRatio(def), 1e-6f);
        Assert.AreEqual(1.4f, GunnerTraitTooltip.VolleyTotalRatio(def), 1e-5f);
    }

    [Test]
    public void DerivedDamage_UsesEnhancedSourceOnce()
    {
        // 이중 출력 = 원래 기본 발 × 0.40. 출력 강화(1.2)는 원래 발 Dref 에 이미 들어 있고 다시 곱하지 않는다(명세 GUN_1_BASIC_DAMAGE).
        int dref = PlayerStatFormula.Snapshot(100, 1f, 0, 0f, With(Load("GUN_1_BASIC_DAMAGE")).DamageMultiplier(GunnerTraitRules.BasicAttackKey));
        Assert.AreEqual(120, dref);
        SkillTooltipDamage damage = GunnerTraitTooltip.DerivedDamage(dref, 1.2f, 0, 1f, 1.3f, 0.4f);
        Assert.AreEqual(48, damage.Minimum);
        Assert.AreEqual(62, damage.Maximum); // round(120×1.3)=156 × 0.4 = 62.4
    }

    // ── 1차 · 출력 강화 ───────────────────────────────────────────────

    [Test]
    public void BasicDamage_OnlyOriginalBasicAttack_Plus20Percent()
    {
        TraitModifierSet with = With(Load("GUN_1_BASIC_DAMAGE"));
        TraitModifierSet without = None();

        Assert.AreEqual(1.2f, with.DamageMultiplier(GunnerTraitRules.BasicAttackKey), 1e-6f);
        Assert.AreEqual(1f, without.DamageMultiplier(GunnerTraitRules.BasicAttackKey), 1e-6f);
        foreach (TraitAttackKey key in AllKeys.Where(k => k.Type != AttackType.Default))
            Assert.AreEqual(1f, with.DamageMultiplier(key), 1e-6f, key.ToString());

        // 판정 스냅샷(Dref) 비교 — 공격력 100, 계수 1: 100 → 120.
        Assert.AreEqual(100, PlayerStatFormula.Snapshot(100, 1f, 0, 0f, without.DamageMultiplier(GunnerTraitRules.BasicAttackKey)));
        Assert.AreEqual(120, PlayerStatFormula.Snapshot(100, 1f, 0, 0f, with.DamageMultiplier(GunnerTraitRules.BasicAttackKey)));
    }

    [Test]
    public void BasicDamage_SameTraitTwice_CountsOnce()
    {
        GunnerTraitDefinition def = Load("GUN_1_BASIC_DAMAGE");
        var set = new TraitModifierSet();
        set.Rebuild(new[] { def.IdHash, def.IdHash }, _ => def);
        Assert.AreEqual(1.2f, set.DamageMultiplier(GunnerTraitRules.BasicAttackKey), 1e-6f);
    }

    [Test]
    public void BasicDamage_ChangesNoCooldownAreaOrSkillValue()
    {
        TraitModifierSet with = With(Load("GUN_1_BASIC_DAMAGE"));
        Assert.AreEqual(1f, with.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal), 1e-6f);
        Assert.AreEqual(1f, with.AreaMultiplier(GunnerTraitRules.QAttackKey, TraitAreaAxis.Width), 1e-6f);
        Assert.AreEqual(1.2f, with.ResolveSkillValue(GunnerTraitRules.QMaxChargeTime, 1.2f), 1e-6f);
    }

    // ── 1차 · 고속 집속 ───────────────────────────────────────────────

    [Test]
    public void Charge_MaxChargeTime_Times075_OnlyWithTrait()
    {
        GunnerChargeLaserData data = QData();
        TraitModifierSet with = With(Load("GUN_1_CHARGE"));

        Assert.AreEqual(data.MaxChargeTime * 0.75f, with.ResolveSkillValue(GunnerTraitRules.QMaxChargeTime, data.MaxChargeTime), 1e-5f);
        Assert.AreEqual(data.MaxChargeTime, None().ResolveSkillValue(GunnerTraitRules.QMaxChargeTime, data.MaxChargeTime), 1e-6f);
        Assert.AreEqual(data.MaxChargeTime, GunnerTraitRules.QMaxChargeTimeFor(null, data.MaxChargeTime), 1e-6f);
    }

    [Test]
    public void Charge_ReachesFullAtShortenedTime_EndpointsAndHoldUnchanged()
    {
        GunnerChargeLaserData data = QData();
        float effective = With(Load("GUN_1_CHARGE")).ResolveSkillValue(GunnerTraitRules.QMaxChargeTime, data.MaxChargeTime);

        // 특성 최대 시간에 계수 1 — 피해·사거리 최대치는 데이터 그대로.
        float full = data.ChargeFactor(GunnerTraitRules.DataClockElapsed(effective, data.MaxChargeTime, effective));
        Assert.AreEqual(1f, full, 1e-5f);
        Assert.AreEqual(data.RangeAt(1f), data.RangeAt(full), 1e-5f);
        Assert.AreEqual(data.DamageMultiplierAt(1f), data.DamageMultiplierAt(full), 1e-5f);

        // 시작 = 최소치 그대로.
        Assert.AreEqual(data.ChargeFactor(0f), data.ChargeFactor(GunnerTraitRules.DataClockElapsed(0f, data.MaxChargeTime, effective)), 1e-6f);

        // 같은 진행률(절반)에서 같은 계수 — 곡선 모양 유지.
        Assert.AreEqual(data.ChargeFactor(data.MaxChargeTime * 0.5f),
            data.ChargeFactor(GunnerTraitRules.DataClockElapsed(effective * 0.5f, data.MaxChargeTime, effective)), 1e-5f);

        // 특성 없으면 이전과 같다.
        Assert.AreEqual(data.ChargeFactor(0.3f), data.ChargeFactor(GunnerTraitRules.DataClockElapsed(0.3f, data.MaxChargeTime, data.MaxChargeTime)), 1e-6f);

        // 자동 발사 = 특성 최대 충전시간 + 데이터 유지시간(유지시간은 줄지 않는다).
        Assert.AreEqual(effective + data.AutoFireHoldTime, GunnerTraitRules.AutoFireDelay(effective, data.AutoFireHoldTime), 1e-5f);
        Assert.AreEqual(data.MaxChargeTime + data.AutoFireHoldTime, GunnerTraitRules.AutoFireDelay(data.MaxChargeTime, data.AutoFireHoldTime), 1e-5f);
    }

    [Test]
    public void Charge_ChangesNoDamageAreaOrCooldown()
    {
        TraitModifierSet with = With(Load("GUN_1_CHARGE"));
        foreach (TraitAttackKey key in AllKeys)
        {
            Assert.AreEqual(1f, with.DamageMultiplier(key), 1e-6f, key.ToString());
            Assert.AreEqual(1f, with.AreaMultiplier(key, TraitAreaAxis.Width), 1e-6f, key.ToString());
        }
        Assert.AreEqual(1f, with.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal), 1e-6f);
    }

    [Test]
    public void Charge_InvalidZeroTime_ClampsToDataMinimum()
    {
        Assert.AreEqual(0.01f, GunnerTraitRules.ClampChargeTime(0f), 1e-6f);
        Assert.AreEqual(0.01f, GunnerTraitRules.ClampChargeTime(float.NaN), 1e-6f);
    }

    // ── 1차 · 광폭 레이저 ─────────────────────────────────────────────

    [Test]
    public void Width_QBeamWidthOnly_Times130()
    {
        TraitModifierSet with = With(Load("GUN_1_WIDTH"));
        Assert.AreEqual(1.3f, with.AreaMultiplier(GunnerTraitRules.QAttackKey, TraitAreaAxis.Width), 1e-6f);
        Assert.AreEqual(1f, None().AreaMultiplier(GunnerTraitRules.QAttackKey, TraitAreaAxis.Width), 1e-6f);

        // 사거리(길이)·반경 축과 다른 공격(기본 공격·R)은 그대로.
        Assert.AreEqual(1f, with.AreaMultiplier(GunnerTraitRules.QAttackKey, TraitAreaAxis.Length), 1e-6f);
        Assert.AreEqual(1f, with.AreaMultiplier(GunnerTraitRules.QAttackKey, TraitAreaAxis.Radius), 1e-6f);
        Assert.AreEqual(1f, with.AreaMultiplier(GunnerTraitRules.BasicAttackKey, TraitAreaAxis.Width), 1e-6f);
        Assert.AreEqual(1f, with.AreaMultiplier(new TraitAttackKey(AttackType.SkillR), TraitAreaAxis.Width), 1e-6f);

        foreach (TraitAttackKey key in AllKeys)
            Assert.AreEqual(1f, with.DamageMultiplier(key), 1e-6f, key.ToString());
        Assert.AreEqual(QData().MaxChargeTime, with.ResolveSkillValue(GunnerTraitRules.QMaxChargeTime, QData().MaxChargeTime), 1e-6f);
    }

    [Test]
    public void Width_HitVolume_WiderOnly_LengthAndHeightUnchanged()
    {
        GunnerChargeLaserData data = QData();
        float width = data.BeamWidth * With(Load("GUN_1_WIDTH")).AreaMultiplier(GunnerTraitRules.QAttackKey, TraitAreaAxis.Width);
        Vector3 origin = new Vector3(0f, data.MuzzleHeight, 0f);
        float length = data.RangeAt(0.5f);

        SkillHitVolume normal = GunnerChargeLaserSkill.FireVolume(origin, Vector3.forward, length, data);
        SkillHitVolume wide = GunnerChargeLaserSkill.FireVolume(origin, Vector3.forward, length, width, data);

        Assert.AreEqual(normal.HalfExtents.x * 1.3f, wide.HalfExtents.x, 1e-4f);
        Assert.AreEqual(normal.HalfExtents.y, wide.HalfExtents.y, 1e-6f);
        Assert.AreEqual(normal.HalfExtents.z, wide.HalfExtents.z, 1e-6f);
        Assert.That(Vector3.Distance(normal.Center, wide.Center), Is.LessThan(1e-5f));
        Assert.AreEqual(data.BeamWidth, GunnerTraitRules.QBeamWidthFor(null, data.BeamWidth), 1e-6f);
    }

    // ── 아직 동작이 없는 특성 — 수치만 있고 자동 배율이 새지 않는다 ──────────────

    [TestCase("GUN_2_OVERDRIVE")]
    [TestCase("GUN_2_HEAT_SHOT")]
    [TestCase("GUN_2_COOL_MODE")]
    [TestCase("GUN_3_Q_PUSH")]
    [TestCase("GUN_3_PIERCE")]
    [TestCase("GUN_3_VOLLEY")]
    [TestCase("GUN_4_R_FOCUS")]
    [TestCase("GUN_4_EXTRA_BASIC")]
    [TestCase("GUN_4_ECHO_Q")]
    public void LaterTraits_LeakNoAutomaticDamageAreaOrCooldown(string id)
    {
        TraitModifierSet with = With(Load(id));
        foreach (TraitAttackKey key in AllKeys)
        {
            Assert.AreEqual(1f, with.DamageMultiplier(key), 1e-6f, key.ToString());
            Assert.AreEqual(1f, with.AreaMultiplier(key, TraitAreaAxis.Width), 1e-6f, key.ToString());
        }

        foreach (PlayerSkillSlot slot in new[] { PlayerSkillSlot.Main, PlayerSkillSlot.Sub, PlayerSkillSlot.Ultimate })
            Assert.AreEqual(1f, with.CooldownMultiplier(slot, TraitAttackForm.Normal), 1e-6f, slot.ToString());

        Assert.AreEqual(1.2f, with.ResolveSkillValue(GunnerTraitRules.QMaxChargeTime, 1.2f), 1e-6f);
    }
}
