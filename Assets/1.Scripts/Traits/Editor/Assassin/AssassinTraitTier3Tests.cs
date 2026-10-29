using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 128 (2/5) — 어쌔신 3차 3개(칼날의 영역·돌파의 여세·사냥의 박자)의 명세 §11 수용 조건을 프로젝트 정의 에셋으로 본다.
/// 사건형(⑨·③)은 정의 효과를 가짜 허브에 걸어 순수 로직으로 돌린다. 가짜 허브는 어쌔신 스킬 컨트롤러처럼
/// Q·E 슬롯을 현재 상태(일반/변신) 스킬로 바인딩한다(<see cref="PlayerSkillController.GetSkill"/>).
/// 스킬 쪽 연결: 강타 부채꼴·변신 E 반경 = <see cref="AssassinTraitAreaRules"/>, 기본 공격 동작 시전 ID·첫 판정 소모 = <see cref="AssassinBasicAttackTraitRules"/>.
/// </summary>
public sealed class AssassinTraitTier3Tests
{
    const float Eps = 1e-4f;

    const string Area = "ASN_3_E_AREA";
    const string Momentum = "ASN_3_Q_BASIC";
    const string Rhythm = "ASN_3_BASIC_CDR";
    const string Smash = "ASN_1_NORMAL_E_DAMAGE";

    static readonly TraitAttackKey NormalBasic = new TraitAttackKey(AttackType.Default, TraitAttackForm.Normal);
    static readonly TraitAttackKey EnhancedBasic = new TraitAttackKey(AttackType.Default, TraitAttackForm.Enhanced);
    static readonly TraitAttackKey TransformedBasic = new TraitAttackKey(AttackType.Default, TraitAttackForm.Alternate);
    static readonly TraitAttackKey NormalQ = new TraitAttackKey(AttackType.SkillQ, TraitAttackForm.Normal);
    static readonly TraitAttackKey NormalE = new TraitAttackKey(AttackType.SkillE, TraitAttackForm.Normal);
    static readonly TraitAttackKey TransformedE = new TraitAttackKey(AttackType.SkillE, TraitAttackForm.Alternate);

    // ── 가짜 허브 — Q·E 는 현재 상태(일반/변신) 스킬의 쿨, 우클릭·R 은 따로 ─────────────

    sealed class AssassinHost : ITraitEffectHost
    {
        public double Time;
        public bool Transformed;
        public readonly Dictionary<PlayerSkillSlot, float> Normal = new Dictionary<PlayerSkillSlot, float>();
        public readonly Dictionary<PlayerSkillSlot, float> Alternate = new Dictionary<PlayerSkillSlot, float>();
        public readonly Dictionary<PlayerSkillSlot, float> Shared = new Dictionary<PlayerSkillSlot, float>();

        public double Now => Time;
        public event Action<TraitTriggerEvent> Triggered;

        public void Raise(TraitTriggerEvent e) => Triggered?.Invoke(e);

        Dictionary<PlayerSkillSlot, float> Bound(PlayerSkillSlot slot) =>
            slot == PlayerSkillSlot.Main || slot == PlayerSkillSlot.Sub ? (Transformed ? Alternate : Normal) : Shared;

        public float GetCooldownRemaining(PlayerSkillSlot slot) => Bound(slot).TryGetValue(slot, out float r) ? r : 0f;

        public void ReduceCooldown(PlayerSkillSlot slot, float seconds) =>
            Bound(slot)[slot] = TraitCooldownRefundRules.Reduce(GetCooldownRemaining(slot), seconds);

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

    static ITraitEffectBinding Bind(string id, ITraitEffectHost host)
    {
        TraitDefinition def = Load(id);
        Assert.AreEqual(1, def.Effects.Count, id);
        ITraitEffectBinding binding = def.Effects[0].BindServer(host, TraitIdHash.Of(id));
        Assert.IsNotNull(binding, $"{id} 는 사건형 효과(서버 구독)");
        return binding;
    }

