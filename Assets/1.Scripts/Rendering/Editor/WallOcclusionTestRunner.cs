using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

internal static class WallOcclusionTestRunner
{
    private static readonly string[] TestAssemblyNames =
    {
        "VeyTrace.Rendering.Occlusion.EditModeTests",
        "VeyTrace.RuntimeSafety.EditModeTests"
    };

    private static TestRunnerApi activeRunner;

    [MenuItem("Tools/Rendering/Wall Occlusion/Run EditMode Tests")]
    private static void Run()
    {
        if (activeRunner != null)
        {
            Debug.LogWarning("[WallOcclusionTests] A test run is already active.");
            return;
        }

        activeRunner = ScriptableObject.CreateInstance<TestRunnerApi>();
        activeRunner.RegisterCallbacks(new Callbacks());
        activeRunner.Execute(new ExecutionSettings(new Filter
        {
            testMode = TestMode.EditMode,
            assemblyNames = TestAssemblyNames
        }));
    }

    private sealed class Callbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun)
        {
            Debug.Log($"[WallOcclusionTests] Running {testsToRun.TestCaseCount} tests.");
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            string summary =
                $"[WallOcclusionTests] {result.TestStatus}. " +
                $"passed={result.PassCount}, failed={result.FailCount}, " +
                $"skipped={result.SkipCount}, duration={result.Duration:F3}s";

            if (result.FailCount > 0)
                Debug.LogError(summary);
            else
                Debug.Log(summary);

            Object.DestroyImmediate(activeRunner);
            activeRunner = null;
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (result.TestStatus != TestStatus.Failed)
                return;

            Debug.LogError(
                $"[WallOcclusionTests] FAIL {result.FullName}: {result.Message}\n" +
                result.StackTrace);
        }
    }
}
