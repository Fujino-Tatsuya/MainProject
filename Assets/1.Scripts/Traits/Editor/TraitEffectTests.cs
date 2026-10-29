using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 125 (3/5) — 재사용 효과 클래스 뼈대의 순수 로직: 각 효과 수치(명세 §10 검산)·이벤트 거름(시전당 1회·간격)·
/// 되돌리기 장부(재선택·재합류에서 1회 동작 중복 없음·빠진 특성 되돌리기)·HealHp 실제 회복량·밀치기가 AttackInfo 필드만 채우는지.
/// </summary>
public sealed class TraitEffectTests
{
    const float Eps = 1e-4f;

    // ── 가짜 허브(ITraitEffectHost) ────────────────────────────────────

    sealed class FakeHost : ITraitEffectHost
    {
        public double Time;
        public readonly Dictionary<PlayerSkillSlot, float> Remaining = new Dictionary<PlayerSkillSlot, float>();
        public int MaxHp = 1000;
        public readonly List<(Unit target, int hash, int amount, float duration)> Shields = new List<(Unit, int, int, float)>();
        public readonly List<ITraitAttackModifier> AttackModifiers = new List<ITraitAttackModifier>();
        public readonly List<TraitDerivedAttack> Derived = new List<TraitDerivedAttack>();
        public readonly List<TraitOverflowShare> Shares = new List<TraitOverflowShare>();

        public double Now => Time;
        public event Action<TraitTriggerEvent> Triggered;
        public int SubscriberCount => Triggered?.GetInvocationList().Length ?? 0;

        public void Raise(TraitTriggerEvent e) => Triggered?.Invoke(e);

        public float GetCooldownRemaining(PlayerSkillSlot slot) => Remaining.TryGetValue(slot, out float r) ? r : 0f;

        public void ReduceCooldown(PlayerSkillSlot slot, float seconds) =>
            Remaining[slot] = TraitCooldownRefundRules.Reduce(GetCooldownRemaining(slot), seconds);

        public int MaxHpOf(Unit target) => MaxHp;

        public void GrantTraitShield(Unit target, int traitHash, int amount, float duration) =>
            Shields.Add((target, traitHash, amount, duration));

        public TraitNextAttackBuffs NextAttackBuffs { get; } = new TraitNextAttackBuffs();
        public void AddAttackModifier(ITraitAttackModifier modifier) => AttackModifiers.Add(modifier);
        public void RemoveAttackModifier(ITraitAttackModifier modifier) => AttackModifiers.Remove(modifier);
        public void AddDerivedAttack(TraitDerivedAttack attack) => Derived.Add(attack);
        public void RemoveDerivedAttack(TraitDerivedAttack attack) => Derived.Remove(attack);
        public void AddOverflowShare(TraitOverflowShare share) => Shares.Add(share);
        public void RemoveOverflowShare(TraitOverflowShare share) => Shares.Remove(share);
    }

    // '획득 1회' 동작을 세는 테스트 효과(입장 쿨감·즉시 회복 같은 1회 동작의 대역)
    [Serializable]
    sealed class CountingOnceEffect : TraitEffect
    {
        public int Runs;
        public override bool HasOnceAction => true;
        public override void RunOnceServer(ITraitEffectHost host, int traitHash) => Runs++;
    }

    sealed class Contributor : ITraitModifierContributor
    {
        readonly TraitEffect[] effects;
        public Contributor(params TraitEffect[] effects) => this.effects = effects;
        public void GetModifiers(List<TraitModifier> result)
        {
            foreach (TraitEffect e in effects) e.GetModifiers(result);
        }
    }

    static TraitModifierSet SetOf(params TraitEffect[][] traits)
    {
        var table = new Dictionary<int, ITraitModifierContributor>();
        var hashes = new int[traits.Length];
        for (int i = 0; i < traits.Length; i++)
        {
            hashes[i] = i + 1;
            table[i + 1] = new Contributor(traits[i]);
        }

        var set = new TraitModifierSet();
        set.Rebuild(hashes, h => table.TryGetValue(h, out ITraitModifierContributor c) ? c : null);
        Assert.IsEmpty(set.Errors);
        return set;
    }

    static TraitTriggerEvent Landed(AttackType type, int castId, TraitAttackForm form = TraitAttackForm.Normal) =>
        new TraitTriggerEvent(TraitTrigger.AttackLanded, type, form, castId, true, 1);

    static TraitTriggerEvent Guard(int castId) =>
        new TraitTriggerEvent(TraitTrigger.GuardSucceeded, AttackType.SkillE, TraitAttackForm.Normal, castId);

    // ── ① 피해 배율 ─────────────────────────────────────────────────────

    [Test]
    public void Damage_GunnerBasicUp_TimesCoolMode_Is102Percent()
    {
        // 명세 §10: 출력 강화 1.20 × 정온 운용 0.85 = 1.02
        TraitModifierSet set = SetOf(
            new TraitEffect[] { new TraitDamageEffect(0.2f, new[] { AttackType.Default }) },
            new TraitEffect[] { new TraitDamageEffect(-0.15f, new[] { AttackType.Default }) });

        Assert.AreEqual(1.02f, set.DamageMultiplier(new TraitAttackKey(AttackType.Default)), Eps);
        Assert.AreEqual(1f, set.DamageMultiplier(new TraitAttackKey(AttackType.SkillQ)), "다른 공격 계열엔 안 붙는다");
    }

    [Test]
    public void Damage_FormFilter_EnhancedOnly()
    {
        TraitModifierSet set = SetOf(new TraitEffect[]
        {
            new TraitDamageEffect(0.2f, new[] { AttackType.Default }, new[] { TraitAttackForm.Enhanced }),
        });

        Assert.AreEqual(1.2f, set.DamageMultiplier(new TraitAttackKey(AttackType.Default, TraitAttackForm.Enhanced)), Eps);
        Assert.AreEqual(1f, set.DamageMultiplier(new TraitAttackKey(AttackType.Default, TraitAttackForm.Normal)));
    }

