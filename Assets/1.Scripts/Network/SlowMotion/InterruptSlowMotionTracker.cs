/// <summary>
/// 인터럽트 스킬 한 번의 슬로우 모션 발동 기록(서버). 승인 시 예측으로 발동한 슬로우를, 판정에서 빗나가거나
/// 판정 전에 스킬이 끝나면 실패 복귀시킬 발동 번호로 돌려준다. (PLAN-interrupt-slowmo S3 — D1·D3)
///
/// 순수 C# 이다. 실제 발동·실패는 호출측이 <see cref="GlobalTimeScale"/> 에 넘긴다 — 여기서 돌려주는 번호가 0 이면 아무것도 안 한다.
/// 판정과 조기 종료 중 먼저 온 쪽 한 번만 결론을 낸다.
/// </summary>
public sealed class InterruptSlowMotionTracker
{
    bool _settled = true;

    /// <summary>이번 발동의 슬로우 발동 번호. 0 = 슬로우를 시작하지 않았다(예측 실패·레이어 거부).</summary>
    public uint TriggerId { get; private set; }

    /// <summary>슬로우를 시작했고 아직 결론(판정·종료)이 나지 않았다.</summary>
    public bool IsPending => TriggerId != 0 && !_settled;

    /// <summary>스킬 승인. 이전 발동의 기록을 버린다.</summary>
    public void Begin()
    {
        TriggerId = 0;
        _settled = false;
    }

    /// <summary>예측 통과로 슬로우를 발동했다. 레이어가 거부해 0 이 오면 시작하지 않은 것으로 본다.</summary>
    public void Started(uint triggerId)
    {
        if (!_settled)
            TriggerId = triggerId;
    }

    /// <summary>
    /// 판정 끝. 인터럽트가 하나도 성공하지 않았으면 실패 복귀할 발동 번호를, 아니면 0 을 돌려준다.
    /// 성공이면 슬로우는 정상 복귀 단계로 흐른다.
    /// </summary>
    public uint Resolve(bool interruptLanded) => Settle(!interruptLanded);

    /// <summary>스킬 종료(완료·취소·사망). 판정 전에 끝났으면 실패 복귀할 발동 번호를, 아니면 0 을 돌려준다.</summary>
    public uint Abort() => Settle(true);

    uint Settle(bool fail)
    {
        if (_settled)
            return 0;

        _settled = true;
        return fail ? TriggerId : 0;
    }
}
