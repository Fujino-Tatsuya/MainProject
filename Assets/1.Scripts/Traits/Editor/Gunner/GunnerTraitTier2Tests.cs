using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 거너 2차 과열 모드 3개 수용 조건(할 일 127 3/4, 명세 11장 거너 '0열/최대열/충전취소/빗나감' + 한계 돌파·축열 포격·정온 운용 규칙).
/// 모드·수치는 서버·클라·툴팁이 같이 쓰는 순수 질의(<see cref="GunnerHeatTraits"/>)와 시전 스냅샷(<see cref="GunnerQCastSnapshot"/>)·과열 모델로 본다.
/// </summary>
public class GunnerTraitTier2Tests
{
    private const string Folder = "Assets/9.ScriptableObject/Traits/Gunner/";

    private static GunnerTraitDefinition Load(string id)
    {
        var def = AssetDatabase.LoadAssetAtPath<GunnerTraitDefinition>(Folder + id + ".asset");
        Assert.IsNotNull(def, Folder + id + ".asset");
        return def;
    }

    private static TraitModifierSet With(params string[] ids)
    {
        TraitDefinition[] defs = ids.Select(id => (TraitDefinition)Load(id)).ToArray();
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

    private static GunnerHeatTraits Traits(params string[] ids) => GunnerHeatTraits.From(ids.Length == 0 ? None() : With(ids));

    // 기본 단계 배율 표 { 1, 1.1, 1.2, 1.3 }(데이터 기본값), 최대 100, 단계 25/50/75, 대기 1.5초, 냉각 20/초·과열 40/초.
    private static GunnerHeatData HeatData() =>
        GunnerHeatData.CreateForTest(100f, new[] { 25f, 50f, 75f }, 1.5f, 20f, 40f);

    private static GunnerChargeLaserData QData()
    {
        string guid = AssetDatabase.FindAssets("t:" + nameof(GunnerChargeLaserData), new[] { "Assets" }).FirstOrDefault();
        Assert.IsNotNull(guid, "GunnerChargeLaserData 에셋이 없다");
        return AssetDatabase.LoadAssetAtPath<GunnerChargeLaserData>(AssetDatabase.GUIDToAssetPath(guid));
    }

    private static TraitDerivedAttack Derived(string id, string key)
    {
        TraitDerivedAttackEffect effect = GunnerTraitTooltip.FindDerived(Load(id), key);
        Assert.IsNotNull(effect, id + " 파생 " + key);
        return effect.CreateRuntime(Load(id).IdHash);
    }

    // ── 모드 판정 ───────────────────────────────────────────────────────

    [TestCase("GUN_2_OVERDRIVE", GunnerHeatMode.Overdrive)]
    [TestCase("GUN_2_HEAT_SHOT", GunnerHeatMode.HeatShot)]
    [TestCase("GUN_2_COOL_MODE", GunnerHeatMode.Cool)]
    public void Mode_FromTraitSwitch(string id, GunnerHeatMode expected)
    {
        Assert.AreEqual(expected, Traits(id).Mode);
        Assert.AreEqual(GunnerHeatMode.Normal, Traits("GUN_1_BASIC_DAMAGE").Mode, "2차 모드가 아닌 특성은 원래 동작");
    }

    // ── 특성 없음 = 기존 값 회귀 ───────────────────────────────────────

    [Test]
    public void NoTrait_KeepsOriginalTables_HeatGain_AndHeatLogic()
    {
        GunnerHeatTraits none = Traits();
        GunnerHeatData heat = HeatData();
        GunnerChargeLaserData q = QData();

        Assert.AreEqual(GunnerHeatMode.Normal, none.Mode);
        Assert.IsTrue(none.HeatEnabled);
        Assert.IsFalse(none.SpendsHeatOnQ);
        Assert.AreEqual(3, none.MaxStage);
        for (int s = 0; s <= 3; s++)
        {
            Assert.AreEqual(heat.StageDamageMultiplier(s), none.BasicShotScale(s, heat), 1e-6f, "기본 공격 " + s);
            Assert.AreEqual(q.StageDamageMultiplier(s), none.SkillStageScale(s, q.StageDamageMultiplier(s)), 1e-6f, "Q " + s);
        }
        Assert.AreEqual(4f, none.HeatPerShot(4f), 1e-6f);
        Assert.AreEqual(1f, none.HeatShotScale(1f), 1e-6f);
        Assert.AreEqual(1f, GunnerHeatTraits.None.BasicModeScale, 1e-6f);

        // 툴팁 범위도 원래 표 그대로.
        Assert.AreEqual(q.DamageMultiplierAt(0f) * q.StageDamageMultiplier(0), q.MinShotScale(none), 1e-6f);
        Assert.AreEqual(q.DamageMultiplierAt(1f) * q.StageDamageMultiplier(3), q.MaxShotScale(none), 1e-6f);
    }

    // ── 한계 돌파(GUN_2_OVERDRIVE) ─────────────────────────────────────

    [Test]
    public void Overdrive_BasicTableReplaced_NotMultipliedOnOriginal()
    {
        GunnerHeatTraits od = Traits("GUN_2_OVERDRIVE");
        GunnerHeatData heat = HeatData();
        float[] expected = { 1f, 1.2f, 1.4f, 1.6f };
        for (int s = 0; s <= 3; s++)
            Assert.AreEqual(expected[s], od.BasicShotScale(s, heat), 1e-6f, $"{s}단계 = 교체 표(원래 {heat.StageDamageMultiplier(s)} 에 곱하지 않음)");
    }

    [Test]
    public void Overdrive_SkillTableReplaced_ForQAndR()
    {
        GunnerHeatTraits od = Traits("GUN_2_OVERDRIVE");
        float[] expected = { 1f, 1.25f, 1.5f, 1.75f };
        for (int s = 0; s <= 3; s++)
        {
            // 데이터 표 값이 무엇이든 교체 표 — Q·R 이 같은 질의를 쓴다.
            Assert.AreEqual(expected[s], od.SkillStageScale(s, QData().StageDamageMultiplier(s)), 1e-6f, "Q " + s);
            Assert.AreEqual(expected[s], od.SkillStageScale(s, 9f), 1e-6f, "R " + s);
        }
    }

    [Test]
    public void Overdrive_StageBoundaries_AndShieldTable_Unchanged()
    {
        GunnerHeatTraits od = Traits("GUN_2_OVERDRIVE");
        GunnerHeatData heat = HeatData();
        Assert.AreEqual(0, GunnerHeatModel.StageOf(24.9f, heat));
        Assert.AreEqual(1, GunnerHeatModel.StageOf(25f, heat), "구간 경계는 데이터 그대로");
        Assert.AreEqual(3, GunnerHeatModel.StageOf(75f, heat));
        Assert.AreEqual(3, od.EffectiveStage(3), "한계 돌파는 단계를 바꾸지 않는다");
        Assert.AreEqual(1f, od.HeatShotScale(1f), 1e-6f, "축열 보너스 없음");
        // Q 보호막 단계 배율은 GunnerHeatTraits 를 지나지 않는다 — 스킬 수치 기여에 보호막 이름이 없다.
        TraitModifierSet set = With("GUN_2_OVERDRIVE");
        Assert.IsFalse(set.Modifiers.Any(m => m.Key != null && m.Key.ToUpperInvariant().Contains("SHIELD")));
    }

    [Test]
    public void Overdrive_HeatGainTimes135_ClampedAtMax_StillOverheatLock()
    {
        GunnerHeatTraits od = Traits("GUN_2_OVERDRIVE");
        GunnerHeatData heat = HeatData();
        Assert.AreEqual(13.5f, od.HeatPerShot(10f), 1e-5f, "발사당 실제 증가량 × 1.35");

        var state = new GunnerHeatState { heat = 95f, anchorTime = 0d, overheated = false };
        GunnerHeatState next = GunnerHeatModel.AddShot(state, 0.1d, od.HeatPerShot(10f), heat);
        Assert.AreEqual(100f, next.heat, 1e-5f, "최대치 초과는 최대로 제한");
        Assert.IsTrue(next.overheated, "최대치 = 과열(기본 공격 불가 유지)");
        Assert.IsTrue(od.HeatEnabled, "E·자동 냉각 기존 유지");
    }

    // ── 축열 포격(GUN_2_HEAT_SHOT) ─────────────────────────────────────

    [Test]
    public void HeatShot_ZeroHeat_CanFire_NoBonus()
    {
        GunnerHeatTraits hs = Traits("GUN_2_HEAT_SHOT");
        var cast = new GunnerQCastSnapshot();
        cast.Begin(0);
        Assert.IsTrue(cast.CommitFirstShot(hs, 0f), "열 0 이어도 발사(커밋)");
        Assert.AreEqual(1f, cast.HeatShotScale, 1e-6f);
        Assert.AreEqual(150, cast.ShotDamage(100, 1.5f, 1f));
    }

    [Test]
    public void HeatShot_MaxHeat_Times160_OnTopOfStoredStage()
    {
        GunnerHeatTraits hs = Traits("GUN_2_HEAT_SHOT");
        var cast = new GunnerQCastSnapshot();
        cast.Begin(3);
        cast.CommitFirstShot(hs, 1f);
        Assert.AreEqual(3, cast.StoredStage, "시전 시작 단계 저장 유지");
        Assert.AreEqual(1.6f, cast.HeatShotScale, 1e-6f);
        // Dref 100 × 충전 1.5 × 저장 단계 1.5 × 1.6 = 360 — 단계 배율에 추가 곱.
        Assert.AreEqual(360, cast.ShotDamage(100, 1.5f, 1.5f));
        Assert.AreEqual(1.6f, hs.MaxHeatShotScale, 1e-6f);
    }

    [Test]
    public void HeatShot_ChargeCancelled_NoCommit_NoSpend()
    {
        GunnerHeatTraits hs = Traits("GUN_2_HEAT_SHOT");
        var cast = new GunnerQCastSnapshot();
        cast.Begin(2);
        // 충전 취소·발사 전 종료 = Fire 를 지나지 않음 → 커밋·소모 없음, 열 상태는 그대로.
        var state = new GunnerHeatState { heat = 80f, anchorTime = 0d, overheated = false };
        Assert.IsFalse(cast.FirstShotCommitted);
        Assert.AreEqual(0f, cast.HeatSpentRatio, 1e-6f);
        Assert.AreEqual(80f, GunnerHeatModel.HeatAt(state, 1d, HeatData()), 1e-5f, "소모 없음");
        // 다음 시전은 새 스냅샷.
        cast.Begin(1);
        Assert.IsFalse(cast.FirstShotCommitted);
        Assert.AreEqual(1f, cast.HeatShotScale, 1e-6f);
        Assert.IsTrue(hs.SpendsHeatOnQ);
    }

    [Test]
    public void HeatShot_MissedShot_StillSpends_HeatZero_UnlocksOverheat()
    {
        GunnerHeatData heat = HeatData();
        // 과열(최대치) 상태에서 발사 — 소모는 적중과 무관하다(빗나가도 소모, Fire 가 판정 전에 소모).
        var state = new GunnerHeatState { heat = 100f, anchorTime = 0d, overheated = true };
        GunnerHeatState next = GunnerHeatModel.SpendAll(state, 0.5d, heat, out float ratio);
        Assert.AreEqual(1f, ratio, 1e-6f);
        Assert.AreEqual(0f, GunnerHeatModel.HeatAt(next, 0.5d, heat), 1e-6f, "열 = 0");
        Assert.IsFalse(GunnerHeatModel.OverheatedAt(next, 0.5d, heat), "과열 잠금 해제");
        Assert.AreEqual(0, GunnerHeatModel.StageAt(next, 0.5d, heat));
    }

    [Test]
    public void HeatShot_HeatReducedDuringCharge_SpendsOnlyRemaining()
    {
        GunnerHeatTraits hs = Traits("GUN_2_HEAT_SHOT");
        GunnerHeatData heat = HeatData();
        // 시전 시작 80 → 대기 1.5초 뒤 20/초 냉각 → 3.5초 시점 40 만 남음.
        var state = new GunnerHeatState { heat = 80f, anchorTime = 0d, overheated = false };
        GunnerHeatModel.SpendAll(state, 3.5d, heat, out float ratio);
        Assert.AreEqual(0.4f, ratio, 1e-5f, "실제 발사 순간 남은 열만");

        // E 로 0 이 됐으면 0 만 소모(보너스 없음).
        GunnerHeatState afterE = GunnerHeatModel.Reset(1d);
        GunnerHeatModel.SpendAll(afterE, 2d, heat, out float afterERatio);
        Assert.AreEqual(0f, afterERatio, 1e-6f);

        var cast = new GunnerQCastSnapshot();
        cast.Begin(1);
        cast.CommitFirstShot(hs, ratio);
        Assert.AreEqual(1.24f, cast.HeatShotScale, 1e-5f, "1 + 0.6 × 0.4");
    }

    [Test]
    public void HeatShot_FirstShotOnly_LaterShotsShareSnapshot_EchoIncludesBonus()
    {
        GunnerHeatTraits hs = Traits("GUN_2_HEAT_SHOT");
        var cast = new GunnerQCastSnapshot();
        cast.Begin(0);
        Assert.IsTrue(cast.CommitFirstShot(hs, 1f));
        Assert.IsFalse(cast.CommitFirstShot(hs, 0f), "후속 발은 새로 소모·계산하지 않는다(5연사 4/4)");
        Assert.AreEqual(1.6f, cast.HeatShotScale, 1e-6f, "첫 발 스냅샷 유지");

        // 잔류 포격은 같은 피해 스냅샷(예정 합계에 축열 보너스 포함).
        int shot = cast.ShotDamage(100, 1.5f, 1f); // 240
        var plan = new GunnerEchoPlan();
        plan.AddOriginalShot(shot);
        plan.AddHitTarget(1UL);
        Assert.IsTrue(plan.TryBuildEcho(true, SkillEndReason.Completed, Derived("GUN_4_ECHO_Q", GunnerTraitRules.QEchoDerivedKey), out int echo));
        Assert.AreEqual(240, shot);
        Assert.AreEqual(60, echo, "240 × 0.25");
    }

    [Test]
    public void HeatShot_NotOtherModes_ShieldAndSkillTableUntouched()
    {
        GunnerHeatTraits hs = Traits("GUN_2_HEAT_SHOT");
        GunnerChargeLaserData q = QData();
        for (int s = 0; s <= 3; s++)
            Assert.AreEqual(q.StageDamageMultiplier(s), hs.SkillStageScale(s, q.StageDamageMultiplier(s)), 1e-6f, "단계 표는 원래");
        Assert.IsTrue(hs.HeatEnabled);
        // 다른 모드는 소모 비율을 받아도 보너스 없음.
        var cast = new GunnerQCastSnapshot();
        cast.Begin(3);
        cast.CommitFirstShot(Traits("GUN_2_OVERDRIVE"), 1f);
        Assert.AreEqual(1f, cast.HeatShotScale, 1e-6f);
        Assert.AreEqual(0f, cast.HeatSpentRatio, 1e-6f);
    }

    // ── 정온 운용(GUN_2_COOL_MODE) ─────────────────────────────────────

    [Test]
    public void CoolMode_HeatLogicDisabled_StageZero_NoUiCap()
    {
        GunnerHeatTraits cool = Traits("GUN_2_COOL_MODE");
        Assert.IsFalse(cool.HeatEnabled, "과열 증가·단계·과열 상태·냉각 비활성(GunnerHeat 가 이 값으로 끈다)");
        Assert.AreEqual(0, cool.MaxStage);
        Assert.AreEqual(0, cool.EffectiveStage(3), "저장 단계도 0");
        Assert.IsFalse(cool.SpendsHeatOnQ);
    }

    [Test]
    public void CoolMode_BasicDamage085_StageZeroOnly()
    {
        GunnerHeatTraits cool = Traits("GUN_2_COOL_MODE");
        GunnerHeatData heat = HeatData();
        for (int s = 0; s <= 3; s++)
            Assert.AreEqual(heat.StageDamageMultiplier(0) * 0.85f, cool.BasicShotScale(s, heat), 1e-6f, "과열 피해표 0단계 × 0.85");
    }

    [Test]
    public void CoolMode_WithOutputBoost_102Percent()
    {
        TraitModifierSet set = With("GUN_1_BASIC_DAMAGE", "GUN_2_COOL_MODE");
        GunnerHeatTraits traits = GunnerHeatTraits.From(set);
        float total = set.DamageMultiplier(GunnerTraitRules.BasicAttackKey) * traits.BasicShotScale(0, HeatData());
        Assert.AreEqual(1.02f, total, 1e-5f, "출력 강화 1.2(Dref) × 정온 0.85");
        Assert.AreEqual(1f, set.DamageMultiplier(GunnerTraitRules.QAttackKey), 1e-6f, "다른 상시 피해 증가 없음");
    }

    [Test]
    public void CoolMode_QRStageBonusZero_ChargeKept_ECooldownNormal()
    {
        GunnerHeatTraits cool = Traits("GUN_2_COOL_MODE");
        GunnerChargeLaserData q = QData();
        int s = cool.EffectiveStage(3);
        Assert.AreEqual(q.StageDamageMultiplier(0), cool.SkillStageScale(3, q.StageDamageMultiplier(s)), 1e-6f, "Q·R 과열 보너스 없음");
        Assert.AreEqual(q.DamageMultiplierAt(1f) * q.StageDamageMultiplier(0), q.MaxShotScale(cool), 1e-6f, "Q 충전 피해 유지·단계 0");
        Assert.AreEqual(q.DamageMultiplierAt(0f) * q.StageDamageMultiplier(0), q.MinShotScale(cool), 1e-6f);

        TraitModifierSet set = With("GUN_2_COOL_MODE");
        Assert.AreEqual(1f, set.CooldownMultiplier(PlayerSkillSlot.Sub, TraitAttackForm.Normal), 1e-6f, "E 정상 쿨");
        Assert.AreEqual(1f, set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal), 1e-6f);
    }