    [Test]
    public void Damage_NegativeMultiplier_IsValidationError()
    {
        var errors = new List<string>();
        new TraitDamageEffect(-1.5f).Validate(errors);
        Assert.IsNotEmpty(errors);
    }

    // ── ② 쿨 배율 ──────────────────────────────────────────────────────

    [Test]
    public void Cooldown_AssassinQ_Multiplies_Spec()
    {
        // 명세 §10: Q 5초 × 0.85 × 0.75 = 3.1875 / × 0.85 × 1.20 = 5.1
        TraitEffect[] haste = { new TraitCooldownEffect(-0.15f, new[] { PlayerSkillSlot.Main }, new[] { TraitAttackForm.Normal }) };
        TraitModifierSet restraint = SetOf(haste, new TraitEffect[] { new TraitCooldownEffect(-0.25f, new[] { PlayerSkillSlot.Main }) });
        TraitModifierSet ret = SetOf(haste, new TraitEffect[] { new TraitCooldownEffect(0.2f, new[] { PlayerSkillSlot.Main }) });

        Assert.AreEqual(3.1875f, 5f * restraint.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal), Eps);
        Assert.AreEqual(5.1f, 5f * ret.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal), Eps);
        Assert.AreEqual(1.2f, ret.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Alternate), Eps, "돌파 가속은 일반 Q 만");
        Assert.IsTrue(haste[0].AffectsCooldownOf(PlayerSkillSlot.Main));
        Assert.IsFalse(haste[0].AffectsCooldownOf(PlayerSkillSlot.Sub));
    }

    [Test]
    public void Cooldown_ReductionOfOneOrMore_IsValidationError()
    {
        var errors = new List<string>();
        new TraitCooldownEffect(-1f).Validate(errors);
        Assert.IsNotEmpty(errors);
    }

    // ── 이벤트 거름: 시전당 1회·간격 ─────────────────────────────────────

    [Test]
    public void ProcGate_OncePerCast_SecondHitOfSameCastRejected()
    {
        var gate = new TraitProcGate(oncePerCast: true, minInterval: 0f);
        Assert.IsTrue(gate.TryPass(7, 0));
        Assert.IsFalse(gate.TryPass(7, 0.1), "같은 시전의 다른 틱·대상");
        Assert.IsTrue(gate.TryPass(8, 0.1), "다음 시전");
        Assert.IsFalse(gate.TryPass(7, 5), "늦게 온 지난 시전 판정도 거부");
    }

    [Test]
    public void ProcGate_CastIdZero_IsNeverGrouped()
    {
        var gate = new TraitProcGate(true, 0f);
        Assert.IsTrue(gate.TryPass(0, 0));
        Assert.IsTrue(gate.TryPass(0, 0), "0 = 모름 — 매번 새 시전");
    }

    [Test]
    public void ProcGate_PerActionPlusInterval_HuntingBeat()
    {
        // 사냥의 박자: 동작당 1회 + 0.8초
        var gate = new TraitProcGate(true, 0.8f);
        Assert.IsTrue(gate.TryPass(1, 0.0));
        Assert.IsFalse(gate.TryPass(1, 0.1), "같은 동작");
        Assert.IsFalse(gate.TryPass(2, 0.5), "다른 동작이지만 간격 안");
        Assert.IsTrue(gate.TryPass(3, 0.8), "간격 끝");
        Assert.IsFalse(gate.TryPass(3, 2.0), "간격이 지나도 같은 동작은 다시 안 된다");
    }

    [Test]
    public void ProcGate_RejectedCast_IsNotRemembered()
    {
        var gate = new TraitProcGate(true, 0.8f);
        Assert.IsTrue(gate.TryPass(1, 0));
        Assert.IsFalse(gate.TryPass(2, 0.5), "간격 안 거부");
        Assert.IsTrue(gate.TryPass(2, 0.9), "거부된 시전은 기록하지 않았다 — 간격 뒤 같은 시전의 다음 적중은 통과");
    }

    [Test]
    public void ProcGate_Reset_ForgetsCastsAndInterval()
    {
        var gate = new TraitProcGate(true, 10f);
        gate.TryPass(1, 0);
        gate.Reset();
        Assert.IsTrue(gate.TryPass(1, 0.1));
    }

    [Test]
    public void CastTargetGate_OncePerTargetPerCast()
    {
        var gate = new TraitCastTargetGate();
        Assert.IsTrue(gate.TryMark(1, 100));
        Assert.IsFalse(gate.TryMark(1, 100), "같은 시전·같은 대상(여러 콜라이더·여러 틱)");
        Assert.IsTrue(gate.TryMark(1, 200), "같은 시전·다른 대상");
        Assert.IsTrue(gate.TryMark(2, 100), "다른 시전");
        Assert.IsTrue(gate.TryMark(0, 100) && gate.TryMark(0, 100), "CastId 0 은 거르지 않는다");
    }

    [Test]
    public void CastTargetGate_ForgetsOldCasts()
    {
        var gate = new TraitCastTargetGate();
        gate.TryMark(1, 5);
        for (int cast = 2; cast <= TraitProcGate.RememberedCasts + 1; cast++)
            gate.TryMark(cast, 5);
        Assert.IsTrue(gate.TryMark(1, 5), "기억 범위를 넘은 오래된 시전은 잊는다(메모리 상한)");
    }

    // ── ③ 사건 시 남은 쿨 차감 ──────────────────────────────────────────

    [Test]
    public void Refund_GuardSuccess_OncePerCast_NotBelowZero()
    {
        var host = new FakeHost();
        host.Remaining[PlayerSkillSlot.Sub] = 4f;
        var effect = new TraitCooldownRefundEffect(new TraitTriggerFilter(TraitTrigger.GuardSucceeded), 2f, new[] { PlayerSkillSlot.Sub });
        ITraitEffectBinding binding = effect.BindServer(host, 11);

        host.Raise(Guard(1));
        Assert.AreEqual(2f, host.Remaining[PlayerSkillSlot.Sub], Eps);
        host.Raise(Guard(1));
        Assert.AreEqual(2f, host.Remaining[PlayerSkillSlot.Sub], Eps, "같은 방어 시전 = 1회");

        host.Remaining[PlayerSkillSlot.Sub] = 1f;
        host.Raise(Guard(2));
        Assert.AreEqual(0f, host.Remaining[PlayerSkillSlot.Sub], "0 아래로 내려가지 않는다");

        host.Raise(Landed(AttackType.SkillE, 3));
        binding.Unbind();
        Assert.AreEqual(0, host.SubscriberCount, "되돌리면 구독 해제");
    }

    [Test]
    public void Refund_CombinedWithSuppression_Spec()
    {
        // 명세 §10: 전체 5.95, 남은 4 → 방어 성공 −2 → 제압 −2.975 → 0
        var host = new FakeHost();
        host.Remaining[PlayerSkillSlot.Sub] = 4f;
        new TraitCooldownRefundEffect(new TraitTriggerFilter(TraitTrigger.GuardSucceeded), 2f, new[] { PlayerSkillSlot.Sub })
            .BindServer(host, 1);
        host.Raise(Guard(1));
        Assert.AreEqual(0f, TraitCooldownRefundRules.Reduce(host.Remaining[PlayerSkillSlot.Sub], SuppressHasteRules.EntryCooldownReduction(5.95f)));
    }

    [Test]
    public void Refund_LongestRemaining_TieGoesToFirst_BothReadyNothing()
    {
        var picked = new List<PlayerSkillSlot>();
        PlayerSkillSlot[] qe = { PlayerSkillSlot.Main, PlayerSkillSlot.Sub };

        TraitCooldownRefundRules.PickSlots(TraitRefundTarget.LongestRemaining, qe, s => s == PlayerSkillSlot.Main ? 3f : 5f, picked);
        CollectionAssert.AreEqual(new[] { PlayerSkillSlot.Sub }, picked);

        TraitCooldownRefundRules.PickSlots(TraitRefundTarget.LongestRemaining, qe, s => 4f, picked);
        CollectionAssert.AreEqual(new[] { PlayerSkillSlot.Main }, picked, "동률 = Q 우선");

        TraitCooldownRefundRules.PickSlots(TraitRefundTarget.LongestRemaining, qe, s => 0f, picked);
        Assert.IsEmpty(picked, "둘 다 준비 = 저장 안 함");

        TraitCooldownRefundRules.PickSlots(TraitRefundTarget.EachSlot, qe, s => s == PlayerSkillSlot.Main ? 0f : 1f, picked);
        CollectionAssert.AreEqual(new[] { PlayerSkillSlot.Sub }, picked, "각각 — 준비된 슬롯은 빠진다");
    }

    [Test]
    public void Refund_HuntingBeat_EachSlot_PerActionPlusInterval()
    {
        var host = new FakeHost();
        host.Remaining[PlayerSkillSlot.Main] = 3f;
        host.Remaining[PlayerSkillSlot.Sub] = 3f;
        new TraitCooldownRefundEffect(
            new TraitTriggerFilter(TraitTrigger.AttackLanded, new[] { AttackType.Default }, oncePerCast: true, minInterval: 0.8f),
            0.2f, new[] { PlayerSkillSlot.Main, PlayerSkillSlot.Sub }).BindServer(host, 1);

        host.Time = 0; host.Raise(Landed(AttackType.Default, 1));
        host.Time = 0.1; host.Raise(Landed(AttackType.Default, 1));
        host.Time = 0.5; host.Raise(Landed(AttackType.Default, 2));
        host.Time = 0.5; host.Raise(Landed(AttackType.SkillQ, 9));
        Assert.AreEqual(2.8f, host.Remaining[PlayerSkillSlot.Main], Eps);
        Assert.AreEqual(2.8f, host.Remaining[PlayerSkillSlot.Sub], Eps);

        host.Time = 0.85; host.Raise(Landed(AttackType.Default, 3));
        Assert.AreEqual(2.6f, host.Remaining[PlayerSkillSlot.Main], Eps);
    }

    // ── ④ 회복·초과 회복 + HealHp 반환 ─────────────────────────────────

    [Test]
    public void HealthHealHp_ReturnsActualHeal()
    {
        var health = new Health(1000, 0);
        Assert.AreEqual(0, health.HealHp(50), "가득 차 있으면 0");

        health.TakeHpDamage(20);
        Assert.AreEqual(20, health.HealHp(63), "결손 20 — 요청 63 중 실제 20");
        Assert.AreEqual(1000, health.CurrentHealth);

        health.TakeHpDamage(100);
        Assert.AreEqual(30, health.HealHp(30));
        Assert.AreEqual(0, health.HealHp(-10), "음수 회복은 실제 회복 0");
    }

    [Test]
    public void Heal_Spec_RequestOverflowShieldChain()
    {
        // 명세 §10: 원래 회복 50 × 1.25 = 63(62.5 반올림), 자신 결손 20 → 실제 20, 초과 43 → 아군 실제 43, 보호막 15·32
        int requested = TraitHealRules.Request(50, new TraitHealEffect(0.25f).Multiplier);
        Assert.AreEqual(63, requested);

        var self = new Health(1000, 0);
        self.TakeHpDamage(20);
        int actual = self.HealHp(requested);
        int overflow = TraitHealRules.Overflow(requested, actual);
        Assert.AreEqual(20, actual);
        Assert.AreEqual(43, overflow);

        var ally = new Health(1000, 0);
        ally.TakeHpDamage(300);
        int allyActual = ally.HealHp(TraitHealRules.Transfer(overflow, 1f));
        Assert.AreEqual(43, allyActual);

        Assert.AreEqual(15, TraitShieldRules.Amount(TraitShieldAmountSource.EventAmountRatio, 0.75f, 1000, actual, 0.1f));
        Assert.AreEqual(32, TraitShieldRules.Amount(TraitShieldAmountSource.EventAmountRatio, 0.75f, 1000, allyActual, 0.1f));
    }

    [Test]
    public void Heal_ModifierAndShareBinding()
    {
        TraitModifierSet set = SetOf(new TraitEffect[] { new TraitHealEffect(0.25f) });
        Assert.AreEqual(1.25f, set.HealMultiplier, Eps);

        var host = new FakeHost();
        Assert.IsNull(new TraitHealEffect(0.25f).BindServer(host, 1), "전달 없으면 걸 것이 없다");
        ITraitEffectBinding binding = new TraitHealEffect(0f, 12f).BindServer(host, 2);
        Assert.AreEqual(1, host.Shares.Count);
        Assert.AreEqual(12f, host.Shares[0].Range);
        binding.Unbind();
        Assert.AreEqual(0, host.Shares.Count);
        Assert.AreEqual(1f, SetOf(new TraitEffect[] { new TraitHealEffect(0f, 12f) }).HealMultiplier, "전달만 = 배율 없음");
    }

    [Test]
    public void Heal_OverflowRecipient_LowestRatio_Ties_Range()
    {
        var candidates = new List<TraitHealCandidate>
        {
            new TraitHealCandidate(5, 500, 1000, 4f),      // 50%
            new TraitHealCandidate(3, 250, 500, 4f),       // 50%, 잃은 250 < 500
            new TraitHealCandidate(9, 100, 1000, 400f),    // 10% 지만 범위 밖(20m)
            new TraitHealCandidate(2, 1000, 1000, 1f),     // 가득 — 제외
            new TraitHealCandidate(1, 50, 1000, 1f, false), // 자격 없음(쓰러짐 등)
        };
        Assert.AreEqual(0, TraitHealRules.PickOverflowRecipient(candidates, 12f), "비율 동률 → 잃은 체력 큰 쪽");

        candidates.Add(new TraitHealCandidate(4, 500, 1000, 4f)); // 비율·잃은 체력 같음 → Id 작은 쪽
        Assert.AreEqual(5, TraitHealRules.PickOverflowRecipient(candidates, 12f));

        Assert.AreEqual(-1, TraitHealRules.PickOverflowRecipient(new List<TraitHealCandidate> { new TraitHealCandidate(2, 1000, 1000, 1f) }, 12f),
            "유효 대상 없음 = 버림");
    }

    // ── ⑤ 특성 채널 보호막 ──────────────────────────────────────────────

    [Test]
    public void Shield_GuardSuccess_SelfMaxHpRatio_OncePerCast()
    {
        var host = new FakeHost { MaxHp = 1000 };
        new TraitShieldEffect(new TraitTriggerFilter(TraitTrigger.GuardSucceeded), TraitShieldAmountSource.MaxHpRatio, 0.1f, 4f)
            .BindServer(host, 77);

        host.Raise(Guard(1));
        host.Raise(Guard(1));
        Assert.AreEqual(1, host.Shields.Count, "같은 방어 시전 = 1회");
        Assert.AreEqual((null as Unit, 77, 100, 4f), host.Shields[0]);
    }

    [Test]
    public void Shield_HealedSubject_ZeroHealOrNoSubject_NoShield()
    {
        var host = new FakeHost();
        new TraitShieldEffect(new TraitTriggerFilter(TraitTrigger.Healed, oncePerCast: false), TraitShieldAmountSource.EventAmountRatio,
            0.75f, 4f, TraitShieldRecipient.EventSubject, 0.1f).BindServer(host, 1);

        host.Raise(new TraitTriggerEvent(TraitTrigger.Healed, AttackType.None, TraitAttackForm.Normal, 0, false, 0, null, 20));
        Assert.IsEmpty(host.Shields, "받을 대상이 없으면 없음");
        Assert.AreEqual(0, TraitShieldRules.Amount(TraitShieldAmountSource.EventAmountRatio, 0.75f, 1000, 0, 0.1f), "실제 회복 0 = 보호막 없음");
    }

    [Test]
    public void Shield_ChannelRules_KeepLarger_CapAndSourceIsTraitHash()
    {
        Assert.AreEqual(40, TraitShieldRules.Merge(40, 15), "큰 잔량 유지(합산 아님)");
        Assert.AreEqual(32, TraitShieldRules.Merge(10, 32));
        Assert.AreEqual(100, TraitShieldRules.Amount(TraitShieldAmountSource.EventAmountRatio, 0.75f, 1000, 400, 0.1f), "최대 체력 10% 상한");
        Assert.AreEqual(TraitShieldRules.ChannelSource(-5), TraitShieldRules.ChannelSource(-5), "시전자 무관 — 같은 특성 = 같은 출처");
        Assert.AreNotEqual(TraitShieldRules.ChannelSource(1), TraitShieldRules.ChannelSource(2));
    }

    [Test]
    public void Shield_ReplaceOnSameTraitChannel_KeepsOtherChannels()
    {
        // Health 단계: 특성 채널은 Replace 규칙 — 디렉터 허브가 Merge 값으로 갈아 끼운다. 다른 채널(Q 보호막)은 그대로.
        var health = new Health(1000, 0);
        ulong channel = TraitShieldRules.ChannelSource(123);
        health.AddShield(ShieldType.GunnerCharge, 9, 30, 5f, 0);
        health.AddShield(ShieldType.Trait, channel, 40, 4f, 0);
        health.AddShield(ShieldType.Trait, channel, TraitShieldRules.Merge(40, 15), 4f, 1);
        Assert.AreEqual(70, health.CurrentShield);
        Assert.AreEqual(2, health.Shields.Count);
    }

    // ── ⑥ 밀치기 ───────────────────────────────────────────────────────

    [Test]
    public void Knockback_FillsOnlyKnockbackFields()
    {
        var effect = new TraitKnockbackEffect(2f, 0.2f, 0.75f);
        var original = new AttackInfo(123, AttackType.SkillQ, isInterruptAttack: true, hitPattern: AttackHitPattern.Multi,
            backAttackMultiplier: 1.5f, forceBackAttack: true);
        AttackInfo info = original;

        effect.ApplyCrowdControl(ref info, new Vector3(0f, 5f, 3f));

        Assert.AreEqual(10f, info.knockbackStrength, Eps, "속도 = 거리 ÷ 시간");
        Assert.AreEqual(0.2f, info.knockbackDuration, Eps);
        Assert.AreEqual(0.75f, info.staggerDuration, Eps, "경직 > 0 — 23호 취약 밀림 조건");
        Assert.AreEqual(Vector3.forward, info.knockbackDirection, "수평 정규화");

        Assert.AreEqual(original.damage, info.damage);
        Assert.AreEqual(original.attackType, info.attackType);
        Assert.AreEqual(original.hitPattern, info.hitPattern);
        Assert.AreEqual(original.isInterruptAttack, info.isInterruptAttack);
        Assert.AreEqual(original.backAttackMultiplier, info.backAttackMultiplier);
        Assert.AreEqual(original.forceBackAttack, info.forceBackAttack);
    }

    [Test]
    public void Knockback_StrongerExisting_Kept_StaggerMax_NoSum()
    {
        var effect = new TraitKnockbackEffect(2f, 0.2f, 0.1f);
        var info = new AttackInfo(10, AttackType.SkillQ, knockbackStrength: 20f, knockbackDuration: 0.3f, staggerDuration: 0.5f,
            knockbackDirection: Vector3.right);

        effect.ApplyCrowdControl(ref info, Vector3.forward);

        Assert.AreEqual(20f, info.knockbackStrength, "기존(6m)이 더 세다 — 벡터 합산·덮어쓰기 없음");
        Assert.AreEqual(Vector3.right, info.knockbackDirection);
        Assert.AreEqual(0.5f, info.staggerDuration, Eps, "경직은 큰 값(합산 아님)");
    }

    [Test]
    public void Knockback_Modifier_KeyFilter_OncePerTargetPerCast_Unbind()
    {
        var host = new FakeHost();
        ITraitEffectBinding binding = new TraitKnockbackEffect(2f, 0.2f, 0.1f, new[] { AttackType.SkillQ }).BindServer(host, 1);
        Assert.AreEqual(1, host.AttackModifiers.Count);
        ITraitAttackModifier modifier = host.AttackModifiers[0];
        var q = new TraitAttackKey(AttackType.SkillQ);

        AttackInfo basic = new AttackInfo(10, AttackType.Default);
        modifier.Modify(new TraitAttackKey(AttackType.Default), 1, 100, Vector3.forward, ref basic);
        Assert.AreEqual(0f, basic.knockbackStrength, "다른 공격 계열은 안 민다");

        AttackInfo first = new AttackInfo(10, AttackType.SkillQ);
        modifier.Modify(q, 1, 100, Vector3.forward, ref first);
        AttackInfo secondTick = new AttackInfo(10, AttackType.SkillQ);
        modifier.Modify(q, 1, 100, Vector3.forward, ref secondTick);
        AttackInfo otherTarget = new AttackInfo(10, AttackType.SkillQ);
        modifier.Modify(q, 1, 200, Vector3.forward, ref otherTarget);

        Assert.Greater(first.knockbackStrength, 0f);
        Assert.AreEqual(0f, secondTick.knockbackStrength, "같은 시전·같은 대상 = 1회");
        Assert.Greater(otherTarget.knockbackStrength, 0f, "다른 대상은 따로");

        binding.Unbind();
        Assert.IsEmpty(host.AttackModifiers);
    }

    [Test]
    public void Knockback_RejectedBySuperArmor_NotRecorded_LaterHitSameCastRetries()
    {
        // 할 일 133 — 보스가 슈퍼아머로 밀치기를 거부하면 '시전당 대상별 1회' 기록을 남기지 않는다.
        var host = new FakeHost();
        new TraitKnockbackEffect(2f, 0.2f, 0f, new[] { AttackType.SkillQ }).BindServer(host, 1);
        var q = new TraitAttackKey(AttackType.SkillQ);

        AttackInfo rejected = new AttackInfo(10, AttackType.SkillQ);
        TraitAttackModifiers.Apply(host.AttackModifiers, q, 1, 100, false, Vector3.forward, ref rejected);
        Assert.AreEqual(0f, rejected.knockbackStrength, "거부 중 = 싣지 않는다");

        AttackInfo retry = new AttackInfo(10, AttackType.SkillQ);
        TraitAttackModifiers.Apply(host.AttackModifiers, q, 1, 100, true, Vector3.forward, ref retry);
        Assert.Greater(retry.knockbackStrength, 0f, "같은 시전의 뒤 발 = 다시 시도해 적용");

        AttackInfo again = new AttackInfo(10, AttackType.SkillQ);
        TraitAttackModifiers.Apply(host.AttackModifiers, q, 1, 100, true, Vector3.forward, ref again);
        Assert.AreEqual(0f, again.knockbackStrength, "적용된 뒤엔 같은 시전·같은 대상 1회");
    }

    [Test]
    public void Knockback_ZeroStagger_IsValid()
    {
        // 할 일 134 — 23호도 넉백 세기만으로 취약 밀림이 걸린다. 경직 0 = 밀기만
        var errors = new List<string>();
        new TraitKnockbackEffect(2f, 0.2f, 0f).Validate(errors);
        Assert.IsEmpty(errors, string.Join("\n", errors));

        var info = new AttackInfo(10, AttackType.SkillQ);
        new TraitKnockbackEffect(2f, 0.2f, 0f).ApplyCrowdControl(ref info, Vector3.forward);
        Assert.Greater(info.knockbackStrength, 0f, "밀기는 실린다");
        Assert.AreEqual(0f, info.staggerDuration, "경직 없음");
    }

    [Test]
    public void Knockback_DefaultStagger_IsZero()
    {
        var info = new AttackInfo(10, AttackType.SkillQ);
        new TraitKnockbackEffect().ApplyCrowdControl(ref info, Vector3.forward);
        Assert.AreEqual(0f, info.staggerDuration, "인스펙터 기본 경직 = 0");
        var errors = new List<string>();
        new TraitKnockbackEffect().Validate(errors);
        Assert.IsEmpty(errors, string.Join("\n", errors));
    }

    // ── ⑦ 범위·폭 ───────────────────────────────────────────────────────

    [Test]
    public void Area_WidthOnlyForQ()
    {
        TraitModifierSet set = SetOf(new TraitEffect[] { new TraitAreaEffect(0.3f, new[] { TraitAreaAxis.Width }, new[] { AttackType.SkillQ }) });
        var q = new TraitAttackKey(AttackType.SkillQ);
        Assert.AreEqual(1.3f, set.AreaMultiplier(q, TraitAreaAxis.Width), Eps);
        Assert.AreEqual(1f, set.AreaMultiplier(q, TraitAreaAxis.Length), "사거리 그대로");
        Assert.AreEqual(1f, set.AreaMultiplier(new TraitAttackKey(AttackType.Default), TraitAreaAxis.Width));
    }

    // ── ⑧ 파생 공격 ─────────────────────────────────────────────────────

    [Test]
    public void Derived_Damage_Spec_PaladinBlast()
    {
        // 명세 §10: 기본 Q 합계 300 × 1.20 × 1.25 = 450 → 추가 135
        int dref = TraitHealRules.RoundHalfUp(300 * 1.2 * 1.25);
        Assert.AreEqual(450, dref);
        var effect = new TraitDerivedAttackEffect("PAL_Q_BLAST", 0.3f, new[] { AttackType.SkillQ }, radius: 2f, forwardOffset: 1f);
        Assert.AreEqual(135, effect.CreateRuntime(1).DamageFrom(dref));
    }

    [Test]
    public void Derived_ShotCounter_EveryThird()
    {
        var counter = new TraitShotCounter(3);
        var fired = new List<bool>();
        for (int i = 0; i < 6; i++)
            fired.Add(counter.Tick());
        CollectionAssert.AreEqual(new[] { false, false, true, false, false, true }, fired);

        counter.Tick();
        counter.Reset();
        Assert.IsFalse(counter.Tick(), "사망·새 회차 = 0 부터");
        Assert.IsTrue(new TraitShotCounter(1).Tick(), "N = 1 이면 매번");
    }

    [Test]
    public void Derived_Binding_RegistersSpec_KeyMatch_Unbind()
    {
        var host = new FakeHost();
        ITraitEffectBinding binding = new TraitDerivedAttackEffect("GUN_EXTRA_BASIC", 0.4f, new[] { AttackType.Default }, requiredCount: 3)
            .BindServer(host, 5);
        Assert.AreEqual(1, host.Derived.Count);
        TraitDerivedAttack spec = host.Derived[0];
        Assert.AreEqual("GUN_EXTRA_BASIC", spec.Key);
        Assert.AreEqual(5, spec.TraitHash);
        Assert.AreEqual(3, spec.Counter.Required);
        Assert.IsTrue(spec.Matches(new TraitAttackKey(AttackType.Default)));
        Assert.IsFalse(spec.Matches(new TraitAttackKey(AttackType.SkillQ)));
        Assert.AreEqual(TraitHitFlags.None, TraitHitFlags.Derived, "파생 = 특성·자원·CC·간파 없음");

        binding.Unbind();
        Assert.IsEmpty(host.Derived);
    }

    [Test]
    public void Derived_EmptyKey_IsValidationError()
    {
        var errors = new List<string>();
        new TraitDerivedAttackEffect("", 0.3f).Validate(errors);
        Assert.IsNotEmpty(errors);
    }

    // ── ⑨ 다음 공격 1회 강화 ────────────────────────────────────────────

    [Test]
    public void NextAttack_ConsumedOnce_SameCastSharesMultiplier()
    {
        var buffs = new TraitNextAttackBuffs();
        var basic = new TraitAttackKey(AttackType.Default);
        buffs.Grant(1, 1.3f, 4.0, TraitModifier.MaskOf(AttackType.Default), 0);

        Assert.AreEqual(1f, buffs.Consume(new TraitAttackKey(AttackType.SkillQ), 9, 0.5), "다른 공격은 소모하지 않는다");
        Assert.AreEqual(1.3f, buffs.Consume(basic, 10, 1.0), Eps);
        Assert.AreEqual(1.3f, buffs.Consume(basic, 10, 1.0), Eps, "같은 공격의 다른 대상 = 같은 배율(적 수만큼 소모 안 함)");
        Assert.AreEqual(1f, buffs.Consume(basic, 11, 1.1), "다음 공격엔 없음");
        Assert.AreEqual(0, buffs.Count);
    }

    [Test]
    public void NextAttack_Expires_RegrantRefreshesWithoutStacking()
    {
        var buffs = new TraitNextAttackBuffs();
        var basic = new TraitAttackKey(AttackType.Default);
        buffs.Grant(1, 1.3f, 4.0, 0, 0);
        Assert.AreEqual(1f, buffs.Consume(basic, 1, 4.0), "만료");

        buffs.Grant(1, 1.3f, 4.0, 0, 0);
        buffs.Grant(1, 1.3f, 6.0, 0, 0);
        Assert.AreEqual(1, buffs.Count, "중첩 없음");
        Assert.AreEqual(1.3f, buffs.Consume(basic, 2, 5.0), Eps, "지속시간 갱신");
    }

    [Test]
    public void NextAttack_Effect_GrantOncePerQCast_UnbindRemoves()
    {
        var host = new FakeHost();
        ITraitEffectBinding binding = new TraitNextAttackBuffEffect(
            new TraitTriggerFilter(TraitTrigger.AttackLanded, new[] { AttackType.SkillQ }), 0.3f, 4f, new[] { AttackType.Default })
            .BindServer(host, 42);

        host.Raise(Landed(AttackType.Default, 1));
        Assert.IsFalse(host.NextAttackBuffs.Has(42, 0), "Q 적중만");

        host.Raise(Landed(AttackType.SkillQ, 2));
        host.Raise(Landed(AttackType.SkillQ, 2));
        Assert.IsTrue(host.NextAttackBuffs.Has(42, 0));
        Assert.AreEqual(1, host.NextAttackBuffs.Count);

        binding.Unbind();
        Assert.IsFalse(host.NextAttackBuffs.Has(42, 0), "특성이 빠지면 강화도 지운다");
        Assert.AreEqual(0, host.SubscriberCount);
    }

    // ── ⑩ 스킬 수치 덮어쓰기 ────────────────────────────────────────────

    [Test]
    public void SkillValue_SetAddMultiplyOrder()
    {
        TraitModifierSet set = SetOf(
            new TraitEffect[] { new TraitSkillValueEffect(new TraitSkillValueEffect.Entry("GUN_Q_MAX_CHARGE", TraitValueOp.Multiply, 0.75f)) },
            new TraitEffect[]
            {
                new TraitSkillValueEffect(
                    new TraitSkillValueEffect.Entry("X", TraitValueOp.Set, 10f),
                    new TraitSkillValueEffect.Entry("X", TraitValueOp.Add, 2f),
                    new TraitSkillValueEffect.Entry("X", TraitValueOp.Multiply, 0.5f)),
            });

        Assert.AreEqual(1.5f, set.ResolveSkillValue("GUN_Q_MAX_CHARGE", 2f), Eps, "최대 충전 2초 × 0.75");
        Assert.AreEqual(6f, set.ResolveSkillValue("X", 99f), Eps, "(Set 10 + 2) × 0.5");
        Assert.AreEqual(3f, set.ResolveSkillValue("없는 이름", 3f), "없으면 기본값");
        Assert.IsTrue(set.HasSkillValue("X"));
        Assert.IsFalse(set.HasSkillValue("Y"));
    }

    // ── 스탯·모드 스위치 ───────────────────────────────────────────────

    [Test]
    public void Stat_Stages()
    {
        TraitModifierSet set = SetOf(new TraitEffect[]
        {
            new TraitStatEffect(PlayerStatKind.MoveSpeed, 0.1f, TraitStatStage.Base),
            new TraitStatEffect(PlayerStatKind.MoveSpeed, -0.2f),
            new TraitStatEffect(PlayerStatKind.AttackDamage, 0.15f, TraitStatStage.DealtDamage),
        });

        Assert.AreEqual(0.1f, set.BasePercent(PlayerStatKind.MoveSpeed), Eps);
        Assert.AreEqual(-0.2f, set.TotalPercent(PlayerStatKind.MoveSpeed), Eps);
        Assert.AreEqual(0.15f, set.DealtDamagePercent, Eps);
    }

    [Test]
    public void ModeIds_AreSpecIds_AndHasTraitIsFalseWithoutDirector()
    {
        Assert.AreEqual(7, TraitModeIds.All.Length);
        foreach (string id in TraitModeIds.All)
            Assert.IsTrue(TraitCatalog.TryGetSlot(id, out _), $"{id} 는 명세 고정표 ID");

        var go = new GameObject("TraitModeTest");
        try
        {
            PlayerTraitModifiers modifiers = PlayerTraitModifiers.For(go.transform);
            Assert.IsFalse(modifiers.HasTrait(TraitModeIds.GunnerVolley));
            Assert.IsFalse(modifiers.HasTrait(null));
            Assert.AreEqual(1f, modifiers.AreaMultiplier(new TraitAttackKey(AttackType.SkillQ), TraitAreaAxis.Width));
            Assert.AreEqual(2f, modifiers.ResolveSkillValue("GUN_Q_MAX_CHARGE", 2f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    // ── 되돌리기 장부: 재선택·재합류 ─────────────────────────────────────

    [Test]
    public void Ledger_Reselect_RevertsAndRebinds_OnceActionNotRerun()
    {
        var ledger = new TraitEffectLedger<List<ITraitEffectBinding>>();
        var host = new FakeHost();
        host.Remaining[PlayerSkillSlot.Sub] = 4f;
        var once = new CountingOnceEffect();
        var refund = new TraitCooldownRefundEffect(new TraitTriggerFilter(TraitTrigger.GuardSucceeded), 1f, new[] { PlayerSkillSlot.Sub });
        TraitEffect[] trait = { once, refund };
        const ulong client = 1;
        const int hash = 500;

        // 획득
        var bindings = new List<ITraitEffectBinding>();
        Assert.AreEqual(1, TraitEffectBinder.Bind(trait, host, client, hash, ledger, bindings));
        ledger.Add(client, hash, bindings);
        Assert.AreEqual(1, once.Runs);
        Assert.AreEqual(1, host.SubscriberCount);

        // 전멸 재선택 [예] — 전부 되돌림
        var taken = new List<TraitEffectLedger<List<ITraitEffectBinding>>.Record>();
        ledger.TakeAll(client, taken);
        foreach (var record in taken)
            foreach (ITraitEffectBinding b in record.Handle) b.Unbind();
        Assert.AreEqual(0, host.SubscriberCount, "빠진 특성 = 구독 해제");
        host.Raise(Guard(1));
        Assert.AreEqual(4f, host.Remaining[PlayerSkillSlot.Sub], "되돌린 뒤엔 발동하지 않는다");

        // 같은 특성을 다시 고름 — 연결은 다시, 1회 동작은 다시 하지 않음
        var again = new List<ITraitEffectBinding>();
        Assert.AreEqual(0, TraitEffectBinder.Bind(trait, host, client, hash, ledger, again));
        ledger.Add(client, hash, again);
        Assert.AreEqual(1, once.Runs, "재선택에서 1회 동작 중복 없음");
        host.Raise(Guard(2));
        Assert.AreEqual(3f, host.Remaining[PlayerSkillSlot.Sub], Eps, "다시 고른 특성은 다시 동작");
    }

    [Test]
    public void Ledger_Rejoin_OnceMarksMoveToNewClient_ViaSnapshot()
    {
        var ledger = new TraitEffectLedger<int>();
        var host = new FakeHost();
        var once = new CountingOnceEffect();
        TraitEffect[] trait = { new TraitStatEffect(PlayerStatKind.AttackDamage, 0.1f), once };
        const ulong oldClient = 1, newClient = 7;
        const int hash = 900;

        TraitEffectBinder.Bind(trait, host, oldClient, hash, ledger, null);
        ledger.Add(oldClient, hash, 0);
        Assert.IsTrue(ledger.HasOnce(oldClient, hash, 1), "효과 칸 1 = 1회 동작");

        // 퇴장: 보관 → 장부에서 빼기
        var model = new TraitSelectionModel(poolSize: 0);
        var onceKeys = new List<TraitOnceKey>();
        ledger.CaptureOnce(oldClient, onceKeys);
        TraitRejoinSnapshot snapshot = TraitRejoin.Capture(model, null, oldClient, onceKeys);
        ledger.TakeAll(oldClient, new List<TraitEffectLedger<int>.Record>());
        Assert.AreEqual(1, ledger.ClearOnce(oldClient));
        CollectionAssert.AreEqual(new[] { new TraitOnceKey(hash, 1) }, snapshot.UsedOnce);

        // 재합류: 1회 표시 복원 → 효과 다시 걸기
        Assert.AreEqual(1, ledger.RestoreOnce(newClient, snapshot.UsedOnce));
        Assert.AreEqual(0, TraitEffectBinder.Bind(trait, host, newClient, hash, ledger, null), "재합류에서 1회 동작 중복 없음");
        Assert.AreEqual(1, once.Runs);
        Assert.AreEqual(0, ledger.RestoreOnce(newClient, snapshot.UsedOnce), "같은 기록이 또 와도(Drop 경로) 멱등");
    }

    [Test]
    public void Ledger_Reconcile_RevertsDroppedAndAppliesMissing()
    {
        var ledger = new TraitEffectLedger<string>();
        ledger.Add(1, 10, "A");
        ledger.Add(1, 20, "B");
        ledger.Add(2, 10, "other");

        var toApply = new List<int>();
        var toRevert = new List<TraitEffectLedger<string>.Record>();
        ledger.Reconcile(1, new[] { 20, 30 }, toApply, toRevert);

        CollectionAssert.AreEqual(new[] { 30 }, toApply);
        Assert.AreEqual(1, toRevert.Count);
        Assert.AreEqual("A", toRevert[0].Handle);
        Assert.IsFalse(ledger.Has(1, 10));
        Assert.IsTrue(ledger.Has(1, 20));
        Assert.IsTrue(ledger.Has(2, 10), "다른 플레이어 기록은 그대로");

        ledger.Reconcile(1, new[] { 20 }, toApply, toRevert);
        Assert.IsEmpty(toApply);
        Assert.IsEmpty(toRevert, "이미 맞으면 할 일 없음");
    }

    [Test]
    public void Ledger_ClearOnDirectorDespawn_ForgetsOnceMarks()
    {
        var ledger = new TraitEffectLedger<int>();
        ledger.MarkOnce(1, 5, 0);
        Assert.IsFalse(ledger.MarkOnce(1, 5, 0));
        ledger.Clear();
        Assert.IsTrue(ledger.MarkOnce(1, 5, 0), "새 판 = 새로");
    }

    // ── 정의(SO) 연결 ───────────────────────────────────────────────────

    [Test]
    public void Definition_SerializedEffects_ContributeAndValidate()
    {
        TraitDefinition def = TraitModifierSetTests.MakeDefinition(
            new TraitDamageEffect(0.2f, new[] { AttackType.SkillQ }),
            new TraitCooldownRefundEffect(new TraitTriggerFilter(TraitTrigger.GuardSucceeded), 2f, new[] { PlayerSkillSlot.Sub }));
        try
        {
            Assert.AreEqual(2, def.Effects.Count, "[SerializeReference] 목록이 직렬화된다");
            var modifiers = new List<TraitModifier>();
            def.GetModifiers(modifiers);
            Assert.AreEqual(1, modifiers.Count, "이벤트형은 수치 기여 없음");
            Assert.AreEqual(1.2f, modifiers[0].Value, Eps);
            Assert.IsTrue(def.AffectsCooldownOf(PlayerSkillSlot.Sub));
            Assert.IsFalse(def.AffectsCooldownOf(PlayerSkillSlot.Main));

            var errors = new List<string>();
            def.Validate(errors);
            Assert.IsEmpty(errors);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(def);
        }
    }

    // ── 시전 ID ────────────────────────────────────────────────────────

    [Test]
    public void PlayerAttackLanded_OldConstructor_HasNoCastId()
    {
        var legacy = new PlayerAttackLanded(AttackType.Default, true, Array.Empty<Unit>(), null);
        var withCast = new PlayerAttackLanded(AttackType.SkillQ, true, Array.Empty<Unit>(), null, 12);
        Assert.AreEqual(0, legacy.CastId, "기존 발행자 시그니처 그대로");
        Assert.AreEqual(12, withCast.CastId);
    }
}
