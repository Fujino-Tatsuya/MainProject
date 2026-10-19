/// <summary>
/// 어쌔신 평타 다음 타 연결(예약 창 + 홀드)의 순수 규칙(할 일 47 연장·정정). Unity 의존 없이 EditMode 로 검증한다.
/// 창 자체는 클립 ComboWindowOpen~Close 이벤트다(가붕이와 같다, PLAN-anim-event-timing P2-1) — 여기는 End 순간 잇기 규칙만.
/// </summary>
public static class AssassinComboWindowRules
{
    /// <summary>
    /// 한 타의 End 순간 다음 타로 잇는가(할 일 47 정정, 거너 기본 공격과 같은 규칙) — 창 안 클릭 예약이 있거나 지금 좌클릭이 눌려 있으면.
    /// 눌림은 "처음 누른 입력을 한 번도 떼지 않았는가"가 아니라 End 순간의 상태다 — 뗐다 다시 눌러 들고 있어도 잇는다.
    /// </summary>
    public static bool ShouldContinueAtEnd(bool queued, bool held)
    {
        return queued || held;
    }

    /// <summary>
    /// [서버] 오너가 알린 눌림이 아직 유효한가. 눌린 동안 오는 주기 신호가 <paramref name="staleAfterSeconds"/> 넘게 끊기면
    /// 안 눌림으로 본다(연결 문제로 뗌 신호를 놓쳐도 무한히 이어지지 않게, 거너 StallIntervals 와 같은 안전장치).
    /// </summary>
    public static bool IsHoldFresh(bool held, float lastSignalTime, float now, float staleAfterSeconds)
    {
        return held && now - lastSignalTime <= staleAfterSeconds;
    }
}