    static string Card(string id)
    {
        TraitDefinition def = Load(id);
        Assert.IsTrue(TraitTooltip.Format(def, def.InlineDescription, null, false, TraitContent.Build(), out string text, out string error), error);
        return text;
    }

    static TraitTriggerEvent Landed(AttackType type, int castId, TraitAttackForm form = TraitAttackForm.Normal) =>
        new TraitTriggerEvent(TraitTrigger.AttackLanded, type, form, castId, true, 1);

    sealed class SetSource : IPlayerStatSource
    {
        readonly TraitModifierSet set;
        public SetSource(TraitModifierSet set) => this.set = set;
        public float GetBasePercent(PlayerStatKind kind) => set.BasePercent(kind);
        public float GetTotalPercent(PlayerStatKind kind) => set.TotalPercent(kind);
        public float DealtDamagePercent => set.DealtDamagePercent;
        public event Action Changed { add { } remove { } }
    }

    /// <summary>공격력 100 플레이어의 원래 타격(계수 1) = Dref × 다음 공격 강화 — 어쌔신 평타 ResolveHitDamage 와 같은 식.</summary>
    static int Hit(TraitModifierSet set, TraitAttackKey key, float nextAttackMultiplier)
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

    // ── 정의 데이터 ─────────────────────────────────────────────────────

    [Test]
    public void Definitions_HaveSpecNamesAndCategories_AndValidData()
    {
        var expected = new (string id, string name, string category, int effects)[]
        {
            (Area, "칼날의 영역", "E 강화", 2),
            (Momentum, "돌파의 여세", "Q 연계 강화", 1),
            (Rhythm, "사냥의 박자", "기본 공격 연계", 1),
        };

        foreach (var (id, name, category, effects) in expected)
        {
            TraitDefinition def = Load(id);
            Assert.AreEqual(id, def.name, "파일 이름 = 특성 ID(시트 열 이름)");
            Assert.AreEqual(name, def.InlineDisplayName, id);
            Assert.AreEqual(category, def.InlineCategory, id);
            var errors = new List<string>();
            def.Validate(errors);
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual(effects, def.Effects.Count, $"{id} 효과 칸 수(시트 행 effects[i].*)");
        }
    }

    [Test]
    public void Pool_Tier3_AfterRelease_InSpecOrder()
    {
        string[] poolIds = TraitContentTestKit.CharacterPool(TutorialCharacter.Assassin).Traits.Select(t => t.Id).ToArray();
        Assert.GreaterOrEqual(Array.IndexOf(poolIds, "ASN_2_RELEASE"), 0);
        // 2차(완전 해방 · 절제된 분노 · 그림자 회귀 — 들어와 있는 것) 마지막 뒤.
        int lastTier2 = Array.FindLastIndex(poolIds, id => id.StartsWith("ASN_2_", StringComparison.Ordinal));
        CollectionAssert.AreEqual(new[] { Area, Momentum, Rhythm }, poolIds.Skip(lastTier2 + 1).Take(3).ToArray(),
            "3차는 명세 순서(칼날의 영역 → 돌파의 여세 → 사냥의 박자)");
    }

    [Test]
    public void Cards_ResolveToSpecSelectionText()
    {
        Assert.AreEqual("일반 E 강타의 공격 범위와 변신 E의 반경이 25% 늘어납니다.", Card(Area));
        Assert.AreEqual("Q를 맞히면 4초 안에 하는 다음 기본 공격의 첫 타격 피해가 30% 증가합니다. 일반 E 강타에도 적용됩니다.", Card(Momentum));
        Assert.AreEqual("기본 공격을 맞히면 Q와 E의 남은 재사용 대기시간이 각각 0.2초 줄어듭니다. 0.8초마다 한 번 발동합니다.", Card(Rhythm));
    }

    // ── 칼날의 영역 ─────────────────────────────────────────────────────

