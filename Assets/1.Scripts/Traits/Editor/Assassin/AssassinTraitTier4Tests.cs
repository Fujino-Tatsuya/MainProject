using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 128 (3/5) — 어쌔신 4차 3개(급소 파괴·추격의 궤적·끝없는 칼날)의 명세 §11 수용 조건을 프로젝트 정의 에셋으로 본다.
/// 정의 효과를 가짜 허브에 걸어(밀치기 수정자·파생 공격 명세) 순수 로직으로 돌린다.
/// 스킬 쪽 연결: 밀치기 방향 = <see cref="AssassinStrikePushRules"/>, 궤적 예약·선분 = <see cref="AssassinTrailRules"/>,
/// 추가 베기 자리·지연 = <see cref="AssassinComboExtraRules"/>, 예약 줄 = <see cref="AssassinDelayedStrikeQueue{T}"/>, 카드 {dmg} = <see cref="AssassinDerivedTooltipRules"/>.
/// </summary>
public sealed class AssassinTraitTier4Tests
{
    const float Eps = 1e-4f;

    const string Push = "ASN_4_NORMAL_E_PUSH";
    const string Trail = "ASN_4_Q_TRAIL";
    const string Combo = "ASN_4_COMBO_EXTRA";

    static readonly TraitAttackKey NormalBasic = new TraitAttackKey(AttackType.Default, TraitAttackForm.Normal);
    static readonly TraitAttackKey EnhancedBasic = new TraitAttackKey(AttackType.Default, TraitAttackForm.Enhanced);
    static readonly TraitAttackKey TransformedBasic = new TraitAttackKey(AttackType.Default, TraitAttackForm.Alternate);
    static readonly TraitAttackKey NormalQ = new TraitAttackKey(AttackType.SkillQ, TraitAttackForm.Normal);
    static readonly TraitAttackKey TransformedQ = new TraitAttackKey(AttackType.SkillQ, TraitAttackForm.Alternate);
    static readonly TraitAttackKey NormalE = new TraitAttackKey(AttackType.SkillE, TraitAttackForm.Normal);
    static readonly TraitAttackKey TransformedE = new TraitAttackKey(AttackType.SkillE, TraitAttackForm.Alternate);
    static readonly TraitAttackKey Interrupt = new TraitAttackKey(AttackType.Skill, TraitAttackForm.Normal);

    // ── 가짜 허브 — 밀치기 수정자·파생 공격 명세를 모은다 ─────────────

    sealed class CaptureHost : ITraitEffectHost
    {
        public readonly List<ITraitAttackModifier> Modifiers = new List<ITraitAttackModifier>();
        public readonly List<TraitDerivedAttack> Derived = new List<TraitDerivedAttack>();

        public double Now => 0;
        public event Action<TraitTriggerEvent> Triggered { add { } remove { } }
        public float GetCooldownRemaining(PlayerSkillSlot slot) => 0f;
        public void ReduceCooldown(PlayerSkillSlot slot, float seconds) { }
        public int MaxHpOf(Unit target) => 1000;
        public void GrantTraitShield(Unit target, int traitHash, int amount, float duration) { }
        public TraitNextAttackBuffs NextAttackBuffs { get; } = new TraitNextAttackBuffs();
        public void AddAttackModifier(ITraitAttackModifier modifier) => Modifiers.Add(modifier);
        public void RemoveAttackModifier(ITraitAttackModifier modifier) => Modifiers.Remove(modifier);
        public void AddDerivedAttack(TraitDerivedAttack attack) => Derived.Add(attack);
        public void RemoveDerivedAttack(TraitDerivedAttack attack) => Derived.Remove(attack);
        public void AddOverflowShare(TraitOverflowShare share) { }
        public void RemoveOverflowShare(TraitOverflowShare share) { }

        // PlayerTraitEffects.ModifyOutgoingAttackServer 와 같은 순회
        public AttackInfo Hit(TraitAttackKey key, int castId, int targetId, Vector3 direction)
        {
            var info = new AttackInfo(100, key.Type);
            for (int i = 0; i < Modifiers.Count; i++)
                Modifiers[i].Modify(key, castId, targetId, direction, ref info);
            return info;
        }

