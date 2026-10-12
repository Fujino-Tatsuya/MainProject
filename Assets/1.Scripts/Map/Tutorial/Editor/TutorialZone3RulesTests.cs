using NUnit.Framework;

// 튜토리얼 존3 흐름 순수 규칙(S9) — TutorialZone3Model · TutorialZone3Display · TutorialZone3Geometry.
public class TutorialZone3RulesTests
{
    static TutorialZone3Model Practicing()
    {
        var m = new TutorialZone3Model();
        m.NotifyEntryGateOpen();
        m.NotifyPartyEntered();
        return m;
    }

    // ─── 시작 ───

    [Test]
    public void Start_NeedsBothGateAndEntry_InEitherOrder()
    {
        var a = new TutorialZone3Model();
        Assert.IsFalse(a.NotifyEntryGateOpen());
        Assert.IsTrue(a.NotifyPartyEntered());
        Assert.AreEqual(TutorialZone3Phase.Practice, a.Phase);

        var b = new TutorialZone3Model();
        Assert.IsFalse(b.NotifyPartyEntered());
        Assert.AreEqual(TutorialZone3Phase.Waiting, b.Phase);
        Assert.IsTrue(b.NotifyEntryGateOpen());
        Assert.AreEqual(TutorialZone3Phase.Practice, b.Phase);
    }

    [Test]
    public void Start_OnlyOnce()
    {
        var m = Practicing();
        Assert.IsFalse(m.NotifyPartyEntered());
        Assert.IsFalse(m.NotifyEntryGateOpen());
    }

    // ─── 간파 성공 ───

    [Test]
    public void CounterSuccess_CountsOnlyInPractice_FirstSubtitleOnce()
    {
        var waiting = new TutorialZone3Model();
        Assert.IsFalse(waiting.NotifyCounterSucceeded(out bool f0));
        Assert.IsFalse(f0);

        var m = Practicing();
        Assert.IsTrue(m.NotifyCounterSucceeded(out bool first));
        Assert.IsTrue(first);
        Assert.IsTrue(m.NotifyCounterSucceeded(out bool second));
        Assert.IsFalse(second);

        Assert.IsTrue(m.NotifyRealModeGate(out _));
        Assert.IsFalse(m.NotifyCounterSucceeded(out bool afterReal), "본 모드 간파는 세지 않는다");
        Assert.IsFalse(afterReal);
    }

    [Test]
    public void RealModeGate_FromPractice_NotSkipped_AndOnce()
    {
        var m = Practicing();
        Assert.IsTrue(m.NotifyRealModeGate(out bool skipped));
        Assert.IsFalse(skipped);
        Assert.AreEqual(TutorialZone3Phase.Real, m.Phase);
        Assert.IsFalse(m.NotifyRealModeGate(out _));
    }

    [Test]
    public void RealModeGate_BeforePractice_ReportsSkipped()
    {
        var m = new TutorialZone3Model();
        Assert.IsTrue(m.NotifyRealModeGate(out bool skipped));
        Assert.IsTrue(skipped, "디버그로 G5 를 먼저 열면 휴면부터 풀어야 한다");
        Assert.IsFalse(m.NotifyPartyEntered(), "본 모드 뒤에는 연습을 다시 시작하지 않는다");
    }

    [Test]
    public void Cleared_Once()
    {
        var m = Practicing();
        m.NotifyRealModeGate(out _);
        Assert.IsTrue(m.NotifyCleared());
        Assert.IsFalse(m.NotifyCleared());
        Assert.AreEqual(TutorialZone3Phase.Cleared, m.Phase);
    }

    // ─── 개인 피드백 ───

    [Test]
    public void Classify_ZeroHits_IsMiss()
    {
        var m = Practicing();
        Assert.AreEqual(TutorialZone3Feedback.Miss, m.ClassifyInterrupt(0, true, false, false, false));
        Assert.AreEqual(TutorialZone3Feedback.Miss, m.ClassifyInterrupt(0, true, false, true, true));
    }

