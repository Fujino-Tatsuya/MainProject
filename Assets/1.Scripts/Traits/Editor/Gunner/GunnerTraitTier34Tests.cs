using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 거너 3·4차 단순 특성 5개 수용 조건(할 일 127 2/4, 명세 11장 거너 — 관통 벽 차단·잔류 1회·이중 발수 + 다중 적/콜라이더·CC 시전당 1회).
/// 판정 순서·배율·예약은 순수 규칙(<c>Traits/Gunner/GunnerTraitCombatRules.cs</c>)으로 보고, 수치는 프로젝트 정의 에셋에서 읽는다.
/// 물리 질의·네트워크·VFX 는 Play 확인 몫이다.
/// </summary>
public class GunnerTraitTier34Tests
{
    private const string Folder = "Assets/9.ScriptableObject/Traits/Gunner/";

    private static GunnerTraitDefinition Load(string id)
    {
        var def = AssetDatabase.LoadAssetAtPath<GunnerTraitDefinition>(Folder + id + ".asset");
        Assert.IsNotNull(def, Folder + id + ".asset");
        return def;
    }

    private static TraitModifierSet With(params TraitDefinition[] defs)
    {
        var set = new TraitModifierSet();
        set.Rebuild(defs.Select(d => d.IdHash).ToList(), hash => defs.First(d => d.IdHash == hash));
        Assert.IsEmpty(set.Errors, string.Join("\n", set.Errors));
        return set;
    }

    private static TraitModifierSet None()
    {
        var set = new TraitModifierSet();
        set.Rebuild(new List<int>(), _ => null);
        return set;
    }

    private static TraitDerivedAttack Derived(string id, string key)
    {
        TraitDerivedAttackEffect effect = GunnerTraitTooltip.FindDerived(Load(id), key);
        Assert.IsNotNull(effect, id + " 파생 " + key);
        return effect.CreateRuntime(Load(id).IdHash);
    }

    private static int[] Keys(List<GunnerBeamCandidate> ordered) => ordered.Select(c => c.TargetKey).ToArray();

    // ── 관통 탄도(GUN_3_PIERCE) ─────────────────────────────────────────

    [Test]
    public void Pierce_Asset_SecondaryRatio065_OnlyWhenHeld()
    {
        Assert.AreEqual(0.65f, With(Load("GUN_3_PIERCE")).ResolveSkillValue(GunnerTraitRules.BasicPierceSecondaryRatio, -1f), 1e-6f);
        Assert.Less(None().ResolveSkillValue(GunnerTraitRules.BasicPierceSecondaryRatio, -1f), 0f, "특성 없음 = 관통 없음(첫 대상 1체)");
        Assert.AreEqual(1f, With(Load("GUN_3_PIERCE")).DamageMultiplier(GunnerTraitRules.BasicAttackKey), 1e-6f, "자동 피해 배율 아님");
    }

    [Test]
    public void Pierce_Order_ByServerDistance_TieBrokenByNetworkId()
    {
        var raw = new List<GunnerBeamCandidate>
        {
            new GunnerBeamCandidate(1, 50, 3f),
            new GunnerBeamCandidate(2, 20, 3f),
            new GunnerBeamCandidate(3, 90, 1f),
        };
        var ordered = new List<GunnerBeamCandidate>();
        GunnerPierceRules.Order(raw, float.PositiveInfinity, ordered);
        CollectionAssert.AreEqual(new[] { 3, 2, 1 }, Keys(ordered));

        // 입력 순서가 달라도 같은 결과(안정 정렬).
        raw.Reverse();
        GunnerPierceRules.Order(raw, float.PositiveInfinity, ordered);
        CollectionAssert.AreEqual(new[] { 3, 2, 1 }, Keys(ordered));
    }

