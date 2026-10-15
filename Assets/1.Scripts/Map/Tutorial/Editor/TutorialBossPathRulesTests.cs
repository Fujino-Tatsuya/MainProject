using NUnit.Framework;

/// <summary>
/// 튜토리얼 보스 길 순수 규칙(<see cref="TutorialBossPathRules"/>) — 타이머 자동 시작·제한시간·HUD 숨김·패드 잠금·엘리베이터 문,
/// 만료 강제 이동이 게이트·패드 잠금과 무관함, G8(타이머 시작) 순서. 원본: Docs/design/tutorial-scenario.md §3.7~§3.9·§4, Docs/tech/tutorial-director.md §9.
/// </summary>
public sealed class TutorialBossPathRulesTests
{
    const double Eps = 1e-6;

    // ─── 타이머 ───────────────────────────────────────────────────────────

    [Test]
    public void AutoStart_OnlyOutsideTutorial()
    {
        Assert.IsTrue(TutorialBossPathRules.ShouldAutoStartTimer(false), "절차 맵 = 맵 생성 직후 자동 시작(기존)");
        Assert.IsFalse(TutorialBossPathRules.ShouldAutoStartTimer(true), "튜토리얼 = G8 에서 서버가 시작");
    }

    [Test]
    public void Limit_OverrideOnlyWhenPositive()
    {
        Assert.AreEqual(600f, TutorialBossPathRules.EffectiveLimitSeconds(0f, 600f), "비튜토리얼 = 씬 값 그대로");
        Assert.AreEqual(300f, TutorialBossPathRules.EffectiveLimitSeconds(0f, 300f), "코드 기본값도 그대로");
        Assert.AreEqual(420f, TutorialBossPathRules.EffectiveLimitSeconds(420f, 600f), "튜토리얼 시작 API 가 덮어씀");
        Assert.AreEqual(600f, TutorialBossPathRules.EffectiveLimitSeconds(-1f, 600f), "음수 = 덮어쓰지 않음");
        Assert.AreEqual(420f, TutorialBossPathRules.DefaultTutorialTimerLimitSeconds, "D6");
    }

    [Test]
    public void TutorialLimit_ExpiresAt420_InTimerDomain()
    {
        // 시작 시각 + 덮어쓴 제한시간 = 만료 시각. 시간 도메인(GameNow)·만료 판정은 기존 순수 계산 그대로.
        double start = 1000.0;
        double expiresAt = start + TutorialBossPathRules.EffectiveLimitSeconds(420f, 600f);
        Assert.AreEqual(420.0, BossTimerManager.RemainingAt(expiresAt, start), Eps);
        Assert.IsFalse(BossTimerManager.IsExpiredAt(expiresAt, start + 419.9));
        Assert.IsTrue(BossTimerManager.IsExpiredAt(expiresAt, start + 420.0));
    }

    // ─── HUD ──────────────────────────────────────────────────────────────

    [Test]
    public void Hud_HiddenOnlyInTutorialIdleBeforeReveal()
    {
        Assert.IsTrue(TutorialBossPathRules.ShouldHideTimerHud(true, BossTimerManager.TimerState.Idle, false), "튜토리얼 Idle = 숨김");
        Assert.IsFalse(TutorialBossPathRules.ShouldHideTimerHud(true, BossTimerManager.TimerState.Idle, true), "Z4-0 표시 = 빈 게이지");
        Assert.IsFalse(TutorialBossPathRules.ShouldHideTimerHud(false, BossTimerManager.TimerState.Idle, false), "비튜토리얼 = 기존(빈 틀 보임)");
    }

    [TestCase(BossTimerManager.TimerState.Running)]
    [TestCase(BossTimerManager.TimerState.Expired)]
    [TestCase(BossTimerManager.TimerState.Stopped)]
    public void Hud_AlwaysShownAfterStart(BossTimerManager.TimerState state)
    {
        Assert.IsFalse(TutorialBossPathRules.ShouldHideTimerHud(true, state, false), "시작 뒤에는 표시 플래그와 무관하게 보인다");
    }

    // ─── 패드 · 문 ────────────────────────────────────────────────────────

    [Test]
    public void Pad_LockedInTutorialUntilG10()
    {
        Assert.IsFalse(TutorialBossPathRules.IsPadEnabled(true, false), "튜토리얼 G10 전 = 잠김");
        Assert.IsTrue(TutorialBossPathRules.IsPadEnabled(true, true), "G10 열림 = 활성");
        Assert.IsTrue(TutorialBossPathRules.IsPadEnabled(false, false), "비튜토리얼 = 처음부터 활성(기존)");
    }

