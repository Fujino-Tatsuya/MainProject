#if UNITY_EDITOR
/// <summary>
/// QA 자동화 진입점 한 곳 — 에이전트·스크립트가 창 없이 부른다. 이름 = 섹션 접두어(공통은 없음) + 동사 + 대상.
/// 각 메서드는 기존 실행부(<see cref="QAScenarioPlay"/>·<see cref="QAScenario48Menu"/>·<see cref="QAScenario88Menu"/>·
/// <see cref="QAScenario103Menu"/>·<see cref="QABoss103BatchRunner"/>·<see cref="QANightlyMenu"/>)로 위임만 한다. 나머지 버튼은 QA 창(Tools/QA)에서.
/// <para>MCP: <c>Unity_RunCommand</c> 에서 <c>QAAutomation.NightStart();</c> 처럼 호출한다.
/// 🔴 Play 는 <see cref="PlayActiveScenario"/>·각 Run 진입점으로만 시작한다(isPlaying 직접 토글 금지 — 가상 플레이어가 안 붙는다).
/// 끝나면 <see cref="StopActiveScenario"/> → <see cref="DeactivateVirtualPlayers"/> → <see cref="ClearScenarioMode"/> 로 원복.</para>
/// </summary>
public static class QAAutomation
{
    // ── 공통 ─────────────────────────────────────────────────

    public static void PlayActiveScenario() => QAScenarioPlay.Play();

    public static void StopActiveScenario() => QAScenarioPlay.Stop();

    public static void ActivateVirtualPlayers() => QAScenarioPlay.ActivateVirtualPlayers();

    public static void DeactivateVirtualPlayers() => QAScenarioPlay.DeactivateVirtualPlayers();

    public static void EnsureClientTags() => QAScenarioPlay.EnsureClientTags();

    public static void ClearScenarioMode() => QAScenario48Menu.Clear();

    // ── 야간 QA(108) ─────────────────────────────────────────

    public static void NightStart() => QANightlyMenu.StartNight();

    public static void NightResume() => QANightlyMenu.ResumeLast();

    /// <summary>지금 판 뒤 연속 루프 정지 — 야간·103 배치 공용(같은 루프).</summary>
    public static void NightStopAfterCurrent() => QANightlyMenu.StopAfterCurrent();

    public static void NightLogStatus() => QANightlyMenu.Status();

    /// <summary>연속 루프 리포트(report.html) — 야간·103 배치 공용.</summary>
    public static void NightOpenReport() => QANightlyMenu.OpenReport();

    public static void NightValidateSeed() => QANightlyMenu.ValidateSeed();

    public static void NightCreateLocalSettings() => QANightlyMenu.CreateLocal();

    public static void NightTestObs() => QANightlyMenu.TestObs();

    // ── 48 로비 데모 ─────────────────────────────────────────

    public static void Scenario48RunSoloBatch() => QAScenario48Menu.RunSolo();

    public static void Scenario48RunMppmBatch() => QAScenario48Menu.RunMppm();

    // ── 88 간파 지연 ─────────────────────────────────────────

    public static void Interrupt88RunSuite() => QAScenario88Menu.RunSuite();

    public static void Interrupt88RunSingle() => QAScenario88Menu.RunSingle();

    public static void Interrupt88StopSuite() => QAScenario88Menu.StopSuite();

    // ── 103 보스전 ───────────────────────────────────────────

    public static void Boss103RunMatch() => QAScenario103Menu.RunOne();

    public static void Boss103LogStatus() => QAScenario103Menu.Status();

    public static void Boss103BatchStart90() => QABoss103BatchRunner.Start90();

    public static void Boss103BatchStart30() => QABoss103BatchRunner.Start30();

    public static void Boss103BatchResume() => QABoss103BatchRunner.ResumeLast();

    public static void Boss103BatchLogStatus() => QABoss103BatchRunner.Status();

    // ── 57.2 보스 프로파일 ───────────────────────────────────

    public static void Profile572AnalyzeLatest() => QABossProfileAnalyzer.AnalyzeLatest();
}
#endif
