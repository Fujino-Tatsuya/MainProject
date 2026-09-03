/// <summary>
/// 개전(보스방 진입 직후) 억제 대상 공격을 고르는 규칙.
///
/// 문제 — 전투가 시작될 때 플레이어는 멀리 있다. 그러면 거리창 게이트를 통과하는 행이
/// Jump(거리 무관)와 Dash(5~20m) <b>둘뿐</b>이라, 가중치 룰렛은 반드시 그중 하나를 뽑는다.
/// 확률이 아니라 <b>후보 집합의 구조</b>라서 가중치를 낮춰도 없어지지 않는다.
/// 그래서 개전 첫 인상이 매번 "등장하자마자 돌진/점프"가 됐다.
///
/// 해법 — 이 둘만 전투 시작 시 "방금 쓴 것"으로 표시해 각자의 쿨(Dash 5초 · Jump 10초)이
/// 게이트가 되게 한다. 그동안 후보가 0이라 보스는 추격으로 빠져 걸어 들어가고, 근접 거리에
/// 닿으면 훅이 나간다. 쿨이 풀릴 때쯤엔 이미 근접이라 Dash(≥5m)는 플레이어가 물러나야 나온다.
///
/// 🔴 새 상태를 만들지 않은 이유 — "개전 동안 근접만" 같은 플래그는 플레이어가 계속 도망치면
///    풀릴 길이 없어 흡수 상태가 된다(교훈 #78). 쿨다운은 시간이 지나면 <b>스스로</b> 풀린다.
/// </summary>
public static class BossOpeningAttackPolicy
{
    /// <summary>
    /// 개전에 억제할 "원거리 진입기"인가.
    ///
    /// 🔴 <c>minDistance</c> 만 보면 Jump 를 놓친다 — Jump 는 minDistance 가 0 이면서
    ///    <c>ignoreDistanceWindow</c> 로 거리창을 통째로 건너뛴다. 두 조건을 함께 봐야 한다.
    /// </summary>
    public static bool IsRangedOpener(BossAttackEntry entry) =>
        entry != null && (entry.ignoreDistanceWindow || entry.minDistance > 0f);
}