    [Test]
    public void Area_StrikeLengthWidth_AndCircleRadius_Times1_25_OnlyThere()
    {
        TraitModifierSet set = SetOf(Area);
        Assert.AreEqual(1.25f, set.AreaMultiplier(EnhancedBasic, TraitAreaAxis.Length), Eps, "강타 길이");
        Assert.AreEqual(1.25f, set.AreaMultiplier(EnhancedBasic, TraitAreaAxis.Width), Eps, "강타 폭");
        Assert.AreEqual(1.25f, set.AreaMultiplier(TransformedE, TraitAreaAxis.Radius), Eps, "변신 E 반경");

        foreach (TraitAreaAxis axis in Enum.GetValues(typeof(TraitAreaAxis)))
        {
            Assert.AreEqual(1f, set.AreaMultiplier(NormalBasic, axis), Eps, $"일반 평타 {axis}");
            Assert.AreEqual(1f, set.AreaMultiplier(TransformedBasic, axis), Eps, $"변신 평타 묶음 {axis}");
            Assert.AreEqual(1f, set.AreaMultiplier(NormalQ, axis), Eps, $"Q {axis}");
            Assert.AreEqual(1f, set.AreaMultiplier(NormalE, axis), Eps, $"일반 E(준비) {axis}");
        }

        Assert.AreEqual(1f, set.AreaMultiplier(EnhancedBasic, TraitAreaAxis.Radius), Eps, "강타는 반경 축이 없다");
        Assert.AreEqual(1f, set.AreaMultiplier(TransformedE, TraitAreaAxis.Length), Eps, "변신 E 는 반경만");
        Assert.AreEqual(1f, set.DamageMultiplier(EnhancedBasic), Eps, "피해는 그대로");
    }

    [Test]
    public void Area_NotOwned_AllOne()
    {
        TraitModifierSet set = SetOf();
        foreach (TraitAreaAxis axis in Enum.GetValues(typeof(TraitAreaAxis)))
        {
            Assert.AreEqual(1f, set.AreaMultiplier(EnhancedBasic, axis), Eps);
            Assert.AreEqual(1f, set.AreaMultiplier(TransformedE, axis), Eps);
        }
    }

    [Test]
    public void Area_StrikeBox_DimensionsTimes1_25_NotArea()
    {
        TraitModifierSet set = SetOf(Area);
        float length = set.AreaMultiplier(AssassinTraitAreaRules.EnhancedStrike, TraitAreaAxis.Length);
        float width = set.AreaMultiplier(AssassinTraitAreaRules.EnhancedStrike, TraitAreaAxis.Width);
        Assert.AreEqual(1.25f, length, Eps, "길이 ×1.25");
        Assert.AreEqual(1.25f, width, Eps, "폭 ×1.25");

        // 강타 기본 상자(할 일 144) — 몸에서 앞 1.8m, 폭 3.12m. 콜라이더 스케일 배율로 앞 끝 2.25m·반폭 1.95m.
        var center = new Vector3(0f, 1f, 0.9f);
        var size = new Vector3(3.12f, 2f, 1.8f);
        AssassinStrikeBox scaled = AssassinStrikeBox.Place(Vector3.zero, Vector3.forward, Matrix4x4.identity, center, size, length, width);
        Assert.AreEqual(1.125f, scaled.Center.z, Eps, "중심도 몸 기준으로 같은 배율 — 뒤 끝은 몸 그대로");
        Assert.AreEqual(1.125f, scaled.HalfExtents.z, Eps);
        Assert.AreEqual(1.95f, scaled.HalfExtents.x, Eps);
        Assert.AreEqual(1f, scaled.HalfExtents.y, Eps, "높이는 그대로");
        Assert.IsTrue(scaled.Contains(new Vector3(0f, 1f, 2.2f)));
        Assert.IsFalse(scaled.Contains(new Vector3(0f, 1f, 2.3f)));

        // 미보유 = 그대로
        TraitModifierSet none = SetOf();
        AssassinStrikeBox plain = AssassinStrikeBox.Place(Vector3.zero, Vector3.forward, Matrix4x4.identity, center, size,
            none.AreaMultiplier(AssassinTraitAreaRules.EnhancedStrike, TraitAreaAxis.Length),
            none.AreaMultiplier(AssassinTraitAreaRules.EnhancedStrike, TraitAreaAxis.Width));
        Assert.AreEqual(0.9f, plain.HalfExtents.z, Eps);
        Assert.AreEqual(1.56f, plain.HalfExtents.x, Eps);
    }

