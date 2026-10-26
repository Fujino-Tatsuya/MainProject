#if UNITY_EDITOR
using System;
using NUnit.Framework;

/// <summary>할 일 103 C3 순수 규칙 — 예고 분류·범위 판정·회피 방향·행동 선택·방어 유지·위험별 1회 굴림·간파 누름 시각·정면·창 길이 추정.</summary>
public sealed class QABoss103EvasionTests
{
    private const float Eps = 1e-4f;

    // ── 예고 분류 ────────────────────────────────────────────

    [Test]
    public void 예고_모양으로_종류를_가른다()
    {
        Assert.AreEqual(QABoss103ThreatKind.Grab, QABoss103Evasion.ClassifyTelegraph(false, true, 4f, 0f, 0f, 1f), "안쪽 채움 부채꼴 = 잡기");
        Assert.AreEqual(QABoss103ThreatKind.Dash, QABoss103Evasion.ClassifyTelegraph(true, false, 0f, 1.7f, 10f, 1.5f), "파랑 띠 = 돌진");
        Assert.AreEqual(QABoss103ThreatKind.RageDash, QABoss103Evasion.ClassifyTelegraph(false, false, 0f, 1.7f, 10f, 0f), "빨강 띠·즉시 = 레이지 돌진");
        Assert.AreEqual(QABoss103ThreatKind.Band, QABoss103Evasion.ClassifyTelegraph(false, false, 0f, 1f, 3f, 0.8f), "빨강 띠·채움 = 네모 등");
        Assert.AreEqual(QABoss103ThreatKind.Lunge, QABoss103Evasion.ClassifyTelegraph(false, false, 3f, 1f, 2f, 0.8f), "띠+부채꼴 = 전진");
        Assert.AreEqual(QABoss103ThreatKind.Cone, QABoss103Evasion.ClassifyTelegraph(false, false, 3f, 0f, 0f, 0.8f), "부채꼴만");
    }

    [Test]
    public void 로그_토큰()
    {
        Assert.AreEqual("rage_dash", QABoss103Evasion.Token(QABoss103ThreatKind.RageDash));
        Assert.AreEqual("floor", QABoss103Evasion.Token(QABoss103ThreatKind.Floor));
        Assert.AreEqual("no_charge", QABoss103Evasion.Token(QABoss103DodgeAction.NoCharge));
        Assert.AreEqual("guard", QABoss103Evasion.Token(QABoss103DodgeAction.Guard));
        Assert.AreEqual("none", QABoss103Evasion.Token(QABoss103DodgeAction.None));
    }

    // ── 범위 판정 ────────────────────────────────────────────

    [Test]
    public void 띠_안팎()
    {
        // 앞 0~10m, 반폭 1.5, 치우침 0, 여유 0.5
        Assert.IsTrue(QABoss103Evasion.InBand(5f, 1.9f, 0f, 10f, 1.5f, 0f, 0.5f));
        Assert.IsFalse(QABoss103Evasion.InBand(5f, 2.1f, 0f, 10f, 1.5f, 0f, 0.5f), "옆으로 벗어남");
        Assert.IsFalse(QABoss103Evasion.InBand(-1f, 0f, 0f, 10f, 1.5f, 0f, 0.5f), "보스 뒤");
        Assert.IsFalse(QABoss103Evasion.InBand(11f, 0f, 0f, 10f, 1.5f, 0f, 0.5f), "끝 너머");
        Assert.IsTrue(QABoss103Evasion.InBand(3f, 2.5f, 2f, 2f, 1f, 2f, 0f), "가로 치우침·앞 오프셋");
        Assert.IsFalse(QABoss103Evasion.InBand(3f, 0f, 0f, 0f, 1f, 0f), "띠 없음");
    }

    [Test]
    public void 부채꼴_안팎()
    {
        // 반경 4, 반각 45, 치우침 0, 꼭짓점 0, 여유 0
        Assert.IsTrue(QABoss103Evasion.InSector(3f, 0f, 0f, 4f, 45f, 0f, 0f));
        Assert.IsTrue(QABoss103Evasion.InSector(2f, 1.9f, 0f, 4f, 45f, 0f, 0f), "44° 안");
        Assert.IsFalse(QABoss103Evasion.InSector(1f, 2f, 0f, 4f, 45f, 0f, 0f), "63° 밖");
        Assert.IsFalse(QABoss103Evasion.InSector(4.5f, 0f, 0f, 4f, 45f, 0f, 0f), "반경 밖");
        Assert.IsTrue(QABoss103Evasion.InSector(0f, 3f, 0f, 4f, 30f, 90f, 0f), "오른쪽 90° 치우친 부채꼴");
        Assert.IsFalse(QABoss103Evasion.InSector(0f, -3f, 0f, 4f, 30f, 90f, 0f), "왼쪽은 밖");
        Assert.IsTrue(QABoss103Evasion.InSector(-3f, 0f, 0f, 4f, 180f, 0f, 0f), "원(반각 180) = 뒤도 안");
        Assert.IsTrue(QABoss103Evasion.InSector(5f, 0f, 2f, 4f, 45f, 0f, 0f), "꼭짓점을 앞으로 민다");
        Assert.IsFalse(QABoss103Evasion.InSector(1f, 0f, 0f, 0f, 45f, 0f, 0f), "반경 0 = 없음");
    }

