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

    /// <summary>② 전력 공급 송전탑 개수 — 인원과 같게(1/2/3). 기존 송전기는 3인 이상 4개(PylonCountFor).</summary>
    public static int RaidPylonCount(int players) => Mathf.Clamp(players, 1, 3);

    /// <summary>② 실패 방전 마스크 = 전 타일 − 안전 칸(부순 송전탑이 선 칸).</summary>
    public static ulong DischargeMask(IReadOnlyList<Vector2Int> safeTiles)
    {
        ulong mask = BossTileGrid.AllMask;
        if (safeTiles == null) return mask;
        for (int i = 0; i < safeTiles.Count; i++)
        {
            Vector2Int t = safeTiles[i];
            if (t.x < 0 || t.y < 0 || t.x >= BossTileGrid.Size || t.y >= BossTileGrid.Size) continue;
            mask &= ~(1UL << BossTileGrid.Bit(t.x, t.y));
        }
        return mask;
    }

    /// <summary>④ 자폭 공동 게이지 — 플레이어 피해 <paramref name="damage"/> 만큼 깎은 뒤 값(0~100). 목표 피해 합이 100%.</summary>
    public static float SelfDestructGaugeAfterDamage(float gauge, int damage, int gaugeDamageTotal)
    {
        if (damage <= 0 || gaugeDamageTotal <= 0) return gauge;
        return Mathf.Clamp(gauge - 100f * damage / gaugeDamageTotal, 0f, 100f);
    }

    /// <summary>
    /// ③ 구출 각본 공격 — 2인 이상 = 잡기(동료가 정면 간파로 구출), 1인 = 점프(→ 점프·점프·점프 콤보, 경석 10-20).
    /// 예전 1인 = 간파 가능 돌진(로아 싱글: 구출 → 단순 돌진).
    /// </summary>
    public static BossAttackId RescueAttackFor(int alivePlayers) =>
        alivePlayers >= 2 ? BossAttackId.Grab : BossAttackId.Jump;

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

    public static float Ratio => _owner != null && !LethalOverride ? _ratio : 0f;

    /// <summary>[서버] 즉사기가 피해를 넣는 동안만 true — 상한을 건너뛴다(④ 자폭 실패 대폭발, 팀장 10-14). 넣은 쪽이 바로 끈다.</summary>
    public static bool LethalOverride { get; set; }

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
