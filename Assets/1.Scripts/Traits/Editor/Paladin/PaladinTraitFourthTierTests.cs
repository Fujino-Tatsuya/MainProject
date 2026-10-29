using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 126 (3/4) — 팔라딘 4차 특성 3개(수호의 파동·진격의 충격·불굴의 가호)의 수용 조건(명세 §11·§3·§5·§7·§10)을 실제 정의 에셋 값으로 확인한다:
/// 수호의 파동 = 반격 판정에 밀치기(피해 유지·추가 피해 없음)·시전당 대상별 1회·반경 경계·보스 평상 밀치기만 면역·E 넉백 실행당 벽 1회 ·
/// 진격의 충격 = 정상 종료/해제/만료 1회·강제 중단·사망·대시 없음·DQpacket(예정 틱 합계, 적 수·실제 틱 무관) × 0.3·파생 = 특성·자원·CC 없음 ·
/// 불굴의 가호 = 실제 회복 × 75%·상한 최대 체력 10%·실회복 0 없음·같은 채널 큰 값(여러 팔라딘 합산 없음)·다른 채널 유지·연쇄 없음 ·
/// 공통 = 단독 적용/미적용·재선택·재접속 재실행 없음 · §10 검산(진격 강화+진격의 충격, 불굴의 회복+나누는 불굴+불굴의 가호).
/// </summary>
public sealed class PaladinTraitFourthTierTests
{
    const float Eps = 1e-4f;
    const string Folder = "Assets/9.ScriptableObject/Traits/Paladin/";
    const string QDataPath = "Assets/9.ScriptableObject/Player/Garen/FirstMeleeMainSkillData.asset";
    const string EDataPath = "Assets/9.ScriptableObject/Player/Garen/FirstMeleeSubSkillData.asset";

    // ── 도우미 ───────────────────────────────────────────────────────────

    /// <summary>보호막 장부 — 대상 × 채널 → 잔량. 여러 팔라딘(허브)이 같은 대상에 쓰는 경우를 위해 허브 밖에 둔다.</summary>
    sealed class ShieldBook
    {
        public readonly Dictionary<(Unit, ulong), int> Amount = new Dictionary<(Unit, ulong), int>();
        public readonly Dictionary<(Unit, ulong), float> Duration = new Dictionary<(Unit, ulong), float>();
        public int Grants;

        public int Of(Unit target, int traitHash) =>
            Amount.TryGetValue((target, TraitShieldRules.ChannelSource(traitHash)), out int v) ? v : 0;
    }

    sealed class FakeHost : ITraitEffectHost
    {
        public double Time;
        public readonly ShieldBook Book;
        public readonly Dictionary<Unit, int> MaxHp = new Dictionary<Unit, int>();
        public readonly List<ITraitAttackModifier> Modifiers = new List<ITraitAttackModifier>();
        public readonly List<TraitDerivedAttack> Derived = new List<TraitDerivedAttack>();
        public int Raised;

        public FakeHost(ShieldBook book = null) => Book = book ?? new ShieldBook();

        public double Now => Time;
        public event Action<TraitTriggerEvent> Triggered;
        public int SubscriberCount => Triggered?.GetInvocationList().Length ?? 0;

        public void Raise(TraitTriggerEvent e)
        {
            Raised++;
            Triggered?.Invoke(e);
        }

        public float GetCooldownRemaining(PlayerSkillSlot slot) => 0f;
        public void ReduceCooldown(PlayerSkillSlot slot, float seconds) { }

        public int MaxHpOf(Unit target) => target != null && MaxHp.TryGetValue(target, out int hp) ? hp : 1000;

        /// <summary><see cref="PlayerTraitEffects.GrantTraitShield"/> 와 같은 규칙(같은 채널 큰 값·시간 새로, 다른 채널 그대로).</summary>
        public void GrantTraitShield(Unit target, int traitHash, int amount, float duration)
        {
            var key = (target, TraitShieldRules.ChannelSource(traitHash));
            Book.Amount.TryGetValue(key, out int existing);
            Book.Amount[key] = TraitShieldRules.Merge(existing, amount);
            Book.Duration[key] = duration;
            Book.Grants++;
        }

        public TraitNextAttackBuffs NextAttackBuffs { get; } = new TraitNextAttackBuffs();
        public void AddAttackModifier(ITraitAttackModifier modifier) => Modifiers.Add(modifier);
        public void RemoveAttackModifier(ITraitAttackModifier modifier) => Modifiers.Remove(modifier);
        public void AddDerivedAttack(TraitDerivedAttack attack) => Derived.Add(attack);
        public void RemoveDerivedAttack(TraitDerivedAttack attack) => Derived.Remove(attack);
        public void AddOverflowShare(TraitOverflowShare share) { }
        public void RemoveOverflowShare(TraitOverflowShare share) { }

