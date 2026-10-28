using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 126 (4/4) — 팔라딘 2차 모드 변경 돌파의 방패(PAL_2_DASH)의 수용 조건(명세 PAL_2_DASH·§5·§8·§9·§10·§11)을 실제 정의 에셋 값으로 확인한다:
/// 모드 on/off(특성·수치가 있어야만) · 옛 홀드 루틴(지속 타격·지속 밀치기·조향·홀드 해제) 꺼짐 · 대상당 피해 1회·넉백 1회 · 방향 고정 ·
/// 벽·낭떠러지 안전 정지(보고 거리로 상한만 줄임) · 3m/18m/s ≈ 0.167초 · DQfull × 1.25 · §10 585(450 + 135) ·
/// 제압의 진격 = 단발 넉백 뒤 기절 1회 · 기본 Q 쿨 유지 · 기본 Q 거리 3m 이하 = 검증 오류 · 재선택·재접속 재실행 없음 · 12개 풀 순서.
/// 스킬 MonoBehaviour 자체(이동·Overlap)는 Play 확인 몫 — 여기서는 스킬이 그대로 부르는 순수 규칙·장부를 고정한다.
/// </summary>
public sealed class PaladinTraitDashTests
{
    const float Eps = 1e-4f;
    const string Folder = "Assets/9.ScriptableObject/Traits/Paladin/";
    const string QDataPath = "Assets/9.ScriptableObject/Player/Garen/FirstMeleeMainSkillData.asset";

    // ── 도우미 ───────────────────────────────────────────────────────────

    sealed class FakeHost : ITraitEffectHost
    {
        public readonly List<ITraitAttackModifier> Modifiers = new List<ITraitAttackModifier>();
        public readonly List<TraitDerivedAttack> Derived = new List<TraitDerivedAttack>();

        public double Now => 0;
        public event Action<TraitTriggerEvent> Triggered;
        public int SubscriberCount => Triggered?.GetInvocationList().Length ?? 0;
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

    static FirstMeleeMainSkillData QData()
    {
        var data = AssetDatabase.LoadAssetAtPath<FirstMeleeMainSkillData>(QDataPath);
        Assert.IsNotNull(data, QDataPath);
        return data;
    }

    static List<ITraitEffectBinding> Bind(TraitDefinition def, FakeHost host)
    {
        var bindings = new List<ITraitEffectBinding>();
        TraitEffectBinder.Bind(def.Effects, host, 1, def.IdHash, (TraitEffectLedger<int>)null, bindings);
        return bindings;
    }

    static TraitModifierSet SetOf(params TraitDefinition[] defs)
    {
        var set = new TraitModifierSet();
        set.Rebuild(defs.Select(d => d.IdHash).ToList(), h => defs.FirstOrDefault(d => d.IdHash == h));
        Assert.IsEmpty(set.Errors, string.Join("\n", set.Errors));
        return set;
    }

    static string Plain(string text) => Regex.Replace(text ?? string.Empty, "<[^>]+>", string.Empty);

    static PaladinShieldDashSettings Dash() => PaladinShieldDashSettings.FromTrait(Load(PaladinTraitValues.Dash));

    static readonly TraitAttackKey QKey = TraitAttackKey.ForSkill(PlayerSkillSlot.Main, false);

    // ── 풀·카드·공통 ────────────────────────────────────────────────────

    [Test]
    public void Pool_TwelveTraits_DashAfterPassiveHeal_BeforeERefund()
    {
        List<string> ids = TraitContentTestKit.CharacterPool(TutorialCharacter.Paladin).Traits.Select(t => t.Id).ToList();
        CollectionAssert.AreEqual(new[]
        {
            PaladinTraitValues.QDamage, PaladinTraitValues.ECooldown, PaladinTraitValues.PassiveHeal,
            PaladinTraitValues.Dash, PaladinTraitValues.ERefund, PaladinTraitValues.ShareHeal,
            PaladinTraitValues.QStun, PaladinTraitValues.EShield, PaladinTraitValues.PassiveCdr,
            PaladinTraitValues.EPush, PaladinTraitValues.QBlast, PaladinTraitValues.HealShield,
        }, ids, "명세 순서 12개");
        Assert.AreEqual(TraitModeIds.PaladinShieldDash, PaladinTraitValues.Dash, "모드 스위치 ID = TraitModeIds");
    }