    [Test]
    public void Pierce_MultipleCollidersOfOneUnit_MergeToOneHit_AtNearest()
    {
        var raw = new List<GunnerBeamCandidate>
        {
            new GunnerBeamCandidate(7, 1, 5f, 0),
            new GunnerBeamCandidate(8, 2, 3f, 1),
            new GunnerBeamCandidate(7, 1, 2f, 2), // 같은 유닛의 다른 콜라이더(더 가깝다)
            new GunnerBeamCandidate(7, 1, 4f, 3),
        };
        var ordered = new List<GunnerBeamCandidate>();
        GunnerPierceRules.Order(raw, float.PositiveInfinity, ordered);
        CollectionAssert.AreEqual(new[] { 7, 8 }, Keys(ordered), "한 발당 대상별 1회");
        Assert.AreEqual(2f, ordered[0].Distance);
        Assert.AreEqual(2, ordered[0].SourceIndex, "가장 가까운 콜라이더로 맞는다");
    }

    [Test]
    public void Pierce_Wall_BlocksTargetsBehind_ButNotInFront()
    {
        var raw = new List<GunnerBeamCandidate>
        {
            new GunnerBeamCandidate(1, 1, 2f),
            new GunnerBeamCandidate(2, 2, 4f), // 벽 면에 붙음
            new GunnerBeamCandidate(3, 3, 6f), // 벽 뒤
        };
        var ordered = new List<GunnerBeamCandidate>();
        GunnerPierceRules.Order(raw, 4f, ordered);
        CollectionAssert.AreEqual(new[] { 1, 2 }, Keys(ordered), "적은 관통, 벽은 관통 안 함");

        // 같은 유닛이 벽 앞·뒤 콜라이더를 다 가지면 앞 콜라이더로 맞는다.
        raw.Add(new GunnerBeamCandidate(3, 3, 3.5f));
        GunnerPierceRules.Order(raw, 4f, ordered);
        CollectionAssert.AreEqual(new[] { 1, 3, 2 }, Keys(ordered));
    }

    [Test]
    public void Pierce_Damage_First100_EachLater065_NotPowered()
    {
        Assert.AreEqual(100, GunnerPierceRules.Damage(100, 0, 0.65f));
        Assert.AreEqual(65, GunnerPierceRules.Damage(100, 1, 0.65f));
        Assert.AreEqual(65, GunnerPierceRules.Damage(100, 2, 0.65f), "0.65² = 42 아님");
        Assert.AreEqual(65, GunnerPierceRules.Damage(100, 5, 0.65f));
        Assert.AreEqual(33, GunnerPierceRules.Damage(51, 1, 0.65f), "33.15 → 33(한 번 반올림)");
        Assert.AreEqual(34, GunnerPierceRules.Damage(52, 1, 0.65f), "33.8 → 34");
    }

    // ── 집중 포화(GUN_4_R_FOCUS) ────────────────────────────────────────

    [Test]
    public void FocusFire_Asset_Bonus020_NotAnAutomaticDamageMultiplier()
    {
        TraitModifierSet set = With(Load("GUN_4_R_FOCUS"));
        Assert.AreEqual(0.2f, set.ResolveSkillValue(GunnerTraitRules.RFocusDamageBonus, 0f), 1e-6f);
        Assert.AreEqual(0f, None().ResolveSkillValue(GunnerTraitRules.RFocusDamageBonus, 0f));
        Assert.AreEqual(1f, set.DamageMultiplier(GunnerTraitRules.BasicAttackKey), 1e-6f, "위치 조건 없이 붙지 않는다");
        Assert.AreEqual(1f, set.DamageMultiplier(GunnerTraitRules.QAttackKey), 1e-6f);
    }

    [Test]
    public void FocusFire_OnlyOwnR_OtherGunnerRDoesNotQualify()
    {
        var zones = new List<GunnerRZone> { new GunnerRZone(2, Vector3.zero, 3f) };
        Assert.IsFalse(GunnerFocusFireRules.IsInsideOwnZone(1, new Vector3(1f, 0f, 0f), zones), "다른 거너 R");
        Assert.IsTrue(GunnerFocusFireRules.IsInsideOwnZone(2, new Vector3(1f, 0f, 0f), zones));
        Assert.IsFalse(GunnerFocusFireRules.IsInsideOwnZone(2, new Vector3(3.5f, 0f, 0f), zones), "범위 밖");
        Assert.IsTrue(GunnerFocusFireRules.IsInsideOwnZone(2, new Vector3(0f, 5f, 3f), zones), "높이는 보지 않는다(수평 거리)");
    }

