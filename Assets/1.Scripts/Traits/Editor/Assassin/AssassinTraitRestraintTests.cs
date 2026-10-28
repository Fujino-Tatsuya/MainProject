using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

/// <summary>
/// 할 일 128 (4/5) — 절제된 분노(ASN_2_RESTRAINT)의 명세 §11 수용 조건. 수치는 프로젝트 정의 에셋(피해 ×1.35 두 칸·쿨 ×0.75 한 칸),
/// 모드 변경(R 차단·분노 획득 0·변신 중 선택 시 안전 종료 후 적용)은 순수 규칙 <see cref="AssassinRestraintRules"/> + <see cref="AssassinStateModel"/> 로 본다.
/// 스킬 쪽 분기는 <see cref="AssassinState"/>(<c>IsRestrained</c> = <see cref="PlayerTraitModifiers.HasTrait"/>(<see cref="TraitModeIds.AssassinRestraint"/>)).
/// </summary>
public sealed class AssassinTraitRestraintTests
{
    const float Eps = 1e-4f;

    const string Restraint = "ASN_2_RESTRAINT";
    const string Release = "ASN_2_RELEASE";
    const string Frenzy = "ASN_1_TRANS_E_DAMAGE";
    const string Smash = "ASN_1_NORMAL_E_DAMAGE";
    const string Breakthrough = "ASN_1_Q_COOLDOWN";
    const string Momentum = "ASN_3_Q_BASIC";

    static readonly TraitAttackKey NormalBasic = new TraitAttackKey(AttackType.Default, TraitAttackForm.Normal);
    static readonly TraitAttackKey EnhancedBasic = new TraitAttackKey(AttackType.Default, TraitAttackForm.Enhanced);
    static readonly TraitAttackKey TransformedBasic = new TraitAttackKey(AttackType.Default, TraitAttackForm.Alternate);
    static readonly TraitAttackKey NormalQ = new TraitAttackKey(AttackType.SkillQ, TraitAttackForm.Normal);
    static readonly TraitAttackKey TransformedQ = new TraitAttackKey(AttackType.SkillQ, TraitAttackForm.Alternate);
    static readonly TraitAttackKey NormalE = new TraitAttackKey(AttackType.SkillE, TraitAttackForm.Normal);
    static readonly TraitAttackKey TransformedE = new TraitAttackKey(AttackType.SkillE, TraitAttackForm.Alternate);

    static readonly AssassinStateRules Rules = AssassinStateRules.Default;

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

    static string Card(string id)
    {
        TraitDefinition def = Load(id);
        Assert.IsTrue(TraitTooltip.Format(def, def.InlineDescription, null, false, TraitContent.Build(), out string text, out string error), error);
        return text;
    }

    sealed class SetSource : IPlayerStatSource
    {
        readonly TraitModifierSet set;
        public SetSource(TraitModifierSet set) => this.set = set;
        public float GetBasePercent(PlayerStatKind kind) => set.BasePercent(kind);
        public float GetTotalPercent(PlayerStatKind kind) => set.TotalPercent(kind);
        public float DealtDamagePercent => set.DealtDamagePercent;
        public event Action Changed { add { } remove { } }
    }

    /// <summary>공격력 100 플레이어의 원래 타격(계수 1) = Dref × 다음 공격 강화 — 어쌔신 평타·Q 와 같은 식(마지막에 한 번 정수화).</summary>
    static int Hit(TraitModifierSet set, TraitAttackKey key, float nextAttackMultiplier = 1f)
    {
        var stats = new PlayerStats(kind => kind == PlayerStatKind.AttackDamage ? 100f : 5f, null);
        stats.Register(new SetSource(set));
        try
        {
            return stats.ResolveDamage(1f, 0, set.DamageMultiplier(key) * nextAttackMultiplier);
        }
        finally
        {
            stats.Dispose();
        }
    }

    static AssassinStateSnapshot Transformed(double start, float fuel) => new AssassinStateSnapshot
    {
        rage = fuel,
        transformed = true,
        transformStartTime = start,
        transformEndTime = start + Rules.DurationFor(fuel),
    };