    [Test]
    public void Card_TemplateIsSpecGrammar_DashDistanceAlias_DamageNeedsPlayer()
    {
        PaladinTraitDefinition def = Load(PaladinTraitValues.Dash);
        Assert.AreEqual("돌파의 방패", def.InlineDisplayName);
        Assert.AreEqual("Q 변경 · 밀치기", def.InlineCategory);
        Assert.AreEqual("Q가 {dashDistance}m를 빠르게 돌진하며 {dmg}의 피해를 줍니다. 적을 끌고 가지 않고 한 번 밀쳐냅니다.",
            def.InlineDescription, "명세 '문법 원문' 그대로");
        Assert.AreEqual(1, def.Effects.Count);
        Assert.IsInstanceOf<PaladinShieldDashEffect>(def.Effects[0], "시트에 칸 이름(effects[0].dashDistance 등)으로 나가는 수치 칸");

        Assert.IsTrue(TraitTooltip.Format(def, def.InlineDescription, null, false, TraitContent.Build(), out string text, out string error), error);
        StringAssert.StartsWith("Q가 3m를 빠르게 돌진하며 ", Plain(text));
        StringAssert.EndsWith("의 피해를 줍니다. 적을 끌고 가지 않고 한 번 밀쳐냅니다.", Plain(text));
        Assert.IsFalse(PaladinTraitValues.DashDamage(def, null).HasValue, "플레이어 없음 = 값 없음(화면 '-')");
    }

    [Test]
    public void Definition_Valid_ValueOnly_NoOnceAction_ReselectRejoinRerunsNothing()
    {
        PaladinTraitDefinition def = Load(PaladinTraitValues.Dash);
        var errors = new List<string>();
        def.Validate(errors);
        Assert.IsEmpty(errors, string.Join("\n", errors));
        Assert.IsFalse(def.Effects.Any(e => e != null && e.HasOnceAction), "획득 1회 동작 없음(재접속·재선택에서 다시 실행할 것이 없다)");

        // 수치 효과만 — 서버 구독·수정자·파생 등록이 없어 재선택해도 이중 연결이 생길 곳이 없다.
        var host = new FakeHost();
        List<ITraitEffectBinding> bindings = Bind(def, host);
        Assert.AreEqual(0, host.Modifiers.Count + host.Derived.Count + host.SubscriberCount, "이벤트형 연결 없음");
        Bind(def, host);
        Assert.AreEqual(0, host.Modifiers.Count + host.Derived.Count + host.SubscriberCount, "다시 걸어도 없음");
        foreach (ITraitEffectBinding b in bindings) b?.Unbind();
    }

    // ── 수치·모드 스위치 ────────────────────────────────────────────────

    [Test]
    public void Settings_FromAsset_SpecValues_TravelTime()
    {
        PaladinShieldDashSettings s = Dash();
        Assert.IsTrue(s.IsValid);
        Assert.AreEqual(3f, s.Distance, Eps, "dashDistance");
        Assert.AreEqual(18f, s.Speed, Eps, "dashSpeed");
        Assert.AreEqual(0.1f, s.PrepareTime, Eps, "prepareTime");
        Assert.AreEqual(0.12f, s.RecoveryTime, Eps, "recoveryTime");
        Assert.AreEqual(1.25f, s.DamageRatio, Eps, "fullCastDamageRatio");
        Assert.AreEqual(3f, s.PushDistance, Eps, "pushDistance");
        Assert.AreEqual(0.3f, s.PushDuration, Eps, "pushDuration");
        Assert.AreEqual(3f / 18f, s.TravelTime, Eps, "3m ÷ 18m/s ≈ 0.167초");
        Assert.AreEqual(0.1f + 3f / 18f + 0.12f, s.TotalTime, Eps);
        Assert.AreEqual(10f, s.PushSpeed, Eps, "넉백 3m ÷ 0.3초");
    }

