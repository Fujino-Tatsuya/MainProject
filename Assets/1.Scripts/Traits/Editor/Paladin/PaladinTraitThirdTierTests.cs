using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 126 (2/4) — 팔라딘 3차 특성 3개(제압의 진격·수호의 잔광·불굴의 순환)의 수용 조건(명세 §11·§4·§7)을 실제 정의 에셋 값으로 확인한다:
/// 제압의 진격 = Q 만·시전당 대상별 1회·지속 밀치기 한 시퀀스 = 기절 1회·원래 Q 밀치기 유지·일반 몹 적용/중간보스 평상·제압 거부/취약 허용·밀치기 거부면 기절 없음·기절과 큰 값 ·
/// 수호의 잔광 = 무시 종료 때 1회·실패/사망 없음·중복 성공 1회·같은 채널 큰 값·다른 채널 무변경 ·
/// 불굴의 순환 = 긴 쪽만·동률 Q·둘 다 준비면 없음·0 아래 없음·R/우클릭 제외 · 공통 = 단독 적용/미적용·재선택·재접속 재실행 없음.
/// </summary>
public sealed class PaladinTraitThirdTierTests
{
    const float Eps = 1e-4f;
    const string Folder = "Assets/9.ScriptableObject/Traits/Paladin/";
    const string QDataPath = "Assets/9.ScriptableObject/Player/Garen/FirstMeleeMainSkillData.asset";

    // ── 도우미 ───────────────────────────────────────────────────────────

    sealed class FakeHost : ITraitEffectHost
    {
        public double Time;
        public readonly Dictionary<PlayerSkillSlot, float> Remaining = new Dictionary<PlayerSkillSlot, float>();
        public readonly List<ITraitAttackModifier> Modifiers = new List<ITraitAttackModifier>();
        /// <summary>보호막 채널(출처) → 잔량. <see cref="PlayerTraitEffects.GrantTraitShield"/> 와 같은 규칙(같은 채널 큰 값, 다른 채널 그대로).</summary>
        public readonly Dictionary<ulong, int> Shields = new Dictionary<ulong, int>();
        public readonly Dictionary<ulong, float> ShieldDurations = new Dictionary<ulong, float>();
        public int ShieldGrants;

        public double Now => Time;
        public event Action<TraitTriggerEvent> Triggered;
        public int SubscriberCount => Triggered?.GetInvocationList().Length ?? 0;

        public void Raise(TraitTriggerEvent e) => Triggered?.Invoke(e);

        public float GetCooldownRemaining(PlayerSkillSlot slot) => Remaining.TryGetValue(slot, out float r) ? r : 0f;

        public void ReduceCooldown(PlayerSkillSlot slot, float seconds) =>
            Remaining[slot] = TraitCooldownRefundRules.Reduce(GetCooldownRemaining(slot), seconds);

        public int MaxHpOf(Unit target) => 1000;

        public void GrantTraitShield(Unit target, int traitHash, int amount, float duration)
        {
            ulong source = TraitShieldRules.ChannelSource(traitHash);
            Shields.TryGetValue(source, out int existing);
            Shields[source] = TraitShieldRules.Merge(existing, amount);
            ShieldDurations[source] = duration;
            ShieldGrants++;
        }

        public TraitNextAttackBuffs NextAttackBuffs { get; } = new TraitNextAttackBuffs();
        public void AddAttackModifier(ITraitAttackModifier modifier) => Modifiers.Add(modifier);
        public void RemoveAttackModifier(ITraitAttackModifier modifier) => Modifiers.Remove(modifier);
        public void AddDerivedAttack(TraitDerivedAttack attack) { }
        public void RemoveDerivedAttack(TraitDerivedAttack attack) { }
        public void AddOverflowShare(TraitOverflowShare share) { }
        public void RemoveOverflowShare(TraitOverflowShare share) { }

        /// <summary><see cref="PlayerTraitEffects.ModifyOutgoingAttackServer"/> 와 같은 순회.</summary>
        public void Modify(TraitAttackKey key, int castId, int targetId, Vector3 direction, ref AttackInfo info)
        {
            for (int i = 0; i < Modifiers.Count; i++)
                Modifiers[i].Modify(key, castId, targetId, direction, ref info);
        }
    }

