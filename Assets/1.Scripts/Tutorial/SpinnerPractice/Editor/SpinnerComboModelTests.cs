using NUnit.Framework;

/// <summary>
/// 튜토리얼 스피너봇 연계 연습(v8 §3.5) — 간파 → 기절 → 취약 유지 → 우클릭 밀치기 → 벽 충돌 → 회복 × 3.
/// 회복 끝 = 그 자리에서 다시 준비·간파(사용자 변경 — 원위치 복귀 없음), 3회째 회복 끝 = 완료.
/// 할 일 32: 벽 충돌 = 공용 벽 충돌 이벤트(UnitWallImpact) 여부 — 거리·방향 판정은 UnitWallImpactJudgeTests 가 맡는다.
/// </summary>
public sealed class SpinnerComboModelTests
{
    // 기절 1초 · 회복 0.5초 · 밀치기 3m/0.3초(10m/s)
    static SpinnerComboModel New(int target = 3) => new SpinnerComboModel(target, 1f, 0.5f, 3f, 0.3f);

    static void ToVulnerable(SpinnerComboModel m)
    {
        Assert.IsTrue(m.NotifyCounterSucceeded());
        Assert.AreEqual(SpinnerComboEvent.StunEnded, m.Tick(1.01f));
    }

    // 밀치기를 끝까지 보낸다. 누적 이동이 impactAt(m) 에 이르면 벽 충돌 이벤트가 났다고 보고한다. blockedAt 에 이르면 더 못 움직인다.
    static SpinnerComboEvent PushAll(SpinnerComboModel m, float impactAt = float.PositiveInfinity, float blockedAt = float.PositiveInfinity)
    {
        SpinnerComboEvent all = SpinnerComboEvent.None;
        float travelled = 0f;
        for (int i = 0; i < 100 && m.IsPushing; i++)
        {
            float step = m.PushStep(0.05f);
            float moved = System.Math.Max(0f, System.Math.Min(step, blockedAt - travelled));
            travelled += moved;
            all |= m.ReportPushMoved(step, moved, travelled >= impactAt);
        }
        return all;
    }

    [Test]
    public void 간파_성공은_준비_단계에서만_받고_기절_뒤_취약이_유지된다()
    {
        var m = New();
        Assert.IsTrue(m.AllowsSpin);
        Assert.IsTrue(m.NotifyCounterSucceeded());
        Assert.AreEqual(SpinnerComboPhase.Stunned, m.Phase);
        Assert.IsFalse(m.NotifyCounterSucceeded(), "취약 중 중복 간파 없음");
        Assert.IsFalse(m.AllowsSpin, "취약·회복 중 새 간파 공격 예고 없음");
        Assert.IsFalse(m.SuspendsFsm, "기절 동안은 원래 그로기");
        Assert.IsTrue(m.BlocksExternalKnockback);

        Assert.AreEqual(SpinnerComboEvent.None, m.Tick(0.5f));
        Assert.AreEqual(SpinnerComboEvent.StunEnded, m.Tick(0.6f));
        Assert.AreEqual(SpinnerComboPhase.Vulnerable, m.Phase);
        Assert.IsTrue(m.SuspendsFsm, "스스로 이동·추격·공격 정지");

        Assert.AreEqual(SpinnerComboEvent.None, m.Tick(999f), "취약은 시간으로 끝나지 않는다");
        Assert.IsTrue(m.IsVulnerable);
    }

    [Test]
    public void 밀치기는_취약_중에만()
    {
        var m = New();
        Assert.IsFalse(m.TryBeginPush(1f, 0f), "준비 단계");
        m.NotifyCounterSucceeded();
        Assert.IsTrue(m.TryBeginPush(1f, 0f), "기절 중에도 밀 수 있다");
        Assert.IsFalse(m.TryBeginPush(0f, 0f), "방향 없음");
    }

    [Test]
    public void 밀치기_중_벽_충돌_이벤트면_연계_1회_회복_뒤_그_자리에서_다시_준비()
    {
        var m = New();
        ToVulnerable(m);
        Assert.IsTrue(m.TryBeginPush(1f, 0f));
        SpinnerComboEvent e = PushAll(m, impactAt: 2.1f);
        Assert.AreEqual(SpinnerComboEvent.WallHit, e);
        Assert.AreEqual(1, m.Successes);
        Assert.AreEqual(SpinnerComboPhase.Recovering, m.Phase);
        Assert.IsFalse(m.IsVulnerable, "충돌로 취약 종료");
        Assert.IsFalse(m.TryBeginPush(1f, 0f), "회복 중 외부 밀치기 없음");
        Assert.IsFalse(m.NotifyCounterSucceeded(), "회복 중 간파 없음");
        Assert.IsTrue(m.SuspendsFsm, "회복 중 스스로 움직이지 않는다");
        Assert.IsFalse(m.ClampsToPracticeRadius);

        Assert.AreEqual(SpinnerComboEvent.None, m.Tick(0.3f), "회복 중");
        Assert.AreEqual(SpinnerComboEvent.ReadyAgain, m.Tick(0.3f), "회복 끝 = 복귀 단계 없이 바로 다시 준비");
        Assert.AreEqual(SpinnerComboPhase.Ready, m.Phase);
        Assert.IsTrue(m.AllowsSpin);
        Assert.IsFalse(m.SuspendsFsm);
        Assert.IsFalse(m.BlocksExternalKnockback);
        Assert.IsTrue(m.NotifyCounterSucceeded(), "그 자리에서 다음 간파");
    }