    [Test]
    public void ModeSwitch_OnlyWithTraitAndValidValues_OldHoldRoutineOff()
    {
        PaladinTraitDefinition def = Load(PaladinTraitValues.Dash);
        TraitModifierSet with = SetOf(def);
        Assert.IsTrue(with.TraitHashes.Contains(TraitIdHash.Of(TraitModeIds.PaladinShieldDash)), "HasTrait 가 읽는 해시");
        Assert.AreEqual(3f, with.ResolveSkillValue(PaladinTraitValues.DashDistanceKey, 0f), Eps, "스킬이 ResolveSkillValue 로 읽는 값");
        Assert.AreEqual(18f, with.ResolveSkillValue(PaladinTraitValues.DashSpeedKey, 0f), Eps);

        TraitModifierSet without = SetOf(Load(PaladinTraitValues.QDamage));
        Assert.AreEqual(0f, without.ResolveSkillValue(PaladinTraitValues.DashDistanceKey, 0f), "특성 없음 = 0");

        Assert.IsTrue(PaladinTraitRules.UsesShieldDash(true, Dash()), "특성 + 수치 = 돌진 모드");
        Assert.IsFalse(PaladinTraitRules.UsesShieldDash(false, Dash()), "특성 없음 = 원래 Q");
        Assert.IsFalse(PaladinTraitRules.UsesShieldDash(true, default), "수치 누락 = 원래 Q(스킬이 오류를 남긴다)");

        // §11 'Q 교체 후 옛 지속 타격 제거' — 홀드 틱(지속 타격·지속 밀치기)·홀드 해제는 Charging 에서만 돈다(PlayerHoldSkill).
        Assert.AreEqual(SkillState.Charging, PaladinTraitRules.QStartState(false), "원래 Q = 홀드");
        Assert.AreNotEqual(SkillState.Charging, PaladinTraitRules.QStartState(true), "돌진 = 홀드 아님 → 지속 타격·지속 밀치기·홀드 해제 없음");
        Assert.AreEqual(SkillState.Active, PaladinTraitRules.QStartState(true));
    }

    [Test]
    public void BaseQCooldown_Kept_NoDamageMultiplier()
    {
        TraitModifierSet set = SetOf(Load(PaladinTraitValues.Dash));
        Assert.AreEqual(1f, set.CooldownMultiplier(PlayerSkillSlot.Main, TraitAttackForm.Normal), Eps, "기본 Q 쿨 유지");
        Assert.IsFalse(Load(PaladinTraitValues.Dash).AffectsCooldownOf(PlayerSkillSlot.Main), "쿨 관여 없음");
        Assert.AreEqual(1f, set.DamageMultiplier(QKey), Eps, "Dref 배율 기여 없음 — 1.25 는 교체 단발 피해 한 곳에서만");
    }

    [Test]
    public void Content_BaseQLongerThanDash_ElseValidationError()
    {
        FirstMeleeMainSkillData q = QData();
        float baseDistance = PaladinTraitRules.BaseQMaxDistance(q.AdvanceSpeed, q.MaxActiveDuration);
        var errors = new List<string>();
        PaladinTraitRules.ValidateShieldDash(Dash(), baseDistance, errors);
        Assert.IsEmpty(errors, $"현재 기본 Q 유효 최대 거리 {baseDistance}m > 돌진 3m 이어야 한다 — " + string.Join("\n", errors));

        var shortQ = new List<string>();
        PaladinTraitRules.ValidateShieldDash(Dash(), 3f, shortQ);
        Assert.AreEqual(1, shortQ.Count, "기본 Q 3m = 오류(거리를 자동으로 늘리지 않는다)");
        var shorter = new List<string>();
        PaladinTraitRules.ValidateShieldDash(Dash(), 2.5f, shorter);
        Assert.AreEqual(1, shorter.Count);

        var missing = new List<string>();
        PaladinTraitRules.ValidateShieldDash(default, baseDistance, missing);
        Assert.IsNotEmpty(missing, "수치 누락 = 오류");

        var bad = new List<string>();
        new PaladinShieldDashEffect(0f, 18f, -0.1f, 0.12f, 1.25f, 3f, float.NaN).Validate(bad);
        Assert.AreEqual(3, bad.Count, "거리 0·음수 준비·NaN 넉백 시간 = 정의 검증 오류(명세 §9) — " + string.Join("\n", bad));
    }

