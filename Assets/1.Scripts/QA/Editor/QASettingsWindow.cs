#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// QA 창 — QA 메뉴는 Tools ▸ QA 하나(이 창 열기)뿐이고 실행·설정은 모두 여기 섹션 버튼으로 한다(할 일 155).
/// 맨 위 '공통 설정' = Play 시간·반복 횟수·자동실행 토글·리포트 저장 경로(EditorPrefs, 재컴파일 없이 즉시 반영).
/// 아래 Foldout 섹션 = 공통 / 48 / 48 Build / 88 / 103 / 103 Batch / 57.2 / Nightly. 에이전트는 창 대신 <see cref="QAAutomation"/> 을 부른다.
/// 버튼 동작은 <see cref="EditorApplication.delayCall"/> 로 OnGUI 밖에서 실행한다(폴더 선택 창·Play 전환이 레이아웃을 깨지 않게).
/// </summary>
public sealed class QASettingsWindow : EditorWindow
{
    private const string FoldPrefix = "QA.Window.Fold.";

    private static readonly int[] DelayOptions = QAInterruptLag88Plan.DelayStepsMs;
    private static readonly int[] AttemptsOptions = { 30, 10, 3 };
    private static readonly string[] AttemptsLabels = { "30 (default)", "10", "3 (smoke)" };
    private static readonly int[] TimeoutOptions = { 600, 1200 };
    private static readonly string[] TimeoutLabels = { "600s (default)", "1200s" };
    private static readonly int[] TierOptions = { -1, (int)QABoss103BotTier.Beginner, (int)QABoss103BotTier.Normal, (int)QABoss103BotTier.Skilled };
    private static readonly string[] TierLabels = { "Auto (run mod 3)", "Beginner", "Normal", "Skilled" };

    private Vector2 _scroll;

    [MenuItem("Tools/QA")]
    public static void Open()
    {
        var window = GetWindow<QASettingsWindow>("QA");
        window.minSize = new Vector2(440, 320);
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.Space(6);

        if (Section("Settings", "공통 설정", true))
            DrawSettings();
        if (Section("Common", "공통 — 가상 플레이어·Play"))
            DrawCommon();
        if (Section("48", "48 — 로비 데모 시나리오"))
            Draw48();
        if (Section("48Build", "48 Build — standalone"))
            Draw48Build();
        if (Section("88", "88 — 간파 지연"))
            Draw88();
        if (Section("103", "103 — 보스전 1판"))
            Draw103();
        if (Section("103Batch", "103 Batch — 연속 루프"))
            Draw103Batch();
        if (Section("57.2", "57.2 — 보스 프로파일 분석"))
            Draw572();
        if (Section("Nightly", "Nightly — 야간 QA(108)"))
            DrawNightly();

        EditorGUILayout.Space(6);
        EditorGUILayout.EndScrollView();
    }

    // ── 섹션 ─────────────────────────────────────────────────

    private static void DrawCommon()
    {
        Row(("Activate Virtual Players (2,3)", QAAutomation.ActivateVirtualPlayers, true),
            ("Deactivate Virtual Players (2,3)", QAAutomation.DeactivateVirtualPlayers, true));
        Row(("Ensure MPPM Client Tags (2,3)", QAAutomation.EnsureMppmClientTags, true));
        Row(("Play Active Scenario", QAAutomation.PlayActiveScenario, true),
            ("Stop Active Scenario", QAAutomation.StopActiveScenario, true));
        SubHeader("Debug");
        Row(("MPPM Status", QAAutomation.MppmStatus, true),
            ("Virtual Player States", QAAutomation.VirtualPlayerStates, true),
            ("Dump MPPM API", QAScenarioPlay.Dump, true));
    }

