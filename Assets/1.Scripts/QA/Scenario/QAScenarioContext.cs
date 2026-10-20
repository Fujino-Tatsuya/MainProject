using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>시나리오 항목 판정. Eye = 자동 판정 불가 — 스크린샷·근거로 사람이 본다.</summary>
public enum QAItemStatus
{
    Pass,
    Fail,
    Eye,
    Skip,
}

/// <summary>시나리오 체크리스트 한 줄(리포트 표의 행).</summary>
public sealed class QAScenarioItem
{
    public string Scenario;
    public string Name;
    public QAItemStatus Status;
    public string Evidence;
    public string Screenshot; // runDir 기준 상대 경로(없으면 null)
    public float TimeSeconds;
}

/// <summary>
/// 시나리오 하나가 쓰는 공용 도구 — 상태 대기형 단언(타임아웃), 항목 기록, 스크린샷, 에러 구간 집계.
/// 시간 기준 단언은 쓰지 않는다(PLAN-48-qa R2) — 전부 "조건이 참이 될 때까지, 최대 N초".
/// </summary>
public sealed class QAScenarioContext
{
    public readonly List<QAScenarioItem> Items = new List<QAScenarioItem>();
    public readonly List<string> SceneLog = new List<string>();

    public QARecorder Recorder;
    public QAInputController Input;
    public string RunDir;
    public string Role;
    public int ExpectedPlayers = 1;

    /// <summary>S5 판 종료 경로 순서(판마다 돌려 쓴다).</summary>
    public QAResultPath[] EndPaths = { QAResultPath.Aborted, QAResultPath.Wiped, QAResultPath.Cleared };

    /// <summary>지금 돌고 있는 시나리오 id(S1·M4 …).</summary>
    public string Current;

    /// <summary>직전 <see cref="Wait"/> 가 타임아웃으로 끝났는가.</summary>
    public bool TimedOut { get; private set; }

    /// <summary>직전 <see cref="Wait"/> 에 걸린 시간(초, unscaled).</summary>
    public float LastWaitSeconds { get; private set; }

    private int _shotIndex;

    public IEnumerator Wait(Func<bool> condition, float timeoutSeconds)
    {
        TimedOut = false;
        float elapsed = 0f;
        while (!SafeEval(condition))
        {
            if (elapsed >= timeoutSeconds)
            {
                TimedOut = true;
                LastWaitSeconds = elapsed;
                yield break;
            }
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        LastWaitSeconds = elapsed;
    }

    /// <summary>조건 평가 중 예외(파괴된 오브젝트 등)는 거짓으로 본다 — 대기 코루틴이 예외로 죽지 않게.</summary>
    private static bool SafeEval(Func<bool> condition)
    {
        try { return condition(); }
        catch (Exception) { return false; }
    }

    public QAScenarioItem Pass(string name, string evidence, string shot = null) => Add(QAItemStatus.Pass, name, evidence, shot);
    public QAScenarioItem Fail(string name, string evidence, string shot = null) => Add(QAItemStatus.Fail, name, evidence, shot);
    public QAScenarioItem Eye(string name, string evidence, string shot = null) => Add(QAItemStatus.Eye, name, evidence, shot);
    public QAScenarioItem Skip(string name, string evidence) => Add(QAItemStatus.Skip, name, evidence, null);

    public QAScenarioItem Check(bool ok, string name, string evidence, string shot = null)
        => Add(ok ? QAItemStatus.Pass : QAItemStatus.Fail, name, evidence, shot);

    private QAScenarioItem Add(QAItemStatus status, string name, string evidence, string shot)
    {
        var item = new QAScenarioItem
        {
            Scenario = Current,
            Name = name,
            Status = status,
            Evidence = evidence,
            Screenshot = shot,
            TimeSeconds = Recorder != null ? Mathf.Max(0f, Time.time - Recorder.SessionStartTime) : 0f,
        };
        Items.Add(item);
        Debug.Log($"[QA][{Current}] {status} — {name} | {evidence}{(shot != null ? $" | {shot}" : "")}");
        return item;
    }

    /// <summary>Game 뷰 스크린샷을 runDir/shots 에 남기고 runDir 기준 상대 경로를 돌려준다(프레임 끝에 저장된다).</summary>
    public string Shot(string label)
    {
        if (string.IsNullOrEmpty(RunDir))
            return null;

        string rel = Path.Combine("shots", $"{++_shotIndex:00}_{Current}_{label}.png");
        string full = Path.Combine(RunDir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        ScreenCapture.CaptureScreenshot(full);
        return rel.Replace('\\', '/');
    }

    // ── 에러 구간 집계(탐지기 기록소 기준) ─────────────────────────

    public readonly struct ErrorMark
    {
        public readonly int Total;
        public readonly float Time;

        public ErrorMark(int total, float time)
        {
            Total = total;
            Time = time;
        }
    }

    public ErrorMark MarkErrors()
    {
        return new ErrorMark(ErrorTotal(), Mathf.Max(0f, Time.time - Recorder.SessionStartTime));
    }

    /// <summary>마크 이후 늘어난 Error·Critical 건수와 요약(최대 5줄).</summary>
    public int ErrorsSince(ErrorMark mark, out string summary)
    {
        int delta = ErrorTotal() - mark.Total;
        var lines = new List<string>();
        var findings = Recorder.Findings;
        for (int i = 0; i < findings.Count && lines.Count < 5; i++)
        {
            QAFinding f = findings[i];
            if (f.Severity < QASeverity.Error || f.LastTimeSeconds < mark.Time)
                continue;
            lines.Add($"[{f.Severity}/{f.Category} x{f.Count}] {FirstLine(f.Summary)}");
        }
        summary = lines.Count > 0 ? string.Join(" / ", lines) : "없음";
        return delta;
    }

    private int ErrorTotal()
    {
        return Recorder.CountBySeverity(QASeverity.Error) + Recorder.CountBySeverity(QASeverity.Critical);
    }

    private static string FirstLine(string s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        int nl = s.IndexOf('\n');
        string line = nl >= 0 ? s.Substring(0, nl) : s;
        return line.Length > 160 ? line.Substring(0, 160) + "…" : line;
    }
}