    [Test]
    public void FocusFire_OverlappingOwnR_AppliesOnce()
    {
        var zones = new List<GunnerRZone>
        {
            new GunnerRZone(1, Vector3.zero, 3f),
            new GunnerRZone(1, new Vector3(1f, 0f, 0f), 3f),
        };
        bool inside = GunnerFocusFireRules.IsInsideOwnZone(1, new Vector3(0.5f, 0f, 0f), zones);
        float multiplier = GunnerFocusFireRules.Multiplier(AttackType.Default, 0.2f, inside);
        Assert.AreEqual(1.2f, multiplier, 1e-6f, "겹쳐도 1.44 아님");
        Assert.AreEqual(120, GunnerFocusFireRules.Apply(100, multiplier));
        Assert.AreEqual(100, GunnerFocusFireRules.Apply(100, GunnerFocusFireRules.Multiplier(AttackType.Default, 0.2f, false)));
    }

    [TestCase(AttackType.Default, 1.2f)]
    [TestCase(AttackType.SkillQ, 1.2f)]
    [TestCase(AttackType.SkillR, 1f)]   // R 자체 틱
    [TestCase(AttackType.Skill, 1f)]    // 우클릭(간파)
    [TestCase(AttackType.SkillE, 1f)]
    public void FocusFire_BasicAndQOnly_RTickAndRightClickExcluded(AttackType type, float expected)
    {
        Assert.AreEqual(expected, GunnerFocusFireRules.Multiplier(type, 0.2f, true), 1e-6f);
    }

    [Test]
    public void FocusFire_DerivedDamage_GetsTargetConditionOnce()
    {
        // 파생 = 원본 × 파생 비율 × 대상 조건 배율(한 번). 원본 100 → 이중 출력 40 → R 안 48.
        int derived = TraitDerivedAttackRules.Damage(100, 0.4f);
        Assert.AreEqual(48, GunnerFocusFireRules.Apply(derived, GunnerFocusFireRules.Multiplier(AttackType.Default, 0.2f, true)));
    }

    // ── 충격 레이저(GUN_3_Q_PUSH) ──────────────────────────────────────

    [Test]
    public void ImpactLaser_Asset_QOnly_Push2m_In02s()
    {
        GunnerTraitDefinition def = Load("GUN_3_Q_PUSH");
        var knockback = def.Effects.OfType<TraitKnockbackEffect>().Single();
        Assert.IsTrue(knockback.Matches(GunnerTraitRules.QAttackKey));
        Assert.IsFalse(knockback.Matches(GunnerTraitRules.BasicAttackKey), "기본 공격엔 없음");
        Assert.IsFalse(knockback.Matches(new TraitAttackKey(AttackType.SkillR)));
        Assert.AreEqual(10f, knockback.PushSpeed, 1e-4f, "2m ÷ 0.2초");
        Assert.AreEqual(0f, knockback.ConcussionDuration);
    }

    [Test]
    public void ImpactLaser_Gate_OncePerCastPerTarget()
    {
        var gate = new GunnerCastCcGate();
        Assert.IsTrue(gate.ShouldRequest(1, 100, false));
        gate.MarkApproved(1, 100);
        Assert.IsFalse(gate.ShouldRequest(1, 100, false), "같은 시전 같은 대상 두 번째");
        Assert.IsTrue(gate.ShouldRequest(1, 200, false), "다른 대상");
        Assert.IsTrue(gate.ShouldRequest(2, 100, false), "새 시전");
        gate.Reset();
        Assert.IsTrue(gate.ShouldRequest(1, 100, false), "사망 정리 뒤");
    }

    [Test]
    public void ImpactLaser_ImmuneFirst_ThenLaterOriginalShotApprovesOnce()
    {
        // 같은 시전의 원래 발 3발(집속 연사 대비) — 1발: 슈퍼아머(면역), 2발: 취약, 3발: 이미 승인.
        var effect = new TraitKnockbackEffect(2f, 0.2f, 0.1f, new[] { AttackType.SkillQ });
        var modifier = new TraitKnockbackEffect.Modifier(effect);
        var gate = new GunnerCastCcGate();
        const int cast = 7, target = 42;
        bool[] immune = { true, false, false };
        var pushed = new List<bool>();

        foreach (bool isImmune in immune)
        {
            var info = new AttackInfo(50, AttackType.SkillQ);
            if (gate.ShouldRequest(cast, target, isImmune))
            {
                modifier.Modify(GunnerTraitRules.QAttackKey, cast, target, Vector3.right, ref info);
                if (info.knockbackStrength * info.knockbackDuration > 0f)
                    gate.MarkApproved(cast, target);
            }

            pushed.Add(info.knockbackStrength > 0f);
            if (info.knockbackStrength > 0f)
                Assert.AreEqual(Vector3.right, info.knockbackDirection, "레이저 진행 방향");
        }

        CollectionAssert.AreEqual(new[] { false, true, false }, pushed);
    }