    // ── 정의 데이터·카드 ────────────────────────────────────────────────

    [Test]
    public void Definition_SpecNameCategory_ThreeEffects_Valid_AfterReleaseInPool()
    {
        TraitDefinition def = Load(Restraint);
        Assert.AreEqual(Restraint, def.name, "파일 이름 = 특성 ID(시트 열 이름)");
        Assert.AreEqual("절제된 분노", def.InlineDisplayName);
        Assert.AreEqual("R 제거 · 일반 Q/E 강화", def.InlineCategory);
        Assert.AreEqual(3, def.Effects.Count, "effects[0] 일반 Q 피해 · [1] 강타 피해 · [2] 일반 Q·E 쿨");
        Assert.IsInstanceOf<TraitDamageEffect>(def.Effects[0]);
        Assert.IsInstanceOf<TraitDamageEffect>(def.Effects[1]);
        Assert.IsInstanceOf<TraitCooldownEffect>(def.Effects[2]);
        var errors = new List<string>();
        def.Validate(errors);
        Assert.IsEmpty(errors, string.Join("\n", errors));

        string[] poolIds = TraitContentTestKit.CharacterPool(TutorialCharacter.Assassin).Traits.Select(t => t.Id).ToArray();
        Assert.AreEqual(Array.IndexOf(poolIds, Release) + 1, Array.IndexOf(poolIds, Restraint), "풀에서 완전 해방 바로 뒤");
    }

    [Test]
    public void Card_ResolvesToSpecSelectionText()
    {
        Assert.AreEqual(
            "R과 변신을 사용할 수 없습니다. 대신 일반 Q와 E 피해가 35% 증가하고 재사용 대기시간이 25% 줄어듭니다.",
            Card(Restraint));
    }

    [Test]
    public void Conflict_WithFrenzy_Registered_AndListedWhenOwned()
    {
        TraitContentRegistry registry = TraitContent.Build();
        Assert.IsEmpty(registry.Errors, string.Join("\n", registry.Errors));
        CollectionAssert.Contains(registry.ConflictSources.ToList(), Restraint);

        var disabled = new List<string>();
        registry.GetDisabledBy(Restraint, new[] { Frenzy, Smash }, disabled);
        CollectionAssert.AreEqual(new[] { Frenzy }, disabled, "광란 강화만 효과를 잃는다(보유 목록엔 남음)");

        disabled.Clear();
        registry.GetDisabledBy(Restraint, new[] { Smash, Breakthrough }, disabled);
        Assert.IsEmpty(disabled, "광란 강화가 없으면 확인창 없음");

        Assert.IsTrue(registry.TryGetConflictText(Restraint, out string ko));
        StringAssert.Contains("{traits}", ko);
        StringAssert.Contains("광란 강화", TraitPickConfirmModel.FormatMessage(Restraint, new[] { "광란 강화" }, registry));
    }

    // ── 수치 — 피해 ×1.35 · 쿨 ×0.75 ─────────────────────────────────────

    [Test]
    public void Damage_NormalQAndEnhancedStrike_Times1_35_TransformedUnaffected()
    {
        TraitModifierSet set = SetOf(Restraint);
        foreach (TraitAttackKey key in AllKeys())
        {
            bool covered = key.Equals(NormalQ) || key.Equals(EnhancedBasic);
            Assert.AreEqual(covered ? 1.35f : 1f, set.DamageMultiplier(key), Eps, key.ToString());
        }

        Assert.AreEqual(1f, set.DamageMultiplier(NormalBasic), "일반 기본 공격 그대로");
        Assert.AreEqual(1f, set.DamageMultiplier(NormalE), "일반 E 준비 자체는 피해 없음 — 강타 키로 받는다");
        Assert.AreEqual(1f, set.DamageMultiplier(TransformedBasic), "변신 쪽 무관");
        Assert.AreEqual(1f, set.DamageMultiplier(TransformedQ), "변신 쪽 무관");
        Assert.AreEqual(1f, set.DamageMultiplier(TransformedE), "변신 쪽 무관");
        Assert.AreEqual(135, Hit(set, NormalQ));
        Assert.AreEqual(135, Hit(set, EnhancedBasic));
        Assert.AreEqual(100, Hit(set, NormalBasic));
    }