    [Test]
    public void Area_BoxRule_WidthOnly_KeepsLength()
    {
        AssassinStrikeBox box = AssassinStrikeBox.Place(Vector3.zero, Vector3.forward, Matrix4x4.identity,
            new Vector3(0f, 1f, 1f), new Vector3(2f, 2f, 2f), 1f, 1.25f);
        Assert.AreEqual(1f, box.HalfExtents.z, Eps);
        Assert.AreEqual(1f, box.Center.z, Eps);
        Assert.AreEqual(1.25f, box.HalfExtents.x, Eps);
    }

    [Test]
    public void Area_CircleRadius_Times1_25_AllFiveStrikesUseIt()
    {
        TraitModifierSet set = SetOf(Area);
        float radius = AssassinTraitAreaRules.ScaleRadius(2f, set.AreaMultiplier(AssassinTraitAreaRules.CircleStrike, TraitAreaAxis.Radius));
        Assert.AreEqual(2.5f, radius, Eps);
        Assert.AreEqual(2f, AssassinTraitAreaRules.ScaleRadius(2f, SetOf().AreaMultiplier(AssassinTraitAreaRules.CircleStrike, TraitAreaAxis.Radius)), Eps);

        // 원 판정·오너 미리보기 공용 부피 — 반경 2.5 원 가장자리(2.4m)는 들어오고 2.6m 는 밖.
        var data = ScriptableObject.CreateInstance<AssassinCircleStrikeSkillData>();
        try
        {
            SkillHitVolume volume = AssassinCircleStrikeSkill.HitVolume(Vector3.zero, data, radius);
            Assert.IsTrue(volume.Accepts(new Bounds(new Vector3(2.4f, 0.5f, 0f), Vector3.one * 0.01f)));
            Assert.IsFalse(volume.Accepts(new Bounds(new Vector3(2.6f, 0.5f, 0f), Vector3.one * 0.01f)));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(data);
        }
    }

    [Test]
    public void Area_TransformedE_AimMarkerRadius_EqualsHitRadius()
    {
        // 조준 원(PlayerSkillTargeting) = 그 칸 스킬 키(E·변신)로 같은 반경 배율을 묻는다 — 판정(ScaledRadius)과 같은 값(할 일 133 d).
        TraitAttackKey markerKey = TraitAttackKey.ForSkill(PlayerSkillSlot.Sub, alternate: true);
        Assert.AreEqual(AssassinTraitAreaRules.CircleStrike, markerKey, "변신 E 칸 키 = 판정 키");

        var data = ScriptableObject.CreateInstance<AssassinCircleStrikeSkillData>();
        try
        {
            var so = new UnityEditor.SerializedObject(data);
            so.FindProperty("aoeRadius").floatValue = 2f;
            so.ApplyModifiedPropertiesWithoutUndo();

            TraitModifierSet owned = SetOf(Area);
            float hit = AssassinTraitAreaRules.ScaleRadius(data.AreaRadius, owned.AreaMultiplier(AssassinTraitAreaRules.CircleStrike, TraitAreaAxis.Radius));
            float marker = PlayerSkillTargeting.GroundMarkerRadius(data, owned.AreaMultiplier(markerKey, TraitAreaAxis.Radius));
            Assert.AreEqual(2.5f, hit, Eps);
            Assert.AreEqual(hit, marker, Eps, "보유 = 표시 반경도 ×1.25");

            Assert.AreEqual(2f, PlayerSkillTargeting.GroundMarkerRadius(data, SetOf().AreaMultiplier(markerKey, TraitAreaAxis.Radius)), Eps,
                "미보유 = 기존 값(데이터 반경)");
            Assert.AreEqual(2f, PlayerSkillTargeting.GroundMarkerRadius(data, owned.AreaMultiplier(NormalE, TraitAreaAxis.Radius)), Eps,
                "일반 E 칸은 배율 없음");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(data);
        }
    }

