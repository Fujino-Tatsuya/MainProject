using NUnit.Framework;

/// <summary>
/// 튜토리얼 스피너봇 연계 연습(v8 §3.5) — 간파 → 기절 → 취약 유지 → 우클릭 밀치기 → 유효 벽 충돌 → 회복 × 3.
/// 회복 끝 = 그 자리에서 다시 준비·간파(사용자 변경 — 원위치 복귀 없음), 3회째 회복 끝 = 완료.
/// </summary>
public sealed class SpinnerComboModelTests
{
    // 기절 1초 · 회복 0.5초 · 밀치기 3m/0.3초(10m/s) · 접촉 0.4 · 막힘 접촉 1.6
    static SpinnerComboModel New(int target = 3) => new SpinnerComboModel(target, 1f, 0.5f, 3f, 0.3f, 0.4f, 1.6f);

    static void ToVulnerable(SpinnerComboModel m)
    {
        Assert.IsTrue(m.NotifyCounterSucceeded());
        Assert.AreEqual(SpinnerComboEvent.StunEnded, m.Tick(1.01f));
    }

    // 벽까지 몸 가장자리 거리 gap 에서 벽 쪽으로 한 번 밀어 끝까지 보낸다. 벽에 닿으면 WallHit.
    static SpinnerComboEvent PushAll(SpinnerComboModel m, ref float gap, bool towardWall = true, float blockedAt = float.NegativeInfinity)
    {
        SpinnerComboEvent all = SpinnerComboEvent.None;
        for (int i = 0; i < 100 && m.IsPushing; i++)
        {
            float step = m.PushStep(0.05f);
            float moved = towardWall ? System.Math.Max(0f, System.Math.Min(step, gap - blockedAt)) : step;
            if (towardWall) gap -= moved;
            all |= m.ReportPushMoved(step, moved, gap, towardWall);
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
    public void 밀치기_한_번이_벽에_닿으면_연계_1회_회복_뒤_그_자리에서_다시_준비()
    {
        var m = New();
        ToVulnerable(m);
        float gap = 2.5f;   // 3m 밀치기 안
        Assert.IsTrue(m.TryBeginPush(1f, 0f));
        SpinnerComboEvent e = PushAll(m, ref gap);
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
        float gap = 4f;   // 3m 로는 1m 남는다(1~2회 거리)
        m.TryBeginPush(1f, 0f);
        Assert.AreEqual(SpinnerComboEvent.PushEndedNoWall, PushAll(m, ref gap));
        Assert.AreEqual(0, m.Successes);
        Assert.IsTrue(m.IsVulnerable);
        Assert.AreEqual(1f, gap, 1e-3f);

        m.TryBeginPush(1f, 0f);
        Assert.AreEqual(SpinnerComboEvent.WallHit, PushAll(m, ref gap), "두 번째에 닿는다");
        Assert.AreEqual(1, m.Successes);
    }

    [Test]
    public void NavMesh_가장자리에_막혀도_벽_가까이면_충돌로_본다()
    {
        var m = New();
        ToVulnerable(m);
        float gap = 2.5f;
        m.TryBeginPush(1f, 0f);
        // 벽 면 1.0m 앞에서 막힘(NavMesh 끝) — 막힘 접촉 1.6 이하라 충돌.
        Assert.AreEqual(SpinnerComboEvent.WallHit, PushAll(m, ref gap, true, blockedAt: 1.0f));
    }

    [Test]
    public void 벽에서_먼_곳에서_막히면_충돌이_아니다()
    {
        var m = New();
        ToVulnerable(m);
        float gap = 5f;
        m.TryBeginPush(1f, 0f);
        // 벽 면 3m 앞 장애물(상자·배리어)에 막힘.
        Assert.AreEqual(SpinnerComboEvent.PushEndedNoWall, PushAll(m, ref gap, true, blockedAt: 3f));
        Assert.AreEqual(0, m.Successes);
        Assert.IsTrue(m.IsVulnerable);
    }

    [Test]
    public void 벽_반대쪽으로_민_것은_벽에_붙어_있어도_세지_않는다()
    {
        var m = New();
        ToVulnerable(m);
        m.TryBeginPush(-1f, 0f);
        float step = m.PushStep(0.05f);
        Assert.AreEqual(SpinnerComboEvent.None, m.ReportPushMoved(step, step, 0.1f, towardWall: false));
        Assert.AreEqual(0, m.Successes);
    }

    [Test]
    public void 한_연계에_벽_충돌은_한_번만()
    {
        var m = New();
        ToVulnerable(m);
        m.TryBeginPush(1f, 0f);
        float step = m.PushStep(0.05f);
        Assert.AreEqual(SpinnerComboEvent.WallHit, m.ReportPushMoved(step, step, 0f, true));
        Assert.AreEqual(SpinnerComboEvent.None, m.ReportPushMoved(step, step, 0f, true), "중복 충돌 보고 무시");
        Assert.AreEqual(1, m.Successes);
    }

    [Test]
    public void 세_번째_연계_뒤_마지막_회복이_끝나야_완료()
    {
        var m = New();
        for (int i = 1; i <= 3; i++)
        {
            ToVulnerable(m);
            float gap = 1f;
            m.TryBeginPush(1f, 0f);
            Assert.AreEqual(SpinnerComboEvent.WallHit, PushAll(m, ref gap));
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

    [Test]
    public void 벽_거리와_방향()
    {
        // 벽 x = 10, 방 안쪽 법선 −x. 몸 반경 0.5.
        Assert.AreEqual(4f, SpinnerComboGeometry.WallGap(5.5f, 0f, 10f, 0f, -1f, 0f, 0.5f), 1e-4f);
        Assert.AreEqual(-0.5f, SpinnerComboGeometry.WallGap(10f, 3f, 10f, 0f, -1f, 0f, 0.5f), 1e-4f);
        Assert.IsTrue(SpinnerComboGeometry.IsTowardWall(1f, 0f, -1f, 0f));
        Assert.IsFalse(SpinnerComboGeometry.IsTowardWall(0f, 1f, -1f, 0f), "벽과 나란히");
        Assert.IsFalse(SpinnerComboGeometry.IsTowardWall(-1f, 0f, -1f, 0f), "벽 반대");
    }
}
