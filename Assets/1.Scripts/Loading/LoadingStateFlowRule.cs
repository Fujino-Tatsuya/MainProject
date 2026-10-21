/// <summary>
/// 클라가 서버의 로딩 상태 메시지(flowId·phase)를 받을지 — 순수 함수(EditMode 테스트 대상).
/// flowId 는 서버 <see cref="NetworkLoadingFlowController"/> 가 로딩 흐름마다 1씩 올리는 번호다. 같은 세션 안에서만 의미가 있다.
/// <list type="bullet">
/// <item>같은 세션: 지난 흐름(flowId 가 더 작은) 메시지는 버린다 — 늦게 도착한 옛 상태가 새 흐름을 덮지 않게.</item>
/// <item>새 세션(StartClient 마다): 클라 쪽 번호를 <see cref="SessionStartFlowId"/> 로 되돌린다. 서버 exe 가 다시 켜지면 번호가 1부터라,
/// 되돌리지 않으면 앞 세션에서 더 큰 번호를 본 클라가 새 서버의 상태를 전부 버린다.</item>
/// </list>
/// </summary>
public static class LoadingStateFlowRule
{
    /// <summary>새 세션 시작 때 클라 flowId. 서버의 첫 흐름은 1이다.</summary>
    public const uint SessionStartFlowId = 0;

    /// <summary>이 상태 메시지를 버린다(지난 흐름).</summary>
    public static bool IsStale(uint messageFlowId, uint currentFlowId)
    {
        return messageFlowId < currentFlowId;
    }
}
