using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 거너 3차 집속 연사(GUN_3_VOLLEY) 수용 조건(할 일 127 4/4, 명세 거너 집속 연사 절 + 11장 거너 행).
/// 5발 시각·발당 0.28·총 1.40·첫 발 스냅샷 공유·축열 1회 소모·쿨 1회·중단 시 남은 발 취소·보호막 시전당 1회·충격 레이저 대상별 1회·
/// 잔류 포격 1회(5연사 끝 뒤, 중단 시 없음)·공격 가속(간격 O, 충전 X)·특성 없을 때 단발 회귀.
/// 일정·수치·장부는 순수 규칙(<c>Traits/Gunner/GunnerVolleyRules.cs</c>)으로, 스킬과 같은 호출 순서로 본다. 물리 질의·네트워크·VFX 는 Play 확인 몫이다.
/// </summary>
public class GunnerTraitTier3VolleyTests
{
    private const string Folder = "Assets/9.ScriptableObject/Traits/Gunner/";

    private static GunnerTraitDefinition Load(string id)
    {
        var def = AssetDatabase.LoadAssetAtPath<GunnerTraitDefinition>(Folder + id + ".asset");
        Assert.IsNotNull(def, Folder + id + ".asset");
        return def;
    }

    private static TraitModifierSet With(params string[] ids)
    {
        TraitDefinition[] defs = ids.Select(id => (TraitDefinition)Load(id)).ToArray();
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

    private static GunnerVolleyTraits Volley() => GunnerVolleyTraits.From(With("GUN_3_VOLLEY"));

    // 스킬(Fire → OnTick)과 같은 순서로 일정을 끝까지 돌려 각 발 시각을 모은다. tick = 서버 틱 간격.
    private static List<double> RunSchedule(GunnerVolleySchedule schedule, double start, float tick, double until)
    {
        var times = new List<double> { start };
        for (double now = start; now <= until + 1e-9; now += tick)
        {
            while (schedule.TryTakeDue(now, out _))
                times.Add(now);
        }

        return times;
    }

    // ── 수치 ────────────────────────────────────────────────────────────

    [Test]
    public void Volley_Asset_Five_015s_PerShot028_Total140_OnlyWhenHeld()
    {
        GunnerVolleyTraits v = Volley();
        Assert.IsTrue(v.Active);
        Assert.AreEqual(5, v.ShotCount);
        Assert.AreEqual(0.15f, v.ShotInterval, 1e-6f);
        Assert.AreEqual(0.28f, v.PerShotDamageRatio, 1e-6f);
        Assert.AreEqual(1.40f, v.TotalDamageRatio, 1e-5f, "표시용 검증값 = 발수 × 발당 배율");
        Assert.AreEqual(v.TotalDamageRatio, GunnerTraitTooltip.VolleyTotalRatio(Load("GUN_3_VOLLEY")), 1e-6f, "카드 별칭 totalDamageRatio 와 같은 값");

        // 다른 Q 특성(수치만 있고 모드 스위치 없음)으로는 켜지지 않는다.
        Assert.IsFalse(GunnerVolleyTraits.From(With("GUN_3_Q_PUSH", "GUN_4_ECHO_Q")).Active);
        Assert.IsFalse(GunnerVolleyTraits.From(None()).Active);
        Assert.IsFalse(GunnerVolleyTraits.From((TraitModifierSet)null).Active);
    }

    // ── 일정 ────────────────────────────────────────────────────────────

    [Test]
    public void Volley_FiresAt_0_015_030_045_060()
    {
        GunnerVolleyTraits v = Volley();
        var schedule = new GunnerVolleySchedule();
        schedule.BeginFirstShot(10d, v.ShotCount, v.IntervalAt(1f));

        for (int k = 0; k < 5; k++)
            Assert.AreEqual(10d + 0.15d * k, schedule.DueTime(k), 1e-5, "발 " + k);

        // 첫 발은 BeginFirstShot 이 낸다(스킬 Fire). 시각 전에는 다음 발이 나오지 않는다.
        Assert.AreEqual(1, schedule.ShotsFired);
        Assert.IsFalse(schedule.TryTakeDue(10.149d, out _));
        Assert.IsTrue(schedule.TryTakeDue(10.15d, out int second));
        Assert.AreEqual(1, second);

        // 틱이 늦으면 지난 발을 같은 틱에 순서대로 낸다(발수는 줄지 않는다).
        var late = new List<int>();
        while (schedule.TryTakeDue(10.61d, out int index))
            late.Add(index);
        CollectionAssert.AreEqual(new[] { 2, 3, 4 }, late);
        Assert.IsTrue(schedule.AllShotsFired);
        Assert.IsFalse(schedule.HasPending);
        Assert.IsFalse(schedule.TryTakeDue(99d, out _), "5발 뒤 더 없음");
    }

    [Test]
    public void Volley_ServerTicks_FireFiveShots_EndAt060()
    {
        var schedule = new GunnerVolleySchedule();
        schedule.BeginFirstShot(0d, 5, 0.15f);
        List<double> times = RunSchedule(schedule, 0d, 1f / 60f, 1d);
        Assert.AreEqual(5, times.Count);
        for (int k = 0; k < 5; k++)
            Assert.AreEqual(0.15d * k, times[k], 1d / 60d + 1e-6, "발 " + k + " — 한 틱 안");
    }

    [Test]
    public void Volley_ExtraDuration_ForSafetyNet_Is060()
    {
        Assert.AreEqual(0.6f, Volley().ExtraDuration(1f), 1e-5f, "(5 − 1) × 0.15");
        Assert.AreEqual(0f, GunnerVolleyTraits.None.ExtraDuration(1f), 1e-6f, "단발 — 안전망 그대로");
    }

    // ── 피해·스냅샷 ─────────────────────────────────────────────────────

    [Test]
    public void Volley_PerShot028_FiveShotsTotal140()
    {
        GunnerVolleyTraits v = Volley();
        var cast = new GunnerQCastSnapshot();
        cast.Begin(0);
        cast.CommitFirstShot(GunnerHeatTraits.None, 0f);

        int single = cast.ShotDamage(1000, 1f, 1f);
        int perShot = cast.ShotDamage(1000, v.ShotChargeScale(1f), 1f);
        Assert.AreEqual(1000, single);
        Assert.AreEqual(280, perShot, "원래 Q × 0.28");
        Assert.AreEqual(1400, perShot * v.ShotCount, "모두 맞히면 1.40");
    }

    [Test]
    public void Volley_ChargeDecidesPerShotDamage_NotShotCount()
    {
        GunnerVolleyTraits v = Volley();
        var cast = new GunnerQCastSnapshot();
        cast.Begin(0);
        cast.CommitFirstShot(GunnerHeatTraits.None, 0f);

        // 충전 배율 0.5 / 2.0 — 발수는 둘 다 5, 발당 피해만 다르다(중간 반올림 없이 마지막 한 번).
        Assert.AreEqual(Mathf.RoundToInt(1000 * 0.5f * 0.28f), cast.ShotDamage(1000, v.ShotChargeScale(0.5f), 1f));
        Assert.AreEqual(Mathf.RoundToInt(1000 * 2f * 0.28f), cast.ShotDamage(1000, v.ShotChargeScale(2f), 1f));
        Assert.AreEqual(5, v.ShotCount);
    }

    [Test]
    public void Volley_SnapshotShared_HeatShotSpendsOnce_LaterHeatIgnored()
    {
        // 축열 포격 + 집속 연사 — 첫 발 직전 열 80% 소모 → 모든 원래 발이 같은 1 + 0.6 × 0.8 배율.
        TraitModifierSet set = With("GUN_3_VOLLEY", "GUN_2_HEAT_SHOT");
        GunnerHeatTraits heat = GunnerHeatTraits.From(set);
        GunnerVolleyTraits v = GunnerVolleyTraits.From(set);
        var cast = new GunnerQCastSnapshot();
        cast.Begin(2);

        int spends = 0;
        var damages = new List<int>();
        for (int shot = 0; shot < v.ShotCount; shot++)
        {
            // 스킬: 첫 발(Fire)만 FirstShotCommitted 를 보고 열을 소모한다. 이후 발 사이의 열 변화(0.6, 0.1 …)는 무시.
            if (!cast.FirstShotCommitted)
            {
                spends++;
                Assert.IsTrue(cast.CommitFirstShot(heat, 0.8f));
            }
            else
            {
                Assert.IsFalse(cast.CommitFirstShot(heat, 0.1f * shot), "후속 발은 새로 소모·계산하지 않는다");
            }

            damages.Add(cast.ShotDamage(500, v.ShotChargeScale(1f), 1f));
        }

        Assert.AreEqual(1, spends, "축열 소모는 첫 발 1회");
        Assert.AreEqual(0.8f, cast.HeatSpentRatio, 1e-6f);
        Assert.AreEqual(1.48f, cast.HeatShotScale, 1e-5f);
        Assert.AreEqual(1, damages.Distinct().Count(), "모든 발 같은 피해: " + string.Join(",", damages));
        Assert.AreEqual(Mathf.RoundToInt(500 * 0.28f * 1f * 1.48f), damages[0]);
        Assert.AreEqual(2, cast.StoredStage, "시전 시작 단계 그대로");
    }

    [Test]
    public void Volley_CooldownAndFireEvent_OnlyOnFirstShotIndex()
    {
        // 스킬은 index 0(BeginFirstShot 직후 FireOriginalShot(0))에서만 쿨타임·집중 해제·발사 사건을 낸다.
        var schedule = new GunnerVolleySchedule();
        schedule.BeginFirstShot(0d, 5, 0.15f);
        var indices = new List<int> { 0 };
        for (double now = 0d; now <= 1d; now += 0.05d)
        {
            while (schedule.TryTakeDue(now, out int index))
                indices.Add(index);
        }

        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, indices);
        Assert.AreEqual(1, indices.Count(i => i == 0), "쿨타임 시작은 1회");
    }