        /// <summary><see cref="PlayerTraitEffects.ModifyOutgoingAttackServer"/> 와 같은 순회.</summary>
        public void Modify(TraitAttackKey key, int castId, int targetId, Vector3 direction, ref AttackInfo info)
        {
            for (int i = 0; i < Modifiers.Count; i++)
                Modifiers[i].Modify(key, castId, targetId, direction, ref info);
        }

        /// <summary><see cref="PlayerTraitEffects.GetDerivedAttacks"/> 와 같은 거름.</summary>
        public List<TraitDerivedAttack> DerivedFor(TraitAttackKey key) => Derived.Where(d => d.Matches(key)).ToList();
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

    static TraitModifierSet SetOf(params TraitDefinition[] defs)
    {
        var set = new TraitModifierSet();
        set.Rebuild(defs.Select(d => d.IdHash).ToList(), h => defs.FirstOrDefault(d => d.IdHash == h));
        Assert.IsEmpty(set.Errors, string.Join("\n", set.Errors));
        return set;
    }

    static string Plain(string text) => Regex.Replace(text ?? string.Empty, "<[^>]+>", string.Empty);

    static T LoadData<T>(string path) where T : UnityEngine.Object
    {
        var data = AssetDatabase.LoadAssetAtPath<T>(path);
        Assert.IsNotNull(data, path);
        return data;
    }

    static readonly TraitAttackKey QKey = TraitAttackKey.ForSkill(PlayerSkillSlot.Main, false);
    static readonly TraitAttackKey EKey = TraitAttackKey.ForSkill(PlayerSkillSlot.Sub, false);

    static TraitTriggerEvent Healed(Unit subject, int amount) =>
        new TraitTriggerEvent(TraitTrigger.Healed, AttackType.None, TraitAttackForm.Normal, 0, false, 0, subject, amount);

    /// <summary>회복 대상 자리표시 유닛 2개(자신·아군). EditMode 라 Awake 가 돌지 않는다 — 보호막 장부 키로만 쓴다.</summary>
    static (Unit self, Unit ally, GameObject[] objects) TwoUnits()
    {
        var a = new GameObject("PaladinTraitTest_Self");
        var b = new GameObject("PaladinTraitTest_Ally");
        return (a.AddComponent<TrainingDummy>(), b.AddComponent<TrainingDummy>(), new[] { a, b });
    }

    static void DestroyAll(GameObject[] objects)
    {
        foreach (GameObject go in objects)
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
    }

    // ── 풀·카드·공통 ────────────────────────────────────────────────────

    [Test]
    public void Pool_ContainsFourthTier_InSpecOrder_AfterThirdTier()
    {
        List<string> ids = TraitContentTestKit.CharacterPool(TutorialCharacter.Paladin).Traits.Select(t => t.Id).ToList();
        int cdr = ids.IndexOf(PaladinTraitValues.PassiveCdr);
        int push = ids.IndexOf(PaladinTraitValues.EPush);
        int blast = ids.IndexOf(PaladinTraitValues.QBlast);
        int healShield = ids.IndexOf(PaladinTraitValues.HealShield);
        Assert.That(cdr, Is.GreaterThanOrEqualTo(0));
        Assert.That(push, Is.GreaterThan(cdr), "명세 순서 — 4차는 3차 뒤");
        Assert.That(blast, Is.GreaterThan(push));
        Assert.That(healShield, Is.GreaterThan(blast));
    }

    [TestCase("PAL_4_E_PUSH", "수호의 파동", "E 강화 · 밀치기", "E로 공격을 막으면 반격과 함께 주변 3m의 적을 밀쳐냅니다.")]
    [TestCase("PAL_4_HEAL_SHIELD", "불굴의 가호", "패시브 강화 · 보호막",
        "패시브로 체력을 회복한 대상에게 회복량의 75%만큼 보호막을 4초 동안 줍니다. 보호막은 대상 최대 체력의 10%를 넘지 않습니다.")]
    public void Card_MatchesSpecSelectionText(string id, string name, string category, string expected)
    {
        PaladinTraitDefinition def = Load(id);
        Assert.AreEqual(name, def.InlineDisplayName);
        Assert.AreEqual(category, def.InlineCategory);
        Assert.IsTrue(TraitTooltip.Format(def, def.InlineDescription, null, false, TraitContent.Build(), out string text, out string error), error);
        Assert.AreEqual(expected, Plain(text), "명세 '선택 화면' 문구");
    }