    [Test]
    public void 벽에_못_닿은_밀치기는_취약을_유지하고_다시_밀_수_있다()
    {
        var m = New();
        ToVulnerable(m);
        m.TryBeginPush(1f, 0f);
        Assert.AreEqual(SpinnerComboEvent.PushEndedNoWall, PushAll(m), "거리 소진");
        Assert.AreEqual(0, m.Successes);
        Assert.IsTrue(m.IsVulnerable);

        m.TryBeginPush(1f, 0f);
        Assert.AreEqual(SpinnerComboEvent.WallHit, PushAll(m, impactAt: 1f), "두 번째에 닿는다");
        Assert.AreEqual(1, m.Successes);
    }

    [Test]
    public void 벽_아닌_것에_막히면_충돌이_아니다()
    {
        var m = New();
        ToVulnerable(m);
        m.TryBeginPush(1f, 0f);
        // 상자·배리어·NavMesh 끝에 1m 에서 막힘 — 벽 충돌 이벤트 없음.
        Assert.AreEqual(SpinnerComboEvent.PushEndedNoWall, PushAll(m, blockedAt: 1f));
        Assert.AreEqual(0, m.Successes);
        Assert.IsTrue(m.IsVulnerable);
    }

    [Test]
    public void 막힌_채로라도_벽_충돌_이벤트가_오면_센다()
    {
        var m = New();
        ToVulnerable(m);
        m.TryBeginPush(1f, 0f);
        // NavMesh 끝(벽 앞 1m)에서 막혔지만 공용 판정의 막힘 거리 안이라 이벤트가 났다.
        Assert.AreEqual(SpinnerComboEvent.WallHit, PushAll(m, impactAt: 1f, blockedAt: 1f));
    }

    [Test]
    public void 한_연계에_벽_충돌은_한_번만()
    {
        var m = New();
        ToVulnerable(m);
        m.TryBeginPush(1f, 0f);
        float step = m.PushStep(0.05f);
        Assert.AreEqual(SpinnerComboEvent.WallHit, m.ReportPushMoved(step, step, true));
        Assert.AreEqual(SpinnerComboEvent.None, m.ReportPushMoved(step, step, true), "중복 충돌 보고 무시");
        Assert.AreEqual(1, m.Successes);
    }

    [Test]
    public void 밀치기_중이_아니면_벽_충돌을_세지_않는다()
    {
        var m = New();
        Assert.AreEqual(SpinnerComboEvent.None, m.ReportPushMoved(0.1f, 0.1f, true), "준비 단계(외부 넉백으로 벽에 닿은 경우)");
        ToVulnerable(m);
        Assert.AreEqual(SpinnerComboEvent.None, m.ReportPushMoved(0.1f, 0.1f, true), "취약이지만 밀치기 전");
        Assert.AreEqual(0, m.Successes);
    }

    [Test]
    public void 세_번째_연계_뒤_마지막_회복이_끝나야_완료()
    {
        var m = New();
        for (int i = 1; i <= 3; i++)
        {
            ToVulnerable(m);
            m.TryBeginPush(1f, 0f);
            Assert.AreEqual(SpinnerComboEvent.WallHit, PushAll(m, impactAt: 0.5f));
            Assert.AreEqual(SpinnerComboPhase.Recovering, m.Phase, $"{i}회째 회복(3회째도 충돌 순간은 아직 완료 아님)");
            SpinnerComboEvent recovered = m.Tick(1f);
            Assert.AreEqual(i < 3 ? SpinnerComboEvent.ReadyAgain : SpinnerComboEvent.Completed, recovered, $"{i}회째");
        }
        Assert.AreEqual(SpinnerComboPhase.Done, m.Phase);
        Assert.AreEqual(3, m.Successes);
        Assert.IsFalse(m.AllowsSpin);
        Assert.IsFalse(m.BlocksExternalKnockback);
    }

    [Test]
    public void 본_전투_전환은_교육_제한을_모두_끈다()
    {
        var m = New();
        ToVulnerable(m);
        m.TryBeginPush(1f, 0f);
        m.Finish();
        Assert.AreEqual(SpinnerComboPhase.Done, m.Phase);
        Assert.IsFalse(m.IsPushing);
        Assert.IsFalse(m.SuspendsFsm);
        Assert.IsFalse(m.BlocksExternalKnockback);
        Assert.IsFalse(m.IsVulnerable);
    }
}
