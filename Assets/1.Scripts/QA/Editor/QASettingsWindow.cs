#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// QA 자동화 설정 창. 메뉴 Tools ▸ QA ▸ Settings.
/// Play 시간·반복 횟수·자동실행 토글을 EditorPrefs로 저장(재컴파일 없이 즉시 반영).
/// </summary>
public sealed class QASettingsWindow : EditorWindow
{
    [MenuItem("Tools/QA/Settings")]
    public static void Open()
    {
        var window = GetWindow<QASettingsWindow>("QA Settings");
        window.minSize = new Vector2(320, 200);
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

        EditorGUILayout.Space(8);
        string repeatText = QASettings.RepeatCount <= 0 ? "무한" : $"{QASettings.RepeatCount}회";
        EditorGUILayout.HelpBox(
            $"현재 설정: 사이클당 {QASettings.Duration:F0}초 × {repeatText}\n" +
            "반복 시 매 회 부팅부터 재시작한다(네트워크 종료 → 로비→호스트→맵 재구동, 사이클마다 리포트 1개).\n" +
            "리포트: Docs/temp/qa-runs/",
            MessageType.Info);
    }
}
#endif
