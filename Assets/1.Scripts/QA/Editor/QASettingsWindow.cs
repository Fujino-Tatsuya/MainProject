#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// QA 자동화 설정 창. 메뉴 Tools ▸ QA ▸ Settings.
/// Play 시간·반복 횟수·자동실행 토글·리포트 저장 경로를 EditorPrefs로 저장(재컴파일 없이 즉시 반영).
/// </summary>
public sealed class QASettingsWindow : EditorWindow
{
    [MenuItem("Tools/QA/Settings")]
    public static void Open()
    {
        var window = GetWindow<QASettingsWindow>("QA Settings");
        window.minSize = new Vector2(420, 280);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(6);
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
}
#endif
