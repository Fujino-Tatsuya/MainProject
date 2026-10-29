#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

/// <summary>할 일 140-2 순수 규칙 — 북진 무력화 간파 창 판별·닫힘·포기 거리·마감(일반 창과 비교)·실패 굴림 누름 클램프·대쉬 간파·결과 줄.</summary>
public sealed class QABoss103PushNorthCounterTests
{
    private const double E = 1e-9;

    [Test]
    public void 탑_소멸_직후_열린_창만_북진_창()
    {
        Assert.IsTrue(QABoss103PushNorthCounter.IsPushNorthWindow(10.0, 10.0), "같은 틱");
        Assert.IsTrue(QABoss103PushNorthCounter.IsPushNorthWindow(10.9, 10.0), "복제 지연 안");
        Assert.IsFalse(QABoss103PushNorthCounter.IsPushNorthWindow(11.2, 10.0), "1초 넘게 뒤 = 다른 창");
        Assert.IsFalse(QABoss103PushNorthCounter.IsPushNorthWindow(10.0, -1.0), "탑 소멸을 못 봄");
        Assert.IsFalse(QABoss103PushNorthCounter.IsPushNorthWindow(9.5, 10.0), "창이 먼저 열림");
    }

    [Test]
    public void 닫힘은_탑_소멸_시각부터_무력화_시간()
    {
        Assert.AreEqual(13.0, QABoss103PushNorthCounter.CloseAt(10.2, 10.0, 3.0), E, "실제 남은 시간 = 소멸부터");
        Assert.AreEqual(13.2, QABoss103PushNorthCounter.CloseAt(10.2, -1.0, 3.0), E, "소멸 못 봄 = 열림부터");
        Assert.AreEqual(10.0 + QABoss103PushNorthCounter.DefaultStunSeconds, QABoss103PushNorthCounter.CloseAt(10.0, 10.0, 0.0), E, "값 못 읽음 = 기본 3초");
    }

    [Test]
    public void 북진_창의_마감은_창_길이에_맞고_일반_창은_그대로()
    {
        double open = 10.0;
        double pnDeadline = QABoss103HitTiming.CounterPressDeadline(QABoss103PushNorthCounter.CloseAt(open, open, 3.0));
        double normalDeadline = QABoss103HitTiming.CounterPressDeadline(open + QABoss103Counter.DefaultWindowMs / 1000.0);
        Assert.AreEqual(3.0 - 0.333 - 0.07, pnDeadline - open, 1e-6, "북진 = 3초 − 판정 선딜 − 여유");
        Assert.AreEqual(0.8 - 0.333 - 0.07, normalDeadline - open, 1e-6, "일반 첫 창 = 0.8초 추정 그대로(약 0.4초)");
    }

    [Test]
    public void 포기_거리는_마감까지_갈_수_있는_거리_일반은_2_5m()
    {
        // 마감 2.597초 · 반응 0 · 7m/s · 대시 5m → 18.18 + 5
        Assert.AreEqual(2.597 * 7.0 + 5.0, QABoss103PushNorthCounter.ApproachSlack(2.597, 0.0, 7.0, 5.0, 2.5), 1e-6);
        Assert.AreEqual(2.0 * 7.0, QABoss103PushNorthCounter.ApproachSlack(2.5, 0.5, 7.0, 0.0, 2.5), 1e-6, "반응 지연만큼 덜 걷는다");
        Assert.AreEqual(2.5, QABoss103PushNorthCounter.ApproachSlack(0.1, 0.3, 7.0, 0.0, 2.5), 1e-6, "일반 여유보다 작아지지 않는다");
        Assert.AreEqual(2.0 * QABoss103PushNorthCounter.FallbackMoveSpeed, QABoss103PushNorthCounter.ApproachSlack(2.0, 0.0, 0.0, 0.0, 2.5), 1e-6, "속도 못 읽음 = 보수값");
    }

    [Test]
    public void 실패_굴림_누름은_창_안()
    {
        double open = 10.0, close = 13.0, late = QABoss103Counter.LatePressMs / 1000.0;
        double t = QABoss103PushNorthCounter.MissPressAt(open, close, 0.3, late);
        Assert.AreEqual(QABoss103HitTiming.CounterPressDeadline(close), t, 1e-9, "닫힘 + 150ms(3.15초) → 마감으로 당김");
        Assert.Less(t, close, "창 남은 시간보다 짧다");
        Assert.Less(t + QABoss103HitTiming.ParryHitSeconds, close, "판정도 창 안");

        double slow = QABoss103PushNorthCounter.MissPressAt(open, close, 2.8, late);
        Assert.AreEqual(open + 2.8, slow, 1e-9, "반응 지연보다 이르지 않다");
        double tooSlow = QABoss103PushNorthCounter.MissPressAt(open, close, 5.0, late);
        Assert.AreEqual(close - QABoss103PushNorthCounter.MinInsideSeconds, tooSlow, 1e-9, "반응이 창보다 길어도 닫힘 전");
    }

