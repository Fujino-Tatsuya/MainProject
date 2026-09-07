using UnityEngine;

/// <summary>
/// 카운터 성공 한 번의 결과 — 다음 카운트, Break 여부, <b>전체 행동 불능 시간</b>.
///
/// 🔴 <see cref="Duration"/> 은 총 시간이다. 앞에 Hit 리액션 시간을 따로 더하지 않는다
///    (설계 §3.3 — SO 에 0.5 를 넣었으면 실제로도 0.5 여야 한다).
/// </summary>
public readonly struct BossCounterOutcome
{
    public int NextCount { get; }
    public bool IsBreak { get; }
    public float Duration { get; }

    public BossCounterOutcome(int nextCount, bool isBreak, float duration) =>
        (NextCount, IsBreak, Duration) = (nextCount, isBreak, duration);
}

/// <summary>
/// 카운터 누적 판정. 상태를 갖지 않는 순수 함수라 EditMode 로 전 경계를 고정한다.
/// 실제 카운트 보관과 상태 전이는 호출측(<c>TwentyThreeBoss</c>)의 몫이다.
/// </summary>
public static class BossCounterProgress
{
    /// <param name="current">지금까지 누적된 카운터 성공 횟수.</param>
    /// <param name="threshold">Break 로 승격할 임계(<c>MonsterDataSO.maxGroggyCount</c>).</param>
    /// <param name="allowBreak">
    /// Break 승격을 허용하는가. 카운터 성공은 true.
    /// 송전기 실패(S7)는 <b>false</b> — 카운트는 올리되 Break 로 가지 않는다.
    /// 페이즈 전환 직후에 장시간 무력화가 겹치면 연출이 죽기 때문(확정 스펙).
    /// </param>
    public static BossCounterOutcome Resolve(int current, int threshold, bool allowBreak,
        float groggyDuration, float breakDuration)
    {
        // 음수 방어 — 카운트는 외부(네트워크 변수·저작값)에서 오므로 음수가 들어올 수 있다.
        int next = Mathf.Max(0, current) + 1;

        // >= 인 이유: 임계 1 이면 첫 성공이 곧 Break 여야 한다. > 로 쓰면 임계가 1 만큼 밀린다.
        bool isBreak = allowBreak && next >= Mathf.Max(1, threshold);

        // Break 하면 카운트를 0 으로 되돌린다(다음 사이클 시작).
        return new BossCounterOutcome(
            isBreak ? 0 : next,
            isBreak,
            // 0 이면 그 상태에서 못 빠져나오므로 최소값을 둔다. 저작 실수의 안전망이다.
            Mathf.Max(0.05f, isBreak ? breakDuration : groggyDuration));
    }
}