    [Test]
    public void ImpactLaser_SharedGateAlone_WouldSpendImmuneShot_WhyGunnerSkipsRequest()
    {
        // 공용 거름은 부르는 순간 대상을 쓴다 — 면역 발에서 부르면 다음 발이 승인되지 않는다(그래서 면역이면 요청하지 않는다).
        var modifier = new TraitKnockbackEffect.Modifier(new TraitKnockbackEffect(2f, 0.2f, 0.1f, new[] { AttackType.SkillQ }));
        var first = new AttackInfo(50, AttackType.SkillQ);
        modifier.Modify(GunnerTraitRules.QAttackKey, 3, 9, Vector3.forward, ref first);
        var second = new AttackInfo(50, AttackType.SkillQ);
        modifier.Modify(GunnerTraitRules.QAttackKey, 3, 9, Vector3.forward, ref second);
        Assert.Greater(first.knockbackStrength, 0f);
        Assert.AreEqual(0f, second.knockbackStrength);
    }

    // ── 이중 출력(GUN_4_EXTRA_BASIC) ───────────────────────────────────

    [Test]
    public void DualOutput_EveryThirdCommittedShot_Extra040_After005()
    {
        TraitDerivedAttack extra = Derived("GUN_4_EXTRA_BASIC", GunnerTraitRules.BasicExtraDerivedKey);
        Assert.IsTrue(extra.Matches(GunnerTraitRules.BasicAttackKey));
        Assert.IsFalse(extra.Matches(GunnerTraitRules.QAttackKey), "Q 는 세지 않는다");
        Assert.IsTrue(extra.HitArea.IsEmpty, "판정 도형 없음 = 원래 기본 공격 레이저 재사용(할 일 153)");

        var fired = new List<bool>();
        int damage = 0;
        float delay = 0f;
        for (int shot = 1; shot <= 6; shot++)
        {
            // 원래 발 피해는 매 발 다를 수 있다(과열 단계) — 세 번째 발 기준.
            bool trigger = GunnerDualOutputRules.CountOriginalShot(extra, 100 + shot, 1f, out int d, out float t);
            fired.Add(trigger);
            if (shot == 3)
            {
                damage = d;
                delay = t;
            }
        }

        CollectionAssert.AreEqual(new[] { false, false, true, false, false, true }, fired);
        Assert.AreEqual(41, damage, "세 번째 원래 발 103 × 0.40 = 41.2 → 41");
        Assert.AreEqual(0.05f, delay, 1e-6f);
    }

    [Test]
    public void DualOutput_MissesCount_CancelAndExtraShotDoNot_CounterZeroAfterExtra()
    {
        TraitDerivedAttack extra = Derived("GUN_4_EXTRA_BASIC", GunnerTraitRules.BasicExtraDerivedKey);
        // 발사 커밋 = 적중과 무관하게 부른다(빗나감 포함). 공격 취소(준비 중 대시)·추가 발은 부르지 않는다.
        Assert.IsFalse(GunnerDualOutputRules.CountOriginalShot(extra, 100, 1f, out _, out _)); // 빗나감
        // (취소된 공격 — 발사 커밋 없음)
        Assert.IsFalse(GunnerDualOutputRules.CountOriginalShot(extra, 100, 1f, out _, out _)); // 적중
        Assert.AreEqual(2, extra.Counter.Count);
        Assert.IsTrue(GunnerDualOutputRules.CountOriginalShot(extra, 100, 1f, out int extraDamage, out _)); // 빗나감
        Assert.AreEqual(40, extraDamage);
        Assert.AreEqual(0, extra.Counter.Count, "추가 발사 후 0, 추가 발은 세지 않는다");
    }