    private static void Draw48()
    {
        SubHeader("배치");
        Row(("Solo Batch (S1·S3·S5)", QAAutomation.Scenario48RunSoloBatch, true),
            ("MPPM Batch (M1·M4)", QAAutomation.Scenario48RunMppmBatch, true));
        Row(("Solo Batch 2 (S2·S4)", QAScenario48Menu.RunSolo2, true),
            ("MPPM Batch 2 (M2·M3·M8)", QAScenario48Menu.RunMppm2, true));
        Row(("MPPM Batch 2b (M3·M8)", QAScenario48Menu.RunMppm2b, true),
            ("MPPM Batch 3 (M5·M6·M7)", QAScenario48Menu.RunMppm3, true));
        SubHeader("Single");
        Row(("S1 Boot·Spawn·Camera·Combat", QAScenario48Menu.RunS1, true),
            ("S2 Fall → Spawn", QAScenario48Menu.RunS2, true));
        Row(("S3 Ready Pad → Map", QAScenario48Menu.RunS3, true),
            ("S4 Character Select", QAScenario48Menu.RunS4, true));
        Row(("S5 Match End → Lobby x3", QAScenario48Menu.RunS5, true),
            ("M1 MPPM Auto Join·Slots", QAScenario48Menu.RunM1, true));
        Row(("M2 Ready Reset·Cancel·Reject", QAScenario48Menu.RunM2, true),
            ("M3 Swap Sync·Map Spawn", QAScenario48Menu.RunM3, true));
        Row(("M4 MPPM Mid-match Rejoin", QAScenario48Menu.RunM4, true),
            ("M5 Boss Rejoin", QAScenario48Menu.RunM5, true));
        Row(("M6 Rejoin Restore", QAScenario48Menu.RunM6, true),
            ("M7 Absent Wipe Respawn", QAScenario48Menu.RunM7, true));
        Row(("M8 Newcomer Reject → Lobby", QAScenario48Menu.RunM8, true),
            ("P572 Boss Profile (57.2)", QAScenario48Menu.RunP572, true));
        SubHeader("정리");
        Row(("Clear Scenario Mode", QAAutomation.ClearScenarioMode, true),
            ("Open Report Folder", QAAutomation.Scenario48OpenReportFolder, true));
        Row(("Delete QA Connection Configs", QAScenario48Menu.DeleteQAConnectionConfigs, true));

        bool pump = EditorGUILayout.Toggle(
            new GUIContent("Focusless Play Pump", "48 시나리오 Play 중 에디터 포커스가 없어도 플레이어 루프를 돌린다(다음 Play 부터)."),
            QAFocuslessEditorPump.Enabled);
        if (pump != QAFocuslessEditorPump.Enabled)
            QAFocuslessEditorPump.Toggle();
    }

    private static void Draw48Build()
    {
        Row(("Build Demo Player (QA artifacts)", QABuild48.Build, true),
            ("Prepare Host·Client Folders", QABuild48.PrepareInstances, true));
        Row(("Run B2 Rejection Suite", QABuild48StandaloneRunner.RunB2, true),
            ("Run B3 Recovery Suite", QABuild48StandaloneRunner.RunB3, true));
        if (QABuild48StandaloneRunner.IsRunning)
            EditorGUILayout.HelpBox("standalone 스위트 실행 중.", MessageType.None);
    }

