#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// QA 창 — QA 메뉴는 Tools ▸ QA 하나(이 창 열기)뿐이고 실행·설정은 모두 여기 버튼으로 한다(할 일 155).
/// 맨 위(항상 보임) = 시나리오 실행/정지 · 가상 플레이어 · 야간. 그 아래 Foldout = 설정(기본 펼침) · 48 · 88 · 103 · 103 배치 · 57.2 · 빌드 · 디버그(기본 접힘, 펼침 상태는 EditorPrefs).
/// 라벨은 짧게, 자세한 설명·옛 메뉴 이름은 툴팁. 에이전트는 창 대신 <see cref="QAAutomation"/> 을 부른다.
/// 버튼 동작은 <see cref="EditorApplication.delayCall"/> 로 OnGUI 밖에서 실행한다(폴더 선택 창·Play 전환이 레이아웃을 깨지 않게).
/// </summary>
public sealed class QASettingsWindow : EditorWindow
{
    private const string FoldPrefix = "QA.Window.Fold.";

    private static readonly int[] DelayOptions = QAInterruptLag88Plan.DelayStepsMs;
    private static readonly int[] AttemptsOptions = { 30, 10, 3 };
    private static readonly string[] AttemptsLabels = { "30 (기본)", "10", "3 (스모크)" };
    private static readonly int[] TimeoutOptions = { 600, 1200 };
    private static readonly string[] TimeoutLabels = { "600초 (기본)", "1200초" };
    private static readonly int[] TierOptions = { -1, (int)QABoss103BotTier.Beginner, (int)QABoss103BotTier.Normal, (int)QABoss103BotTier.Skilled };
    private static readonly string[] TierLabels = { "자동 (판 번호 mod 3)", "Beginner", "Normal", "Skilled" };

    private Vector2 _scroll;

    [MenuItem("Tools/QA")]
    public static void Open()
    {
        var window = GetWindow<QASettingsWindow>("QA");
        window.minSize = new Vector2(420, 320);
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.Space(6);

        DrawTop();

        if (Section("Settings", "설정", true))
            DrawSettings();
        if (Section("48", "48 로비 데모"))
            Draw48();
        if (Section("88", "88 간파 지연"))
            Draw88();
        if (Section("103", "103 보스전"))
            Draw103();
        if (Section("103Batch", "103 배치"))
            Draw103Batch();
        if (Section("57.2", "57.2 프로파일"))
            Draw572();
        if (Section("Build", "빌드"))
            DrawBuild();
        if (Section("Debug", "디버그"))
            DrawDebug();

        EditorGUILayout.Space(6);
        EditorGUILayout.EndScrollView();
    }

    // ── 맨 위(항상 보임) ─────────────────────────────────────

    private static void DrawTop()
    {
        LabeledRow("시나리오",
            ("실행", "Play Active Scenario — 툴바 Play 와 같은 경로(가상 플레이어 포함).", QAScenarioPlay.Play, true),
            ("정지", "Stop Active Scenario.", QAScenarioPlay.Stop, true));
        LabeledRow("가상 플레이어",
            ("켜기", "Activate Virtual Players (2,3).", QAScenarioPlay.ActivateVirtualPlayers, true),
            ("끄기", "Deactivate Virtual Players (2,3).", QAScenarioPlay.DeactivateVirtualPlayers, true),
            ("클라 태그", "Ensure MPPM Client Tags (2,3) — 이미 켜진 클론은 끄고 다시 켜야 반영된다.", QAScenarioPlay.EnsureClientTags, true));

        bool canStart = QANightlyMenu.CanStart;
        LabeledRow("야간",
            ("시작", "Start Night — 시드 파일대로 야간 QA(108) 연속 루프 시작.", QANightlyMenu.StartNight, canStart),
            ("이어하기", "Resume Last — 마지막 야간 배치 폴더에서 이어 간다.", QANightlyMenu.ResumeLast, canStart),
            ("현재 판 후 정지", "Stop After Current Run — 지금 판이 끝나면 연속 루프 정지(야간·103 배치 공용).", QANightlyMenu.StopAfterCurrent, true));
        LabeledRow(" ",
            ("상태", "Status — 시드 파일·PC 설정·연속 루프 진행을 콘솔에.", QANightlyMenu.Status, true),
            ("리포트 열기", "Open Report — 연속 루프 report.html(야간·103 배치 공용).", QANightlyMenu.OpenReport, true));
        EditorGUILayout.Space(4);
    }

