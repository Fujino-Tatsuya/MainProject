#if UNITY_EDITOR
/// <summary>
/// QA 자동화 진입점 한 곳 — 에이전트·스크립트가 메뉴 경로 없이 부른다. QA 창(Tools/QA) 버튼도 같은 진입점을 쓴다.
/// 각 메서드는 기존 실행부(<see cref="QAScenarioPlay"/>·<see cref="QAScenario48Menu"/>·<see cref="QAScenario88Menu"/>·
/// <see cref="QAScenario103Menu"/>·<see cref="QABoss103BatchRunner"/>·<see cref="QANightlyMenu"/>)로 위임만 한다.
/// <para>MCP: <c>Unity_RunCommand</c> 에서 <c>QAAutomation.StartNight();</c> 처럼 호출한다.
/// 🔴 Play 는 <see cref="PlayActiveScenario"/>·각 Run 진입점으로만 시작한다(isPlaying 직접 토글 금지 — 가상 플레이어가 안 붙는다).
/// 끝나면 <see cref="StopActiveScenario"/> → <see cref="DeactivateVirtualPlayers"/> → <see cref="ClearScenarioMode"/> 로 원복.</para>
/// </summary>
public static class QAAutomation
{
    // ── 공통 ─────────────────────────────────────────────────

    public static void OpenWindow() => QASettingsWindow.Open();

    public static void PlayActiveScenario() => QAScenarioPlay.Play();

    public static void StopActiveScenario() => QAScenarioPlay.Stop();

    public static void ActivateVirtualPlayers() => QAScenarioPlay.ActivateVirtualPlayers();

    public static void DeactivateVirtualPlayers() => QAScenarioPlay.DeactivateVirtualPlayers();

    public static void EnsureMppmClientTags() => QAScenarioPlay.EnsureClientTags();

    public static void MppmStatus() => QAScenarioPlay.Status();

    public static void VirtualPlayerStates() => QAScenarioPlay.PlayerStates();

    // ── 48 ───────────────────────────────────────────────────

    public static void ClearScenarioMode() => QAScenario48Menu.Clear();

    public static void Scenario48RunSoloBatch() => QAScenario48Menu.RunSolo();

    public static void Scenario48RunMppmBatch() => QAScenario48Menu.RunMppm();

    public static void Scenario48OpenReportFolder() => QAScenario48Menu.OpenReports();

    // ── 야간 QA(108) ─────────────────────────────────────────

    public static void StartNight() => QANightlyMenu.StartNight();

    public static void ResumeNight() => QANightlyMenu.ResumeLast();

    public static void StopNightAfterCurrentRun() => QANightlyMenu.StopAfterCurrent();

    public static void NightStatus() => QANightlyMenu.Status();

    public static void NightOpenReport() => QANightlyMenu.OpenReport();

    public static void NightValidateSeedFile() => QANightlyMenu.ValidateSeed();

    public static void NightCreateLocalSettingsFile() => QANightlyMenu.CreateLocal();

    public static void NightTestObsConnection() => QANightlyMenu.TestObs();

    // ── 103 보스전 ───────────────────────────────────────────

    public static void Boss103Run1Match() => QAScenario103Menu.RunOne();

    public static void Boss103Status() => QAScenario103Menu.Status();

    public static void Boss103DeleteConfigFiles() => QAScenario103Menu.DeleteConfigFiles();

    public static void Boss103BatchStart90() => QABoss103BatchRunner.Start90();

    public static void Boss103BatchStart30() => QABoss103BatchRunner.Start30();

    public static void Boss103BatchResumeLast() => QABoss103BatchRunner.ResumeLast();

    public static void Boss103BatchStopAfterCurrent() => QABoss103BatchRunner.StopAfterCurrent();

    public static void Boss103BatchStatus() => QABoss103BatchRunner.Status();

    public static void Boss103BatchOpenReport() => QABoss103BatchRunner.OpenReport();

    public static void MinimizeWindows() => QAWindowMinimizer.MinimizeAll();

    // ── 88 간파 지연 ─────────────────────────────────────────

    public static void Interrupt88RunSuite() => QAScenario88Menu.RunSuite();

    public static void Interrupt88RunSingle() => QAScenario88Menu.RunSingle();

    public static void Interrupt88Stop() => QAScenario88Menu.StopSuite();

    /// <summary>MPPM 클론에 원격 지연(ms)을 건다. 0 이하 = 원복.</summary>
    public static int Interrupt88SetRemoteDelay(int delayMs) => QAScenario88Menu.SetRemoteDelay(delayMs);

    public static int Interrupt88ResetRemoteDelay() => QAScenario88Menu.ResetRemoteDelay();

    // ── 57.2 ─────────────────────────────────────────────────

    public static void AnalyzeLatestBossProfile() => QABossProfileAnalyzer.AnalyzeLatest();
}
#endif
