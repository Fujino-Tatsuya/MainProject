using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Windows(StandaloneWindows64) 플레이어 빌드 자동화.
/// CLI: Unity.exe -batchmode -quit -projectPath &lt;proj&gt; -executeMethod BuildWindowsPlayer.BuildWindows64 -buildOutput &lt;exe경로&gt;
/// 씬 목록의 정본은 EditorBuildSettings(enabled 항목)이며, 이 스크립트는 메인 플로우 필수 씬이
/// 빠지지 않았는지와 0번이 부트스트랩인지만 검증한다.
/// </summary>
public static class BuildWindowsPlayer
{
    private const string BootstrapScenePath = "Assets/0.Scenes/MainFlow/0.BootStrapScene.unity";

    /// <summary>
    /// 실행 시 반드시 빌드에 포함되어야 하는 메인 플로우 씬.
    ///
    /// 🔴 전투 맵이 <b>두 개 다</b> 들어 있다(2026-08-18 팀장 확정 — 레거시도 계속 출하한다).
    /// 정본은 <c>4.MapScene-trensparent</c> 다: <c>0.BootStrapScene</c> 의 GameManager 인스턴스가
    /// <c>mainGameSceneName</c> 을 그 이름으로 오버라이드하므로 <b>빌드된 게임이 실제로 여는 씬</b>이
    /// 정본 쪽이다(프리팹 기본값만 레거시라 코드만 읽으면 반대로 보인다).
    ///
    /// 이전에는 여기서 <b>레거시만</b> 요구했다 — 즉 정본이 빌드 목록에서 빠져도 빌드는 통과하고
    /// 실행하면 맵에 못 들어갔다. 게이트가 정작 지켜야 할 것을 안 지키고 있었다.
    /// </summary>
    private static readonly string[] RequiredScenes =
    {
        BootstrapScenePath,
        "Assets/0.Scenes/MainFlow/1.TitleScene.unity",
        "Assets/0.Scenes/MainFlow/2.LoadingScene.unity",
        "Assets/0.Scenes/MainFlow/3.LobbyScene.unity",
        "Assets/0.Scenes/MainFlow/4.MapScene.unity",             // 레거시 — 계속 출하한다
        "Assets/0.Scenes/MainFlow/4.MapScene-trensparent.unity", // 🔴 정본 — 부트스트랩이 여는 씬
        "Assets/0.Scenes/MainFlow/5.ResultScene.unity",
    };

    private const string DefaultOutput = "../MainProjectBuilds/Windows/MainProject.exe";

    [MenuItem("Build/Windows64 Player (MainFlow)")]
    public static void BuildWindows64FromMenu()
    {
        Build(ResolveOutputPath(null));
    }

    /// <summary>CLI -executeMethod 진입점. 성공 0, 실패 1로 종료한다.</summary>
    public static void BuildWindows64()
    {
        var output = ResolveOutputPath(GetCliArgument("-buildOutput"));
        var ok = Build(output);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private static bool Build(string outputPath)
    {
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (!Validate(scenes))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // 활성 빌드 타겟 전환은 CLI의 -buildTarget Win64가 담당한다(에디터 API 의존 축소).
        Debug.Log($"[Build] activeBuildTarget={EditorUserBuildSettings.activeBuildTarget}");
        Debug.Log($"[Build] output={outputPath}");
        Debug.Log($"[Build] scenes({scenes.Length}):{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", scenes)}");

        var options = new BuildPlayerOptions
        {
            target = BuildTarget.StandaloneWindows64,
            scenes = scenes,
            locationPathName = outputPath,
            // 릴리즈 빌드: Development Build / script debugging 미포함.
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;

        Debug.Log($"[Build] result={summary.result} errors={summary.totalErrors} warnings={summary.totalWarnings} " +
                  $"size={summary.totalSize / (1024 * 1024)}MB time={summary.totalTime}");

        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[Build] FAILED result={summary.result}");
            return false;
        }

        Debug.Log($"[Build] SUCCEEDED exe={outputPath}");
        return true;
    }

    /// <summary>메인 플로우 씬 누락과 0번 씬 오배치를 잡는다. 부트스트랩이 0번이 아니면 실행 시 타이틀로 못 넘어간다.</summary>
    private static bool Validate(IReadOnlyList<string> scenes)
    {
        if (scenes.Count == 0)
        {
            Debug.LogError("[Build] no enabled scenes in EditorBuildSettings");
            return false;
        }

        if (scenes[0] != BootstrapScenePath)
        {
            Debug.LogError($"[Build] scene index 0 must be {BootstrapScenePath} but was {scenes[0]}");
            return false;
        }

        var missing = RequiredScenes.Where(required => !scenes.Contains(required)).ToArray();
        if (missing.Length > 0)
        {
            Debug.LogError($"[Build] required scenes missing/disabled: {string.Join(", ", missing)}");
            return false;
        }

        return true;
    }

    private static string ResolveOutputPath(string cliValue)
    {
        var path = string.IsNullOrEmpty(cliValue) ? DefaultOutput : cliValue;
        return Path.GetFullPath(path);
    }

    private static string GetCliArgument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
