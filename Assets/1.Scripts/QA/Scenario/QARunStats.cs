using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 48.3 3차 — 실행 환경 측정. "에디터 포커스 없이 돌려도 프레임·타이밍이 정상인가"를 리포트 머리에서 바로 보게 한다.
/// 포커스(시작 값·잃은 프레임 비율)·평균 fps(실시간 기준)·카운트다운 실측 초·이동 입력 중 실측 속도를 모은다.
/// 판정은 하지 않는다 — 숫자를 남기고, 포커스 없음 실행끼리 비교한다.
/// </summary>
public sealed class QARunStats
{
    public bool FocusedAtStart;
    public bool RunInBackground;

    private int _frames;
    private int _unfocusedFrames;
    private float _startRealtime = -1f;
    private float _lastRealtime;
    private float _maxFrameGap;

    // 카운트다운: 시작 관측 시각·시작 때 남은 초(서버 시계)
    private float _countdownStartRealtime = -1f;
    private double _countdownStartRemaining;
    private readonly List<string> _countdowns = new List<string>();

    // 이동: 입력을 넣는 동안의 수평 변위 / 실시간
    private Vector3 _lastPos;
    private bool _hadPos;
    private float _movedDistance;
    private float _movedSeconds;
    private float _expectedSpeed;

    /// <summary>
    /// 에디터 프로세스가 OS 전면(포커스)인가. 에디터 Play 의 Application.isFocused 는 Game 뷰 기준이라
    /// 에디터가 뒤에 있어도 True 로 나온다(48.3 3차 실측) — 판정은 이 값으로 한다. 빌드는 Application.isFocused.
    /// </summary>
    private static bool AppActive()
    {
#if UNITY_EDITOR
        return UnityEditorInternal.InternalEditorUtility.isApplicationActive;
#else
        return Application.isFocused;
#endif
    }

    public bool AppActiveAtStart;

    public void Begin()
    {
        FocusedAtStart = Application.isFocused;
        AppActiveAtStart = AppActive();
        RunInBackground = Application.runInBackground;
        _startRealtime = Time.realtimeSinceStartup;
        _lastRealtime = _startRealtime;
    }

    public void Tick(QAInputController input)
    {
        float now = Time.realtimeSinceStartup;
        _frames++;
        if (!AppActive())
            _unfocusedFrames++;
        _maxFrameGap = Mathf.Max(_maxFrameGap, now - _lastRealtime);
        float realDt = now - _lastRealtime;
        _lastRealtime = now;

        TickCountdown(now);
        TickMove(input, realDt);
    }

    private void TickCountdown(float now)
    {
        LobbySession s = LobbySession.Active;
        bool active = s != null && s.IsCountdownActive;
        if (active && _countdownStartRealtime < 0f)
        {
            _countdownStartRealtime = now;
            _countdownStartRemaining = s.CountdownRemainingSeconds;
        }
        else if (!active && _countdownStartRealtime >= 0f)
        {
            // 세션이 사라졌으면(맵 로드로 로비 언로드) 끝까지 간 것, 남아 있으면 취소·시작 처리 중 하나.
            string how = s == null ? "세션 사라짐(맵 진입)" : "카운트다운 꺼짐";
            _countdowns.Add($"{now - _countdownStartRealtime:F2}s (시작 때 남은 {_countdownStartRemaining:F2}s, {how})");
            _countdownStartRealtime = -1f;
        }
    }

    private void TickMove(QAInputController input, float realDt)
    {
        Player p = input != null ? input.Target : null;
        if (p == null)
        {
            _hadPos = false;
            return;
        }

        Vector3 pos = p.transform.position;
        if (_hadPos && input.IsMoving && realDt > 0f && realDt < 0.5f)
        {
            Vector3 d = pos - _lastPos;
            d.y = 0f;
            // 텔레포트(스폰·재배치)는 빼고 걷기만 잰다.
            if (d.magnitude < 3f)
            {
                _movedDistance += d.magnitude;
                _movedSeconds += realDt;
                _expectedSpeed = Mathf.Max(_expectedSpeed, input.ExpectedMoveSpeed);
            }
        }
        _lastPos = pos;
        _hadPos = true;
    }

    public void AppendMarkdown(StringBuilder sb)
    {
        float elapsed = _startRealtime < 0f ? 0f : _lastRealtime - _startRealtime;
        float fps = elapsed > 0f ? _frames / elapsed : 0f;
        float unfocusedPct = _frames > 0 ? 100f * _unfocusedFrames / _frames : 0f;
        bool focusLess = !AppActiveAtStart || unfocusedPct >= 50f;

        sb.AppendLine("## 실행 환경(포커스·프레임)");
        sb.AppendLine();
        sb.AppendLine($"- 포커스: {(focusLess ? "**포커스 없음**" : "포커스 있음")} — 시작 때 에디터 전면(isApplicationActive)={AppActiveAtStart}, " +
                      $"Application.isFocused={FocusedAtStart}(에디터에선 Game 뷰 기준이라 참고만), 에디터가 뒤에 있던 프레임 {unfocusedPct:F0}% ({_unfocusedFrames}/{_frames})");
#if UNITY_EDITOR
        string pump = UnityEditor.EditorPrefs.GetBool("QA.FocuslessPump", true) ? "켬" : "끔(비교 실행)";
#else
        string pump = "해당 없음(빌드)";
#endif
        sb.AppendLine($"- Application.runInBackground={RunInBackground}(QA 하네스가 런타임에 켬) · 에디터 플레이어 루프 펌프={pump}");
        sb.AppendLine($"- 평균 fps(실시간 기준): {fps:F1} — {_frames} 프레임 / {elapsed:F1}s, 가장 긴 프레임 간격 {_maxFrameGap * 1000f:F0}ms");
        sb.AppendLine($"- 카운트다운 실측: {(_countdowns.Count == 0 ? "관측 없음" : string.Join(" · ", _countdowns))}");
        if (_movedSeconds > 0.5f)
        {
            float speed = _movedDistance / _movedSeconds;
            string ratio = _expectedSpeed > 0f ? $" — 기대 {_expectedSpeed:F2} m/s 의 {100f * speed / _expectedSpeed:F0}%" : "";
            sb.AppendLine($"- 이동 실측: {_movedDistance:F1}m / {_movedSeconds:F1}s = {speed:F2} m/s{ratio}(벽·충돌로 막힌 구간 포함이라 하한 참고)");
        }
        else
        {
            sb.AppendLine("- 이동 실측: 이동 입력 구간 없음");
        }
        sb.AppendLine();
    }
}
