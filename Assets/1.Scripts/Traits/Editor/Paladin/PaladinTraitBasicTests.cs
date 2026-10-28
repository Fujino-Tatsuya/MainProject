using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// 할 일 126 (1/4) — 팔라딘 단순 특성 5개(진격 강화·수호 재정비·불굴의 회복·수호의 순환·나누는 불굴)의 수용 조건(명세 §11)을
/// 실제 정의 에셋 값으로 확인한다: 단독 적용/미적용 비교 · 방어 실패 쿨감 없음 · 시전당 1회 · 0 아래 없음·저장 없음 ·
/// 회복 초과/동률/무대상/연쇄 없음 · 중복 방지 · 재선택 · 재접속 재실행 없음 · §10 검산(제압 남은 0초, 50→63 자신 20·아군 43).
/// </summary>
public sealed class PaladinTraitBasicTests
{
    const float Eps = 1e-4f;
    const string Folder = "Assets/9.ScriptableObject/Traits/Paladin/";
    const string ERefundEDataPath = "Assets/9.ScriptableObject/Player/Garen/FirstMeleeSubSkillData.asset";

    // ── 도우미 ───────────────────────────────────────────────────────────

    sealed class FakeHost : ITraitEffectHost
    {
        public double Time;
        public readonly Dictionary<PlayerSkillSlot, float> Remaining = new Dictionary<PlayerSkillSlot, float>();
        public readonly List<TraitOverflowShare> Shares = new List<TraitOverflowShare>();

        public double Now => Time;
        public event Action<TraitTriggerEvent> Triggered;
        public int SubscriberCount => Triggered?.GetInvocationList().Length ?? 0;

        public void Raise(TraitTriggerEvent e) => Triggered?.Invoke(e);

        public float GetCooldownRemaining(PlayerSkillSlot slot) => Remaining.TryGetValue(slot, out float r) ? r : 0f;

        public void ReduceCooldown(PlayerSkillSlot slot, float seconds) =>
            Remaining[slot] = TraitCooldownRefundRules.Reduce(GetCooldownRemaining(slot), seconds);

        public int MaxHpOf(Unit target) => 1000;
        public void GrantTraitShield(Unit target, int traitHash, int amount, float duration) { }
        public TraitNextAttackBuffs NextAttackBuffs { get; } = new TraitNextAttackBuffs();
        public void AddAttackModifier(ITraitAttackModifier modifier) { }
        public void RemoveAttackModifier(ITraitAttackModifier modifier) { }
        public void AddDerivedAttack(TraitDerivedAttack attack) { }
        public void RemoveDerivedAttack(TraitDerivedAttack attack) { }
        public void AddOverflowShare(TraitOverflowShare share) => Shares.Add(share);
        public void RemoveOverflowShare(TraitOverflowShare share) => Shares.Remove(share);
    }

    static PaladinTraitDefinition Load(string id)
    {
        var def = AssetDatabase.LoadAssetAtPath<PaladinTraitDefinition>(Folder + id + ".asset");
        Assert.IsNotNull(def, $"{Folder}{id}.asset(PaladinTraitDefinition)이 있어야 한다");
        Assert.AreEqual(id, def.Id);
        return def;
    }

    /// <summary>획득한 정의들의 수치 합성(같은 정의 두 번 = 한 번).</summary>
    static TraitModifierSet SetOf(params TraitDefinition[] defs)
    {
        var set = new TraitModifierSet();
        set.Rebuild(defs.Select(d => d.IdHash).ToList(), h => defs.FirstOrDefault(d => d.IdHash == h));
        Assert.IsEmpty(set.Errors, string.Join("\n", set.Errors));
        return set;
    }

    static List<ITraitEffectBinding> Bind(TraitDefinition def, FakeHost host)
    {
        var bindings = new List<ITraitEffectBinding>();
        TraitEffectBinder.Bind(def.Effects, host, 1, def.IdHash, (TraitEffectLedger<int>)null, bindings);
        return bindings;
    }

    static TraitTriggerEvent Guard(int castId) =>
        new TraitTriggerEvent(TraitTrigger.GuardSucceeded, AttackType.SkillE, TraitAttackForm.Normal, castId);

    static string Plain(string text) => Regex.Replace(text ?? string.Empty, "<[^>]+>", string.Empty);

    // ── 풀·카드 ─────────────────────────────────────────────────────────