    [Test]
    public void Pad_LockedWhileTraitResetInProgress()
    {
        // 할 일 30 D3 — 패드는 한 명만 서도 전원을 끌고 가므로 팝업·재선택 중인 사람이 있으면 잠근다.
        Assert.IsFalse(TutorialBossPathRules.IsPadEnabled(true, true, true), "G10 열림 + 재선택 중 = 잠김");
        Assert.IsTrue(TutorialBossPathRules.IsPadEnabled(true, true, false), "재선택 끝 = 활성");
        Assert.IsFalse(TutorialBossPathRules.IsPadEnabled(true, false, false), "G10 전 잠금은 그대로");
        Assert.IsTrue(TutorialBossPathRules.IsPadEnabled(false, false, false), "비튜토리얼(특성 없음) = 기존 그대로");
    }

    [Test]
    public void Pad_LockedPadReportsEmptyEvenWhenOccupied()
    {
        Assert.IsFalse(TutorialBossPathRules.PadOccupied(true, false), "잠긴 패드 = 카운트다운 없음");
        Assert.IsTrue(TutorialBossPathRules.PadOccupied(true, true));
        Assert.IsFalse(TutorialBossPathRules.PadOccupied(false, true));
    }

    [Test]
    public void ElevatorDoor_OpensWithG10_OnlyInTutorial()
    {
        Assert.IsFalse(TutorialBossPathRules.ShouldOpenElevatorDoor(true, false));
        Assert.IsTrue(TutorialBossPathRules.ShouldOpenElevatorDoor(true, true));
        Assert.IsFalse(TutorialBossPathRules.ShouldOpenElevatorDoor(false, true), "비튜토리얼은 손대지 않는다(기존 Closed)");
    }

    // ─── 만료 우선 ────────────────────────────────────────────────────────
    // BossTeleportManager.ForceStartEncounter 를 막는 것은 '진행 중(패드 카운트다운·도착 대기)' 뿐이다.
    // 튜토리얼 잠금은 패드 점유만 끄므로, 잠긴 동안에는 패드가 진행 중을 만들 수 없다 → 강제 이동은 G9·G10·배리어·패드와 무관하게 개시된다.

    static bool ForcedStartBlockedByPad(bool tutorial, bool g10Open, bool anyoneOnPad) =>
        TutorialBossPathRules.PadOccupied(anyoneOnPad, TutorialBossPathRules.IsPadEnabled(tutorial, g10Open));

    [Test]
    public void Expiry_ForcedTeleportNeverBlockedByLockedPad([Values(false, true)] bool g9Open, [Values(false, true)] bool anyoneOnPad)
    {
        // G10 전(패드 잠김)에는 누가 서 있든, G9 가 열렸든 말든 패드가 강제 이동을 미루게 하지 않는다.
        Assert.IsFalse(ForcedStartBlockedByPad(true, false, anyoneOnPad), $"G9={g9Open} 패드위={anyoneOnPad}");
    }

    [Test]
    public void Expiry_AfterG10_PadCountdownMayDeferButSameTeleport()
    {
        // G10 뒤 패드 카운트다운 중 만료 → 기존 계약대로 강제는 미뤄지고(만료 유지·재시도) 패드 경로가 같은 이동을 한다.
        Assert.IsTrue(ForcedStartBlockedByPad(true, true, true));
        Assert.IsFalse(ForcedStartBlockedByPad(true, true, false));
    }

    // ─── G8 = 타이머 시작(단일 기준) 순서 ─────────────────────────────────

    static TutorialFlowModel NewModelThroughG7()
    {
        var m = new TutorialFlowModel(60.0, 9, 3, 4);
        m.AddPlayer(1);
        for (int i = 0; i <= (int)TutorialGate.G7_Zone4Entry; i++)
            Assert.IsTrue(m.TryOpenGate((TutorialGate)i), $"{(TutorialGate)i}");
        return m;
    }

    [Test]
    public void G8_CannotOpenBeforeG7()
    {
        var m = new TutorialFlowModel(60.0, 9, 3, 4);
        m.AddPlayer(1);
        Assert.IsFalse(m.TryOpenGate(TutorialGate.G8_TimerStart), "존4 진입 전에는 타이머가 시작되지 않는다");
    }

    [Test]
    public void G9_RequestedBeforeG8_OpensWithG8()
    {
        TutorialFlowModel m = NewModelThroughG7();
        Assert.IsFalse(m.RequestGate(TutorialGate.G9_Corridor4Entry), "중간보스를 타이머 전에 잡음 → 보류");

        int opened = 0;
        m.GateOpened += (g, _) => { if (g == TutorialGate.G8_TimerStart || g == TutorialGate.G9_Corridor4Entry) opened++; };
        Assert.IsTrue(m.TryOpenGate(TutorialGate.G8_TimerStart));
        m.Evaluate(0.0);
        Assert.IsTrue(m.IsGateOpen(TutorialGate.G9_Corridor4Entry), "G8 이 열리는 Evaluate 에서 G9");
        Assert.AreEqual(2, opened);
        Assert.IsFalse(m.IsGateOpen(TutorialGate.G10_BossEnterAndPad), "G10(패드)은 복도4 특성 전원 선택 전까지 닫힘");
    }
}