    [Test]
    public void Classify_HitNearWithWindowClosed_NoSuccess_IsEarly()
    {
        var m = Practicing();
        Assert.AreEqual(TutorialZone3Feedback.Early, m.ClassifyInterrupt(1, true, false, false, true));
    }

    [Test]
    public void Classify_SuccessOrOpenWindowOrFar_IsNone()
    {
        var m = Practicing();
        Assert.AreEqual(TutorialZone3Feedback.None, m.ClassifyInterrupt(1, true, true, false, true), "성공");
        Assert.AreEqual(TutorialZone3Feedback.None, m.ClassifyInterrupt(1, true, false, true, true), "노랑 중 — 다른 대상");
        Assert.AreEqual(TutorialZone3Feedback.None, m.ClassifyInterrupt(1, true, false, false, false), "스피너봇에서 멂");
    }

    [Test]
    public void Classify_OutsidePracticeOrZone_IsNone()
    {
        var waiting = new TutorialZone3Model();
        Assert.AreEqual(TutorialZone3Feedback.None, waiting.ClassifyInterrupt(0, true, false, false, true));

        var m = Practicing();
        Assert.AreEqual(TutorialZone3Feedback.None, m.ClassifyInterrupt(0, false, false, false, true), "존3 밖 시전");

        m.NotifyRealModeGate(out _);
        Assert.AreEqual(TutorialZone3Feedback.None, m.ClassifyInterrupt(0, true, false, false, true), "본 모드");
    }

    [Test]
    public void Hint_AtMultiplesOfFour_OnlyInPractice()
    {
        var m = Practicing();
        Assert.IsFalse(m.ShouldShowHint(0));
        Assert.IsFalse(m.ShouldShowHint(3));
        Assert.IsTrue(m.ShouldShowHint(4));
        Assert.IsFalse(m.ShouldShowHint(5));
        Assert.IsTrue(m.ShouldShowHint(8));

        m.NotifyRealModeGate(out _);
        Assert.IsFalse(m.ShouldShowHint(4), "본 모드 놓침은 힌트 없음");

        Assert.AreEqual(2, new TutorialZone3Model(2).HintEvery);
        Assert.AreEqual(1, new TutorialZone3Model(0).HintEvery, "0 이하는 1");
    }

    // ─── 표시 ───

    [Test]
    public void Display_ObjectiveByStep_ClearedByG6()
    {
        Assert.AreEqual(TutorialGuideIds.Z3CounterGoal, TutorialZone3Display.ObjectiveFor(TutorialStep.Zone3Practice, false));
        Assert.AreEqual(TutorialGuideIds.Z3Boss, TutorialZone3Display.ObjectiveFor(TutorialStep.Zone3Fight, false));
        Assert.IsNull(TutorialZone3Display.ObjectiveFor(TutorialStep.Zone3Fight, true));
        Assert.IsNull(TutorialZone3Display.ObjectiveFor(TutorialStep.Corridor2, false));
        Assert.IsNull(TutorialZone3Display.ObjectiveFor(TutorialStep.Zone4Lore, false));
    }

    [Test]
    public void Display_PracticeObjective_FollowsComboPhase()
    {
        const TutorialStep P = TutorialStep.Zone3Practice;
        Assert.AreEqual(TutorialGuideIds.Z3CounterGoal, TutorialZone3Display.ObjectiveFor(P, false, SpinnerComboPhase.Ready), "Z3-2");
        Assert.AreEqual(TutorialGuideIds.Z3PushGoal, TutorialZone3Display.ObjectiveFor(P, false, SpinnerComboPhase.Stunned), "Z3-4");
        Assert.AreEqual(TutorialGuideIds.Z3PushGoal, TutorialZone3Display.ObjectiveFor(P, false, SpinnerComboPhase.Vulnerable));
        Assert.AreEqual(TutorialGuideIds.Z3Goal, TutorialZone3Display.ObjectiveFor(P, false, SpinnerComboPhase.Returning), "Z3-7");
        Assert.AreEqual(TutorialGuideIds.Z3Goal, TutorialZone3Display.ObjectiveFor(P, false, SpinnerComboPhase.Done));
    }

