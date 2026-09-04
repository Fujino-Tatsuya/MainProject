using UnityEngine;

/// <summary>
/// 접촉 공격(훅·어퍼·잡기)의 <b>개시 거리</b> 판정.
///
/// 왜 필요한가 (2026-09-03 팀장 Play 관찰 — "가만히 선 플레이어를 훅 거리도 안 됐는데 때린다") —
/// <c>SeekBoss</c> 는 슬롯이 잡히는 <b>즉시</b> 멈춰서 때린다. 그런데 훅 행의 거리창
/// (<c>maxDistance</c> 3.2)이 <c>attackRange</c>(2.0)보다 넓어서, 먼 대상에게 걸어 들어가는 도중
/// 3.2m 를 지나는 순간 훅이 시작됐다. 히트박스는 손 본에 붙은 2.6m 큐브(반 1.3m)라 그 거리엔
/// 닿지 않는다 → <b>허공 훅</b>. 어그로 재선정이 들어오면서 "먼 대상에게 걸어 들어가는" 구간이
/// 상시 생겨 매번 드러났다.
///
/// 🔴 그래서 거리창을 좁히는 것만으로는 안 된다 — 좁히면 "뒤로 빠지는 플레이어를 쫓아 치는"
///    저작 의도가 함께 죽는다. <b>개시</b>와 <b>유지</b>를 분리한다:
///    개시는 <c>attackRange</c> 안까지 걸어 들어간 뒤에만, 일단 붙은 뒤에는 행의 <c>maxDistance</c> 까지.
///
/// ⚠️ 접촉 공격만의 규칙이다. 돌진(<c>minDistance</c> 5)·점프(<c>ignoreDistanceWindow</c>)처럼
///    <b>멀리서 들어가는</b> 공격은 이 게이트에 걸리지 않는다 — 걸면 그 둘이 아예 못 나간다.
/// </summary>
public static class BossContactReachPolicy
{
    /// <summary>
    /// 이 행이 <b>접촉 공격</b>인가 — 붙어서 때리는 공격(훅·어퍼·잡기)이면 true.
    ///
    /// 판별 기준은 "거리창을 쓰면서 <c>attackRange</c> 안에서도 성립하는가"다. 원거리 진입기는
    /// 거리창을 무시하거나(점프) 최소 거리가 <c>attackRange</c> 보다 멀어서(돌진) 여기서 빠진다.
    /// 즉 <b>새 공격을 저작해도 분류가 자동으로 따라온다</b> — 공격 ID 를 나열하지 않는 이유다.
    /// </summary>
    public static bool IsContactRow(BossAttackEntry entry, float attackRange)
    {
        if (entry == null) return false;
        if (entry.ignoreDistanceWindow) return false;
        return entry.minDistance <= attackRange;
    }

    /// <summary>
    /// 이 행을 지금 고를 수 있는 <b>상한 거리</b>.
    ///
    /// 접촉 행이고 아직 안 붙었으면 <c>attackRange</c> 까지로 좁힌다(= 걸어 들어간 뒤에만 개시).
    /// 붙은 뒤이거나 접촉 행이 아니면 저작값 그대로다.
    ///
    /// ⚠️ <c>Min</c> 인 이유 — 저작이 <c>attackRange</c> 보다 <b>더 좁게</b> 잡은 행(잡기 2.2 등)은
    ///    그 좁은 값을 그대로 지켜야 한다. <c>attackRange</c> 로 갈아치우면 거리창이 조용히 넓어진다.
    /// </summary>
    public static float EffectiveMaxDistance(BossAttackEntry entry, float attackRange, bool inReach)
    {
        if (entry == null) return 0f;
        if (inReach || !IsContactRow(entry, attackRange)) return entry.maxDistance;
        return Mathf.Min(entry.maxDistance, attackRange);
    }

    /// <summary>
    /// 접촉 사거리 <b>진입/이탈</b> 판정(히스테리시스).
    ///
    /// 🔴 경계 하나로 판정하면 <c>attackRange</c> 근처에서 매 틱 진입·이탈이 뒤집혀 훅이 나갔다
    ///    말았다 한다. 진입은 <c>attackRange</c>, 이탈은 <c>exitDistance</c>(접촉 행 거리창의 최댓값)로
    ///    따로 둔다 — <c>MonsterBase</c> 의 공격 취소 히스테리시스(+0.5)와 같은 관용구다.
    /// </summary>
    /// <param name="inReach">지금 붙어 있는 상태인가(직전 판정 결과).</param>
    /// <param name="dist">타깃까지 거리.</param>
    /// <param name="attackRange">진입 경계 = SO 의 <c>attackRange</c>.</param>
    /// <param name="exitDistance">이탈 경계. <c>attackRange</c> 보다 작게 들어오면 무시한다(진입 즉시 이탈 방지).</param>
    public static bool StaysInReach(bool inReach, float dist, float attackRange, float exitDistance)
    {
        if (dist <= attackRange) return true;
        if (dist > Mathf.Max(attackRange, exitDistance)) return false;
        return inReach;
    }
}