    // ── 설정 ─────────────────────────────────────────────────

    private static void DrawSettings()
    {
        QASettings.AutoRun = EditorGUILayout.Toggle(
            new GUIContent("자동 실행", "0.BootStrapScene에서 Play 시 QA 하네스를 자동 생성한다(Auto-run from BootStrap)."),
            QASettings.AutoRun);

        float duration = EditorGUILayout.FloatField(
            new GUIContent("Play 시간(초)", "맵 도달 후 각 사이클을 돌릴 시간."),
            QASettings.Duration);
        QASettings.Duration = Mathf.Max(5f, duration);

        int repeat = EditorGUILayout.IntField(
            new GUIContent("반복 (0 = 무한)", "QA 전체 사이클 반복 횟수. 0 이하이면 무한 반복(Play 정지 시까지)."),
            QASettings.RepeatCount);
        QASettings.RepeatCount = Mathf.Max(0, repeat);

        int players = EditorGUILayout.IntField(
            new GUIContent("인원 (호스트 포함)", "3=호스트1+MPPM 가상 플레이어2. 1=호스트 단독. 가상 플레이어를 (인원-1)개 켜야 함."),
            QASettings.PlayerCount);
        QASettings.PlayerCount = Mathf.Max(1, players);

        QASettings.ResultPaths = EditorGUILayout.Toggle(
            new GUIContent("결과 화면 경로",
                "켜면 Play 시간 뒤 호스트가 판을 끝내 결과 화면까지 간다. 사이클 1=중도 종료, 2=전멸, 3=클리어(반복 3 권장). " +
                "각 피어가 result-stats.md 를 남긴다."),
            QASettings.ResultPaths);
        using (new EditorGUI.DisabledScope(!QASettings.ResultPaths))
            QASettings.ResultPathOrder = EditorGUILayout.TextField(
                new GUIContent("경로 순서", "쉼표 구분: Aborted,Wiped,Cleared. 비우면 이 기본 순서."),
                QASettings.ResultPathOrder);

        EditorGUILayout.Space(4);
        DrawReportRoot();

        string repeatText = QASettings.RepeatCount <= 0 ? "무한" : $"{QASettings.RepeatCount}회";
        string mpText = QASettings.PlayerCount <= 1
            ? "호스트 단독"
            : $"{QASettings.PlayerCount}인(호스트1+가상 플레이어{QASettings.PlayerCount - 1})";
        EditorGUILayout.LabelField(
            new GUIContent($"{mpText} · 사이클당 {QASettings.Duration:F0}초 × {repeatText}",
                "반복 시 매 회 부팅부터 재시작(네트워크 종료 → 로비→호스트/클라→맵 재구동, 사이클마다 리포트 1개).\n" +
                "MPPM 은 각 인스턴스가 자기 관점 리포트를 남긴다.\n" +
                $"리포트: {QAReportWriter.RunsRoot()}"),
            EditorStyles.miniLabel);

        SubHeader("야간 준비");
        Row(("시드 확인", "Validate Seed File — 야간 시드 파일 검사.", QANightlyMenu.ValidateSeed, true),
            ("PC 설정 파일", "Create Local Settings File — UserSettings/QANightly.local.json 생성.", QANightlyMenu.CreateLocal, true),
            ("OBS 확인", "Test OBS Connection.", QANightlyMenu.TestObs, true));
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
                new GUIContent("리포트 위치",
                    "절대 경로(예: D:/QALogs). 비우면 기본값 <repo>/Docs/temp/qa-runs 를 쓴다."),
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

            Button("열기", "Open Report Folder — 시나리오 리포트 폴더(48·103·57.2 공용).", QAScenario48Menu.OpenReports, true, GUILayout.Width(44));
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

    // ── 섹션 ─────────────────────────────────────────────────

    private static void Draw48()
    {
        SubHeader("묶음");
        Row(("솔로 1", "Solo Batch (S1·S3·S5).", QAScenario48Menu.RunSolo, true),
            ("솔로 2", "Solo Batch 2 (S2·S4).", QAScenario48Menu.RunSolo2, true));
        Row(("MPPM 1", "MPPM Batch (M1·M4).", QAScenario48Menu.RunMppm, true),
            ("MPPM 2", "MPPM Batch 2 (M2·M3·M8).", QAScenario48Menu.RunMppm2, true),
            ("MPPM 2b", "MPPM Batch 2b (M3·M8).", QAScenario48Menu.RunMppm2b, true),
            ("MPPM 3", "MPPM Batch 3 (M5·M6·M7).", QAScenario48Menu.RunMppm3, true));
        SubHeader("하나씩");
        Row(("S1", "Boot·Spawn·Camera·Combat.", QAScenario48Menu.RunS1, true),
            ("S2", "Fall → Spawn.", QAScenario48Menu.RunS2, true),
            ("S3", "Ready Pad → Map.", QAScenario48Menu.RunS3, true),
            ("S4", "Character Select.", QAScenario48Menu.RunS4, true),
            ("S5", "Match End → Lobby x3.", QAScenario48Menu.RunS5, true));
        Row(("M1", "MPPM Auto Join·Slots.", QAScenario48Menu.RunM1, true),
            ("M2", "Ready Reset·Cancel·Reject.", QAScenario48Menu.RunM2, true),
            ("M3", "Swap Sync·Map Spawn.", QAScenario48Menu.RunM3, true),
            ("M4", "MPPM Mid-match Rejoin.", QAScenario48Menu.RunM4, true));
        Row(("M5", "Boss Rejoin.", QAScenario48Menu.RunM5, true),
            ("M6", "Rejoin Restore.", QAScenario48Menu.RunM6, true),
            ("M7", "Absent Wipe Respawn.", QAScenario48Menu.RunM7, true),
            ("M8", "Newcomer Reject → Lobby.", QAScenario48Menu.RunM8, true));
        SubHeader("정리");
        Row(("시나리오 모드 해제", "Clear Scenario Mode — 평소 Play 에 QA 하네스가 시나리오를 다시 돌리지 않게.", QAScenario48Menu.Clear, true),
            ("접속 설정 삭제", "Delete QA Connection Configs.", QAScenario48Menu.DeleteQAConnectionConfigs, true));

        bool pump = EditorGUILayout.Toggle(
            new GUIContent("포커스 없이 Play", "Focusless Play Pump — 48 시나리오 Play 중 에디터 포커스가 없어도 플레이어 루프를 돌린다(다음 Play 부터)."),
            QAFocuslessEditorPump.Enabled);
        if (pump != QAFocuslessEditorPump.Enabled)
            QAFocuslessEditorPump.Toggle();
    }

    private static void Draw88()
    {
        Row(("스위트 실행", "Run Interrupt Lag Suite.", QAScenario88Menu.RunSuite, true),
            ("1회 실행", "Run Single Run (Selected) — 아래 대상·지연·모드로.", QAScenario88Menu.RunSingle, true),
            ("정지", "Stop Suite.", QAScenario88Menu.StopSuite, true),
            ("최근 리포트", "Open Latest Report Folder — 가장 최근 88-* 폴더.", QAScenario88Menu.OpenLatest, true));

        SubHeader("1회 실행");
        var target = (QAInterruptTargetKind)EditorGUILayout.EnumPopup("대상", QAScenario88Menu.SingleTarget);
        if (target != QAScenario88Menu.SingleTarget)
            QAScenario88Menu.SingleTarget = target;
        int delay = EditorGUILayout.IntPopup("지연", QAScenario88Menu.SingleDelayMs,
            Array.ConvertAll(DelayOptions, d => $"{d} ms"), DelayOptions);
        if (delay != QAScenario88Menu.SingleDelayMs)
            QAScenario88Menu.SingleDelayMs = delay;
        int mode = EditorGUILayout.Popup("모드", (int)QAScenario88Menu.SingleMode, new[] { "A Sweep", "B Human" });
        if (mode != (int)QAScenario88Menu.SingleMode)
            QAScenario88Menu.SingleMode = (QAInterruptBotMode)mode;

        SubHeader("공통");
        int attempts = EditorGUILayout.IntPopup("시도 수", QAScenario88Menu.Attempts, AttemptsLabels, AttemptsOptions);
        if (attempts != QAScenario88Menu.Attempts)
            QAScenario88Menu.Attempts = attempts;
        bool hostBot = EditorGUILayout.Toggle(new GUIContent("호스트 봇도 누름", "Host Bot Also Presses."), QAScenario88Menu.HostBot);
        if (hostBot != QAScenario88Menu.HostBot)
            QAScenario88Menu.HostBot = hostBot;
        int timeout = EditorGUILayout.IntPopup("런 제한 시간", QAScenario88Menu.RunTimeoutSec, TimeoutLabels, TimeoutOptions);
        if (timeout != QAScenario88Menu.RunTimeoutSec)
            QAScenario88Menu.RunTimeoutSec = timeout;

        SubHeader("원격 지연 (NetSim)");
        Row(("5", "원격 지연 5 ms.", QAScenario88Menu.Delay5, true),
            ("35", "원격 지연 35 ms.", QAScenario88Menu.Delay35, true),
            ("80", "원격 지연 80 ms.", QAScenario88Menu.Delay80, true),
            ("120", "원격 지연 120 ms.", QAScenario88Menu.Delay120, true),
            ("0", "Reset Remote Delay (0).", QAScenario88Menu.Reset, true),
            ("상태", "클론별 원격 지연 파일 값을 콘솔에.", QAScenario88Menu.Status, true));
    }

    private static void Draw103()
    {
        Row(("1판 실행", "Run 1 Match (Boss 3P).", QAScenario103Menu.RunOne, true),
            ("상태", "Status — 시드·판·봇 단계·클론 수를 콘솔에.", QAScenario103Menu.Status, true));

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("판", QAScenario103Menu.Run.ToString(), GUILayout.MinWidth(120));
            Button("+1", "Next Run (+1).", QAScenario103Menu.NextRun, true);
            Button("1로", "Run = 1.", QAScenario103Menu.ResetRun, true);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("시드", QAScenario103Menu.SeedValue + (QAScenario103Menu.IsDefaultSeed ? " (기본)" : ""), GUILayout.MinWidth(120));
            Button("기본(103)", "Seed 103 (default).", QAScenario103Menu.DefaultSeed, true);
            Button("랜덤", "New Random Seed.", QAScenario103Menu.RandomSeed, true);
        }
        int tier = EditorGUILayout.IntPopup("봇 단계", QAScenario103Menu.TierSetting, TierLabels, TierOptions);
        if (tier != QAScenario103Menu.TierSetting)
            QAScenario103Menu.TierSetting = tier;

        Row(("설정 파일 삭제", "Delete Config Files — 호스트·클론 루트의 103 설정 파일.", QAScenario103Menu.DeleteConfigFiles, true),
            ("창 최소화", "Minimize Windows Now.", QAWindowMinimizer.MinimizeAll, true));
    }