    [Test]
    public void Card_QBlast_TemplateIsSpecGrammar_DamageNeedsPlayer()
    {
        PaladinTraitDefinition def = Load(PaladinTraitValues.QBlast);
        Assert.AreEqual("진격의 충격", def.InlineDisplayName);
        Assert.AreEqual("Q 강화", def.InlineCategory);
        Assert.AreEqual("Q가 끝나는 지점 앞쪽에 충격을 일으켜 {dmg}의 추가 피해를 줍니다.", def.InlineDescription, "명세 '문법 원문' 그대로");
        Assert.IsTrue(TraitTooltip.Format(def, def.InlineDescription, null, false, TraitContent.Build(), out string text, out string error), error);
        StringAssert.StartsWith("Q가 끝나는 지점 앞쪽에 충격을 일으켜 ", Plain(text));
        StringAssert.EndsWith("의 추가 피해를 줍니다.", Plain(text));
        Assert.IsFalse(PaladinTraitValues.QBlastDamage(def, null).HasValue, "플레이어 없음 = 값 없음(화면 '-')");
    }

    [Test]
    public void Aliases_ReadEffectFields()
    {
        Assert.AreEqual(3f, PaladinTraitValues.EPushRadius(Load(PaladinTraitValues.EPush)), Eps);
        Assert.AreEqual(0.3f, PaladinTraitValues.QBlastRatio(Load(PaladinTraitValues.QBlast)), Eps);
        PaladinTraitDefinition healShield = Load(PaladinTraitValues.HealShield);
        Assert.AreEqual(0.75f, PaladinTraitValues.HealToShieldRatio(healShield), Eps);
        Assert.AreEqual(4f, PaladinTraitValues.ShieldDuration(healShield), Eps);
        Assert.AreEqual(0.1f, PaladinTraitValues.ShieldCapMaxHpRatio(healShield), Eps);
    }

    [Test]
    public void Definitions_Valid_NoOnceActions_SoRejoinRerunsNothing()
    {
        foreach (string id in new[] { PaladinTraitValues.EPush, PaladinTraitValues.QBlast, PaladinTraitValues.HealShield })
        {
            PaladinTraitDefinition def = Load(id);
            var errors = new List<string>();
            def.Validate(errors);
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.IsFalse(def.Effects.Any(e => e != null && e.HasOnceAction), $"{id} — 획득 1회 동작 없음(재접속·재선택에서 다시 실행할 것이 없다)");
        }
    }

    [Test]
    public void Reselect_RebindsOnce_NoDoubleRegistration()
    {
        foreach (string id in new[] { PaladinTraitValues.EPush, PaladinTraitValues.QBlast, PaladinTraitValues.HealShield })
        {
            PaladinTraitDefinition def = Load(id);
            var ledger = new TraitEffectLedger<List<ITraitEffectBinding>>();
            var host = new FakeHost();

            var first = new List<ITraitEffectBinding>();
            Assert.AreEqual(0, TraitEffectBinder.Bind(def.Effects, host, 1, def.IdHash, ledger, first));
            ledger.Add(1, def.IdHash, first);
            int modifiers = host.Modifiers.Count, derived = host.Derived.Count, subscribers = host.SubscriberCount;
            Assert.AreEqual(1, modifiers + derived + subscribers, $"{id} — 건 것 1개");

            var taken = new List<TraitEffectLedger<List<ITraitEffectBinding>>.Record>();
            ledger.TakeAll(1, taken);
            foreach (var record in taken) UnbindAll(record.Handle);
            Assert.AreEqual(0, host.Modifiers.Count + host.Derived.Count + host.SubscriberCount, $"{id} — 되돌리면 전부 해제");

            var again = new List<ITraitEffectBinding>();
            Assert.AreEqual(0, TraitEffectBinder.Bind(def.Effects, host, 1, def.IdHash, ledger, again), "재실행할 1회 동작 없음");
            Assert.AreEqual(1, host.Modifiers.Count + host.Derived.Count + host.SubscriberCount, $"{id} — 다시 1개만(이중 연결 없음)");
        }
    }

    // ── 4차 · 수호의 파동 ───────────────────────────────────────────────

