#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

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
        DataTableBuild.WithTableValues(() => report = BuildPipeline.BuildPlayer(options));
        BuildSummary summary = report.summary;
        string line = $"[QA][Build] result={summary.result} errors={summary.totalErrors} time={summary.totalTime} exe={BaseExe}";
        File.WriteAllText(Path.Combine(ArtifactsRoot, "build-result.txt"), line + Environment.NewLine);
        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError(line);
            return;
        }
        Debug.Log(line);

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

        foreach (string name in new[] { "host", "client", "client2" })
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
        Debug.Log($"[QA][Build] 인스턴스 폴더 준비: {ArtifactsRoot}/host(설정 없음) · client · client2(127.0.0.1)");
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
#endif
