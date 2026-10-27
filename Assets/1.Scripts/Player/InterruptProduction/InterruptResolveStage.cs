/// <summary>
/// 간파 연출 — 판정 단계(할 일 118). [서버] 판정 결과로 시도 슬로우를 정리한다(빗나감이면 실패 복귀). 판정 자체는 하지 않는다.
/// </summary>
public sealed class InterruptResolveStage
{
    private readonly ITimeScalePort _timeScale;

    public InterruptResolveStage(ITimeScalePort timeScale)
    {
        _timeScale = timeScale;
    }

    /// <summary>[서버] 시도 슬로우가 있었고 카운터를 못 맞혔으면 실패 복귀. 늦게 와도 남의 슬로우는 안 끊는다(번호 비교).</summary>
    public void Server(in InterruptProductionEvent e)
    {
        if (e.AttemptTriggerId == 0 || e.Landed)
            return;

        _timeScale.ServerFail(e.AttemptTriggerId);
    }
}
