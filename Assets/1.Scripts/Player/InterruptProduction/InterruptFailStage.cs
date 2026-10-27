/// <summary>
/// 간파 연출 — 실패 단계(할 일 118). [서버] 판정 전 종료·앵커 누락이면 시도 슬로우를 실패 복귀시킨다.
/// 빗나감은 판정 단계가 처리한다. 피어 실패 연출은 지금 없다(훅만, 설계 Q2-a).
/// </summary>
public sealed class InterruptFailStage
{
    private readonly ITimeScalePort _timeScale;

    public InterruptFailStage(ITimeScalePort timeScale)
    {
        _timeScale = timeScale;
    }

    /// <summary>[서버] 발동 번호가 0 이면(시도 슬로우 없음·이미 결론남·클라 피어) 아무것도 안 한다.</summary>
    public void Server(in InterruptProductionEvent e)
    {
        if (e.AttemptTriggerId == 0)
            return;

        _timeScale.ServerFail(e.AttemptTriggerId);
    }

    /// <summary>
    /// [각 피어] 슬로우 실패 복귀를 받았다. 지금 실패 연출은 없다(훅만). 실패 연출을 만들 땐 주의:
    /// 성공이 예측 대기 밖에서 온 판정은 성공 직전 Failed 가 한 번 난다 — 같은 판정의 성공과 묶어 걸러야 한다.
    /// </summary>
    public void Local(in InterruptProductionEvent e)
    {
    }
}