    // ── 돌파의 여세 ─────────────────────────────────────────────────────

    [Test]
    public void Momentum_QFirstLanding_Grants_BasicConsumesOnce_Times1_3()
    {
        var host = new AssassinHost();
        ITraitEffectBinding binding = Bind(Momentum, host);

        host.Raise(Landed(AttackType.Default, 1));
        Assert.AreEqual(0, host.NextAttackBuffs.Count, "기본 공격 적중은 부여 안 함");
        host.Raise(Landed(AttackType.Skill, 2));
        Assert.AreEqual(0, host.NextAttackBuffs.Count, "우클릭은 부여 안 함");

        host.Raise(Landed(AttackType.SkillQ, 10));
        Assert.IsTrue(host.NextAttackBuffs.Has(TraitIdHash.Of(Momentum), host.Now));
        Assert.AreEqual(1f, host.NextAttackBuffs.Consume(NormalQ, 10, host.Now), Eps, "Q 자신은 소모 안 함");

        host.Time = 1;
        Assert.AreEqual(1.3f, host.NextAttackBuffs.Consume(NormalBasic, 100, host.Now), Eps, "다음 기본 공격 첫 판정");
        Assert.AreEqual(1.3f, host.NextAttackBuffs.Consume(NormalBasic, 100, host.Now), Eps, "같은 판정의 다른 적 = 같은 배율(적 수만큼 소모 안 함)");
        Assert.AreEqual(1f, host.NextAttackBuffs.Consume(NormalBasic, 101, host.Now), Eps, "다음 동작은 강화 없음");
        binding.Unbind();
    }

    [Test]
    public void Momentum_TransformedQ_AlsoGrants_AndOncePerCast()
    {
        var host = new AssassinHost();
        ITraitEffectBinding binding = Bind(Momentum, host);

        host.Raise(Landed(AttackType.SkillQ, 20, TraitAttackForm.Alternate));
        Assert.AreEqual(1, host.NextAttackBuffs.Count, "변신 Q 도 부여");

        // 같은 시전의 이후 틱 적중은 다시 부여하지 않는다 — 지속시간이 처음 부여 기준으로 끝난다.
        host.Time = 3;
        host.Raise(Landed(AttackType.SkillQ, 20, TraitAttackForm.Alternate));
        host.Time = 4.01;
        Assert.AreEqual(1f, host.NextAttackBuffs.Consume(TransformedBasic, 200, host.Now), Eps, "시전당 최초 적중 1회 — 3초 재적중으로 갱신 안 됨");
        binding.Unbind();
    }

    [Test]
    public void Momentum_Regrant_RefreshesWithoutStacking()
    {
        var host = new AssassinHost();
        ITraitEffectBinding binding = Bind(Momentum, host);

        host.Raise(Landed(AttackType.SkillQ, 30));
        host.Time = 3;
        host.Raise(Landed(AttackType.SkillQ, 31)); // 새 Q 시전 → 지속 갱신(3+4=7)
        Assert.AreEqual(1, host.NextAttackBuffs.Count, "중첩 없음");

        host.Time = 6.5;
        Assert.AreEqual(1.3f, host.NextAttackBuffs.Consume(NormalBasic, 300, host.Now), Eps, "×1.3 한 번(1.69 아님), 갱신된 지속 안");
        binding.Unbind();
    }