        // PlayerTraitEffects.GetDerivedAttacks 와 같은 거름
        public List<TraitDerivedAttack> DerivedFor(TraitAttackKey key) => Derived.Where(d => d.Matches(key)).ToList();
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

    static ITraitEffectBinding Bind(string id, ITraitEffectHost host)
    {
        TraitDefinition def = Load(id);
        Assert.AreEqual(1, def.Effects.Count, id);
        ITraitEffectBinding binding = def.Effects[0].BindServer(host, TraitIdHash.Of(id));
        Assert.IsNotNull(binding, $"{id} 는 서버 등록 효과");
        return binding;
    }

    static string Card(string id)
    {
        TraitDefinition def = Load(id);
        Assert.IsTrue(TraitTooltip.Format(def, def.InlineDescription, null, false, TraitContent.Build(), out string text, out string error), error);
        return text;
    }

    static TraitDerivedAttack DerivedSpec(string id, CaptureHost host, string key)
    {
        Bind(id, host);
        TraitDerivedAttack spec = host.Derived.SingleOrDefault();
        Assert.IsNotNull(spec, $"{id} 파생 공격 명세 1개");
        Assert.AreEqual(key, spec.Key, "derivedKey = 스킬 코드 이름");
        return spec;
    }

    // ── 정의 데이터 ─────────────────────────────────────────────────────

    [Test]
    public void Definitions_HaveSpecNamesAndCategories_AndValidData()
    {
        var expected = new (string id, string name, string category, Type effect)[]
        {
            (Push, "급소 파괴", "일반 E 강화 · 밀치기", typeof(TraitKnockbackEffect)),
            (Trail, "추격의 궤적", "Q 강화", typeof(TraitDerivedAttackEffect)),
            (Combo, "끝없는 칼날", "기본 공격 강화", typeof(TraitDerivedAttackEffect)),
        };

        foreach (var (id, name, category, effect) in expected)
        {
            TraitDefinition def = Load(id);
            Assert.AreEqual(id, def.name, "파일 이름 = 특성 ID(시트 열 이름)");
            Assert.AreEqual(name, def.InlineDisplayName, id);
            Assert.AreEqual(category, def.InlineCategory, id);
            var errors = new List<string>();
            def.Validate(errors);
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual(1, def.Effects.Count, $"{id} 효과 칸 1개(시트 행 effects[0].*)");
            Assert.IsInstanceOf(effect, def.Effects[0], id);
        }
    }

    [Test]
    public void Pool_Tier4_AfterTier3_InSpecOrder()
    {
        string[] poolIds = TraitContentTestKit.CharacterPool(TutorialCharacter.Assassin).Traits.Select(t => t.Id).ToArray();
        int rhythm = Array.IndexOf(poolIds, "ASN_3_BASIC_CDR");
        Assert.GreaterOrEqual(rhythm, 0);
        CollectionAssert.AreEqual(new[] { Push, Trail, Combo }, poolIds.Skip(rhythm + 1).Take(3).ToArray(),
            "4차는 명세 순서(급소 파괴 → 추격의 궤적 → 끝없는 칼날)");
        Assert.AreEqual(rhythm + 4, poolIds.Length, "4차가 풀 끝");
    }

    [Test]
    public void Cards_ResolveAllTokens_DmgWithoutPlayerIsUnknown()
    {
        Assert.AreEqual("일반 E로 강화한 기본 공격이 적을 밀쳐냅니다. 변신 E에는 적용되지 않습니다.", Card(Push));

        string trail = Card(Trail);
        string combo = Card(Combo);
        foreach (string text in new[] { trail, combo })
        {
            StringAssert.DoesNotContain("{", text, "토큰 미해결 없음");
            StringAssert.Contains(TraitUiStrings.DamageUnknown.Text, text, "로컬 플레이어 없음 = '-'");
        }

        StringAssert.StartsWith("Q로 지나온 길을 한 번 더 베어 ", trail);
        StringAssert.EndsWith("의 추가 피해를 줍니다. 그림자 회귀의 복귀 경로에도 적용됩니다.", trail);
        StringAssert.StartsWith("기본 공격 4타를 끝까지 쓰면 마지막 공격 범위에 ", combo);
        StringAssert.EndsWith("의 추가 피해를 줍니다. 변신 중에도 적용됩니다.", combo);
    }

