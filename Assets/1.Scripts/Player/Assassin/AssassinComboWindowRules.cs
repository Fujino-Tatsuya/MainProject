/// <summary>
/// 어쌔신 평타 다음 타 예약 창의 순수 규칙(할 일 47 연장). Unity 의존 없이 EditMode 로 검증한다.
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
}
