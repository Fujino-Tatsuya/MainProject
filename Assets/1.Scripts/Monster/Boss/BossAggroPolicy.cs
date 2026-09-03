/// <summary>
/// 주기 어그로 재선정 판정(23호 전용 정책).
///
/// 왜 필요한가 — base 의 타깃 락온은 <b>사망·디스폰·리쉬</b> 로만 풀린다. 거리·시간·위협도가
/// 전혀 없어서 3인전에서 보스가 처음 문 한 명만 끝까지 문다. 주기적으로 다시 고르게 해
/// 압박 대상이 돌아가게 한다(위협도 누적은 8월 확장 — PLAN 확정 A).
///
/// 🔴 이 규칙의 핵심은 "언제 바꾸는가"가 아니라 <b>"언제 절대 안 바꾸는가"</b> 다.
///    공격 도중 타깃이 바뀌면 조준·잡기 체인·돌진 방향이 함께 흔들린다 — 그래서
///    교전 중 <c>Idle</c>/<c>Chase</c> 에서만 성립시킨다.
///
/// ⚠️ 어그로는 <b>피해 분배를 바꾸지 않는다.</b> 이 프로젝트에서 "누가 맞는가"는 공간 판정이
///    따로 정한다(훅은 히트박스에 겹친 전원, Grab 은 포획 순간 반경 내 최근접, Dash 는 경로에
///    먼저 걸린 사람). 여기서 바뀌는 것은 보스의 위치·시선·압박 방향뿐이다.
/// </summary>
public static class BossAggroPolicy
{
    /// <param name="state">지금 몬스터 상태.</param>
    /// <param name="secondsSinceLastRetarget">마지막 재선정 이후 경과(초).</param>
    /// <param name="interval">재선정 간격(초). 0 이하면 기능을 끈다.</param>
    public static bool ShouldRetarget(MonsterState state, float secondsSinceLastRetarget, float interval)
    {
        // 0 이하는 "끔"이다. 이 가드가 없으면 저작으로 껐을 때 매 틱 재선정이 돈다.
        if (interval <= 0f) return false;

        // 교전 중 이동/대기 구간에서만. 나머지는 전부 커밋 구간이거나 타깃이 의미 없는 상태다.
        if (state != MonsterState.Idle && state != MonsterState.Chase) return false;

        return secondsSinceLastRetarget >= interval;
    }
}