    // ── 경로·판정 ───────────────────────────────────────────────────────

    [Test]
    public void Path_PrepareThenTravel_DirectionFixed_FinishesAfterRecovery()
    {
        var path = new PaladinShieldDashPath();
        Vector3 dir = new Vector3(1f, 0.5f, 1f);
        path.Begin(new Vector3(2f, 0f, 5f), dir, Dash());
        Vector3 heading = new Vector3(1f, 0f, 1f).normalized;
        Assert.That(Vector3.Distance(heading, path.Heading), Is.LessThan(Eps), "입력 순간 방향(바닥 평면) 고정");

        Assert.AreEqual(0f, path.TraveledAt(0.05f), Eps, "준비 0.1초 동안 제자리");
        Assert.AreEqual(1.5f, path.TraveledAt(0.1f + 1.5f / 18f), 1e-3f, "18m/s");
        Assert.AreEqual(3f, path.TraveledAt(0.1f + 3f / 18f), 1e-3f);
        Assert.AreEqual(3f, path.TraveledAt(5f), Eps, "거리 상한 3m");
        Assert.IsFalse(path.IsFinished(0.1f + 3f / 18f), "회수 0.12초 남음");
        Assert.IsTrue(path.IsFinished(0.1f + 3f / 18f + 0.12f + 1e-4f));

        // 프레임마다 조향 입력이 와도 경로 장부에는 방향을 바꾸는 길이 없다 — 표본은 모두 같은 직선 위.
        var samples = new List<float>();
        for (float t = 0f; t <= 0.4f; t += 1f / 60f)
        {
            path.AdvanceTo(path.TraveledAt(t), samples);
            foreach (float d in samples)
                Assert.That(Vector3.Distance(path.PointAt(d), new Vector3(2f, 0f, 5f) + heading * d), Is.LessThan(Eps));
        }
        Assert.That(Vector3.Distance(heading, path.Heading), Is.LessThan(Eps));
    }

    [Test]
    public void Path_SamplesCoverWholePath_NoGapsBiggerThanSpacing()
    {
        var path = new PaladinShieldDashPath();
        path.Begin(Vector3.zero, Vector3.forward, Dash());
        var all = new List<float>();
        var samples = new List<float>();
        // 느린 서버 프레임(20fps)이어도 끝점만 보지 않고 구간을 훑는다.
        for (float t = 0f; t <= 0.4f; t += 0.05f)
        {
            path.AdvanceTo(path.TraveledAt(t), samples);
            all.AddRange(samples);
        }

        Assert.AreEqual(0f, all.First(), Eps, "시작 지점 1회");
        Assert.AreEqual(3f, all.Last(), 1e-3f, "끝 지점");
        for (int i = 1; i < all.Count; i++)
            Assert.That(all[i] - all[i - 1], Is.LessThanOrEqualTo(PaladinShieldDashPath.SampleSpacing + Eps), $"표본 {i} 간격");
        Assert.AreEqual(all.Count, all.Distinct().Count(), "같은 지점을 두 번 훑지 않는다");
    }

