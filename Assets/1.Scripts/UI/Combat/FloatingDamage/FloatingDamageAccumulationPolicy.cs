using System;

public readonly struct FloatingDamageAccumulationKey : IEquatable<FloatingDamageAccumulationKey>
{
    readonly ulong _attackerClientId;
    readonly AttackType _attackType;
    readonly int _targetId;
    readonly PopupKind _kind;

    public FloatingDamageAccumulationKey(
        ulong attackerClientId, AttackType attackType, int targetId, PopupKind kind)
    {
        _attackerClientId = attackerClientId;
        _attackType = attackType;
        _targetId = targetId;
        _kind = kind;
    }

    public bool Equals(FloatingDamageAccumulationKey other)
    {
        return _attackerClientId == other._attackerClientId &&
               _attackType == other._attackType &&
               _targetId == other._targetId &&
               _kind == other._kind;
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
            return (hash * 397) ^ (int)_kind;
        }
    }
}

public static class FloatingDamageAccumulationPolicy
{
    public static bool TryCreateKey(
        ulong attackerClientId,
        AttackType attackType,
        int targetId,
        PopupKind kind,
        AttackHitPattern hitPattern,
        out FloatingDamageAccumulationKey key)
    {
        if (hitPattern != AttackHitPattern.Multi)
        {
            key = default;
            return false;
        }

        key = new FloatingDamageAccumulationKey(attackerClientId, attackType, targetId, kind);
        return true;
    }
}