    [Test]
    public void DualOutput_DeathReset_StartsOver_MovementDoesNot()
    {
        TraitDerivedAttack extra = Derived("GUN_4_EXTRA_BASIC", GunnerTraitRules.BasicExtraDerivedKey);
        GunnerDualOutputRules.CountOriginalShot(extra, 100, 1f, out _, out _);
        GunnerDualOutputRules.CountOriginalShot(extra, 100, 1f, out _, out _);
        extra.Counter.Reset(); // 사망·새 회차 — PlayerTraitEffects.ClearTransientServer
        Assert.IsFalse(GunnerDualOutputRules.CountOriginalShot(extra, 100, 1f, out _, out _));
        Assert.IsFalse(GunnerDualOutputRules.CountOriginalShot(extra, 100, 1f, out _, out _));
        Assert.IsTrue(GunnerDualOutputRules.CountOriginalShot(extra, 100, 1f, out _, out _));
    }

    [Test]
    public void DualOutput_Delay_ScalesWithAttackSpeed()
    {
        TraitDerivedAttack extra = Derived("GUN_4_EXTRA_BASIC", GunnerTraitRules.BasicExtraDerivedKey);
        GunnerDualOutputRules.CountOriginalShot(extra, 100, 2f, out _, out _);
        GunnerDualOutputRules.CountOriginalShot(extra, 100, 2f, out _, out _);
        Assert.IsTrue(GunnerDualOutputRules.CountOriginalShot(extra, 100, 2f, out _, out float delay));
        Assert.AreEqual(0.025f, delay, 1e-6f, "공격 가속 2배 → 지연 절반");
    }

    [Test]
    public void DelayedShots_FireWhenDue_InOrder_ClearCancels()
    {
        var queue = new GunnerDelayedShots();
        var due = new List<GunnerDerivedShot>();
        queue.Schedule(10.0, 0.05f, new GunnerDerivedShot(Vector3.zero, Vector3.forward, 8f, 1f, 40));
        queue.Schedule(10.02, 0.05f, new GunnerDerivedShot(Vector3.zero, Vector3.forward, 8f, 1f, 41));

        queue.CollectDue(10.04, due);
        Assert.AreEqual(0, due.Count);
        queue.CollectDue(10.06, due);
        CollectionAssert.AreEqual(new[] { 40 }, due.Select(s => s.Damage).ToArray());
        Assert.AreEqual(1, queue.Count);

        queue.Clear(); // 예약 중 사망·씬 종료
        queue.CollectDue(99.0, due);
        Assert.AreEqual(0, due.Count);
    }

    // ── 잔류 포격(GUN_4_ECHO_Q) ────────────────────────────────────────

    [TestCase(true, SkillEndReason.Completed, true)]
    [TestCase(true, SkillEndReason.MaxDurationReached, true)]   // End 이벤트가 빠졌을 때의 안전망 — 발사는 끝났다
    [TestCase(true, SkillEndReason.Cancelled, false)]           // 도중 중단
    [TestCase(true, SkillEndReason.CasterDied, false)]
    [TestCase(false, SkillEndReason.Completed, false)]
    [TestCase(false, SkillEndReason.DashCancelled, false)]      // 충전 중 대시 — 발사 없음
    public void Echo_OnlyAfterNormalEnd(bool fired, SkillEndReason reason, bool expected)
    {
        TraitDerivedAttack echo = Derived("GUN_4_ECHO_Q", GunnerTraitRules.QEchoDerivedKey);
        var plan = new GunnerEchoPlan();
        if (fired)
        {
            plan.AddOriginalShot(300);
            plan.AddHitTarget(11UL);
        }

        Assert.AreEqual(expected, plan.TryBuildEcho(fired, reason, echo, out _));
    }

    [Test]
    public void Echo_NoHitTarget_NoBomb_CounterUntouched()
    {
        // 아무도 안 맞혔으면 폭탄 없음(정상 종료여도).
        TraitDerivedAttack echo = Derived("GUN_4_ECHO_Q", GunnerTraitRules.QEchoDerivedKey);
        var plan = new GunnerEchoPlan();
        plan.AddOriginalShot(300);
        Assert.IsFalse(plan.TryBuildEcho(true, SkillEndReason.Completed, echo, out _));

        plan.AddHitTarget(5UL);
        Assert.IsTrue(plan.TryBuildEcho(true, SkillEndReason.Completed, echo, out int damage), "빈 시전이 카운터를 쓰지 않았다");
        Assert.AreEqual(75, damage);
    }