    [Test]
    public void EPush_CounterHit_GetsPush_DamageKept_OncePerCastPerTarget()
    {
        PaladinTraitDefinition def = Load(PaladinTraitValues.EPush);
        var host = new FakeHost();
        List<ITraitEffectBinding> bindings = Bind(def, host);
        Assert.AreEqual(1, host.Modifiers.Count, "밀치기 수정자 1개");

        // 반격 피해 판정 1건(FirstMeleeSubSkill.ResolveCounterHit 과 같은 값) — 밀치기를 같이 싣고 피해는 그대로.
        AttackInfo counter = new AttackInfo(250, AttackType.SkillE);
        host.Modify(EKey, 1, 10, new Vector3(3f, 0f, 0f), ref counter);
        Assert.AreEqual(250, counter.damage, "원래 반격 피해 유지 — 추가 피해 없음");
        Assert.AreEqual(2f / 0.25f, counter.knockbackStrength, Eps, "밀기 속도 = 2m ÷ 0.25초");
        Assert.AreEqual(0.25f, counter.knockbackDuration, Eps);
        Assert.AreEqual(2f, counter.knockbackStrength * counter.knockbackDuration, Eps, "밀치는 거리 2m");
        Assert.AreEqual(Vector3.right, counter.knockbackDirection, "방향 = 플레이어 위치 → 대상(방사형)");
        Assert.AreEqual(0f, counter.staggerDuration, Eps, "경직 없음(명세) — 23호 취약 밀림은 넉백 세기만 본다(할 일 134)");
        Assert.AreEqual(0f, counter.concussionDuration, Eps, "기절 없음");

        // 같은 시전·같은 대상 = 다시 안 민다 → 피해 없는 밀치기 전용 판정은 건너뛴다(대상별 1회).
        AttackInfo pushOnly = new AttackInfo(0, AttackType.SkillE);
        host.Modify(EKey, 1, 10, Vector3.forward, ref pushOnly);
        Assert.AreEqual(0f, pushOnly.knockbackStrength, Eps, "시전당 대상별 1회 — 반격으로 이미 밀린 대상");

        // 부채꼴 밖 다른 대상 = 피해 없는 밀치기만
        AttackInfo outside = new AttackInfo(0, AttackType.SkillE);
        host.Modify(EKey, 1, 11, Vector3.back, ref outside);
        Assert.AreEqual(0, outside.damage, "반격 범위 밖 = 피해 없음");
        Assert.AreEqual(8f, outside.knockbackStrength, Eps, "그래도 반경 안이면 밀친다");
        Assert.AreEqual(Vector3.back, outside.knockbackDirection);

        AttackInfo nextCast = new AttackInfo(250, AttackType.SkillE);
        host.Modify(EKey, 2, 10, Vector3.right, ref nextCast);
        Assert.AreEqual(8f, nextCast.knockbackStrength, Eps, "다음 방어 성공 시전 = 다시 1회");

        foreach (TraitAttackKey key in new[] { QKey, new TraitAttackKey(AttackType.Default), TraitAttackKey.ForSkill(PlayerSkillSlot.Ultimate, false),
                     new TraitAttackKey(AttackType.Skill) })
        {
            AttackInfo notE = new AttackInfo(100, key.Type);
            host.Modify(key, 3, 10, Vector3.forward, ref notE);
            Assert.AreEqual(0f, notE.knockbackStrength, Eps, $"{key} — E 가 아닌 공격엔 없음");
        }

        UnbindAll(bindings);
        Assert.IsEmpty(host.Modifiers, "특성 없음(되돌림) = E 는 원래대로");
    }

    [Test]
    public void EPush_Radius_FromSkillValue_AndBoundary()
    {
        PaladinTraitDefinition def = Load(PaladinTraitValues.EPush);
        Assert.AreEqual(3f, SetOf(def).ResolveSkillValue(PaladinTraitValues.EPushRadiusKey, 0f), Eps, "E 가 읽는 반경 = 3m");
        Assert.AreEqual(0f, new TraitModifierSet().ResolveSkillValue(PaladinTraitValues.EPushRadiusKey, 0f), Eps, "특성 없음 = 0 = 밀치기 없음");

        Vector3 origin = new Vector3(1f, 0f, 1f);
        Assert.IsTrue(PaladinTraitRules.InsidePushRadius(origin, origin + new Vector3(3f, 0f, 0f), 3f), "경계 3m = 포함");
        Assert.IsTrue(PaladinTraitRules.InsidePushRadius(origin, origin + new Vector3(0f, 0f, -2.99f), 3f), "뒤쪽도(주변 원형)");
        Assert.IsFalse(PaladinTraitRules.InsidePushRadius(origin, origin + new Vector3(3.01f, 0f, 0f), 3f), "3m 밖 = 제외");
        Assert.IsTrue(PaladinTraitRules.InsidePushRadius(origin, origin + new Vector3(2f, 5f, 2f), 3f), "높이는 무시(바닥 평면)");
        Assert.IsFalse(PaladinTraitRules.InsidePushRadius(origin, origin, 0f), "반경 0 = 특성 없음");

        // 반격 부채꼴(120°·3m)보다 넓은 원 — 옆·뒤 적은 반격 피해 없이 밀치기만 받는다(별도 수집 필요).
        FirstMeleeSubSkillData e = LoadData<FirstMeleeSubSkillData>(EDataPath);
        Vector3 side = origin + new Vector3(0f, 0f, -2f);
        Assert.IsFalse(FirstMeleeGuardRules.IsInsideCounterSector(origin, Vector3.right, side, side, e.CounterRange, e.CounterAngle),
            "뒤쪽 2m = 반격 부채꼴 밖");
        Assert.IsTrue(PaladinTraitRules.InsidePushRadius(origin, side, 3f), "하지만 밀치기 반경 안");
    }

