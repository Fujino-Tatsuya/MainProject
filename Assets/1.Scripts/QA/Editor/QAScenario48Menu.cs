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
        Debug.Log("[QA] 48 시나리오 모드 해제 — 이후 Play 는 기존 QA 세션(설정 창 기준)으로 돈다.");
    }

    [MenuItem(Root + "Open Report Folder", priority = 41)]
    public static void OpenReports()
    {
        string root = QAReportWriter.ScenarioRoot();
        System.IO.Directory.CreateDirectory(root);
        EditorUtility.RevealInFinder(root);
    }

    private static void Run(string ids, int players)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[QA] 이미 Play 중이다 — Tools/QA/Stop Active Scenario 로 먼저 정지할 것.");
            return;
        }

        QASettings.AutoRun = true;
        QASettings.Scenarios = ids;
        QASettings.PlayerCount = players;
        Debug.Log($"[QA] 48 시나리오 {ids} ({players}인) 실행 — 리포트 루트 {QAReportWriter.ScenarioRoot()}");
        QAScenarioPlay.Play();
    }
}
#endif
