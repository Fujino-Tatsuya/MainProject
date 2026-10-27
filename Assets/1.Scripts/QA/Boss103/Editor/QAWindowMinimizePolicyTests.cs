#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>할 일 124 — QA 창 최소화 옵션 게이트(<see cref="QAWindowMinimizePolicy.Decide"/>): 기본 꺼짐, 야간·OBS 는 옵션과 무관하게 꺼짐.</summary>
public sealed class QAWindowMinimizePolicyTests
{
    [Test]
    public void 기본은_꺼짐()
    {
        QAWindowMinimizePolicy.Decision d = QAWindowMinimizePolicy.Decide(false, false, false);
        Assert.IsFalse(d.Minimize);
        Assert.AreEqual(QAWindowMinimizePolicy.ReasonOptionOff, d.Reason);
        Assert.IsFalse(QAWindowMinimizePolicy.IsOptionOn((string)null));
        Assert.IsFalse(QAWindowMinimizePolicy.IsOptionOn(new Dictionary<string, string>()), "상태에 키가 없으면 꺼짐");
        Assert.IsFalse(QAWindowMinimizePolicy.IsOptionOn(new Dictionary<string, string> { { QAWindowMinimizePolicy.StateKey, "0" } }));
    }

    [Test]
    public void 명시해_켜면_최소화()
    {
        QAWindowMinimizePolicy.Decision d = QAWindowMinimizePolicy.Decide(true, false, false);
        Assert.IsTrue(d.Minimize);
        Assert.AreEqual(QAWindowMinimizePolicy.ReasonOptionOn, d.Reason);
        Assert.IsTrue(QAWindowMinimizePolicy.IsOptionOn(new Dictionary<string, string> { { QAWindowMinimizePolicy.StateKey, "1" } }));
    }

    [Test]
    public void 야간_QA_중이면_켬이어도_꺼짐()
    {
        QAWindowMinimizePolicy.Decision d = QAWindowMinimizePolicy.Decide(true, true, false);
        Assert.IsFalse(d.Minimize);
        Assert.AreEqual(QAWindowMinimizePolicy.ReasonNightly, d.Reason);
        Assert.AreEqual(QAWindowMinimizePolicy.ReasonNightly, QAWindowMinimizePolicy.Decide(false, true, false).Reason, "옵션 꺼짐이어도 사유는 야간");
    }

    [Test]
    public void OBS_녹화_켜짐이면_켬이어도_꺼짐()
    {
        QAWindowMinimizePolicy.Decision d = QAWindowMinimizePolicy.Decide(true, false, true);
        Assert.IsFalse(d.Minimize);
        Assert.AreEqual(QAWindowMinimizePolicy.ReasonObs, d.Reason);
        Assert.AreEqual(QAWindowMinimizePolicy.ReasonObs, QAWindowMinimizePolicy.Decide(false, false, true).Reason, "옵션 꺼짐이어도 사유는 OBS");
    }
}
#endif