    // ── 중단 ────────────────────────────────────────────────────────────

    [Test]
    public void Volley_Cancel_DropsRemainingShots_NoEcho()
    {
        TraitDerivedAttack echo = Derived("GUN_4_ECHO_Q", GunnerTraitRules.QEchoDerivedKey);
        var schedule = new GunnerVolleySchedule();
        var plan = new GunnerEchoPlan();
        schedule.BeginFirstShot(0d, 5, 0.15f);
        plan.AddOriginalShot(Vector3.zero, Vector3.forward, 10f, 1.2f, 84);
        Assert.IsTrue(schedule.TryTakeDue(0.15d, out _));
        plan.AddOriginalShot(Vector3.zero, Vector3.forward, 10f, 1.2f, 84);

        // 2발 뒤 사망·강제 중단(OnEnd) — 남은 발 취소.
        Assert.IsTrue(schedule.HasPending);
        schedule.Cancel();
        Assert.IsFalse(schedule.HasPending);
        Assert.IsFalse(schedule.TryTakeDue(5d, out _), "남은 발 없음");
        Assert.AreEqual(2, schedule.ShotsFired, "이미 쏜 발(쿨·소모 열)은 그대로");
        Assert.IsFalse(schedule.AllShotsFired);

        // 잔류 포격 — 스킬은 fired && AllShotsFired 로 묻는다. 끝 사유가 무엇이든(안전망 포함) 도중 중단이면 없음.
        foreach (SkillEndReason reason in new[] { SkillEndReason.CasterDied, SkillEndReason.Cancelled, SkillEndReason.MaxDurationReached, SkillEndReason.Completed })
            Assert.IsFalse(plan.TryBuildEcho(schedule.AllShotsFired, reason, echo, out _), reason.ToString());
    }