    private static void Draw88()
    {
        Row(("Run Interrupt Lag Suite", QAAutomation.Interrupt88RunSuite, true),
            ("Run Single Run (Selected)", QAAutomation.Interrupt88RunSingle, true));
        Row(("Stop Suite", QAAutomation.Interrupt88Stop, true),
            ("Open Latest Report Folder", QAScenario88Menu.OpenLatest, true));

        SubHeader("Single");
        var target = (QAInterruptTargetKind)EditorGUILayout.EnumPopup("Target", QAScenario88Menu.SingleTarget);
        if (target != QAScenario88Menu.SingleTarget)
            QAScenario88Menu.SingleTarget = target;
        int delay = EditorGUILayout.IntPopup("Delay (ms)", QAScenario88Menu.SingleDelayMs,
            Array.ConvertAll(DelayOptions, d => $"{d} ms"), DelayOptions);
        if (delay != QAScenario88Menu.SingleDelayMs)
            QAScenario88Menu.SingleDelayMs = delay;
        int mode = EditorGUILayout.Popup("Mode", (int)QAScenario88Menu.SingleMode, new[] { "A Sweep", "B Human" });
        if (mode != (int)QAScenario88Menu.SingleMode)
            QAScenario88Menu.SingleMode = (QAInterruptBotMode)mode;

        SubHeader("Settings");
        int attempts = EditorGUILayout.IntPopup("Attempts", QAScenario88Menu.Attempts, AttemptsLabels, AttemptsOptions);
        if (attempts != QAScenario88Menu.Attempts)
            QAScenario88Menu.Attempts = attempts;
        bool hostBot = EditorGUILayout.Toggle("Host Bot Also Presses", QAScenario88Menu.HostBot);
        if (hostBot != QAScenario88Menu.HostBot)
            QAScenario88Menu.HostBot = hostBot;
        int timeout = EditorGUILayout.IntPopup("Run Timeout", QAScenario88Menu.RunTimeoutSec, TimeoutLabels, TimeoutOptions);
        if (timeout != QAScenario88Menu.RunTimeoutSec)
            QAScenario88Menu.RunTimeoutSec = timeout;

        SubHeader("NetSim (원격 지연)");
        Row(("5 ms", QAScenario88Menu.Delay5, true), ("35 ms", QAScenario88Menu.Delay35, true),
            ("80 ms", QAScenario88Menu.Delay80, true), ("120 ms", QAScenario88Menu.Delay120, true));
        Row(("Reset Remote Delay (0)", QAScenario88Menu.Reset, true), ("Status", QAScenario88Menu.Status, true));
    }

    private static void Draw103()
    {
        Row(("Run 1 Match (Boss 3P)", QAAutomation.Boss103Run1Match, true),
            ("Status", QAAutomation.Boss103Status, true),
            ("Open Report Folder", QAScenario103Menu.OpenReports, true));

        SubHeader("Settings");
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Run", QAScenario103Menu.Run.ToString(), GUILayout.MinWidth(120));
            Button("Next Run (+1)", QAScenario103Menu.NextRun, true);
            Button("Run = 1", QAScenario103Menu.ResetRun, true);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Seed", QAScenario103Menu.SeedValue + (QAScenario103Menu.IsDefaultSeed ? " (default)" : ""), GUILayout.MinWidth(120));
            Button("Seed 103 (default)", QAScenario103Menu.DefaultSeed, true);
            Button("New Random Seed", QAScenario103Menu.RandomSeed, true);
        }
        int tier = EditorGUILayout.IntPopup("Bot Tier", QAScenario103Menu.TierSetting, TierLabels, TierOptions);
        if (tier != QAScenario103Menu.TierSetting)
            QAScenario103Menu.TierSetting = tier;

