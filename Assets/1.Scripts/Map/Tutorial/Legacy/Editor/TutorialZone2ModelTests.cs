using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 존2 순수 규칙(S8 → v8) — 기상 지연 · 목표 줄(처치 진행도만 — Q·E 체크·안내·넛지 없음).
/// 원본: Docs/design/tutorial-scenario.md §3.3·§5(존2).
/// </summary>
public sealed class TutorialZone2ModelTests
{
    [Test]
    public void 기상은_첫_진입_4초_뒤()
    {
        Assert.IsFalse(TutorialZone2Rules.ShouldWake(-1d, 100d), "진입 전");
        Assert.IsFalse(TutorialZone2Rules.ShouldWake(10d, 13.9d));
        Assert.IsTrue(TutorialZone2Rules.ShouldWake(10d, 14d));
        Assert.IsFalse(TutorialZone2Rules.ShouldWake(10d, double.NaN));
    }

    [Test]
    public void 목표_줄은_처치_수만()
    {
        Dictionary<string, string> a = TutorialZone2Goal.Args(3, 9);
        Assert.AreEqual("3", a["n"]);
        Assert.AreEqual("9", a["total"]);
        Assert.IsFalse(a.ContainsKey("q"), "v8 — Q·E 사용 체크 없음");
        Assert.IsFalse(a.ContainsKey("e"));

        Assert.AreEqual("9", TutorialZone2Goal.Args(12, 9)["n"], "목표에서 멈춤");
        Assert.AreEqual("0", TutorialZone2Goal.Args(-1, 9)["n"]);
        Assert.AreEqual("7", TutorialZone2Goal.Args(7, 0)["n"], "목표 0 = 상한 없음");
    }
}
