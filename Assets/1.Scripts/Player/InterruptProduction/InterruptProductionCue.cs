/// <summary>
/// 간파 연출 하나의 이름 — <see cref="InterruptProductionDirector"/> 의 스위치 칸 하나씩(할 일 104 A1~E1, 118 에서 이름 변경).
/// 스위치 칸·재생 지점·테스트가 이 이름을 공유한다. 직렬화된 값 없음(칸은 bool 필드).
/// </summary>
public enum InterruptProductionCue
{
    HitStop,                  // A1
    DarkAdaptation,           // A2
    OwnerZoom,                // A3
    SuccessFxSlot,            // A4
    PaladinShieldGlow,        // B1
    PaladinShieldCharge,      // B2
    PaladinWave,              // B3
    AssassinInsightAttempt,   // C1
    AssassinInsightSuccess,   // C2
    TargetFlash,              // E1
}