    [Test]
    public void 부채꼴_여유는_반경과_각도를_벌린다()
    {
        Assert.IsTrue(QABoss103Evasion.InSector(4.5f, 0f, 0f, 4f, 45f, 0f, 0.8f));
        Assert.IsTrue(QABoss103Evasion.InSector(2f, 2.6f, 0f, 4f, 45f, 0f, 0.8f), "경계 밖 0.6m");
    }

    [Test]
    public void 원과_예고없는_공격()
    {
        Assert.IsTrue(QABoss103Evasion.InCircle(3f, 0f, 3f, 0f));
        Assert.IsFalse(QABoss103Evasion.InCircle(3.1f, 0f, 3f, 0f));
        Assert.IsFalse(QABoss103Evasion.InCircle(0f, 0f, 0f, 1f), "반경 0 = 없음");
        Assert.IsTrue(QABoss103Evasion.InGeneric(1f, 3f));
        Assert.IsFalse(QABoss103Evasion.InGeneric(1f, 4f), "표면 3.5m 밖");
        Assert.IsFalse(QABoss103Evasion.InGeneric(-2f, 1f), "보스 뒤");
    }

    // ── 좌표·회피 방향 ───────────────────────────────────────

    [Test]
    public void 보스_로컬_좌표_왕복()
    {
        // 보스 정면 = +x. 오른쪽 = -z(Cross(up, +x)).
        QABoss103Evasion.WorldToLocal(1f, 0f, 2f, -3f, out float f, out float r);
        Assert.AreEqual(2f, f, Eps);
        Assert.AreEqual(3f, r, Eps);
        QABoss103Evasion.LocalToWorld(1f, 0f, f, r, out float x, out float z);
        Assert.AreEqual(2f, x, Eps);
        Assert.AreEqual(-3f, z, Eps);
        // 정면 +z 이면 오른쪽 = +x
        QABoss103Evasion.LocalToWorld(0f, 1f, 0f, 1f, out x, out z);
        Assert.AreEqual(1f, x, Eps);
        Assert.AreEqual(0f, z, Eps);
    }

    [Test]
    public void 띠는_내가_있는_옆으로_피한다()
    {
        QABoss103Evasion.TelegraphEscapeLocal(5f, 0.3f, 0f, 0f, 0f, 0f, 1.7f, 0f, out float ef, out float er);
        Assert.AreEqual(0f, ef, Eps);
        Assert.AreEqual(1f, er, Eps);
        QABoss103Evasion.TelegraphEscapeLocal(5f, -0.3f, 0f, 0f, 0f, 0f, 1.7f, 0f, out ef, out er);
        Assert.AreEqual(-1f, er, Eps);
        QABoss103Evasion.TelegraphEscapeLocal(5f, 0.3f, 0f, 0f, 0f, 0f, 1.7f, 1f, out ef, out er);
        Assert.AreEqual(-1f, er, Eps, "치우친 띠 중심(1m)의 왼쪽");
        QABoss103Evasion.TelegraphEscapeLocal(5f, 0f, 0f, 0f, 0f, 0f, 1.7f, 0f, out ef, out er);
        Assert.AreEqual(1f, er, Eps, "정확히 중심이면 오른쪽");
    }

    [Test]
    public void 좁은_부채꼴은_축에_수직으로_내_쪽()
    {
        QABoss103Evasion.TelegraphEscapeLocal(3f, 0.5f, 4f, 30f, 0f, 0f, 0f, 0f, out float ef, out float er);
        Assert.AreEqual(0f, ef, Eps);
        Assert.AreEqual(1f, er, Eps);
        QABoss103Evasion.TelegraphEscapeLocal(3f, -0.5f, 4f, 30f, 0f, 0f, 0f, 0f, out ef, out er);
        Assert.AreEqual(-1f, er, Eps);
        // 축이 오른쪽 90° — 수직 = 앞/뒤, 내가 앞(f>0)이면 앞으로.
        QABoss103Evasion.TelegraphEscapeLocal(0.5f, 3f, 4f, 30f, 90f, 0f, 0f, 0f, out ef, out er);
        Assert.AreEqual(1f, ef, Eps);
        Assert.AreEqual(0f, er, 1e-3f);
    }

