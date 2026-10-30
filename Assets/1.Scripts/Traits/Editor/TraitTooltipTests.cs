using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 125 5/5 — 특성 카드 설명 토큰 해결(<see cref="TraitTooltip"/>·<see cref="TraitTooltipValueSource"/>) + 프로젝트 정의 전체 검증.
/// 풀리지 않는 토큰·계산식 없는 {dmg} 는 Play 없이 여기서 오류로 잡힌다(명세 §8 — 조용히 0 으로 보이면 안 된다).
/// </summary>
public class TraitTooltipTests
{
    private readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in created)
            Object.DestroyImmediate(o);
        created.Clear();
    }

    private T Make<T>(string id, string description, params TraitEffect[] effects) where T : TraitDefinition
    {
        T def = TraitContentTestKit.Make<T>(id, "이름", "분류", description, effects);
        created.Add(def);
        return def;
    }

    private static string Format(TraitDefinition def, string template, TraitContentRegistry registry)
    {
        Assert.IsTrue(TraitTooltip.Format(def, template, null, false, registry, out string text, out string error), error);
        return text;
    }

    [Test]
    public void EffectFieldName_Percent_Resolves()
    {
        var def = Make<PaladinTraitDefinition>("PAL_1_Q_DAMAGE", "", new TraitDamageEffect(0.2f, new[] { AttackType.SkillQ }));
        Assert.AreEqual("Q 피해가 20% 증가합니다.", Format(def, "Q 피해가 {damageIncrease:%} 증가합니다.", new TraitContentRegistry()));
    }

    [Test]
    public void EffectPath_Resolves()
    {
        var def = Make<PaladinTraitDefinition>("PAL_1_Q_DAMAGE", "", new TraitDamageEffect(0.25f));
        Assert.AreEqual("25%", Format(def, "{effects[0].damageIncrease:%}", new TraitContentRegistry()));
    }

    [Test]
    public void PlainNumber_UsesCompactFormat()
    {
        var def = Make<PaladinTraitDefinition>("PAL_2_E_REFUND", "",
            new TraitCooldownRefundEffect(new TraitTriggerFilter(TraitTrigger.GuardSucceeded), 2f, new[] { PlayerSkillSlot.Sub }));
        Assert.AreEqual("2초", Format(def, "{refundSeconds}초", new TraitContentRegistry()));
    }

    [Test]
    public void SameFieldInTwoEffects_IsError_UntilPathOrAlias()
    {
        var def = Make<AssassinTraitDefinition>("ASN_3_Q_BASIC", "",
            new TraitDamageEffect(0.2f), new TraitNextAttackBuffEffect());
        var registry = new TraitContentRegistry();
        Assert.IsFalse(TraitTooltip.Validate(def, "{damageIncrease:%}", registry, out string error));
        StringAssert.Contains("효과 칸 0·1", error);

        Assert.IsTrue(TraitTooltip.Validate(def, "{effects[1].damageIncrease:%}", registry, out error), error);

        registry.For(TutorialCharacter.Assassin).Value("ASN_3_Q_BASIC", "damageIncrease", d => 0.3f);
        Assert.AreEqual("30%", Format(def, "{damageIncrease:%}", registry), "별칭이 효과 필드보다 먼저");
    }

    [Test]
    public void UnknownToken_IsValidationError()
    {
        var def = Make<GunnerTraitDefinition>("GUN_1_WIDTH", "", new TraitAreaEffect());
        Assert.IsFalse(TraitTooltip.Validate(def, "폭이 {widthIncrease:%} 넓어집니다.", new TraitContentRegistry(), out string error));
        StringAssert.Contains("widthIncrease", error);
    }

    [Test]
    public void UnknownToken_NotResolvedFromSourceWrapper()
    {
        // 값 소스 자신의 private 필드(trait·registry)로 새지 않는다
        var def = Make<GunnerTraitDefinition>("GUN_1_WIDTH", "", new TraitAreaEffect());
        Assert.IsFalse(TraitTooltip.Validate(def, "{registry}", new TraitContentRegistry(), out _));
    }

    [Test]
    public void BrokenSyntax_IsValidationError()
    {
        var def = Make<GunnerTraitDefinition>("GUN_1_WIDTH", "", new TraitAreaEffect());
        Assert.IsFalse(TraitTooltip.Validate(def, "폭이 {areaIncrease:% 넓어집니다.", new TraitContentRegistry(), out _));
        Assert.IsFalse(TraitTooltip.Validate(def, "<style=heal>회복", new TraitContentRegistry(), out _));
    }

    [Test]
    public void Damage_WithoutCalculator_IsValidationError()
    {
        var def = Make<PaladinTraitDefinition>("PAL_2_DASH", "");
        Assert.IsFalse(TraitTooltip.Validate(def, "{dmg} 피해", new TraitContentRegistry(), out string error));
        StringAssert.Contains("{dmg}", error);
        Assert.IsFalse(TraitTooltip.Format(def, "{dmg} 피해", null, false, new TraitContentRegistry(), out _, out _), "표시도 막는다");
    }

    [Test]
    public void Damage_WithCalculator_NoPlayer_ShowsUnknownNotZero()
    {
        var def = Make<PaladinTraitDefinition>("PAL_2_DASH", "");
        var registry = new TraitContentRegistry();
        bool called = false;
        registry.For(TutorialCharacter.Paladin).Damage("PAL_2_DASH", (t, p) =>
        {
            called = true;
            return default;
        });

        Assert.IsTrue(TraitTooltip.Validate(def, "{dmg} 피해", registry, out string error), error);
        string text = Format(def, "{dmg} 피해", registry);
        Assert.IsFalse(called, "플레이어가 없으면 계산식을 부르지 않는다");
        Assert.AreEqual(TraitUiStrings.DamageUnknown.Text + " 피해", text);
        StringAssert.DoesNotContain("{dmg}", text);
    }

    [Test]
    public void DefinitionOwnField_Resolves()
    {
        var def = Make<PaladinTraitDefinition>("PAL_1_Q_DAMAGE", "");
        Assert.AreEqual("PAL_1_Q_DAMAGE", Format(def, "{id}", new TraitContentRegistry()));
    }

    [Test]
    public void ValidateDefinition_ReportsEmptyNameAndBadDescription()
    {
        PaladinTraitDefinition def = TraitContentTestKit.Make<PaladinTraitDefinition>("PAL_1_Q_DAMAGE", "", "", "{nothing}");
        created.Add(def);
        var errors = new List<string>();
        TraitTooltip.ValidateDefinition(def, new TraitContentRegistry(), errors);
        Assert.AreEqual(2, errors.Count, string.Join("\n", errors));
    }

    // ── 프로젝트 데이터 ────────────────────────────────────────────────

    [Test]
    public void ProjectRegistry_HasNoErrors()
    {
        TraitContentRegistry registry = TraitContent.Build();
        Assert.IsEmpty(registry.Errors, string.Join("\n", registry.Errors));
    }

    [Test]
    public void ProjectDefinitions_AllCardTextsResolve()
    {
        TraitContentRegistry registry = TraitContent.Build();
        var errors = new List<string>();
        foreach (TraitDefinition def in TraitContentTestKit.ProjectDefinitions())
            TraitTooltip.ValidateDefinition(def, registry, errors);
        Assert.IsEmpty(errors, string.Join("\n", errors));
    }

    [Test]
    public void ProjectDefinitions_AreCharacterTypes_InTheirPool()
    {
        TraitPool pool = TraitContentTestKit.ProjectPool();
        Assert.IsNotNull(pool);
        var inPool = new HashSet<TraitDefinition>(pool.Traits);
        var errors = new List<string>();
        foreach (TraitDefinition def in TraitContentTestKit.ProjectDefinitions())
        {
            if (def.Character == TutorialCharacter.None)
                errors.Add($"'{def.name}' 는 기반 TraitDefinition — 캐릭터 하위 타입(Traits/<캐릭터> 생성 메뉴)으로 만들 것.");
            else if (!inPool.Contains(def))
                errors.Add($"'{def.name}' 가 TraitPool_{def.Character} 에 없다.");
        }

        Assert.IsEmpty(errors, string.Join("\n", errors));
    }

    [Test]
    public void ProjectPool_LinksThreeCharacterPools_WithoutErrors()
    {
        TraitPool pool = TraitContentTestKit.ProjectPool();
        var errors = new List<string>();
        pool.Validate(errors);
        Assert.IsEmpty(errors, string.Join("\n", errors));
        foreach (TutorialCharacter c in new[] { TutorialCharacter.Paladin, TutorialCharacter.Gunner, TutorialCharacter.Assassin })
            Assert.IsNotNull(TraitContentTestKit.CharacterPool(c), c.ToString());
        Assert.IsEmpty(pool.Catalog.Errors, string.Join("\n", pool.Catalog.Errors));
    }
}
