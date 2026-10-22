#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 88 스위트 정리 감시자(에디터). <c>Tools/QA/88</c> 실행 메뉴가 <see cref="Arm"/> 하면, 스위트가 어떻게 끝나든(정상·중단·예외·사람이 Play 정지)
/// <b>Play 정지 → 원격 지연 0·클론 명령 파일 삭제 → 가상 플레이어 비활성 → 시나리오 모드 해제(Tools/QA/48/Clear Scenario Mode)</b>를 보장한다.
/// <list type="bullet">
/// <item>호스트 스위트가 끝나면(<see cref="QAInterruptSuite88.Finished"/>) 클론이 <c>end</c> 를 읽을 시간(<see cref="StopDelaySeconds"/>) 뒤 Play 를 멈춘다.</item>
/// <item>호스트 심장박동(<see cref="QAInterruptSuite88.LastBeat"/>)이 <see cref="StallSeconds"/> 동안 멈추면(예외로 코루틴이 죽었거나 대기에 갇힘) Play 를 멈춘다.</item>
/// <item>Arm 뒤 <see cref="NoPlaySeconds"/> 안에 Play 가 시작되지 않으면(저장 안 된 씬 등) 정리만 한다.</item>
/// </list>
/// Play 가 멈추면(EnteredEditMode) 정리하고, 스위트 리포트(report.md)에 정리 사유 한 줄을 덧붙인다. 상태는 EditorPrefs 라 도메인 리로드를 넘는다.
/// </summary>
[InitializeOnLoad]
public static class QAInterruptSuite88Watcher
{
    private const string ActiveKey = "QA.88.WatchActive";
    private const string ArmedAtKey = "QA.88.WatchArmedAt";
    private const string StopReasonKey = "QA.88.WatchStopReason";
    public const double StallSeconds = 600.0;
    public const double NoPlaySeconds = 180.0;
    public const double StopDelaySeconds = 3.0;

    private static double _playEnteredAt = -1.0;
    private static double _finishedAt = -1.0;
    private static bool _stopRequested;

    static QAInterruptSuite88Watcher()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        if (EditorApplication.isPlaying)
            _playEnteredAt = Time.realtimeSinceStartupAsDouble; // Play 진입 도메인 리로드 뒤
    }

    private static bool Active => EditorPrefs.GetBool(ActiveKey, false);

    public static void Arm()
    {
        EditorPrefs.SetBool(ActiveKey, true);
        EditorPrefs.SetString(ArmedAtKey, DateTime.UtcNow.Ticks.ToString());
        EditorPrefs.DeleteKey(StopReasonKey);
        EditorPrefs.DeleteKey(QAInterruptSuite88.SuiteDirPrefKey);
        _stopRequested = false;
        _finishedAt = -1.0;
    }

    public static void Disarm()
    {
        EditorPrefs.DeleteKey(ActiveKey);
        EditorPrefs.DeleteKey(ArmedAtKey);
        EditorPrefs.DeleteKey(StopReasonKey);
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            _playEnteredAt = Time.realtimeSinceStartupAsDouble;
            _finishedAt = -1.0;
            _stopRequested = false;
        }
        else if (state == PlayModeStateChange.EnteredEditMode && Active)
        {
            string reason = EditorPrefs.GetString(StopReasonKey, "Play 정지(사람 또는 외부)");
            Cleanup(reason);
        }
    }

    private static void Tick()
    {
        if (!Active)
            return;

        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (long.TryParse(EditorPrefs.GetString(ArmedAtKey, "0"), out long ticks) &&
                (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds > NoPlaySeconds)
                Cleanup($"Arm 뒤 {NoPlaySeconds:0}초 안에 Play 시작 안 됨");
            return;
        }
        if (!EditorApplication.isPlaying || _stopRequested)
            return;

        double now = Time.realtimeSinceStartupAsDouble;
        if (QAInterruptSuite88.Finished)
        {
            if (_finishedAt < 0.0)
                _finishedAt = now;
            if (now - _finishedAt >= StopDelaySeconds)
                RequestStop("스위트 끝");
            return;
        }
        double last = Math.Max(_playEnteredAt, QAInterruptSuite88.Started ? QAInterruptSuite88.LastBeat : -1.0);
        if (_playEnteredAt >= 0.0 && now - last > StallSeconds)
            RequestStop($"진행 {StallSeconds:0}초 멈춤(예외·대기 갇힘 의심 — host.log·콘솔 확인)");
    }

    private static void RequestStop(string reason)
    {
        _stopRequested = true;
        EditorPrefs.SetString(StopReasonKey, reason);
        Debug.Log($"[QA88] 감시자: {reason} → Play 정지");
        QAScenarioPlay.Stop();
    }

    private static void Cleanup(string reason)
    {
        Disarm();
        try
        {
            QANetSim88.ResetRemoteDelayFiles();
            QAInterruptSuite88.DeleteCommandFiles();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[QA88] 감시자 파일 정리 실패: {e.Message}");
        }
        QAScenarioPlay.DeactivateVirtualPlayers();
        QAScenario48Menu.Clear();

        string dir = EditorPrefs.GetString(QAInterruptSuite88.SuiteDirPrefKey, string.Empty);
        string report = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "report.md");
        if (report != null && File.Exists(report))
            File.AppendAllText(report, $"\n> 정리(에디터 감시자 {DateTime.Now:yyyy-MM-dd HH:mm:ss}): {reason} — Play 정지·가상 플레이어 비활성·원격 지연 0·시나리오 모드 해제\n");
        Debug.Log($"[QA88] 감시자 정리 완료: {reason}{(report != null ? $" — 리포트 {report}" : "")}");
    }
}
#endif