    [Test]
    public void DmgCalculators_Registered_NullPlayerIsDefault()
    {
        TraitContentRegistry registry = TraitContent.Build();
        foreach (string id in new[] { Trail, Combo })
        {
            Assert.IsTrue(registry.TryGetDamage(id, out TraitDamageCalculator calculator), $"{id} {{dmg}} 계산식");
            Assert.IsFalse(calculator(Load(id), null).HasValue, "player == null → default");
        }

        Assert.IsFalse(registry.TryGetDamage(Push, out _), "급소 파괴엔 {dmg} 가 없다");
    }

    [Test]
    public void DmgFormula_SourceDrefTimesRatio()
    {
        Assert.AreEqual(0.25f, AssassinDerivedTooltipRules.RatioOf(Load(Trail)), Eps);
        Assert.AreEqual(0.7f, AssassinDerivedTooltipRules.RatioOf(Load(Combo)), Eps);
        Assert.AreEqual(0f, AssassinDerivedTooltipRules.RatioOf(Load(Push)), Eps);

        // 추격의 궤적 = 출발 Q 1회(Dref 200, 계수 2) × 0.25
        SkillTooltipDamage trail = AssassinDerivedTooltipRules.From(200, 2f, 0, 0.25f);
        Assert.IsTrue(trail.HasValue);
        Assert.AreEqual(50, trail.Minimum);
        Assert.AreEqual(50, trail.Maximum);
        Assert.AreEqual(0.5f, trail.MinimumCoefficient, Eps, "Shift 계산식 계수 = 원래 계수 × 비율");

        // 끝없는 칼날 = 마지막 원래 타(Dref 101) × 0.7 = 70.7 → 71(마지막에 한 번 반올림)
        SkillTooltipDamage combo = AssassinDerivedTooltipRules.From(101, 1.01f, 0, 0.7f);
        Assert.AreEqual(71, combo.Minimum);
        Assert.IsFalse(combo.IsRange);
    }

    // ── 급소 파괴 ───────────────────────────────────────────────────────

    [Test]
    public void Push_EnhancedStrikeOnly_2mIn0_2s_NoExtraCC()
    {
        var host = new CaptureHost();
        ITraitEffectBinding binding = Bind(Push, host);
        Vector3 dir = Vector3.right;

        AttackInfo strike = host.Hit(EnhancedBasic, 1, 10, dir);
        Assert.AreEqual(2f / 0.2f, strike.knockbackStrength, Eps, "속도 = 2m ÷ 0.2초");
        Assert.AreEqual(0.2f, strike.knockbackDuration, Eps);
        Assert.AreEqual(2f, strike.knockbackStrength * strike.knockbackDuration, Eps, "거리 2m");
        Assert.AreEqual(dir, strike.knockbackDirection);
        Assert.AreEqual(0f, strike.concussionDuration, Eps, "뇌진탕 없음");
        Assert.AreEqual(100, strike.damage, "피해는 그대로");

        foreach (TraitAttackKey key in new[] { NormalBasic, TransformedBasic, NormalE, TransformedE, NormalQ, TransformedQ, Interrupt })
        {
            AttackInfo other = host.Hit(key, 2, 10, dir);
            Assert.AreEqual(0f, other.knockbackDuration, Eps, $"{key} 밀치기 없음(변신 E·E 준비·일반 평타 포함)");
            Assert.AreEqual(0f, other.staggerDuration, Eps, $"{key} 경직 없음");
        }

        binding.Unbind();
        Assert.IsEmpty(host.Modifiers, "되돌리면 수정자 제거");
    }

