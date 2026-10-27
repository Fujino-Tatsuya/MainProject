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
}
