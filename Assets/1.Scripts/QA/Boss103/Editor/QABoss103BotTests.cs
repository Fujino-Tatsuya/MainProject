#if UNITY_EDITOR
using System;
using NUnit.Framework;

/// <summary>할 일 103 C2 봇 순수 규칙 — 프로필 표·단계 선택·설정 단계·봇 시드·조준 오프셋·스킬 재굴림·거리 유지·끼임 탈출.</summary>
public sealed class QABoss103BotTests
{
    // ── 프로필 표 ────────────────────────────────────────────

    [Test]
    public void 프로필_표는_사용자_확정값()
    {
        AssertProfile(QABoss103BotTier.Beginner, 0.45, 450, 1.2f, 0.45, 0.25);
        AssertProfile(QABoss103BotTier.Normal, 0.30, 300, 0.6f, 0.70, 0.50);
        AssertProfile(QABoss103BotTier.Skilled, 0.15, 150, 0.2f, 0.90, 0.80);
    }

    private static void AssertProfile(QABoss103BotTier tier, double hit, double react, float aim, double skill, double interrupt)
    {
        QABoss103BotProfile p = QABoss103BotProfile.For(tier);
        Assert.AreEqual(tier, p.Tier);
        Assert.AreEqual(hit, p.PatternHitChance, 1e-9, $"{tier} 패턴 피격");
        Assert.AreEqual(react, p.ReactionMs, 1e-9, $"{tier} 반응 지연");
        Assert.AreEqual(aim, p.AimErrorRadius, 1e-6, $"{tier} 조준 오차");
        Assert.AreEqual(skill, p.SkillUseChance, 1e-9, $"{tier} 스킬 빈도");
        Assert.AreEqual(interrupt, p.InterruptSuccessChance, 1e-9, $"{tier} 간파 성공");
    }

    [Test]
    public void 프로필_한_줄()
    {
        Assert.AreEqual("tier=Normal patternHit=0.30 reactMs=300 aimErrM=0.60 skillUse=0.70 interrupt=0.50",
                        QABoss103BotProfile.For(QABoss103BotTier.Normal).Describe());
    }

    // ── 단계 선택 ────────────────────────────────────────────

    [Test]
    public void 판_번호_빼기_1_mod_3_은_보통_숙련_초보()
    {
        Assert.AreEqual(QABoss103BotTier.Normal, QABoss103Bot.TierForRun(1), "판 1 = 보통");
        Assert.AreEqual(QABoss103BotTier.Skilled, QABoss103Bot.TierForRun(2));
        Assert.AreEqual(QABoss103BotTier.Beginner, QABoss103Bot.TierForRun(3));
        Assert.AreEqual(QABoss103BotTier.Normal, QABoss103Bot.TierForRun(4));
        Assert.AreEqual(QABoss103BotTier.Beginner, QABoss103Bot.TierForRun(0));
        Assert.AreEqual(QABoss103BotTier.Skilled, QABoss103Bot.TierForRun(-1), "음수도 0~2 로 접는다");
    }

    [Test]
    public void 수동_단계가_판_번호보다_우선()
    {
        Assert.AreEqual(QABoss103BotTier.Beginner, QABoss103Bot.ResolveTier(1, QABoss103BotTier.Beginner));
        Assert.AreEqual(QABoss103BotTier.Normal, QABoss103Bot.ResolveTier(1, null));
    }