    [Test]
    public void Push_OncePerTargetPerStrike_AreaWidenedTargetsToo()
    {
        var host = new CaptureHost();
        ITraitEffectBinding binding = Bind(Push, host);

        Assert.Greater(host.Hit(EnhancedBasic, 5, 1, Vector3.forward).knockbackDuration, 0f, "대상 1");
        Assert.AreEqual(0f, host.Hit(EnhancedBasic, 5, 1, Vector3.forward).knockbackDuration, Eps, "같은 강타·같은 대상 = 한 번");
        // 칼날의 영역으로 넓어진 범위의 대상도 같은 경로(대상 ID 만 다름)
        Assert.Greater(host.Hit(EnhancedBasic, 5, 2, Vector3.back).knockbackDuration, 0f, "대상 2(확대 범위)");
        Assert.Greater(host.Hit(EnhancedBasic, 6, 1, Vector3.forward).knockbackDuration, 0f, "다음 강타는 다시");
        binding.Unbind();
    }

    [Test]
    public void Push_Direction_PlayerToTarget_SamePositionUsesAim()
    {
        Vector3 d = AssassinStrikePushRules.Direction(new Vector3(1f, 0f, 1f), new Vector3(4f, 3f, 5f), Vector3.left);
        Assert.AreEqual(0.6f, d.x, Eps);
        Assert.AreEqual(0f, d.y, Eps, "수평");
        Assert.AreEqual(0.8f, d.z, Eps);

        Vector3 same = AssassinStrikePushRules.Direction(new Vector3(2f, 0f, 2f), new Vector3(2f, 1.5f, 2f), new Vector3(0f, 0.3f, -2f));
        Assert.AreEqual(Vector3.back, same, "같은 위치 = 강타 조준 방향(수평·정규화)");

        Assert.AreEqual(Vector3.zero, AssassinStrikePushRules.Direction(Vector3.zero, Vector3.zero, Vector3.zero), "둘 다 없으면 0(수신측 방사형)");
    }

    // ── 추격의 궤적 ─────────────────────────────────────────────────────

    [Test]
    public void Trail_Spec_BothQForms_Ratio0_25_Delay0_15_DerivedFlags()
    {
        var host = new CaptureHost();
        TraitDerivedAttack spec = DerivedSpec(Trail, host, AssassinDerivedAttackKeys.QTrail);
        Assert.AreEqual(0.25f, spec.DamageRatio, Eps);
        Assert.AreEqual(0.15f, spec.Delay, Eps);
        Assert.IsTrue(spec.HitArea.IsEmpty, "판정 도형 없음 = 원래 Q 경로 폭 재사용(할 일 153)");

        Assert.AreEqual(1, host.DerivedFor(NormalQ).Count, "일반 Q");
        Assert.AreEqual(1, host.DerivedFor(TransformedQ).Count, "변신 Q");
        foreach (TraitAttackKey key in new[] { NormalBasic, EnhancedBasic, TransformedBasic, NormalE, TransformedE, Interrupt })
            Assert.IsEmpty(host.DerivedFor(key), $"{key} 엔 궤적 없음");

        Assert.IsTrue(AssassinDerivedAttackKeys.TryFind(host.DerivedFor(TransformedQ), AssassinDerivedAttackKeys.QTrail, out _));
        Assert.IsFalse(AssassinDerivedAttackKeys.TryFind(host.DerivedFor(TransformedQ), AssassinDerivedAttackKeys.ComboExtra, out _));
    }

    [Test]
    public void Trail_Damage_StageDrefTimes0_25_NotSumOfHits_ReturnUses0_6()
    {
        TraitDerivedAttack spec = DerivedSpec(Trail, new CaptureHost(), AssassinDerivedAttackKeys.QTrail);
        Assert.AreEqual(50, spec.DamageFrom(200), "출발 단계 Dref 200 × 0.25");
        // 그림자 회귀 복귀 단계 = 복귀 0.6 을 바른 기준 피해(120)를 넘긴다 → 30. 출발 + 복귀 궤적 = 원래 Q 의 (0.25 + 0.15).
        Assert.AreEqual(30, spec.DamageFrom(TraitHealRules.RoundHalfUp(200 * 0.6)));
        Assert.AreEqual(0, spec.DamageFrom(0));
        Assert.AreEqual(1, spec.DamageFrom(3), "0.75 → 1(마지막에 한 번 반올림)");
    }

