#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

/// <summary>
/// 할 일 48 빌드 시나리오(B1~B3) 준비 — QA 전용 출력 폴더에 빌드하고 인스턴스 폴더 2개(호스트·클라)를 만든다.
/// 공용 출력(MainProjectBuilds)은 덮어쓰지 않는다. 출력 = C:/UnityProject/_lane-artifacts/MainProject/build48/.
/// 빌드 옵션·씬·xlsx 값은 Build/Windows64 Player(MainFlow) 와 같다(DataTableBuild.WithTableValues + 샘플 복사).
/// exe 실행·프로세스 제어는 에디터 밖(셸)에서 한다 — 이 메뉴는 빌드와 폴더 준비까지만.
/// </summary>
public static class QABuild48
{
    public static string ArtifactsRoot =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), "..", "_lane-artifacts", "MainProject", "build48"));

    public static string BaseExe => Path.Combine(ArtifactsRoot, "base", "MainProject.exe");
    public static string MismatchBaseExe => Path.Combine(ArtifactsRoot, "mismatch-base", "MainProject.exe");

    /// <summary>빌드 씬 복사본에만 프로토콜 차이를 넣는 플래그. 원본 프리팹·씬은 저장하지 않는다.</summary>
    internal static bool BuildProtocolMismatch { get; private set; }

    [MenuItem("Tools/QA/48/Build/Build Demo Player (QA artifacts)", priority = 60)]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[QA][Build] Play 중이다 — 정지 후 빌드할 것.");
            return;
        }

        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(BaseExe));
        var options = new BuildPlayerOptions
        {
            target = BuildTarget.StandaloneWindows64,
            scenes = scenes,
            locationPathName = BaseExe,
            options = BuildOptions.None,
        };

        BuildReport report = null;
        BuildReport mismatchReport = null;
        DataTableBuild.WithTableValues(() =>
        {
            report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(MismatchBaseExe));
            options.locationPathName = MismatchBaseExe;
            try
            {
                BuildProtocolMismatch = true;
                mismatchReport = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                BuildProtocolMismatch = false;
            }
        });
        BuildSummary summary = report.summary;
        string line = $"[QA][Build] result={summary.result} errors={summary.totalErrors} time={summary.totalTime} exe={BaseExe}";
        File.WriteAllText(Path.Combine(ArtifactsRoot, "build-result.txt"), line + Environment.NewLine);
        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError(line);
            return;
        }
        Debug.Log(line);

        BuildSummary mismatchSummary = mismatchReport.summary;
        string mismatchLine = $"[QA][Build] mismatch result={mismatchSummary.result} errors={mismatchSummary.totalErrors} " +
                              $"time={mismatchSummary.totalTime} exe={MismatchBaseExe}";
        File.AppendAllText(Path.Combine(ArtifactsRoot, "build-result.txt"), mismatchLine + Environment.NewLine);
        if (mismatchSummary.result != BuildResult.Succeeded)
        {
            Debug.LogError(mismatchLine);
            return;
        }
        Debug.Log(mismatchLine);

        var errors = new System.Collections.Generic.List<string>();
        int copied = DemoConnectionSamples.CopyToBuild(DemoConnectionSamples.ResolveSourceDirectory(Path.GetDirectoryName(Application.dataPath)), BaseExe, errors);
        File.AppendAllText(Path.Combine(ArtifactsRoot, "build-result.txt"), $"samples copied={copied} errors={string.Join(" / ", errors)}{Environment.NewLine}");
        PrepareInstances();
    }

    /// <summary>
    /// base 빌드를 인스턴스 폴더로 복사한다. host = 설정 파일 없음(B1 '파일 없으면 호스트'), client = client 샘플을 이름 바꿔 127.0.0.1 로.
    /// (lobby-demo-flow.md §2 — PC 마다 샘플 하나를 demo-connection.json 으로.)
    /// </summary>
    [MenuItem("Tools/QA/48/Build/Prepare Host·Client Folders", priority = 61)]
    public static void PrepareInstances()
    {
        string baseDir = Path.GetDirectoryName(BaseExe);
        if (!File.Exists(BaseExe))
        {
            Debug.LogError($"[QA][Build] base 빌드 없음: {BaseExe}");
            return;
        }

        foreach (string name in new[] { "host", "client", "client2", "client3" })
        {
            string dir = Path.Combine(ArtifactsRoot, name);
            CopyDirectory(baseDir, dir);
            string cfg = Path.Combine(dir, DemoConnectionConfig.FileName);
            if (File.Exists(cfg))
                File.Delete(cfg);
            if (name == "host")
                continue;
            string sample = File.ReadAllText(Path.Combine(dir, DemoConnectionSamples.ClientSampleFileName));
            sample = System.Text.RegularExpressions.Regex.Replace(sample, "\"hostAddress\"\\s*:\\s*\"[^\"]*\"", "\"hostAddress\": \"127.0.0.1\"");
            sample = System.Text.RegularExpressions.Regex.Replace(sample, "\"playerId\"\\s*:\\s*\"[^\"]*\"", $"\"playerId\": \"qa-{name}\"");
            File.WriteAllText(cfg, sample);
        }

        string mismatchDir = Path.Combine(ArtifactsRoot, "mismatch");
        if (File.Exists(MismatchBaseExe))
        {
            CopyDirectory(Path.GetDirectoryName(MismatchBaseExe), mismatchDir);
            string cfg = Path.Combine(mismatchDir, DemoConnectionConfig.FileName);
            if (File.Exists(cfg))
                File.Delete(cfg);
        }
        Debug.Log($"[QA][Build] 인스턴스 폴더 준비: {ArtifactsRoot}/host(설정 없음) · client · client2 · client3 · mismatch");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string rel = file.Substring(source.Length).TrimStart('\\', '/');
            string target = Path.Combine(destination, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(file, target, true);
        }
    }
}

/// <summary>
/// B2 버전 불일치 전용 빌드 가공. BuildPipeline 이 넘긴 씬 복사본의 NGO ProtocolVersion 만 바꾸므로
/// NetworkManager.prefab 과 0.BootStrapScene 에는 diff·재직렬화가 남지 않는다.
/// </summary>
public sealed class QABuild48ProtocolMismatchProcessor : IProcessSceneWithReport
{
    public int callbackOrder => int.MaxValue;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (!QABuild48.BuildProtocolMismatch || scene.name != DemoSessionRules.BootstrapSceneName)
            return;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (NetworkManager manager in root.GetComponentsInChildren<NetworkManager>(true))
            {
                manager.NetworkConfig.ProtocolVersion++;
                Debug.Log($"[QA][Build] mismatch 씬 복사본 ProtocolVersion={manager.NetworkConfig.ProtocolVersion}", manager);
            }
        }
    }
}
#endif