    // ── 보호막 ──────────────────────────────────────────────────────────

    [Test]
    public void Volley_Shield_OncePerAllyPerCast_NotSplitNotSummed()
    {
        var gate = new GunnerCastShieldGate();
        const int allyA = 11;
        const int allyB = 22;
        int givenA = 0;
        int givenB = 0;
        for (int shot = 0; shot < 5; shot++)
        {
            if (gate.TryGive(allyA))
                givenA++;
            // B 는 3번째 발부터 빔 안에 들어온다.
            if (shot >= 2 && gate.TryGive(allyB))
                givenB++;
        }

        Assert.AreEqual(1, givenA, "같은 아군 시전당 1회(5회 합산 아님)");
        Assert.AreEqual(1, givenB, "늦게 들어온 아군도 1회");

        // 보호막량은 발당 배율로 나누지 않는다 — 스킬은 원래 보호막량(단계 배율만)을 그대로 준다.
        gate.Reset(); // 새 시전
        Assert.IsTrue(gate.TryGive(allyA));
    }

    // ── 연동 ────────────────────────────────────────────────────────────

    [Test]
    public void Volley_ImpactLaser_SameCastId_OncePerTarget_ImmuneThenLaterShot()
    {
        var gate = new GunnerCastCcGate();
        const int castId = 7;
        const int normal = 1;
        const int immuneFirst = 2;
        int requestsNormal = 0;
        int requestsImmune = 0;
        for (int shot = 0; shot < 5; shot++)
        {
            if (gate.ShouldRequest(castId, normal, false))
            {
                requestsNormal++;
                gate.MarkApproved(castId, normal);
            }

            // 1·2번째 발에는 슈퍼아머(면역) — 요청하지 않는다. 3번째 발에서 처음 승인, 이후 없음.
            bool immune = shot < 2;
            if (gate.ShouldRequest(castId, immuneFirst, immune))
            {
                requestsImmune++;
                Assert.AreEqual(2, shot, "취약해진 첫 원래 발에서 승인");
                gate.MarkApproved(castId, immuneFirst);
            }
        }

        Assert.AreEqual(1, requestsNormal, "5연사도 대상별 1회");
        Assert.AreEqual(1, requestsImmune);
        Assert.IsTrue(gate.ShouldRequest(castId + 1, normal, false), "다음 시전은 새로");
    }