    [Test]
    public void Momentum_Expires_After4Seconds()
    {
        var host = new AssassinHost();
        ITraitEffectBinding binding = Bind(Momentum, host);

        host.Raise(Landed(AttackType.SkillQ, 40));
        host.Time = 4.0;
        Assert.AreEqual(1f, host.NextAttackBuffs.Consume(NormalBasic, 400, host.Now), Eps);
        binding.Unbind();
    }

    [Test]
    public void Momentum_MissStillConsumes_AtFirstJudgment()
    {
        var host = new AssassinHost();
        ITraitEffectBinding binding = Bind(Momentum, host);
        host.Raise(Landed(AttackType.SkillQ, 50));

        // 빗나간 동작도 첫 판정에서 소모를 묻는다(적중과 무관 — 스킬이 판정 전에 부른다).
        Assert.IsTrue(AssassinBasicAttackTraitRules.ConsumesNextAttackBuff(1));
        Assert.AreEqual(1.3f, host.NextAttackBuffs.Consume(NormalBasic, 500, host.Now), Eps, "빗나간 판정에 소모");
        Assert.AreEqual(1f, host.NextAttackBuffs.Consume(NormalBasic, 501, host.Now), Eps, "다음 동작엔 남지 않는다");
        binding.Unbind();
    }

    [Test]
    public void Momentum_TransformedBundle_OnlyFirstHitEmpowered()
    {
        var host = new AssassinHost();
        ITraitEffectBinding binding = Bind(Momentum, host);
        host.Raise(Landed(AttackType.SkillQ, 60));

        // 변신 4타 묶음 = 한 동작(시전 ID 하나). 판정 번호 1 만 소모를 묻는다.
        const int bundleCast = 600;
        var multipliers = new List<float>();
        for (int hit = 1; hit <= 4; hit++)
            multipliers.Add(AssassinBasicAttackTraitRules.ConsumesNextAttackBuff(hit)
                ? host.NextAttackBuffs.Consume(TransformedBasic, bundleCast, host.Now)
                : 1f);

        CollectionAssert.AreEqual(new[] { 1.3f, 1f, 1f, 1f }, multipliers);
        Assert.AreEqual(1f, host.NextAttackBuffs.Consume(TransformedBasic, 601, host.Now), Eps);
        binding.Unbind();
    }

    [Test]
    public void Momentum_AppliesToEnhancedStrike_MultipliesWithSmash()
    {
        var host = new AssassinHost();
        ITraitEffectBinding binding = Bind(Momentum, host);
        host.Raise(Landed(AttackType.SkillQ, 70));

        float next = host.NextAttackBuffs.Consume(EnhancedBasic, 700, host.Now);
        Assert.AreEqual(1.3f, next, Eps, "일반 E 강타가 다음 기본 공격이면 강타에");
        Assert.AreEqual(130, Hit(SetOf(), EnhancedBasic, next));
        Assert.AreEqual(156, Hit(SetOf(Smash), EnhancedBasic, next), "강타 강화 1.2 × 여세 1.3 — 마지막에 한 번 정수화");
        binding.Unbind();
    }

    [Test]
    public void Momentum_DeathClears_UnbindRemoves()
    {
        var host = new AssassinHost();
        ITraitEffectBinding binding = Bind(Momentum, host);
        int hash = TraitIdHash.Of(Momentum);

        host.Raise(Landed(AttackType.SkillQ, 80));
        host.NextAttackBuffs.Clear(); // 사망·새 회차 = PlayerTraitEffects.ClearTransientServer
        Assert.IsFalse(host.NextAttackBuffs.Has(hash, host.Now), "사망 제거");
        Assert.AreEqual(1f, host.NextAttackBuffs.Consume(NormalBasic, 800, host.Now), Eps);

        host.Raise(Landed(AttackType.SkillQ, 81));
        binding.Unbind();
        Assert.IsFalse(host.NextAttackBuffs.Has(hash, host.Now), "특성 되돌리기 = 제거");
        host.Raise(Landed(AttackType.SkillQ, 82));
        Assert.AreEqual(0, host.NextAttackBuffs.Count, "되돌린 뒤 구독 없음");
    }

