#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// QA 창 — QA 메뉴는 Tools ▸ QA 하나(이 창 열기)뿐이고 실행·설정은 모두 여기 버튼으로 한다(할 일 155).
/// 섹션은 두 개뿐: ① 야간 = 실행 버튼 + 야간 설정 ② QA = 시나리오 실행 버튼 + QA 설정 칸. 48·88·103·103 배치·57.2·빌드·디버그 섹션은 삭제됨(155).
/// 라벨은 짧게, 자세한 설명·옛 메뉴 이름은 툴팁. 에이전트는 창 대신 <see cref="QAAutomation"/> 을 부른다.
/// 버튼 동작은 <see cref="EditorApplication.delayCall"/> 로 OnGUI 밖에서 실행한다(폴더 선택 창·Play 전환이 레이아웃을 깨지 않게).
/// </summary>
public sealed class QASettingsWindow : EditorWindow
{
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

        Header("야간");
        DrawNightly();

        EditorGUILayout.Space(8);
        Header("QA");
        DrawQA();

        EditorGUILayout.Space(6);
        EditorGUILayout.EndScrollView();
    }

    // ── ① 야간 ───────────────────────────────────────────────

    private static void DrawNightly()
    {
        bool canStart = QANightlyMenu.CanStart;
        LabeledRow("실행",
            ("시작", "Start Night — 시드 파일대로 야간 QA(108) 연속 루프 시작.", QANightlyMenu.StartNight, canStart),
            ("이어하기", "Resume Last — 마지막 야간 배치 폴더에서 이어 간다.", QANightlyMenu.ResumeLast, canStart),
            ("현재 판 후 정지", "Stop After Current Run — 지금 판이 끝나면 연속 루프 정지.", QANightlyMenu.StopAfterCurrent, true));
        LabeledRow(" ",
            ("상태", "Status — 시드 파일·PC 설정·연속 루프 진행을 콘솔에.", QANightlyMenu.Status, true),
            ("리포트 열기", "Open Report — 야간 배치 report.html.", QANightlyMenu.OpenReport, true));
        LabeledRow("설정",
            ("시드 확인", "Validate Seed File — 야간 시드 파일 검사.", QANightlyMenu.ValidateSeed, true),
            ("PC 설정 파일", "Create Local Settings File — UserSettings/QANightly.local.json 생성.", QANightlyMenu.CreateLocal, true),
            ("OBS 확인", "Test OBS Connection.", QANightlyMenu.TestObs, true));
    }

    // ── ② QA ─────────────────────────────────────────────────

    private static void DrawQA()
    {
        LabeledRow("시나리오",
            ("실행", "Play Active Scenario — 툴바 Play 와 같은 경로(가상 플레이어 포함).", QAScenarioPlay.Play, true),
            ("정지", "Stop Active Scenario.", QAScenarioPlay.Stop, true),
            ("모드 해제", "Clear Scenario Mode — 평소 Play 에 QA 하네스가 시나리오를 다시 돌리지 않게(QA 접속 설정·원격 지연 파일도 삭제).", QAScenario48Menu.Clear, true));
        LabeledRow("가상 플레이어",
            ("켜기", "Activate Virtual Players (2,3).", QAScenarioPlay.ActivateVirtualPlayers, true),
            ("끄기", "Deactivate Virtual Players (2,3).", QAScenarioPlay.DeactivateVirtualPlayers, true),
            ("클라 태그", "Ensure MPPM Client Tags (2,3) — 이미 켜진 클론은 끄고 다시 켜야 반영된다.", QAScenarioPlay.EnsureClientTags, true));
        EditorGUILayout.Space(4);

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

            Button("열기", "Open Report Folder — 시나리오 리포트 폴더.", QAScenario48Menu.OpenReports, true, GUILayout.Width(44));
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

    /// <summary>섹션 머리(야간·QA).</summary>
    private static void Header(string title)
    {
        EditorGUILayout.LabelField(title, EditorStyles.largeLabel);
        Rect line = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(line, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        EditorGUILayout.Space(2);
    }

    /// <summary>왼쪽에 고정 폭 라벨을 붙인 버튼 줄.</summary>
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
