using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

/// <summary>
/// 할 일 128 (1/5) — 어쌔신 수치 특성 4개(광란 강화·강타 강화·돌파 가속·완전 해방)의 명세 §11 수용 조건을 프로젝트 정의 에셋으로 본다.
/// 피해는 공격 키(종류·형태)로 Dref 에 한 번 곱해지고(<see cref="Player.ResolveDamage(float,int,TraitAttackKey)"/>), 쿨은 슬롯·형태로 거른다
/// (<see cref="PlayerSkillController.GetEffectiveCooldownTime(PlayerSkillBase)"/>). 어쌔신 스킬이 넘기는 키:
/// 일반 기본 = Default/Normal · 강타 = Default/Enhanced · 변신 묶음 = Default/Alternate · 일반 Q/E = SkillQ·SkillE/Normal ·
/// 변신 Q/E = SkillQ·SkillE/Alternate(대체 세트) · 우클릭 = Skill.
/// </summary>
public sealed class AssassinTraitNumericTests
{
    const float Eps = 1e-5f;

    const string Frenzy = "ASN_1_TRANS_E_DAMAGE";
    const string Smash = "ASN_1_NORMAL_E_DAMAGE";
    const string Breakthrough = "ASN_1_Q_COOLDOWN";
    const string Release = "ASN_2_RELEASE";

    static readonly TraitAttackKey NormalBasic = new TraitAttackKey(AttackType.Default, TraitAttackForm.Normal);
    static readonly TraitAttackKey EnhancedBasic = new TraitAttackKey(AttackType.Default, TraitAttackForm.Enhanced);
    static readonly TraitAttackKey TransformedBasic = new TraitAttackKey(AttackType.Default, TraitAttackForm.Alternate);
    static readonly TraitAttackKey NormalQ = new TraitAttackKey(AttackType.SkillQ, TraitAttackForm.Normal);
    static readonly TraitAttackKey TransformedQ = new TraitAttackKey(AttackType.SkillQ, TraitAttackForm.Alternate);
    static readonly TraitAttackKey NormalE = new TraitAttackKey(AttackType.SkillE, TraitAttackForm.Normal);
    static readonly TraitAttackKey TransformedE = new TraitAttackKey(AttackType.SkillE, TraitAttackForm.Alternate);

    // ── 도우미 ──────────────────────────────────────────────────────────

    static TraitDefinition Load(string id)
    {
        TraitCharacterPool pool = TraitContentTestKit.CharacterPool(TutorialCharacter.Assassin);
        Assert.IsNotNull(pool, "어쌔신 캐릭터 풀");
        TraitDefinition def = pool.Traits.FirstOrDefault(t => t != null && t.Id == id);
        Assert.IsNotNull(def, $"TraitPool_Assassin 에 {id} 정의가 있어야 한다");
        Assert.IsInstanceOf<AssassinTraitDefinition>(def);
        return def;
    }

    static TraitModifierSet SetOf(params string[] ids)
    {
        var table = new Dictionary<int, ITraitModifierContributor>();
        foreach (string id in ids.Distinct())
            table[TraitIdHash.Of(id)] = Load(id);

        var set = new TraitModifierSet();
        set.Rebuild(ids.Select(TraitIdHash.Of).ToArray(), hash => table.TryGetValue(hash, out ITraitModifierContributor c) ? c : null);
        Assert.IsEmpty(set.Errors, string.Join("\n", set.Errors));
        return set;
    }

    static IEnumerable<TraitAttackKey> AllKeys() =>
        from AttackType type in Enum.GetValues(typeof(AttackType))
        from TraitAttackForm form in Enum.GetValues(typeof(TraitAttackForm))
        select new TraitAttackKey(type, form);

    /// <summary>지정한 키만 <paramref name="expected"/>, 나머지 키는 전부 1(적용 안 함).</summary>
    static void AssertDamageOnly(TraitModifierSet set, float expected, params TraitAttackKey[] keys)
    {
        foreach (TraitAttackKey key in AllKeys())
            Assert.AreEqual(keys.Contains(key) ? expected : 1f, set.DamageMultiplier(key), Eps, key.ToString());
    }