    [Test]
    public void Pool_HasImplementedTraits_InSpecOrder()
    {
        TraitCharacterPool pool = TraitContentTestKit.CharacterPool(TutorialCharacter.Paladin);
        Assert.IsNotNull(pool);
        string[] specOrder = { "PAL_1_Q_DAMAGE", "PAL_1_E_COOLDOWN", "PAL_1_PASSIVE_HEAL", "PAL_2_DASH", "PAL_2_E_REFUND", "PAL_2_SHARE_HEAL",
            "PAL_3_Q_STUN", "PAL_3_E_SHIELD", "PAL_3_PASSIVE_CDR", "PAL_4_E_PUSH", "PAL_4_Q_BLAST", "PAL_4_HEAL_SHIELD" };
        List<string> ids = pool.Traits.Select(t => t.Id).ToList();
        foreach (string id in new[] { PaladinTraitValues.QDamage, PaladinTraitValues.ECooldown, PaladinTraitValues.PassiveHeal,
                     PaladinTraitValues.ERefund, PaladinTraitValues.ShareHeal })
            CollectionAssert.Contains(ids, id);

        List<int> order = ids.Select(id => Array.IndexOf(specOrder, id)).ToList();
        CollectionAssert.DoesNotContain(order, -1, "팔라딘 풀에는 명세 팔라딘 ID 만");
        CollectionAssert.IsOrdered(order, "명세 순서(순서 = 목록 버전)");
    }

    [TestCase("PAL_1_Q_DAMAGE", "진격 강화", "Q 강화", "Q 피해가 20% 증가합니다.")]
    [TestCase("PAL_1_E_COOLDOWN", "수호 재정비", "E 강화", "E 재사용 대기시간이 15% 감소합니다.")]
    [TestCase("PAL_1_PASSIVE_HEAL", "불굴의 회복", "패시브 강화", "패시브의 체력 회복량이 25% 증가합니다.")]
    [TestCase("PAL_2_E_REFUND", "수호의 순환", "E 강화", "E로 공격을 막으면 남은 E 재사용 대기시간이 2초 줄어듭니다.")]
    [TestCase("PAL_2_SHARE_HEAL", "나누는 불굴", "패시브 변경 · 아군 회복",
        "패시브로 회복하고 남은 양으로 12m 안에서 남은 체력 비율이 가장 낮은 아군 한 명을 회복합니다.")]
    public void Card_MatchesSpecSelectionText(string id, string name, string category, string expected)
    {
        PaladinTraitDefinition def = Load(id);
        Assert.AreEqual(name, def.InlineDisplayName);
        Assert.AreEqual(category, def.InlineCategory);
        Assert.IsTrue(TraitTooltip.Format(def, def.InlineDescription, null, false, TraitContent.Build(), out string text, out string error), error);
        Assert.AreEqual(expected, Plain(text), "명세 '선택 화면' 문구");
    }

    [Test]
    public void Definitions_HaveNoOnceActions_SoRejoinRerunsNothing()
    {
        foreach (string id in new[] { PaladinTraitValues.QDamage, PaladinTraitValues.ECooldown, PaladinTraitValues.PassiveHeal,
                     PaladinTraitValues.ERefund, PaladinTraitValues.ShareHeal })
        {
            PaladinTraitDefinition def = Load(id);
            var errors = new List<string>();
            def.Validate(errors);
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.IsFalse(def.Effects.Any(e => e != null && e.HasOnceAction), $"{id} — 획득 1회 동작 없음(재접속·재선택에서 다시 실행할 것이 없다)");
        }
    }

    // ── 1차 · 진격 강화 ─────────────────────────────────────────────────

