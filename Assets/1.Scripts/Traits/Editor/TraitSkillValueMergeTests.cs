using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// 할 일 133 c — 캐릭터 전용 '스킬 수치' 효과 8종(거너 7·돌파의 방패)을 공용 ⑩ <see cref="TraitSkillValueEffect"/> 로 합친 뒤에도
/// 정의 에셋이 합치기 전과 같은 스킬 수치(이름·연산·값)를 내고, 카드 토큰이 같은 값(해석 전)으로 풀리며, 시트 행으로 나가는지.
/// 기대값 = 합치기 전 효과 클래스가 내던 값(f7f84171 기준 에셋 수치).
/// </summary>
public class TraitSkillValueMergeTests
{
    private const string Gunner = "Assets/9.ScriptableObject/Traits/Gunner/";
    private const string Paladin = "Assets/9.ScriptableObject/Traits/Paladin/";

    // 정의 → 합치기 전 기여(이름:연산:값, 이름 순) · 카드 토큰(이름=해석 전 값)
    private static readonly object[] Cases =
    {
        new object[] { Gunner + "GUN_1_CHARGE.asset", "GUN_Q_MAX_CHARGE_TIME:Multiply:0.75", "chargeTimeReduction=0.25" },
        new object[] { Gunner + "GUN_2_OVERDRIVE.asset",
            "GUN_BASIC_HEAT_PER_SHOT:Multiply:1.35;GUN_BASIC_STAGE_DAMAGE_0:Set:1;GUN_BASIC_STAGE_DAMAGE_1:Set:1.2;GUN_BASIC_STAGE_DAMAGE_2:Set:1.4;" +
            "GUN_BASIC_STAGE_DAMAGE_3:Set:1.6;GUN_SKILL_STAGE_DAMAGE_0:Set:1;GUN_SKILL_STAGE_DAMAGE_1:Set:1.25;GUN_SKILL_STAGE_DAMAGE_2:Set:1.5;" +
            "GUN_SKILL_STAGE_DAMAGE_3:Set:1.75",
            "heatGainIncrease=0.35;basicStageScale3=1.6;skillStageScale3=1.75" },
        new object[] { Gunner + "GUN_2_HEAT_SHOT.asset", "GUN_Q_HEAT_SPENT_DAMAGE_BONUS:Set:0.6", "maxSpentDamageBonus=0.6" },
        new object[] { Gunner + "GUN_2_COOL_MODE.asset", "GUN_BASIC_COOL_DAMAGE_PENALTY:Set:0.15", "basicDamagePenalty=0.15" },
        new object[] { Gunner + "GUN_3_PIERCE.asset", "GUN_BASIC_PIERCE_SECONDARY_RATIO:Set:0.65", "secondaryDamageRatio=0.65" },
        new object[] { Gunner + "GUN_3_VOLLEY.asset",
            "GUN_Q_VOLLEY_PER_SHOT_RATIO:Set:0.28;GUN_Q_VOLLEY_SHOT_COUNT:Set:5;GUN_Q_VOLLEY_SHOT_INTERVAL:Set:0.15",
            "shotCount=5;shotInterval=0.15;perShotDamageRatio=0.28" },
        new object[] { Gunner + "GUN_4_R_FOCUS.asset", "GUN_R_FOCUS_DAMAGE_BONUS:Set:0.2", "damageIncrease=0.2" },
        new object[] { Paladin + "PAL_2_DASH.asset",
            "PAL_Q_DASH_DAMAGE_RATIO:Set:1.25;PAL_Q_DASH_DISTANCE:Set:3;PAL_Q_DASH_PREPARE:Set:0.1;PAL_Q_DASH_PUSH_DISTANCE:Set:3;" +
            "PAL_Q_DASH_PUSH_DURATION:Set:0.3;PAL_Q_DASH_RECOVERY:Set:0.12;PAL_Q_DASH_SPEED:Set:18",
            "dashDistance=3;dashSpeed=18;prepareTime=0.1;recoveryTime=0.12;fullCastDamageRatio=1.25;pushDistance=3;pushDuration=0.3" },
    };

