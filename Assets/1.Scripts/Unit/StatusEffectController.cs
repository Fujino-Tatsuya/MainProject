using System;
using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 상태이상 한 건. 재적용 키는 (type, sourceId) — 같은 키는 시간·수치 갱신, 다른 출처는 병존.
/// </summary>
public struct StatusEffectInstance : INetworkSerializable, IEquatable<StatusEffectInstance>
{
    public StatusEffectType type;
    public float magnitude;         // 스탯 modifier 배율 (차단류는 의미 없음, 1)
    public float duration;          // 0 이하 = 수동 해제 전까지 유지
    public double appliedServerTime; // ServerTime 기준 (피어 간 시계 차이 대응)
    public ulong sourceId;          // 출처 (거는 쪽 NetworkObjectId 등)

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref type);
        serializer.SerializeValue(ref magnitude);
        serializer.SerializeValue(ref duration);
        serializer.SerializeValue(ref appliedServerTime);
        serializer.SerializeValue(ref sourceId);
    }

    public bool Equals(StatusEffectInstance other)
    {
        return type == other.type &&
            magnitude == other.magnitude &&
            duration == other.duration &&
            appliedServerTime == other.appliedServerTime &&
            sourceId == other.sourceId;
    }
}

/// <summary>
/// Unit 공통 상태이상(버프/디버프) 컨트롤러 — 서버 권위.
/// 서버만 리스트를 쓰고(Apply/Remove/만료 스윕) NetworkList가 전 피어에 동기화되며,
/// 집계(차단 = OR, 스탯 배율 = 곱)는 각 피어가 로컬에서 계산한다.
/// </summary>
public class StatusEffectController : BaseNetworkBehaviour
{
    // 타입 → 차단 매핑 테이블. 차단 규칙 변경은 여기 한 곳만 고친다.
    private const StatusEffectType MovementBlockers =
        StatusEffectType.Stunned | StatusEffectType.Rooted | StatusEffectType.Airborne;
    private const StatusEffectType AttackBlockers =
        StatusEffectType.Stunned | StatusEffectType.Airborne;
    private const StatusEffectType InterruptBlockers =
        StatusEffectType.Stunned | StatusEffectType.Rooted | StatusEffectType.Airborne | StatusEffectType.Debilitated;
    private const StatusEffectType SkillBlockers =
        StatusEffectType.Stunned | StatusEffectType.Silenced | StatusEffectType.Airborne;

    private readonly NetworkList<StatusEffectInstance> effects = new NetworkList<StatusEffectInstance>();

    // NetworkList 쓰기는 스폰된 서버에서만 유효 (오프라인 실행에서는 상태이상이 걸리지 않는다 — Unit 스탯 계열과 동일)
    private bool CanWrite => IsSpawned && IsServer;

    public StatusEffectType ActiveEffects
    {
        get
        {
            StatusEffectType flags = StatusEffectType.None;
            for (int i = 0; i < effects.Count; i++)
                flags |= effects[i].type;

            return flags;
        }
    }

    public bool Has(StatusEffectType type)
    {
        return (ActiveEffects & type) != 0;
    }

    public bool BlocksMovement => Has(MovementBlockers);
    public bool BlocksAttack => Has(AttackBlockers);
    public bool BlocksInterrupt => Has(InterruptBlockers);
    public bool BlocksSkill => Has(SkillBlockers);
    public bool HasSuperArmor => Has(StatusEffectType.SuperArmor);

    /// <summary>활성 인스턴스 중 statType에 해당하는 배율의 곱. 없으면 1.</summary>
    public float GetStatMultiplier(StatusEffectType statType)
    {
        float multiplier = 1f;
        for (int i = 0; i < effects.Count; i++)
        {
            if ((effects[i].type & statType) != 0)
                multiplier *= Mathf.Max(0f, effects[i].magnitude);
        }

        return multiplier;
    }

    /// <summary>차단류 등 수치 없는 효과 적용 (배율 1).</summary>
    public void Apply(StatusEffectType type, float duration, ulong sourceId)
    {
        Apply(type, 1f, duration, sourceId);
    }

    /// <summary>서버 전용. 같은 (type, sourceId)가 있으면 시간·수치를 갱신하고, 없으면 병존 추가한다.</summary>
    public void Apply(StatusEffectType type, float magnitude, float duration, ulong sourceId)
    {
        if (!CanWrite || type == StatusEffectType.None)
            return;

        StatusEffectInstance instance = new StatusEffectInstance
        {
            type = type,
            magnitude = magnitude,
            duration = duration,
            appliedServerTime = NetworkManager.ServerTime.Time,
            sourceId = sourceId
        };

        int index = IndexOf(type, sourceId);
        if (index >= 0)
            effects[index] = instance;
        else
            effects.Add(instance);
    }

    /// <summary>서버 전용. 특정 출처가 건 특정 타입 해제.</summary>
    public bool Remove(StatusEffectType type, ulong sourceId)
    {
        if (!CanWrite)
            return false;

        int index = IndexOf(type, sourceId);
        if (index < 0)
            return false;

        effects.RemoveAt(index);
        return true;
    }

    /// <summary>서버 전용. 특정 출처가 건 효과 전부 해제 (스킬 종료 정리용).</summary>
    public int RemoveBySource(ulong sourceId)
    {
        if (!CanWrite)
            return 0;

        int removed = 0;
        for (int i = effects.Count - 1; i >= 0; i--)
        {
            if (effects[i].sourceId == sourceId)
            {
                effects.RemoveAt(i);
                removed++;
            }
        }

        return removed;
    }

    private int IndexOf(StatusEffectType type, ulong sourceId)
    {
        for (int i = 0; i < effects.Count; i++)
        {
            if (effects[i].type == type && effects[i].sourceId == sourceId)
                return i;
        }

        return -1;
    }

    private void Update()
    {
        if (!CanWrite)
            return;

        double now = NetworkManager.ServerTime.Time;
        for (int i = effects.Count - 1; i >= 0; i--)
        {
            StatusEffectInstance instance = effects[i];
            if (instance.duration > 0f && now >= instance.appliedServerTime + instance.duration)
                effects.RemoveAt(i);
        }
    }
}
