using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 간파 지연 측정(할 일 74 후속)의 순수 판정·집계. 판정 결과는 안 바꾸고 로그만 계산한다.
/// 상한 100ms 는 기본값일 뿐이라 여기선 인자로 넘긴다.
/// </summary>
public sealed class InterruptLagModelTests
{
    private const double Cap = 0.1;

    // ─── 창 기록 ──────────────────────────────────────────────────────

    [Test]
    public void 기록이_없으면_Unknown()
    {
        var h = new InterruptWindowHistory();
        Assert.AreEqual(InterruptWindowState.Unknown, h.StateAt(5.0));
    }

    [Test]
    public void 창_경계_직전은_이전_상태_경계_시각부터는_바뀐_상태()
    {
        var h = new InterruptWindowHistory();
        h.Record(10.0, true);
        h.Record(11.0, false);

        Assert.AreEqual(InterruptWindowState.Unknown, h.StateAt(9.999), "첫 열림 전 = 기록 없음");
        Assert.AreEqual(InterruptWindowState.Open, h.StateAt(10.0), "열림 경계 = 열림");
        Assert.AreEqual(InterruptWindowState.Open, h.StateAt(10.999), "닫힘 직전 = 열림");
        Assert.AreEqual(InterruptWindowState.Closed, h.StateAt(11.0), "닫힘 경계 = 닫힘");
        Assert.AreEqual(InterruptWindowState.Closed, h.StateAt(11.001), "닫힘 직후 = 닫힘");
    }

    [Test]
    public void 링버퍼가_가득_차면_가장_오래된_기록을_덮어쓴다()
    {
        var h = new InterruptWindowHistory(capacity: 3);
        h.Record(1.0, true);
        h.Record(2.0, false);
        h.Record(3.0, true);
        h.Record(4.0, false); // 1.0 기록이 밀려난다

        Assert.AreEqual(3, h.Count);
        Assert.AreEqual(InterruptWindowState.Unknown, h.StateAt(1.5), "덮어써진 구간은 추측하지 않는다");
        Assert.AreEqual(InterruptWindowState.Closed, h.StateAt(2.5));
        Assert.AreEqual(InterruptWindowState.Open, h.StateAt(3.5));
        Assert.AreEqual(InterruptWindowState.Closed, h.StateAt(4.5));
    }

    // ─── 누른 시각 기준 성공 ──────────────────────────────────────────

    [Test]
    public void 차이가_정확히_상한이면_true()
    {
        Assert.IsTrue(InterruptLagJudge.WouldSucceedAtPress(InterruptWindowState.Open, 1.0, 1.1, Cap),
            "1.1 − 1.0 의 부동소수 오차로 떨어지면 안 된다");
    }

    [Test]
    public void 차이가_상한을_넘으면_false()
    {
        Assert.IsFalse(InterruptLagJudge.WouldSucceedAtPress(InterruptWindowState.Open, 1.0, 1.1001, Cap));
    }

    [Test]
    public void 누른_시각에_창이_닫혔거나_모르면_false()
    {
        Assert.IsFalse(InterruptLagJudge.WouldSucceedAtPress(InterruptWindowState.Closed, 1.0, 1.05, Cap));
        Assert.IsFalse(InterruptLagJudge.WouldSucceedAtPress(InterruptWindowState.Unknown, 1.0, 1.05, Cap));
    }

    [Test]
    public void 창_닫힘_직전에_누르고_직후에_판정되면_보정했으면_성공()
    {
        var h = new InterruptWindowHistory();
        h.Record(10.0, true);
        h.Record(11.0, false);

        double press = 10.95, hit = 11.02;
        InterruptWindowState pressWindow = h.StateAt(press);
        Assert.AreEqual(InterruptWindowState.Open, pressWindow);
        Assert.AreEqual(InterruptWindowState.Closed, h.StateAt(hit));

        bool would = InterruptLagJudge.WouldSucceedAtPress(pressWindow, press, hit, Cap);
        Assert.IsTrue(would);
        Assert.IsTrue(InterruptLagJudge.CorrectedSuccess(actualSuccess: false, would));
    }

