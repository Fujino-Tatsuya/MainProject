#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 할 일 48 B2 standalone 자동 판정. 이 메뉴가 띄운 프로세스만 추적·종료하고 시스템/방화벽 설정은 건드리지 않는다.
/// 결과는 C:/UnityProject/QA/48-&lt;시각&gt;/report.md 와 하위 로그·스크린샷에 남긴다.
/// </summary>
public static class QABuild48StandaloneRunner
{
    const string MenuRoot = "Tools/QA/48/Build/";
    const string ConfigMarker = "qa48-standalone";
    static readonly List<LaunchedPlayer> Started = new List<LaunchedPlayer>();
    static bool _running;
    static int _nextPort = 18048;

    [MenuItem(MenuRoot + "Run B2 Rejection Suite", priority = 70)]
    public static async void RunB2Menu()
    {
        if (_running)
        {
            Debug.LogWarning("[QA][B2] 이미 standalone 스위트가 실행 중이다.");
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[QA][B2] Play 중에는 실행하지 않는다.");
            return;
        }
        if (!RequiredBuildsExist(out string missing))
        {
            Debug.LogError($"[QA][B2] 빌드가 없다: {missing} — Build Demo Player (QA artifacts) 먼저 실행");
            return;
        }

        _running = true;
        string reportRoot = Path.Combine("C:/UnityProject/QA", $"48-{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(reportRoot);
        var results = new List<CaseResult>();
        try
        {
            results.Add(await RunWrongAddress(reportRoot));
            results.Add(await RunLobbyFull(reportRoot));
            results.Add(await RunDuplicatePlayerId(reportRoot));
            results.Add(await RunHashMismatch(reportRoot));
        }
        catch (Exception e)
        {
            results.Add(new CaseResult("B2-runner", false, $"러너 예외: {e}", string.Empty));
            Debug.LogException(e);
        }
        finally
        {
            await StopAll();
            DeleteTemporaryConfigs();
            WriteReport(reportRoot, results, "B2");
            _running = false;
            AssetDatabase.Refresh();
            Debug.Log($"[QA][B2] 완료: {reportRoot}/report.md — PASS {results.Count(x => x.Pass)}/{results.Count}");
        }
    }

    static async Task<CaseResult> RunWrongAddress(string root)
    {
        string dir = CaseDirectory(root, "B2-1-wrong-address");
        int port = NextAvailablePort();
        WriteClientConfig("client", "192.0.2.1", port, "wrong-address", 1f, 3f);
        LaunchedPlayer client = Launch("client", dir, "client");
        bool timeout = await WaitLog(client, text => Count(text, "StartClient before") >= 2 && text.Contains("reason=Timeout"), 25f);
        string evidence = $"잘못된 IP 192.0.2.1:{port}, StartClient {Count(ReadAll(client), "StartClient before")}회, Timeout={ReadAll(client).Contains("reason=Timeout")}";
        await FinishCase(dir);
        return new CaseResult("B2-1 잘못된 IP 재시도", timeout, evidence, Relative(root, dir));
    }

    static async Task<CaseResult> RunLobbyFull(string root)
    {
        string dir = CaseDirectory(root, "B2-2-lobby-full");
        int port = NextAvailablePort();
        WriteHostConfig(port, "full-host");
        WriteClientConfig("client", "127.0.0.1", port, "full-1");
        WriteClientConfig("client2", "127.0.0.1", port, "full-2");
        WriteClientConfig("client3", "127.0.0.1", port, "full-3");
        LaunchedPlayer host = Launch("host", dir, "host");
        bool hostReady = await WaitLog(host, t => t.Contains("LobbySceneManager.Awake"), 60f);
        LaunchedPlayer c1 = Launch("client", dir, "client1");
        bool c1Ready = await WaitLog(c1, t => t.Contains("LobbySceneManager.Awake"), 60f);
        LaunchedPlayer c2 = Launch("client2", dir, "client2");
        bool c2Ready = await WaitLog(c2, t => t.Contains("LobbySceneManager.Awake"), 60f);
        LaunchedPlayer c3 = Launch("client3", dir, "client3");
        bool rejected = await WaitLog(c3, t => t.Contains("reason=LobbyFull"), 30f);
        string evidence = $"host/c1/c2 lobby={hostReady}/{c1Ready}/{c2Ready}, 4번째 reason=LobbyFull={rejected}, " +
                          $"호스트 승인 로그 LobbyFull={ReadAll(host).Contains("reason=LobbyFull")}";
        await FinishCase(dir);
        return new CaseResult("B2-2 4번째 인원 방 가득", hostReady && c1Ready && c2Ready && rejected, evidence, Relative(root, dir));
    }

    static async Task<CaseResult> RunDuplicatePlayerId(string root)
    {
        string dir = CaseDirectory(root, "B2-3-duplicate-player-id");
        int port = NextAvailablePort();
        WriteHostConfig(port, "duplicate-host");
        WriteClientConfig("client", "127.0.0.1", port, "same-id");
        WriteClientConfig("client2", "127.0.0.1", port, "same-id");
        LaunchedPlayer host = Launch("host", dir, "host");
        bool hostReady = await WaitLog(host, t => t.Contains("LobbySceneManager.Awake"), 60f);
        LaunchedPlayer c1 = Launch("client", dir, "client1");
        bool c1Ready = await WaitLog(c1, t => t.Contains("LobbySceneManager.Awake"), 60f);
        LaunchedPlayer c2 = Launch("client2", dir, "client2");
        bool rejected = await WaitLog(c2, t => t.Contains("reason=DuplicatePlayerId"), 30f);
        string evidence = $"host/c1 lobby={hostReady}/{c1Ready}, same playerId='qa48-standalone-same-id', DuplicatePlayerId={rejected}";
        await FinishCase(dir);
        return new CaseResult("B2-3 같은 playerId 거절", hostReady && c1Ready && rejected, evidence, Relative(root, dir));
    }

    static async Task<CaseResult> RunHashMismatch(string root)
    {
        string dir = CaseDirectory(root, "B2-4-version-mismatch");
        int port = NextAvailablePort();
        WriteHostConfig(port, "hash-host");
        WriteClientConfig("mismatch", "127.0.0.1", port, "hash-client");
        LaunchedPlayer host = Launch("host", dir, "host");
        bool hostReady = await WaitLog(host, t => t.Contains("LobbySceneManager.Awake"), 60f);
        LaunchedPlayer client = Launch("mismatch", dir, "mismatch-client");
        bool rejected = await WaitLog(client, t => t.Contains("reason=HashMismatch"), 30f);
        string all = ReadAll(client);
        string evidence = $"normal host + ProtocolVersion 차이 client, HashMismatch={rejected}, ClosedByRemote={all.Contains("ClosedByRemote")}";
        await FinishCase(dir);
        return new CaseResult("B2-4 버전 불일치", hostReady && rejected, evidence, Relative(root, dir));
    }

    static bool RequiredBuildsExist(out string missing)
    {
        foreach (string name in new[] { "host", "client", "client2", "client3", "mismatch" })
        {
            string exe = Exe(name);
            if (!File.Exists(exe)) { missing = exe; return false; }
        }
        missing = string.Empty;
        return true;
    }

    static string CaseDirectory(string root, string name)
    {
        string dir = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(dir, "shots"));
        return dir;
    }

