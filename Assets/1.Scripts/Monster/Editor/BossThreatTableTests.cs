using NUnit.Framework;

/// <summary>
/// 23호 위협 점수표 — 누적·감쇠·1등·교체 문턱(PLAN-boss-aggro §3.1~3.2).
/// 🔴 교체 규칙의 핵심은 "비등하면 안 바꾼다"(문턱)와 "문 지 얼마 안 됐으면 안 바꾼다"(최소 유지)다.
/// </summary>
public sealed class BossThreatTableTests
{
    const float Ratio = 1.3f, MinHold = 4f, MinScore = 30f;

    [Test]
    public void Add_Accumulates_WithoutDecayAtSameTime()
    {
        var t = new BossThreatTable { HalfLife = 6f };
        t.Add(1, 20f, 0f);
        t.Add(1, 30f, 0f);
        Assert.That(t.Score(1, 0f), Is.EqualTo(50f).Within(1e-4));
    }

    [Test]
    public void Score_HalvesAfterHalfLife()
    {
        var t = new BossThreatTable { HalfLife = 6f };
        t.Add(1, 100f, 0f);
        Assert.That(t.Score(1, 6f), Is.EqualTo(50f).Within(1e-3));
        Assert.That(t.Score(1, 12f), Is.EqualTo(25f).Within(1e-3));
    }

    [Test]
    public void Add_DecaysOldScoreBeforeAdding()
    {
        var t = new BossThreatTable { HalfLife = 6f };
        t.Add(1, 100f, 0f);
        t.Add(1, 10f, 6f);   // 100 → 50 으로 감쇠한 뒤 +10
        Assert.That(t.Score(1, 6f), Is.EqualTo(60f).Within(1e-3));
    }

    [TestCase(0f)]
    [TestCase(-5f)]
    public void Add_IgnoresNonPositive(float amount)
    {
        var t = new BossThreatTable();
        t.Add(1, amount, 0f);
        Assert.That(t.Count, Is.EqualTo(0));
    }

    [Test]
    public void ZeroHalfLife_NoDecay()
    {
        var t = new BossThreatTable { HalfLife = 0f };
        t.Add(1, 40f, 0f);
        Assert.That(t.Score(1, 100f), Is.EqualTo(40f).Within(1e-4));
    }

    // 최근 딜이 옛날 큰 딜을 이긴다 — "방금 많이 때린 사람"이 1등.
    [Test]
    public void TryGetTop_RecentDamageBeatsOldDamage()
    {
        var t = new BossThreatTable { HalfLife = 6f };
        t.Add(1, 200f, 0f);   // 24초 뒤 = 12.5
        t.Add(2, 40f, 24f);
        Assert.That(t.TryGetTop(null, 24f, out int id, out float score), Is.True);
        Assert.That(id, Is.EqualTo(2));
        Assert.That(score, Is.EqualTo(40f).Within(1e-3));
    }

    // 사망·유령 등 무효 후보는 1등이 될 수 없고 표에서 지워진다.
    [Test]
    public void TryGetTop_SkipsAndRemovesInvalid()
    {
        var t = new BossThreatTable { HalfLife = 6f };
        t.Add(1, 500f, 0f);
        t.Add(2, 10f, 0f);
        Assert.That(t.TryGetTop(k => k != 1, 0f, out int id, out _), Is.True);
        Assert.That(id, Is.EqualTo(2));
        Assert.That(t.Count, Is.EqualTo(1));
    }

    [Test]
    public void TryGetTop_EmptyIsFalse()
    {
        Assert.That(new BossThreatTable().TryGetTop(null, 0f, out _, out _), Is.False);
    }

    // ── 교체 판정 ──

    [Test]
    public void ShouldSwitch_NeedsRatio()
    {
        Assert.That(BossThreatTable.ShouldSwitch(100f, 129f, 10f, Ratio, MinHold, MinScore), Is.False, "1.29배 — 비등");
        Assert.That(BossThreatTable.ShouldSwitch(100f, 130f, 10f, Ratio, MinHold, MinScore), Is.True, "1.3배 — 교체");
    }