    [Test]
    public void QDamage_MultipliesOnlyQ_ByOnePointTwo_AndDedupes()
    {
        PaladinTraitDefinition def = Load(PaladinTraitValues.QDamage);
        TraitModifierSet none = SetOf();
        TraitModifierSet with = SetOf(def);
        TraitModifierSet twice = SetOf(def, def);

        Assert.AreEqual(1f, none.DamageMultiplier(TraitAttackKey.ForSkill(PlayerSkillSlot.Main, false)), Eps, "미적용");
        Assert.AreEqual(1.2f, with.DamageMultiplier(TraitAttackKey.ForSkill(PlayerSkillSlot.Main, false)), Eps, "Q ×1.2");
        Assert.AreEqual(1.2f, twice.DamageMultiplier(TraitAttackKey.ForSkill(PlayerSkillSlot.Main, false)), Eps, "같은 ID 중복 적용 없음");
        Assert.AreEqual(1f, with.DamageMultiplier(new TraitAttackKey(AttackType.Default)), Eps, "평타 무관");
        Assert.AreEqual(1f, with.DamageMultiplier(TraitAttackKey.ForSkill(PlayerSkillSlot.Sub, false)), Eps, "E 무관");
        Assert.AreEqual(1f, with.DamageMultiplier(TraitAttackKey.ForSkill(PlayerSkillSlot.Ultimate, false)), Eps, "R 무관");
        Assert.AreEqual(1f, with.DamageMultiplier(new TraitAttackKey(AttackType.None)), Eps, "패시브 추가피해(계열 미지정) 무관");
        Assert.AreEqual(360, TraitHealRules.RoundHalfUp(300 * with.DamageMultiplier(TraitAttackKey.ForSkill(PlayerSkillSlot.Main, false))),
            "Q 기준 300 → 360(명세 §10 첫 곱)");
    }

    // ── 1차 · 수호 재정비 ───────────────────────────────────────────────