    static PaladinTraitDefinition Load(string id)
    {
        var def = AssetDatabase.LoadAssetAtPath<PaladinTraitDefinition>(Folder + id + ".asset");
        Assert.IsNotNull(def, $"{Folder}{id}.asset(PaladinTraitDefinition)이 있어야 한다");
        Assert.AreEqual(id, def.Id);
        return def;
    }

    static List<ITraitEffectBinding> Bind(TraitDefinition def, FakeHost host)
    {
        var bindings = new List<ITraitEffectBinding>();
        TraitEffectBinder.Bind(def.Effects, host, 1, def.IdHash, (TraitEffectLedger<int>)null, bindings);
        return bindings;
    }

    static void UnbindAll(List<ITraitEffectBinding> bindings)
    {
        foreach (ITraitEffectBinding b in bindings) b?.Unbind();
    }

    static string Plain(string text) => Regex.Replace(text ?? string.Empty, "<[^>]+>", string.Empty);

    static FirstMeleeMainSkillData QData()
    {
        var data = AssetDatabase.LoadAssetAtPath<FirstMeleeMainSkillData>(QDataPath);
        Assert.IsNotNull(data, QDataPath);
        return data;
    }

    /// <summary>원래 Q 틱 1건의 공격 정보(<see cref="FirstMeleeMainSkill"/>.OnHoldTick 과 같은 값).</summary>
    static AttackInfo QTickInfo(FirstMeleeMainSkillData data) =>
        new AttackInfo(100, AttackType.SkillQ,
            knockbackStrength: Mathf.Max(data.KnockbackStrength, data.AdvanceSpeed),
            knockbackDuration: data.KnockbackDuration,
            staggerDuration: data.StaggerDuration,
            knockbackDirection: Vector3.forward);

    static readonly TraitAttackKey QKey = TraitAttackKey.ForSkill(PlayerSkillSlot.Main, false);

    static TraitTriggerEvent ECompleted(int castId) =>
        new TraitTriggerEvent(TraitTrigger.SkillCompleted, AttackType.SkillE, TraitAttackForm.Normal, castId);

    static TraitTriggerEvent Passive() =>
        new TraitTriggerEvent(TraitTrigger.PassiveProc, AttackType.None, TraitAttackForm.Normal, 0, false);

    // ── 풀·카드·공통 ────────────────────────────────────────────────────

    [Test]
    public void Pool_ContainsThirdTier_AfterSecondTier()
    {
        List<string> ids = TraitContentTestKit.CharacterPool(TutorialCharacter.Paladin).Traits.Select(t => t.Id).ToList();
        int share = ids.IndexOf(PaladinTraitValues.ShareHeal);
        int stun = ids.IndexOf(PaladinTraitValues.QStun);
        int shield = ids.IndexOf(PaladinTraitValues.EShield);
        int cdr = ids.IndexOf(PaladinTraitValues.PassiveCdr);
        Assert.That(share, Is.GreaterThanOrEqualTo(0));
        Assert.That(stun, Is.GreaterThan(share), "명세 순서 — 3차는 2차 뒤");
        Assert.That(shield, Is.GreaterThan(stun));
        Assert.That(cdr, Is.GreaterThan(shield));
    }

    [TestCase("PAL_3_Q_STUN", "제압의 진격", "Q 강화 · 기절", "Q로 밀친 적을 밀치기가 끝난 뒤 0.75초 동안 기절시킵니다.")]
    [TestCase("PAL_3_E_SHIELD", "수호의 잔광", "E 강화 · 보호막",
        "E로 공격을 막으면 피해 무시 효과가 끝난 뒤 최대 체력의 10%만큼 보호막을 4초 동안 얻습니다.")]
    [TestCase("PAL_3_PASSIVE_CDR", "불굴의 순환", "패시브 강화",
        "패시브가 발동하면 Q와 E 중 더 오래 기다려야 하는 스킬의 남은 재사용 대기시간이 1초 줄어듭니다.")]
    public void Card_MatchesSpecSelectionText(string id, string name, string category, string expected)
    {
        PaladinTraitDefinition def = Load(id);
        Assert.AreEqual(name, def.InlineDisplayName);
        Assert.AreEqual(category, def.InlineCategory);
        Assert.IsTrue(TraitTooltip.Format(def, def.InlineDescription, null, false, TraitContent.Build(), out string text, out string error), error);
        Assert.AreEqual(expected, Plain(text), "명세 '선택 화면' 문구");
    }

