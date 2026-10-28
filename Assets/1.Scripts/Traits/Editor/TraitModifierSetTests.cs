using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 125 (2/5) — 특성 수치 출처 순수 합성(<see cref="TraitModifierSet"/>): 빈 목록 = 전부 1·배율 곱/스탯 % 합·페널티 유지·형태/슬롯 거름·
/// 같은 ID 1회·잘못된 값 거부·서버/클라 같은 입력 = 같은 값·획득 목록 변경 시 재합성·PlayerStats/Dref 연결·TraitDefinition 기여.
/// </summary>
public sealed class TraitModifierSetTests
{
    const float Eps = 1e-5f;

    // 해시 → 기여 표(테스트용 정의 대역)
    sealed class FakeContributor : ITraitModifierContributor
    {
        readonly TraitModifier[] modifiers;
        public FakeContributor(params TraitModifier[] modifiers) => this.modifiers = modifiers;
        public void GetModifiers(List<TraitModifier> result) => result.AddRange(modifiers);
    }

    static Func<int, ITraitModifierContributor> Table(Dictionary<int, ITraitModifierContributor> table) =>
        hash => table.TryGetValue(hash, out ITraitModifierContributor c) ? c : null;

    static readonly int QMask = TraitModifier.MaskOf(AttackType.SkillQ);
    static readonly int EMask = TraitModifier.MaskOf(AttackType.SkillE);
    static readonly int NormalForm = TraitModifier.MaskOf(TraitAttackForm.Normal);
    static readonly int MainSlot = TraitModifier.MaskOf(PlayerSkillSlot.Main);

    // ── 빈 목록 ─────────────────────────────────────────────────────────

    [Test]
    public void Empty_EverythingIsOne_AndPercentsZero()
    {
        var set = new TraitModifierSet();
        Assert.IsTrue(set.Rebuild(Array.Empty<int>(), null), "처음 합성은 빈 목록이라도 한다");

        foreach (AttackType type in Enum.GetValues(typeof(AttackType)))
        foreach (TraitAttackForm form in Enum.GetValues(typeof(TraitAttackForm)))
            Assert.AreEqual(1f, set.DamageMultiplier(new TraitAttackKey(type, form)), $"{type}/{form}");

        foreach (PlayerSkillSlot slot in Enum.GetValues(typeof(PlayerSkillSlot)))
        foreach (TraitAttackForm form in Enum.GetValues(typeof(TraitAttackForm)))
            Assert.AreEqual(1f, set.CooldownMultiplier(slot, form), $"{slot}/{form}");

        Assert.AreEqual(1f, set.HealMultiplier);
        foreach (PlayerStatKind kind in Enum.GetValues(typeof(PlayerStatKind)))
        {
            Assert.AreEqual(0f, set.BasePercent(kind));
            Assert.AreEqual(0f, set.TotalPercent(kind));
        }
        Assert.AreEqual(0f, set.DealtDamagePercent);
        Assert.IsEmpty(set.Errors);
    }

