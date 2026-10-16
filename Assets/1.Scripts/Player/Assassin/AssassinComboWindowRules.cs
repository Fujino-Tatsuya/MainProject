/// <summary>
/// 어쌔신 평타 다음 타 연결(예약 창 + 홀드)의 순수 규칙(할 일 47 연장·정정). Unity 의존 없이 EditMode 로 검증한다.
/// 창은 클립 정규화 시간(0~1)이다 — 가붕이 ComboWindowOpen~Close 클립 이벤트와 같은 의미라 재생 속도가 바뀌어도 클립 위 위치가 그대로다.
/// </summary>
public static class AssassinComboWindowRules
{
    /// <summary>클립 재생 시작부터 지난 시간 → 정규화 진행도. 재생 시간이 없으면 -1(창 밖).</summary>
    public static float NormalizedProgress(float elapsedSeconds, float motionDuration)
    {
        if (motionDuration <= 0f)
            return -1f;

        return elapsedSeconds / motionDuration;
    }

    /// <summary>정규화 진행도가 창 [open, close] 안인가. 양 끝 포함, close 가 open 보다 앞이면 창 없음.</summary>
    public static bool IsInside(float normalizedTime, float open, float close)
    {
        return open <= close && normalizedTime >= open && normalizedTime <= close;
    }

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