    static void AssertNoCooldownChange(TraitModifierSet set)
    {
        foreach (PlayerSkillSlot slot in Enum.GetValues(typeof(PlayerSkillSlot)))
        foreach (TraitAttackForm form in Enum.GetValues(typeof(TraitAttackForm)))
            Assert.AreEqual(1f, set.CooldownMultiplier(slot, form), Eps, $"{slot}/{form}");
    }

    static string Card(string id)
    {
        TraitDefinition def = Load(id);
        Assert.IsTrue(TraitTooltip.Format(def, def.InlineDescription, null, false, TraitContent.Build(), out string text, out string error), error);
        return text;
    }

    sealed class SetSource : IPlayerStatSource
    {
        public readonly TraitModifierSet Set;
        public SetSource(TraitModifierSet set) => Set = set;
        public float GetBasePercent(PlayerStatKind kind) => Set.BasePercent(kind);
        public float GetTotalPercent(PlayerStatKind kind) => Set.TotalPercent(kind);
        public float DealtDamagePercent => Set.DealtDamagePercent;
        public event Action Changed { add { } remove { } }
    }

    /// <summary>공격력 100 플레이어의 Dref(계수 1) — 스킬이 부르는 경로와 같은 식(특성 배율은 Dref 에 한 번).</summary>
    static int Dref(TraitModifierSet set, TraitAttackKey key)
    {
        var stats = new PlayerStats(kind => kind == PlayerStatKind.AttackDamage ? 100f : 5f, null);
        stats.Register(new SetSource(set));
        try
        {
            return stats.ResolveDamage(1f, 0, set.DamageMultiplier(key));
        }
        finally
        {
            stats.Dispose();
        }
    }

    // ── 공통: 정의 데이터·풀 순서 ────────────────────────────────────────

    [Test]
    public void Pool_FollowsSpecOrder()
    {
        string[] poolIds = TraitContentTestKit.CharacterPool(TutorialCharacter.Assassin).Traits.Select(t => t.Id).ToArray();
        // 12개 완성(할 일 128 5b) — 명세 순서 고정. 카탈로그 순서와도 같아야 한다(목록 버전).
        string[] expected =
        {
            Frenzy, Smash, Breakthrough,
            Release, "ASN_2_RESTRAINT", "ASN_2_RETURN",
            "ASN_3_E_AREA", "ASN_3_Q_BASIC", "ASN_3_BASIC_CDR",
            "ASN_4_NORMAL_E_PUSH", "ASN_4_Q_TRAIL", "ASN_4_COMBO_EXTRA",
        };
        CollectionAssert.AreEqual(expected, poolIds, "풀 순서 = 명세 순서(1차 3 → 2차 3 → 3차 3 → 4차 3)");
        string[] specOrder = TraitCatalog.Spec.Where(s => s.Character == TutorialCharacter.Assassin).Select(s => s.Id).ToArray();
        CollectionAssert.AreEqual(specOrder, poolIds, "카탈로그 명세 순서 12개 전부");
    }

    [Test]
    public void Definitions_HaveSpecNamesAndCategories_AndValidData()
    {
        var expected = new (string id, string name, string category)[]
        {
            (Frenzy, "광란 강화", "변신 E 강화"),
            (Smash, "강타 강화", "일반 E 강화"),
            (Breakthrough, "돌파 가속", "일반 Q 강화"),
            (Release, "완전 해방", "변신 강화"),
        };

        foreach (var (id, name, category) in expected)
        {
            TraitDefinition def = Load(id);
            Assert.AreEqual(id, def.name, "파일 이름 = 특성 ID(시트 열 이름)");
            Assert.AreEqual(name, def.InlineDisplayName, id);
            Assert.AreEqual(category, def.InlineCategory, id);
            var errors = new List<string>();
            def.Validate(errors);
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual(1, def.Effects.Count, $"{id} 효과 칸 1개(시트 행 effects[0].*)");
        }
    }

