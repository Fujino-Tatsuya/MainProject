using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 결과 화면 플레이어별 통계(PLAN-result-stats) EditMode 테스트를 메뉴 한 번으로 돌리고 결과를 콘솔에 남긴다.
///
/// 픽스처가 asmdef 없는 <c>Assembly-CSharp-Editor</c> 에 있어 <b>테스트 풀네임 정규식</b>으로 거른다.
/// 구현은 <c>Assets/1.Scripts/Editor/CombatEditModeTestRunner.cs</c> 패턴을 따랐다.
/// </summary>
internal static class ResultStatsEditModeTestRunner
{
    static readonly string[] Fixtures =
    {
        "^SessionStatsAggregatorTests", // 집계 필터·막타·사망 전이
        "^SessionResultPayloadTests"    // GoToResult 가변 페이로드 왕복
    };

    const string Tag = "[ResultStatsTests]";

    static TestRunnerApi _runner;

    [MenuItem("Tools/Tests/결과 통계 EditMode 테스트 실행")]
    static void Run()
    {
        if (_runner != null)
        {
            Debug.LogWarning($"{Tag} 이미 실행 중이다 — 끝날 때까지 기다릴 것.");
            return;
        }

        _runner = ScriptableObject.CreateInstance<TestRunnerApi>();
        _runner.RegisterCallbacks(new Callbacks());
        _runner.Execute(new ExecutionSettings(new Filter
        {
            testMode = TestMode.EditMode,
            groupNames = Fixtures
        }));
    }

    sealed class Callbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun)
        {
            // 이 수는 필터링된 개수가 아니라 로드된 EditMode 트리 전체다 — 실제 실행 수는 RunFinished 로 본다.
            Debug.Log($"{Tag} 실행 시작 (로드된 트리 {testsToRun.TestCaseCount}건 — 실행 수 아님)");
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            string summary =
                $"{Tag} {result.TestStatus} — 통과 {result.PassCount} / 실패 {result.FailCount} / " +
                $"건너뜀 {result.SkipCount} ({result.Duration:F3}s)";

            // 통과 0건은 "실패 0건"과 다르다 — 정규식이 아무것도 못 잡아도 TestStatus 는 Passed 가 된다.
            if (result.FailCount > 0 || result.PassCount == 0)
                Debug.LogError(summary + (result.PassCount == 0 ? "  ⚠️ 통과 0건 — 필터가 안 걸렸는지 확인" : ""));
            else
                Debug.Log(summary);

            Object.DestroyImmediate(_runner);
            _runner = null;
        }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (result.TestStatus != TestStatus.Failed) return;

            Debug.LogError($"{Tag} FAIL {result.FullName}\n{result.Message}\n{result.StackTrace}");
        }
    }
}