    [Test]
    public void Aliases_ReadEffectFields()
    {
        Assert.AreEqual(0.75f, PaladinTraitValues.StunDuration(Load(PaladinTraitValues.QStun)), Eps);
        Assert.AreEqual(0.1f, PaladinTraitValues.ShieldMaxHpRatio(Load(PaladinTraitValues.EShield)), Eps);
        Assert.AreEqual(4f, PaladinTraitValues.ShieldDuration(Load(PaladinTraitValues.EShield)), Eps);
    }

    [Test]
    public void Definitions_Valid_NoOnceActions_SoRejoinRerunsNothing()
    {
        foreach (string id in new[] { PaladinTraitValues.QStun, PaladinTraitValues.EShield, PaladinTraitValues.PassiveCdr })
        {
            PaladinTraitDefinition def = Load(id);
            var errors = new List<string>();
            def.Validate(errors);
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.IsFalse(def.Effects.Any(e => e != null && e.HasOnceAction), $"{id} — 획득 1회 동작 없음(재접속·재선택에서 다시 실행할 것이 없다)");
        }
    }

    [TestCase("PAL_3_E_SHIELD")]
    [TestCase("PAL_3_PASSIVE_CDR")]
    public void Reselect_RebindsOnce_NoDoubleSubscription(string id)
    {
        PaladinTraitDefinition def = Load(id);
        var ledger = new TraitEffectLedger<List<ITraitEffectBinding>>();
        var host = new FakeHost();

        var first = new List<ITraitEffectBinding>();
        Assert.AreEqual(0, TraitEffectBinder.Bind(def.Effects, host, 1, def.IdHash, ledger, first));
        ledger.Add(1, def.IdHash, first);

        var taken = new List<TraitEffectLedger<List<ITraitEffectBinding>>.Record>();
        ledger.TakeAll(1, taken);
        foreach (var record in taken) UnbindAll(record.Handle);
        Assert.AreEqual(0, host.SubscriberCount, "되돌리면 구독 해제");

        var again = new List<ITraitEffectBinding>();
        Assert.AreEqual(0, TraitEffectBinder.Bind(def.Effects, host, 1, def.IdHash, ledger, again), "재실행할 1회 동작 없음");
        Assert.AreEqual(1, host.SubscriberCount, "구독 1개만(이중 연결 없음)");
    }

    // ── 3차 · 제압의 진격 ───────────────────────────────────────────────

    [Test]
    public void QStun_FirstQHitPerTargetPerCast_CarriesConcussion_KeepsOriginalPush()
    {
        FirstMeleeMainSkillData data = QData();
        PaladinTraitDefinition def = Load(PaladinTraitValues.QStun);
        var host = new FakeHost();
        List<ITraitEffectBinding> bindings = Bind(def, host);
        Assert.AreEqual(1, host.Modifiers.Count, "밀치기 수정자 1개");

        AttackInfo original = QTickInfo(data);
        AttackInfo info = QTickInfo(data);
        host.Modify(QKey, 1, 10, Vector3.right, ref info);
        Assert.AreEqual(0.75f, info.concussionDuration, Eps, "Q 첫 판정에 뇌진탕 0.75");
        Assert.AreEqual(original.knockbackStrength, info.knockbackStrength, Eps, "원래 Q 밀치기 속도 그대로");
        Assert.AreEqual(original.knockbackDuration, info.knockbackDuration, Eps, "원래 Q 밀치기 시간 그대로");
        Assert.AreEqual(original.knockbackDirection, info.knockbackDirection, "원래 Q 견인 방향(진행 방향) 그대로");
        Assert.AreEqual(original.staggerDuration, info.staggerDuration, Eps, "원래 Q 경직 그대로");

        AttackInfo again = QTickInfo(data);
        host.Modify(QKey, 1, 10, Vector3.forward, ref again);
        Assert.AreEqual(0f, again.concussionDuration, Eps, "같은 시전·같은 대상 두 번째 판정 = 새 부여 없음(시전당 대상별 1회)");

        AttackInfo other = QTickInfo(data);
        host.Modify(QKey, 1, 11, Vector3.forward, ref other);
        Assert.AreEqual(0.75f, other.concussionDuration, Eps, "같은 시전 다른 대상 = 각자 1회");

        AttackInfo nextCast = QTickInfo(data);
        host.Modify(QKey, 2, 10, Vector3.forward, ref nextCast);
        Assert.AreEqual(0.75f, nextCast.concussionDuration, Eps, "다음 시전 = 다시 1회");

        foreach (TraitAttackKey key in new[] { new TraitAttackKey(AttackType.Default), TraitAttackKey.ForSkill(PlayerSkillSlot.Sub, false),
                     TraitAttackKey.ForSkill(PlayerSkillSlot.Ultimate, false), new TraitAttackKey(AttackType.Skill) })
        {
            AttackInfo notQ = new AttackInfo(100, key.Type);
            host.Modify(key, 3, 10, Vector3.forward, ref notQ);
            Assert.AreEqual(0f, notQ.concussionDuration, Eps, $"{key} — Q 가 아닌 공격엔 없음");
            Assert.AreEqual(0f, notQ.knockbackStrength, Eps, $"{key} — 밀치기도 붙이지 않는다");
        }

        UnbindAll(bindings);
        Assert.IsEmpty(host.Modifiers, "특성 없음(되돌림) = 수정자 없음 → Q 는 원래대로");
    }

