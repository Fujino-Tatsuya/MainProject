using System;

public readonly struct FloatingDamageAccumulationKey : IEquatable<FloatingDamageAccumulationKey>
{
    readonly ulong _attackerClientId;
    readonly AttackType _attackType;
    readonly int _targetId;
    readonly PopupKind _kind;
    readonly bool _suppressHaste;

    public FloatingDamageAccumulationKey(
        ulong attackerClientId, AttackType attackType, int targetId, PopupKind kind, bool suppressHaste = false)
    {
        _attackerClientId = attackerClientId;
        _attackType = attackType;
        _targetId = targetId;
        _kind = kind;
        _suppressHaste = suppressHaste;
    }

    public bool Equals(FloatingDamageAccumulationKey other)
    {
        return _attackerClientId == other._attackerClientId &&
               _attackType == other._attackType &&
               _targetId == other._targetId &&
               _kind == other._kind &&
               _suppressHaste == other._suppressHaste;
    }

    public override bool Equals(object obj)
    {
        return obj is FloatingDamageAccumulationKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = _attackerClientId.GetHashCode();
            hash = (hash * 397) ^ (int)_attackType;
            hash = (hash * 397) ^ _targetId;
            hash = (hash * 397) ^ (int)_kind;
            return (hash * 397) ^ (_suppressHaste ? 1 : 0);
        }
    }
}

/// <summary>숫자 하나가 어떻게 누적되는가.</summary>
public enum FloatingDamageAccumulation
{
    None,          // 단발 — 타격마다 따로
    Multi,         // 공격자·공격 종류·대상·숫자 종류별 합산(Multi 공격)
    SuppressHaste  // 제압 가속 중 — 공격 종류·Multi 여부 무관, 공격자·대상·숫자 종류별 하나(할 일 174)
}

public static class FloatingDamageAccumulationPolicy
{
    /// <summary>공격자에게 제압 가속이 켜져 있으면 무조건 합산, 아니면 Multi 만.</summary>
    public static FloatingDamageAccumulation Resolve(AttackHitPattern hitPattern, bool attackerSuppressHaste)
    {
        if (attackerSuppressHaste)
            return FloatingDamageAccumulation.SuppressHaste;

        return hitPattern == AttackHitPattern.Multi
            ? FloatingDamageAccumulation.Multi
            : FloatingDamageAccumulation.None;
    }

    /// <summary>
    /// 숫자 종류까지 본 누적 방식. 힐 숫자(268)는 늘 힐한 사람·대상별 합산(Multi)이고 제압 가속 합산은 타지 않는다
    /// (가속은 피해 연출 — 힐 숫자가 가속 끝까지 붙잡히지 않게). 나머지는 <see cref="Resolve"/>.
    /// </summary>
    public static FloatingDamageAccumulation Resolve(PopupKind kind, AttackHitPattern hitPattern, bool attackerSuppressHaste)
    {
        return kind == PopupKind.Heal
            ? FloatingDamageAccumulation.Multi
            : Resolve(hitPattern, attackerSuppressHaste);
    }

    public static bool TryCreateKey(
        ulong attackerClientId,
        AttackType attackType,
        int targetId,
        PopupKind kind,
        AttackHitPattern hitPattern,
        out FloatingDamageAccumulationKey key)
    {
        return TryCreateKey(attackerClientId, attackType, targetId, kind,
            Resolve(hitPattern, false), out key);
    }

    public static bool TryCreateKey(
        ulong attackerClientId,
        AttackType attackType,
        int targetId,
        PopupKind kind,
        FloatingDamageAccumulation accumulation,
        out FloatingDamageAccumulationKey key)
    {
        switch (accumulation)
        {
            case FloatingDamageAccumulation.Multi:
                key = new FloatingDamageAccumulationKey(attackerClientId, attackType, targetId, kind);
                return true;
            case FloatingDamageAccumulation.SuppressHaste:
                // 공격 종류는 묻지 않는다(고정값). 표지로 같은 공격자·대상의 Multi 키와 겹치지 않게 한다.
                // 숫자 종류는 그대로 가른다 — 보호막 숫자는 색·위치가 달라 HP 숫자와 따로 합산한다.
                key = new FloatingDamageAccumulationKey(attackerClientId, AttackType.Default, targetId, kind,
                    suppressHaste: true);
                return true;
            default:
                key = default;
                return false;
        }
    }

    /// <summary>
    /// 제압 가속 합산 숫자의 "마지막 타격 시각" — 가속이 켜져 있는 동안은 매 프레임 지금으로 밀어 사라지지 않게 붙잡는다.
    /// 가속이 끝나면 더 밀지 않아 그 순간부터 평소처럼 유지(accumulateHold) → 흐려짐.
    /// </summary>
    public static float HeldLastHitAt(float lastHitAt, float elapsed, bool suppressHasteActive) =>
        suppressHasteActive && elapsed > lastHitAt ? elapsed : lastHitAt;
}
