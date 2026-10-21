using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// EditMode 테스트 <b>전체</b>를 메뉴 한 번으로 돌리고 요약을 콘솔에 남긴다.
/// 영역별 러너(<c>Tools/Tests/… EditMode 테스트 실행</c>)는 픽스처 정규식으로 고르지만, 공용 코드
/// (이펙트 매니저 등)를 고친 뒤에는 회귀를 한 번에 봐야 한다.
///
/// MCP <c>Unity_RunCommand</c> 안에서 <c>TestRunnerApi.Execute</c> 를 직접 부르면 거부되므로
/// 메뉴로 열어 둔다(<c>EditorApplication.ExecuteMenuItem</c>). 구현은 <see cref="CombatEditModeTestRunner"/> 패턴.
/// </summary>
internal static class AllEditModeTestRunner
{
    const string Tag = "[AllEditModeTests]";

    /// <summary>
    /// 마지막 실행 요약·실패 목록. 콘솔이 다른 로그에 밀려도 <c>SessionState.GetString</c> 으로 읽을 수 있게 남긴다
    /// (에디터를 끄면 사라진다). 실행 중이면 "running".
    /// </summary>
    public const string SummaryKey = "AllEditModeTests.LastSummary";
    public const string FailuresKey = "AllEditModeTests.LastFailures";

    static TestRunnerApi _runner;

    [MenuItem("Tools/Tests/EditMode 전체 실행")]
    static void Run()
    {
        if (_runner != null)
        {
            Debug.LogWarning($"{Tag} 이미 실행 중이다 — 끝날 때까지 기다릴 것.");
            return;
        }

        SessionState.SetString(SummaryKey, "running");
        SessionState.SetString(FailuresKey, "");
        _runner = ScriptableObject.CreateInstance<TestRunnerApi>();
        _runner.RegisterCallbacks(new Callbacks());
        _runner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
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
            // 실패만 남긴다 — 케이스마다 찍으면 요약이 밀린다.
            if (result.TestStatus != TestStatus.Failed || result.HasChildren) return;

            SessionState.SetString(FailuresKey,
                SessionState.GetString(FailuresKey, "") + $"{result.FullName} :: {result.Message}\n");
            Debug.LogError($"{Tag} FAIL {result.FullName}\n{result.Message}\n{result.StackTrace}");
        }
    }
}