    [Test]
    public void Echo_SameTargetHitManyTimes_OneEntryPerCast()
    {
        // 집속 연사로 같은 적을 5번 맞혀도 시전당 대상 1개.
        var plan = new GunnerEchoPlan();
        for (int shot = 0; shot < 5; shot++)
        {
            plan.AddOriginalShot(84);
            plan.AddHitTarget(7UL);
            if (shot >= 2)
                plan.AddHitTarget(9UL); // 3번째 발부터 들어온 적
        }

        CollectionAssert.AreEqual(new[] { 7UL, 9UL }, plan.HitTargets.ToArray());
        plan.Reset();
        Assert.AreEqual(0, plan.HitTargets.Count, "새 시전은 새 묶음");
    }

    [Test]
    public void Echo_Asset_Q_Ratio025_Delay015_Once()
    {
        TraitDerivedAttack echo = Derived("GUN_4_ECHO_Q", GunnerTraitRules.QEchoDerivedKey);
        Assert.IsTrue(echo.Matches(GunnerTraitRules.QAttackKey));
        Assert.IsFalse(echo.Matches(GunnerTraitRules.BasicAttackKey));
        Assert.AreEqual(0.25f, echo.DamageRatio, 1e-6f);
        Assert.AreEqual(0.15f, echo.Delay, 1e-6f);
        Assert.AreEqual(1, echo.Counter.Required, "매 시전 1회");
        Assert.IsTrue(echo.HitArea.IsEmpty, "판정 도형 없음 — 폭탄은 붙은 대상 하나만 맞는다(할 일 149)");
    }

    [Test]
    public void Echo_UsesPlannedSingleTargetSum_PerBomb()
    {
        TraitDerivedAttack echo = Derived("GUN_4_ECHO_Q", GunnerTraitRules.QEchoDerivedKey);
        var plan = new GunnerEchoPlan();
        // 단발 Q — 원래 발 300. 3명을 맞혀도 폭탄마다 같은 값(나누지 않는다).
        plan.AddOriginalShot(300);
        plan.AddHitTarget(1UL);
        plan.AddHitTarget(2UL);
        plan.AddHitTarget(3UL);
        Assert.IsTrue(plan.TryBuildEcho(true, SkillEndReason.Completed, echo, out int damage));
        Assert.AreEqual(75, damage, "300 × 0.25 — 실제 총 피해·맞힌 수 아님");
    }

    [Test]
    public void Echo_BundleOfOriginalShots_SumsPlanned_FiresOnce()
    {
        // 집속 연사 — 같은 시전 원래 발 5발(84씩)은 합계 420 × 0.25 = 105, 1회.
        TraitDerivedAttack echo = Derived("GUN_4_ECHO_Q", GunnerTraitRules.QEchoDerivedKey);
        var plan = new GunnerEchoPlan();
        for (int i = 0; i < 5; i++)
            plan.AddOriginalShot(84);
        plan.AddHitTarget(4UL);

        Assert.AreEqual(420, plan.PlannedDamageSum);
        Assert.IsTrue(plan.TryBuildEcho(true, SkillEndReason.Completed, echo, out int damage));
        Assert.AreEqual(105, damage);
    }

