using UnityEngine;

/// <summary>
/// 사거리 안으로 들어온 직후 첫 공격을 늦추는 규칙.
///
/// 문제 — 몹은 <c>attackRange</c> 에 닿는 <b>프레임</b>에 때린다. 쿨다운 초기값이
/// "아주 오래전"(-999)이라 첫 공격은 어떤 게이트도 타지 않는다. 그래서 걸어오다가
/// 사거리에 발을 들이는 순간 선딜만 있고 예고 없이 공격이 나가 "즉시 때린다"로 읽힌다.
/// SpinnerBot 은 그 첫 타가 40% 확률로 돌진 스핀(속도 ×15)이라 특히 두드러졌다.
/// (2026-09-08 팀장 Play 관찰)
///
/// 해법 — 새 상태나 플래그를 만들지 않고 <b>쿨다운 기계를 되감는다</b>.
/// 마지막 사용 시각을 "지금 − (쿨 − 지연)" 으로 적으면 쿨이 정확히 지연 뒤에 풀린다.
///
/// 🔴 새 게이트를 만들지 않은 이유 — "조우 중" 같은 플래그는 해제 경로를 따로 만들어야 하고,
///    그 경로가 하나라도 빠지면 몹이 영구히 공격을 못 하는 흡수 상태가 된다(교훈 #78).
///    쿨다운은 시간이 지나면 스스로 풀리므로 해제를 잊을 수가 없다.
/// </summary>
public static class MonsterEngagePolicy
{
    /// <summary>지연을 걸 필요가 있는가. 0 이하면 기존 동작(사거리에 닿는 프레임에 공격)이다.</summary>
    public static bool ShouldDelay(float delaySeconds) => delaySeconds > 0f;

    /// <summary>
    /// 첫 공격을 <paramref name="delaySeconds"/> 뒤로 미루는 "마지막 사용 시각" 도장값.
    ///
    /// 반환값을 쿨다운 타임스탬프에 그대로 쓰면 <c>now + delaySeconds</c> 에 쿨이 풀린다.
    /// 지연이 쿨보다 길면 도장이 <b>미래</b>가 되는데, <c>now - stamp >= cooldown</c> 식이
    /// 그대로 성립하므로 특별 취급이 필요 없다.
    /// </summary>
    public static float FirstAttackStamp(float now, float effectiveCooldown, float delaySeconds) =>
        now - effectiveCooldown + Mathf.Max(0f, delaySeconds);

    /// <summary>
    /// 지금 "사거리 안"인가. 들어올 때는 <paramref name="attackRange"/>, 나갈 때는
    /// 거기에 <paramref name="exitMargin"/> 을 더한 경계를 쓴다(비대칭 = 히스테리시스).
    ///
    /// 🔴 경계에서 진입 판정이 깜빡이면 그때마다 지연이 다시 걸려 몹이 <b>영원히 못 때린다</b>.
    ///    지연을 거는 트리거가 "진입 순간"이므로 이 히스테리시스는 선택이 아니라 필수다.
    ///    (같은 이유로 재조준 이탈 판정도 attackRange + 0.5 를 쓴다 — MonsterBase 의 선례)
    /// </summary>
    public static bool IsInAttackRange(bool wasInRange, float dist, float attackRange, float exitMargin) =>
        dist <= (wasInRange ? attackRange + Mathf.Max(0f, exitMargin) : attackRange);

    /// <summary>
    /// 사거리 안 어그로 전환(팀장 10-26) — 지금 대상이 사거리 밖이면 사거리 <b>안</b>에 있는 다른 사람을 찾아볼 것인가.
    ///
    /// 왜 — 몬스터는 한 번 문 대상을 놓지 않아, 멀리 있는 대상을 쫓는 동안 바로 옆에서 때리는 사람을
    /// 무시했다(분산 교전에서 GauntletBot 이 제자리에 선 원인 중 하나).
    ///
    /// 🔴 나가는 경계에 <paramref name="exitMargin"/> 을 더한다 — 대상이 사거리 경계에서 깜빡이면
    ///    두 사람 사이를 매 틱 오가게 된다(<see cref="IsInAttackRange"/> 와 같은 히스테리시스).
    /// </summary>
    public static bool ShouldLookForInRangeTarget(float distToTarget, float attackRange, float exitMargin) =>
        distToTarget > attackRange + Mathf.Max(0f, exitMargin);
}
