using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 설정창(PLAN-settings) EditMode 테스트를 메뉴 한 번으로 돌리고 결과를 콘솔에 남긴다.
/// 패턴은 <see cref="PlayerEditModeTestRunner"/> 와 같다 — 픽스처가 늘면 <see cref="Fixtures"/> 에만 추가한다.
/// </summary>
internal static class SettingsEditModeTestRunner
{
    static readonly string[] Fixtures =
    {
        "^SettingsRulesTests$",       // 해상도 후보·기본값·폴백 · 프레임 제한 · 창 모드 · 데미지 숫자 끄기
        "^DashDirectionRulesTests$",  // 대시 방향 옵션(은희 할 일 25 흡수)
    };

    const string Tag = "[SettingsTests]";

    static TestRunnerApi _runner;

    [MenuItem("Tools/Tests/설정창 EditMode 테스트 실행")]
    static void Run()
    {
        if (_runner != null)
        {
            Debug.LogWarning($"{Tag} 이미 실행 중이다 — 끝날 때까지 기다릴 것.");
            return;
        }

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

            // 통과 0건은 "실패 0건"과 다르다 — 필터가 아무것도 못 잡아도 TestStatus 는 Passed 가 된다.
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
