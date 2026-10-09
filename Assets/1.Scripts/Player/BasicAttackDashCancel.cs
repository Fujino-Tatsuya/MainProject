/// <summary>
/// 기본 공격 대시 취소 경계(할 일 9, 2026-10-09 확정). 세 캐릭터 공통 규칙:
/// 지금 타(발)의 "다음 평타 입력 창"이 열리기 전이면 공용 대시로 끊을 수 있고, 열린 뒤엔 대시 입력을 무시한다.
/// 판정(Hit)이 이미 나갔어도 창 전이면 끊긴다 — 피해는 그대로, 남은 동작만 끊긴다.
///
/// 판단은 오너가 한다(대시 예측 시작이 오너). 서버는 대시 요청이 오면 공격 상태를 그대로 덮어쓴다
/// (<see cref="PlayerStateController.BeginDash"/>) — 오너가 창 전이라 본 대시가 서버 경합에서도 이긴다.
/// </summary>
public static class BasicAttackDashCancel
{
    /// <summary>
    /// 애니 이벤트형(가붕이·암살자). 이번 타 클립의 ComboWindowOpen 이벤트를 아직 못 받았으면 취소 가능.
    /// 플래그는 타 시작마다 내린다.
    /// </summary>
    public static bool BeforeComboWindowEvent(bool comboWindowOpenedThisStep) => !comboWindowOpenedThisStep;

    /// <summary>
    /// 시간형(거너). 아직 한 발도 안 나갔으면(준비 자세 포함) 취소 가능, 나갔으면 직전 발사 + 창 열림 전까지.
    /// </summary>
    /// <param name="lastShotTime">직전 발사 시각. 아직 없으면 음수.</param>
    public static bool BeforeTimedComboWindow(float now, float lastShotTime, float comboWindowOpen) =>
        lastShotTime < 0f || now < lastShotTime + comboWindowOpen;
}