    [Test]
    public void 설정_파일_단계_왕복()
    {
        var cfg = new QABoss103Config { Seed = 7, Run = 2, Tier = QABoss103BotTier.Skilled };
        Assert.IsTrue(QABoss103Config.TryParse(cfg.Format(), out QABoss103Config back));
        Assert.AreEqual(QABoss103BotTier.Skilled, back.Tier);
        Assert.AreEqual(QABoss103BotTier.Skilled, back.BotTier);

        var auto = new QABoss103Config { Seed = 7, Run = 2 };
        Assert.IsTrue(QABoss103Config.TryParse(auto.Format(), out QABoss103Config autoBack));
        Assert.IsNull(autoBack.Tier);
        Assert.AreEqual(QABoss103BotTier.Skilled, autoBack.BotTier, "판 2 = 숙련");

        Assert.IsTrue(QABoss103Config.TryParse("run=3\ntier=beginner", out QABoss103Config lower));
        Assert.AreEqual(QABoss103BotTier.Beginner, lower.Tier, "대소문자 무관");
        Assert.IsTrue(QABoss103Config.TryParse("run=3\ntier=7", out QABoss103Config junk));
        Assert.IsNull(junk.Tier, "숫자·모르는 값은 자동");
        Assert.AreEqual(QABoss103BotTier.Beginner, junk.BotTier, "판 3 = 초보");
    }

    // ── 시드·조준 ────────────────────────────────────────────

    [Test]
    public void 봇_시드는_판_시드_자리_용도로_결정적()
    {
        Assert.AreEqual(QABoss103Bot.Seed(55, 1, QABoss103Bot.Stream.Aim), QABoss103Bot.Seed(55, 1, QABoss103Bot.Stream.Aim));
        Assert.AreNotEqual(QABoss103Bot.Seed(55, 0, QABoss103Bot.Stream.Aim), QABoss103Bot.Seed(55, 1, QABoss103Bot.Stream.Aim), "자리마다 다르다");
        Assert.AreNotEqual(QABoss103Bot.Seed(55, 0, QABoss103Bot.Stream.Aim), QABoss103Bot.Seed(55, 0, QABoss103Bot.Stream.Skill), "용도마다 다르다");
        Assert.AreNotEqual(QABoss103Bot.Seed(55, 0, QABoss103Bot.Stream.Aim), QABoss103Bot.Seed(56, 0, QABoss103Bot.Stream.Aim), "판마다 다르다");
    }

    [Test]
    public void 같은_시드는_같은_조준_오프셋_순서()
    {
        var a = new Random(QABoss103Bot.Seed(9, 2, QABoss103Bot.Stream.Aim));
        var b = new Random(QABoss103Bot.Seed(9, 2, QABoss103Bot.Stream.Aim));
        for (int i = 0; i < 20; i++)
        {
            QABoss103Bot.AimOffset(a, 0.6f, out float ax, out float az);
            QABoss103Bot.AimOffset(b, 0.6f, out float bx, out float bz);
            Assert.AreEqual(ax, bx);
            Assert.AreEqual(az, bz);
        }
    }

    [Test]
    public void 조준_오프셋은_반경_안이고_면적_균일()
    {
        var rng = new Random(103);
        const float r = 1.2f;
        int inner = 0;
        const int n = 20000;
        for (int i = 0; i < n; i++)
        {
            QABoss103Bot.AimOffset(rng, r, out float x, out float z);
            float d = (float)Math.Sqrt(x * x + z * z);
            Assert.LessOrEqual(d, r + 1e-4f);
            if (d <= r * 0.5f)
                inner++;
        }
        // 면적 균일이면 반지름 절반 원 안 = 25%.
        Assert.AreEqual(0.25, inner / (double)n, 0.02);
    }

    [Test]
    public void 반경_0_은_오프셋_없음_그래도_두_번_굴림()
    {
        var a = new Random(1);
        var b = new Random(1);
        QABoss103Bot.AimOffset(a, 0f, out float x, out float z);
        Assert.AreEqual(0f, x);
        Assert.AreEqual(0f, z);
        b.NextDouble();
        b.NextDouble();
        Assert.AreEqual(b.NextDouble(), a.NextDouble(), "굴림 순서가 반경과 무관");
    }

    // ── 스킬 빈도 ────────────────────────────────────────────

    [Test]
    public void 확률_1_이면_준비되자마자_장전()
    {
        var gate = new QABoss103SkillGate();
        var rng = new Random(1);
        Assert.IsFalse(gate.Tick(false, 0.0, rng, 1.0), "쿨 중");
        Assert.IsTrue(gate.Tick(true, 0.1, rng, 1.0));
        Assert.IsTrue(gate.Tick(true, 0.2, rng, 1.0), "시전 전까지 장전 유지(다시 굴리지 않음)");
        Assert.AreEqual(1, gate.Rolls);
        gate.MarkUsed();
        Assert.IsFalse(gate.Tick(false, 0.3, rng, 1.0), "시전 뒤 쿨");
        Assert.IsTrue(gate.Tick(true, 5.0, rng, 1.0));
        Assert.AreEqual(2, gate.Rolls);
    }

