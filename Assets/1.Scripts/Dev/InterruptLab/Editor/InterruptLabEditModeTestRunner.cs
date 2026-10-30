using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 간파 랩(115·132·139) EditMode 테스트를 메뉴 한 번으로 돌리고 결과를 콘솔·SessionState 에 남긴다.
/// 패턴은 <see cref="AllEditModeTestRunner"/> 를 따랐다(MCP RunCommand 에선 메뉴로 연다).
/// </summary>
internal static class InterruptLabEditModeTestRunner
{
    static readonly string[] Fixtures =
    {
        "^InterruptCalibrationTests$",
        "^InterruptCalibrationCaptureTests$",
        "^InterruptCalibrationQuestionTests$",
        "^InterruptCalibrationSample22Tests$",
        "^InterruptWindowLengthResolverTests$",
        "^TwentyThreeInterruptWindowLengthTests$",
        "^DebugInterruptLabTests$",
        "^DebugInterruptLabIsolationTests$",
        "^DummyInterruptWindowScheduleTests$",
        "^TrainingDummyInterruptCycleTests$",
        "^InterruptJudgeShapeGizmoTests$",
        "^HitboxAnchorShapeTests$",
    };

    const string Tag = "[InterruptLabTests]";

    /// <summary>마지막 실행 요약(실행 중이면 "running")·픽스처별 통과 수와 실패 목록.</summary>
    public const string SummaryKey = "InterruptLabTests.LastSummary";
    public const string DetailsKey = "InterruptLabTests.LastDetails";

    static TestRunnerApi _runner;

    [MenuItem("Tools/Tests/간파 랩 EditMode 테스트 실행")]
    static void Run()
    {
        if (_runner != null)
        {
            Debug.LogWarning($"{Tag} 이미 실행 중이다 — 끝날 때까지 기다릴 것.");
            return;
        }

        SessionState.SetString(SummaryKey, "running");
        SessionState.SetString(DetailsKey, "");
        _runner = ScriptableObject.CreateInstance<TestRunnerApi>();
        _runner.RegisterCallbacks(new Callbacks());
        _runner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, groupNames = Fixtures }));
    }

    sealed class Callbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }

        public void RunFinished(ITestResultAdaptor result)
        {
            string summary =
                $"{Tag} {result.TestStatus} — 통과 {result.PassCount} / 실패 {result.FailCount} / " +
                $"건너뜀 {result.SkipCount} ({result.Duration:F3}s)";
            SessionState.SetString(SummaryKey, summary);

            // 통과 0건은 "실패 0건"과 다르다 — 필터가 아무것도 못 잡아도 TestStatus 는 Passed 가 된다.
            if (result.FailCount > 0 || result.PassCount == 0)
                Debug.LogError(summary + (result.PassCount == 0 ? "  ⚠️ 통과 0건 — 필터가 안 걸렸는지 확인" : ""));
            else
                Debug.Log(summary);

            // 콜백은 전역 등록이라 해제하지 않으면 다른 메뉴의 테스트 실행에도 이 태그로 결과를 찍는다.
            _runner.UnregisterCallbacks(this);
            Object.DestroyImmediate(_runner);
            _runner = null;
        }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (result.HasChildren)
            {
                // 픽스처별 통과 수(보고용)
                if (result.Test.IsSuite && result.Name.EndsWith("Tests"))
                    SessionState.SetString(DetailsKey, SessionState.GetString(DetailsKey, "") +
                        $"{result.Name}: 통과 {result.PassCount} / 실패 {result.FailCount}\n");
                return;
            }

            if (result.TestStatus != TestStatus.Failed) return;

            SessionState.SetString(DetailsKey,
                SessionState.GetString(DetailsKey, "") + $"FAIL {result.FullName} :: {result.Message}\n");
            Debug.LogError($"{Tag} FAIL {result.FullName}\n{result.Message}\n{result.StackTrace}");
        }
    }
}
