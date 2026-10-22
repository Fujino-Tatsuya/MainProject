#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>할 일 88 간파 지연 QA 의 순수 계산 — 스윕 오프셋·사람형 반응·누를 시각·지연 파일·창 길이 학습.</summary>
public sealed class QAInterruptLag88PlanTests
{
    // ─── A 경계 스윕 ──────────────────────────────────────────────────

    [Test]
    public void 스윕은_양끝을_포함해_고르게_나눈다()
    {
        Assert.AreEqual(-150.0, QAInterruptLag88Plan.SweepOffsetMs(0, 30), 1e-9, "첫 시도 = −150");
        Assert.AreEqual(50.0, QAInterruptLag88Plan.SweepOffsetMs(29, 30), 1e-9, "마지막 시도 = +50");
        double step = 200.0 / 29.0;
        for (int i = 1; i < 30; i++)
            Assert.AreEqual(step, QAInterruptLag88Plan.SweepOffsetMs(i, 30) - QAInterruptLag88Plan.SweepOffsetMs(i - 1, 30), 1e-9, $"간격 일정 i={i}");
    }

    [Test]
    public void 스윕_다섯번은_50ms_간격()
    {
        var got = new List<double>();
        for (int i = 0; i < 5; i++)
            got.Add(QAInterruptLag88Plan.SweepOffsetMs(i, 5));
        CollectionAssert.AreEqual(new[] { -150.0, -100.0, -50.0, 0.0, 50.0 }, got);
    }

    [Test]
    public void 스윕_시도수를_넘으면_처음부터_다시()
    {
        Assert.AreEqual(QAInterruptLag88Plan.SweepOffsetMs(0, 30), QAInterruptLag88Plan.SweepOffsetMs(30, 30), 1e-9);
        Assert.AreEqual(QAInterruptLag88Plan.SweepOffsetMs(3, 30), QAInterruptLag88Plan.SweepOffsetMs(33, 30), 1e-9);
    }

    [Test]
    public void 스윕_시도수_1이하면_최소값_범위가_뒤집혀도_정렬()
    {
        Assert.AreEqual(-150.0, QAInterruptLag88Plan.SweepOffsetMs(0, 1), 1e-9);
        Assert.AreEqual(-150.0, QAInterruptLag88Plan.SweepOffsetMs(5, 0), 1e-9);
        Assert.AreEqual(-150.0, QAInterruptLag88Plan.SweepOffsetMs(0, 3, 50.0, -150.0), 1e-9);
        Assert.AreEqual(50.0, QAInterruptLag88Plan.SweepOffsetMs(2, 3, 50.0, -150.0), 1e-9);
    }

    [Test]
    public void 스윕_누를_시각은_창길이_더하기_오프셋_열림보다_앞이면_0()
    {
        Assert.AreEqual(1350.0, QAInterruptLag88Plan.SweepPressDelayMs(1500.0, -150.0), 1e-9);
        Assert.AreEqual(1550.0, QAInterruptLag88Plan.SweepPressDelayMs(1500.0, 50.0), 1e-9);
        Assert.AreEqual(0.0, QAInterruptLag88Plan.SweepPressDelayMs(100.0, -150.0), 1e-9, "창이 오프셋보다 짧으면 관측 즉시");
    }

    // ─── B 사람형 ─────────────────────────────────────────────────────

    [Test]
    public void 반응_시간은_150_350_안에서_고르게_퍼진다()
    {
        var rng = new System.Random(88);
        double min = double.MaxValue, max = double.MinValue;
        int low = 0;
        const int n = 2000;
        for (int i = 0; i < n; i++)
        {
            double r = QAInterruptLag88Plan.ReactionMs(rng);
            Assert.That(r, Is.InRange(150.0, 350.0));
            min = System.Math.Min(min, r);
            max = System.Math.Max(max, r);
            if (r < 250.0) low++;
        }
        Assert.Less(min, 160.0, "아래 끝 근처까지 나온다");
        Assert.Greater(max, 340.0, "위 끝 근처까지 나온다");
        Assert.That(low, Is.InRange(n * 0.45, n * 0.55), "가운데 기준 절반씩(균등)");
    }

