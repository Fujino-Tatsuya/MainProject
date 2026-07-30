using System;
using System.Collections.Generic;
using UnityEngine;

// ML-Agents QA 자동화 — 공용 모델/기록소.
// 게임 코드 무수정 원칙: QA 네임스페이스 없이 "QA" 접두사로 전역 충돌만 피한다.

/// <summary>발견 심각도. Critical은 스크린샷·즉시 강조 대상.</summary>
public enum QASeverity
{
    Info,
    Warning,
    Error,
    Critical,
}

/// <summary>QA 세션 중 감지기가 남기는 단일 발견 항목.</summary>
[Serializable]
public sealed class QAFinding
{
    public QASeverity Severity;
    public string Category;   // 감지기 이름(예: "LogError", "Softlock")
    public string Summary;    // 한 줄 요약
    public string Detail;     // 스택트레이스 등 상세(선택)
    public float TimeSeconds; // 세션 시작 기준 경과초
    public string Scene;      // 발생 시점 활성 씬
    public int Count = 1;     // 동일 발견 중복 횟수
}

/// <summary>
/// 감지기들이 발견을 모으는 공용 기록소. 리포트 라이터가 이걸 읽어 파일로 남긴다.
/// 로그 콜백(메인스레드)에서 호출되므로 단순 리스트로 충분하다.
/// </summary>
public sealed class QARecorder
{
    private readonly List<QAFinding> _findings = new List<QAFinding>();
    private readonly Dictionary<string, QAFinding> _dedup = new Dictionary<string, QAFinding>();

    public IReadOnlyList<QAFinding> Findings => _findings;

    /// <summary>세션 시작 시각(Time.time). 경과초 계산 기준.</summary>
    public float SessionStartTime { get; set; }

    /// <summary>
    /// 발견을 기록한다. dedupKey가 주어지고 동일 키가 이미 있으면 Count만 증가시켜
    /// 스팸성 에러가 리포트를 뒤덮지 않게 한다.
    /// </summary>
    public QAFinding Add(QASeverity severity, string category, string summary,
        string detail = null, string dedupKey = null)
    {
        if (dedupKey != null && _dedup.TryGetValue(dedupKey, out QAFinding existing))
        {
            existing.Count++;
            return existing;
        }

        var finding = new QAFinding
        {
            Severity = severity,
            Category = category,
            Summary = summary,
            Detail = detail,
            TimeSeconds = Mathf.Max(0f, Time.time - SessionStartTime),
            Scene = QAUtil.ActiveSceneName(),
        };

        _findings.Add(finding);
        if (dedupKey != null)
            _dedup[dedupKey] = finding;

        // 콘솔에도 즉시 표기(에디터에서 실시간 확인).
        Debug.Log($"[QA][{severity}][{category}] {summary}");
        return finding;
    }

    public int CountBySeverity(QASeverity severity)
    {
        int n = 0;
        for (int i = 0; i < _findings.Count; i++)
            if (_findings[i].Severity == severity)
                n += _findings[i].Count;
        return n;
    }
}

/// <summary>
/// 에이전트/입력/감지기 사이의 공유 런타임 신호. SoftlockDetector가
/// "에이전트가 이동을 시도했는지"를 알아야 오탐(정상 대기)을 줄일 수 있어 최소한만 공유한다.
/// </summary>
public static class QABlackboard
{
    /// <summary>QAAgent가 마지막으로 0이 아닌 이동을 지시한 Time.time.</summary>
    public static float LastMoveIntentTime = -999f;

    /// <summary>플레이어가 스폰되어 QA가 실제 조작 중인지.</summary>
    public static bool Controlling;

    public static void Reset()
    {
        LastMoveIntentTime = -999f;
        Controlling = false;
    }
}

/// <summary>QA 감지기 공통 수명주기. 세션 시작~틱~종료로 발견을 기록소에 쌓는다.</summary>
public interface IQADetector
{
    string Name { get; }
    void OnSessionStart(QARecorder recorder);
    void Tick(QARecorder recorder, float deltaTime);
    void OnSessionEnd(QARecorder recorder);
}

/// <summary>QA 공용 소도구.</summary>
public static class QAUtil
{
    public static string ActiveSceneName()
    {
        return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
    }
}
