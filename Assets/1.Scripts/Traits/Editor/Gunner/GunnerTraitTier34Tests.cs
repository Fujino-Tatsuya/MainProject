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
            plan.AddOriginalShot(Vector3.up, Vector3.forward, 12f, 1.56f, 300);

        Assert.AreEqual(expected, plan.TryBuildEcho(fired, reason, echo, out _));
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
    }

    [Test]
    public void Echo_UsesPlannedSingleTargetSum_AndFirstShotGeometry()
    {
        TraitDerivedAttack echo = Derived("GUN_4_ECHO_Q", GunnerTraitRules.QEchoDerivedKey);
        var plan = new GunnerEchoPlan();
        // 단발 Q — 원래 발 300(맞힌 적이 0명이든 3명이든 같은 값).
        plan.AddOriginalShot(new Vector3(1f, 1f, 0f), Vector3.right, 12f, 1.56f, 300);
        Assert.IsTrue(plan.TryBuildEcho(true, SkillEndReason.Completed, echo, out GunnerDerivedShot shot));
        Assert.AreEqual(75, shot.Damage, "300 × 0.25 — 실제 총 피해·맞힌 수 아님");
        Assert.AreEqual(new Vector3(1f, 1f, 0f), shot.Origin);
        Assert.AreEqual(Vector3.right, shot.Direction);
        Assert.AreEqual(12f, shot.Range, "최종 충전 사거리(지형 차단 전) — 지형은 발사 때 다시 잰다");
        Assert.AreEqual(1.56f, shot.Width, 1e-6f, "광폭 반영 폭");
    }

    [Test]
    public void Echo_BundleOfOriginalShots_SumsPlanned_KeepsFirstShot_FiresOnce()
    {
        // 4/4 집속 연사 대비 — 같은 시전 원래 발 5발(84씩)은 합계 420 × 0.25 = 105, 1회.
        TraitDerivedAttack echo = Derived("GUN_4_ECHO_Q", GunnerTraitRules.QEchoDerivedKey);
        var plan = new GunnerEchoPlan();
        plan.AddOriginalShot(Vector3.zero, Vector3.forward, 10f, 1.2f, 84);
        for (int i = 1; i < 5; i++)
            plan.AddOriginalShot(Vector3.one * i, Vector3.left, 99f, 9f, 84);

        Assert.AreEqual(420, plan.PlannedDamageSum);
        Assert.IsTrue(plan.TryBuildEcho(true, SkillEndReason.Completed, echo, out GunnerDerivedShot shot));
        Assert.AreEqual(105, shot.Damage);
        Assert.AreEqual(Vector3.forward, shot.Direction, "첫 발 방향");
        Assert.AreEqual(10f, shot.Range);
    }

    [Test]
    public void Echo_NewCast_DoesNotOverwritePendingEcho()
    {
        TraitDerivedAttack echo = Derived("GUN_4_ECHO_Q", GunnerTraitRules.QEchoDerivedKey);
        var plan = new GunnerEchoPlan();
        var queue = new GunnerDelayedShots();

        plan.AddOriginalShot(Vector3.zero, Vector3.forward, 10f, 1.2f, 200);
        Assert.IsTrue(plan.TryBuildEcho(true, SkillEndReason.Completed, echo, out GunnerDerivedShot first));
        queue.Schedule(1.0, echo.Delay, first);

        plan.Reset(); // 새 시전
        plan.AddOriginalShot(Vector3.zero, Vector3.back, 6f, 1.2f, 400);
        Assert.IsTrue(plan.TryBuildEcho(true, SkillEndReason.Completed, echo, out GunnerDerivedShot second));
        queue.Schedule(1.05, echo.Delay, second);

        var due = new List<GunnerDerivedShot>();
        queue.CollectDue(2.0, due);
        CollectionAssert.AreEqual(new[] { 50, 100 }, due.Select(s => s.Damage).ToArray());
        Assert.AreEqual(Vector3.forward, due[0].Direction, "이전 예약은 이전 시전 스냅샷 그대로");
    }

    [Test]
    public void Echo_TooltipFormula_MatchesRuntimeDamage()
    {
        // {dmg} = 현재 Q 원래 발 × 0.25(GunnerTraitTooltip.EchoQDamage) — 단발 Q 의 예정 합계 = 원래 발 1발.
        TraitDerivedAttack echo = Derived("GUN_4_ECHO_Q", GunnerTraitRules.QEchoDerivedKey);
        foreach (int planned in new[] { 1, 99, 300, 517 })
        {
            var plan = new GunnerEchoPlan();
            plan.AddOriginalShot(Vector3.zero, Vector3.forward, 10f, 1.2f, planned);
            plan.TryBuildEcho(true, SkillEndReason.Completed, echo, out GunnerDerivedShot shot);
            SkillTooltipDamage tooltip = GunnerTraitTooltip.DerivedDamage(planned, 1f, 0, 1f, 1f, echo.DamageRatio);
            Assert.AreEqual(tooltip.Minimum, shot.Damage, "planned " + planned);
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