    [Test]
    public void Volley_Echo_OnceAfterFifthShot_SumOfPlannedPerShot()
    {
        TraitDerivedAttack echo = Derived("GUN_4_ECHO_Q", GunnerTraitRules.QEchoDerivedKey);
        GunnerVolleyTraits v = Volley();
        var cast = new GunnerQCastSnapshot();
        cast.Begin(0);
        cast.CommitFirstShot(GunnerHeatTraits.None, 0f);
        int perShot = cast.ShotDamage(300, v.ShotChargeScale(1f), 1f); // 84

        var schedule = new GunnerVolleySchedule();
        var plan = new GunnerEchoPlan();
        schedule.BeginFirstShot(0d, v.ShotCount, v.IntervalAt(1f));
        plan.AddOriginalShot(new Vector3(1f, 1f, 0f), Vector3.right, 12f, 1.56f, perShot);

        // 마지막 발 전 정상 종료 이벤트는 스킬이 무시한다(HasPending) — 여기서는 잔류 포격 자격도 없다.
        Assert.IsFalse(plan.TryBuildEcho(schedule.AllShotsFired, SkillEndReason.Completed, echo, out _));
        echo.Counter.Reset();

        while (schedule.TryTakeDue(1d, out _))
            plan.AddOriginalShot(new Vector3(9f, 1f, 9f), Vector3.left, 3f, 9f, perShot);

        Assert.AreEqual(84, perShot);
        Assert.AreEqual(420, plan.PlannedDamageSum, "5발 예정 단일 대상 피해 합(맞힌 수 무관)");
        Assert.IsTrue(plan.TryBuildEcho(schedule.AllShotsFired, SkillEndReason.Completed, echo, out GunnerDerivedShot shot));
        Assert.AreEqual(105, shot.Damage, "420 × 0.25");
        Assert.AreEqual(Vector3.right, shot.Direction, "첫 발 방향");
        Assert.AreEqual(12f, shot.Range, "첫 발 최종 충전 사거리");
        Assert.AreEqual(1.56f, shot.Width, 1e-6f);
    }