    [Test]
    public void 대쉬_간파는_걸어서_못_닿고_대시_거리_안일_때만()
    {
        Assert.IsTrue(QABoss103PushNorthCounter.UseDashInterrupt(3.0, 0.3, 7.0, 5.0), "3m 를 0.3초에 못 걷는다 → 대시");
        Assert.IsFalse(QABoss103PushNorthCounter.UseDashInterrupt(3.0, 1.0, 7.0, 5.0), "걸어서 닿는다");
        Assert.IsFalse(QABoss103PushNorthCounter.UseDashInterrupt(4.9, 0.1, 7.0, 5.0), "대시 거리 − 여유 밖");
        Assert.IsFalse(QABoss103PushNorthCounter.UseDashInterrupt(0.0, 0.1, 7.0, 5.0), "이미 사거리 안");
        Assert.IsFalse(QABoss103PushNorthCounter.UseDashInterrupt(1.0, 0.0, 7.0, 0.0), "충전 없음(거리 0)");
    }

    [Test]
    public void 결과_토큰()
    {
        Assert.AreEqual("none", QABoss103PushNorthCounter.Outcome(false, false, false, double.NaN, 13.0, 13.0));
        Assert.AreEqual("rejected", QABoss103PushNorthCounter.Outcome(true, false, false, 11.3, 13.0, 13.0));
        Assert.AreEqual("miss", QABoss103PushNorthCounter.Outcome(true, true, true, 12.9, 13.0, 13.0), "실패 굴림 = 일부러 빗나감");
        Assert.AreEqual("hit", QABoss103PushNorthCounter.Outcome(true, true, false, 11.3, 11.35, 13.0), "판정 직후 일찍 닫힘 = 적중");
        Assert.AreEqual("closed_before_judge", QABoss103PushNorthCounter.Outcome(true, true, false, 11.3, 11.0, 13.0), "판정 전에 닫힘(다른 사람)");
        Assert.AreEqual("miss", QABoss103PushNorthCounter.Outcome(true, true, false, 11.3, 13.0, 13.0), "끝까지 열림 = 빗나감");
        Assert.AreEqual("miss", QABoss103PushNorthCounter.Outcome(true, true, false, 11.3, double.NaN, 13.0), "닫힘 못 봄");
    }

    [Test]
    public void 결과_줄_형식과_야간_기록_전용_규칙()
    {
        string line = QABoss103PushNorthCounter.FormatLine("[QA103]", "Paladin", 0, "Normal", 123.456, double.NaN, 3.0,
            8.5, 7.6, 1.8, 23.2, "hit", true, true, "hit", "pressed", 812.4, double.NaN);
        Assert.AreEqual("[QA103] event=pn_counter bot=Paladin slot=0 tier=Normal openT=123.46 openServerT=na windowMs=3000 distM=8.50 surfM=7.60 " +
                        "reachM=1.80 slackM=23.20 roll=hit tried=true dash=true result=hit reason=pressed pressMs=812 closeMs=na", line);
        StringAssert.DoesNotContain("\t", QABoss103PushNorthCounter.FormatLine("[QA103]", "Gunner", 1, "Low", 1, 2, 3, 4, 5, 6, 7, null, false, false, "none", "a b", double.NaN, 1));

        QANightlyEventRule r = QANightlyEvents.Match(line, LogType.Log, null);
        Assert.IsNotNull(r);
        Assert.AreEqual(QANightlyEvents.PushNorthCounter, r.Name);
        Assert.AreEqual(QABoss103PushNorthCounter.Event, QANightlyEvents.PushNorthCounter, "봇 줄 이름과 표 이름이 같다");
        Assert.IsTrue(r.Trace, "기록 전용 — 요약 표·클립 제외");
        StringAssert.EndsWith("closeMs=na", QANightlyEvents.Detail(line, r.DetailMax), "뒤쪽 필드가 잘리지 않는다");
    }

    // ── 할 일 140-4 ──

