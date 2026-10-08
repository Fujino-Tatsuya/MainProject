using System;
using System.Collections.Generic;

/// <summary>
/// 사운드 키별 동시 재생 수와 재트리거 쿨다운을 판정하는 순수 런타임 상태.
/// 시간은 외부에서 받아 EditMode 테스트와 런타임이 같은 경계 규칙을 쓴다.
/// </summary>
public sealed class SoundPlaybackLimiter
{
    private sealed class State
    {
        public int ActiveCount;
        public double LastStartedAt = double.NegativeInfinity;
    }

    private readonly Dictionary<string, State> _states =
        new Dictionary<string, State>(StringComparer.Ordinal);

    /// <summary>
    /// 제한 안이면 재생 슬롯을 하나 예약한다. 최대 동시 수 0 이하와 쿨다운 0 이하는 제한 없음이다.
    /// 쿨다운 경계 시각(<c>now == last + cooldown</c>)은 허용한다.
    /// </summary>
    public bool TryAcquire(string key, double now, int maxSimultaneous, double retriggerCooldown)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (!_states.TryGetValue(key, out State state))
        {
            state = new State();
            _states.Add(key, state);
        }

        if (maxSimultaneous > 0 && state.ActiveCount >= maxSimultaneous)
        {
            return false;
        }

        if (retriggerCooldown > 0d && now < state.LastStartedAt + retriggerCooldown)
        {
            return false;
        }

        state.ActiveCount++;
        state.LastStartedAt = now;
        return true;
    }

    /// <summary>재생 종료 콜백에서 예약한 동시 재생 슬롯 하나를 반환한다.</summary>
    public void Release(string key)
    {
        if (key == null || !_states.TryGetValue(key, out State state))
        {
            return;
        }

        state.ActiveCount = Math.Max(0, state.ActiveCount - 1);
    }

    public int ActiveCount(string key) =>
        key != null && _states.TryGetValue(key, out State state) ? state.ActiveCount : 0;
}
