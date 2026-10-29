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
}
#endif