    [Test]
    public void Trail_OnlyAfterNormalEnd_WithActualMovement()
    {
        Assert.IsTrue(AssassinTrailRules.Schedules(SkillEndReason.Completed, 4f), "정상 종료");
        Assert.IsTrue(AssassinTrailRules.Schedules(SkillEndReason.Completed, 1.3f), "벽 앞 정지도 정상 종료(실제 거리까지)");
        Assert.IsFalse(AssassinTrailRules.Schedules(SkillEndReason.Completed, 0f), "출발 전 종료 = 지나온 길 없음");
        Assert.IsFalse(AssassinTrailRules.Schedules(SkillEndReason.Completed, float.NaN));

        foreach (SkillEndReason forced in new[] { SkillEndReason.Cancelled, SkillEndReason.CasterDied, SkillEndReason.DashCancelled,
                     SkillEndReason.MaxDurationReached, SkillEndReason.Released })
            Assert.IsFalse(AssassinTrailRules.Schedules(forced, 4f), $"강제 중단({forced}) = 궤적 없음");
    }

    [Test]
    public void Trail_Segment_IsActualTraveledPath_WithPathWidth()
    {
        Vector3 start = new Vector3(1f, 0f, 2f);
        Vector3 end = AssassinTrailRules.SegmentEnd(start, new Vector3(0f, 0.5f, 3f), 1.5f);
        Assert.AreEqual(new Vector3(1f, 0f, 3.5f), end, "실제 이동 1.5m(벽 보고로 줄어든 상한) — 최대 거리 4m 아님");
        Assert.AreEqual(start, AssassinTrailRules.SegmentEnd(start, Vector3.forward, -2f), "음수 거리 = 0");

        AssassinTrailRules.Box(start, end, 1.2f, 2f, out Vector3 center, out Vector3 half, out Quaternion rotation);
        Assert.AreEqual(1f, center.x, Eps);
        Assert.AreEqual(1f, center.y, Eps, "높이 절반 올림");
        Assert.AreEqual(2.75f, center.z, Eps, "선분 가운데");
        Assert.AreEqual(0.6f, half.x, Eps, "원래 Q 경로 폭 1.2");
        Assert.AreEqual(1f, half.y, Eps);
        Assert.AreEqual(0.75f, half.z, Eps, "길이 절반 — 지나온 구간만");
        Assert.AreEqual(1f, Vector3.Dot(rotation * Vector3.forward, Vector3.forward), Eps, "선분 방향");
    }

    [Test]
    public void DerivedFlags_NoProcResourceCCOrCounter()
    {
        Assert.AreEqual(TraitHitFlags.None, TraitHitFlags.Derived & TraitHitFlags.CanProcTraits, "특성(재귀·다음 공격 강화·쿨감) 발동 없음");
        Assert.AreEqual(TraitHitFlags.None, TraitHitFlags.Derived & TraitHitFlags.CanGainResource, "분노 없음");
        Assert.AreEqual(TraitHitFlags.None, TraitHitFlags.Derived & TraitHitFlags.CanApplyCC, "CC 없음");
        Assert.AreEqual(TraitHitFlags.None, TraitHitFlags.Derived & TraitHitFlags.CanCounter, "간파 없음");

        // 파생은 원래 공격 키로만 찾는다 — 궤적·추가 베기가 다시 다른 파생을 부르지 않는다(파생 판정은 GetDerivedAttacks 를 부르지 않음).
        var host = new CaptureHost();
        Bind(Trail, host);
        Bind(Combo, host);
        Assert.AreEqual(1, host.DerivedFor(NormalQ).Count);
        Assert.AreEqual(1, host.DerivedFor(NormalBasic).Count);
    }

    // ── 끝없는 칼날 ─────────────────────────────────────────────────────

