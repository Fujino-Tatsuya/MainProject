using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 튜토리얼 EditMode 테스트를 메뉴 한 번으로 돌리고 결과를 콘솔에 남긴다.
/// 패턴은 <see cref="TrainingDummyEditModeTestRunner"/> 를 따랐다. 튜토리얼 단계(S2~)의 새 픽스처는 여기에 추가한다.
/// </summary>
internal static class TutorialEditModeTestRunner
{
    static readonly string[] Fixtures =
    {
        "^TutorialFlowModelTests$",
        "^TutorialAreaTrackerTests$",
        "^TutorialBarrierRulesTests$",
        "^TutorialMonsterLedgerTests$",
        "^TutorialBossPathRulesTests$",
        "^TutorialDeathRulesTests$",
        "^TutorialTraitSelectionModelTests$", // 특성(T1) — 후보 뽑기·중복 제외·선택 검증·자동 획득·라운드 완료
        "^TutorialTraitWindowModelTests$", // 특성 창(T2) — 열림·닫힘·닫힌 라운드 재표시 금지·획득 토스트·문구
        "^TutorialGuide(Text|SubtitleQueue|Model)Tests$", // 안내 UI(S6) — 문구 변형·치환·대기 포맷·자막 큐·RebuildFromState
        "^SpinnerPracticeModelTests$", // 스피너봇 연습(S9) — 모드·창 간격·연속 놓침·스폰 반경 클램프
        "^TutorialZone1ModelTests$", // 존1(S7) — 이동 보고 상한·이동 누적·툴팁 1초·착지·목표 순서·대기 줄
    };

    const string Tag = "[TutorialTests]";

    static TestRunnerApi _runner;

    [MenuItem("Tools/Tests/튜토리얼 EditMode 테스트 실행")]
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

            // 콜백은 전역 등록이라 해제하지 않으면 다른 메뉴의 테스트 실행에도 이 태그로 결과를 찍는다.
            _runner.UnregisterCallbacks(this);
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
