#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 간파 예측 보정 캡처(할 일 139 단계 3) — 대쉬 간파 한 번을 답 대기 표본 하나로 묶는다. 랩 씬(InterruptCalibrationLab)에만 둔다.
/// 솔로 호스트 전제(오너 = 서버 같은 프로세스) — 오너 값과 서버 값은 각자 경로의 계측 훅에서 받는다:
/// <list type="bullet">
/// <item>(a) 오너 입력 순간 — <see cref="PlayerInterruptSkillBase.CalibrationOwnerPredicted"/>: pressAge·예측 입력 전 필드·결과·판정 위치 + 대상 거리·각도.</item>
/// <item>(b) 서버 예측 — <see cref="PlayerInterruptSkillBase.CalibrationServerPredicted"/>: 시도 슬로우 강/약(대상 없음 = 슬로우 없음).</item>
/// <item>(c) 서버 판정 — <see cref="PlayerInterruptSkillBase.CalibrationServerJudged"/>: 성공/실패.</item>
/// <item>(d) 판정 시점 실제 창 — 허수아비 창 모드(<see cref="DebugDummyInterruptWindowMode"/>)의 설정·<see cref="DebugDummyInterruptWindowMode.Actual"/>.</item>
/// </list>
/// 짝짓기 규칙은 <see cref="InterruptCalibrationPairing"/>. 완성 표본은 기록하지 않고 <see cref="SampleReady"/> 로만 낸다 — 질문 창(단계 4)이 답을 받아 Commit 한다.
/// 대상 거리 = 예상 판정 위치 → 대상 판정 레이어 콜라이더 표면 수평 거리(예측기와 같은 값), 각도 = 공격(대시) 방향 ↔ 판정 위치에서 대상 루트 방향(수평, 도).
/// 예측이 대상을 못 골랐으면(대상 없음) 판정 위치에서 가장 가까운 등록 대상으로 잰다 — 왜 대상 없음인지 보려고.
/// </summary>
[DisallowMultipleComponent]
public sealed class InterruptCalibrationCapture : MonoBehaviour
{
    const string Tag = "[InterruptCalib]";

    [Tooltip("근접 간파도 기록한다(기본 꺼짐 — 이번 범위는 대쉬 간파만).")]
    [SerializeField] bool includeMelee;

    [Tooltip("판정 없이 기다리는 최대 시간(게임 초). 지나면 그 시도는 버린다.")]
    [SerializeField, Min(0.1f)] float pendingTimeout = InterruptCalibrationPairing.DefaultPendingTimeout;

    [Tooltip("허수아비 창 모드. 비우면 씬에서 찾는다(랩 허수아비 하나 전제).")]
    [SerializeField] DebugDummyInterruptWindowMode windowMode;

    readonly InterruptCalibrationPairing _pairing = new InterruptCalibrationPairing();
    readonly List<InterruptTargetRegistry.Entry> _entries = new List<InterruptTargetRegistry.Entry>();

    /// <summary>답 대기 표본이 완성됐다(서버 판정 직후, 같은 프레임).</summary>
    public event Action<InterruptCalibrationCompleted> SampleReady;

    /// <summary>짝을 못 지어 버린 시도 수.</summary>
    public int DroppedCount => _pairing.DroppedCount;

    void OnEnable()
    {
        ApplySettings();
        PlayerInterruptSkillBase.CalibrationOwnerPredicted += HandleOwnerPredicted;
        PlayerInterruptSkillBase.CalibrationServerPredicted += HandleServerPredicted;
        PlayerInterruptSkillBase.CalibrationServerJudged += HandleServerJudged;
    }

    void OnDisable()
    {
        PlayerInterruptSkillBase.CalibrationOwnerPredicted -= HandleOwnerPredicted;
        PlayerInterruptSkillBase.CalibrationServerPredicted -= HandleServerPredicted;
        PlayerInterruptSkillBase.CalibrationServerJudged -= HandleServerJudged;
        _pairing.Clear();
    }

    void Update()
    {
        ApplySettings();
        _pairing.Tick(Time.time);
    }

    void ApplySettings()
    {
        _pairing.IncludeMelee = includeMelee;
        _pairing.PendingTimeout = pendingTimeout;
    }

