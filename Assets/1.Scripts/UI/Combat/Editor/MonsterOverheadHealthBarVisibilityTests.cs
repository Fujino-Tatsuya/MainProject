using NUnit.Framework;

/// <summary>몬스터 머리 위 체력바 표시 판단 고정 — 피격 표시, 전투 종료 + 지연 숨김, 재피격 리셋, 사망 즉시 숨김, 중간보스 배율.</summary>
public sealed class MonsterOverheadHealthBarVisibilityTests
{
    const float Delay = 3f;

    static MonsterOverheadHealthBarVisibility HitAt(float time, MonsterState state)
    {
        var v = new MonsterOverheadHealthBarVisibility();
        v.Tick(0f, 100, MonsterState.Idle, Delay);
        v.Tick(time, 90, state, Delay);
        return v;
    }

    [Test]
    public void Initially_Hidden_EvenInCombat()
    {
        var v = new MonsterOverheadHealthBarVisibility();

        Assert.That(v.Tick(0f, 100, MonsterState.Chase, Delay), Is.False);
        Assert.That(v.Tick(1f, 100, MonsterState.Attack, Delay), Is.False, "맞기 전엔 전투 중이어도 숨김");
    }

    [Test]
    public void Damage_Shows()
    {
        var v = new MonsterOverheadHealthBarVisibility();
        v.Tick(0f, 100, MonsterState.Idle, Delay);

        Assert.That(v.Tick(0.1f, 90, MonsterState.Hit, Delay), Is.True);
        Assert.That(v.IsShown, Is.True);
    }

    [Test]
    public void HealthIncrease_DoesNotShow()
    {
        var v = new MonsterOverheadHealthBarVisibility();
        v.Tick(0f, 50, MonsterState.Idle, Delay);

        Assert.That(v.Tick(0.1f, 100, MonsterState.Return, Delay), Is.False, "회복(리쉬 복귀 Revive)은 피격이 아니다");
    }

    [Test]
    public void StaysWhileInCombat()
    {
        var v = HitAt(1f, MonsterState.Chase);

        Assert.That(v.Tick(10f, 90, MonsterState.Chase, Delay), Is.True);
        Assert.That(v.Tick(20f, 90, MonsterState.Attack, Delay), Is.True, "전투 상태 동안은 시간이 지나도 유지");
    }

    [Test]
    public void CombatEnds_KeepsUntilDelay_ThenHides()
    {
        var v = HitAt(1f, MonsterState.Chase);
        v.Tick(5f, 90, MonsterState.Chase, Delay);       // 마지막 전투 프레임

        Assert.That(v.Tick(5.1f, 90, MonsterState.Idle, Delay), Is.True, "추격 종료 직후 유지");
        Assert.That(v.Tick(7.9f, 90, MonsterState.Return, Delay), Is.True, "3초 전엔 유지");
        Assert.That(v.Tick(8.0f, 90, MonsterState.Idle, Delay), Is.False, "추격 종료 + 3초 → 숨김");
    }

    [Test]
    public void HitOutsideCombat_HidesDelayAfterHit()
    {
        var v = HitAt(1f, MonsterState.Idle);

        Assert.That(v.Tick(3.9f, 90, MonsterState.Idle, Delay), Is.True);
        Assert.That(v.Tick(4.0f, 90, MonsterState.Idle, Delay), Is.False);
    }

    [Test]
    public void ReHit_ResetsTimer()
    {
        var v = HitAt(1f, MonsterState.Idle);

        Assert.That(v.Tick(3.5f, 80, MonsterState.Idle, Delay), Is.True, "재피격");
        Assert.That(v.Tick(6.4f, 80, MonsterState.Idle, Delay), Is.True, "재피격 기준 3초 전이라 유지");
        Assert.That(v.Tick(6.5f, 80, MonsterState.Idle, Delay), Is.False);
    }

    [Test]
    public void ReturnToCombat_AfterHide_StaysHiddenUntilHit()
    {
        var v = HitAt(1f, MonsterState.Idle);
        v.Tick(5f, 90, MonsterState.Idle, Delay);

        Assert.That(v.Tick(6f, 90, MonsterState.Chase, Delay), Is.False, "다시 추격만 해서는 켜지지 않는다");
        Assert.That(v.Tick(6.1f, 85, MonsterState.Hit, Delay), Is.True);
    }

    [Test]
    public void DeadState_HidesImmediately()
    {
        var v = HitAt(1f, MonsterState.Chase);

        Assert.That(v.Tick(1.1f, 10, MonsterState.Dead, Delay), Is.False);
    }

    [Test]
    public void ZeroHealth_HidesImmediately_BeforeDeadReplicates()
    {
        var v = HitAt(1f, MonsterState.Chase);

        Assert.That(v.Tick(1.1f, 0, MonsterState.Hit, Delay), Is.False);
    }

    [Test]
    public void Reset_HidesAndForgetsHealth()
    {
        var v = HitAt(1f, MonsterState.Chase);
        v.Reset();

        Assert.That(v.IsShown, Is.False);
        Assert.That(v.Tick(2f, 50, MonsterState.Chase, Delay), Is.False, "리셋 직후 첫 값은 기준값 — 피격으로 보지 않는다");
    }

    [TestCase(MonsterState.Chase, true)]
    [TestCase(MonsterState.Attack, true)]
    [TestCase(MonsterState.Hit, true)]
    [TestCase(MonsterState.Groggy, true)]
    [TestCase(MonsterState.Knockback, true)]
    [TestCase(MonsterState.Idle, false)]
    [TestCase(MonsterState.Return, false)]
    [TestCase(MonsterState.Dead, false)]
    public void CombatStates(MonsterState state, bool expected)
    {
        Assert.That(MonsterOverheadHealthBarVisibility.IsCombatState(state), Is.EqualTo(expected));
    }

    [Test]
    public void HeightScale_OnlyMidBoss()
    {
        Assert.That(MonsterOverheadHealthBarVisibility.HeightScale(MonsterRank.MidBoss, 1.5f), Is.EqualTo(1.5f));
        Assert.That(MonsterOverheadHealthBarVisibility.HeightScale(MonsterRank.Normal, 1.5f), Is.EqualTo(1f));
    }

    [Test]
    public void ShouldAttach_ExcludesFinalBossAndExistingBar()
    {
        Assert.That(MonsterOverheadHealthBarVisibility.ShouldAttach(MonsterRank.Normal, false, false), Is.True);
        Assert.That(MonsterOverheadHealthBarVisibility.ShouldAttach(MonsterRank.MidBoss, false, false), Is.True);
        Assert.That(MonsterOverheadHealthBarVisibility.ShouldAttach(MonsterRank.Boss, false, false), Is.False);
        Assert.That(MonsterOverheadHealthBarVisibility.ShouldAttach(MonsterRank.Normal, true, false), Is.False, "BossHudTarget");
        Assert.That(MonsterOverheadHealthBarVisibility.ShouldAttach(MonsterRank.Normal, false, true), Is.False, "이미 바가 있는 프리팹");
    }
}