        Row(("Delete Config Files", QAAutomation.Boss103DeleteConfigFiles, true),
            ("Minimize Windows Now", QAAutomation.MinimizeWindows, true));
    }

    private static void Draw103Batch()
    {
        bool canStart = QABoss103BatchRunner.CanStartBatch;
        Row(("Start 90", QAAutomation.Boss103BatchStart90, canStart),
            ("Start 30 (119 — 단계별 10판)", QAAutomation.Boss103BatchStart30, canStart));
        Row(("Resume Last", QAAutomation.Boss103BatchResumeLast, canStart),
            ("Resume (Choose Folder)...", QABoss103BatchRunner.ResumeChoose, canStart));
        Row(("Stop After Current Run", QAAutomation.Boss103BatchStopAfterCurrent, QABoss103BatchRunner.CanStopAfterCurrent),
            ("Open Report", QAAutomation.Boss103BatchOpenReport, true),
            ("Status", QAAutomation.Boss103BatchStatus, true));
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("비교 기준(119)", QABoss103BatchRunner.CompareDir);
            Button("Compare Folder...", QABoss103BatchRunner.ChooseCompareDir, true, GUILayout.Width(130));
        }
    }

    private static void Draw572()
    {
        Row(("Analyze Latest Boss Profile", QAAutomation.AnalyzeLatestBossProfile, true),
            ("Analyze Previous Boss Profile", QABossProfileAnalyzer.AnalyzePrevious, true));
    }

    private static void DrawNightly()
    {
        bool canStart = QANightlyMenu.CanStart;
        Row(("Start Night", QAAutomation.StartNight, canStart),
            ("Resume Last", QAAutomation.ResumeNight, canStart));
        Row(("Stop After Current Run", QAAutomation.StopNightAfterCurrentRun, true),
            ("Status", QAAutomation.NightStatus, true),
            ("Open Report", QAAutomation.NightOpenReport, true));
        SubHeader("Settings");
        Row(("Validate Seed File", QAAutomation.NightValidateSeedFile, true),
            ("Create Local Settings File", QAAutomation.NightCreateLocalSettingsFile, true),
            ("Test OBS Connection", QAAutomation.NightTestObsConnection, true));
    }

    // ── 공통 설정(기존 설정 창) ──────────────────────────────

    private static void DrawSettings()
    {
        EditorGUILayout.LabelField("ML-Agents QA 자동화 설정", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        QASettings.AutoRun = EditorGUILayout.Toggle(
            new GUIContent("Auto-run from BootStrap", "0.BootStrapScene에서 Play 시 QA 하네스를 자동 생성한다."),
            QASettings.AutoRun);

        float duration = EditorGUILayout.FloatField(
            new GUIContent("Play 시간(초)", "맵 도달 후 각 사이클을 돌릴 시간."),
            QASettings.Duration);
        QASettings.Duration = Mathf.Max(5f, duration);

        int repeat = EditorGUILayout.IntField(
            new GUIContent("반복 횟수 (0 = 무한)", "QA 전체 사이클 반복 횟수. 0 이하이면 무한 반복(Play 정지 시까지)."),
            QASettings.RepeatCount);
        QASettings.RepeatCount = Mathf.Max(0, repeat);

        int players = EditorGUILayout.IntField(
            new GUIContent("인원 수 (호스트 포함)", "3=호스트1+MPPM 가상 플레이어2. 1=호스트 단독. MPPM 창에서 (인원-1)개 가상 플레이어를 활성화해야 함."),
            QASettings.PlayerCount);
        QASettings.PlayerCount = Mathf.Max(1, players);

        QASettings.ResultPaths = EditorGUILayout.Toggle(
            new GUIContent("결과 화면 경로 모드",
                "켜면 Play 시간 뒤 호스트가 판을 끝내 결과 화면까지 간다. 사이클 1=중도 종료, 2=전멸, 3=클리어(반복 3 권장). " +
                "각 피어가 result-stats.md 를 남긴다."),
            QASettings.ResultPaths);
        QASettings.ResultPathOrder = EditorGUILayout.TextField(
            new GUIContent("경로 순서", "쉼표 구분: Aborted,Wiped,Cleared. 비우면 이 기본 순서."),
            QASettings.ResultPathOrder);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("리포트 저장 위치", EditorStyles.boldLabel);
        DrawReportRoot();

        EditorGUILayout.Space(8);
        string repeatText = QASettings.RepeatCount <= 0 ? "무한" : $"{QASettings.RepeatCount}회";
        string mpText = QASettings.PlayerCount <= 1
            ? "호스트 단독"
            : $"{QASettings.PlayerCount}인(호스트1+가상 플레이어{QASettings.PlayerCount - 1})";
        EditorGUILayout.HelpBox(
            $"현재 설정: {mpText} · 사이클당 {QASettings.Duration:F0}초 × {repeatText}\n" +
            "반복 시 매 회 부팅부터 재시작(네트워크 종료 → 로비→호스트/클라→맵 재구동, 사이클마다 리포트 1개).\n" +
            "MPPM: Window ▸ Multiplayer Play Mode에서 가상 플레이어를 (인원-1)개 활성화. 각 인스턴스가 자기 관점 리포트를 남김.\n" +
            $"리포트: {QAReportWriter.RunsRoot()}",
            MessageType.Info);
    }

    /// <summary>
    /// 리포트 저장 루트 입력. 시스템 절대 경로만 유효하며 프로젝트 밖도 가능하다.
    /// 비우면 기본 경로(&lt;repo&gt;/Docs/temp/qa-runs)를 쓴다.
    /// </summary>
    private static void DrawReportRoot()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            QASettings.ReportRoot = EditorGUILayout.TextField(
                new GUIContent("저장 폴더(절대 경로)",
                    "예: D:/QALogs. 비우면 기본값 <repo>/Docs/temp/qa-runs 를 쓴다."),
                QASettings.ReportRoot);

            if (GUILayout.Button("찾기", GUILayout.Width(44)))
            {
                string picked = EditorUtility.OpenFolderPanel(
                    "QA 리포트 저장 폴더 선택", QAReportWriter.RunsRoot(), string.Empty);
                if (!string.IsNullOrEmpty(picked))
                {
                    QASettings.ReportRoot = picked;
                    GUI.FocusControl(null);   // 텍스트 필드가 옛 값을 붙잡고 있지 않도록
                }
            }

            if (GUILayout.Button("기본값", GUILayout.Width(56)))
            {
                QASettings.ReportRoot = string.Empty;
                GUI.FocusControl(null);
            }
        }

        string configured = QASettings.ReportRoot;
        if (string.IsNullOrWhiteSpace(configured))
            return;

        if (!Path.IsPathRooted(configured.Trim()))
        {
            EditorGUILayout.HelpBox(
                "절대 경로가 아니어서 무시되고 기본 경로에 저장됩니다.", MessageType.Warning);
            return;
        }

        // 폴더는 리포트 기록 시점에 생성되므로, 미리 없다고 막지 않고 안내만 한다.
        if (!Directory.Exists(configured.Trim()))
        {
            EditorGUILayout.HelpBox(
                "아직 없는 폴더입니다. 첫 리포트를 남길 때 자동 생성됩니다.", MessageType.None);
        }
    }

    // ── 그리기 도구 ──────────────────────────────────────────

    /// <summary>Foldout 머리. 펼침 상태는 EditorPrefs 에 둔다.</summary>
    private static bool Section(string key, string title, bool defaultOpen = false)
    {
        EditorGUILayout.Space(2);
        bool open = EditorPrefs.GetBool(FoldPrefix + key, defaultOpen);
        bool next = EditorGUILayout.Foldout(open, title, true, EditorStyles.foldoutHeader);
        if (next != open)
            EditorPrefs.SetBool(FoldPrefix + key, next);
        if (next)
            EditorGUILayout.Space(2);
        return next;
    }

    private static void SubHeader(string text) => EditorGUILayout.LabelField(text, EditorStyles.miniBoldLabel);

    private static void Row(params (string label, Action action, bool enabled)[] items)
    {
        using (new EditorGUILayout.HorizontalScope())
            foreach ((string label, Action action, bool enabled) in items)
                Button(label, action, enabled);
    }

    /// <summary>버튼. 누르면 OnGUI 밖(delayCall)에서 실행한다. <paramref name="enabled"/> = 옛 메뉴 validate 조건.</summary>
    private static void Button(string label, Action action, bool enabled, params GUILayoutOption[] options)
    {
        using (new EditorGUI.DisabledScope(!enabled))
            if (GUILayout.Button(label, options))
                EditorApplication.delayCall += () => action();
    }
}
#endif
