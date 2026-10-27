/// <summary>
/// 간파 연출 단계가 설정을 읽는 창구(할 일 118) — 실제 구현은 <see cref="InterruptProductionDirector"/>(인스펙터 필드).
/// 단계는 값을 들고 있지 않고 사건마다 여기서 읽는다(Play 중 인스펙터 변경이 다음 사건에 바로 쓰인다).
/// </summary>
public interface IInterruptProductionSettings
{
    SlowMotionProfile InterruptProfile { get; }
    SlowMotionProfile InterruptSuccessProfile { get; }
    bool Get(InterruptProductionCue cue);
}
