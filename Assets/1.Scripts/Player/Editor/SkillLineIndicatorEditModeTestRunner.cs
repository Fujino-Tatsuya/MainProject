using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 스킬 직선 인디케이터 모양 계산 + 미리보기가 기대는 모터 스윕 EditMode 테스트를 메뉴 한 번으로 돌리고 결과를 콘솔에 남긴다.
///
/// 셋 다 asmdef 없는 <c>Assembly-CSharp-Editor</c> 에 있어 풀네임 정규식으로 고른다.
/// 모터 테스트를 같이 도는 건 미리보기용 오버로드가 기존 이동·리플레이를 안 바꿨는지 보려는 것이다.
///
/// 패턴은 <see cref="DamagePopupEditModeTestRunner"/> 를 따랐다.
/// </summary>
internal static class SkillLineIndicatorEditModeTestRunner
{
    static readonly string[] Fixtures =
    {
        "^SkillPreviewShapeTests",
        "^PlayerMotionSweepStepTests",
        "^PlayerMovementSimulationTests"
    };

    const string Tag = "[SkillLineIndicatorTests]";

    static TestRunnerApi _runner;

    [MenuItem("Tools/Tests/스킬 직선 인디케이터 EditMode 테스트 실행")]
    static void Run()
    {
        if (_runner != null)
        {
            Debug.LogWarning($"{Tag} 이미 실행 중이다 — 끝날 때까지 기다릴 것.");
            return;
        }

        _runner = ScriptableObject.CreateInstance<TestRunnerApi>();
        _runner.RegisterCallbacks(new Callbacks());
        _runner.Execute(new ExecutionSettings(
            new Filter { testMode = TestMode.EditMode, groupNames = Fixtures }));
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