    [Test]
    public void Cards_ResolveToSpecSelectionText()
    {
        Assert.AreEqual("변신 E 피해가 20% 증가합니다.", Card(Frenzy));
        Assert.AreEqual("일반 E로 강화한 기본 공격의 피해가 20% 증가합니다.", Card(Smash));
        Assert.AreEqual("일반 Q 재사용 대기시간이 15% 감소합니다.", Card(Breakthrough));
        Assert.AreEqual("변신 중 기본 공격과 Q, E 피해가 30% 증가합니다.", Card(Release));
    }

    // ── 광란 강화 ───────────────────────────────────────────────────────

    [Test]
    public void Frenzy_TransformedE_Only_Times1_2()
    {
        TraitModifierSet set = SetOf(Frenzy);
        AssertDamageOnly(set, 1.2f, TransformedE);
        AssertNoCooldownChange(set);
        Assert.AreEqual(1f, set.DamageMultiplier(NormalE), "일반 E(강화 준비)에는 0");
        Assert.AreEqual(1f, set.DamageMultiplier(EnhancedBasic), "강타에는 0");
    }

    [Test]
    public void Frenzy_EachOfFiveHits_Plus20Percent()
    {
        // 원형 E 는 시전 때 Dref 하나를 스냅샷해 5회 각각 그 값으로 친다 → 5회 모두 1.20 배(합계도 1.20 배).
        int without = Dref(SetOf(), TransformedE);
        int with = Dref(SetOf(Frenzy), TransformedE);
        Assert.AreEqual(100, without);
        Assert.AreEqual(120, with);
        Assert.AreEqual(5 * 120, Enumerable.Repeat(with, 5).Sum());
    }

    // ── 강타 강화 ───────────────────────────────────────────────────────

    [Test]
    public void Smash_EnhancedBasic_Only_Times1_2()
    {
        TraitModifierSet set = SetOf(Smash);
        AssertDamageOnly(set, 1.2f, EnhancedBasic);
        AssertNoCooldownChange(set);
        Assert.AreEqual(1f, set.DamageMultiplier(NormalBasic), "일반 기본 공격 전체에는 0");
        Assert.AreEqual(1f, set.DamageMultiplier(TransformedBasic), "변신 기본 공격에는 0");
        Assert.AreEqual(1f, set.DamageMultiplier(TransformedE), "변신 E 에는 0");
        Assert.AreEqual(120, Dref(set, EnhancedBasic));
        Assert.AreEqual(100, Dref(set, NormalBasic));
    }

    // ── 돌파 가속 ───────────────────────────────────────────────────────