    [Test]
    public void Display_HighlightsOnlyInPractice_WallWhileVulnerable()
    {
        Assert.AreEqual(TutorialZone3Highlight.Spinner | TutorialZone3Highlight.RmbSlot,
            TutorialZone3Display.HighlightsFor(TutorialStep.Zone3Practice, false));
        Assert.AreEqual(TutorialZone3Highlight.Wall | TutorialZone3Highlight.Spinner | TutorialZone3Highlight.RmbSlot,
            TutorialZone3Display.HighlightsFor(TutorialStep.Zone3Practice, false, SpinnerComboPhase.Vulnerable), "유효 벽 강조");
        Assert.AreEqual(TutorialZone3Highlight.None,
            TutorialZone3Display.HighlightsFor(TutorialStep.Zone3Practice, false, SpinnerComboPhase.Returning));
        Assert.AreEqual(TutorialZone3Highlight.None, TutorialZone3Display.HighlightsFor(TutorialStep.Zone3Fight, false));
        Assert.AreEqual(TutorialZone3Highlight.None, TutorialZone3Display.HighlightsFor(TutorialStep.Zone3Practice, true));
        Assert.IsTrue(TutorialZone3Display.IsZone3Objective(TutorialGuideIds.Z3Goal));
        Assert.IsTrue(TutorialZone3Display.IsZone3Objective(TutorialGuideIds.Z3PushGoal));
        Assert.IsFalse(TutorialZone3Display.IsZone3Objective(TutorialGuideIds.Z2Goal));
        Assert.IsFalse(TutorialZone3Display.IsZone3Objective(null));
    }

    [Test]
    public void Classify_NotReady_IsNone()
    {
        var m = new TutorialZone3Model();
        m.NotifyEntryGateOpen();
        m.NotifyPartyEntered();
        Assert.AreEqual(TutorialZone3Feedback.None, m.ClassifyInterrupt(0, true, false, false, true, comboReady: false),
            "취약 밀치기·복귀 중 우클릭은 빗나감 안내 대신 밀치기 안내");
        Assert.AreEqual(TutorialZone3Feedback.Miss, m.ClassifyInterrupt(0, true, false, false, true, comboReady: true));
    }

    [Test]
    public void PushHint_Throttled()
    {
        var m = new TutorialZone3Model();
        Assert.IsFalse(m.TryPushHint(0d), "연습 전");
        m.NotifyEntryGateOpen();
        m.NotifyPartyEntered();
        Assert.IsTrue(m.TryPushHint(10d));
        Assert.IsFalse(m.TryPushHint(12d), "5초 안 — 도배 방지");
        Assert.IsTrue(m.TryPushHint(15d));
    }

    // ─── 연계 연습 위치 · 유효 벽 ───

    // 2026-10-11 StageTutorial 계산값: 존3 (0.18, −160.55) 20×40(긴 축 z) · 스피너봇 스폰 (0.18, −145.95).
    static readonly TutorialRect Zone3 = new TutorialRect(0.18f, -160.55f, 10f, 20f);

