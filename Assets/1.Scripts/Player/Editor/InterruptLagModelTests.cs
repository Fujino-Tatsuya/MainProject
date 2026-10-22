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

    // ─── 보정 시각 기준 성공 (판정 시각 − min(지연, 상한)) ─────────────

    [Test]
    public void 호스트_지연_0이면_보정_시각은_판정_시각이라_창_상태가_실제와_같다()
    {
        var h = new InterruptWindowHistory();
        h.Record(10.0, true);
        h.Record(11.0, false);

        Assert.AreEqual(0.0, InterruptLagJudge.CappedLagSeconds(10.5, 10.5, Cap));

        double openHit = InterruptLagJudge.CorrectedJudgeTime(10.5, 10.5, 10.7, Cap);
        Assert.AreEqual(10.7, openHit);
        Assert.AreEqual(InterruptWindowState.Open, h.StateAt(openHit));
        Assert.IsTrue(InterruptLagJudge.WouldSucceed(h.StateAt(openHit)), "실제 판정 시각에 열림 → 성공과 같다");

        double closedHit = InterruptLagJudge.CorrectedJudgeTime(10.9, 10.9, 11.2, Cap);
        Assert.AreEqual(11.2, closedHit);
        Assert.IsFalse(InterruptLagJudge.WouldSucceed(h.StateAt(closedHit)), "실제 판정 시각에 닫힘 → 실패와 같다");
    }

    [Test]
    public void 지연이_정확히_상한이면_상한만큼_당긴다()
    {
        Assert.AreEqual(Cap, InterruptLagJudge.CappedLagSeconds(1.0, 1.1, Cap), 1e-9);
        Assert.AreEqual(1.2, InterruptLagJudge.CorrectedJudgeTime(1.0, 1.1, 1.3, Cap), 1e-9);
    }

    [Test]
    public void 지연이_상한을_넘으면_상한으로_잘린다()
    {
        Assert.AreEqual(Cap, InterruptLagJudge.CappedLagSeconds(1.0, 1.5, Cap), 1e-9);
        Assert.AreEqual(1.6, InterruptLagJudge.CorrectedJudgeTime(1.0, 1.5, 1.7, Cap), 1e-9);
    }

    [Test]
    public void 음수_지연은_0으로_본다()
    {
        Assert.AreEqual(0.0, InterruptLagJudge.CappedLagSeconds(1.05, 1.0, Cap), "클라 추정 시각이 앞서도 미래로 밀지 않는다");
        Assert.AreEqual(1.3, InterruptLagJudge.CorrectedJudgeTime(1.05, 1.0, 1.3, Cap));
    }

    [Test]
    public void 보정_시각이_닫힘_경계_직전이면_성공_직후면_실패()
    {
        var h = new InterruptWindowHistory();
        h.Record(10.0, true);
        h.Record(11.0, false);

        // 지연 60ms(상한 안) — 판정 11.05 → 보정 10.99(닫힘 직전)
        double before = InterruptLagJudge.CorrectedJudgeTime(10.9, 10.96, 11.05, Cap);
        Assert.AreEqual(InterruptWindowState.Closed, h.StateAt(11.05));
        Assert.AreEqual(InterruptWindowState.Open, h.StateAt(before));
        bool would = InterruptLagJudge.WouldSucceed(h.StateAt(before));
        Assert.IsTrue(would);
        Assert.IsTrue(InterruptLagJudge.CorrectedSuccess(actualSuccess: false, would));

        // 같은 지연 — 판정 11.07 → 보정 11.01(닫힘 직후)
        double after = InterruptLagJudge.CorrectedJudgeTime(10.9, 10.96, 11.07, Cap);
        Assert.AreEqual(InterruptWindowState.Closed, h.StateAt(after));
        Assert.IsFalse(InterruptLagJudge.WouldSucceed(h.StateAt(after)));
    }

    [Test]
    public void 상한에_잘리면_누른_시각에_열려_있어도_보정_시각_기준이다()
    {
        var h = new InterruptWindowHistory();
        h.Record(10.0, true);
        h.Record(11.0, false);

        // 지연 250ms → 100ms 로 잘림. 판정 11.12 → 보정 11.02(닫힘)
        double corrected = InterruptLagJudge.CorrectedJudgeTime(10.7, 10.95, 11.12, Cap);
        Assert.AreEqual(11.02, corrected, 1e-9);
        Assert.AreEqual(InterruptWindowState.Open, h.StateAt(10.7), "누른 시각엔 열림");
        Assert.IsFalse(InterruptLagJudge.WouldSucceed(h.StateAt(corrected)));
    }

    [Test]
    public void 보정_시각이_열림_경계_직전이면_실패()
    {
        var h = new InterruptWindowHistory();
        h.Record(10.0, false);
        h.Record(11.0, true);

        // 지연 50ms — 판정 11.03(열림) → 보정 10.98(열림 직전)
        double corrected = InterruptLagJudge.CorrectedJudgeTime(10.95, 11.0, 11.03, Cap);
        Assert.AreEqual(InterruptWindowState.Open, h.StateAt(11.03));
        Assert.AreEqual(InterruptWindowState.Closed, h.StateAt(corrected));
        Assert.IsFalse(InterruptLagJudge.WouldSucceed(h.StateAt(corrected)));
    }

    [Test]
    public void 창_기록이_없으면_보정했으면_성공도_false()
    {
        var h = new InterruptWindowHistory();
        double corrected = InterruptLagJudge.CorrectedJudgeTime(1.0, 1.05, 1.2, Cap);
        Assert.AreEqual(InterruptWindowState.Unknown, h.StateAt(corrected));
        Assert.IsFalse(InterruptLagJudge.WouldSucceed(InterruptWindowState.Unknown));
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
        StringAssert.Contains(
            "pressWindow=open hitWindow=closed correctedWindow=open result=fail wouldSucceed=true capMs=100", line);
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
            CorrectedWindow = InterruptWindowState.Open,
            Success = success,
            WouldSucceed = would,
            CapMs = 100f,
        };
}