    [Test]
    public void 넓은_부채꼴은_꼭짓점_반대()
    {
        QABoss103Evasion.TelegraphEscapeLocal(3f, 4f, 6f, 90f, 0f, 0f, 0f, 0f, out float ef, out float er);
        Assert.AreEqual(0.6f, ef, Eps);
        Assert.AreEqual(0.8f, er, Eps);
        QABoss103Evasion.TelegraphEscapeLocal(0f, 0f, 6f, 180f, 0f, 0f, 0f, 0f, out ef, out er);
        Assert.AreEqual(-1f, ef, Eps, "겹치면 보스 뒤로");
    }

    [Test]
    public void 중심_반대_방향()
    {
        QABoss103Evasion.Away(3f, 4f, 0f, 0f, out float x, out float z);
        Assert.AreEqual(0.6f, x, Eps);
        Assert.AreEqual(0.8f, z, Eps);
        QABoss103Evasion.Away(0f, 0f, 0f, -2f, out x, out z);
        Assert.AreEqual(0f, x, Eps);
        Assert.AreEqual(-1f, z, Eps, "겹치면 대체 방향");
        QABoss103Evasion.Away(0f, 0f, 0f, 0f, out x, out z);
        Assert.AreEqual(1f, x, Eps, "대체도 0 이면 +x");
    }

    // ── 행동 선택·방어 ───────────────────────────────────────

    [Test]
    public void 행동_선택()
    {
        Assert.AreEqual(QABoss103DodgeAction.None, QABoss103Evasion.ChooseAction(false, true, true, true), "피격 굴림 = 아무것도");
        Assert.AreEqual(QABoss103DodgeAction.Guard, QABoss103Evasion.ChooseAction(true, true, false, false), "방어 가능이면 충전 무관 방어");
        Assert.AreEqual(QABoss103DodgeAction.Dash, QABoss103Evasion.ChooseAction(true, false, true, true));
        Assert.AreEqual(QABoss103DodgeAction.NoCharge, QABoss103Evasion.ChooseAction(true, false, false, true));
        Assert.AreEqual(QABoss103DodgeAction.Blocked, QABoss103Evasion.ChooseAction(true, false, true, false));
    }

    [Test]
    public void 장판만_방어_불가()
    {
        foreach (QABoss103ThreatKind k in Enum.GetValues(typeof(QABoss103ThreatKind)))
            Assert.AreEqual(k != QABoss103ThreatKind.Floor, QABoss103Evasion.IsGuardable(k), k.ToString());
    }

    [Test]
    public void 방어_유지_시간()
    {
        Assert.AreEqual(1.4, QABoss103Evasion.GuardHoldSeconds(1.0, 3.0), 1e-9, "남은 예고 + 0.4");
        Assert.AreEqual(3.0, QABoss103Evasion.GuardHoldSeconds(5.0, 3.0), 1e-9, "상한");
        Assert.AreEqual(0.4, QABoss103Evasion.GuardHoldSeconds(0.0, 3.0), 1e-9);
        Assert.AreEqual(QABoss103Evasion.GuardCapFallbackSeconds, QABoss103Evasion.GuardHoldSeconds(9.0, 0.0), 1e-9, "상한 모르면 대체 상한");
        Assert.AreEqual(0.4, QABoss103Evasion.GuardHoldSeconds(-1.0, 3.0), 1e-9, "남은 시간 음수 = 0 으로 본다");
        Assert.AreEqual(0.2, QABoss103Evasion.GuardHoldSeconds(-1.0, 0.2), 1e-9, "상한이 하한보다 작으면 상한");
    }

    // ── 위험별 1회 굴림 ──────────────────────────────────────

    [Test]
    public void 위험_1건에_1회만_굴린다()
    {
        var ledger = new QABoss103ThreatLedger();
        var rng = new Random(5);
        Assert.IsTrue(ledger.TryRoll("dash#1", rng, 0.3, out _));
        Assert.IsTrue(ledger.HasRolled("dash#1"));
        Assert.IsFalse(ledger.TryRoll("dash#1", rng, 0.3, out _), "같은 위험 다시 안 굴림");
        Assert.IsTrue(ledger.TryRoll("dash#2", rng, 0.3, out _));
        Assert.AreEqual(2, ledger.Rolls);
        Assert.AreEqual(2, ledger.Dodges + ledger.Takes);
        Assert.IsFalse(ledger.TryRoll(null, rng, 0.3, out _));
    }