    [TestCase(MonsterState.Chase, false, MonsterRank.Normal, false, true, TestName = "EPush_Eligibility_NormalMonster_Pushed")]
    [TestCase(MonsterState.Chase, true, MonsterRank.Normal, false, false, TestName = "EPush_Eligibility_SuperArmor_DamageOnly")]
    [TestCase(MonsterState.Chase, false, MonsterRank.MidBoss, false, false, TestName = "EPush_Eligibility_BossRuleNormal_PushImmune")]
    [TestCase(MonsterState.Chase, false, MonsterRank.MidBoss, true, true, TestName = "EPush_Eligibility_BossRuleVulnerable_Pushed")]
    [TestCase(MonsterState.Groggy, false, MonsterRank.Normal, false, false, TestName = "EPush_Eligibility_Suppressed_NoMove")]
    public void EPush_ReceiverDecidesPush_DamageAlwaysKept(MonsterState state, bool armor, MonsterRank rank, bool vulnerable, bool pushed)
    {
        // 스킬은 보스 상태를 묻지 않고 싣는다 — 수신측(MonsterBase 넉백 진입)이 거부해도 같은 판정의 반격 피해는 그대로 들어간다.
        Assert.AreEqual(pushed, MonsterHitReactionPolicy.CanEnterKnockback(state, armor, rank, vulnerable));
        var host = new FakeHost();
        Bind(Load(PaladinTraitValues.EPush), host);
        AttackInfo info = new AttackInfo(250, AttackType.SkillE);
        host.Modify(EKey, 1, 10, Vector3.right, ref info);
        Assert.AreEqual(250, info.damage, "밀치기 면역이어도 반격 피해 정상");
    }

    [Test]
    public void EPush_WallImpact_OncePerKnockbackExecution()
    {
        // 수신측 벽 충돌 = 넉백 실행(에피소드) 하나당 1회. E 밀치기는 대상별 시전당 1회라 한 실행 = 한 번만 센다.
        var episode = new WallImpactEpisode();
        episode.Begin();
        Assert.IsTrue(episode.TryConsume(), "첫 벽 접촉 = 1회");
        Assert.IsFalse(episode.TryConsume(), "같은 밀치기에서 반복 접촉·코너 두 벽 = 추가 없음");
        episode.End();
        Assert.IsFalse(episode.TryConsume());
    }

    // ── 4차 · 진격의 충격 ───────────────────────────────────────────────

    [TestCase(SkillEndReason.Completed, 500, true, TestName = "QBlast_Fires_NormalEndOrTerrainStop")]
    [TestCase(SkillEndReason.Released, 500, true, TestName = "QBlast_Fires_HoldReleased")]
    [TestCase(SkillEndReason.MaxDurationReached, 500, true, TestName = "QBlast_Fires_FullHold")]
    [TestCase(SkillEndReason.Cancelled, 500, false, TestName = "QBlast_None_ForcedInterrupt")]
    [TestCase(SkillEndReason.DashCancelled, 500, false, TestName = "QBlast_None_DashCut")]
    [TestCase(SkillEndReason.CasterDied, 0, false, TestName = "QBlast_None_Died")]
    [TestCase(SkillEndReason.Released, 0, false, TestName = "QBlast_None_ZeroHp")]
    public void QBlast_EndRule(SkillEndReason reason, int hp, bool expected)
    {
        Assert.AreEqual(expected, PaladinTraitRules.QBlastFires(reason, hp));
    }

