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
/// exe 실행·프로세스 제어는 에디터 밖(셸)에서 한다 — 여기(QA 창(Tools/QA) ▸ 빌드)는 빌드와 폴더 준비까지만.
/// </summary>
public static class QABuild48
{
    public static string ArtifactsRoot =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), "..", "_lane-artifacts", "MainProject", "build48"));

    public static string BaseExe => Path.Combine(ArtifactsRoot, "base", "MainProject.exe");
    public static string MismatchBaseExe => Path.Combine(ArtifactsRoot, "mismatch-base", "MainProject.exe");
    const string NetworkManagerPrefab = "Assets/2.Prefabs/Network/NetworkManager.prefab";

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
        BuildSummary summary = default;
        BuildSummary mismatchSummary = default;
        bool baseBuilt = false;
        bool mismatchBuilt = false;
        DataTableBuild.WithTableValues(() =>
        {
            report = BuildPipeline.BuildPlayer(options);
            summary = report.summary;
            baseBuilt = true;
            if (summary.result != BuildResult.Succeeded)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(MismatchBaseExe));
            options.locationPathName = MismatchBaseExe;
            mismatchReport = BuildProtocolMismatchPlayer(options);
            mismatchSummary = mismatchReport.summary;
            mismatchBuilt = true;
        });
        if (!baseBuilt)
        {
            Debug.LogError("[QA][Build] base BuildReport 를 받지 못했다.");
            return;
        }
        string line = $"[QA][Build] result={summary.result} errors={summary.totalErrors} time={summary.totalTime} exe={BaseExe}";
        File.WriteAllText(Path.Combine(ArtifactsRoot, "build-result.txt"), line + Environment.NewLine);
        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError(line);
            return;
        }
        Debug.Log(line);

        if (!mismatchBuilt)
        {
            Debug.LogError("[QA][Build] mismatch BuildReport 를 받지 못했다.");
            return;
        }
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
            // 할 일 72 뒤 샘플은 공용 auto 하나(playerId 없음)다 — 127.0.0.1 은 auto 판정이 애매하니 클라는 role 을 못박고 playerId 를 넣는다.
            string sample = File.ReadAllText(Path.Combine(dir, DemoConnectionSamples.AutoSampleFileName));
            sample = System.Text.RegularExpressions.Regex.Replace(sample, "\"role\"\\s*:\\s*\"[^\"]*\"", "\"role\": \"client\"");
            sample = System.Text.RegularExpressions.Regex.Replace(sample, "\"hostAddress\"\\s*:\\s*\"[^\"]*\"", "\"hostAddress\": \"127.0.0.1\"");
            sample = System.Text.RegularExpressions.Regex.Replace(sample, "\\{", $"{{\n  \"playerId\": \"qa-{name}\",", System.Text.RegularExpressions.RegexOptions.None, System.TimeSpan.FromSeconds(1));
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

    /// <summary>
    /// NetworkManager.prefab 의 ProtocolVersion 한 바이트만 임시로 바꿔 불일치 빌드를 만들고 원본 바이트를 즉시 복원한다.
    /// YAML 재직렬화 없이 동일 길이 바이트를 교체하므로 finally 뒤 추적 에셋 diff가 남지 않는다.
    /// </summary>
    static BuildReport BuildProtocolMismatchPlayer(BuildPlayerOptions options)
    {
        string fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), NetworkManagerPrefab));
        byte[] original = File.ReadAllBytes(fullPath);
        byte[] changed = (byte[])original.Clone();
        byte[] needle = System.Text.Encoding.ASCII.GetBytes("ProtocolVersion: 0");
        int found = FindBytes(changed, needle);
        if (found < 0)
            throw new InvalidOperationException($"[QA][Build] {NetworkManagerPrefab} 에 ProtocolVersion: 0 이 없다.");
        changed[found + needle.Length - 1] = (byte)'1';

        try
        {
            File.WriteAllBytes(fullPath, changed);
            AssetDatabase.ImportAsset(NetworkManagerPrefab, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Debug.Log("[QA][Build] mismatch 빌드용 NetworkManager ProtocolVersion=1 임시 적용");
            return BuildPipeline.BuildPlayer(options);
        }
        finally
        {
            File.WriteAllBytes(fullPath, original);
            AssetDatabase.ImportAsset(NetworkManagerPrefab, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Debug.Log("[QA][Build] NetworkManager.prefab 원본 바이트 복원");
        }
    }

    static int FindBytes(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            int j = 0;
            for (; j < needle.Length && haystack[i + j] == needle[j]; j++) { }
            if (j == needle.Length) return i;
        }
        return -1;
    }
}
#endif