    [Test]
    public void BeforeAnyRebuild_QueriesAreNeutral()
    {
        var set = new TraitModifierSet();
        Assert.AreEqual(1f, set.DamageMultiplier(new TraitAttackKey(AttackType.Default)));
        Assert.AreEqual(1f, set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal));
        Assert.AreEqual(0f, set.TotalPercent(PlayerStatKind.AttackDamage));
    }

    // ── 합성 규칙 ───────────────────────────────────────────────────────

    [Test]
    public void DamageMultipliers_FromDifferentTraits_Multiply()
    {
        // 명세 §10: 거너 출력 강화 + 정온 운용 = 1.20 × 0.85 = 1.02
        var table = new Dictionary<int, ITraitModifierContributor>
        {
            [1] = new FakeContributor(TraitModifier.Damage(1.20f, TraitModifier.MaskOf(AttackType.Default))),
            [2] = new FakeContributor(TraitModifier.Damage(0.85f)), // 모든 공격
        };
        var set = new TraitModifierSet();
        set.Rebuild(new[] { 1, 2 }, Table(table));

        Assert.AreEqual(1.02f, set.DamageMultiplier(new TraitAttackKey(AttackType.Default)), Eps);
        Assert.AreEqual(0.85f, set.DamageMultiplier(new TraitAttackKey(AttackType.SkillQ)), Eps, "평타 전용 배율은 Q 에 안 붙는다");
    }

    [Test]
    public void CooldownMultipliers_Multiply_PerSpecExamples()
    {
        // 명세 §10: 돌파 가속(일반 Q 0.85) + 절제된 분노(일반 Q 0.75) → 5초 × 0.6375 = 3.1875초
        var table = new Dictionary<int, ITraitModifierContributor>
        {
            [10] = new FakeContributor(TraitModifier.Cooldown(0.85f, MainSlot, NormalForm)),
            [11] = new FakeContributor(TraitModifier.Cooldown(0.75f, MainSlot | TraitModifier.MaskOf(PlayerSkillSlot.Sub), NormalForm)),
            [12] = new FakeContributor(TraitModifier.Cooldown(1.20f, MainSlot, NormalForm)), // 그림자 회귀(증가)
        };
        var set = new TraitModifierSet();

        set.Rebuild(new[] { 10, 11 }, Table(table));
        Assert.AreEqual(3.1875f, 5f * set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal), Eps);
        Assert.AreEqual(1f, set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Alternate), "변신 Q 제외");
        Assert.AreEqual(0.75f, set.CooldownMultiplier(PlayerSkillSlot.Sub, TraitAttackForm.Normal), Eps);
        Assert.AreEqual(1f, set.CooldownMultiplier(PlayerSkillSlot.Ultimate, TraitAttackForm.Normal));

        set.Rebuild(new[] { 10, 12 }, Table(table));
        Assert.AreEqual(5.1f, 5f * set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal), Eps);
    }

    [Test]
    public void StatPercents_SameStageAdd()
    {
        var table = new Dictionary<int, ITraitModifierContributor>
        {
            [1] = new FakeContributor(TraitModifier.StatTotal(PlayerStatKind.MoveSpeed, 0.10f), TraitModifier.StatBase(PlayerStatKind.AttackDamage, 0.05f)),
            [2] = new FakeContributor(TraitModifier.StatTotal(PlayerStatKind.MoveSpeed, 0.20f), TraitModifier.DealtDamage(0.15f)),
        };
        var set = new TraitModifierSet();
        set.Rebuild(new[] { 1, 2 }, Table(table));

        Assert.AreEqual(0.30f, set.TotalPercent(PlayerStatKind.MoveSpeed), Eps);
        Assert.AreEqual(0.05f, set.BasePercent(PlayerStatKind.AttackDamage), Eps);
        Assert.AreEqual(0f, set.TotalPercent(PlayerStatKind.AttackDamage));
        Assert.AreEqual(0.15f, set.DealtDamagePercent, Eps);
    }

    [Test]
    public void FormFilter_SeparatesNormalAlternateEnhanced()
    {
        var table = new Dictionary<int, ITraitModifierContributor>
        {
            // 광란 강화 = 변신 E 만, 강타 강화 = 강타(일반 E 로 강화한 기본 공격)만
            [1] = new FakeContributor(TraitModifier.Damage(1.20f, EMask, TraitModifier.MaskOf(TraitAttackForm.Alternate))),
            [2] = new FakeContributor(TraitModifier.Damage(1.20f, TraitModifier.MaskOf(AttackType.Default), TraitModifier.MaskOf(TraitAttackForm.Enhanced))),
        };
        var set = new TraitModifierSet();
        set.Rebuild(new[] { 1, 2 }, Table(table));

        Assert.AreEqual(1.20f, set.DamageMultiplier(new TraitAttackKey(AttackType.SkillE, TraitAttackForm.Alternate)), Eps);
        Assert.AreEqual(1f, set.DamageMultiplier(new TraitAttackKey(AttackType.SkillE, TraitAttackForm.Normal)));
        Assert.AreEqual(1.20f, set.DamageMultiplier(new TraitAttackKey(AttackType.Default, TraitAttackForm.Enhanced)), Eps);
        Assert.AreEqual(1f, set.DamageMultiplier(new TraitAttackKey(AttackType.Default, TraitAttackForm.Normal)), "일반 기본 공격 전체엔 안 붙는다");
        Assert.AreEqual(1f, set.DamageMultiplier(new TraitAttackKey(AttackType.Default, TraitAttackForm.Alternate)));
    }

    [Test]
    public void ForSkill_MapsSlotToAttackType()
    {
        Assert.AreEqual(new TraitAttackKey(AttackType.SkillQ, TraitAttackForm.Normal), TraitAttackKey.ForSkill(PlayerSkillSlot.Main, false));
        Assert.AreEqual(new TraitAttackKey(AttackType.SkillE, TraitAttackForm.Alternate), TraitAttackKey.ForSkill(PlayerSkillSlot.Sub, true));
        Assert.AreEqual(AttackType.SkillR, TraitAttackKey.AttackTypeOf(PlayerSkillSlot.Ultimate));
        Assert.AreEqual(AttackType.Skill, TraitAttackKey.AttackTypeOf(PlayerSkillSlot.Interrupt));
    }

    // ── 페널티·중복·검증 ────────────────────────────────────────────────

    [Test]
    public void Penalty_BelowOne_IsKept()
    {
        var table = new Dictionary<int, ITraitModifierContributor>
        {
            [1] = new FakeContributor(TraitModifier.Damage(0.85f), TraitModifier.StatTotal(PlayerStatKind.MoveSpeed, -0.2f), TraitModifier.Heal(0.5f)),
        };
        var set = new TraitModifierSet();
        set.Rebuild(new[] { 1 }, Table(table));

        Assert.AreEqual(0.85f, set.DamageMultiplier(new TraitAttackKey(AttackType.SkillQ)), Eps);
        Assert.AreEqual(-0.2f, set.TotalPercent(PlayerStatKind.MoveSpeed), Eps);
        Assert.AreEqual(0.5f, set.HealMultiplier, Eps);
        Assert.IsEmpty(set.Errors);
    }

    [Test]
    public void SameTraitTwice_CountsOnce()
    {
        var table = new Dictionary<int, ITraitModifierContributor> { [7] = new FakeContributor(TraitModifier.Damage(1.2f, QMask)) };
        var set = new TraitModifierSet();
        set.Rebuild(new[] { 7, 7 }, Table(table));

        Assert.AreEqual(1.2f, set.DamageMultiplier(new TraitAttackKey(AttackType.SkillQ)), Eps);
        CollectionAssert.AreEqual(new[] { 7 }, set.TraitHashes);
        Assert.IsFalse(set.Rebuild(new[] { 7 }, Table(table)), "중복 제거 후 같은 목록 = 변화 없음");
    }

    [Test]
    public void InvalidValues_AreRejected_WithErrors()
    {
        var table = new Dictionary<int, ITraitModifierContributor>
        {
            [1] = new FakeContributor(
                TraitModifier.Damage(float.NaN),
                TraitModifier.Damage(-0.1f),
                TraitModifier.Cooldown(0f),
                TraitModifier.Heal(float.PositiveInfinity),
                TraitModifier.Damage(1.1f)),
        };
        var set = new TraitModifierSet();
        set.Rebuild(new[] { 1, 99 }, Table(table));

        Assert.AreEqual(1.1f, set.DamageMultiplier(new TraitAttackKey(AttackType.Default)), Eps, "유효한 기여만 남는다");
        Assert.AreEqual(1f, set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal));
        Assert.AreEqual(1f, set.HealMultiplier);
        Assert.AreEqual(5, set.Errors.Count, "잘못된 값 4건 + 정의 없음 1건");
    }

    // ── 서버·클라 / 재선택 ─────────────────────────────────────────────

    [Test]
    public void ServerAndClient_SameInput_SameValues()
    {
        var table = new Dictionary<int, ITraitModifierContributor>
        {
            [1] = new FakeContributor(TraitModifier.Damage(1.2f, QMask)),
            [2] = new FakeContributor(TraitModifier.Damage(1.25f, QMask), TraitModifier.Cooldown(0.85f, MainSlot)),
            [3] = new FakeContributor(TraitModifier.Damage(1.3f, QMask), TraitModifier.StatTotal(PlayerStatKind.AttackDamage, 0.1f)),
        };
        var server = new TraitModifierSet();
        var client = new TraitModifierSet();
        int[] replicated = { 1, 2, 3 };
        server.Rebuild(replicated, Table(table));
        client.Rebuild((int[])replicated.Clone(), Table(table));

        var q = new TraitAttackKey(AttackType.SkillQ);
        Assert.AreEqual(server.DamageMultiplier(q), client.DamageMultiplier(q)); // 정확히 같은 값(같은 순서·같은 연산)
        Assert.AreEqual(server.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal), client.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal));
        Assert.AreEqual(server.TotalPercent(PlayerStatKind.AttackDamage), client.TotalPercent(PlayerStatKind.AttackDamage));
        // 명세 §10 팔라딘: 300 × 1.20 × 1.25 × 1.30 = 585
        Assert.AreEqual(585f, 300f * server.DamageMultiplier(q), 1e-3f);
    }

    [Test]
    public void AcquiredListChange_Recomputes()
    {
        var table = new Dictionary<int, ITraitModifierContributor>
        {
            [1] = new FakeContributor(TraitModifier.Damage(1.2f, QMask)),
            [2] = new FakeContributor(TraitModifier.Cooldown(0.5f, MainSlot)),
        };
        var set = new TraitModifierSet();
        var q = new TraitAttackKey(AttackType.SkillQ);

        Assert.IsTrue(set.Rebuild(new[] { 1 }, Table(table)));
        Assert.AreEqual(1.2f, set.DamageMultiplier(q), Eps);
        Assert.IsFalse(set.Rebuild(new[] { 1 }, Table(table)), "같은 목록 = 변화 없음");

        Assert.IsTrue(set.Rebuild(new[] { 1, 2 }, Table(table)), "획득 추가");
        Assert.AreEqual(0.5f, set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal), Eps);

        Assert.IsTrue(set.Rebuild(Array.Empty<int>(), Table(table)), "특성 초기화 = 빈 목록");
        Assert.AreEqual(1f, set.DamageMultiplier(q));
        Assert.AreEqual(1f, set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal));

        Assert.IsTrue(set.Rebuild(new[] { 2 }, Table(table)), "재선택 = 다른 특성");
        Assert.AreEqual(1f, set.DamageMultiplier(q));
        Assert.AreEqual(0.5f, set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal), Eps);

        Assert.IsTrue(set.Rebuild(new[] { 2, 1 }, Table(table)), "순서가 바뀌어도 다시 합성(복제 순서 = 획득 순서)");
    }

    // ── 연결: PlayerStats·Dref·TraitDefinition ─────────────────────────

    sealed class SetSource : IPlayerStatSource
    {
        public readonly TraitModifierSet Set = new TraitModifierSet();
        public float GetBasePercent(PlayerStatKind kind) => Set.BasePercent(kind);
        public float GetTotalPercent(PlayerStatKind kind) => Set.TotalPercent(kind);
        public float DealtDamagePercent => Set.DealtDamagePercent;
        public event Action Changed { add { } remove { } }
    }

    [Test]
    public void PlayerStats_ReadsTraitPercents_AndDrefAppliesTraitMultiplierOnce()
    {
        var source = new SetSource();
        var stats = new PlayerStats(kind => kind == PlayerStatKind.AttackDamage ? 100f : 5f, null);
        stats.Register(source);

        source.Set.Rebuild(Array.Empty<int>(), null);
        Assert.AreEqual(100, stats.FinalAttackDamage);
        Assert.AreEqual(stats.ResolveDamage(1.5f, 0), stats.ResolveDamage(1.5f, 0, 1f), "빈 목록 Dref = 이전 스냅샷");

        var table = new Dictionary<int, ITraitModifierContributor>
        {
            [1] = new FakeContributor(TraitModifier.StatTotal(PlayerStatKind.AttackDamage, 0.2f), TraitModifier.StatTotal(PlayerStatKind.MoveSpeed, -0.2f)),
        };
        source.Set.Rebuild(new[] { 1 }, Table(table));
        Assert.AreEqual(120, stats.FinalAttackDamage);
        Assert.AreEqual(4f, stats.FinalMoveSpeed, Eps, "페널티 % 도 그대로");

        // Dref = round(round(120 × 1.0) × 1 × 1.2 × 0.85) = round(122.4) = 122 — 중간 정수화 없음
        Assert.AreEqual(122, stats.ResolveDamage(1f, 0, 1.2f * 0.85f));
        stats.Dispose();
    }

    [Test]
    public void Snapshot_TraitMultiplier_DefaultOneMatchesOldFormula_AndClampsAtZero()
    {
        Assert.AreEqual(PlayerStatFormula.Snapshot(195, 1.8f, 0, 0.2f), PlayerStatFormula.Snapshot(195, 1.8f, 0, 0.2f, 1f));
        Assert.AreEqual(421, PlayerStatFormula.Snapshot(195, 1.8f, 0, 0.2f));
        Assert.AreEqual(0, PlayerStatFormula.Snapshot(100, 1f, 0, 0f, -1f));
        Assert.AreEqual(85, PlayerStatFormula.Snapshot(100, 1f, 0, 0f, 0.85f));
    }

    [Test]
    public void TraitDefinition_StatKinds_ContributeTotalPercent_IncludingPenalty()
    {
        // 3/5: 옛 효과 enum 대신 스탯 효과·이벤트형 효과(쿨 차감)
        TraitDefinition attack = MakeDefinition(new TraitStatEffect(PlayerStatKind.AttackDamage, 0.2f));
        TraitDefinition slow = MakeDefinition(new TraitStatEffect(PlayerStatKind.MoveSpeed, -0.2f));
        TraitDefinition onHit = MakeDefinition(new TraitCooldownRefundEffect(
            new TraitTriggerFilter(TraitTrigger.AttackLanded, new[] { AttackType.Default }), 1.5f, new[] { PlayerSkillSlot.Main }));
        try
        {
            var table = new Dictionary<int, ITraitModifierContributor> { [1] = attack, [2] = slow, [3] = onHit };
            var set = new TraitModifierSet();
            set.Rebuild(new[] { 1, 2, 3 }, Table(table));

            Assert.AreEqual(0.2f, set.TotalPercent(PlayerStatKind.AttackDamage), Eps);
            Assert.AreEqual(-0.2f, set.TotalPercent(PlayerStatKind.MoveSpeed), Eps, "1 미만 = 페널티 유지(예전 상태이상 경로는 1로 올렸다)");
            Assert.AreEqual(2, set.Modifiers.Count, "적중 쿨감은 이벤트 효과 — 수치 기여 없음");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(attack);
            UnityEngine.Object.DestroyImmediate(slow);
            UnityEngine.Object.DestroyImmediate(onHit);
        }
    }

    [Test]
    public void Component_WithoutDirector_IsNeutral()
    {
        var go = new GameObject("TraitModifiersTest");
        try
        {
            PlayerTraitModifiers modifiers = PlayerTraitModifiers.For(go.transform);
            Assert.AreSame(modifiers, PlayerTraitModifiers.For(go.transform), "For 는 하나만 만든다");
            Assert.AreEqual(1f, modifiers.DamageMultiplier(new TraitAttackKey(AttackType.SkillR)));
            Assert.AreEqual(1f, modifiers.CooldownMultiplier(PlayerSkillSlot.Sub, TraitAttackForm.Normal));
            Assert.AreEqual(1f, modifiers.HealMultiplier);
            Assert.AreEqual(0f, modifiers.GetTotalPercent(PlayerStatKind.AttackDamage));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    /// <summary>효과 목록([SerializeReference])을 채운 정의 — 다른 특성 테스트도 쓴다.</summary>
    internal static TraitDefinition MakeDefinition(params TraitEffect[] effects)
    {
        var def = ScriptableObject.CreateInstance<TraitDefinition>();
        var so = new SerializedObject(def);
        SerializedProperty list = so.FindProperty("effects");
        list.arraySize = effects.Length;
        for (int i = 0; i < effects.Length; i++)
            list.GetArrayElementAtIndex(i).managedReferenceValue = effects[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        return def;
    }
}