    [Test]
    public void Combo_Spec_NormalAndTransformedBasic_NotStrike_Ratio0_7_Delay0_08()
    {
        var host = new CaptureHost();
        TraitDerivedAttack spec = DerivedSpec(Combo, host, AssassinDerivedAttackKeys.ComboExtra);
        Assert.AreEqual(0.7f, spec.DamageRatio, Eps);
        Assert.AreEqual(0.08f, spec.Delay, Eps);
        Assert.IsTrue(spec.HitArea.IsEmpty, "판정 도형 없음 = 마지막 원래 한 타의 도형 재사용(할 일 153)");

        Assert.AreEqual(1, host.DerivedFor(NormalBasic).Count, "일반 연계");
        Assert.AreEqual(1, host.DerivedFor(TransformedBasic).Count, "변신 묶음");
        Assert.IsEmpty(host.DerivedFor(EnhancedBasic), "강타는 4타가 아니다");
        foreach (TraitAttackKey key in new[] { NormalQ, TransformedQ, NormalE, TransformedE, Interrupt })
            Assert.IsEmpty(host.DerivedFor(key), key.ToString());
    }

    [Test]
    public void Combo_SchedulesOnlyOnNormalFourthHit_OrTransformedLastJudgment()
    {
        const int steps = 4;
        for (int step = 0; step < steps; step++)
            Assert.AreEqual(step == steps - 1, AssassinComboExtraRules.Schedules(AssassinBasicAttackMode.Normal, step, steps, 1, 1), $"일반 {step + 1}타");

        for (int hit = 1; hit <= 4; hit++)
            Assert.AreEqual(hit == 4, AssassinComboExtraRules.Schedules(AssassinBasicAttackMode.Transformed, 0, steps, hit, 4), $"변신 묶음 {hit}번째 판정");

        Assert.IsFalse(AssassinComboExtraRules.Schedules(AssassinBasicAttackMode.Enhanced, 3, steps, 1, 1), "강타는 4타로 세지 않는다");
        Assert.IsFalse(AssassinComboExtraRules.Schedules(AssassinBasicAttackMode.Normal, 3, steps, 0, 1), "판정 전");
    }

    [Test]
    public void Combo_InterruptedChain_RestartsAtFirst_NoExtra_StrikeResets()
    {
        var combo = new AssassinComboModel(4, 0.8f);
        float t = 0f;

        // 1~3타 뒤 대시·피격 취소(Reset) → 다음은 1타, 마지막 타가 아니라 예약 없음
        for (int i = 0; i < 3; i++)
            combo.Complete(combo.Begin(t += 0.3f), t += 0.3f);
        combo.Reset();
        Assert.AreEqual(0, combo.Begin(t += 0.1f));
        Assert.IsFalse(AssassinComboExtraRules.Schedules(AssassinBasicAttackMode.Normal, combo.NextStep, 4, 1, 1));

        // 3타 뒤 멈춤(유예 0.8초 초과) → 1타부터
        combo.Reset();
        for (int i = 0; i < 3; i++)
            combo.Complete(combo.Begin(t += 0.3f), t += 0.3f);
        Assert.AreEqual(0, combo.Begin(t += 1.0f), "도중 멈춤");

        // 이어진 1~4타 → 4타에서 예약
        combo.Reset();
        int fourth = -1;
        for (int i = 0; i < 4; i++)
        {
            fourth = combo.Begin(t += 0.3f);
            if (i < 3)
                combo.Complete(fourth, t += 0.3f);
        }
        Assert.IsTrue(AssassinComboExtraRules.Schedules(AssassinBasicAttackMode.Normal, fourth, 4, 1, 1), "1~4타 연계");

        // 강타 시작 = 콤보 초기화(AssassinBasicAttack.StartServer) — 강타 뒤 일반은 1타부터
        combo.Complete(fourth, t += 0.3f);
        combo.Complete(combo.Begin(t += 0.3f), t += 0.3f);
        combo.Reset();
        Assert.AreEqual(0, combo.Begin(t += 0.1f), "강타 뒤 1타부터");
    }

