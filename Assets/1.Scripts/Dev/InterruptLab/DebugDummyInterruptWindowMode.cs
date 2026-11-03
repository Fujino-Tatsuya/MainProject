using UnityEngine;

/// <summary>
/// 간파 예측 보정 랩 허수아비 창 모드(할 일 139). 랩 씬의 허수아비 인스턴스에만 붙인다 — 본 게임 허수아비(맵씬·튜토리얼)엔 붙이지 않는다.
///
/// <list type="bullet">
/// <item><b>무기한</b> — 기존 동작 그대로(성공 → 회복 → 다시 무기한 인터럽트 가능, <see cref="DebugDummyInterruptRecovery"/>).
///       "강 연출이 안 뜬다" 증상 재현용.</item>
/// <item><b>주기</b> — 열림·닫힘 길이 반복. 타이밍 규칙은 <see cref="DummyInterruptWindowSchedule"/>.</item>
/// </list>
/// 같은 오브젝트의 <see cref="TrainingDummyInterrupt"/> 에 <see cref="ITrainingDummyWindowGate"/> 로 걸린다(서버만 쓴다 — 솔로 호스트 전제).
/// Play 중 모드·길이 변경(인스펙터 또는 <see cref="SetMode"/>·<see cref="SetDurations"/>)은 바로 적용된다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TrainingDummyInterrupt))]
public sealed class DebugDummyInterruptWindowMode : MonoBehaviour, ITrainingDummyWindowGate
{
    [Tooltip("무기한 = 기존 동작(증상 재현). 주기 = 열림·닫힘 길이 반복. Play 중 변경은 바로 적용(주기로 바꾸면 열림부터).")]
    [SerializeField] DummyInterruptWindowMode mode = DummyInterruptWindowMode.Unbounded;

    [Tooltip("주기 모드 열림 길이(초). 0 = 열리지 않음.")]
    [SerializeField, Min(0f)] float openSeconds = DummyInterruptWindowSchedule.DefaultOpenSeconds;

    [Tooltip("주기 모드 닫힘 길이(초). 0 = 계속 열림.")]
    [SerializeField, Min(0f)] float closedSeconds = DummyInterruptWindowSchedule.DefaultClosedSeconds;

    DummyInterruptWindowSchedule _schedule;
    TrainingDummyInterrupt _target;

    public DummyInterruptWindowMode Mode => mode;
    public float OpenSeconds => openSeconds;
    public float ClosedSeconds => closedSeconds;

    /// <summary>회복 중이 아닐 때 지금 창이 열려 있어야 하는가(마지막 서버 틱 기준).</summary>
    public bool ScheduleOpen => Schedule.IsOpen;

    /// <summary>[서버] 실제 창 상태(회복 포함, 마지막 서버 틱 기준) — 예측 보정 표본의 판정 시점 창 상태(할 일 139 단계 3).</summary>
    public DummyInterruptWindowActual Actual { get; } = new DummyInterruptWindowActual();

    DummyInterruptWindowSchedule Schedule =>
        _schedule ??= new DummyInterruptWindowSchedule(mode, openSeconds, closedSeconds);

    /// <summary>모드를 바꾼다. 바로 적용(다음 서버 틱). 주기로 바꾸면 열림 단계 처음부터.</summary>
    public void SetMode(DummyInterruptWindowMode value)
    {
        mode = value;
        Schedule.SetMode(value);
    }

    /// <summary>주기 길이를 바꾼다(음수 = 0). 바로 적용 — 지금 단계 경과는 유지.</summary>
    public void SetDurations(float open, float closed)
    {
        openSeconds = Mathf.Max(0f, open);
        closedSeconds = Mathf.Max(0f, closed);
        Schedule.SetDurations(openSeconds, closedSeconds);
    }

    void OnEnable()
    {
        _target = GetComponent<TrainingDummyInterrupt>();
        if (_target != null)
            _target.ServerWindowGate = this;
    }

    void OnDisable()
    {
        // 다른 게이트로 바뀐 뒤라면 건드리지 않는다. 떼면 허수아비가 닫아 둔 창을 바로 연다.
        if (_target != null && ReferenceEquals(_target.ServerWindowGate, this))
            _target.ServerWindowGate = null;
    }

    bool ITrainingDummyWindowGate.TickWindowOpen(float deltaTime, bool recovering)
    {
        bool open = Schedule.Tick(deltaTime, recovering);
        Actual.Observe(Time.time, recovering, Schedule);
        return open;
    }

    float ITrainingDummyWindowGate.OpenWindowLength => Schedule.OpenWindowLength;

#if UNITY_EDITOR
    // Play 중 인스펙터 변경도 바로 적용한다(Play 밖에선 Schedule 이 첫 사용 때 직렬화 값으로 만들어진다).
    void OnValidate()
    {
        if (_schedule == null)
            return;

        _schedule.SetMode(mode);
        _schedule.SetDurations(openSeconds, closedSeconds);
    }
#endif
}