    [TestCaseSource(nameof(Cases))]
    public void MergedDefinition_SameSkillValues_SameCardTokens_ExportsRows(string path, string expectedModifiers, string expectedTokens)
    {
        var def = AssetDatabase.LoadAssetAtPath<TraitDefinition>(path);
        Assert.IsNotNull(def, path);
        Assert.AreEqual(1, def.Effects.Count);
        var effect = def.Effects[0] as TraitSkillValueEffect;
        Assert.IsNotNull(effect, "공용 ⑩ 스킬 수치 효과로 합쳤다");

        var errors = new List<string>();
        def.Validate(errors);
        Assert.IsEmpty(errors, string.Join("\n", errors));

        var modifiers = new List<TraitModifier>();
        def.GetModifiers(modifiers);
        string actual = string.Join(";", modifiers
            .Select(m => $"{m.Key}:{m.Op}:{m.Value.ToString("R", CultureInfo.InvariantCulture)}")
            .OrderBy(s => s, StringComparer.Ordinal));
        Assert.AreEqual(expectedModifiers, actual, "합치기 전과 같은 스킬 수치(이름·연산·값)");

        var source = new TraitTooltipValueSource(def, TraitContent.Build());
        foreach (string token in expectedTokens.Split(';'))
        {
            string[] pair = token.Split('=');
            Assert.IsTrue(source.TryGetTooltipValue(pair[0], out object value, out string error), error);
            Assert.AreEqual(float.Parse(pair[1], CultureInfo.InvariantCulture), Convert.ToSingle(value), 1e-5f, pair[0]);
        }

        List<string> rows = DataTableTemplate.CollectFields(def).Select(f => f.Path).ToList();
        CollectionAssert.AreEqual(Enumerable.Range(0, effect.Entries.Count).Select(j => $"effects[0].entries[{j}].value").ToList(), rows,
            "시트 Traits_<캐릭터> 행 = 칸마다 value");
    }

    [Test]
    public void Scale_AppliedOnlyInModifier_Validation()
    {
        var effect = new TraitSkillValueEffect(
            new TraitSkillValueEffect.Entry("A", TraitValueOp.Multiply, 0.25f, "reduction", TraitSkillValueScale.OneMinus),
            new TraitSkillValueEffect.Entry("B", TraitValueOp.Multiply, 0.35f, "increase", TraitSkillValueScale.OnePlus),
            new TraitSkillValueEffect.Entry("C", TraitValueOp.Add, -2f));
        var modifiers = new List<TraitModifier>();
        effect.GetModifiers(modifiers);
        Assert.AreEqual(0.75f, modifiers[0].Value, 1e-6f);
        Assert.AreEqual(1.35f, modifiers[1].Value, 1e-6f);
        Assert.AreEqual(-2f, modifiers[2].Value, 1e-6f, "Add 는 음수 허용");
        Assert.IsTrue(effect.TryGetNamedValue("reduction", out object raw));
        Assert.AreEqual(0.25f, (float)raw, 1e-6f, "토큰 = 해석 전 값");
        Assert.IsFalse(effect.TryGetNamedValue("C", out _), "name 없는 칸은 토큰이 아니다(key 로 풀지 않는다)");

        var ok = new List<string>();
        effect.Validate(ok);
        Assert.IsEmpty(ok, string.Join("\n", ok));

        var bad = new List<string>();
        new TraitSkillValueEffect(
            new TraitSkillValueEffect.Entry("A", TraitValueOp.Multiply, 1f, "x", TraitSkillValueScale.OneMinus),  // 감소율 ≥ 1
            new TraitSkillValueEffect.Entry("B", TraitValueOp.Set, -1f, "x"),                                   // Set 음수 + name 겹침
            new TraitSkillValueEffect.Entry("", TraitValueOp.Set, 1f)).Validate(bad);                           // key 비었음
        Assert.AreEqual(4, bad.Count, string.Join("\n", bad));
    }
}
