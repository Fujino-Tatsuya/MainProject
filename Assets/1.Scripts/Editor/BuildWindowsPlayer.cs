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
    /// 🔴 2026-09-09 씬 재배치: 전투 맵은 <c>4.MapScene</c> <b>하나</b>다. 구 정본
    /// <c>4.MapScene-trensparent</c> 는 <c>0.Scenes/Lagacy/4.MapScene_Lagacy.unity</c> 로 보관 이동했고
    /// EditorBuildSettings 에서도 빠졌다 — 더 이상 출하하지 않으므로 여기서도 요구하지 않는다.
    /// <c>0.BootStrapScene</c> 의 GameManager 인스턴스가 <c>mainGameSceneName</c> 을
    /// <c>4.MapScene</c> 으로 오버라이드한다.
    /// </summary>
    private static readonly string[] RequiredScenes =
    {
        BootstrapScenePath,
        "Assets/0.Scenes/MainFlow/1.TitleScene.unity",
        "Assets/0.Scenes/MainFlow/2.LoadingScene.unity",
        "Assets/0.Scenes/MainFlow/3.LobbyScene.unity",
        "Assets/0.Scenes/MainFlow/4.MapScene.unity", // 🔴 정본 — 부트스트랩이 여는 전투 맵
        "Assets/0.Scenes/MainFlow/5.ResultScene.unity",
    };

    private const string DefaultOutput = "../MainProjectBuilds/Windows/MainProject.exe";

    private const string DevOutput = "../MainProjectBuilds/WindowsDev/MainProject.exe";

    [MenuItem("Build/Windows64 Player (MainFlow)")]
    public static void BuildWindows64FromMenu()
    {
        Build(ResolveOutputPath(null), BuildOptions.None);
    }

    /// <summary>
    /// 측정용 개발 빌드(PLAN-cleanup-optimization §S6). ProfilerHUD·ZonePerfRecorder 는 DEVELOPMENT_BUILD 에서만 살아 있다.
    /// 프로파일러 자동 연결·딥 프로파일은 켜지 않는다 — 그 자체가 프레임 비용이라 측정을 오염시킨다.
    /// 출력 폴더를 릴리스와 분리해 덮어쓰지 않는다. 결과 CSV = exe 옆 <c>ZonePerf/</c>.
    /// ⚠️ 이 메뉴(와 위 릴리스 메뉴)는 <c>BuildPipeline.BuildPlayer</c> 직접 호출이라 데이터 테이블 훅
    /// (<c>DataTableBuild</c> — Build 버튼에만 걸림)을 거치지 않는다 → xlsx 가 아니라 **인스펙터 값**으로 빌드된다.
    /// </summary>
    [MenuItem("Build/Windows64 Player (MainFlow · 측정용 Development)")]
    public static void BuildWindows64DevFromMenu()
    {
        Build(Path.GetFullPath(DevOutput), BuildOptions.Development);
    }

    /// <summary>CLI -executeMethod 진입점. 성공 0, 실패 1로 종료한다.</summary>
    public static void BuildWindows64()
    {
        var output = ResolveOutputPath(GetCliArgument("-buildOutput"));
        var ok = Build(output, BuildOptions.None);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private static bool Build(string outputPath, BuildOptions buildOptions)
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
        Debug.Log($"[Build] output={outputPath} options={buildOptions}");
        Debug.Log($"[Build] scenes({scenes.Length}):{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", scenes)}");

        var options = new BuildPlayerOptions
        {
            target = BuildTarget.StandaloneWindows64,
            scenes = scenes,
            locationPathName = outputPath,
            // 릴리즈 = None. 측정용 = Development 만(script debugging·프로파일러 연결 미포함).
            options = buildOptions,
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