    [Test]
    public void 다시_안_굴리면_rng_를_건드리지_않는다()
    {
        var a = new QABoss103ThreatLedger();
        var b = new QABoss103ThreatLedger();
        var ra = new Random(9);
        var rb = new Random(9);
        a.TryRoll("x#1", ra, 0.5, out bool a1);
        a.TryRoll("x#1", ra, 0.5, out _);
        a.TryRoll("x#2", ra, 0.5, out bool a2);
        b.TryRoll("x#1", rb, 0.5, out bool b1);
        b.TryRoll("x#2", rb, 0.5, out bool b2);
        Assert.AreEqual(b1, a1);
        Assert.AreEqual(b2, a2);
    }

    [Test]
    public void 피격_확률_0_과_1()
    {
        var ledger = new QABoss103ThreatLedger();
        var rng = new Random(1);
        for (int i = 0; i < 50; i++)
        {
            ledger.TryRoll("a#" + i, rng, 0.0, out bool dodge);
            Assert.IsTrue(dodge, "피격 0 = 항상 회피");
            ledger.TryRoll("b#" + i, rng, 1.0, out dodge);
            Assert.IsFalse(dodge, "피격 1 = 항상 피격");
        }
    }

    [Test]
    public void 피격_비율은_프로필_확률_근처()
    {
        var ledger = new QABoss103ThreatLedger();
        var rng = new Random(103);
        const int n = 4000;
        for (int i = 0; i < n; i++)
            ledger.TryRoll("t#" + i, rng, 0.30, out _);
        Assert.AreEqual(0.30, (double)ledger.Takes / n, 0.03);
    }

    [Test]
    public void 기억은_최근_것만()
    {
        var ledger = new QABoss103ThreatLedger();
        var rng = new Random(2);
        for (int i = 0; i <= QABoss103ThreatLedger.Memory; i++)
            ledger.TryRoll("m#" + i, rng, 0.5, out _);
        Assert.IsFalse(ledger.HasRolled("m#0"), "가장 오래된 것은 잊는다");
        Assert.IsTrue(ledger.HasRolled("m#" + QABoss103ThreatLedger.Memory));
    }

    // ── 간파 ────────────────────────────────────────────────

    [Test]
    public void 성공_굴림_누름은_반응이되_닫힘_80ms_전으로_당긴다()
    {
        Assert.AreEqual(300.0, QABoss103Counter.HitPressDelayMs(300.0, 1500.0), 1e-9);
        Assert.AreEqual(420.0, QABoss103Counter.HitPressDelayMs(450.0, 500.0), 1e-9, "창 500 → 420 으로 당김");
        Assert.AreEqual(0.0, QABoss103Counter.HitPressDelayMs(150.0, 50.0), 1e-9, "창이 여유보다 짧으면 즉시");
    }

    [Test]
    public void 성공_굴림_확률()
    {
        var rng = new Random(7);
        int hits = 0;
        const int n = 4000;
        for (int i = 0; i < n; i++)
            if (QABoss103Counter.RollHit(rng, 0.8))
                hits++;
        Assert.AreEqual(0.8, (double)hits / n, 0.03);
        Assert.IsFalse(QABoss103Counter.RollHit(new Random(1), 0.0));
        Assert.IsTrue(QABoss103Counter.RollHit(new Random(1), 1.0));
    }

    [Test]
    public void 정면_판정()
    {
        Assert.IsTrue(QABoss103Counter.IsFront(0f, 1f, 0f, 3f, 90f));
        Assert.IsTrue(QABoss103Counter.IsFront(0f, 1f, 3f, 0f, 90f), "옆 90° = 경계 포함");
        Assert.IsFalse(QABoss103Counter.IsFront(0f, 1f, 3f, -0.5f, 90f), "옆 뒤");
        Assert.IsFalse(QABoss103Counter.IsFront(0f, 1f, 3f, 3f, 30f), "45° > 30°");
        Assert.IsTrue(QABoss103Counter.IsFront(0f, 1f, 0f, 0f, 10f), "겹치면 정면");
    }

    [Test]
    public void 창_길이_추정은_최솟값_기본_800()
    {
        var w = new QABoss103WindowEstimate();
        Assert.AreEqual(QABoss103Counter.DefaultWindowMs, w.EstimateMs(), 1e-9);
        w.Add(1.5);
        w.Add(0.6);
        w.Add(9.0);    // 너무 김 — 버림
        w.Add(-1.0);   // 버림
        Assert.AreEqual(2, w.Count);
        Assert.AreEqual(600.0, w.EstimateMs(), 1e-6);
        for (int i = 0; i < QABoss103WindowEstimate.Capacity; i++)
            w.Add(1.0);
        Assert.AreEqual(1000.0, w.EstimateMs(), 1e-6, "최근 5개만");
    }
}
#endif