    [Test]
    public void QStun_TraitPushNeverOverridesOriginalQ_AndQPushIsContinuous()
    {
        // 공용 밀치기 효과는 거리 0 을 거부(Validate)하므로 '원래 Q 그대로'는 원래보다 작은 값으로 둔다 — 더 센 기존 밀치기가 남는다.
        FirstMeleeMainSkillData data = QData();
        var effect = (TraitKnockbackEffect)Load(PaladinTraitValues.QStun).Effects.Single();
        float qDistance = Mathf.Max(data.KnockbackStrength, data.AdvanceSpeed) * data.KnockbackDuration;
        Assert.That(effect.PushSpeed * 0.1f, Is.LessThan(qDistance), "특성 밀치기 거리 < 원래 Q 밀치기 거리");
        AttackInfo info = QTickInfo(data);
        effect.ApplyCrowdControl(ref info, Vector3.right);
        Assert.AreEqual(data.StaggerDuration, info.staggerDuration, Eps, "특성 경직 ≤ 원래 Q 경직");
        Assert.AreEqual(data.KnockbackDuration, info.knockbackDuration, Eps);

        // 지속 밀치기 = 틱마다 다시 민다(넉백 시간 ≥ 틱 간격). 같으면 틱 사이에 넉백이 잠깐 끊길 수 있어 뇌진탕을 이어 싣는다(아래 테스트).
        Assert.That(data.KnockbackDuration, Is.GreaterThanOrEqualTo(data.TickInterval - Eps), "Q 넉백 시간 ≥ 틱 간격");
    }

    [Test]
    public void QStun_Carry_WholeContinuousPush_IsOneSequence()
    {
        var carry = new PaladinQConcussionCarry();
        // 틱 1: 대상 10 첫 판정(부여 0.75), 대상 11 은 아직 없음
        Assert.AreEqual(0.75f, carry.Resolve(10, 0.75f), Eps);
        carry.EndTick();
        // 틱 2·3: 공용 수정자는 더 안 싣지만(시전당 대상별 1회) 끊김 없이 맞는 동안 이어 싣는다
        Assert.AreEqual(0.75f, carry.Resolve(10, 0f), Eps, "지속 밀치기 중 재설정");
        Assert.AreEqual(0f, carry.Resolve(11, 0f), Eps, "부여받은 적 없는 대상은 없음");
        carry.EndTick();
        Assert.AreEqual(0.75f, carry.Resolve(10, 0f), Eps);
        carry.EndTick();
        // 틱 4: 대상 10 이 빠짐 → 시퀀스 끝. 틱 5 에 다시 맞아도 같은 시전에서는 없음
        carry.EndTick();
        Assert.AreEqual(0f, carry.Resolve(10, 0f), Eps, "한 틱이라도 빠지면 그 시퀀스 끝 — 같은 시전 재기절 없음");
        carry.EndTick();
        // 다음 시전
        carry.Reset();
        Assert.AreEqual(0f, carry.Resolve(10, 0f), Eps, "시전 시작에 비운다");
        Assert.AreEqual(0.75f, carry.Resolve(10, 0.75f), Eps, "새 시전의 첫 부여");
    }