    [Test]
    public void 실패하면_1초_뒤_다시_굴림()
    {
        var gate = new QABoss103SkillGate();
        var rng = new Random(1);
        Assert.IsFalse(gate.Tick(true, 10.0, rng, 0.0));
        Assert.AreEqual(1, gate.Rolls);
        Assert.AreEqual(1, gate.Fails);
        Assert.IsFalse(gate.Tick(true, 10.5, rng, 0.0));
        Assert.AreEqual(1, gate.Rolls, "1초 안엔 굴리지 않는다");
        Assert.IsFalse(gate.Tick(true, 11.0, rng, 0.0));
        Assert.AreEqual(2, gate.Rolls, "정확히 1초 뒤 다시");
    }

    [Test]
    public void 쿨이_다시_돌면_재굴림_대기가_풀린다()
    {
        var gate = new QABoss103SkillGate();
        var rng = new Random(1);
        gate.Tick(true, 10.0, rng, 0.0);
        gate.Tick(false, 10.2, rng, 0.0);
        Assert.IsTrue(gate.Tick(true, 10.3, rng, 1.0), "준비가 새로 오면 바로 굴린다");
    }

    [Test]
    public void 사용_빈도는_확률에_수렴()
    {
        var rng = new Random(7);
        int armed = 0;
        const int n = 10000;
        for (int i = 0; i < n; i++)
        {
            var gate = new QABoss103SkillGate();
            if (gate.Tick(true, 0.0, rng, 0.7))
                armed++;
        }
        Assert.AreEqual(0.7, armed / (double)n, 0.02);
    }

    // ── 거리 유지 ────────────────────────────────────────────

    [Test]
    public void 근접은_2m_넘으면_붙고_1m_에서_멈춘다()
    {
        QABoss103Bot.RangeBand b = QABoss103Bot.MeleeBand;
        Assert.AreEqual(QABoss103Bot.Move.Approach, QABoss103Bot.DecideMove(2.5f, b, QABoss103Bot.Move.Hold));
        Assert.AreEqual(QABoss103Bot.Move.Approach, QABoss103Bot.DecideMove(1.5f, b, QABoss103Bot.Move.Approach), "붙는 중엔 1m 까지");
        Assert.AreEqual(QABoss103Bot.Move.Hold, QABoss103Bot.DecideMove(0.9f, b, QABoss103Bot.Move.Approach));
        Assert.AreEqual(QABoss103Bot.Move.Hold, QABoss103Bot.DecideMove(1.8f, b, QABoss103Bot.Move.Hold), "띠 안에서 서 있으면 그대로");
        Assert.AreEqual(QABoss103Bot.Move.Hold, QABoss103Bot.DecideMove(0f, b, QABoss103Bot.Move.Hold), "근접은 물러나지 않는다");
        Assert.IsTrue(QABoss103Bot.InAttackRange(2.0f, b));
        Assert.IsFalse(QABoss103Bot.InAttackRange(2.1f, b));
    }