    [Test]
    public void Path_SafeStop_ReportOnlyShrinks_Validated()
    {
        var path = new PaladinShieldDashPath();
        var samples = new List<float>();
        path.Begin(Vector3.zero, Vector3.forward, Dash());

        Assert.IsFalse(path.ReportStop(-0.1f), "음수 거부");
        Assert.IsFalse(path.ReportStop(float.NaN), "NaN 거부");
        Assert.IsFalse(path.ReportStop(10f), "최대 거리 초과 거부");
        Assert.AreEqual(3f, path.Cap, Eps);

        // 벽·낭떠러지 1.2m 앞에서 멈춤
        Assert.IsTrue(path.ReportStop(1.2f));
        Assert.AreEqual(1.2f, path.Cap, Eps);
        Assert.AreEqual(1.2f, path.TraveledAt(5f), Eps, "보고 뒤 경로가 벽 너머로 가지 않는다");
        path.AdvanceTo(path.TraveledAt(5f), samples);
        Assert.AreEqual(1.2f, samples.Last(), Eps);

        Assert.IsTrue(path.ReportStop(2.5f));
        Assert.AreEqual(1.2f, path.Cap, Eps, "늘리지 않는다");

        // 이미 훑은 거리 아래로는 줄이지 않는다(원격 오너 보고 지연).
        var late = new PaladinShieldDashPath();
        late.Begin(Vector3.zero, Vector3.forward, Dash());
        late.AdvanceTo(2f, samples);
        Assert.IsTrue(late.ReportStop(1f));
        Assert.AreEqual(2f, late.Cap, Eps);

        path.End();
        Assert.IsFalse(path.ReportStop(0.5f), "끝난 시전엔 보고 무시");
    }

    [Test]
    public void Path_OneHitAndOneKnockbackPerTarget_PerCast()
    {
        var path = new PaladinShieldDashPath();
        path.Begin(Vector3.zero, Vector3.right, Dash());
        Assert.IsTrue(path.TryRegisterTarget(10), "첫 판정");
        Assert.IsFalse(path.TryRegisterTarget(10), "같은 시전 같은 대상 = 다시 없음(여러 표본·여러 콜라이더)");
        Assert.IsTrue(path.TryRegisterTarget(11), "다른 대상 = 각자 1회");

        AttackInfo info = path.CreateAttack(450, AttackType.SkillQ, 0.2f, AttackHitPattern.Single);
        Assert.AreEqual(450, info.damage);
        Assert.AreEqual(10f, info.knockbackStrength, Eps, "단발 넉백 속도 = 3m ÷ 0.3초");
        Assert.AreEqual(0.3f, info.knockbackDuration, Eps);
        Assert.AreEqual(3f, info.knockbackStrength * info.knockbackDuration, Eps, "넉백 거리 3m — 끌고 가지 않는 한 번");
        Assert.AreEqual(Vector3.right, info.knockbackDirection, "진행 방향");

        path.Begin(Vector3.zero, Vector3.right, Dash());
        Assert.IsTrue(path.TryRegisterTarget(10), "다음 시전 = 다시 1회");
    }

    // ── 피해 ─────────────────────────────────────────────────────────────

    [Test]
    public void Damage_IsDQfullTimesRatio_IndependentOfHits()
    {
        FirstMeleeMainSkillData q = QData();
        int full = PaladinTraitRules.QPacketDamage(25, q.MaxActiveDuration, q.TickInterval);
        Assert.AreEqual(full, PaladinTraitRules.QPacketDamage(25, q.MaxActiveDuration, q.TickInterval, 0f), "배율 0 = 원래 Q 묶음");
        Assert.AreEqual(TraitDerivedAttackRules.Damage(full, 1.25f), PaladinTraitRules.QPacketDamage(25, q.MaxActiveDuration, q.TickInterval, 1.25f));
        Assert.AreEqual(375, PaladinTraitRules.QPacketDamage(25, 3f, 0.25f, Dash().DamageRatio), "기본 Q 합계 300 × 1.25 = 375(명세 문서 예시)");
        Assert.AreEqual(int.MaxValue, PaladinTraitRules.QPacketDamage(int.MaxValue, 3f, 0.25f, 1.25f), "넘침 없음");
    }