    [Test]
    public void Cooldown_NormalQAndE_Times0_75_TransformedRAndRightClickUnchanged()
    {
        TraitModifierSet set = SetOf(Restraint);
        Assert.AreEqual(0.75f, set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal), Eps);
        Assert.AreEqual(0.75f, set.CooldownMultiplier(PlayerSkillSlot.Sub, TraitAttackForm.Normal), Eps);
        Assert.AreEqual(1f, set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Alternate), Eps, "변신 Q");
        Assert.AreEqual(1f, set.CooldownMultiplier(PlayerSkillSlot.Sub, TraitAttackForm.Alternate), Eps, "변신 E");
        foreach (TraitAttackForm form in Enum.GetValues(typeof(TraitAttackForm)))
        {
            Assert.AreEqual(1f, set.CooldownMultiplier(PlayerSkillSlot.Interrupt, form), Eps, $"우클릭 {form}");
            Assert.AreEqual(1f, set.CooldownMultiplier(PlayerSkillSlot.Ultimate, form), Eps, $"R {form}");
        }

        TraitDefinition def = Load(Restraint);
        Assert.IsTrue(def.AffectsCooldownOf(PlayerSkillSlot.Main));
        Assert.IsTrue(def.AffectsCooldownOf(PlayerSkillSlot.Sub));
        Assert.IsFalse(def.AffectsCooldownOf(PlayerSkillSlot.Interrupt));
        Assert.IsFalse(def.AffectsCooldownOf(PlayerSkillSlot.Ultimate));
    }

    [Test]
    public void Spec10_BreakthroughPlusRestraint_NormalQ_3_1875Seconds()
    {
        TraitModifierSet set = SetOf(Breakthrough, Restraint);
        // 명세 §10: Q 기본 5초 × 0.85 × 0.75 = 3.1875초(서로 다른 특성은 곱).
        Assert.AreEqual(3.1875f, 5f * set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal), Eps);
        Assert.AreEqual(0.75f, set.CooldownMultiplier(PlayerSkillSlot.Sub, TraitAttackForm.Normal), Eps, "돌파 가속은 E 와 무관");
    }

    [Test]
    public void Spec10_SmashRestraintMomentum_210_6Percent_NextStrikeOnly()
    {
        TraitModifierSet set = SetOf(Smash, Restraint, Momentum);
        Assert.AreEqual(1.2f * 1.35f, set.DamageMultiplier(EnhancedBasic), Eps, "강타 강화 × 절제된 분노 — Dref 에 한 번");

        // 돌파의 여세 = 다음 기본 공격 1회 강화(⑨) — 정의 효과를 가짜 허브에 걸어 Q 적중 → 강타 첫 판정 소모.
        var host = new BuffHost();
        ITraitEffectBinding binding = Load(Momentum).Effects[0].BindServer(host, TraitIdHash.Of(Momentum));
        Assert.IsNotNull(binding);
        host.Raise(new TraitTriggerEvent(TraitTrigger.AttackLanded, AttackType.SkillQ, TraitAttackForm.Normal, 10, true, 1));

        float next = host.NextAttackBuffs.Consume(EnhancedBasic, 100, host.Now);
        Assert.AreEqual(1.3f, next, Eps);
        Assert.AreEqual(2.106f, set.DamageMultiplier(EnhancedBasic) * next, Eps, "명세 §10: 1.20 × 1.35 × 1.30 = 210.6%");
        Assert.AreEqual(211, Hit(set, EnhancedBasic, next), "공격력 100 — 마지막에 한 번 정수화(210.6 → 211)");

        float after = host.NextAttackBuffs.Consume(EnhancedBasic, 101, host.Now);
        Assert.AreEqual(1f, after, Eps, "다음 강타 1회만");
        Assert.AreEqual(162, Hit(set, EnhancedBasic, after), "그다음 강타 = 1.20 × 1.35 만");
        binding.Unbind();
    }

    sealed class BuffHost : ITraitEffectHost
    {
        public double Now => 0.0;
        public event Action<TraitTriggerEvent> Triggered;
        public void Raise(TraitTriggerEvent e) => Triggered?.Invoke(e);
        public float GetCooldownRemaining(PlayerSkillSlot slot) => 0f;
        public void ReduceCooldown(PlayerSkillSlot slot, float seconds) { }
        public int MaxHpOf(Unit target) => 1000;
        public void GrantTraitShield(Unit target, int traitHash, int amount, float duration) { }
        public TraitNextAttackBuffs NextAttackBuffs { get; } = new TraitNextAttackBuffs();
        public void AddAttackModifier(ITraitAttackModifier modifier) { }
        public void RemoveAttackModifier(ITraitAttackModifier modifier) { }
        public void AddDerivedAttack(TraitDerivedAttack attack) { }
        public void RemoveDerivedAttack(TraitDerivedAttack attack) { }
        public void AddOverflowShare(TraitOverflowShare share) { }
        public void RemoveOverflowShare(TraitOverflowShare share) { }
    }

    [Test]
    public void SameTraitTwice_AppliesOnce()
    {
        TraitModifierSet once = SetOf(Restraint);
        TraitModifierSet twice = SetOf(Restraint, Restraint);
        foreach (TraitAttackKey key in AllKeys())
            Assert.AreEqual(once.DamageMultiplier(key), twice.DamageMultiplier(key), Eps, key.ToString());
        foreach (PlayerSkillSlot slot in Enum.GetValues(typeof(PlayerSkillSlot)))
        foreach (TraitAttackForm form in Enum.GetValues(typeof(TraitAttackForm)))
            Assert.AreEqual(once.CooldownMultiplier(slot, form), twice.CooldownMultiplier(slot, form), Eps, $"{slot}/{form}");
    }

    // ── 모드 변경 — R 차단 · 분노 획득 0 · 백어택 유지 ───────────────────────

    [Test]
    public void ModeId_IsRegisteredSwitch()
    {
        Assert.AreEqual(Restraint, TraitModeIds.AssassinRestraint);
        CollectionAssert.Contains(TraitModeIds.All, Restraint);
    }

    [Test]
    public void R_Blocked_EvenWithFullRage()
    {
        var full = new AssassinStateSnapshot { rage = Rules.MaxRage };
        Assert.IsTrue(AssassinStateModel.CanBeginTransform(full, Rules), "전제: 분노 가득이면 원래 변신 가능");
        Assert.IsTrue(AssassinRestraintRules.CanBeginTransform(false, true), "특성 없음 = 원래 판정");
        Assert.IsFalse(AssassinRestraintRules.CanBeginTransform(true, true), "절제된 분노 = R 사용 불가");
        Assert.IsFalse(AssassinRestraintRules.CanBeginTransform(true, false));
    }

    [Test]
    public void RageGain_Stopped_AndLeftoverClearedToZero()
    {
        Assert.IsTrue(AssassinRestraintRules.CanGainRage(false));
        Assert.IsFalse(AssassinRestraintRules.CanGainRage(true), "적중 충전·훈련 보정 모두 막는다");

        var state = new AssassinStateSnapshot { rage = 45f, enhancedReady = true };
        Assert.AreEqual(AssassinRestraintAction.ClearRage, AssassinRestraintRules.Decide(true, state));
        Assert.IsTrue(AssassinRestraintRules.Apply(ref state, AssassinRestraintAction.ClearRage));
        Assert.AreEqual(0f, state.rage);
        Assert.IsTrue(state.enhancedReady, "일반 E 강화 준비는 그대로(일반 E 는 남는다)");
        Assert.AreEqual(AssassinRestraintAction.None, AssassinRestraintRules.Decide(true, state), "0 이면 더 할 일 없음");
        Assert.IsFalse(AssassinRestraintRules.Apply(ref state, AssassinRestraintAction.ClearRage));

        Assert.AreEqual(AssassinRestraintAction.None, AssassinRestraintRules.Decide(false, new AssassinStateSnapshot { rage = 45f }),
            "특성 없으면 분노 보존");
    }

    [Test]
    public void BackAttack_Kept_NoRestraintBranchInBackAttackPath()
    {
        // 패시브 백어택은 AssassinStateData.BackAttackMultiplier — 절제된 분노 분기는 R·분노만 본다.
        // 피해 배율(특성)은 백어택 키를 건드리지 않는다: 우클릭(Skill)·평타 일반 형태는 그대로 1.
        TraitModifierSet set = SetOf(Restraint);
        foreach (TraitAttackForm form in Enum.GetValues(typeof(TraitAttackForm)))
            Assert.AreEqual(1f, set.DamageMultiplier(new TraitAttackKey(AttackType.Skill, form)), Eps, $"우클릭 {form}");
        Assert.AreEqual(1f, set.DamageMultiplier(NormalBasic), Eps);
    }

    // ── 변신 중 선택 — 현재 원자 공격 완료 후 안전 종료 → 분노 0 ─────────────

    [Test]
    public void PickedWhileTransformed_WaitsForCurrentAttack_ThenEndsAndClearsRage()
    {
        // 변신 0.5초째(해제 제한 2초 전)에 고름 — 해제 제한과 무관하게 종료를 요청한다.
        AssassinStateSnapshot state = Transformed(10.0, 60f);
        double now = 10.5;
        Assert.IsFalse(AssassinStateModel.CanRequestRelease(state, now, Rules), "전제: 수동 해제는 아직 불가");

        AssassinRestraintAction action = AssassinRestraintRules.Decide(true, state);
        Assert.AreEqual(AssassinRestraintAction.RequestTransformEnd, action);
        Assert.IsTrue(AssassinRestraintRules.Apply(ref state, action));
        Assert.IsTrue(state.transformed, "즉시 강제 취소 안 함");
        Assert.IsTrue(AssassinStateModel.IsEndPending(state, now), "종료 대기 — 새 공격·다음 묶음 시작 안 함");
        Assert.AreEqual(AssassinRestraintAction.None, AssassinRestraintRules.Decide(true, state), "요청은 한 번");

        // 공격 중이면 기다린다(기존 안전 종료 경로).
        Assert.IsFalse(AssassinStateModel.ShouldFinishTransform(state, now, actionInProgress: true));
        now = 10.8;
        Assert.IsTrue(AssassinStateModel.ShouldFinishTransform(state, now, actionInProgress: false), "현재 원자 공격 완료 후 종료");
        Assert.IsTrue(AssassinStateModel.TryFinishTransform(ref state, now, Rules));
        Assert.IsFalse(state.transformed);
        Assert.Greater(state.rage, 0f, "기존 종료는 남은 분노를 보존 —");

        // 다음 틱: 일반 상태 → 분노 0.
        action = AssassinRestraintRules.Decide(true, state);
        Assert.AreEqual(AssassinRestraintAction.ClearRage, action);
        Assert.IsTrue(AssassinRestraintRules.Apply(ref state, action));
        Assert.AreEqual(0f, state.rage, "적용 후 분노 0");
        Assert.IsFalse(AssassinRestraintRules.CanBeginTransform(true, AssassinStateModel.CanBeginTransform(state, Rules)), "R 차단");
    }

    [Test]
    public void PickedWhileTransformed_AlreadyReleaseRequested_DoesNotTouch()
    {
        AssassinStateSnapshot state = Transformed(0.0, 50f);
        state.releaseRequested = true;
        AssassinStateSnapshot before = state;
        Assert.AreEqual(AssassinRestraintAction.None, AssassinRestraintRules.Decide(true, state));
        Assert.IsFalse(AssassinRestraintRules.Apply(ref state, AssassinRestraintAction.RequestTransformEnd));
        Assert.IsFalse(AssassinRestraintRules.Apply(ref state, AssassinRestraintAction.ClearRage), "변신 중엔 분노를 건드리지 않는다");
        Assert.IsTrue(before.Equals(state));
    }

    [Test]
    public void WithoutTrait_TransformUntouched()
    {
        AssassinStateSnapshot state = Transformed(0.0, 50f);
        Assert.AreEqual(AssassinRestraintAction.None, AssassinRestraintRules.Decide(false, state));
    }
}