    [Test]
    public void QBlast_DerivedSpec_FromAsset_QOnly_NoCcNoProc()
    {
        PaladinTraitDefinition def = Load(PaladinTraitValues.QBlast);
        var host = new FakeHost();
        List<ITraitEffectBinding> bindings = Bind(def, host);

        Assert.IsEmpty(host.Modifiers, "파생 공격은 밀치기 수정자를 걸지 않는다(CC 없음)");
        Assert.AreEqual(0, host.SubscriberCount, "사건 구독 없음 — 파생이 다시 특성·패시브·쿨감을 만들지 않는다");
        List<TraitDerivedAttack> forQ = host.DerivedFor(QKey);
        Assert.AreEqual(1, forQ.Count);
        TraitDerivedAttack blast = forQ[0];
        Assert.AreEqual(PaladinTraitValues.QBlastKey, blast.Key);
        Assert.AreEqual(0.3f, blast.DamageRatio, Eps);
        Assert.AreEqual(2f, blast.Radius, Eps, "반경 2m");
        Assert.AreEqual(1f, blast.ForwardOffset, Eps, "전방 1m 꼭짓점");
        Assert.AreEqual(90f, blast.Angle, Eps, "90° 부채꼴(할 일 146)");
        Assert.AreEqual(0.05f, blast.Delay, Eps);
        Assert.IsTrue(blast.Counter.Tick() && blast.Counter.Tick(), "매 정상 종료마다(N번마다 아님)");

        foreach (TraitAttackKey key in new[] { EKey, new TraitAttackKey(AttackType.Default), TraitAttackKey.ForSkill(PlayerSkillSlot.Ultimate, false) })
            Assert.IsEmpty(host.DerivedFor(key), $"{key} — Q 가 아니면 없음");

        Assert.AreEqual(TraitHitFlags.None, TraitHitFlags.Derived & (TraitHitFlags.CanProcTraits | TraitHitFlags.CanGainResource |
                                                                    TraitHitFlags.CanApplyCC | TraitHitFlags.CanCounter),
            "파생 판정 = 특성·자원·CC·간파 없음");

        UnbindAll(bindings);
        Assert.IsEmpty(host.DerivedFor(QKey), "특성 없음(되돌림) = 충격 없음");
    }

    // 할 일 146 — 원 → 90° 부채꼴. 반경은 호출 쪽 구 질의, 여기선 각도만(대상 표면 기준, 꼭짓점이 콜라이더 안이면 중심 방향).
    [Test]
    public void QBlast_Sector_90Degrees_FrontOnly()
    {
        Vector3 apex = new Vector3(2f, 0f, 2f);
        Vector3 forward = Vector3.forward;
        Vector3 At(float yaw, float distance) => apex + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * distance;

        Assert.IsTrue(PaladinTraitRules.QBlastInSector(apex, forward, 90f, At(0f, 1.5f), At(0f, 1.5f)), "정면");
        Assert.IsTrue(PaladinTraitRules.QBlastInSector(apex, forward, 90f, At(44.5f, 1.5f), At(44.5f, 1.5f)), "반각 45° 안");
        Assert.IsFalse(PaladinTraitRules.QBlastInSector(apex, forward, 90f, At(46f, 1.5f), At(46f, 1.5f)), "반각 45° 밖");
        Assert.IsFalse(PaladinTraitRules.QBlastInSector(apex, forward, 90f, At(180f, 1f), At(180f, 1f)), "뒤 = 제외(예전 원은 포함)");
        Assert.IsTrue(PaladinTraitRules.QBlastInSector(apex, forward, 90f, At(0f, 1f) + Vector3.up * 3f, At(0f, 1f)), "높이 무시");
        Assert.IsTrue(PaladinTraitRules.QBlastInSector(apex, forward, 90f, apex, At(10f, 0.5f)), "꼭짓점을 덮은 대상 = 중심 방향");
        Assert.IsTrue(PaladinTraitRules.QBlastInSector(apex, forward, 360f, At(180f, 1f), At(180f, 1f)), "360 = 원(다른 파생 공격 기본값)");

        var spec = new TraitDerivedAttackEffect("X", 0.3f).CreateRuntime(1);
        Assert.AreEqual(360f, spec.Angle, Eps, "angle 칸 기본 = 원 — 진격의 충격 외 파생 공격은 그대로");
    }