    [Test]
    public void 거너는_6에서_9m_를_지키고_7_5m_로_돌아온다()
    {
        QABoss103Bot.RangeBand b = QABoss103Bot.BandFor(QABoss103Character.Gunner);
        Assert.AreEqual(QABoss103Bot.Move.Retreat, QABoss103Bot.DecideMove(4f, b, QABoss103Bot.Move.Hold), "너무 가까우면 뒤로");
        Assert.AreEqual(QABoss103Bot.Move.Retreat, QABoss103Bot.DecideMove(7f, b, QABoss103Bot.Move.Retreat), "7.5m 까지 계속");
        Assert.AreEqual(QABoss103Bot.Move.Hold, QABoss103Bot.DecideMove(7.6f, b, QABoss103Bot.Move.Retreat));
        Assert.AreEqual(QABoss103Bot.Move.Approach, QABoss103Bot.DecideMove(9.5f, b, QABoss103Bot.Move.Hold));
        Assert.AreEqual(QABoss103Bot.Move.Approach, QABoss103Bot.DecideMove(8f, b, QABoss103Bot.Move.Approach));
        Assert.AreEqual(QABoss103Bot.Move.Hold, QABoss103Bot.DecideMove(7.4f, b, QABoss103Bot.Move.Approach));
        Assert.AreEqual(QABoss103Bot.Move.Hold, QABoss103Bot.DecideMove(6.5f, b, QABoss103Bot.Move.Hold));
        Assert.AreEqual(QABoss103Bot.MeleeBand.Max, QABoss103Bot.BandFor(QABoss103Character.Paladin).Max);
        Assert.AreEqual(QABoss103Bot.MeleeBand.Max, QABoss103Bot.BandFor(QABoss103Character.Assassin).Max);
    }

    // ── 끼임 ─────────────────────────────────────────────────

    [Test]
    public void 이동_중_2초_제자리면_옆걸음_좌우_번갈아()
    {
        var s = new QABoss103StuckDetector();
        Assert.IsFalse(s.Tick(0.0, 0f, 0f, true));
        Assert.IsFalse(s.Tick(1.9, 0.1f, 0f, true));
        Assert.IsTrue(s.Tick(2.0, 0.1f, 0f, true));
        Assert.AreEqual(1, s.Side, "첫 번째는 왼쪽");
        Assert.IsTrue(s.IsSidestepping(2.5));
        Assert.IsFalse(s.Tick(2.5, 0.1f, 0f, true), "옆걸음 중엔 재지 않는다");
        Assert.IsFalse(s.IsSidestepping(2.0 + QABoss103StuckDetector.SidestepSeconds));
        s.Tick(3.0, 0.1f, 0f, true);
        Assert.IsTrue(s.Tick(5.0, 0.1f, 0f, true));
        Assert.AreEqual(-1, s.Side, "두 번째는 오른쪽");
        Assert.AreEqual(2, s.Escapes);
    }

    [Test]
    public void 움직이고_있거나_멈추려_하면_끼임_아님()
    {
        var s = new QABoss103StuckDetector();
        s.Tick(0.0, 0f, 0f, true);
        Assert.IsFalse(s.Tick(1.5, 0.5f, 0f, true), "0.3m 넘게 갔으면 다시 잰다");
        Assert.IsFalse(s.Tick(3.0, 0.6f, 0f, true));
        Assert.IsFalse(s.Tick(3.4, 0.6f, 0f, true));
        Assert.IsFalse(s.Tick(10.0, 0.6f, 0f, false), "서 있으려는 중");
        Assert.IsFalse(s.Tick(10.1, 0.6f, 0f, true), "다시 이동 — 처음부터 잰다");
        Assert.AreEqual(0, s.Escapes);
    }

    [Test]
    public void 옆_방향은_진행_방향에_수직()
    {
        QABoss103StuckDetector.SideDirection(0f, 1f, 1, out float lx, out float lz);
        Assert.AreEqual(-1f, lx);
        Assert.AreEqual(0f, lz);
        QABoss103StuckDetector.SideDirection(0f, 1f, -1, out float rx, out float rz);
        Assert.AreEqual(1f, rx);
        Assert.AreEqual(0f, rz, 1e-6f);
    }

    // ── C7d 끼임 단계 ────────────────────────────────────────

