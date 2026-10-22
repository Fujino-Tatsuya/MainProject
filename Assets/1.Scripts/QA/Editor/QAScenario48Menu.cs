#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 48 로비 데모 QA 메뉴 — 시나리오 id 를 EditorPrefs(QASettings.Scenarios)에 넣고 Play Active Scenario 와 같은 경로로 시작한다.
/// 🔴 isPlaying 을 직접 토글하지 않는다(MPPM 가상 플레이어가 안 붙는다) — QAScenarioPlay.Play 를 쓴다.
/// MPPM 묶음은 먼저 Tools/QA/Activate Virtual Players (2,3) 로 가상 플레이어(태그 Client)를 켠 뒤 실행한다.
/// 끝나면 Tools/QA/48/Clear Scenario Mode 로 시나리오 모드를 풀어야 평소 Play 에 QA 하네스가 시나리오를 다시 돌리지 않는다.
/// </summary>
public static class QAScenario48Menu
{
    private const string Root = "Tools/QA/48/";

    [MenuItem(Root + "Run Solo Batch (S1·S3·S5)", priority = 0)]
    public static void RunSolo() => Run("S1,S3,S5", 1);

    [MenuItem(Root + "Run MPPM Batch (M1·M4)", priority = 1)]
    public static void RunMppm() => Run("M1,M4", 3);

    [MenuItem(Root + "Run Solo Batch 2 (S2·S4)", priority = 2)]
    public static void RunSolo2() => Run("S2,S4", 1);

    [MenuItem(Root + "Run MPPM Batch 2 (M2·M3·M8)", priority = 3)]
    public static void RunMppm2() => Run("M2,M3,M8", 3);

    [MenuItem(Root + "Run MPPM Batch 2b (M3·M8)", priority = 3)]
    public static void RunMppm2b() => Run("M3,M8", 3);

    [MenuItem(Root + "Run MPPM Batch 3 (M5·M6·M7)", priority = 4)]
    public static void RunMppm3() => Run("M5,M6,M7", 3);

    [MenuItem(Root + "Single/S2 Fall → Spawn", priority = 30)]
    public static void RunS2() => Run("S2", 1);

    [MenuItem(Root + "Single/S4 Character Select", priority = 31)]
    public static void RunS4() => Run("S4", 1);

    [MenuItem(Root + "Single/M2 Ready Reset·Cancel·Reject", priority = 32)]
    public static void RunM2() => Run("M2", 3);

    [MenuItem(Root + "Single/M3 Swap Sync·Map Spawn", priority = 33)]
    public static void RunM3() => Run("M3", 3);

    [MenuItem(Root + "Single/M5 Boss Rejoin", priority = 34)]
    public static void RunM5() => Run("M5", 3);

    [MenuItem(Root + "Single/M6 Rejoin Restore", priority = 35)]
    public static void RunM6() => Run("M6", 3);

    [MenuItem(Root + "Single/M7 Absent Wipe Respawn", priority = 36)]
    public static void RunM7() => Run("M7", 3);

    [MenuItem(Root + "Single/M8 Newcomer Reject → Lobby", priority = 37)]
    public static void RunM8() => Run("M8", 3);