    // ── 사냥의 박자 ─────────────────────────────────────────────────────

    static AssassinHost RhythmHost(out ITraitEffectBinding binding)
    {
        var host = new AssassinHost();
        host.Normal[PlayerSkillSlot.Main] = 5f;
        host.Normal[PlayerSkillSlot.Sub] = 5f;
        host.Alternate[PlayerSkillSlot.Main] = 5f;
        host.Alternate[PlayerSkillSlot.Sub] = 5f;
        host.Shared[PlayerSkillSlot.Interrupt] = 5f;
        host.Shared[PlayerSkillSlot.Ultimate] = 5f;
        binding = Bind(Rhythm, host);
        return host;
    }

    [Test]
    public void Rhythm_BasicHit_ReducesCurrentQAndE_Each0_2_ExcludesRightClickAndR()
    {
        AssassinHost host = RhythmHost(out ITraitEffectBinding binding);

        host.Raise(Landed(AttackType.Default, 1));
        Assert.AreEqual(4.8f, host.Normal[PlayerSkillSlot.Main], Eps, "일반 Q −0.2");
        Assert.AreEqual(4.8f, host.Normal[PlayerSkillSlot.Sub], Eps, "일반 E −0.2");
        Assert.AreEqual(5f, host.Alternate[PlayerSkillSlot.Main], Eps, "비활성 상태(변신) Q 제외");
        Assert.AreEqual(5f, host.Alternate[PlayerSkillSlot.Sub], Eps, "비활성 상태(변신) E 제외");
        Assert.AreEqual(5f, host.Shared[PlayerSkillSlot.Interrupt], Eps, "우클릭 제외");
        Assert.AreEqual(5f, host.Shared[PlayerSkillSlot.Ultimate], Eps, "R 제외");
        binding.Unbind();
    }

    [Test]
    public void Rhythm_Transformed_ReducesTransformedSlotsOnly()
    {
        AssassinHost host = RhythmHost(out ITraitEffectBinding binding);
        host.Transformed = true;

        host.Raise(Landed(AttackType.Default, 1));
        Assert.AreEqual(4.8f, host.Alternate[PlayerSkillSlot.Main], Eps);
        Assert.AreEqual(4.8f, host.Alternate[PlayerSkillSlot.Sub], Eps);
        Assert.AreEqual(5f, host.Normal[PlayerSkillSlot.Main], Eps, "비활성 일반 Q 제외");
        Assert.AreEqual(5f, host.Normal[PlayerSkillSlot.Sub], Eps, "비활성 일반 E 제외");
        binding.Unbind();
    }

    [Test]
    public void Rhythm_Interval0_8_AndOncePerAction()
    {
        AssassinHost host = RhythmHost(out ITraitEffectBinding binding);

        host.Raise(Landed(AttackType.Default, 1));          // t 0 — 발동
        host.Time = 0.5;
        host.Raise(Landed(AttackType.Default, 2));          // 다른 동작이지만 0.8초 안 — 거부
        Assert.AreEqual(4.8f, host.Normal[PlayerSkillSlot.Main], Eps);

        host.Time = 0.8;
        host.Raise(Landed(AttackType.Default, 3));          // 정확히 0.8초 — 발동
        Assert.AreEqual(4.6f, host.Normal[PlayerSkillSlot.Main], Eps);

        host.Time = 2.0;
        host.Raise(Landed(AttackType.Default, 3));          // 같은 동작(변신 묶음 다음 타) — 간격이 지나도 거부
        Assert.AreEqual(4.6f, host.Normal[PlayerSkillSlot.Main], Eps, "동작당 1회");
        binding.Unbind();
    }

