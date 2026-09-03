using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 전투(몬스터 경직 · 보스 카운터) EditMode 테스트를 메뉴 한 번으로 돌리고 결과를 콘솔에 남긴다.
///
/// 왜 필요한가 — 이 픽스처들은 asmdef 없는 <c>Assembly-CSharp-Editor</c> 에 있어서
/// 어셈블리 이름으로 골라낼 수 없다(같은 어셈블리에 무관한 테스트가 섞여 있다).
/// 그래서 <b>테스트 풀네임 정규식</b>으로 거른다. 픽스처가 늘면 <see cref="Fixtures"/> 에만 추가하면 된다.
///
/// 🔴 컴파일 0에러는 "동작한다"가 아니다. 이 메뉴의 존재 이유가 그것이다 —
///    코드를 고친 뒤 반드시 여기까지 돌려서 통과 수를 눈으로 확인할 것.
///
/// 구현은 <c>Assets/1.Scripts/Rendering/Editor/WallOcclusionTestRunner.cs</c> 의 패턴을 따랐다.
/// </summary>
internal static class CombatEditModeTestRunner
{
    // 테스트 풀네임에 걸리는 정규식. 픽스처를 추가하면 여기에 한 줄 넣는다.
    static readonly string[] Fixtures =
    {
        "^MonsterHitReactionPolicyTests",
        "^BossCounter",  // BossCounterWindupGateTests / BossCounterProgressTests / BossCounterDataTests
        "^BossOpening",  // BossOpeningAttackPolicyTests
        "^BossAggro"     // BossAggroPolicyTests
    };

    const string Tag = "[CombatTests]";

    static TestRunnerApi _runner;

    [MenuItem("Tools/Tests/전투 EditMode 테스트 실행")]
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
            // 🔴 이 수는 **필터링된 개수가 아니다** — 로드된 EditMode 트리 전체를 센다
            //    (실측 2026-09-02: 필터가 9건만 잡았는데 여기엔 69가 찍혔다).
            //    실제로 몇 건이 돌았는지는 아래 RunFinished 의 통과/실패/건너뜀 으로만 판단할 것.
            Debug.Log($"{Tag} 실행 시작 (로드된 트리 {testsToRun.TestCaseCount}건 — 실행 수 아님)");
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            string summary =
                $"{Tag} {result.TestStatus} — 통과 {result.PassCount} / 실패 {result.FailCount} / " +
                $"건너뜀 {result.SkipCount} ({result.Duration:F3}s)";

            // 통과 0건은 "실패 0건"과 다르다 — 정규식이 아무것도 못 잡아도 TestStatus 는 Passed 가 된다.
            // 이 계통의 대표적 거짓 신호라 에러로 올려 눈에 띄게 한다.
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
            // 실패만 남긴다 — 케이스마다 로그를 찍으면 정작 필요한 요약이 밀린다(교훈 #8).
            if (result.TestStatus != TestStatus.Failed) return;

            Debug.LogError($"{Tag} FAIL {result.FullName}\n{result.Message}\n{result.StackTrace}");
        }
    }
}