    /// <summary>할 일 57.2 보스전 실측 프로파일(솔로). 분석은 Tools/QA/57.2/Analyze Latest Boss Profile.</summary>
    [MenuItem(Root + "Single/P572 Boss Profile (57.2)", priority = 38)]
    public static void RunP572()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[QA] 이미 Play 중이다 — Tools/QA/Stop Active Scenario 로 먼저 정지할 것.");
            return;
        }
        QABossProfileAnalyzer.PrepareProfilerForRun();
        Run("P572", 1);
    }

    [MenuItem(Root + "Single/S1 Boot·Spawn·Camera·Combat", priority = 20)]
    public static void RunS1() => Run("S1", 1);

    [MenuItem(Root + "Single/S3 Ready Pad → Map", priority = 21)]
    public static void RunS3() => Run("S3", 1);

    [MenuItem(Root + "Single/S5 Match End → Lobby x3", priority = 22)]
    public static void RunS5() => Run("S5", 1);

    [MenuItem(Root + "Single/M1 MPPM Auto Join·Slots", priority = 23)]
    public static void RunM1() => Run("M1", 3);

    [MenuItem(Root + "Single/M4 MPPM Mid-match Rejoin", priority = 24)]
    public static void RunM4() => Run("M1,M4", 3); // M4 는 M1 의 집결·준비 흐름 위에서 돈다

    [MenuItem(Root + "Clear Scenario Mode", priority = 40)]
    public static void Clear()
    {
        QASettings.Scenarios = string.Empty;
        DeleteQAConnectionConfigs();
        QAScenario88Menu.ResetRemoteDelay();
        Debug.Log("[QA] 48 시나리오 모드 해제 — 이후 Play 는 기존 QA 세션(설정 창 기준)으로 돈다.");
    }

    // ── QA 실행 포트 돌리기 ───────────────────────────────────────
    // 48.3 2차: Play 정지 때 NGO 종료 NRE(NetworkSceneManager.Dispose) 뒤 에디터가 호스트 UDP 포트를 계속 쥐어
    // 다음 Play 호스트가 'Server failed to bind' 로 무한 재시도했다(7777·7778 두 번). 에디터 재시작 없이 돌리려고
    // 실행마다 새 포트로 demo-connection.json(gitignore 개인 파일)을 루트·MPPM 클론 루트에 쓴다. 우리가 쓴 파일만 지운다(playerId 'qa' 표식).

    private const string PortPrefKey = "QA.Scenario48.Port";
    private const string QAMarker = "\"playerId\": \"qa";

    private static int NextPort()
    {
        int port = EditorPrefs.GetInt(PortPrefKey, 7789) + 1;
        if (port > 7899)
            port = 7790;
        EditorPrefs.SetInt(PortPrefKey, port);
        return port;
    }

    private static string ProjectRoot => System.IO.Path.GetDirectoryName(Application.dataPath);

    private static System.Collections.Generic.IEnumerable<string> ConfigPaths()
    {
        yield return System.IO.Path.Combine(ProjectRoot, DemoConnectionConfig.FileName);
        string vp = System.IO.Path.Combine(ProjectRoot, "Library", "VP");
        if (System.IO.Directory.Exists(vp))
            foreach (string dir in System.IO.Directory.GetDirectories(vp, "mppm*"))
                yield return System.IO.Path.Combine(dir, DemoConnectionConfig.FileName);
    }

    private static bool IsOurs(string path) =>
        !System.IO.File.Exists(path) || System.IO.File.ReadAllText(path).Contains(QAMarker);

    private static void WriteQAConnectionConfigs()
    {
        int port = NextPort();
        foreach (string path in ConfigPaths())
        {
            if (!IsOurs(path))
            {
                Debug.LogWarning($"[QA] {path} 는 사람이 둔 설정이라 건드리지 않는다.");
                continue;
            }
            bool host = System.IO.Path.GetDirectoryName(path) == ProjectRoot;
            string json = host
                ? $"{{\n  \"role\": \"host\",\n  \"port\": {port},\n  {QAMarker}-host\",\n  \"retryIntervalSeconds\": 3,\n  \"connectTimeoutSeconds\": 5,\n  \"disconnectTimeoutSeconds\": 10\n}}\n"
                : $"{{\n  \"role\": \"client\",\n  \"hostAddress\": \"127.0.0.1\",\n  \"port\": {port},\n  {QAMarker}\",\n  \"retryIntervalSeconds\": 3,\n  \"connectTimeoutSeconds\": 5,\n  \"disconnectTimeoutSeconds\": 10\n}}\n";
            System.IO.File.WriteAllText(path, json);
        }
        Debug.Log($"[QA] QA 실행 포트 {port} — demo-connection.json(루트·클론) 기록");
    }

    [MenuItem(Root + "Delete QA Connection Configs", priority = 42)]
    public static void DeleteQAConnectionConfigs()
    {
        foreach (string path in ConfigPaths())
            if (System.IO.File.Exists(path) && IsOurs(path))
            {
                System.IO.File.Delete(path);
                Debug.Log($"[QA] {path} 삭제");
            }
    }

    [MenuItem(Root + "Open Report Folder", priority = 41)]
    public static void OpenReports()
    {
        string root = QAReportWriter.ScenarioRoot();
        System.IO.Directory.CreateDirectory(root);
        EditorUtility.RevealInFinder(root);
    }

    /// <summary>시나리오 실행 공통 경로(포트 돌리기 → 시나리오 모드 → Play Active Scenario). 할 일 88 메뉴도 쓴다. 이미 Play 중이라 안 했으면 false.</summary>
    internal static bool Run(string ids, int players)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[QA] 이미 Play 중이다 — Tools/QA/Stop Active Scenario 로 먼저 정지할 것.");
            return false;
        }

        WriteQAConnectionConfigs();
        QASettings.AutoRun = true;
        QASettings.Scenarios = ids;
        QASettings.PlayerCount = players;
        Debug.Log($"[QA] 48 시나리오 {ids} ({players}인) 실행 — 리포트 루트 {QAReportWriter.ScenarioRoot()}");
        QAScenarioPlay.Play();
        return true;
    }
}
#endif
