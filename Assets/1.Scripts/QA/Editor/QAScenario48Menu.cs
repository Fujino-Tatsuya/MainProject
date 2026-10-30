#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 시나리오 실행 공용 경로(이름은 할 일 48 때 그대로) — 야간 연속 루프(<see cref="QABoss103BatchRunner"/>)가 판마다 <see cref="Run"/> 으로 시작하고 끝나면 <see cref="Clear"/> 로 정리한다.
/// 시나리오 id 를 EditorPrefs(QASettings.Scenarios)에 넣고 Play Active Scenario 와 같은 경로로 시작한다.
/// 🔴 isPlaying 을 직접 토글하지 않는다(MPPM 가상 플레이어가 안 붙는다) — QAScenarioPlay.Play 를 쓴다.
/// 48 S·M 묶음·하나씩 실행·57.2 프로파일 실행 메뉴는 삭제됨(155). 남은 사람용 버튼 = QA 창 ▸ QA ▸ 모드 해제(<c>QAAutomation.ClearScenarioMode()</c>)·설정 ▸ 리포트 위치 ▸ 열기.
/// </summary>
public static class QAScenario48Menu
{
    /// <summary>시나리오 모드 해제 — 평소 Play 에 QA 하네스가 시나리오를 다시 돌리지 않게. QA 접속 설정 파일·클론 원격 지연 파일도 지운다.</summary>
    public static void Clear()
    {
        QASettings.Scenarios = string.Empty;
        DeleteQAConnectionConfigs();
        int n = QANetSim88.ResetRemoteDelayFiles();
        Debug.Log($"[QA] 시나리오 모드 해제(원격 지연 파일 {n}개 삭제) — 이후 Play 는 기존 QA 세션(QA 창 설정 기준)으로 돈다.");
    }

    // ── QA 실행 포트 돌리기 ───────────────────────────────────────
    // 48.3 2차: Play 정지 때 NGO 종료 NRE(NetworkSceneManager.Dispose) 뒤 에디터가 호스트 UDP 포트를 계속 쥐어
    // 다음 Play 호스트가 'Server failed to bind' 로 무한 재시도했다(7777·7778 두 번). 에디터 재시작 없이 돌리려고
    // 실행마다 새 포트로 demo-connection.json 을 루트·MPPM 클론 루트에 쓴다. 우리가 쓴 파일만 지운다(playerId 'qa' 표식).
    // 루트가 사람 파일(964fc4ef 부터 git 추적)이면 루트는 두고 클론만 그 포트로 쓴다 — HumanRootPort.

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

    /// <summary>
    /// 루트 설정이 사람 파일(git 추적 부스 기본값 등)이면 호스트는 그 포트로 듣고 QA 가 바꿀 수 없다 — 클론도 그 포트로 붙인다.
    /// 할 일 88 스모크 2차: 호스트 7777(루트 추적 파일)·클론 7799(QA 회전 포트)로 갈려 클론이 접속 타임아웃만 반복했다.
    /// 값은 게임의 <see cref="DemoConnectionConfig.TryParse"/> 그대로라 비었거나 깨진 파일이면 호스트와 같은 기본 포트가 나온다.
    /// </summary>
    internal static ushort HumanRootPort(string rootJson)
    {
        DemoConnectionConfig.TryParse(rootJson, "qa", System.Array.Empty<string>(), out DemoConnectionLoadResult parsed);
        return parsed.Config.Port;
    }

    private static void WriteQAConnectionConfigs()
    {
        string rootPath = System.IO.Path.Combine(ProjectRoot, DemoConnectionConfig.FileName);
        bool humanRoot = !IsOurs(rootPath);
        int port = humanRoot ? HumanRootPort(System.IO.File.ReadAllText(rootPath)) : NextPort();
        if (humanRoot)
            Debug.LogWarning($"[QA] 루트 설정이 사람 파일이라 포트 회전 없이 그 포트 {port} 로 클론을 붙인다 — 호스트 포트가 묶이면 에디터 재시작 필요.");
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

    private static void DeleteQAConnectionConfigs()
    {
        foreach (string path in ConfigPaths())
            if (System.IO.File.Exists(path) && IsOurs(path))
            {
                System.IO.File.Delete(path);
                Debug.Log($"[QA] {path} 삭제");
            }
    }

    public static void OpenReports()
    {
        string root = QAReportWriter.ScenarioRoot();
        System.IO.Directory.CreateDirectory(root);
        EditorUtility.RevealInFinder(root);
    }

    /// <summary>시나리오 실행 공통 경로(포트 돌리기 → 시나리오 모드 → Play Active Scenario). 야간 연속 루프가 판마다 부른다. 이미 Play 중이라 안 했으면 false.</summary>
    internal static bool Run(string ids, int players)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[QA] 이미 Play 중이다 — QA 창(Tools/QA) ▸ QA ▸ 정지 로 먼저 정지할 것.");
            return false;
        }

        WriteQAConnectionConfigs();
        QASettings.AutoRun = true;
        QASettings.Scenarios = ids;
        QASettings.PlayerCount = players;
        Debug.Log($"[QA] 시나리오 {ids} ({players}인) 실행 — 리포트 루트 {QAReportWriter.ScenarioRoot()}");
        QAScenarioPlay.Play();
        return true;
    }
}
#endif
