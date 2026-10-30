#if UNITY_EDITOR
/// <summary>
/// QA 자동화 진입점 한 곳 — 에이전트·스크립트가 창 없이 부른다. QA 창(Tools/QA)의 두 섹션(야간·QA)과 같은 동작이고,
/// 이름 = 동사 + 대상(야간은 대상에 Night). 각 메서드는 실행부(<see cref="QANightlyMenu"/>·<see cref="QAScenarioPlay"/>·<see cref="QAScenario48Menu"/>)로 위임만 한다.
/// 48·88·103·103 배치·57.2·빌드·디버그 진입점은 삭제됨(155).
/// <para>MCP: <c>Unity_RunCommand</c> 에서 <c>QAAutomation.StartNight();</c> 처럼 호출한다.
/// 🔴 Play 는 <see cref="PlayScenario"/>·<see cref="StartNight"/> 로만 시작한다(isPlaying 직접 토글 금지 — 가상 플레이어가 안 붙는다).
/// 끝나면 <see cref="StopScenario"/> → <see cref="DeactivateVirtualPlayers"/> → <see cref="ClearScenarioMode"/> 로 원복.</para>
/// </summary>
public static class QAAutomation
{
    // ── 야간 QA(108) ─────────────────────────────────────────

    public static void StartNight() => QANightlyMenu.StartNight();

    public static void ResumeNight() => QANightlyMenu.ResumeLast();

    /// <summary>지금 판이 끝나면 야간 연속 루프 정지.</summary>
    public static void StopNightAfterCurrentRun() => QANightlyMenu.StopAfterCurrent();

    /// <summary>시드 파일·PC 설정·연속 루프 진행을 콘솔에.</summary>
    public static void LogNightStatus() => QANightlyMenu.Status();

    /// <summary>야간 배치 report.html.</summary>
    public static void OpenNightReport() => QANightlyMenu.OpenReport();

    public static void ValidateNightSeed() => QANightlyMenu.ValidateSeed();

    public static void CreateNightLocalSettings() => QANightlyMenu.CreateLocal();

    public static void TestNightObs() => QANightlyMenu.TestObs();

    // ── QA 시나리오 ──────────────────────────────────────────

    /// <summary>Play Active Scenario — 툴바 Play 와 같은 경로(가상 플레이어 포함).</summary>
    public static void PlayScenario() => QAScenarioPlay.Play();

    public static void StopScenario() => QAScenarioPlay.Stop();

    public static void ActivateVirtualPlayers() => QAScenarioPlay.ActivateVirtualPlayers();

    public static void DeactivateVirtualPlayers() => QAScenarioPlay.DeactivateVirtualPlayers();

    public static void EnsureClientTags() => QAScenarioPlay.EnsureClientTags();

    /// <summary>시나리오 모드 해제 — 평소 Play 에 QA 하네스가 시나리오를 다시 돌리지 않게(QA 접속 설정·원격 지연 파일도 삭제).</summary>
    public static void ClearScenarioMode() => QAScenario48Menu.Clear();
}
#endif
