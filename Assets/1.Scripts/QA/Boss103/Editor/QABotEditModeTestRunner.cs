#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 할 일 193 — QA 봇(103 전투 봇·88 간파 지연) EditMode 테스트만 돌리고 요약을 콘솔·SessionState 에 남긴다.
/// MCP <c>Unity_RunCommand</c> 안에서 <c>TestRunnerApi.Execute</c> 를 직접 부르면 돌지 않으므로 메뉴로 연다(<c>InterruptJudgeEditModeTestRunner</c> 패턴).
/// </summary>
internal static class QABotEditModeTestRunner
{
    const string Tag = "[QABotTests]";
    public const string SummaryKey = "QABotTests.LastSummary";
    public const string FailuresKey = "QABotTests.LastFailures";
    public const string ClassesKey = "QABotTests.LastClasses";

    static readonly string[] Fixtures =
    {
        "^QABoss103\\w*Tests$",
        "^QAInterruptLag88\\w*Tests$",
        "^QAPartyPlacement88Tests$",
    };

    static TestRunnerApi _runner;

    [MenuItem("Tools/Tests/QA 봇 EditMode 테스트 실행")]
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
#endif
