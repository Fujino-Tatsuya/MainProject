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

    // ─── 23호 착지 창 판별·누름 진단 각도(스모크 5차) ──────────────────

    [Test]
    public void 체공을_본_직후_열린_창만_착지_창()
    {
        Assert.IsTrue(QAInterruptLag88Plan.IsLandingWindow(10.0, 9.98), "착지 직후(한 프레임 뒤)");
        Assert.IsTrue(QAInterruptLag88Plan.IsLandingWindow(10.0, 10.0), "같은 프레임");
        Assert.IsTrue(QAInterruptLag88Plan.IsLandingWindow(10.0, 9.0), "경계 1.0s 포함");
        Assert.IsFalse(QAInterruptLag88Plan.IsLandingWindow(10.0, 8.5), "체공이 오래전 — 개전 돌진 창 등");
        Assert.IsFalse(QAInterruptLag88Plan.IsLandingWindow(10.0, -1.0), "체공을 본 적 없음(스모크 5차 첫 창 = 개전 돌진 1.5s)");
        Assert.IsFalse(QAInterruptLag88Plan.IsLandingWindow(10.0, 10.5), "열린 뒤의 체공은 이 창과 무관");
    }

    [Test]
    public void 수평_각도는_0_180_길이0이면_NaN()
    {
        Assert.AreEqual(0.0, QAInterruptLag88Plan.FlatAngleDeg(0, 1, 0, 5), 1e-9);
        Assert.AreEqual(90.0, QAInterruptLag88Plan.FlatAngleDeg(1, 0, 0, 1), 1e-9);
        Assert.AreEqual(180.0, QAInterruptLag88Plan.FlatAngleDeg(1, 0, -2, 0), 1e-9);
        Assert.AreEqual(45.0, QAInterruptLag88Plan.FlatAngleDeg(1, 0, 1, 1), 1e-9);
        Assert.IsNaN(QAInterruptLag88Plan.FlatAngleDeg(0, 0, 1, 0));
    }

    [Test]
    public void 봇_기본값은_두_대상_모두_조준하고_23호만_더_붙는다()
    {
        QAInterruptBot88Config tt = QAInterruptBot88Config.Default(QAInterruptTargetKind.TwentyThree, QAInterruptBotMode.Sweep);
        QAInterruptBot88Config mb = QAInterruptBot88Config.Default(QAInterruptTargetKind.Midboss, QAInterruptBotMode.Sweep);
        Assert.IsTrue(tt.AimAtTarget, "23호 = 대상 조준(마우스 조준으로 '대상 있음 0')");
        Assert.AreEqual(1.0f, tt.StopDistance, 1e-6f);
        Assert.IsTrue(mb.AimAtTarget, "중간보스도 대상 조준 — 마우스 위치로 측정 오염 방지");
        Assert.AreEqual(1.5f, mb.StopDistance, 1e-6f, "중간보스 거리 = 5차 OK 그대로");
    }

    // ─── 23호 대상 고정·넉백 면역(사용자 결정 ①-a·②-a) ──────────────────

    [Test]
    public void 대상_고정은_서버_대상이나_콤보_고정_대상이_누르는_클라가_아니면_건다()
    {
        Assert.IsTrue(QAInterruptLag88Plan.ShouldForceAggro(false, false, false), "콤보 밖 · 다른 대상");
        Assert.IsFalse(QAInterruptLag88Plan.ShouldForceAggro(false, true, false), "콤보 밖 · 이미 대상 — 고정 대상은 안 본다");
        Assert.IsTrue(QAInterruptLag88Plan.ShouldForceAggro(true, false, false), "콤보 중 · 다른 대상(스모크 3차: 틱마다 콤보 중이라 한 번도 안 걸림)");
        Assert.IsTrue(QAInterruptLag88Plan.ShouldForceAggro(true, false, true), "콤보 중 · 고정은 맞지만 서버 대상이 다름");
        Assert.IsTrue(QAInterruptLag88Plan.ShouldForceAggro(true, true, false), "콤보 중 · 서버 대상은 맞지만 고정 대상이 다름");
        Assert.IsFalse(QAInterruptLag88Plan.ShouldForceAggro(true, true, true), "콤보 중 · 둘 다 누르는 클라");
    }

    [Test]
    public void 콤보_고정_대상은_콤보_중이고_누르는_클라가_아닐_때만_바꾼다()
    {
        Assert.IsTrue(QAInterruptLag88Plan.ShouldSwapComboLock(true, false), "콤보 중 · 다른 고정 대상(첫 타 전 null 포함)");
        Assert.IsFalse(QAInterruptLag88Plan.ShouldSwapComboLock(true, true), "이미 누르는 클라");
        Assert.IsFalse(QAInterruptLag88Plan.ShouldSwapComboLock(false, false), "콤보 밖 — 다음 첫 타가 대상으로 잡는다");
    }

    [Test]
    public void 슈퍼아머는_켜_둔_동안_QA_스택이_없을_때만_다시_건다()
    {
        Assert.IsTrue(QAInterruptLag88Plan.ShouldReapplySuperArmor(true, 0), "연출 일괄 해제 등으로 빠짐");
        Assert.IsFalse(QAInterruptLag88Plan.ShouldReapplySuperArmor(true, 1), "이미 걸림");
        Assert.IsFalse(QAInterruptLag88Plan.ShouldReapplySuperArmor(false, 0), "해제 뒤엔 다시 걸지 않는다");
    }

    // ─── 중간보스 강제 개시 — 존4 스킵 보낼 시점 ────────────────────────

    [Test]
    public void 존4_스킵은_로딩_흐름이_끝나고_컨트롤러가_붙은_뒤에만_보낸다()
    {
        Assert.IsTrue(QABossStart88.CanRequestZone4Skip(MapSceneStep.Zone1, true, false));
        Assert.IsFalse(QABossStart88.CanRequestZone4Skip(MapSceneStep.Zone1, true, true), "첫 진입 로딩 흐름 중 = RestartInProgress 로 거부된다(스모크 3차)");
        Assert.IsFalse(QABossStart88.CanRequestZone4Skip(MapSceneStep.Zone1, false, false), "스킵 컨트롤러가 아직 안 붙었다");
    }

    [Test]
    public void 존4_스킵은_존4_뒤에서는_보내지_않는다()
    {
        Assert.IsTrue(QABossStart88.CanRequestZone4Skip(MapSceneStep.Corridor3, true, false));
        Assert.IsFalse(QABossStart88.CanRequestZone4Skip(MapSceneStep.Corridor4, true, false), "Backward");
        Assert.IsFalse(QABossStart88.CanRequestZone4Skip(MapSceneStep.Finished, true, false), "Finished");
    }
}
#endif