    [Test]
    public void 대각선을_보는_보스는_AABB_거리가_실제보다_짧다_원인_재현()
    {
        // 23호 HurtBox = 수직 캡슐 r 1.7. 북진 중 보스는 화면 남쪽(카메라 yaw 315 → 월드 (0.71, 0, -0.71))을 본다.
        Bounds hurt = new Bounds(new Vector3(0f, 2.89f, 0f), new Vector3(3.4f, 5.78f, 3.4f));
        Vector3 dir = new Vector3(1f, 0f, -1f).normalized;
        float d = 1.73f + 1.7f * Mathf.Sqrt(2f);   // AABB 표면 거리 1.73 이 되는 중심 거리
        Vector3 bot = dir * d;
        double aabb = QABoss103PushNorthCounter.AabbSurfaceDistance(bot.x, bot.z, hurt.min.x, hurt.min.z, hurt.max.x, hurt.max.z);
        double real = QABoss103PushNorthCounter.CylinderSurfaceDistance(bot.x, bot.z, 0.0, 0.0, 1.7);
        Assert.AreEqual(1.73, aabb, 1e-3, "봇·오너 예측이 본 거리(사거리 1.75 안)");
        Assert.Greater(real, 1.75 + 0.6, "실제 표면은 사거리 밖 0.6m 이상 — 서버 판정 상자가 안 닿는다");
        Assert.AreEqual(1.7 * (Mathf.Sqrt(2f) - 1f), real - aabb, 1e-3, "차이 = 반경 × (√2 − 1)");
        Assert.AreEqual(0.0, QABoss103PushNorthCounter.CylinderSurfaceDistance(0.5, 0.0, 0.0, 0.0, 1.7), E, "안이면 0");
    }

    [Test]
    public void 누름_여유는_0_5m_사거리가_작으면_비율()
    {
        Assert.AreEqual(0.5, QABoss103PushNorthCounter.PressMargin(1.75), E);
        Assert.AreEqual(1.25, QABoss103PushNorthCounter.PressDistance(1.75), E, "사거리 1.75 → 실제 표면 1.25m 안에서");
        Assert.AreEqual(0.3, QABoss103PushNorthCounter.PressMargin(1.0), E, "1m × 0.3");
        Assert.AreEqual(0.7, QABoss103PushNorthCounter.PressDistance(1.0), E);
        Assert.AreEqual(0.0, QABoss103PushNorthCounter.PressMargin(0.0), E);
        Assert.AreEqual(0.0, QABoss103PushNorthCounter.PressDistance(double.NaN), E, "사거리 못 읽음 = 0");
    }

    [Test]
    public void 정면_한도는_서버_값보다_15도_안쪽_절반보다_좁히지_않는다()
    {
        Assert.AreEqual(75.0, QABoss103PushNorthCounter.FrontLimit(90.0, true), E, "dev No23 = 90");
        Assert.AreEqual(45.0, QABoss103PushNorthCounter.FrontLimit(60.0, true), E, "코드 기본 60");
        Assert.AreEqual(10.0, QABoss103PushNorthCounter.FrontLimit(20.0, true), E, "20 − 15 = 5 < 절반 10");
        Assert.AreEqual(180.0, QABoss103PushNorthCounter.FrontLimit(90.0, false), E, "정면 판정 꺼짐");
    }

    [Test]
    public void 누름_자리는_실제_거리와_좁힌_정면()
    {
        Assert.IsTrue(QABoss103PushNorthCounter.InPressSpot(1.2, 1.75, 70.0, 90.0, true));
        Assert.IsFalse(QABoss103PushNorthCounter.InPressSpot(1.73, 1.75, 2.0, 90.0, true), "사거리 끝 2cm = 지난 배치의 누름 자리");
        Assert.IsFalse(QABoss103PushNorthCounter.InPressSpot(1.0, 1.75, 80.0, 90.0, true), "정면 한도 75 밖");
        Assert.IsTrue(QABoss103PushNorthCounter.InPressSpot(1.0, 1.75, 170.0, 90.0, false), "정면 판정 꺼짐");
    }

    [Test]
    public void 멈춤은_틱과_시간_둘_다()
    {
        Assert.IsTrue(QABoss103PushNorthCounter.IsSettled(2, 0.1));
        Assert.IsFalse(QABoss103PushNorthCounter.IsSettled(1, 0.5), "틱 부족");
        Assert.IsFalse(QABoss103PushNorthCounter.IsSettled(10, 0.05), "시간 부족");
    }