    [Test]
    public void Echo_NewCast_DoesNotOverwritePendingBombs()
    {
        TraitDerivedAttack echo = Derived("GUN_4_ECHO_Q", GunnerTraitRules.QEchoDerivedKey);
        var plan = new GunnerEchoPlan();
        var bombs = new GunnerEchoBombs();
        var planted = new List<GunnerEchoBomb>();

        plan.AddOriginalShot(200);
        plan.AddHitTarget(7UL);
        Assert.IsTrue(plan.TryBuildEcho(true, SkillEndReason.Completed, echo, out int first));
        bombs.Plant(1.0, echo.Delay, first, plan.HitTargets, planted);

        plan.Reset(); // 새 시전 — 같은 대상에 하나 더
        plan.AddOriginalShot(400);
        plan.AddHitTarget(7UL);
        Assert.IsTrue(plan.TryBuildEcho(true, SkillEndReason.Completed, echo, out int second));
        bombs.Plant(1.05, echo.Delay, second, plan.HitTargets, planted);
        Assert.AreEqual(2, bombs.Count, "같은 대상이어도 이전 시전 폭탄을 덮어쓰지 않는다");

        var due = new List<GunnerEchoBomb>();
        var fizzled = new List<GunnerEchoBomb>();
        bombs.Collect(2.0, _ => true, due, fizzled);
        CollectionAssert.AreEqual(new[] { 50, 100 }, due.Select(b => b.Damage).ToArray(), "이전 폭탄은 이전 시전 피해 그대로");
        Assert.AreNotEqual(due[0].Id, due[1].Id, "폭탄 번호로 구분");
    }

    // ── 잔류 포격 에너지 폭탄 수명(할 일 149) ───────────────────────────

    [Test]
    public void EchoBombs_OnePerTarget_DetonateAfterDelay_OnlyThatTarget()
    {
        var bombs = new GunnerEchoBombs();
        var planted = new List<GunnerEchoBomb>();
        var due = new List<GunnerEchoBomb>();
        var fizzled = new List<GunnerEchoBomb>();

        bombs.Plant(10.0, 0.15f, 75, new[] { 3UL, 4UL, 3UL }, planted);
        CollectionAssert.AreEqual(new[] { 3UL, 4UL }, planted.Select(b => b.TargetId).ToArray(), "대상마다 1개");

        bombs.Collect(10.1, _ => true, due, fizzled);
        Assert.AreEqual(0, due.Count, "지연 전엔 안 터진다");
        Assert.AreEqual(2, bombs.Count);

        bombs.Collect(10.16, _ => true, due, fizzled);
        CollectionAssert.AreEqual(new[] { 3UL, 4UL }, due.Select(b => b.TargetId).ToArray(), "폭탄마다 붙은 대상 하나");
        Assert.IsTrue(due.All(b => b.Damage == 75));
        Assert.AreEqual(0, fizzled.Count);
        Assert.AreEqual(0, bombs.Count, "터지면 사라진다");
    }

    [Test]
    public void EchoBombs_TargetDiesFirst_FizzlesImmediately_NoDamageTarget()
    {
        var bombs = new GunnerEchoBombs();
        var planted = new List<GunnerEchoBomb>();
        var due = new List<GunnerEchoBomb>();
        var fizzled = new List<GunnerEchoBomb>();
        bombs.Plant(0.0, 0.15f, 75, new[] { 1UL, 2UL }, planted);

        // 0.05초에 대상 1 사망·디스폰 — 시각 전이어도 바로 그 자리 빈 폭발, 대상 2 는 그대로.
        bombs.Collect(0.05, id => id != 1UL, due, fizzled);
        Assert.AreEqual(0, due.Count);
        CollectionAssert.AreEqual(new[] { 1UL }, fizzled.Select(b => b.TargetId).ToArray());
        Assert.AreEqual(1, bombs.Count);

        bombs.Collect(0.2, _ => true, due, fizzled);
        CollectionAssert.AreEqual(new[] { 2UL }, due.Select(b => b.TargetId).ToArray());
        Assert.AreEqual(0, fizzled.Count);
    }

    [Test]
    public void EchoBombs_Clear_CancelsWithoutDetonation()
    {
        // 사수 사망·씬 종료 — 터지지 않고 사라진다.
        var bombs = new GunnerEchoBombs();
        var planted = new List<GunnerEchoBomb>();
        var due = new List<GunnerEchoBomb>();
        var fizzled = new List<GunnerEchoBomb>();
        bombs.Plant(0.0, 0.15f, 75, new[] { 1UL }, planted);
        bombs.Clear();
        bombs.Collect(99.0, _ => false, due, fizzled);
        Assert.AreEqual(0, due.Count);
        Assert.AreEqual(0, fizzled.Count);
    }