    void HandleOwnerPredicted(PlayerInterruptSkillBase.CalibrationPrediction p)
    {
        var capture = new InterruptCalibrationOwnerCapture
        {
            ViaDash = p.ViaDash,
            PressAge = p.PressAge,
            Input = p.Input,
            Prediction = p.Prediction,
        };
        MeasureTarget(p, out capture.TargetDistance, out capture.TargetAngle);
        _pairing.OnOwnerPredicted(capture, Time.time);
    }

    void HandleServerPredicted(PlayerInterruptSkillBase.CalibrationPrediction p) =>
        _pairing.OnServerPredicted(p.ViaDash, p.Prediction.Forecast, Time.time);

    void HandleServerJudged(PlayerInterruptSkillBase skill, bool viaDash, bool succeeded)
    {
        float now = Time.time;
        var capture = new InterruptCalibrationJudgeCapture
        {
            ViaDash = viaDash,
            Succeeded = succeeded,
            Time = now,
            RealTime = Time.realtimeSinceStartup,
        };

        DebugDummyInterruptWindowMode window = FindWindowMode();
        if (window != null)
        {
            capture.WindowMode = InterruptCalibrationPairing.ToCalibrationMode(window.Mode);
            capture.WindowOpenLength = window.OpenSeconds;
            capture.WindowClosedLength = window.ClosedSeconds;
            capture.ActualWindowOpen = window.Actual.IsOpen;
            capture.ActualOpenElapsed = InterruptCalibrationPairing.KnownOrNull(window.Actual.OpenElapsed(now));
            capture.ActualRemaining = InterruptCalibrationPairing.KnownOrNull(window.Actual.Remaining(now));
        }

        if (!_pairing.OnJudged(capture, out InterruptCalibrationCompleted completed))
            return;

        InterruptCalibrationSample s = completed.Sample;
        Debug.Log($"{Tag} 표본 완성 — 오너 {s.OwnerForecast?.ToString() ?? "-"}({s.OwnerReason?.ToString() ?? "-"}) · " +
                  $"서버 {s.ServerForecast?.ToString() ?? "-"} · 판정 {(succeeded ? "성공" : "실패")} · " +
                  $"실제 창 {(s.ActualWindowOpen == true ? "열림" : s.ActualWindowOpen == false ? "닫힘" : "-")}" +
                  $"(경과 {s.ActualOpenElapsed?.ToString("F3") ?? "-"}s · 남음 {s.ActualRemaining?.ToString("F3") ?? "-"}s)", this);
        SampleReady?.Invoke(completed);
    }

    DebugDummyInterruptWindowMode FindWindowMode()
    {
        if (windowMode == null)
            windowMode = FindAnyObjectByType<DebugDummyInterruptWindowMode>();
        return windowMode;
    }

    // 예측 대상(없으면 판정 위치에서 가장 가까운 등록 대상)까지 표면 거리·공격 방향 각도.
    void MeasureTarget(in PlayerInterruptSkillBase.CalibrationPrediction p, out float? distance, out float? angle)
    {
        distance = null;
        angle = null;
        Vector3 resolvePosition = p.Prediction.PredictedEndPosition;
        LayerMask layers = p.Skill != null && p.Skill.Data != null ? p.Skill.Data.HittableLayers : (LayerMask)~0;
        Unit owner = p.Skill != null ? p.Skill.GetComponentInParent<Unit>() : null;

        InterruptTargetRegistry.CopyLiveTo(_entries);
        InterruptTargetRegistry.Entry chosen = null;
        float best = float.MaxValue;
        for (int i = 0; i < _entries.Count; i++)
        {
            InterruptTargetRegistry.Entry entry = _entries[i];
            if (entry.Unit == null || entry.Unit == owner)
                continue;

            if (p.Target != null)
            {
                if (entry.Unit == p.Target)
                {
                    chosen = entry;
                    break;
                }
                continue;
            }

            float d = Flat(entry.Unit.transform.position - resolvePosition).sqrMagnitude;
            if (d < best)
            {
                best = d;
                chosen = entry;
            }
        }
        _entries.Clear();

        if (chosen == null)
            return;

        Vector3 toTarget = Flat(chosen.Unit.transform.position - resolvePosition);
        distance = chosen.TryGetHorizontalDistance(resolvePosition, layers, out float surface) ? surface : toTarget.magnitude;
        Vector3 attack = Flat(p.AttackDirection);
        if (attack.sqrMagnitude > 0.0001f && toTarget.sqrMagnitude > 0.0001f)
            angle = Vector3.Angle(attack, toTarget);
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
#endif
