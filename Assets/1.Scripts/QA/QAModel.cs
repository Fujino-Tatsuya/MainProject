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
    public float TimeSeconds; // 세션 시작 기준 경과초(최초 발생)
    public string Scene;      // 최초 발생 시점 활성 씬
    public int Count = 1;     // 동일 발견 중복 횟수
    // dedup으로 묶인 발견은 최초 발생 시각·씬만 리포트에 남고 이후 재발은 Count만 오르는데,
    // 그러면 "몇 초 뒤 다른 씬에서도 계속 재발했다"가 안 보여 원인 판단을 그르칠 수 있다
    // (예: 로딩 중 1회로 보였지만 실제로는 본 게임플레이 씬에서도 계속 재발한 사례).
    // 그래서 마지막 재발 시각·씬도 별도로 갱신해 둔다.
    public float LastTimeSeconds; // 가장 최근 재발 시각(세션 시작 기준 경과초)
    public string LastScene;      // 가장 최근 재발 시점 활성 씬
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
        float nowSeconds = Mathf.Max(0f, Time.time - SessionStartTime);
        string nowScene = QAUtil.ActiveSceneName();

        if (dedupKey != null && _dedup.TryGetValue(dedupKey, out QAFinding existing))
        {
            existing.Count++;
            existing.LastTimeSeconds = nowSeconds;
            existing.LastScene = nowScene;
            // 최초 발생만 남기면 "그 뒤 다른 씬에서도 계속 재발했다"가 안 보인다 — 매 재발을
            // 콘솔에도 남겨 에디터에서 실시간으로 재발 여부를 눈으로 확인할 수 있게 한다.
            Debug.Log($"[QA][{severity}][{category}] (재발 {existing.Count}회, {nowScene} {nowSeconds:F1}s) {summary}");
            return existing;
        }

        var finding = new QAFinding
        {
            Severity = severity,
            Category = category,
            Summary = summary,
            Detail = detail,
            TimeSeconds = nowSeconds,
            Scene = nowScene,
            LastTimeSeconds = nowSeconds,
            LastScene = nowScene,
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

    /// <summary>스크립트(로비 걷기·시나리오)가 이동을 쥐고 있다 — QAAgent 휴리스틱은 행동을 넣지 않는다.</summary>
    public static bool ScriptedControl;

    public static void Reset()
    {
        LastMoveIntentTime = -999f;
        Controlling = false;
        ScriptedControl = false;
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

/// <summary>
/// 판정에서 빼는 발견의 단일 목록(할 일 48.3) — 검증 대상(48)과 무관한 알려진 노이즈.
/// 매칭된 로그는 Error 가 아니라 Info 로 <see cref="NoiseCategory"/> 에 쌓이고, 리포트의 '무관 노이즈·오탐 제외' 집계에 따로 나온다.
/// 감지기 오탐 제외는 같은 집계에 <see cref="FalsePositivePrefix"/> 분류로 쌓는다(근거는 각 감지기 주석).
/// </summary>
public static class QAKnownNoise
{
    public const string NoiseCategory = "Noise";
    public const string FalsePositivePrefix = "FP-";

    private struct Rule
    {
        public string[] All;   // 전부 포함해야 매칭
        public string Reason;
    }

    private static readonly Rule[] Rules =
    {
        new Rule
        {
            All = new[] { "[MCP] Failed to start server" },
            Reason = "com.community.unity-mcp 서버 포트 충돌(에디터 도구) — 48 무관",
        },
        new Rule
        {
            All = new[] { "[BridgeGate]", "미저작" },
            Reason = "절차 맵 다리 열림 위치 미저작(맵 저작 데이터) — 48 무관",
        },
    };

    public static bool TryMatch(string message, out string reason)
    {
        reason = null;
        if (string.IsNullOrEmpty(message))
            return false;

        foreach (Rule rule in Rules)
        {
            bool all = true;
            foreach (string token in rule.All)
                if (!message.Contains(token)) { all = false; break; }
            if (all)
            {
                reason = rule.Reason;
                return true;
            }
        }
        return false;
    }

    public static bool IsExcluded(QAFinding f) =>
        f.Category == NoiseCategory || (f.Category != null && f.Category.StartsWith(FalsePositivePrefix, StringComparison.Ordinal));
}

/// <summary>QA 공용 소도구.</summary>
public static class QAUtil
{
    public static string ActiveSceneName()
    {
        return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
    }
}
