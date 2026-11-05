/// <summary>
/// 간파 연출 단계가 설정을 읽는 창구(할 일 118) — 실제 구현은 <see cref="InterruptProductionDirector"/>(스위치 = 인스펙터 필드,
/// 슬로우 프로필 = 디렉터가 참조하는 <see cref="InterruptSlowMotionSettings"/>, 194 후속 2).
/// 단계는 값을 들고 있지 않고 사건마다 여기서 읽는다(Play 중 인스펙터 변경이 다음 사건에 바로 쓰인다).
/// </summary>
public interface IInterruptProductionSettings
{
    /// <summary>시도 슬로우 — 성공 예측(강).</summary>
    SlowMotionProfile InterruptProfile { get; }
    /// <summary>시도 슬로우 — 실패 예측(약, 할 일 132).</summary>
    SlowMotionProfile InterruptWeakProfile { get; }
    /// <summary>대쉬 간파 시도 슬로우 — 성공 예측(강, 194 후속 2).</summary>
    SlowMotionProfile InterruptDashProfile { get; }
    /// <summary>대쉬 간파 시도 슬로우 — 실패 예측(약, 194 후속 2).</summary>
    SlowMotionProfile InterruptDashWeakProfile { get; }
    /// <summary>성공 슬로우 — 일반·대쉬 간파.</summary>
    SlowMotionProfile InterruptSuccessProfile { get; }
    /// <summary>제압 간파 성공 슬로우(할 일 219 — 제압 칸 마지막 1칸). 서버가 성공 사건의 제압 표식으로 고른다.</summary>
    SlowMotionProfile InterruptSuppressSuccessProfile { get; }
    bool Get(InterruptProductionCue cue);
}