    [Test]
    public void Breakthrough_NormalQCooldown_Times0_85_TransformedQUnchanged()
    {
        TraitModifierSet set = SetOf(Breakthrough);
        Assert.AreEqual(0.85f, set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal), Eps);
        Assert.AreEqual(1f, set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Alternate), Eps, "변신 Q 제외");
        foreach (PlayerSkillSlot slot in new[] { PlayerSkillSlot.Sub, PlayerSkillSlot.Interrupt, PlayerSkillSlot.Ultimate })
        foreach (TraitAttackForm form in Enum.GetValues(typeof(TraitAttackForm)))
            Assert.AreEqual(1f, set.CooldownMultiplier(slot, form), Eps, $"{slot}/{form}");
        AssertDamageOnly(set, 1f);

        // 명세 §10: 기본 5초 × 0.85 = 4.25초(절제된 분노 ×0.75 는 H4 에서 곱한다).
        Assert.AreEqual(4.25f, 5f * set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal), Eps);
    }

    [Test]
    public void Breakthrough_AffectsOnlyMainSlotCooldown_ForHud()
    {
        TraitDefinition def = Load(Breakthrough);
        Assert.IsTrue(def.AffectsCooldownOf(PlayerSkillSlot.Main));
        Assert.IsFalse(def.AffectsCooldownOf(PlayerSkillSlot.Sub));
        Assert.IsFalse(def.AffectsCooldownOf(PlayerSkillSlot.Interrupt));
        Assert.IsFalse(def.AffectsCooldownOf(PlayerSkillSlot.Ultimate));
    }

    // ── 완전 해방 ───────────────────────────────────────────────────────

    [Test]
    public void Release_TransformedBasicQE_Times1_3_RightClickExcluded()
    {
        TraitModifierSet set = SetOf(Release);
        AssertDamageOnly(set, 1.3f, TransformedBasic, TransformedQ, TransformedE);
        AssertNoCooldownChange(set);
        foreach (TraitAttackForm form in Enum.GetValues(typeof(TraitAttackForm)))
            Assert.AreEqual(1f, set.DamageMultiplier(new TraitAttackKey(AttackType.Skill, form)), $"우클릭 {form} 제외");
        Assert.AreEqual(1f, set.DamageMultiplier(NormalBasic), "일반 상태 피해 그대로(감소 없음)");
        Assert.AreEqual(1f, set.DamageMultiplier(NormalQ));
        Assert.AreEqual(1f, set.DamageMultiplier(EnhancedBasic));
        Assert.AreEqual(130, Dref(set, TransformedQ));
        Assert.AreEqual(100, Dref(set, NormalQ));
    }

    // ── 조합·중복 ───────────────────────────────────────────────────────

    [Test]
    public void FrenzyAndRelease_OnTransformedE_Multiply()
    {
        TraitModifierSet set = SetOf(Frenzy, Release);
        Assert.AreEqual(1.2f * 1.3f, set.DamageMultiplier(TransformedE), Eps, "서로 다른 특성은 곱(명세 §5)");
        Assert.AreEqual(1.3f, set.DamageMultiplier(TransformedQ), Eps);
        Assert.AreEqual(1.3f, set.DamageMultiplier(TransformedBasic), Eps);
        Assert.AreEqual(156, Dref(set, TransformedE));
    }

    [Test]
    public void AllFour_DoNotLeakAcrossForms()
    {
        TraitModifierSet set = SetOf(Frenzy, Smash, Breakthrough, Release);
        Assert.AreEqual(1f, set.DamageMultiplier(NormalBasic), Eps);
        Assert.AreEqual(1.2f, set.DamageMultiplier(EnhancedBasic), Eps);
        Assert.AreEqual(1f, set.DamageMultiplier(NormalQ), Eps);
        Assert.AreEqual(1f, set.DamageMultiplier(NormalE), Eps);
        Assert.AreEqual(1.3f, set.DamageMultiplier(TransformedBasic), Eps);
        Assert.AreEqual(1.3f, set.DamageMultiplier(TransformedQ), Eps);
        Assert.AreEqual(1.56f, set.DamageMultiplier(TransformedE), Eps);
        Assert.AreEqual(0.85f, set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal), Eps);
        Assert.AreEqual(1f, set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Alternate), Eps);
    }

    [TestCase(Frenzy)]
    [TestCase(Smash)]
    [TestCase(Breakthrough)]
    [TestCase(Release)]
    public void SameTraitTwice_AppliesOnce(string id)
    {
        TraitModifierSet once = SetOf(id);
        TraitModifierSet twice = SetOf(id, id);
        foreach (TraitAttackKey key in AllKeys())
            Assert.AreEqual(once.DamageMultiplier(key), twice.DamageMultiplier(key), Eps, key.ToString());
        foreach (PlayerSkillSlot slot in Enum.GetValues(typeof(PlayerSkillSlot)))
        foreach (TraitAttackForm form in Enum.GetValues(typeof(TraitAttackForm)))
            Assert.AreEqual(once.CooldownMultiplier(slot, form), twice.CooldownMultiplier(slot, form), Eps, $"{slot}/{form}");
    }
}