    [Test]
    public void 누름_판단_자리_멈춤_마감_앞_사거리_마감()
    {
        const double deadline = 12.6;
        Assert.IsTrue(QABoss103PushNorthCounter.ShouldPress(11.0, deadline, true, true, true), "자리 + 멈춤");
        Assert.IsFalse(QABoss103PushNorthCounter.ShouldPress(11.0, deadline, true, false, true), "아직 움직이는 중");
        Assert.IsFalse(QABoss103PushNorthCounter.ShouldPress(11.0, deadline, false, true, true), "사거리 안이어도 여유 자리 전엔 기다린다");
        Assert.IsTrue(QABoss103PushNorthCounter.ShouldPress(deadline - 0.3, deadline, false, false, true), "마감 0.4초 앞부터 사거리 안이면");
        Assert.IsFalse(QABoss103PushNorthCounter.ShouldPress(deadline - 0.3, deadline, false, false, false), "사거리 밖은 마감까지 기다린다");
        Assert.IsTrue(QABoss103PushNorthCounter.ShouldPress(deadline, deadline, false, false, false), "마감 = 조건 없이 1회(기존 규칙)");
    }

    [Test]
    public void 서버_판정_줄_형식과_야간_기록_전용_규칙()
    {
        string line = QABoss103PushNorthCounter.FormatServerCastLine("[QA103]", "Gunner", 2, "PnStun", true, 0, false,
            1.234, -5.678, 3.0, -4.0, 2.43, 1.73, 1.75, 12.4, 90.0, "false", 278.9421);
        Assert.AreEqual("[QA103] event=pn_server_cast bot=Gunner slot=2 step=PnStun windowOpen=true resolved=0 success=false pos=1.23/-5.68 boss=3.00/-4.00 " +
                        "trueSurfM=2.43 aabbSurfM=1.73 reachM=1.75 frontDeg=12 frontLimit=90 overlap=false serverT=278.942", line);
        StringAssert.Contains("step=na", QABoss103PushNorthCounter.FormatServerCastLine("[QA103]", "a b", -1, null, false, 1, true,
            double.NaN, 0, 0, 0, double.NaN, 0, 0, 0, double.NaN, null, 0));

        QANightlyEventRule r = QANightlyEvents.Match(line, LogType.Log, null);
        Assert.IsNotNull(r);
        Assert.AreEqual(QANightlyEvents.PushNorthServerCast, r.Name);
        Assert.AreEqual(QABoss103PushNorthCounter.ServerCastEvent, QANightlyEvents.PushNorthServerCast, "계측기 줄 이름과 표 이름이 같다");
        Assert.IsTrue(r.Trace);
        StringAssert.EndsWith("serverT=278.942", QANightlyEvents.Detail(line, r.DetailMax));
    }

    [Test]
    public void 서버_간파_진단_줄은_기록_전용으로_시각표에()
    {
        // TwentyThreeBoss.ReceiveAttack 진단(LogWarning)·OnPushNorthCounter(Log) 문구 그대로.
        string reject = "[23호] 인터럽트가 카운터로 성립하지 않았다 — 서버=True · 창열림=True · 정면=False (허용 90°) · 상태=Attack · 페이즈=ChargeWait · 공격=ChargeSequence";
        QANightlyEventRule r = QANightlyEvents.Match(reject, LogType.Warning, null);
        Assert.IsNotNull(r);
        Assert.AreEqual(QANightlyEvents.CounterReject, r.Name);
        Assert.IsTrue(r.Trace);
        StringAssert.EndsWith("공격=ChargeSequence", QANightlyEvents.Detail(reject, r.DetailMax), "어느 조건이 거짓인지 끝까지 남는다");

        QANightlyEventRule ok = QANightlyEvents.Match("[23호/북진] 간파 — 위로 4.2m (남은 12.6m)", LogType.Log, null);
        Assert.IsNotNull(ok);
        Assert.AreEqual(QANightlyEvents.PushNorthCounterOk, ok.Name);
        Assert.IsTrue(ok.Trace);

        Assert.IsNull(QANightlyEvents.Match("[23호/북진] 2회차 탑 전부 파괴 — 무력화 3초(간파 1회 = 1칸)", LogType.Log, null), "무력화 시작 줄은 간파 성립이 아니다");
        Assert.IsNull(QANightlyEvents.Match("[23호/북진] 시작 — 1칸 4.2m · 위 끝까지 12.6m", LogType.Log, null));
    }
}
#endif