    [Test]
    public void QStun_ReceiverSequence_StunsOnceAfterLastPush()
    {
        // 수신측(MonsterBase): 밀린 상태면 매 히트 ConcussionState.Apply(재설정) → 넉백이 끝나는 순간 1회 기절.
        var state = new ConcussionState();
        var carry = new PaladinQConcussionCarry();
        int stuns = 0;
        float stunLength = 0f;
        for (int tick = 0; tick < 6; tick++)
        {
            float concussion = carry.Resolve(1, tick == 0 ? 0.75f : 0f);
            Assert.AreEqual(ConcussionState.ApplyResult.Pending, state.Apply(concussion, true, false), $"틱 {tick} 재설정");
            if (tick == 2)
            {
                // 틱 사이에 넉백이 잠깐 끝난 경우(넉백 시간 = 틱 간격) — 소비된 기절은 다음 틱 밀치기가 덮지만, 이어 실은 뇌진탕이 다시 대기한다.
                state.ConsumeOnKnockbackEnd(false);
            }
            carry.EndTick();
        }

        float end = state.ConsumeOnKnockbackEnd(false);
        if (end > 0f) { stuns++; stunLength = end; }
        Assert.AreEqual(1, stuns, "마지막 밀치기 뒤 기절이 남는다");
        Assert.AreEqual(0.75f, stunLength, Eps);
    }

    [TestCase(MonsterState.Chase, false, MonsterRank.Normal, false, true, TestName = "QStun_Eligibility_NormalMonster_Applies")]
    [TestCase(MonsterState.Attack, false, MonsterRank.Normal, false, true, TestName = "QStun_Eligibility_NormalMonsterAttacking_Applies")]
    [TestCase(MonsterState.Chase, true, MonsterRank.Normal, false, false, TestName = "QStun_Eligibility_SuperArmor_PushRejected_NoStun")]
    [TestCase(MonsterState.Groggy, false, MonsterRank.Normal, false, false, TestName = "QStun_Eligibility_Groggy_NoStun")]
    [TestCase(MonsterState.Chase, false, MonsterRank.MidBoss, false, false, TestName = "QStun_Eligibility_BossRuleNormal_Rejected")]
    [TestCase(MonsterState.Chase, false, MonsterRank.MidBoss, true, true, TestName = "QStun_Eligibility_BossRuleVulnerable_Allowed")]
    public void QStun_OnlyActuallyPushedTargets_GetConcussion(MonsterState state, bool armor, MonsterRank rank, bool vulnerable, bool stunned)
    {
        // MonsterBase.ReceiveAttack: 넉백 진입(CanEnterKnockback)이 된 대상만 뇌진탕을 받는다 — 밀치기 거부(피해만)면 기절 자격 없음.
        bool pushed = MonsterHitReactionPolicy.CanEnterKnockback(state, armor, rank, vulnerable);
        var concussion = new ConcussionState();
        if (pushed)
            concussion.Apply(0.75f, true, armor);
        float stun = concussion.ConsumeOnKnockbackEnd(false);
        Assert.AreEqual(stunned ? 0.75f : 0f, stun, Eps);
    }

    [Test]
    public void QStun_SuppressCancels_SuperArmorRejects_StunTakesMax()
    {
        Assert.IsTrue(MonsterStatusEffect.CancelsPendingConcussion(MonsterState.Knockback, MonsterState.Groggy), "밀치기 중 제압(그로기) 진입 = 예약 기절 취소");

        var armored = new ConcussionState();
        Assert.AreEqual(ConcussionState.ApplyResult.Rejected, armored.Apply(0.75f, true, true), "슈퍼아머 거부");

        var lateArmor = new ConcussionState();
        lateArmor.Apply(0.75f, true, false);
        Assert.AreEqual(0f, lateArmor.ConsumeOnKnockbackEnd(true), Eps, "넉백 끝 순간 자격 재확인");

        Assert.AreEqual(1.2f, ConcussionState.MergeStun(1.2f, 0.75f), Eps, "기존 기절이 더 길면 유지");
        Assert.AreEqual(0.75f, ConcussionState.MergeStun(0.3f, 0.75f), Eps, "짧으면 0.75 — 합산 아님");
    }

    // ── 3차 · 수호의 잔광 ───────────────────────────────────────────────

