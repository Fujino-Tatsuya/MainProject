using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 슬로우 모션 산식·전역 시간 레이어 규칙 EditMode 테스트를 메뉴 한 번으로 돌리고 결과를 콘솔에 남긴다.
/// 패턴은 <see cref="TrainingDummyEditModeTestRunner"/> 를 따랐다.
/// </summary>
internal static class SlowMotionEditModeTestRunner
{
    static readonly string[] Fixtures =
    {
        "^SlowMotionTimelineTests$",
        "^SlowMotionSessionTests$",
        "^InterruptSlowMotionTrackerTests$",
        "^SlowMotionCameraZoomTests$",
        "^BossTimerClockTests$",  // 보스 제한시간 GameNow 도메인(D16)
        "^InterruptDarkAdaptationModelTests$",     // 간파 성공 암순응(할 일 87 C3)
        "^DarkAdaptationRendererSetupTests$",
        "^InterruptProductionDirectorTests$",      // 간파 연출 디렉터 스위치(할 일 104 → 118)
        "^InterruptProductionPrefabTests$",        // 간파 연출 디렉터 프리팹·프로필 흡수(할 일 118 C2)
        "^InterruptProductionStageTests$",         // 간파 연출 서버 사건 4개·단계(할 일 118 C3)
        "^InterruptProductionChannelTests$",       // 간파 연출 피어 단계·개인/공용 채널(할 일 118 C4)
        "^InterruptProductionLiveReadTests$",      // 간파 연출 인스펙터 값 사건마다 읽기(캐싱 금지, 할 일 118 C4)
        "^InterruptOwnerPresentationTests$",       // 오너 연출 상태 기계·클라 예측 입력(할 일 132 단계 2)
        "^InterruptTargetRegistryTests$",          // 간파 예측 대상 캐시 등록·해제(할 일 132 정정1)
        "^DebugInterruptLabTests$",                // 디버그 배속 합성·허수아비 회복(할 일 115 C5)
    };

    const string Tag = "[SlowMotionTests]";

    static TestRunnerApi _runner;

    [MenuItem("Tools/Tests/슬로우 모션 EditMode 테스트 실행")]
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
