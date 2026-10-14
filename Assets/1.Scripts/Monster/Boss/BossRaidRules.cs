using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 23호 레이드 변형(PLAN-boss-raid.md)의 순수 규칙 — Unity 런타임 없이 EditMode 로 검증한다(BossRaidRulesTests).
/// </summary>
public static class BossRaidRules
{
    /// <summary>
    /// 페이즈 <paramref name="fromPhase"/> → <paramref name="toPhase"/> 로 올라갈 때 **지나친 모든 페이즈**의 시퀀스를 순서대로 담는다.
    /// 한 방에 임계 두 개를 넘겨도 기믹이 빠지지 않게 하려는 것(예전엔 bool 하나라 마지막 것만 남았다).
    /// 페이즈 번호는 1부터(phases[i] = 페이즈 i+1). None 은 담지 않는다.
    /// </summary>
    public static void CollectEnteredSequences(
        IReadOnlyList<BossPhaseEntry> phases, int fromPhase, int toPhase, List<BossPhaseSequence> result)
    {
        if (phases == null || result == null) return;
        int last = Mathf.Min(toPhase, phases.Count);
        for (int phase = Mathf.Max(fromPhase, 0) + 1; phase <= last; phase++)
        {
            BossPhaseEntry entry = phases[phase - 1];
            if (entry != null && entry.sequence != BossPhaseSequence.None)
                result.Add(entry.sequence);
        }
    }

    /// <summary>인원별 최대 HP. 배수 표가 비었으면 보정 없음. 인원이 표보다 많으면 마지막 값.</summary>
    public static int PartyScaledMaxHp(int baseMaxHp, IReadOnlyList<float> multipliers, int partySize)
    {
        if (multipliers == null || multipliers.Count == 0 || partySize <= 0) return baseMaxHp;
        int index = Mathf.Clamp(partySize - 1, 0, multipliers.Count - 1);
        float m = Mathf.Max(0.01f, multipliers[index]);
        return Mathf.Max(1, Mathf.RoundToInt(baseMaxHp * m));
    }

    /// <summary>한 번에 받는 피해 상한. ratio ≤ 0 이면 상한 없음.</summary>
    public static int CapHitDamage(int damage, int receiverMaxHp, float ratio)
    {
        if (ratio <= 0f || receiverMaxHp <= 0) return damage;
        int cap = Mathf.Max(1, Mathf.FloorToInt(receiverMaxHp * ratio));
        return Mathf.Min(damage, cap);
    }
}

/// <summary>
/// 레이드 보스가 싸우는 동안만 켜지는 "플레이어 단일 피해 상한"(서버 전용 상태).
/// 보스 피해 생성부가 8곳 이상으로 흩어져 있어(근접·잡기·점프·돌진·장판·드론·존) 수신부 한 곳에서 건다 — Player.ReceiveAttack.
/// 보스방 안에서는 보스 계열만 피해를 주므로 출처 구분 없이 건다.
/// </summary>
public static class PlayerHitDamageCap
{
    static Object _owner;
    static float _ratio;

    public static float Ratio => _owner != null ? _ratio : 0f;

    public static void Set(Object owner, float ratio)
    {
        _owner = ratio > 0f ? owner : null;
        _ratio = ratio;
    }

    /// <summary>켠 주인만 끌 수 있다(다른 보스가 실수로 끄지 않게).</summary>
    public static void Clear(Object owner)
    {
        if (_owner == owner) { _owner = null; _ratio = 0f; }
    }
}