    [Test]
    public void QBlast_Packet_IsPlannedTickTotal_IndependentOfHitsAndTargets()
    {
        FirstMeleeMainSkillData q = LoadData<FirstMeleeMainSkillData>(QDataPath);
        int ticks = PaladinTraitRules.PlannedHoldTicks(q.MaxActiveDuration, q.TickInterval);
        Assert.AreEqual(Mathf.FloorToInt(q.MaxActiveDuration / q.TickInterval + 1e-4f), ticks);
        Assert.AreEqual(12, PaladinTraitRules.PlannedHoldTicks(3f, 0.25f), "3초 ÷ 0.25초 = 12틱(부동소수 오차에 11 이 되지 않는다)");
        Assert.AreEqual(0, PaladinTraitRules.PlannedHoldTicks(3f, 0f), "틱 없는 데이터 = 0");

        Assert.AreEqual(25 * ticks, PaladinTraitRules.QPacketDamage(25, q.MaxActiveDuration, q.TickInterval), "틱 Dref × 예정 틱 수");
        Assert.AreEqual(int.MaxValue, PaladinTraitRules.QPacketDamage(int.MaxValue, 3f, 0.25f), "넘침 없음");
        Assert.AreEqual(0, PaladinTraitRules.QPacketDamage(-5, 3f, 0.25f));

        // 실제 맞힌 틱·적 수는 입력에 없다 — 빗나가도(0틱 적중)·적 3명이어도 1명에게 주는 충격 피해는 같다.
        var blast = Load(PaladinTraitValues.QBlast).Effects.OfType<TraitDerivedAttackEffect>().Single().CreateRuntime(1);
        int one = blast.DamageFrom(PaladinTraitRules.QPacketDamage(25, 3f, 0.25f));
        Assert.AreEqual(90, one, "기본 Q 합계 300 × 0.30 = 90(명세 문서 예시)");
    }

    [Test]
    public void QBlast_WithQDamage_Section10_DoesNotMultiplyTwice()
    {
        // §10 진격 강화+진격의 충격(돌파의 방패 없이): 기본 Q 합계 300 × 1.20 = 원래 360, 추가 = 360 × 0.30 = 108.
        PaladinTraitDefinition qDamage = Load(PaladinTraitValues.QDamage);
        PaladinTraitDefinition qBlast = Load(PaladinTraitValues.QBlast);
        TraitModifierSet set = SetOf(qDamage, qBlast);
        float multiplier = set.DamageMultiplier(QKey);
        Assert.AreEqual(1.2f, multiplier, Eps, "진격의 충격은 Q 피해 배율에 기여하지 않는다");

        // 틱 Dref = 판정 스냅샷 식(공격력 100 × 계수 0.25 = 틱 25, 12틱 = 300) × Q 특성 배율 — Player.ResolveDamage 와 같은 식.
        int tickDref = PlayerStatFormula.Snapshot(100, 0.25f, 0, 0f, multiplier);
        int packet = PaladinTraitRules.QPacketDamage(tickDref, 3f, 0.25f);
        Assert.AreEqual(360, packet, "원래 Q 묶음 = 300 × 1.2");
        TraitDerivedAttack blast = qBlast.Effects.OfType<TraitDerivedAttackEffect>().Single().CreateRuntime(qBlast.IdHash);
        Assert.AreEqual(108, blast.DamageFrom(packet), "추가 = 원래 묶음 × 0.3 — 진격 강화를 다시 곱하지 않는다(× 1.2 한 번)");
        Assert.AreEqual(468, packet + blast.DamageFrom(packet), "단일 대상 원래 + 추가 총합 = 300 × 1.2 × 1.3");

        Assert.AreEqual(90, blast.DamageFrom(PaladinTraitRules.QPacketDamage(PlayerStatFormula.Snapshot(100, 0.25f, 0, 0f), 3f, 0.25f)),
            "단독(진격 강화 없음) = 300 × 0.3");
    }

    // ── 4차 · 불굴의 가호 ───────────────────────────────────────────────

    [Test]
    public void HealShield_Section10_SelfAndAlly_EachActualHeal()
    {
        // §10 불굴의 회복+나누는 불굴+불굴의 가호 — 최대HP 1000, 원래 회복 50 → 63. 자신 결손 20 → 실회복 20, 초과 43 → 아군 실회복 43.
        int requested = TraitHealRules.Request(50, SetOf(Load(PaladinTraitValues.PassiveHeal)).HealMultiplier);
        Assert.AreEqual(63, requested);
        int selfActual = Math.Min(requested, 20);
        int overflow = TraitHealRules.Overflow(requested, selfActual);
        int allyActual = TraitHealRules.Transfer(overflow, 1f);
        Assert.AreEqual(43, allyActual);

        PaladinTraitDefinition def = Load(PaladinTraitValues.HealShield);
        var (self, ally, objects) = TwoUnits();
        try
        {
            var host = new FakeHost();
            List<ITraitEffectBinding> bindings = Bind(def, host);
            // PlayerTraitEffects.HealServer 순서: 자신 실제 A → Healed(자신) → 초과 전달 → 아군 실제 B → Healed(아군)
            host.Raise(Healed(self, selfActual));
            host.Raise(Healed(ally, allyActual));
            Assert.AreEqual(15, host.Book.Of(self, def.IdHash), "자신 20 × 0.75");
            Assert.AreEqual(32, host.Book.Of(ally, def.IdHash), "아군 43 × 0.75 = 32.25 → 32");
            Assert.AreEqual(4f, host.Book.Duration[(self, TraitShieldRules.ChannelSource(def.IdHash))], Eps, "4초");
            Assert.AreEqual(2, host.Raised, "보호막 지급이 새 회복·패시브 사건을 내지 않는다(연쇄 없음)");

            UnbindAll(bindings);
            host.Raise(Healed(self, 20));
            Assert.AreEqual(2, host.Book.Grants, "특성 없음(되돌림) = 보호막 없음");
        }
        finally
        {
            DestroyAll(objects);
        }
    }