    [Test]
    public void ECooldown_SevenSecondsBecomesFiveNinetyFive_OnlyE_ProgressKept()
    {
        PaladinTraitDefinition def = Load(PaladinTraitValues.ECooldown);
        TraitModifierSet with = SetOf(def);

        Assert.AreEqual(1f, SetOf().CooldownMultiplier(PlayerSkillSlot.Sub, TraitAttackForm.Normal), Eps, "미적용");
        Assert.AreEqual(5.95f, 7f * with.CooldownMultiplier(PlayerSkillSlot.Sub, TraitAttackForm.Normal), Eps, "기본 E 7초 × 0.85");
        Assert.AreEqual(1f, with.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal), Eps, "Q 무관");
        Assert.AreEqual(1f, with.CooldownMultiplier(PlayerSkillSlot.Ultimate, TraitAttackForm.Normal), Eps, "R 무관");
        Assert.AreEqual(0.85f, SetOf(def, def).CooldownMultiplier(PlayerSkillSlot.Sub, TraitAttackForm.Normal), Eps, "같은 ID 중복 적용 없음");
        Assert.IsTrue(def.AffectsCooldownOf(PlayerSkillSlot.Sub));
        // 선택 때 돌던 쿨은 진행률 보존(명세 §6): 7초 중 3.5 남음 → 5.95 중 2.975.
        Assert.AreEqual(2.975f, TraitModifierRules.RescaleRemaining(3.5f, 7f, 5.95f), Eps);
        Assert.AreEqual(0.15f, PaladinTraitValues.CooldownReduction(def), Eps, "카드 별칭 cooldownReduction = −cooldownChange");
    }

    // ── 1차 · 불굴의 회복 ───────────────────────────────────────────────

    [Test]
    public void PassiveHeal_RequestTimesOnePointTwoFive()
    {
        PaladinTraitDefinition def = Load(PaladinTraitValues.PassiveHeal);
        Assert.AreEqual(1f, SetOf().HealMultiplier, Eps, "미적용");
        Assert.AreEqual(1.25f, SetOf(def).HealMultiplier, Eps);
        Assert.AreEqual(1.25f, SetOf(def, def).HealMultiplier, Eps, "같은 ID 중복 적용 없음");
        Assert.AreEqual(50, TraitHealRules.Request(50, SetOf().HealMultiplier), "미적용 50");
        Assert.AreEqual(63, TraitHealRules.Request(50, SetOf(def).HealMultiplier), "50 × 1.25 = 62.5 → 63(명세 §10)");
        var host = new FakeHost();
        Assert.IsEmpty(Bind(def, host), "회복 배율만 — 초과 전달 없음");
        Assert.IsEmpty(host.Shares);
    }

    // ── 2차 · 수호의 순환 ───────────────────────────────────────────────

    [Test]
    public void ERefund_GuardSuccess_MinusTwo_OncePerCast_NeverBelowZero_NoStorage()
    {
        PaladinTraitDefinition def = Load(PaladinTraitValues.ERefund);
        var host = new FakeHost();
        host.Remaining[PlayerSkillSlot.Sub] = 4f;
        host.Remaining[PlayerSkillSlot.Main] = 4f;
        List<ITraitEffectBinding> bindings = Bind(def, host);

        // 방어 실패 = 사건 없음 · 반격 적중(AttackLanded) 은 방어 성공이 아니다
        host.Raise(new TraitTriggerEvent(TraitTrigger.AttackLanded, AttackType.SkillE, TraitAttackForm.Normal, 1, true, 1));
        Assert.AreEqual(4f, host.Remaining[PlayerSkillSlot.Sub], Eps, "방어 실패·반격 적중 쿨감 없음");

        host.Raise(Guard(1));
        Assert.AreEqual(2f, host.Remaining[PlayerSkillSlot.Sub], Eps, "방어 성공 −2");
        Assert.AreEqual(4f, host.Remaining[PlayerSkillSlot.Main], Eps, "Q 무관");
        host.Raise(Guard(1));
        Assert.AreEqual(2f, host.Remaining[PlayerSkillSlot.Sub], Eps, "같은 시전 두 번째 방어 = 중복 차감 없음");

        host.Remaining[PlayerSkillSlot.Sub] = 1f;
        host.Raise(Guard(2));
        Assert.AreEqual(0f, host.Remaining[PlayerSkillSlot.Sub], Eps, "max(0, 1 − 2)");

        host.Raise(Guard(3));
        host.Remaining[PlayerSkillSlot.Sub] = 5f;
        Assert.AreEqual(5f, host.Remaining[PlayerSkillSlot.Sub], Eps, "준비 상태 차감은 다음 E 에 저장되지 않는다");

        foreach (ITraitEffectBinding b in bindings) b.Unbind();
        host.Raise(Guard(4));
        Assert.AreEqual(5f, host.Remaining[PlayerSkillSlot.Sub], Eps, "특성 없음(되돌림) = 차감 없음");
    }

    [Test]
    public void ERefund_Reselect_RebindsOnce_NoDoubleRefund()
    {
        PaladinTraitDefinition def = Load(PaladinTraitValues.ERefund);
        var ledger = new TraitEffectLedger<List<ITraitEffectBinding>>();
        var host = new FakeHost();
        host.Remaining[PlayerSkillSlot.Sub] = 5f;

        var first = new List<ITraitEffectBinding>();
        Assert.AreEqual(0, TraitEffectBinder.Bind(def.Effects, host, 1, def.IdHash, ledger, first));
        ledger.Add(1, def.IdHash, first);

        // 재선택 [예] — 되돌리고 같은 특성을 다시 고른다
        var taken = new List<TraitEffectLedger<List<ITraitEffectBinding>>.Record>();
        ledger.TakeAll(1, taken);
        foreach (var record in taken)
            foreach (ITraitEffectBinding b in record.Handle) b.Unbind();
        var again = new List<ITraitEffectBinding>();
        Assert.AreEqual(0, TraitEffectBinder.Bind(def.Effects, host, 1, def.IdHash, ledger, again), "재실행할 1회 동작 없음");
        ledger.Add(1, def.IdHash, again);

        Assert.AreEqual(1, host.SubscriberCount, "구독 1개만(이중 연결 없음)");
        host.Raise(Guard(10));
        Assert.AreEqual(3f, host.Remaining[PlayerSkillSlot.Sub], Eps, "한 번만 −2");
    }

    [Test]
    public void ECooldown_ERefund_Suppress_Example_EndsAtZero()
    {
        // 명세 §10: 기본 E 7초 → 전체 5.95초. 남은 4초에서 방어 성공 −2, 이후 제압 진입 −(전체 5.95 × 50%) = −2.975 → 남은 0초.
        TraitModifierSet set = SetOf(Load(PaladinTraitValues.ECooldown), Load(PaladinTraitValues.ERefund));
        float full = 7f * set.CooldownMultiplier(PlayerSkillSlot.Sub, TraitAttackForm.Normal);
        Assert.AreEqual(5.95f, full, Eps);

        var host = new FakeHost();
        host.Remaining[PlayerSkillSlot.Sub] = 4f;
        Bind(Load(PaladinTraitValues.ERefund), host);
        host.Raise(Guard(1));
        Assert.AreEqual(2f, host.Remaining[PlayerSkillSlot.Sub], Eps, "고정 차감이 제압 차감보다 먼저(명세 §6 틱 순서)");

        float suppressEntry = full * 0.5f;
        Assert.AreEqual(2.975f, suppressEntry, Eps, "제압 차감은 전체 시간의 절반 — 남은 시간의 절반이 아님");
        Assert.AreEqual(0f, TraitCooldownRefundRules.Reduce(host.Remaining[PlayerSkillSlot.Sub], suppressEntry), Eps);
    }

    [Test]
    public void ERefund_PaladinE_CommitsCooldownOnGuardEntry()
    {
        // 수호의 순환 전제(명세 321~341): E 쿨이 방어 진입(승인) 순간 시작해야 성공 시 남은 시간에서 뺄 수 있다.
        // 수동 커밋(commitCooldownManually)이면 종료 쪽에서 시작될 수 있으니 자동 커밋이어야 한다.
        var data = AssetDatabase.LoadAssetAtPath<FirstMeleeSubSkillData>(ERefundEDataPath);
        Assert.IsNotNull(data, ERefundEDataPath);
        Assert.IsFalse(data.CommitCooldownManually, "팔라딘 E = 승인 즉시 쿨 커밋");
    }

    // ── 2차 · 나누는 불굴 ───────────────────────────────────────────────

    /// <summary>
    /// <see cref="PlayerTraitEffects.HealServer"/> 와 같은 순서를 공용 순수 규칙으로 밟는다(H → 자신 A → O → 아군 1명 B, 재전달 없음).
    /// allies[i] = (id, hp, maxHp, 거리 m, 자격). 반환 = (자신 실회복, 고른 아군 칸 −1 = 없음, 아군 실회복).
    /// </summary>
    static (int self, int pick, int ally) SimulatePassiveHeal(int baseAmount, float multiplier, int selfHp, int selfMax,
        TraitOverflowShare share, params (ulong id, int hp, int max, float distance, bool eligible)[] allies)
    {
        int requested = TraitHealRules.Request(baseAmount, multiplier);
        int self = Math.Min(requested, Math.Max(0, selfMax - selfHp));
        int overflow = TraitHealRules.Overflow(requested, self);
        if (overflow <= 0 || share == null)
            return (self, -1, 0);

        int transfer = TraitHealRules.Transfer(overflow, share.TransferRatio);
        var candidates = allies.Select(a => new TraitHealCandidate(a.id, a.hp, a.max, a.distance * a.distance, a.eligible)).ToList();
        int pick = TraitHealRules.PickOverflowRecipient(candidates, share.Range);
        if (pick < 0 || transfer <= 0)
            return (self, -1, 0);

        return (self, pick, Math.Min(transfer, allies[pick].max - allies[pick].hp));
    }

    static TraitOverflowShare ShareOf(TraitDefinition def)
    {
        var host = new FakeHost();
        Assert.AreEqual(1, Bind(def, host).Count, "초과 전달 등록 1개");
        Assert.AreEqual(1, host.Shares.Count);
        return host.Shares[0];
    }

    [Test]
    public void ShareHeal_Binds_TwelveMeters_FullTransfer_NoHealBonus()
    {
        PaladinTraitDefinition def = Load(PaladinTraitValues.ShareHeal);
        TraitOverflowShare share = ShareOf(def);
        Assert.AreEqual(12f, share.Range, Eps);
        Assert.AreEqual(1f, share.TransferRatio, Eps);
        Assert.AreEqual(1f, SetOf(def).HealMultiplier, Eps, "회복량 자체는 늘리지 않는다");
        Assert.AreEqual(12f, PaladinTraitValues.AllyRange(def), Eps, "카드 별칭 allyRange = overflowShareRange");

        var host = new FakeHost();
        List<ITraitEffectBinding> bindings = Bind(def, host);
        foreach (ITraitEffectBinding b in bindings) b.Unbind();
        Assert.IsEmpty(host.Shares, "되돌리면 전달 해제");
    }

    [Test]
    public void ShareHeal_PicksLowestRatio_TieLostHp_ThenId_RangeAndEligibility()
    {
        float range = ShareOf(Load(PaladinTraitValues.ShareHeal)).Range;
        int Pick(params TraitHealCandidate[] c) => TraitHealRules.PickOverflowRecipient(c, range);
        static TraitHealCandidate C(ulong id, int hp, int max, float meters, bool ok = true) => new TraitHealCandidate(id, hp, max, meters * meters, ok);

        Assert.AreEqual(1, Pick(C(1, 600, 1000, 5), C(2, 400, 1000, 5), C(3, 800, 1000, 5)), "HP 비율 최소");
        Assert.AreEqual(1, Pick(C(1, 500, 1000, 5), C(2, 1000, 2000, 5)), "비율 동률(50%) → 잃은 체력 큰 쪽(1000 > 500)");
        Assert.AreEqual(0, Pick(C(3, 500, 1000, 5), C(7, 500, 1000, 5)), "완전 동률 → 작은 플레이어 ID");
        Assert.AreEqual(1, Pick(C(7, 500, 1000, 5), C(3, 500, 1000, 5)), "목록 순서 무관 — ID 순");
        Assert.AreEqual(0, Pick(C(1, 900, 1000, 12f), C(2, 100, 1000, 12.01f)), "12m 경계 안만");
        Assert.AreEqual(1, Pick(C(1, 1000, 1000, 1), C(2, 999, 1000, 1)), "체력 가득은 제외");
        Assert.AreEqual(1, Pick(C(1, 100, 1000, 1, ok: false), C(2, 900, 1000, 1)), "쓰러짐·비활성(자격 없음) 제외");
        Assert.AreEqual(1, Pick(C(1, 0, 1000, 1), C(2, 900, 1000, 1)), "사망(HP 0) 제외");
        Assert.AreEqual(-1, Pick(C(1, 1000, 1000, 1), C(2, 100, 1000, 20)), "유효 대상 없음 = 버림");
        Assert.AreEqual(-1, Pick(), "아군 없음(솔로) = 버림");
    }

    [Test]
    public void PassiveHeal_ShareHeal_Example_Fifty_To_SixtyThree_Self20_Ally43()
    {
        // 명세 §10: 최대 HP 1000, 원래 회복 50 → 63. 자신 결손 20 → 실회복 20, 초과 43 → 아군 실회복 43.
        PaladinTraitDefinition heal = Load(PaladinTraitValues.PassiveHeal);
        PaladinTraitDefinition shareDef = Load(PaladinTraitValues.ShareHeal);
        float multiplier = SetOf(heal, shareDef).HealMultiplier;
        TraitOverflowShare share = ShareOf(shareDef);

        var result = SimulatePassiveHeal(50, multiplier, 980, 1000, share,
            (5, 700, 1000, 6f, true), (2, 500, 1000, 8f, true), (9, 100, 1000, 30f, true));
        Assert.AreEqual(20, result.self);
        Assert.AreEqual(1, result.pick, "12m 안 비율 최소(30m 밖 10% 아군은 제외)");
        Assert.AreEqual(43, result.ally);

        // 미적용 비교: 나누는 불굴 없음 = 초과 43 은 버린다 / 불굴의 회복 없음 = 요청 50 → 초과 30.
        Assert.AreEqual((20, -1, 0), SimulatePassiveHeal(50, multiplier, 980, 1000, null, (2, 500, 1000, 8f, true)));
        Assert.AreEqual((20, 0, 30), SimulatePassiveHeal(50, SetOf(shareDef).HealMultiplier, 980, 1000, share, (2, 500, 1000, 8f, true)));
    }

    [Test]
    public void ShareHeal_NoOverflow_NoTarget_NoChain()
    {
        TraitOverflowShare share = ShareOf(Load(PaladinTraitValues.ShareHeal));

        Assert.AreEqual((50, -1, 0), SimulatePassiveHeal(50, 1f, 500, 1000, share, (2, 100, 1000, 1f, true)), "초과 없음 = 전달 없음");
        Assert.AreEqual((0, -1, 0), SimulatePassiveHeal(50, 1f, 1000, 1000, share), "가득 찬 자신 + 무대상 = 전부 버림");

        // 아군도 거의 가득 — 받은 뒤 남은 양(40 − 10 = 30)은 재전달·다른 아군으로 넘기지 않는다(한 명에게 한 번).
        var chain = SimulatePassiveHeal(50, 1f, 990, 1000, share, (2, 990, 1000, 1f, true), (3, 995, 1000, 1f, true));
        Assert.AreEqual(10, chain.self);
        Assert.AreEqual(0, chain.pick, "비율 최소 아군 1명");
        Assert.AreEqual(10, chain.ally, "그 아군 실회복만 — 나머지 30 은 버림");
    }
}