    private static void Draw103Batch()
    {
        bool canStart = QABoss103BatchRunner.CanStartBatch;
        Row(("90판 시작", "Start 90.", QABoss103BatchRunner.Start90, canStart),
            ("30판 시작", "Start 30 (119 — 단계별 10판).", QABoss103BatchRunner.Start30, canStart),
            ("상태", "Status — 103 배치 진행만 콘솔에(야간 상태는 맨 위).", QABoss103BatchRunner.Status, true));
        Row(("이어하기", "Resume Last — 마지막 103 배치 폴더에서.", QABoss103BatchRunner.ResumeLast, canStart),
            ("폴더 골라 이어하기", "Resume (Choose Folder)...", QABoss103BatchRunner.ResumeChoose, canStart));
        EditorGUILayout.LabelField("정지·리포트는 맨 위 야간의 '현재 판 후 정지'·'리포트 열기'(같은 루프).", EditorStyles.miniLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(new GUIContent("비교 기준", "119 — 비교할 이전 배치 폴더."), new GUIContent(QABoss103BatchRunner.CompareDir));
            Button("폴더...", "Compare Folder...", QABoss103BatchRunner.ChooseCompareDir, true, GUILayout.Width(60));
        }
    }

    private static void Draw572()
    {
        Row(("실행", "P572 Boss Profile (57.2) — 솔로 보스전 실측 프로파일.", QAScenario48Menu.RunP572, true),
            ("최근 분석", "Analyze Latest Boss Profile.", QABossProfileAnalyzer.AnalyzeLatest, true),
            ("이전 분석", "Analyze Previous Boss Profile.", QABossProfileAnalyzer.AnalyzePrevious, true));
    }

