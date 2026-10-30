using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 로비 캐릭터 선택 패널(할 일 48.2) EditMode 테스트를 메뉴 한 번으로 돌리고 결과를 콘솔에 남긴다.
/// 픽스처가 asmdef 없는 <c>Assembly-CSharp-Editor</c> 에 있어 테스트 풀네임 정규식으로 거른다.
/// 구현은 <c>ResultStatsEditModeTestRunner</c> 패턴.
/// </summary>
internal static class CharacterSelectEditModeTestRunner
{
    static readonly string[] Fixtures =
    {
        "^CharacterSelectPanelRulesTests", // 포커스·확정·잠금·ESC·배경 흐름·카드 이동 연출(할 일 60)
        "^CharacterSelectConfirmButtonTests", // SELECT 셰브론 클릭 = Confirm(할 일 109, 실제 프리팹)
        "^UiTriangleRaycastFilterTests", // 셰브론 삼각형 클릭 영역 판정(할 일 109)
        "^CharacterSelectChevronHoverOutlineTests", // 셰브론 hover 흰 아웃라인(할 일 109 후속, 실제 프리팹)
        "^CharacterSelectCardTemplateTests", // 가운데 카드 원본 Card_Template → 런타임 카드(할 일 109 후속, 실제 프리팹)
        "^CharacterSelectUiPolishTests" // UI 수정(할 일 162) — 이름판(영어 이름)·셰브론 hover·가운데 링·기본/코어 팝업(실제 프리팹)
    };

    const string Tag = "[CharacterSelectTests]";

    static TestRunnerApi _runner;

    [MenuItem("Tools/Tests/캐릭터 선택 패널 EditMode 테스트 실행")]
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
        public void RunStarted(ITestAdaptor testsToRun) =>
            Debug.Log($"{Tag} 실행 시작 (로드된 트리 {testsToRun.TestCaseCount}건 — 실행 수 아님)");

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