    [TestCase(true, SkillEndReason.Completed, 500, true, TestName = "EShield_Grant_CounterEnd_Alive")]
    [TestCase(true, SkillEndReason.DashCancelled, 500, true, TestName = "EShield_Grant_DashCutsIgnore_Alive")]
    [TestCase(true, SkillEndReason.Cancelled, 500, true, TestName = "EShield_Grant_StaggerCutsIgnore_Alive")]
    [TestCase(false, SkillEndReason.Completed, 500, false, TestName = "EShield_NoGrant_GuardFailed")]
    [TestCase(true, SkillEndReason.CasterDied, 0, false, TestName = "EShield_NoGrant_Died")]
    [TestCase(true, SkillEndReason.Completed, 0, false, TestName = "EShield_NoGrant_ZeroHp")]
    public void EShield_GrantRule(bool blocked, SkillEndReason reason, int hp, bool expected)
    {
        Assert.AreEqual(expected, PaladinTraitRules.GrantsGuardAfterglow(blocked, reason, hp));
    }

    [Test]
    public void EShield_GrantsAtIgnoreEnd_TenPercentFourSeconds_OncePerGuardCast()
    {
        PaladinTraitDefinition def = Load(PaladinTraitValues.EShield);
        var host = new FakeHost();
        List<ITraitEffectBinding> bindings = Bind(def, host);
        ulong channel = TraitShieldRules.ChannelSource(def.IdHash);

        host.Raise(new TraitTriggerEvent(TraitTrigger.GuardSucceeded, AttackType.SkillE, TraitAttackForm.Normal, 1));
        Assert.AreEqual(0, host.ShieldGrants, "방어 성공 순간엔 아직 없음(지급 = 피해 무시가 끝난 뒤)");

        host.Raise(ECompleted(1));
        Assert.AreEqual(1, host.ShieldGrants);
        Assert.AreEqual(100, host.Shields[channel], "최대 체력 1000 × 10%");
        Assert.AreEqual(4f, host.ShieldDurations[channel], Eps);

        host.Raise(ECompleted(1));
        Assert.AreEqual(1, host.ShieldGrants, "같은 방어 성공 시전 = 1회");

        host.Raise(new TraitTriggerEvent(TraitTrigger.SkillCompleted, AttackType.SkillQ, TraitAttackForm.Normal, 2));
        host.Raise(new TraitTriggerEvent(TraitTrigger.Healed, AttackType.None, TraitAttackForm.Normal, 0, false, 0, null, 50));
        host.Raise(new TraitTriggerEvent(TraitTrigger.AttackLanded, AttackType.SkillE, TraitAttackForm.Normal, 3, true, 1));
        Assert.AreEqual(1, host.ShieldGrants, "Q 종료·회복·반격 적중은 지급 사건이 아님");

        host.Raise(ECompleted(4));
        Assert.AreEqual(2, host.ShieldGrants, "다음 방어 성공 시전 = 다시 1회");

        UnbindAll(bindings);
        host.Raise(ECompleted(5));
        Assert.AreEqual(2, host.ShieldGrants, "특성 없음(되돌림) = 보호막 없음");
    }

    [Test]
    public void EShield_SameChannelKeepsLarger_OtherChannelsUntouched()
    {
        PaladinTraitDefinition def = Load(PaladinTraitValues.EShield);
        var host = new FakeHost();
        Bind(def, host);
        ulong channel = TraitShieldRules.ChannelSource(def.IdHash);
        ulong otherChannel = TraitShieldRules.ChannelSource(TraitIdHash.Of("PAL_4_HEAL_SHIELD"));
        Assert.AreNotEqual(channel, otherChannel, "불굴의 가호와 다른 채널");

        host.Shields[otherChannel] = 40;
        host.Shields[channel] = 150;
        host.Raise(ECompleted(1));
        Assert.AreEqual(150, host.Shields[channel], "기존 잔량이 크면 유지(합산 아님)");
        Assert.AreEqual(4f, host.ShieldDurations[channel], Eps, "지속시간은 새로");

        host.Shields[channel] = 30;
        host.Raise(ECompleted(2));
        Assert.AreEqual(100, host.Shields[channel], "작으면 새 양");
        Assert.AreEqual(40, host.Shields[otherChannel], "다른 보호막 채널은 그대로");
    }

    // ── 3차 · 불굴의 순환 ───────────────────────────────────────────────