    [Test]
    public void Combo_Damage_LastHitDrefTimes0_7_NotSum_NoNextAttackBuff()
    {
        TraitDerivedAttack spec = DerivedSpec(Combo, new CaptureHost(), AssassinDerivedAttackKeys.ComboExtra);
        // 마지막 원래 한 타 Dref 100 → 70. 4타 합(예 400)이 아니다. 다음 공격 강화(×1.3)를 받은 타여도 Dref(강화 전)로 계산 — 복제 안 함.
        Assert.AreEqual(70, spec.DamageFrom(100));
        Assert.AreNotEqual(spec.DamageFrom(400), spec.DamageFrom(100));
        Assert.AreEqual(70, spec.DamageFrom(100), "강화 전 Dref");
    }

    [Test]
    public void Combo_Delay_ScaledByAttackAccelerationOnly()
    {
        Assert.AreEqual(0.08f, AssassinComboExtraRules.Delay(0.08f, 1f), Eps);
        Assert.AreEqual(0.04f, AssassinComboExtraRules.Delay(0.08f, 2f), Eps, "제압 가속(공격속도 ×2) = 지연 절반");
        Assert.AreEqual(0.16f, AssassinComboExtraRules.Delay(0.08f, 0.5f), Eps, "감속");
        Assert.AreEqual(0f, AssassinComboExtraRules.Delay(-1f, 1f), Eps);
    }

    [Test]
    public void Trail_Delay_ScaledByAttackAcceleration_LikeCombo()
    {
        // 명세 §6 — 공격 지연·연사 간격·회수 시간은 같은 공격 가속기. 추격의 궤적 0.15초도 끝없는 칼날처럼 ÷ 공격속도.
        Assert.AreEqual(0.15f, AssassinTrailRules.Delay(0.15f, 1f), Eps);
        Assert.AreEqual(0.075f, AssassinTrailRules.Delay(0.15f, 2f), Eps, "제압 가속(공격속도 ×2) = 지연 절반");
        Assert.AreEqual(0.3f, AssassinTrailRules.Delay(0.15f, 0.5f), Eps, "감속");
        Assert.AreEqual(0f, AssassinTrailRules.Delay(-1f, 1f), Eps);
        foreach (float rate in new[] { 0.5f, 1f, 1.5f, 2f })
            Assert.AreEqual(AssassinComboExtraRules.Delay(0.15f, rate), AssassinTrailRules.Delay(0.15f, rate), Eps, $"같은 가속기 ×{rate}");
    }

    // ── 예약 줄(사망·씬 취소·재사용) ────────────────────────────────────

    [Test]
    public void Queue_FiresOnceAtDueTime_InOrder()
    {
        var queue = new AssassinDelayedStrikeQueue<int>();
        queue.Schedule(10.0, 0.15f, 1);
        queue.Schedule(10.0, 0.08f, 2);
        queue.Schedule(10.0, 0.08f, 3);

        Assert.IsFalse(queue.TryPopDue(10.07, out _), "지연 전");
        Assert.IsTrue(queue.TryPopDue(10.08, out int a));
        Assert.IsTrue(queue.TryPopDue(10.08, out int b));
        Assert.IsFalse(queue.TryPopDue(10.1, out _));
        Assert.IsTrue(queue.TryPopDue(10.2, out int c));
        CollectionAssert.AreEqual(new[] { 2, 3, 1 }, new[] { a, b, c }, "시각 순서, 같은 시각은 넣은 순서");
        Assert.IsFalse(queue.TryPopDue(99.0, out _), "한 번만");
    }

    [Test]
    public void Queue_DeathOrSceneEnd_Clears_NoResumeAfterRevive()
    {
        var queue = new AssassinDelayedStrikeQueue<int>();
        queue.Schedule(0.0, 0.08f, 7);
        queue.Clear(); // 사망(살아 있음 → 그 밖)·비활성(씬 종료)
        Assert.AreEqual(0, queue.Count);
        Assert.IsFalse(queue.TryPopDue(100.0, out _), "부활 뒤 재개 없음");

        // 재사용 — 새 예약만 남는다
        queue.Schedule(1.0, 0f, 8);
        Assert.IsTrue(queue.TryPopDue(1.0, out int next));
        Assert.AreEqual(8, next);
    }
}