    static LaunchedPlayer Launch(string instance, string caseDir, string label)
    {
        string exe = Exe(instance);
        string playerLog = Path.Combine(caseDir, label + ".log");
        string screenshot = Path.Combine(caseDir, "shots", label + "-wait.png");
        string networkLog = Path.Combine(Path.GetDirectoryName(exe), "network.log");
        if (File.Exists(playerLog)) File.Delete(playerLog);
        if (File.Exists(networkLog)) File.Delete(networkLog);
        var start = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe),
            UseShellExecute = false,
            Arguments = $"-logFile \"{playerLog}\" -screen-fullscreen 0 -screen-width 640 -screen-height 360 " +
                        $"-qa48-initial-screenshot \"{screenshot}\"",
        };
        var launched = new LaunchedPlayer(label, Process.Start(start), playerLog, networkLog);
        Started.Add(launched);
        Debug.Log($"[QA][Standalone] 시작 {label} pid={launched.Process.Id} exe={exe}");
        return launched;
    }

    static async Task<bool> WaitLog(LaunchedPlayer player, Func<string, bool> predicate, float timeoutSeconds)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < until)
        {
            if (predicate(ReadAll(player))) return true;
            if (player.Process.HasExited) break;
            await Task.Delay(500);
        }
        return predicate(ReadAll(player));
    }

    static string ReadAll(LaunchedPlayer player)
    {
        var sb = new StringBuilder();
        AppendReadable(sb, player.PlayerLog);
        AppendReadable(sb, player.NetworkLog);
        return sb.ToString();
    }

    static void AppendReadable(StringBuilder sb, string path)
    {
        try { if (File.Exists(path)) sb.AppendLine(File.ReadAllText(path)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static async Task FinishCase(string caseDir)
    {
        LaunchedPlayer[] players = Started.ToArray();
        await StopAll();
        foreach (LaunchedPlayer p in players)
        {
            try
            {
                if (File.Exists(p.NetworkLog))
                    File.Copy(p.NetworkLog, Path.Combine(caseDir, p.Label + "-network.log"), true);
            }
            catch (Exception e) { Debug.LogWarning($"[QA][Standalone] network.log 보관 실패 {p.Label}: {e.Message}"); }
        }
        DeleteTemporaryConfigs();
        await Task.Delay(1500);
    }

    static async Task StopAll()
    {
        foreach (LaunchedPlayer p in Started.ToArray())
        {
            try
            {
                if (p.Process == null || p.Process.HasExited) continue;
                p.Process.CloseMainWindow();
                await Task.Delay(300);
                if (!p.Process.HasExited)
                    p.Process.Kill();
                p.Process.WaitForExit(3000);
                Debug.Log($"[QA][Standalone] 종료 {p.Label} pid={p.Process.Id}");
            }
            catch (Exception e) { Debug.LogWarning($"[QA][Standalone] 종료 실패 {p.Label}: {e.Message}"); }
        }
        Started.Clear();
    }

    static void WriteHostConfig(int port, string suffix) => WriteConfig("host", "host", "127.0.0.1", port, suffix, 1f, 5f);
    static void WriteClientConfig(string instance, string address, int port, string suffix, float retry = 1f, float connect = 5f) =>
        WriteConfig(instance, "client", address, port, suffix, retry, connect);

    static void WriteConfig(string instance, string role, string address, int port, string suffix, float retry, float connect)
    {
        string path = Path.Combine(QABuild48.ArtifactsRoot, instance, DemoConnectionConfig.FileName);
        string json = "{\n" +
                      $"  \"role\": \"{role}\",\n" +
                      $"  \"hostAddress\": \"{address}\",\n" +
                      $"  \"port\": {port},\n" +
                      $"  \"playerId\": \"{ConfigMarker}-{suffix}\",\n" +
                      $"  \"retryIntervalSeconds\": {retry.ToString(System.Globalization.CultureInfo.InvariantCulture)},\n" +
                      $"  \"connectTimeoutSeconds\": {connect.ToString(System.Globalization.CultureInfo.InvariantCulture)},\n" +
                      "  \"disconnectTimeoutSeconds\": 3\n}\n";
        File.WriteAllText(path, json);
    }

    static void DeleteTemporaryConfigs()
    {
        foreach (string name in new[] { "host", "client", "client2", "client3", "mismatch" })
        {
            string path = Path.Combine(QABuild48.ArtifactsRoot, name, DemoConnectionConfig.FileName);
            try
            {
                if (File.Exists(path) && File.ReadAllText(path).Contains(ConfigMarker)) File.Delete(path);
            }
            catch (Exception e) { Debug.LogWarning($"[QA][Standalone] 임시 설정 삭제 실패 {path}: {e.Message}"); }
        }
    }

    static int NextAvailablePort()
    {
        for (int i = 0; i < 2000; i++)
        {
            int port = _nextPort++;
            try { using (var probe = new UdpClient(port)) return port; }
            catch (SocketException) { }
        }
        throw new InvalidOperationException("QA standalone 용 빈 UDP 포트를 찾지 못했다.");
    }

    static string Exe(string instance) => Path.Combine(QABuild48.ArtifactsRoot, instance, "MainProject.exe");
    static int Count(string text, string needle)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0) { count++; index += needle.Length; }
        return count;
    }
    static string Relative(string root, string path) => path.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');

    static void WriteReport(string root, IReadOnlyList<CaseResult> results, string suite)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# 할 일 48.3 standalone QA — {suite}");
        sb.AppendLine();
        sb.AppendLine($"- 실행: {DateTime.Now:yyyy-MM-dd HH:mm:ss} KST · 브랜치 qa/LobbyDemo");
        sb.AppendLine("- 방식: QA 러너가 시작한 build48 exe만 종료. 방화벽·네트워크 어댑터·Unity 에디터는 건드리지 않음.");
        sb.AppendLine();
        sb.AppendLine("| 항목 | 결과 | 근거 | 로그·스크린샷 |");
        sb.AppendLine("|---|---|---|---|");
        foreach (CaseResult r in results)
            sb.AppendLine($"| {r.Name} | **{(r.Pass ? "PASS" : "FAIL")}** | {r.Evidence.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ")} | `{r.Artifact}` |");
        sb.AppendLine();
        sb.AppendLine($"집계: PASS {results.Count(x => x.Pass)} / {results.Count}");
        File.WriteAllText(Path.Combine(root, "report.md"), sb.ToString(), new UTF8Encoding(false));
    }

    sealed class LaunchedPlayer
    {
        public LaunchedPlayer(string label, Process process, string playerLog, string networkLog)
        { Label = label; Process = process; PlayerLog = playerLog; NetworkLog = networkLog; }
        public string Label { get; }
        public Process Process { get; }
        public string PlayerLog { get; }
        public string NetworkLog { get; }
    }

    readonly struct CaseResult
    {
        public CaseResult(string name, bool pass, string evidence, string artifact)
        { Name = name; Pass = pass; Evidence = evidence; Artifact = artifact; }
        public string Name { get; }
        public bool Pass { get; }
        public string Evidence { get; }
        public string Artifact { get; }
    }
}
#endif
