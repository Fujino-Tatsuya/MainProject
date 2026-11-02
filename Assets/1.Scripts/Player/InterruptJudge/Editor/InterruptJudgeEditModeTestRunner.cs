using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 할 일 191(간파 클라 판정·서버 검증) 관련 EditMode 테스트만 돌리고 요약을 콘솔·SessionState 에 남긴다.
/// MCP <c>Unity_RunCommand</c> 안에서 <c>TestRunnerApi.Execute</c> 를 직접 부르면 거부되므로 메뉴로 연다(<see cref="AllEditModeTestRunner"/> 패턴).
/// </summary>
internal static class InterruptJudgeEditModeTestRunner
{
    const string Tag = "[InterruptJudgeTests]";
    public const string SummaryKey = "InterruptJudgeTests.LastSummary";
    public const string FailuresKey = "InterruptJudgeTests.LastFailures";
    public const string ClassesKey = "InterruptJudgeTests.LastClasses";

    // 새 매니저·검증 + 간파 스킬·대상(23호 게이트·중간보스 창·스파링 봇·허수아비)·대쉬 간파·선착순 장부.
    static readonly string[] Fixtures =
    {
        "^InterruptClaimServerTests$",
        "^InterruptJudgeCalculatorTests$",
        "^InterruptManagerSnapshotTests$",
        "^BeaverLobby\\.Player\\.Dash\\.EditModeTests\\.DashInterrupt\\w*Tests$",
        "^BeaverLobby\\.Player\\.Dash\\.EditModeTests\\.InterruptTargetGeometryTests$",
        "^InterruptTargetRegistryTests$",
        "^InterruptWindowClaimsTests$",
        "^InterruptLocalWindowClaimsTests$",
        "^InterruptSlowMotionTrackerTests$",
        "^InterruptLagModelTests$",
        "^InterruptWindowLengthResolverTests$",
        "^TwentyThreeInterruptWindowLengthTests$",
        "^CounterWindowTests$",
        "^BossCounterWindupGateTests$",
        "^BossCounterProgressTests$",
        "^BossChainCounterRulesTests$",
        "^TrainingSparringBotTests$",
        "^TrainingSparringModelTests$",
        "^TrainingDummyInterruptCycleTests$",
        "^InterruptProductionDirectorTests$",
        "^InterruptProductionStageTests$",
        "^DebugInterruptLabIsolationTests$",
        "^InterruptCalibrationCaptureTests$",
        "^DataTableTemplateTests$",
        "^DataTableSchemaTests$",
        "^DataTableSheetGroupTests$",
    };

    static TestRunnerApi _runner;

    [MenuItem("Tools/Tests/간파 판정·검증 EditMode 테스트 실행")]
    static void Run()
    {
        if (_runner != null)
        {
            Debug.LogWarning($"{Tag} 이미 실행 중이다 — 끝날 때까지 기다릴 것.");
            return;
        }

        SessionState.SetString(SummaryKey, "running");
        SessionState.SetString(FailuresKey, "");
        SessionState.SetString(ClassesKey, "");
        _runner = ScriptableObject.CreateInstance<TestRunnerApi>();
        _runner.RegisterCallbacks(new Callbacks());
        _runner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, groupNames = Fixtures }));
    }

    sealed class Callbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) => Debug.Log($"{Tag} 실행 시작 ({testsToRun.TestCaseCount}건)");

        public void RunFinished(ITestResultAdaptor result)
        {
            string summary =
                $"{Tag} {result.TestStatus} — 통과 {result.PassCount} / 실패 {result.FailCount} / " +
                $"건너뜀 {result.SkipCount} / 미결 {result.InconclusiveCount} ({result.Duration:F3}s)";
            SessionState.SetString(SummaryKey, summary);

            if (result.FailCount > 0 || result.PassCount == 0) Debug.LogError(summary);
            else Debug.Log(summary);

            Object.DestroyImmediate(_runner);
            _runner = null;
        }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            // 클래스(픽스처) 단위 결과 한 줄씩 — 리프 테스트는 실패만.
            if (result.HasChildren && result.Test.IsSuite && result.Test.TypeInfo != null && result.Test.Method == null)
            {
                SessionState.SetString(ClassesKey, SessionState.GetString(ClassesKey, "") +
                    $"{result.Test.TypeInfo.FullName}\t{result.TestStatus}\t통과 {result.PassCount}\t실패 {result.FailCount}\n");
                return;
            }

            if (result.TestStatus != TestStatus.Failed || result.HasChildren) return;

            SessionState.SetString(FailuresKey,
                SessionState.GetString(FailuresKey, "") + $"{result.FullName} :: {result.Message}\n");
            Debug.LogError($"{Tag} FAIL {result.FullName}\n{result.Message}\n{result.StackTrace}");
        }
    }
}