    [Test]
    public void EchoBombVisuals_UnknownTarget_NoOrb_RemoveReportsNoPosition()
    {
        // 네트워크 없는 EditMode — 대상 못 찾음 = 구체를 띄우지 않고, 빠질 때 폭발 위치도 없다.
        var prefab = new GameObject("EchoBombPrefab");
        try
        {
            var visuals = new GunnerEchoBombVisuals(prefab);
            visuals.Add(1, 123UL);
            visuals.Add(1, 123UL);
            Assert.AreEqual(1, visuals.Count, "같은 폭탄 번호는 한 번");
            Assert.IsFalse(visuals.Remove(1, out _));
            Assert.AreEqual(0, visuals.Count);

            visuals.Sync(new[] { new GunnerEchoBombState(2, 5UL), new GunnerEchoBombState(3, 6UL) });
            Assert.AreEqual(2, visuals.Count, "늦은 합류 — 목록 그대로");
            visuals.Sync(new[] { new GunnerEchoBombState(3, 6UL) });
            Assert.AreEqual(1, visuals.Count, "목록에서 빠진 건 조용히 지운다");
            visuals.Clear();
            Assert.AreEqual(0, visuals.Count);
        }
        finally
        {
            Object.DestroyImmediate(prefab);
        }
    }

    [Test]
    public void EchoBombVisuals_ShowDelayAndOffset_PureRules()
    {
        // 대상 Unit 은 스폰된 NetworkObject 가 필요해 EditMode 에서 못 만든다 — Follow 가 쓰는 순수 규칙만 본다.
        Assert.IsTrue(GunnerEchoBombVisuals.IsShowTime(1f, 1f), "지연 0 = 붙는 그 프레임에 보인다");
        Assert.IsFalse(GunnerEchoBombVisuals.IsShowTime(1.09f, 1.1f), "지연 전에는 구체 없음");
        Assert.IsTrue(GunnerEchoBombVisuals.IsShowTime(1.2f, 1.1f));

        var body = new Vector3(1f, 2f, 3f);
        Assert.AreEqual(body, GunnerEchoBombVisuals.OrbPoint(body, Vector3.zero), "기본 오프셋 0 = 몸 점 그대로");
        Assert.AreEqual(new Vector3(1f, 2.5f, 2.8f), GunnerEchoBombVisuals.OrbPoint(body, new Vector3(0f, 0.5f, -0.2f)), "월드 오프셋을 더한다");
    }

    [Test]
    public void Echo_TooltipFormula_MatchesRuntimeDamage()
    {
        // {dmg} = 현재 Q 원래 발 × 0.25(GunnerTraitTooltip.EchoQDamage) — 단발 Q 의 예정 합계 = 원래 발 1발.
        TraitDerivedAttack echo = Derived("GUN_4_ECHO_Q", GunnerTraitRules.QEchoDerivedKey);
        foreach (int planned in new[] { 1, 99, 300, 517 })
        {
            var plan = new GunnerEchoPlan();
            plan.AddOriginalShot(planned);
            plan.AddHitTarget(1UL);
            plan.TryBuildEcho(true, SkillEndReason.Completed, echo, out int damage);
            SkillTooltipDamage tooltip = GunnerTraitTooltip.DerivedDamage(planned, 1f, 0, 1f, 1f, echo.DamageRatio);
            Assert.AreEqual(tooltip.Minimum, damage, "planned " + planned);
        }
    }

    [Test]
    public void DualOutput_TooltipFormula_MatchesRuntimeDamage()
    {
        TraitDerivedAttack extra = Derived("GUN_4_EXTRA_BASIC", GunnerTraitRules.BasicExtraDerivedKey);
        foreach (int shotDamage in new[] { 1, 37, 100, 263 })
        {
            extra.Counter.Reset();
            GunnerDualOutputRules.CountOriginalShot(extra, shotDamage, 1f, out _, out _);
            GunnerDualOutputRules.CountOriginalShot(extra, shotDamage, 1f, out _, out _);
            GunnerDualOutputRules.CountOriginalShot(extra, shotDamage, 1f, out int damage, out _);
            SkillTooltipDamage tooltip = GunnerTraitTooltip.DerivedDamage(shotDamage, 1f, 0, 1f, 1f, extra.DamageRatio);
            Assert.AreEqual(tooltip.Minimum, damage, "shot " + shotDamage);
        }
    }
}