    [Test]
    public void 반응_시간은_같은_시드면_같은_순서()
    {
        var a = new System.Random(7);
        var b = new System.Random(7);
        for (int i = 0; i < 10; i++)
            Assert.AreEqual(QAInterruptLag88Plan.ReactionMs(a), QAInterruptLag88Plan.ReactionMs(b));
    }

    // ─── 원격 지연 파일 ───────────────────────────────────────────────

    [Test]
    public void 지연_파일은_정수_한줄_범위로_자른다()
    {
        Assert.IsTrue(QAInterruptLag88Plan.TryParseDelayMs("35\r\n", out int ms));
        Assert.AreEqual(35, ms);
        Assert.IsTrue(QAInterruptLag88Plan.TryParseDelayMs("-5", out ms));
        Assert.AreEqual(0, ms);
        Assert.IsTrue(QAInterruptLag88Plan.TryParseDelayMs("99999", out ms));
        Assert.AreEqual(QAInterruptLag88Plan.MaxDelayMs, ms);
        Assert.IsFalse(QAInterruptLag88Plan.TryParseDelayMs("", out ms));
        Assert.IsFalse(QAInterruptLag88Plan.TryParseDelayMs("abc", out ms));
        Assert.AreEqual(0, ms);
    }

    [Test]
    public void 지연_단계는_사양대로()
    {
        CollectionAssert.AreEqual(new[] { 5, 35, 80, 120 }, QAInterruptLag88Plan.DelayStepsMs);
        Assert.AreEqual(30, QAInterruptLag88Plan.DefaultAttempts);
    }

    [Test]
    public void 로그_토큰에는_공백과_등호가_없다()
    {
        Assert.AreEqual("Gauntlet_Bot(Clone)", QAInterruptLag88Plan.LogToken("Gauntlet Bot(Clone)"));
        Assert.AreEqual("a_b", QAInterruptLag88Plan.LogToken("a=b"));
        Assert.AreEqual("none", QAInterruptLag88Plan.LogToken(null));
    }

    // ─── 창 길이 학습(23호) ───────────────────────────────────────────

    [Test]
    public void 학습_전에는_추정_없음()
    {
        var l = new QAWindowDurationLearner();
        Assert.IsFalse(l.TryEstimate(out _));
        l.Add(0.0);
        l.Add(-1.0);
        Assert.IsFalse(l.TryEstimate(out _), "0·음수는 버린다");
    }

    [Test]
    public void 학습은_최근_다섯개_중_최대()
    {
        var l = new QAWindowDurationLearner();
        l.Add(0.8);
        l.Add(0.3);   // 성공으로 일찍 닫힌 창
        Assert.IsTrue(l.TryEstimate(out double s));
        Assert.AreEqual(0.8, s, 1e-9);

        for (int i = 0; i < QAWindowDurationLearner.Capacity; i++)
            l.Add(0.5);
        Assert.AreEqual(QAWindowDurationLearner.Capacity, l.Count);
        Assert.IsTrue(l.TryEstimate(out s));
        Assert.AreEqual(0.5, s, 1e-9, "오래된 0.8 은 밀려났다");
    }

    // ─── 중간보스 강제 개시 — 존4 스킵 보낼 시점 ────────────────────────

    [Test]
    public void 존4_스킵은_로딩_흐름이_끝나고_컨트롤러가_붙은_뒤에만_보낸다()
    {
        Assert.IsTrue(QABossStart88.CanRequestZone4Skip(TutorialStep.Zone1, true, false));
        Assert.IsFalse(QABossStart88.CanRequestZone4Skip(TutorialStep.Zone1, true, true), "첫 진입 로딩 흐름 중 = RestartInProgress 로 거부된다(스모크 3차)");
        Assert.IsFalse(QABossStart88.CanRequestZone4Skip(TutorialStep.Zone1, false, false), "스킵 컨트롤러가 아직 안 붙었다");
    }

    [Test]
    public void 존4_스킵은_존4_뒤에서는_보내지_않는다()
    {
        Assert.IsTrue(QABossStart88.CanRequestZone4Skip(TutorialStep.Corridor3, true, false));
        Assert.IsFalse(QABossStart88.CanRequestZone4Skip(TutorialStep.Corridor4, true, false), "Backward");
        Assert.IsFalse(QABossStart88.CanRequestZone4Skip(TutorialStep.Finished, true, false), "Finished");
    }
}
#endif