    [Test]
    public void ShouldSwitch_NeedsMinHold()
    {
        Assert.That(BossThreatTable.ShouldSwitch(0f, 500f, MinHold - 0.01f, Ratio, MinHold, MinScore), Is.False);
        Assert.That(BossThreatTable.ShouldSwitch(0f, 500f, MinHold, Ratio, MinHold, MinScore), Is.True);
    }

    [Test]
    public void ShouldSwitch_NeedsMinScore()
    {
        Assert.That(BossThreatTable.ShouldSwitch(0f, MinScore - 1f, 10f, Ratio, MinHold, MinScore), Is.False, "잽 몇 번");
        Assert.That(BossThreatTable.ShouldSwitch(0f, MinScore, 10f, Ratio, MinHold, MinScore), Is.True);
    }

    // 현재 대상 점수 0(아직 안 때림·근접이 맞기만 함)이면 최소 점수만 넘으면 간다.
    [Test]
    public void ShouldSwitch_CurrentZero_SwitchesAtMinScore()
    {
        Assert.That(BossThreatTable.ShouldSwitch(0f, 31f, 5f, Ratio, MinHold, MinScore), Is.True);
    }

    // 배수 1 미만으로 저작해도 "앞서야 바꾼다"는 지킨다(1 로 클램프).
    [Test]
    public void ShouldSwitch_RatioBelowOneClampedToOne()
    {
        Assert.That(BossThreatTable.ShouldSwitch(100f, 99f, 10f, 0.5f, MinHold, MinScore), Is.False);
    }
}

/// <summary>원거리 압박·교체 가능 상태(PLAN-boss-aggro §3.2~3.3).</summary>
public sealed class BossAggroPolicyThreatTests
{
    [TestCase(MonsterState.Idle, true)]
    [TestCase(MonsterState.Chase, true)]
    [TestCase(MonsterState.Attack, false)]
    [TestCase(MonsterState.Groggy, false)]
    [TestCase(MonsterState.Return, false)]
    public void CanSwitchNow_OnlyIdleOrChase(MonsterState state, bool expected)
    {
        Assert.That(BossAggroPolicy.CanSwitchNow(state), Is.EqualTo(expected));
    }

    [Test]
    public void FarPressure_Boundary()
    {
        Assert.That(BossAggroPolicy.IsFarPressure(7.99f, 8f), Is.False);
        Assert.That(BossAggroPolicy.IsFarPressure(8f, 8f), Is.True);
    }

    [Test]
    public void FarPressure_ZeroDisables()
    {
        Assert.That(BossAggroPolicy.IsFarPressure(100f, 0f), Is.False);
    }
}

/// <summary>일반 몬스터 추적 해제(PLAN-boss-aggro §3.4).</summary>
public sealed class MonsterPerceptionPolicyLoseTargetTests
{
    const float Radius = 9.6f, Grace = 1f;

    [Test]
    public void InsideRadius_Keeps()
    {
        Assert.That(MonsterPerceptionPolicy.ShouldLoseTarget(Radius, Radius, 0f, 99f, Grace), Is.False);
    }

    [Test]
    public void OutsideShorterThanGrace_Keeps()
    {
        Assert.That(MonsterPerceptionPolicy.ShouldLoseTarget(12f, Radius, 10f, 10.99f, Grace), Is.False);
    }

    [Test]
    public void OutsideForGrace_Loses()
    {
        Assert.That(MonsterPerceptionPolicy.ShouldLoseTarget(12f, Radius, 10f, 11f, Grace), Is.True);
    }

    // 아직 밖으로 나간 시각이 안 찍혔으면(-1) 놓지 않는다 — 호출측이 이번 틱에 시각을 찍는다.
    [Test]
    public void NoOutsideStamp_Keeps()
    {
        Assert.That(MonsterPerceptionPolicy.ShouldLoseTarget(50f, Radius, -1f, 99f, Grace), Is.False);
    }

    // 0 = 끔(중간보스·23호) — 아무리 멀어도 리쉬로만 풀린다(이전 동작).
    [TestCase(0f)]
    [TestCase(-1f)]
    public void NonPositiveRadius_Disabled(float radius)
    {
        Assert.That(MonsterPerceptionPolicy.ShouldLoseTarget(999f, radius, 0f, 99f, Grace), Is.False);
    }
}