    [Test]
    public void Section10_QDamage_Dash_QBlast_585()
    {
        PaladinTraitDefinition qDamage = Load(PaladinTraitValues.QDamage);
        PaladinTraitDefinition dash = Load(PaladinTraitValues.Dash);
        PaladinTraitDefinition qBlast = Load(PaladinTraitValues.QBlast);
        TraitModifierSet set = SetOf(qDamage, dash, qBlast);
        float multiplier = set.DamageMultiplier(QKey);
        Assert.AreEqual(1.2f, multiplier, Eps, "진격 강화만 Dref 에 — 돌파의 방패·진격의 충격은 배율 기여 없음");

        // 틱 Dref = 판정 스냅샷 식(공격력 100 × 계수 0.25 = 틱 25, 12틱 = 300) × Q 특성 배율 — Player.ResolveDamage 와 같은 식.
        int tickDref = PlayerStatFormula.Snapshot(100, 0.25f, 0, 0f, multiplier);
        float dashRatio = set.ResolveSkillValue(PaladinTraitValues.DashDamageRatioKey, 0f);
        int original = PaladinTraitRules.QPacketDamage(tickDref, 3f, 0.25f, dashRatio);
        Assert.AreEqual(450, original, "원래 = 300 × 1.20 × 1.25");

        TraitDerivedAttack blast = qBlast.Effects.OfType<TraitDerivedAttackEffect>().Single().CreateRuntime(qBlast.IdHash);
        int extra = blast.DamageFrom(original);
        Assert.AreEqual(135, extra, "추가 = 교체 단발 450 × 0.30(진격의 충격 DQpacket 분기)");
        Assert.AreEqual(585, original + extra, "단일 대상 총 585");
    }

    // ── 제압의 진격 결합 ────────────────────────────────────────────────

    [Test]
    public void QStun_SingleKnockback_ThenStunOnce()
    {
        var host = new FakeHost();
        List<ITraitEffectBinding> bindings = Bind(Load(PaladinTraitValues.QStun), host);
        var path = new PaladinShieldDashPath();
        path.Begin(Vector3.zero, Vector3.forward, Dash());
        var carry = new PaladinQConcussionCarry();

        Assert.IsTrue(path.TryRegisterTarget(10));
        AttackInfo info = path.CreateAttack(450, AttackType.SkillQ, 0.2f, AttackHitPattern.Single);
        host.Modify(QKey, 1, 10, path.Heading, ref info); // = ApplyTraitCrowdControlServer
        info.concussionDuration = carry.Resolve(10, info.concussionDuration);
        carry.EndTick();
        Assert.AreEqual(0.75f, info.concussionDuration, Eps, "단발 타격에 뇌진탕 0.75");
        Assert.AreEqual(3f, info.knockbackStrength * info.knockbackDuration, Eps, "단발 넉백 3m 유지(특성 밀치기 0.1m 가 덮지 않음)");
        Assert.AreEqual(0.3f, info.knockbackDuration, Eps);
        Assert.IsFalse(path.TryRegisterTarget(10), "같은 시전 두 번째 판정 자체가 없다");

        // 수신측: 밀린 상태로 1회 → 넉백 끝나는 순간 1회 기절, 그 뒤 없음.
        var state = new ConcussionState();
        Assert.AreEqual(ConcussionState.ApplyResult.Pending, state.Apply(info.concussionDuration, true, StatusEffectType.None));
        Assert.AreEqual(0.75f, state.ConsumeOnKnockbackEnd(StatusEffectType.None), Eps, "단발 넉백 끝 = 기절 0.75");
        Assert.AreEqual(0f, state.ConsumeOnKnockbackEnd(StatusEffectType.None), Eps, "대상별 1회");

        foreach (ITraitEffectBinding b in bindings) b?.Unbind();
    }
}
