using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 존2 순수 규칙(S8) — 기상 지연 · E 안내(Q 첫 사용 / 기상 10초) · 넛지(기상 15초, 안 쓴 키만) · 강조 · 목표 줄.
/// 원본: Docs/design/tutorial-scenario.md §3.3·§5(존2).
/// </summary>
public sealed class TutorialZone2ModelTests
{
    const float Wake = (float)TutorialZone2Rules.WakeDelaySeconds;

    [Test]
    public void 기상은_첫_진입_4초_뒤()
    {
        Assert.IsFalse(TutorialZone2Rules.ShouldWake(-1d, 100d), "진입 전");
        Assert.IsFalse(TutorialZone2Rules.ShouldWake(10d, 13.9d));
        Assert.IsTrue(TutorialZone2Rules.ShouldWake(10d, 14d));
        Assert.IsFalse(TutorialZone2Rules.ShouldWake(10d, double.NaN));
    }

    [Test]
    public void E_안내는_Q_첫_사용_직후_한번()
    {
        var g = new TutorialZone2Guide();
        Assert.IsFalse(g.Tick(1f, false, false).ShowEPrompt);
        Assert.IsTrue(g.HighlightQ);
        Assert.IsTrue(g.Tick(0.1f, true, false).ShowEPrompt);
        Assert.IsFalse(g.Tick(0.1f, true, false).ShowEPrompt, "한 번만");
        Assert.IsFalse(g.HighlightQ);
        Assert.IsTrue(g.HighlightE(false));
        Assert.IsFalse(g.HighlightE(true), "E 를 쓰면 강조 해제");
    }

    [Test]
    public void E_안내는_Q를_안_써도_기상_10초_뒤()
    {
        var g = new TutorialZone2Guide();
        Assert.IsFalse(g.Tick(Wake + 9.9f, false, false).ShowEPrompt);
        Assert.IsTrue(g.Tick(0.2f, false, false).ShowEPrompt);
    }

    [Test]
    public void 넛지는_기상_15초에_안_쓴_키만_한번()
    {
        var g = new TutorialZone2Guide();
        g.Tick(Wake + 14.9f, true, false);
        TutorialZone2Cue cue = g.Tick(0.2f, true, false);
        Assert.AreEqual(TutorialZone2Keys.E, cue.Nudge);
        Assert.AreEqual(TutorialZone2Keys.None, g.Tick(30f, false, false).Nudge, "한 번만");
    }

    [Test]
    public void 넛지_둘_다_썼으면_없음_둘_다_안_썼으면_둘_다()
    {
        var a = new TutorialZone2Guide();
        Assert.AreEqual(TutorialZone2Keys.None, a.Tick(Wake + 15f, true, true).Nudge);
        Assert.IsTrue(a.NudgeDone);

        var b = new TutorialZone2Guide();
        Assert.AreEqual(TutorialZone2Keys.Q | TutorialZone2Keys.E, b.Tick(Wake + 15f, false, false).Nudge);
    }

    [Test]
    public void 넛지_문구_인자()
    {
        Assert.AreEqual("Q", TutorialZone2Guide.KeyLabel(TutorialZone2Keys.Q));
        Assert.AreEqual("Q · E", TutorialZone2Guide.KeyLabel(TutorialZone2Keys.Q | TutorialZone2Keys.E));
        Assert.AreEqual(string.Empty, TutorialZone2Guide.KeyLabel(TutorialZone2Keys.None));
        Assert.AreEqual("보호막", TutorialZone2Guide.SkillLabel(TutorialZone2Keys.E, "방패 돌진", "보호막"));
        Assert.AreEqual("방패 돌진 · E", TutorialZone2Guide.SkillLabel(TutorialZone2Keys.Q | TutorialZone2Keys.E, "방패 돌진", null));
    }

    [Test]
    public void 늦은_클라는_시간_안내를_건너뛴다()
    {
        var g = new TutorialZone2Guide();
        g.SkipTimedCues();
        TutorialZone2Cue cue = g.Tick(1f, false, false);
        Assert.IsFalse(cue.ShowEPrompt);
        Assert.AreEqual(TutorialZone2Keys.None, cue.Nudge);
        Assert.IsFalse(g.HighlightQ);
        Assert.IsTrue(g.HighlightE(false));
    }

    [Test]
    public void 비정상_시간은_무시()
    {
        var g = new TutorialZone2Guide();
        g.Tick(float.NaN, false, false);
        g.Tick(-5f, false, false);
        Assert.AreEqual(0f, g.Elapsed);
    }

    [Test]
    public void 목표_줄_체크와_처치_수()
    {
        Dictionary<string, string> a = TutorialZone2Goal.Args(true, false, 3, 9);
        Assert.AreEqual(TutorialZone2Goal.CheckedMark, a["q"]);
        Assert.AreEqual(TutorialZone2Goal.UncheckedMark, a["e"]);
        Assert.AreEqual("3", a["n"]);
        Assert.AreEqual("9", a["total"]);

        Assert.AreEqual("9", TutorialZone2Goal.Args(false, false, 12, 9)["n"], "목표에서 멈춤");
        Assert.AreEqual("0", TutorialZone2Goal.Args(false, false, -1, 9)["n"]);
        Assert.AreEqual("7", TutorialZone2Goal.Args(false, false, 7, 0)["n"], "목표 0 = 상한 없음");
    }
}
