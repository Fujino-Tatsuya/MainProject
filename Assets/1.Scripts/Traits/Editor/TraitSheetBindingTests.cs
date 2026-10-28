using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using Object = UnityEngine.Object;

/// <summary>
/// 할 일 125 5/5 — 특성 시트 3장(<see cref="TraitSheets"/>) 바인딩. 특성 = 열(정의 에셋 이름), 필드 = 행(<c>effects[i].필드</c>).
/// 핵심: xlsx 에 시트가 없거나 빈 표여도 오류 0(테이블 Play·빌드가 멈추지 않는다) · <c>[SerializeReference]</c> 효과 칸까지 쓰고 되돌린다 · 범위 검사.
/// </summary>
public class TraitSheetBindingTests
{
    private const string File = "GameData.xlsx";
    private readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in created)
            Object.DestroyImmediate(o);
        created.Clear();
    }

    private sealed class MemoryLookup : IDataTableAssetLookup
    {
        public readonly Dictionary<string, Object> Targets = new Dictionary<string, Object>(StringComparer.Ordinal);

        public IReadOnlyList<Type> FindTypes(string sheetName, out string error)
        {
            error = null;
            switch (sheetName)
            {
                case TraitSheets.Paladin: return new[] { typeof(PaladinTraitDefinition) };
                case TraitSheets.Gunner: return new[] { typeof(GunnerTraitDefinition) };
                case TraitSheets.Assassin: return new[] { typeof(AssassinTraitDefinition) };
                default:
                    error = $"시트 '{sheetName}' 없음";
                    return null;
            }
        }

        public Object FindTarget(Type type, string id, out string error)
        {
            error = null;
            if (Targets.TryGetValue(id, out Object target) && target.GetType() == type)
                return target;
            error = $"'{id}' 없음";
            return null;
        }

        public IReadOnlyCollection<string> AllIds(Type type) => null;
    }

    private static XlsxSheet Sheet(string name, params string[][] rows) => new XlsxSheet(name, rows);

    private static List<DataTableWrite> Bind(IEnumerable<XlsxSheet> sheets, IDataTableAssetLookup lookup, DataTableIssues issues)
    {
        List<DataTableEntry> entries = DataTableSchema.Parse(File, sheets, issues);
        return DataTableApplier.Bind(entries, lookup, issues);
    }

    private static string Describe(DataTableIssues issues) => string.Join("\n", issues.Items.Select(i => i.ToString()));

    [Test]
    public void SheetNames_MapToCharacterTypes()
    {
        Assert.AreEqual(new[] { "Traits_Paladin", "Traits_Gunner", "Traits_Assassin" }, TraitSheets.All);
        Assert.AreEqual(TraitSheets.Paladin, TraitSheets.For(TutorialCharacter.Paladin));
        Assert.AreEqual(TraitSheets.Gunner, TraitSheets.For(TutorialCharacter.Gunner));
        Assert.AreEqual(TraitSheets.Assassin, TraitSheets.For(TutorialCharacter.Assassin));
        Assert.IsNull(TraitSheets.For(TutorialCharacter.None));

        var types = new[] { typeof(PaladinTraitDefinition), typeof(GunnerTraitDefinition), typeof(AssassinTraitDefinition) };
        for (int i = 0; i < types.Length; i++)
        {
            Assert.AreEqual(TraitSheets.All[i], AssetDatabaseLookup.SheetNameOf(types[i]));
            IReadOnlyList<Type> found = new AssetDatabaseLookup().FindTypes(TraitSheets.All[i], out string error);
            Assert.IsNull(error);
            CollectionAssert.AreEqual(new[] { types[i] }, found, "시트 하나 = 캐릭터 정의 타입 하나");
        }

        Assert.IsNull(typeof(TraitDefinition).GetCustomAttribute<DataTableSheetAttribute>(false), "기반 타입(옛 에셋)은 테이블 대상이 아니다");
    }

    [Test]
    public void MissingSheets_NoEntries_NoIssues()
    {
        var issues = new DataTableIssues();
        List<DataTableWrite> writes = Bind(Array.Empty<XlsxSheet>(), new MemoryLookup(), issues);
        Assert.IsEmpty(writes);
        Assert.IsEmpty(issues.Items, Describe(issues));
    }

    [Test]
    public void EmptyAndHeaderOnlySheets_PassWithoutIssues()
    {
        var issues = new DataTableIssues();
        List<DataTableWrite> writes = Bind(new[]
        {
            Sheet(TraitSheets.Paladin),                                                // 완전히 빈 시트
            Sheet(TraitSheets.Gunner, new[] { "필드", "#설명" }),                      // 머리글만(대상 열 없음)
            Sheet(TraitSheets.Assassin, new[] { "#■ AssassinTraitDefinition" }, new[] { "필드", "#설명", "ASN_X" }), // 대상 열만, 값 행 없음
        }, new MemoryLookup(), issues);

        Assert.IsEmpty(writes);
        Assert.IsFalse(issues.HasErrors, Describe(issues));
    }

    [Test]
    public void EffectFields_BindApplyRestore()
    {
        PaladinTraitDefinition def = TraitContentTestKit.Make<PaladinTraitDefinition>("PAL_1_Q_DAMAGE", "진격 강화", "Q 강화", "",
            new TraitDamageEffect(0.2f, new[] { AttackType.SkillQ }),
            new TraitKnockbackEffect());
        created.Add(def);
        var lookup = new MemoryLookup();
        lookup.Targets["PAL_1_Q_DAMAGE"] = def;

        var issues = new DataTableIssues();
        List<DataTableWrite> writes = Bind(new[]
        {
            Sheet(TraitSheets.Paladin,
                new[] { "#■ PaladinTraitDefinition" },
                new[] { "필드", "#설명", "PAL_1_Q_DAMAGE" },
                new[] { "#  ▸ effects[0] · TraitDamageEffect" },
                new[] { "effects[0].damageIncrease", "", "0.35" },
                new[] { "effects[1].pushDistance", "", "3" }),
        }, lookup, issues);

        Assert.IsFalse(issues.HasErrors, Describe(issues));
        Assert.AreEqual(2, writes.Count);

        DataTableSnapshot snapshot = DataTableApplier.ApplyInMemory(writes);
        Assert.IsTrue(new TraitTooltipValueSource(def, null).TryGetTooltipValue("damageIncrease", out object applied, out _));
        Assert.AreEqual(0.35f, (float)applied, 1e-5f);
        Assert.IsTrue(new TraitTooltipValueSource(def, null).TryGetTooltipValue("pushDistance", out object push, out _));
        Assert.AreEqual(3f, (float)push, 1e-5f);

        DataTableApplier.Restore(snapshot);
        new TraitTooltipValueSource(def, null).TryGetTooltipValue("damageIncrease", out object restored, out _);
        Assert.AreEqual(0.2f, (float)restored, 1e-5f, "테이블 Play 가 끝나면 인스펙터 값으로");
    }

    [Test]
    public void EffectField_RangeCheckedOnRuntimeType()
    {
        // TraitTriggerFilter.minInterval [Min(0)] — [SerializeReference] 원소 안쪽 필드도 범위 검사가 닿는다
        GunnerTraitDefinition def = TraitContentTestKit.Make<GunnerTraitDefinition>("GUN_3_PIERCE", "관통", "", "",
            new TraitCooldownRefundEffect(new TraitTriggerFilter(TraitTrigger.AttackLanded), 1f, new[] { PlayerSkillSlot.Main }));
        created.Add(def);
        var lookup = new MemoryLookup();
        lookup.Targets["GUN_3_PIERCE"] = def;

        var issues = new DataTableIssues();
        Bind(new[]
        {
            Sheet(TraitSheets.Gunner,
                new[] { "필드", "#설명", "GUN_3_PIERCE" },
                new[] { "effects[0].filter.minInterval", "", "-1" }),
        }, lookup, issues);

        Assert.IsTrue(issues.HasErrors);
        StringAssert.Contains("이상", Describe(issues));
    }

    [Test]
    public void MissingEffectSlot_IsError_NotSilent()
    {
        AssassinTraitDefinition def = TraitContentTestKit.Make<AssassinTraitDefinition>("ASN_1_Q_COOLDOWN", "돌파 가속", "", "",
            new TraitCooldownEffect(-0.15f));
        created.Add(def);
        var lookup = new MemoryLookup();
        lookup.Targets["ASN_1_Q_COOLDOWN"] = def;

        var issues = new DataTableIssues();
        Bind(new[]
        {
            Sheet(TraitSheets.Assassin,
                new[] { "필드", "#설명", "ASN_1_Q_COOLDOWN" },
                new[] { "effects[3].cooldownChange", "", "-0.2" }),
        }, lookup, issues);

        Assert.IsTrue(issues.HasErrors, "효과 칸이 없으면(배열 범위 밖) 오류 — 테이블은 효과를 늘리지 않는다");
    }

    [Test]
    public void Template_CollectsEffectFields_WithClassLabel()
    {
        PaladinTraitDefinition def = TraitContentTestKit.Make<PaladinTraitDefinition>("PAL_1_Q_DAMAGE", "진격 강화", "", "",
            new TraitDamageEffect(0.2f),
            new TraitCooldownRefundEffect(new TraitTriggerFilter(TraitTrigger.GuardSucceeded), 2f, new[] { PlayerSkillSlot.Sub }));
        created.Add(def);

        List<DataTableTemplate.Field> fields = DataTableTemplate.CollectFields(def);
        List<string> paths = fields.Select(f => f.Path).ToList();
        CollectionAssert.Contains(paths, "effects[0].damageIncrease");
        CollectionAssert.Contains(paths, "effects[1].refundSeconds");
        CollectionAssert.Contains(paths, "effects[1].filter.minInterval");
        CollectionAssert.DoesNotContain(paths, "id", "ID·이름·설명 문자열은 수치 시트에 나가지 않는다(Strings 키)");
        CollectionAssert.DoesNotContain(paths, "displayName");
        Assert.AreEqual("effects[0] · TraitDamageEffect", fields.First(f => f.Path == "effects[0].damageIncrease").Element);
    }

    [Test]
    public void SkillValueEntries_ExportAsRows_BindApplyRestore()
    {
        // 할 일 133 c — [SerializeReference] 원소 안쪽 배열(⑩ entries)도 숫자 칸(value)만 시트 행으로 나간다. key·name(문자열)·op·scale(enum)은 아니다.
        GunnerTraitDefinition def = TraitContentTestKit.Make<GunnerTraitDefinition>("GUN_1_CHARGE", "고속 집속", "", "{chargeTimeReduction:%}",
            new TraitSkillValueEffect(
                new TraitSkillValueEffect.Entry(GunnerTraitRules.QMaxChargeTime, TraitValueOp.Multiply, 0.25f, "chargeTimeReduction", TraitSkillValueScale.OneMinus),
                new TraitSkillValueEffect.Entry(GunnerTraitRules.QVolleyShotCount, TraitValueOp.Set, 5f, "shotCount")));
        created.Add(def);

        List<string> paths = DataTableTemplate.CollectFields(def).Select(f => f.Path).ToList();
        CollectionAssert.AreEqual(new[] { "effects[0].entries[0].value", "effects[0].entries[1].value" }, paths);

        var lookup = new MemoryLookup();
        lookup.Targets["GUN_1_CHARGE"] = def;
        var issues = new DataTableIssues();
        List<DataTableWrite> writes = Bind(new[]
        {
            Sheet(TraitSheets.Gunner,
                new[] { "필드", "#설명", "GUN_1_CHARGE" },
                new[] { "effects[0].entries[0].value", "", "0.4" }),
        }, lookup, issues);
        Assert.IsFalse(issues.HasErrors, Describe(issues));
        Assert.AreEqual(1, writes.Count);

        DataTableSnapshot snapshot = DataTableApplier.ApplyInMemory(writes);
        Assert.IsTrue(new TraitTooltipValueSource(def, null).TryGetTooltipValue("chargeTimeReduction", out object applied, out string error), error);
        Assert.AreEqual(0.4f, (float)applied, 1e-5f, "카드 토큰 = 칸 name, 해석 전 값");
        Assert.AreEqual(0.6f, GunnerTraitTooltip.SkillValue(def, GunnerTraitRules.QMaxChargeTime, 1f), 1e-5f, "연산 = × (1 − 0.4)");

        DataTableApplier.Restore(snapshot);
        new TraitTooltipValueSource(def, null).TryGetTooltipValue("chargeTimeReduction", out object restored, out _);
        Assert.AreEqual(0.25f, (float)restored, 1e-5f);
    }
}
