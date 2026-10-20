using UnityEngine;

/// <summary>
/// 소프트락/스턱 감지: 에이전트가 이동을 지시했는데도 일정 시간 실제 위치가 거의
/// 변하지 않으면(벽 끼임·CC 무한·이동 불능) 발견으로 기록한다.
/// 정상 대기(이동 미지시)는 오탐이 되지 않도록 QABlackboard.LastMoveIntentTime을 참조한다.
/// </summary>
public sealed class SoftlockDetector : IQADetector
{
    public string Name => "Softlock";

    private const float StuckSeconds = 6f;      // 이 시간 이상 정체 시 의심
    private const float MoveEpsilon = 0.5f;     // 이 거리 미만 이동은 "정체"로 간주
    private const float ReportCooldown = 15f;   // 같은 스턱 반복 보고 방지 간격

    private Vector3 _lastPos;
    private float _stuckSince = -1f;
    private float _lastReportTime = -999f;
    private bool _hasPos;

    public void OnSessionStart(QARecorder recorder)
    {
        _hasPos = false;
        _stuckSince = -1f;
    }

    public void OnSessionEnd(QARecorder recorder) { }

    public void Tick(QARecorder recorder, float deltaTime)
    {
        Player player = WorldObserver.LocalPlayer;
        if (player == null)
        {
            _hasPos = false;
            _stuckSince = -1f;
            return;
        }

        Vector3 pos = player.transform.position;
        if (!_hasPos)
        {
            _lastPos = pos;
            _hasPos = true;
            return;
        }

        // 최근에 이동을 지시했는지(0.5s 이내). 아니면 정상 대기이므로 스턱 판정 제외.
        bool intendedToMove = (Time.time - QABlackboard.LastMoveIntentTime) < 0.5f;
        bool moved = Vector3.Distance(pos, _lastPos) >= MoveEpsilon;

        if (moved || !intendedToMove)
        {
            _lastPos = pos;
            _stuckSince = -1f;
            return;
        }

        // 이동 지시 중인데 위치가 안 변함 — 정체 타이머 시작/지속.
        if (_stuckSince < 0f)
            _stuckSince = Time.time;

        float stuckDuration = Time.time - _stuckSince;
        if (stuckDuration >= StuckSeconds && (Time.time - _lastReportTime) >= ReportCooldown)
        {
            _lastReportTime = Time.time;
            recorder.Add(
                QASeverity.Warning,
                Name,
                $"이동 지시 중 {stuckDuration:F1}s 동안 위치 정체(소프트락 의심) @ {pos}",
                $"state={player.CurrentState} canMove={player.CanMove}");
            // 보고 후 기준 위치 갱신해 연속 스팸 방지.
            _lastPos = pos;
            _stuckSince = -1f;
        }
    }
}