    [Test]
    public void 진척_없이_6초면_대시_3초마다_20초면_재배치()
    {
        var e = new QABoss103StuckEscalation();
        Assert.AreEqual(QABoss103StuckStep.None, e.Tick(0.0, 10.0, true));
        Assert.AreEqual(QABoss103StuckStep.None, e.Tick(5.9, 9.8, true), "0.5m 못 줄었으면 진척 아님");
        Assert.AreEqual(QABoss103StuckStep.Dash, e.Tick(6.0, 9.8, true));
        Assert.AreEqual(QABoss103StuckStep.None, e.Tick(8.9, 10.2, true), "옆걸음으로 멀어져도 진척 아님 — 대시는 3초 간격");
        Assert.AreEqual(QABoss103StuckStep.Dash, e.Tick(9.0, 10.0, true));
        Assert.AreEqual(2, e.Dashes, "6·9초 대시 2회");
        Assert.AreEqual(QABoss103StuckStep.Dash, e.Tick(18.0, 10.0, true));
        Assert.AreEqual(QABoss103StuckStep.Relocate, e.Tick(20.0, 10.0, true), "20초 — 재배치가 대시보다 먼저");
        Assert.AreEqual(20.0, e.StuckSeconds(20.0), 1e-9);
        Assert.AreEqual(QABoss103StuckStep.None, e.Tick(20.5, 10.0, true));
        Assert.AreEqual(QABoss103StuckStep.Relocate, e.Tick(35.0, 10.0, true), "재배치 실패면 15초 뒤 다시");
        Assert.AreEqual(2, e.Relocates);
    }

    [Test]
    public void 다가가거나_멈추면_단계_초기화()
    {
        var e = new QABoss103StuckEscalation();
        e.Tick(0.0, 10.0, true);
        Assert.AreEqual(QABoss103StuckStep.None, e.Tick(5.0, 9.4, true), "0.5m 넘게 다가감 — 다시 센다");
        Assert.AreEqual(QABoss103StuckStep.None, e.Tick(10.9, 9.4, true));
        Assert.AreEqual(QABoss103StuckStep.Dash, e.Tick(11.0, 9.4, true));
        Assert.AreEqual(QABoss103StuckStep.None, e.Tick(12.0, 9.4, false), "멈춤(공격·거리 안) = 끼임 아님");
        Assert.AreEqual(0.0, e.StuckSeconds(12.0));
        Assert.AreEqual(QABoss103StuckStep.None, e.Tick(12.1, 9.4, true));
        Assert.AreEqual(QABoss103StuckStep.None, e.Tick(18.0, 9.4, true));
        e.Reset();
        Assert.AreEqual(QABoss103StuckStep.None, e.Tick(19.0, 3.0, true), "목표가 바뀌면 처음부터");
        Assert.AreEqual(QABoss103StuckStep.None, e.Tick(24.9, 3.0, true));
        Assert.AreEqual(QABoss103StuckStep.None, e.Tick(30.0, double.NaN, true), "지표 없음 = 이동 아님");
        Assert.AreEqual(0, e.Relocates);
    }

    [Test]
    public void 물러나기는_음수_거리로_진척()
    {
        var e = new QABoss103StuckEscalation();
        e.Tick(0.0, -4.0, true);
        Assert.AreEqual(QABoss103StuckStep.None, e.Tick(5.0, -4.6, true), "멀어짐 = 진척");
        Assert.AreEqual(QABoss103StuckStep.Dash, e.Tick(11.0, -4.6, true));
    }

    [Test]
    public void 대시_탈출_방향_차례와_충전_남기기()
    {
        Assert.AreEqual(180f, QABoss103StuckEscalation.DashTurnDegrees(1));
        Assert.AreEqual(90f, QABoss103StuckEscalation.DashTurnDegrees(2));
        Assert.AreEqual(-90f, QABoss103StuckEscalation.DashTurnDegrees(3));
        Assert.AreEqual(180f, QABoss103StuckEscalation.DashTurnDegrees(4));
        Assert.IsTrue(QABoss103StuckEscalation.CanSpendDash(2, 2));
        Assert.IsFalse(QABoss103StuckEscalation.CanSpendDash(1, 2), "최대 2개면 회피용 1개 남김");
        Assert.IsTrue(QABoss103StuckEscalation.CanSpendDash(1, 1), "최대 1개면 그 1개를 쓴다");
        Assert.IsFalse(QABoss103StuckEscalation.CanSpendDash(0, 1));
    }
}
#endif