    [Test]
    public void Rhythm_TransformedBundle_FourHitsOneAction_ProcsOnce()
    {
        AssassinHost host = RhythmHost(out ITraitEffectBinding binding);
        host.Transformed = true;

        for (int hit = 0; hit < 4; hit++)
        {
            host.Time = hit * 0.9; // 간격을 넘겨도 같은 동작이면 한 번
            host.Raise(Landed(AttackType.Default, 7, TraitAttackForm.Normal));
        }

        Assert.AreEqual(4.8f, host.Alternate[PlayerSkillSlot.Main], Eps);
        binding.Unbind();
    }

    [Test]
    public void Rhythm_FloorAtZero_AndNoCreditForUnstartedSlot()
    {
        AssassinHost host = RhythmHost(out ITraitEffectBinding binding);
        host.Normal[PlayerSkillSlot.Main] = 0.1f;
        host.Normal[PlayerSkillSlot.Sub] = 0f; // 준비 중인 일반 E — 쿨 시작 전

        host.Raise(Landed(AttackType.Default, 1));
        Assert.AreEqual(0f, host.Normal[PlayerSkillSlot.Main], Eps, "0 아래로 안 감");
        Assert.AreEqual(0f, host.Normal[PlayerSkillSlot.Sub], Eps);

        // 강타로 E 쿨이 시작된 뒤 — 앞의 발동이 저장돼 있지 않다(0.2 만 줄어든다).
        host.Normal[PlayerSkillSlot.Sub] = 5f;
        host.Time = 1.0;
        host.Raise(Landed(AttackType.Default, 2));
        Assert.AreEqual(4.8f, host.Normal[PlayerSkillSlot.Sub], Eps, "미시작 슬롯엔 저장 안 함");
        binding.Unbind();
    }

    [Test]
    public void Rhythm_SkillHits_DoNotProc()
    {
        AssassinHost host = RhythmHost(out ITraitEffectBinding binding);

        host.Raise(Landed(AttackType.SkillQ, 1));
        host.Raise(Landed(AttackType.SkillE, 2, TraitAttackForm.Alternate));
        host.Raise(Landed(AttackType.Skill, 3));
        host.Raise(Landed(AttackType.SkillR, 4));
        Assert.AreEqual(5f, host.Normal[PlayerSkillSlot.Main], Eps);
        Assert.AreEqual(5f, host.Normal[PlayerSkillSlot.Sub], Eps);

        // 강타는 기본 공격(Default) 사건으로 온다 — 발동.
        host.Raise(Landed(AttackType.Default, 5));
        Assert.AreEqual(4.8f, host.Normal[PlayerSkillSlot.Main], Eps);
        binding.Unbind();
    }

    [Test]
    public void Rhythm_TransformToggle_DoesNotResetInterval()
    {
        AssassinHost host = RhythmHost(out ITraitEffectBinding binding);

        host.Raise(Landed(AttackType.Default, 1));
        host.Transformed = true; // R 전환
        host.Time = 0.5;
        host.Raise(Landed(AttackType.Default, 2));
        Assert.AreEqual(5f, host.Alternate[PlayerSkillSlot.Main], Eps, "R 전환으로 간격 초기화 안 함");
        binding.Unbind();
    }

    [Test]
    public void Rhythm_AffectsQAndECooldownOnly_ForHud()
    {
        TraitDefinition def = Load(Rhythm);
        Assert.IsTrue(def.AffectsCooldownOf(PlayerSkillSlot.Main));
        Assert.IsTrue(def.AffectsCooldownOf(PlayerSkillSlot.Sub));
        Assert.IsFalse(def.AffectsCooldownOf(PlayerSkillSlot.Interrupt));
        Assert.IsFalse(def.AffectsCooldownOf(PlayerSkillSlot.Ultimate));
    }
}