    [Test]
    public void PassiveCdr_ReducesOnlyLonger_TieQ_NoneWhenBothReady_NeverBelowZero()
    {
        PaladinTraitDefinition def = Load(PaladinTraitValues.PassiveCdr);
        var host = new FakeHost();
        List<ITraitEffectBinding> bindings = Bind(def, host);
        void Set(float q, float e)
        {
            host.Remaining[PlayerSkillSlot.Main] = q;
            host.Remaining[PlayerSkillSlot.Sub] = e;
            host.Remaining[PlayerSkillSlot.Interrupt] = 9f;
            host.Remaining[PlayerSkillSlot.Ultimate] = 30f;
        }

        Set(5f, 3f);
        host.Raise(Passive());
        Assert.AreEqual(4f, host.Remaining[PlayerSkillSlot.Main], Eps, "Q 가 더 길다 → Q −1");
        Assert.AreEqual(3f, host.Remaining[PlayerSkillSlot.Sub], Eps, "둘 다 줄이지 않는다");
        Assert.AreEqual(9f, host.Remaining[PlayerSkillSlot.Interrupt], Eps, "우클릭 제외");
        Assert.AreEqual(30f, host.Remaining[PlayerSkillSlot.Ultimate], Eps, "R 제외");

        Set(2f, 6f);
        host.Raise(Passive());
        Assert.AreEqual(2f, host.Remaining[PlayerSkillSlot.Main], Eps);
        Assert.AreEqual(5f, host.Remaining[PlayerSkillSlot.Sub], Eps, "E 가 더 길다 → E −1");

        Set(4f, 4f);
        host.Raise(Passive());
        Assert.AreEqual(3f, host.Remaining[PlayerSkillSlot.Main], Eps, "동률 = Q 우선");
        Assert.AreEqual(4f, host.Remaining[PlayerSkillSlot.Sub], Eps);

        Set(0f, 0f);
        host.Raise(Passive());
        host.Remaining[PlayerSkillSlot.Main] = 5f;
        host.Remaining[PlayerSkillSlot.Sub] = 5f;
        Assert.AreEqual(5f, host.Remaining[PlayerSkillSlot.Main], Eps, "둘 다 준비면 아무것도 안 함 — 저장 없음");
        Assert.AreEqual(5f, host.Remaining[PlayerSkillSlot.Sub], Eps);

        Set(0.4f, 0f);
        host.Raise(Passive());
        Assert.AreEqual(0f, host.Remaining[PlayerSkillSlot.Main], Eps, "max(0, 0.4 − 1)");
        Assert.AreEqual(0f, host.Remaining[PlayerSkillSlot.Sub], Eps);

        UnbindAll(bindings);
        Set(5f, 3f);
        host.Raise(Passive());
        Assert.AreEqual(5f, host.Remaining[PlayerSkillSlot.Main], Eps, "특성 없음(되돌림) = 차감 없음");
    }

    [Test]
    public void PassiveCdr_OncePerProc_HealShieldGuardAreNotProcs()
    {
        PaladinTraitDefinition def = Load(PaladinTraitValues.PassiveCdr);
        var host = new FakeHost();
        Bind(def, host);
        host.Remaining[PlayerSkillSlot.Main] = 10f;
        host.Remaining[PlayerSkillSlot.Sub] = 2f;

        host.Raise(new TraitTriggerEvent(TraitTrigger.Healed, AttackType.None, TraitAttackForm.Normal, 0, false, 0, null, 50));
        host.Raise(ECompleted(1));
        host.Raise(new TraitTriggerEvent(TraitTrigger.GuardSucceeded, AttackType.SkillE, TraitAttackForm.Normal, 1));
        host.Raise(new TraitTriggerEvent(TraitTrigger.AttackLanded, AttackType.Default, TraitAttackForm.Normal, 2, true, 1));
        Assert.AreEqual(10f, host.Remaining[PlayerSkillSlot.Main], Eps, "회복 전달·보호막·방어·적중은 패시브 발동이 아니다");

        host.Raise(Passive());
        Assert.AreEqual(9f, host.Remaining[PlayerSkillSlot.Main], Eps, "발동 1회 = 1초 1회");
        host.Raise(Passive());
        Assert.AreEqual(8f, host.Remaining[PlayerSkillSlot.Main], Eps, "다음 발동은 또 1회(발동마다 새 사건)");
    }
}