    [Test]
    public void 창_열림_직전에_누르면_보정해도_실패()
    {
        var h = new InterruptWindowHistory();
        h.Record(10.0, false);
        h.Record(11.0, true);

        InterruptWindowState pressWindow = h.StateAt(10.999);
        Assert.AreEqual(InterruptWindowState.Closed, pressWindow);
        Assert.IsFalse(InterruptLagJudge.WouldSucceedAtPress(pressWindow, 10.999, 11.05, Cap));
    }

    // ─── 요약 집계 ────────────────────────────────────────────────────

    [Test]
    public void 요약은_호스트와_원격을_나눠_센다()
    {
        var s = new InterruptLagSummary();
        s.Record(Sample(host: true, target: "Boss", success: true, would: false, rtt: 0));
        s.Record(Sample(host: false, target: "Boss", success: false, would: true, rtt: 40));
        s.Record(Sample(host: false, target: "Boss", success: true, would: true, rtt: 80));
        s.Record(Sample(host: false, target: null, success: false, would: false, rtt: 60));

        Assert.AreEqual(1, s.Host.Attempts);
        Assert.AreEqual(1, s.Host.Success);
        Assert.AreEqual(0.0, s.Host.RttMaxMs);

        Assert.AreEqual(3, s.Remote.Attempts);
        Assert.AreEqual(2, s.Remote.WithTarget);
        Assert.AreEqual(1, s.Remote.Success);
        Assert.AreEqual(2, s.Remote.CorrectedSuccess, "실제 성공 ∨ 누른 시각 기준 성공");
        Assert.AreEqual(1, s.Remote.Rescued, "보정했으면 성공인데 실제 실패");
        Assert.AreEqual(60.0, s.Remote.RttAvgMs, 1e-9);
        Assert.AreEqual(80.0, s.Remote.RttMaxMs, 1e-9);
    }

    [Test]
    public void 요약_줄은_고정_키를_갖고_Clear_뒤엔_비어_있다()
    {
        var s = new InterruptLagSummary();
        s.Record(Sample(host: false, target: "Boss", success: false, would: true, rtt: 45.5));

        var lines = new List<string>();
        s.AppendLogLines("session_end", lines);
        Assert.AreEqual(2, lines.Count);
        StringAssert.StartsWith("[InterruptLag] event=summary reason=session_end group=host", lines[0]);
        StringAssert.Contains("group=remote attempts=1 withTarget=1 success=0 correctedSuccess=1 rescued=1 " +
                              "rttAvgMs=45.5 rttMaxMs=45.5", lines[1]);

        s.Clear();
        Assert.IsTrue(s.IsEmpty);
    }

    [Test]
    public void 판정_줄은_공백_없는_key_value()
    {
        InterruptLagSample sample = Sample(host: false, target: "Gauntlet Bot", success: false, would: true, rtt: 80);
        string line = sample.ToLogLine();

        StringAssert.StartsWith("[InterruptLag] event=judge clientId=1 host=false", line);
        StringAssert.Contains("target=Gauntlet_Bot", line);
        StringAssert.Contains("pressT=10.000 arriveT=10.040 hitT=10.090 lagMs=40.0 hitAfterPressMs=90.0 rttMs=80", line);
        StringAssert.Contains("pressWindow=open hitWindow=closed result=fail wouldSucceed=true capMs=100", line);
        foreach (string token in line.Substring("[InterruptLag] ".Length).Split(' '))
            Assert.AreEqual(1, token.Split('=').Length - 1, token);
    }

    private static InterruptLagSample Sample(bool host, string target, bool success, bool would, double rtt) =>
        new InterruptLagSample
        {
            ClientId = host ? 0UL : 1UL,
            IsHost = host,
            Skill = "GunnerInterruptSkill",
            Target = target,
            PressServerTime = 10.0,
            ArriveServerTime = 10.04,
            HitServerTime = 10.09,
            RttMs = rtt,
            PressWindow = InterruptWindowState.Open,
            HitWindow = InterruptWindowState.Closed,
            Success = success,
            WouldSucceed = would,
            CapMs = 100f,
        };
}
