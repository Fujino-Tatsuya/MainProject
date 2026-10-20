using UnityEngine;

/// <summary>
/// Unity 콘솔의 Exception/Error/Assert를 잡아 발견으로 기록한다.
/// 게임 코드 무수정: Application.logMessageReceived를 QA에서 새로 구독한다.
/// (기존 Edit.* 로그는 [Conditional("UNITY_EDITOR")]라 에디터 PlayMode에서만 캡처됨 — Debug.* 는 항상)
/// </summary>
public sealed class LogErrorDetector : IQADetector
{
    public string Name => "LogError";

    private QARecorder _recorder;

    public void OnSessionStart(QARecorder recorder)
    {
        _recorder = recorder;
        Application.logMessageReceived += HandleLog;
    }

    public void OnSessionEnd(QARecorder recorder)
    {
        Application.logMessageReceived -= HandleLog;
        _recorder = null;
    }

    public void Tick(QARecorder recorder, float deltaTime) { }

    private void HandleLog(string condition, string stackTrace, LogType type)
    {
        if (_recorder == null)
            return;

        // 자기 자신([QA] 로그)은 무시해 되먹임 방지.
        if (!string.IsNullOrEmpty(condition) && condition.StartsWith("[QA]"))
            return;

        QASeverity severity;
        switch (type)
        {
            case LogType.Exception:
                severity = QASeverity.Critical;
                break;
            case LogType.Error:
            case LogType.Assert:
                severity = QASeverity.Error;
                break;
            default:
                return; // Warning/Log는 무시
        }

        // 동일 메시지 첫 줄로 dedup — 매 프레임 같은 예외가 쏟아져도 1건+카운트로 집계.
        string key = type + "|" + FirstLine(condition);
        _recorder.Add(severity, Name, condition, stackTrace, key);
    }

    private static string FirstLine(string s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        int nl = s.IndexOf('\n');
        return nl >= 0 ? s.Substring(0, nl) : s;
    }
}