    [Test]
    public void PracticeLayout_LongSideWall_AnchorGapFromWall_AwayFromEnds()
    {
        TutorialZone3PracticeLayout l = TutorialZone3Geometry.ChoosePracticeLayout(Zone3, 0.18f, -145.95f, 0.5f, 4f, 8f);
        // 짧은 축 좌표 0(동률) → 로컬 + 쪽 긴 변(x = 10.18). 안쪽 법선 −x.
        Assert.AreEqual(10.18f, l.WallX, 1e-3f);
        Assert.AreEqual(-1f, l.NormalX, 1e-4f);
        Assert.AreEqual(0f, l.NormalZ, 1e-4f);
        // 연습 위치: 벽 면 − 몸 0.5 − 간격 4 = x 5.68. 긴 축은 존 끝에서 8m 안쪽으로 잘림(스폰 14.6 → 12) → z −148.55.
        Assert.AreEqual(5.68f, l.AnchorX, 1e-3f);
        Assert.AreEqual(-148.55f, l.AnchorZ, 1e-3f);
        Assert.AreEqual(l.AnchorZ, l.WallZ, 1e-3f, "벽 표시 지점 = 연습 위치에서 가장 가까운 벽 점");
        // 몸 가장자리 ↔ 벽 = 간격
        Assert.AreEqual(4f, SpinnerComboGeometry.WallGap(l.AnchorX, l.AnchorZ, l.WallX, l.WallZ, l.NormalX, l.NormalZ, 0.5f), 1e-3f);
    }

    [Test]
    public void PracticeLayout_PicksNearerLongSide_AndRotation()
    {
        TutorialZone3PracticeLayout l = TutorialZone3Geometry.ChoosePracticeLayout(Zone3, -3f, -160f, 0.5f, 4f, 8f);
        Assert.AreEqual(-9.82f, l.WallX, 1e-3f, "스폰이 − 쪽이면 − 긴 변");
        Assert.AreEqual(1f, l.NormalX, 1e-4f);

        // 90° 돌린 존(긴 축이 로컬 z 그대로, 월드에서는 x) — 법선은 여전히 벽에서 방 안쪽.
        var rotated = new TutorialRect(0f, 0f, 10f, 20f, 90f);
        TutorialZone3PracticeLayout r = TutorialZone3Geometry.ChoosePracticeLayout(rotated, 0f, 0f, 0.5f, 4f, 8f);
        float gap = SpinnerComboGeometry.WallGap(r.AnchorX, r.AnchorZ, r.WallX, r.WallZ, r.NormalX, r.NormalZ, 0.5f);
        Assert.AreEqual(4f, gap, 1e-3f);
        Assert.AreEqual(10f, System.Math.Abs(r.WallZ), 1e-3f, "로컬 x 긴 변 → 월드 z = ±10");
    }

    // ─── 연습 반경 ───

    // 2026-10-11 StageTutorial 실측: 스피너봇 스폰 (0.18, −145.95) · G6 배리어 (0.18, −140.35) 4.6×0.4 → 존 쪽 면 z −140.55.
    static readonly TutorialRect G6 = new TutorialRect(0.18f, -140.35f, 2.3f, 0.2f);

    [Test]
    public void DistanceToRect_OutsideAndInside()
    {
        Assert.AreEqual(5.4f, TutorialZone3Geometry.DistanceToRect(0.18f, -145.95f, G6), 1e-3f);
        Assert.AreEqual(0f, TutorialZone3Geometry.DistanceToRect(0.18f, -140.35f, G6), 1e-6f);
        // 대각 — 모서리 (2.48, −140.55) 에서 (3, 4) 떨어진 점 = 5
        Assert.AreEqual(5f, TutorialZone3Geometry.DistanceToRect(5.48f, -144.55f, G6), 1e-3f);
    }

    [Test]
    public void PracticeRadius_StopsBodyBeforeBarrier_CappedByInspector()
    {
        // 5.4 − 몸 0.5 − 여유 0.5 = 4.4 (인스펙터 6 보다 작다)
        Assert.AreEqual(4.4f, TutorialZone3Geometry.ComputePracticeRadius(0.18f, -145.95f, G6, 0.5f, 0.5f, 6f), 1e-3f);
        // 상한
        Assert.AreEqual(3f, TutorialZone3Geometry.ComputePracticeRadius(0.18f, -145.95f, G6, 0.5f, 0.5f, 3f), 1e-6f);
        // 너무 가까우면 0(기준점 고정)
        Assert.AreEqual(0f, TutorialZone3Geometry.ComputePracticeRadius(0.18f, -141.0f, G6, 0.5f, 0.5f, 6f), 1e-6f);
    }
}