    private static void DrawBuild()
    {
        Row(("데모 빌드", "Build Demo Player (QA artifacts).", QABuild48.Build, true),
            ("폴더 준비", "Prepare Host·Client Folders.", QABuild48.PrepareInstances, true));
        Row(("B2 거부", "Run B2 Rejection Suite — standalone.", QABuild48StandaloneRunner.RunB2, true),
            ("B3 복구", "Run B3 Recovery Suite — standalone.", QABuild48StandaloneRunner.RunB3, true));
        if (QABuild48StandaloneRunner.IsRunning)
            EditorGUILayout.HelpBox("standalone 스위트 실행 중.", MessageType.None);
    }

    private static void DrawDebug()
    {
        Row(("MPPM 상태", "MPPM Status.", QAScenarioPlay.Status, true),
            ("가상 플레이어 상태", "Virtual Player States.", QAScenarioPlay.PlayerStates, true),
            ("MPPM API 덤프", "Dump MPPM API — 내부 API 가 바뀌었을 때 확인용.", QAScenarioPlay.Dump, true));
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

    private static void Row(params (string label, string tooltip, Action action, bool enabled)[] items)
    {
        using (new EditorGUILayout.HorizontalScope())
            foreach ((string label, string tooltip, Action action, bool enabled) in items)
                Button(label, tooltip, action, enabled);
    }

    /// <summary>왼쪽에 고정 폭 라벨을 붙인 버튼 줄(맨 위 영역).</summary>
    private static void LabeledRow(string label, params (string label, string tooltip, Action action, bool enabled)[] items)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel, GUILayout.Width(90));
            foreach ((string text, string tooltip, Action action, bool enabled) in items)
                Button(text, tooltip, action, enabled);
        }
    }

    /// <summary>버튼. 누르면 OnGUI 밖(delayCall)에서 실행한다. <paramref name="enabled"/> = 옛 메뉴 validate 조건.</summary>
    private static void Button(string label, string tooltip, Action action, bool enabled, params GUILayoutOption[] options)
    {
        using (new EditorGUI.DisabledScope(!enabled))
            if (GUILayout.Button(new GUIContent(label, tooltip), options))
                EditorApplication.delayCall += () => action();
    }
}
#endif
