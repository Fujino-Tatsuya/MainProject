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
    /// <para>
    /// 2026-10-19 PLAN-48 C13: <c>5.ResultScene</c> 은 흐름에서 빠졌다(결과 = 로비 결과 패널) — EditorBuildSettings 에서도 뺐다.
    /// 씬 파일·<c>ResultSceneManager</c> 는 보관만 한다. 대신 데모 대기 씬 <c>6.DemoConnectScene</c> 이 필수다.
    /// </para>
    /// </summary>
    private static readonly string[] RequiredScenes =
    {
        BootstrapScenePath,
        "Assets/0.Scenes/MainFlow/1.TitleScene.unity",
        "Assets/0.Scenes/MainFlow/2.LoadingScene.unity",
        "Assets/0.Scenes/MainFlow/3.LobbyScene.unity",
        "Assets/0.Scenes/MainFlow/4.MapScene.unity", // 🔴 정본 — 부트스트랩이 여는 전투 맵
        "Assets/0.Scenes/MainFlow/6.DemoConnectScene.unity", // 부스 데모 접속 대기(DemoSessionDirector)
    };

    private const string DefaultOutput = "../MainProjectBuilds/Windows/MainProject.exe";

    private const string DevOutput = "../MainProjectBuilds/WindowsDev/MainProject.exe";

    // 경석 10-20: 기본 빌드 = 23호 A 보스(TwentyThree.prefab → No23_A). B 는 아래 'B 보스' 메뉴.
    [MenuItem("Build/Windows64 Player (MainFlow · A 보스)")]
    public static void BuildWindows64FromMenu()
    {
        Build(ResolveOutputPath(null), BuildOptions.None);
    }

    /// <summary>
    /// 측정용 개발 빌드(PLAN-cleanup-optimization §S6). ProfilerHUD·ZonePerfRecorder 는 DEVELOPMENT_BUILD 에서만 살아 있다.
    /// 프로파일러 자동 연결·딥 프로파일은 켜지 않는다 — 그 자체가 프레임 비용이라 측정을 오염시킨다.
    /// 출력 폴더를 릴리스와 분리해 덮어쓰지 않는다. 결과 CSV = exe 옆 <c>ZonePerf/</c>.
    /// 두 메뉴 모두 <c>DataTableBuild.WithTableValues</c> 로 감싸 **xlsx 값**으로 빌드한다(10-07 — 전엔 Build 버튼 훅을
    /// 안 거쳐 인스펙터 값으로 빌드됐다).
    /// </summary>
    [MenuItem("Build/Windows64 Player (MainFlow · 측정용 Development)")]
    public static void BuildWindows64DevFromMenu()
    {
        Build(Path.GetFullPath(DevOutput), BuildOptions.Development);
    }

    private const string RaidOutput = "../MainProjectBuilds/WindowsRaid/MainProject.exe";

    /// <summary>
    /// 23호 **레이드 변형**으로 뽑는 테스트 빌드(팀장 10-16 — 다 같이 보스만 플레이). 이번 빌드에만 <c>BOSS_RAID_BUILD</c> 심볼을 얹어
    /// <see cref="BossVariantSelection.UseRaid"/> 가 true 가 된다 — ProjectSettings 는 건드리지 않으므로 다른 빌드는 그대로 기존 보스다.
    /// 튜토리얼 스킵(Alt+1~9)·보스방 워프(F5)는 DEV_HOTKEYS(Standalone 기본 심볼)로 이미 들어간다.
    /// ⚠️ 호스트·클라 모두 이 빌드여야 한다(RPC 테이블·보스 프리팹이 같아야 한다).
    /// </summary>
    // 경석 10-20: 이 빌드 = 23호 B 보스(TwentyThree_Raid.prefab → No23_B). 출력 폴더 이름(WindowsRaid)은 그대로.
    [MenuItem("Build/Windows64 Player (MainFlow · B 보스)")]
    public static void BuildWindows64RaidFromMenu()
    {
        Build(Path.GetFullPath(RaidOutput), BuildOptions.None, new[] { "BOSS_RAID_BUILD" });
    }

    /// <summary>CLI -executeMethod 진입점. 성공 0, 실패 1로 종료한다.</summary>
    public static void BuildWindows64()
    {
        var output = ResolveOutputPath(GetCliArgument("-buildOutput"));
        var ok = Build(output, BuildOptions.None);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private static bool Build(string outputPath, BuildOptions buildOptions, string[] extraDefines = null)
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
            // 이번 빌드에만 얹는 심볼(레이드 빌드 = BOSS_RAID_BUILD). ProjectSettings 의 Standalone 심볼(DEV_HOTKEYS 등)에 더해진다.
            extraScriptingDefines = extraDefines,
        };
        if (extraDefines != null && extraDefines.Length > 0)
            Debug.Log($"[Build] extraDefines={string.Join(";", extraDefines)}");

        // 빌드 = 항상 테이블(xlsx) 값 — Build 버튼 훅과 같은 경로로 감싼다(10-07, 이전엔 인스펙터 값으로 빌드됐다).
        UnityEditor.Build.Reporting.BuildReport report = null;
        DataTableBuild.WithTableValues(() => report = BuildPipeline.BuildPlayer(options));
        var summary = report.summary;

        Debug.Log($"[Build] result={summary.result} errors={summary.totalErrors} warnings={summary.totalWarnings} " +
                  $"size={summary.totalSize / (1024 * 1024)}MB time={summary.totalTime}");

        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[Build] FAILED result={summary.result}");
            return false;
        }

        Debug.Log($"[Build] SUCCEEDED exe={outputPath}");
        CopyDemoConnectionSamples(outputPath);
        return true;
    }

    /// <summary>
    /// 부스 데모 연결 설정 샘플(공용 auto · 호스트·클라 명시)을 exe 폴더에 둔다 — Docs/tech/lobby-demo-flow.md. 세 빌드 메뉴·CLI 가 모두 여기를 지난다.
    /// 실패해도 빌드 결과는 그대로 성공이다(샘플 없이도 exe 는 호스트로 뜬다). 콘솔 에러로만 알린다.
    /// </summary>
    private static void CopyDemoConnectionSamples(string outputPath)
    {
        var projectRoot = Path.GetDirectoryName(Application.dataPath);
        var errors = new List<string>();
        var copied = DemoConnectionSamples.CopyToBuild(DemoConnectionSamples.ResolveSourceDirectory(projectRoot), outputPath, errors);
        foreach (var error in errors)
            Debug.LogError($"[Build] demo-connection {error}");
        Debug.Log($"[Build] demo-connection samples copied={copied}/{DemoConnectionSamples.SampleFileNames.Count} " +
                  $"dir={Path.GetDirectoryName(outputPath)} — {DemoConnectionSamples.AutoSampleFileName} 의 hostAddress 를 고쳐 {DemoConnectionConfig.FileName} 으로 바꾸고 폴더째 모든 PC 에 복사");
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