    [Test]
    public void Volley_EchoTooltip_MatchesRuntimeBundle()
    {
        // {dmg} 잔류 포격 = 발수 × round(원래 발 × 발당 배율) × 0.25 — 판정의 예정 합계와 같은 식.
        TraitDerivedAttack echo = Derived("GUN_4_ECHO_Q", GunnerTraitRules.QEchoDerivedKey);
        GunnerVolleyTraits v = Volley();
        foreach (int dref in new[] { 1, 99, 300, 517, 1000 })
        {
            var cast = new GunnerQCastSnapshot();
            cast.Begin(0);
            cast.CommitFirstShot(GunnerHeatTraits.None, 0f);
            var plan = new GunnerEchoPlan();
            for (int i = 0; i < v.ShotCount; i++)
                plan.AddOriginalShot(Vector3.zero, Vector3.forward, 10f, 1.2f, cast.ShotDamage(dref, v.ShotChargeScale(1f), 1f));
            echo.Counter.Reset();
            plan.TryBuildEcho(true, SkillEndReason.Completed, echo, out GunnerDerivedShot shot);

            SkillTooltipDamage tooltip = GunnerTraitTooltip.DerivedDamage(dref, 1f, 0, 1f, 1f, echo.DamageRatio, v.ShotCount, v.PerShotDamageRatio);
            Assert.AreEqual(shot.Damage, tooltip.Minimum, "dref " + dref);
            Assert.AreEqual(1f * v.TotalDamageRatio * echo.DamageRatio, tooltip.MinimumCoefficient, 1e-5f, "계수 = 1.40 × 0.25");
        }
    }

    // ── 공격 가속 ───────────────────────────────────────────────────────

    [Test]
    public void Volley_AttackSpeed_ShortensInterval_NotChargeTime()
    {
        GunnerVolleyTraits v = Volley();
        Assert.AreEqual(0.15f, v.IntervalAt(1f), 1e-6f);
        Assert.AreEqual(0.075f, v.IntervalAt(2f), 1e-6f, "공격 가속 ×2 → 간격 ÷2");
        Assert.AreEqual(0.3f, v.ExtraDuration(2f), 1e-5f);

        // 충전시간은 공격 가속과 무관 — 집속 연사는 Q 최대 충전시간 수치를 건드리지 않는다.
        TraitModifierSet set = With("GUN_3_VOLLEY");
        Assert.AreEqual(1.2f, set.ResolveSkillValue(GunnerTraitRules.QMaxChargeTime, 1.2f), 1e-6f, "충전시간 그대로(공격 가속 인자 없음)");

        var schedule = new GunnerVolleySchedule();
        schedule.BeginFirstShot(0d, v.ShotCount, v.IntervalAt(2f));
        Assert.AreEqual(0.3d, schedule.DueTime(4), 1e-5, "마지막 발 0.30초");
    }

    // ── 회귀 ────────────────────────────────────────────────────────────

    [Test]
    public void NoVolley_SingleShot_Regression()
    {
        GunnerVolleyTraits none = GunnerVolleyTraits.From(None());
        Assert.IsFalse(none.Active);
        Assert.AreEqual(1, none.ShotCount);
        foreach (float charge in new[] { 0.5f, 1f, 1.37f, 2f })
            Assert.AreEqual(charge, none.ShotChargeScale(charge), "단발 피해 배율 그대로(정확히 같은 float)");

        var schedule = new GunnerVolleySchedule();
        schedule.BeginFirstShot(3d, none.ShotCount, none.IntervalAt(1f));
        Assert.IsTrue(schedule.AllShotsFired, "단발 = 첫 발로 끝 — 잔류 포격 자격 그대로");
        Assert.IsFalse(schedule.HasPending, "End 이벤트를 막지 않는다");
        Assert.IsFalse(schedule.TryTakeDue(10d, out _));

        // 단발 잔류 포격 {dmg} 는 이전 식과 같다.
        TraitDerivedAttack echo = Derived("GUN_4_ECHO_Q", GunnerTraitRules.QEchoDerivedKey);
        foreach (int planned in new[] { 1, 99, 300, 517 })
        {
            SkillTooltipDamage withDefaults = GunnerTraitTooltip.DerivedDamage(planned, 1.3f, 7, 0.8f, 2.1f, echo.DamageRatio);
            SkillTooltipDamage explicitSingle = GunnerTraitTooltip.DerivedDamage(planned, 1.3f, 7, 0.8f, 2.1f, echo.DamageRatio, 1, 1f);
            Assert.AreEqual(TraitDerivedAttackRules.Damage(Mathf.RoundToInt(planned * 0.8f), echo.DamageRatio), withDefaults.Minimum);
            Assert.AreEqual(TraitDerivedAttackRules.Damage(Mathf.RoundToInt(planned * 2.1f), echo.DamageRatio), withDefaults.Maximum);
            Assert.AreEqual(withDefaults.Minimum, explicitSingle.Minimum);
            Assert.AreEqual(withDefaults.Maximum, explicitSingle.Maximum);
        }
    }
}
