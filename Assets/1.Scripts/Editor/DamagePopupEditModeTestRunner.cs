using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 데미지 숫자(EuniTween 이징 + 플로팅 데미지 누적 규칙) EditMode 테스트를 메뉴 한 번으로 돌리고 결과를 콘솔에 남긴다.
///
/// EuniTween 테스트는 자기 asmdef 가 있어 어셈블리 이름으로, 플로팅 데미지 정책 테스트는 asmdef 없는
/// <c>Assembly-CSharp-Editor</c> 에 섞여 있어 풀네임 정규식으로 고른다. Filter 를 두 개 넘기면 합집합으로 돈다.
///
/// 패턴은 <see cref="CombatEditModeTestRunner"/> 를 따랐다.
/// </summary>
internal static class DamagePopupEditModeTestRunner
{
    static readonly string[] Assemblies = { "EuniTween.EditModeTests" };

    static readonly string[] Fixtures =
    {
        "^FloatingDamage" // 누적 키·표시 스타일 등 FloatingDamage* 정책
    };

    const string Tag = "[DamagePopupTests]";

    static TestRunnerApi _runner;

    [MenuItem("Tools/Tests/데미지 숫자 EditMode 테스트 실행")]
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
            new Filter { testMode = TestMode.EditMode, assemblyNames = Assemblies },
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
