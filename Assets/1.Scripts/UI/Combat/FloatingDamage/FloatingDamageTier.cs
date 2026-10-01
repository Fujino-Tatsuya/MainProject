using System.Collections.Generic;
using UnityEngine;

// 피해 강도. 숫자 크기와 움직임을 고른다(색은 피해 유형이 정한다 — 강도로 바꾸지 않는다).
// 순서가 곧 강도라 비교 연산(tier > current)에 쓴다. 값은 끝에만 추가.
public enum FloatingDamageTier
{
    Low,
    Mid,
    High
}

public static class FloatingDamageTierPolicy
{
    /// <summary>
    /// 피해 비율 = 개별 타격의 최종 피해 ÷ 기준 최대 체력 × 100. 경계값은 위 구간에 넣는다("이상").
    /// 누적액이 아니라 **한 타격**으로 판정한다 — 누적 숫자의 크기는 이 결과의 최댓값을 유지한다.
    /// </summary>
    public static FloatingDamageTier Classify(int amount, int baseMaxHp, float midPercent, float highPercent)
    {
        if (amount <= 0 || baseMaxHp <= 0)
            return FloatingDamageTier.Low;

        float percent = amount * 100f / baseMaxHp;
        if (percent >= highPercent) return FloatingDamageTier.High;
        if (percent >= midPercent) return FloatingDamageTier.Mid;
        return FloatingDamageTier.Low;
    }
}

/// <summary>
/// 높은 피해 강조(큰 확대 + 흔들림)의 최소 간격. 같은 공격자·같은 대상 기준.
/// 간격에 걸려도 숫자 값과 크기는 정상 반영하고 강조만 생략한다 — 이 게이트는 연출만 막는다.
/// </summary>
public sealed class FloatingDamageEmphasisGate
{
    const int PruneThreshold = 64;

    readonly Dictionary<(ulong attacker, int target), float> _lastEmphasisTime = new();
    readonly List<(ulong, int)> _expired = new();

    public bool TryConsume(ulong attackerClientId, int targetId, float now, float interval)
    {
        var key = (attackerClientId, targetId);
        if (_lastEmphasisTime.TryGetValue(key, out float last) && now - last < interval)
            return false;

        _lastEmphasisTime[key] = now;
        if (_lastEmphasisTime.Count > PruneThreshold)
            Prune(now, interval);
        return true;
    }

    // 대상이 계속 바뀌는 전투에서 키가 쌓이지 않게, 간격이 지난 항목은 지워도 결과가 같다.
    void Prune(float now, float interval)
    {
        _expired.Clear();
        foreach (KeyValuePair<(ulong, int), float> pair in _lastEmphasisTime)
        {
            if (now - pair.Value >= interval)
                _expired.Add(pair.Key);
        }

        for (int i = 0; i < _expired.Count; i++)
            _lastEmphasisTime.Remove(_expired[i]);
    }
}