    // ── 카드 {dmg} — 이중 출력·잔류 포격 범위 ───────────────────────────

    [Test]
    public void Tooltip_BasicRange_FollowsHeatMode()
    {
        GunnerHeatData heat = HeatData();

        GunnerTraitTooltip.BasicScaleRange(Traits(), heat, out float min, out float max);
        Assert.AreEqual(1f, min, 1e-6f);
        Assert.AreEqual(1.3f, max, 1e-6f, "원래 표 최고 단계");

        GunnerTraitTooltip.BasicScaleRange(Traits("GUN_2_OVERDRIVE"), heat, out min, out max);
        Assert.AreEqual(1f, min, 1e-6f);
        Assert.AreEqual(1.6f, max, 1e-6f, "한계 돌파 표");

        GunnerTraitTooltip.BasicScaleRange(Traits("GUN_2_COOL_MODE"), heat, out min, out max);
        Assert.AreEqual(0.85f, min, 1e-6f);
        Assert.AreEqual(0.85f, max, 1e-6f, "정온 = 0단계 한 값");

        // 이중 출력 = 원래 발 × 0.40 — Dref 100, 한계 돌파 → 40~64.
        SkillTooltipDamage dmg = GunnerTraitTooltip.DerivedDamage(100, 1f, 0, 1f, 1.6f, 0.4f);
        Assert.AreEqual(40, dmg.Minimum);
        Assert.AreEqual(64, dmg.Maximum);
    }

    [Test]
    public void Tooltip_EchoRange_IncludesOverdriveTable_AndHeatShotMax()
    {
        GunnerChargeLaserData q = QData();
        Assert.AreEqual(q.DamageMultiplierAt(1f) * 1.75f, q.MaxShotScale(Traits("GUN_2_OVERDRIVE")), 1e-5f);
        Assert.AreEqual(q.DamageMultiplierAt(1f) * q.StageDamageMultiplier(3) * 1.6f, q.MaxShotScale(Traits("GUN_2_HEAT_SHOT")), 1e-5f, "축열 최대 소모");
        Assert.AreEqual(q.DamageMultiplierAt(0f) * q.StageDamageMultiplier(0), q.MinShotScale(Traits("GUN_2_HEAT_SHOT")), 1e-5f, "최소 = 소모 0");
    }
}
