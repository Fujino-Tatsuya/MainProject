/// <summary>
/// 인터럽트 슬로우 모션 예측 대상(서버 전용). 인터럽트 스킬이 승인 시점에 "지금 인터럽트 가능한가"로 유효타를 예측하고,
/// 판정 직전·직후의 성공 수를 비교해 "이 타격으로 인터럽트가 성공했는가"를 확인한다. (PLAN-interrupt-slowmo S3)
///
/// Unit 과 같은 GameObject 에 붙인다 — 스킬은 판정 콜라이더에서 해석한 Unit 에서 찾는다.
/// 구현하지 않은 대상(몬스터 — D15 범위 밖)은 예측에 걸리지 않아 슬로우가 없다.
/// </summary>
public interface IInterruptSlowMotionTarget
{
    /// <summary>[서버] 지금 인터럽트 공격을 받으면 성공하는가.</summary>
    bool ServerIsInterruptible { get; }

    /// <summary>
    /// [서버] 지금까지의 인터럽트 성공 수. 값 자체엔 의미가 없고, 한 타격의 수신 전후로 비교해 증가했으면 그 타격이 성공한 것이다.
    /// </summary>
    int ServerInterruptSuccessCount { get; }
}
