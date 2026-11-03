#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 간파 보정 캡처(할 일 139 → 191 5단계 '예측 vs 판정 vs 서버' 비교 기록기) — 간파 한 번을 답 대기 표본 하나로 묶는다. 랩 씬(InterruptCalibrationLab)에만 둔다.
/// 옛 예측기 계측 훅(139 — 오너·서버 예측·재예측·서버 판정)은 삭제됐고, 지금은 새 경로만 받는다:
/// <list type="bullet">
/// <item>(a) 오너 판정 순간 — <see cref="InterruptManager.OwnerJudged"/>: 판정 시점 허수아비 창 설정·실제 창 상태(<see cref="DebugDummyInterruptWindowMode.Actual"/>)·판정 위치 → 대상 거리·각도.</item>
/// <item>(b) QA 기록 한 줄 — <see cref="InterruptQaRecorder.RecordCompleted"/>: 예측(대상 없음/약/강)·오너 판정·서버 결과(승인·동시·거부·신고 안 함)·어긋난 이유·서버 시도 슬로우.</item>
/// </list>
/// 짝짓기 규칙은 <see cref="InterruptCalibrationPairing"/>. 완성 표본은 기록하지 않고 <see cref="SampleReady"/> 로만 낸다 — 질문 창이 답을 받아 Commit 한다.
/// 대상 거리 = 판정 위치 → 대상 판정 레이어 콜라이더 표면 수평 거리, 각도 = 공격 방향 ↔ 판정 위치에서 대상 루트 방향(수평, 도).
/// 판정이 대상을 못 골랐으면(대상 없음) 판정 위치에서 가장 가까운 등록 대상으로 잰다 — 왜 대상 없음인지 보려고.
/// 솔로 호스트 전제는 그대로(실제 창 상태는 서버 허수아비 게이트 값).
/// </summary>
[DisallowMultipleComponent]
public sealed class InterruptCalibrationCapture : MonoBehaviour
{
    const string Tag = "[InterruptCalib]";

    [Tooltip("근접 간파도 기록한다(기본 꺼짐 — 대쉬 간파만).")]
    [SerializeField] bool includeMelee;

    [Tooltip("허수아비 창 모드. 비우면 씬에서 찾는다(랩 허수아비 하나 전제).")]
    [SerializeField] DebugDummyInterruptWindowMode windowMode;

    readonly InterruptCalibrationPairing _pairing = new InterruptCalibrationPairing();
    readonly List<InterruptTargetRegistry.Entry> _entries = new List<InterruptTargetRegistry.Entry>();

    /// <summary>답 대기 표본이 완성됐다(QA 기록 줄이 닫힌 프레임).</summary>
    public event Action<InterruptCalibrationCompleted> SampleReady;

    /// <summary>판정 전에 끝나 표본이 되지 못한 시도 수.</summary>
    public int DroppedCount => _pairing.DroppedCount;

    void OnEnable()
    {
        ApplySettings();
        InterruptManager.OwnerJudged += HandleOwnerJudged;
        InterruptQaRecorder.RecordCompleted += HandleRecordCompleted;
    }

    void OnDisable()
    {
        InterruptManager.OwnerJudged -= HandleOwnerJudged;
        InterruptQaRecorder.RecordCompleted -= HandleRecordCompleted;
        _pairing.Clear();
    }

    void Update() => ApplySettings();

    void ApplySettings() => _pairing.IncludeMelee = includeMelee;

    void HandleOwnerJudged(ulong actorId, InterruptJudgeResult result)
    {
        float now = Time.time;
        var capture = new InterruptCalibrationJudgeCapture
        {
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

        MeasureTarget(actorId, result, out capture.TargetDistance, out capture.TargetAngle);
        _pairing.NoteJudged(actorId, capture);
    }

    void HandleRecordCompleted(InterruptQaRecord record)
    {
        if (!_pairing.OnRecord(record, out InterruptCalibrationCompleted completed))
            return;

        InterruptCalibrationSample s = completed.Sample;
        Debug.Log($"{Tag} 표본 완성 — {InterruptQaRules.Describe(record)} · " +
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

    // 판정 대상(없으면 판정 위치에서 가장 가까운 등록 대상)까지 표면 거리·공격 방향 각도.
    void MeasureTarget(ulong actorId, in InterruptJudgeResult result, out float? distance, out float? angle)
    {
        distance = null;
        angle = null;
        Vector3 resolvePosition = result.ResolvePosition;
        Player caster = null;
        InterruptManager manager = InterruptManager.Instance;
        if (manager != null)
            manager.TryGetPlayer(actorId, out caster);
        PlayerSkillController skills = caster != null ? caster.GetComponent<PlayerSkillController>() : null;
        PlayerSkillBase skill = skills != null ? skills.GetSkill(PlayerSkillSlot.Interrupt) : null;
        LayerMask layers = skill != null && skill.Data != null ? skill.Data.HittableLayers : (LayerMask)~0;

        InterruptTargetRegistry.CopyLiveTo(_entries);
        InterruptTargetRegistry.Entry chosen = null;
        float best = float.MaxValue;
        for (int i = 0; i < _entries.Count; i++)
        {
            InterruptTargetRegistry.Entry entry = _entries[i];
            if (entry.Unit == null || entry.Unit == caster)
                continue;

            if (result.TargetId != 0)
            {
                if (entry.Unit.IsSpawned && entry.Unit.NetworkObjectId == result.TargetId)
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
        Vector3 attack = Flat(result.AttackDirection);
        if (attack.sqrMagnitude > 0.0001f && toTarget.sqrMagnitude > 0.0001f)
            angle = Vector3.Angle(attack, toTarget);
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
#endif