    [Test]
    public void HealShield_Cap_ZeroHeal_SameChannelLarger_NoStacking_OtherChannelKept()
    {
        PaladinTraitDefinition def = Load(PaladinTraitValues.HealShield);
        var (self, ally, objects) = TwoUnits();
        try
        {
            var book = new ShieldBook();
            var paladinA = new FakeHost(book);
            var paladinB = new FakeHost(book);
            paladinA.MaxHp[ally] = 500;
            paladinB.MaxHp[ally] = 500;
            Bind(def, paladinA);
            Bind(def, paladinB);

            paladinA.Raise(Healed(self, 400));
            Assert.AreEqual(100, book.Of(self, def.IdHash), "400 × 0.75 = 300 → 대상 최대 체력 1000 의 10% 상한 100");
            paladinA.Raise(Healed(ally, 400));
            Assert.AreEqual(50, book.Of(ally, def.IdHash), "상한은 받는 대상 최대 체력 기준(500 × 10%)");

            int grants = book.Grants;
            paladinA.Raise(Healed(self, 0));
            paladinA.Raise(Healed(null, 40));
            Assert.AreEqual(grants, book.Grants, "실회복 0·대상 없음 = 보호막 없음");

            // 같은 대상에 두 팔라딘 — 같은 특성 채널이라 큰 값만(합산 아님), 시간은 새로
            paladinB.Raise(Healed(ally, 20));
            Assert.AreEqual(50, book.Of(ally, def.IdHash), "다른 팔라딘의 작은 보호막(15) = 기존 50 유지, 합산 65 아님");
            paladinB.Raise(Healed(self, 40));
            Assert.AreEqual(100, book.Of(self, def.IdHash), "기존 100 > 30 → 100 유지");

            // 다른 보호막 채널(수호의 잔광)은 건드리지 않는다
            int afterglowHash = TraitIdHash.Of(PaladinTraitValues.EShield);
            Assert.AreNotEqual(TraitShieldRules.ChannelSource(afterglowHash), TraitShieldRules.ChannelSource(def.IdHash), "다른 채널");
            book.Amount[(self, TraitShieldRules.ChannelSource(afterglowHash))] = 100;
            book.Amount[(self, TraitShieldRules.ChannelSource(def.IdHash))] = 5;
            paladinA.Raise(Healed(self, 20));
            Assert.AreEqual(15, book.Of(self, def.IdHash), "작아진 잔량 5 < 15 → 새 양");
            Assert.AreEqual(100, book.Of(self, afterglowHash), "수호의 잔광 채널 그대로");
        }
        finally
        {
            DestroyAll(objects);
        }
    }

    [Test]
    public void HealShield_OnlyHealedEvents_NotPassiveOrGuardOrQEnd()
    {
        PaladinTraitDefinition def = Load(PaladinTraitValues.HealShield);
        var (self, _, objects) = TwoUnits();
        try
        {
            var host = new FakeHost();
            Bind(def, host);
            host.Raise(new TraitTriggerEvent(TraitTrigger.PassiveProc, AttackType.None, TraitAttackForm.Normal, 0, false, 0, self, 50));
            host.Raise(new TraitTriggerEvent(TraitTrigger.GuardSucceeded, AttackType.SkillE, TraitAttackForm.Normal, 1, true, 0, self, 50));
            host.Raise(new TraitTriggerEvent(TraitTrigger.SkillCompleted, AttackType.SkillE, TraitAttackForm.Normal, 1, true, 0, self, 50));
            host.Raise(new TraitTriggerEvent(TraitTrigger.AttackLanded, AttackType.SkillQ, TraitAttackForm.Normal, 2, true, 1, self, 50));
            Assert.AreEqual(0, host.Book.Grants, "실제 회복 사건만 — 패시브 발동·방어·스킬 종료·적중은 아님");

            host.Raise(Healed(self, 20));
            host.Raise(Healed(self, 20));
            Assert.AreEqual(2, host.Book.Grants, "회복마다(시전 ID 0 = 매번 새 사건) — 같은 채널이라 잔량은 큰 값");
            Assert.AreEqual(15, host.Book.Of(self, def.IdHash));
        }
        finally
        {
            DestroyAll(objects);
        }
    }
}
