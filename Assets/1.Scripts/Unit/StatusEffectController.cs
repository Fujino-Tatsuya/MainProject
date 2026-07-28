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
    public int stackCount;          // 같은 출처 재적용으로 쌓인 스택 (1~maxStacks), 배율은 magnitude^stackCount

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref type);
        serializer.SerializeValue(ref magnitude);
        serializer.SerializeValue(ref duration);
        serializer.SerializeValue(ref appliedServerTime);
        serializer.SerializeValue(ref sourceId);
        serializer.SerializeValue(ref stackCount);
    }

    public bool Equals(StatusEffectInstance other)
    {
        return type == other.type &&
            magnitude == other.magnitude &&
            duration == other.duration &&
            appliedServerTime == other.appliedServerTime &&
            sourceId == other.sourceId &&
            stackCount == other.stackCount;
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

    /// <summary>활성 인스턴스 중 statType에 해당하는 배율의 곱. 스택은 magnitude^stackCount로 누적. 없으면 1.</summary>
    public float GetStatMultiplier(StatusEffectType statType)
    {
        float multiplier = 1f;
        for (int i = 0; i < effects.Count; i++)
        {
            if ((effects[i].type & statType) == 0)
                continue;

            float magnitude = Mathf.Max(0f, effects[i].magnitude);
            int stacks = Mathf.Max(1, effects[i].stackCount);
            multiplier *= stacks == 1 ? magnitude : Mathf.Pow(magnitude, stacks);
        }

        return multiplier;
    }

    /// <summary>특정 출처가 건 특정 타입의 현재 스택 수. 없으면 0.</summary>
    public int GetStackCount(StatusEffectType type, ulong sourceId)
    {
        int index = IndexOf(type, sourceId);
        return index >= 0 ? Mathf.Max(1, effects[index].stackCount) : 0;
    }

    // ── UI 조회용 (전 피어 읽기 가능 — NetworkList는 복제됨) ──

    public int ActiveCount => effects.Count;

    public StatusEffectInstance GetActive(int index)
    {
        return effects[index];
    }

    /// <summary>index 인스턴스의 남은 시간(초). 수동 해제형(duration 0 이하)은 -1.</summary>
    public float GetRemainingTime(int index)
    {
        StatusEffectInstance instance = effects[index];
        if (instance.duration <= 0f)
            return -1f;

        return Mathf.Max(0f, (float)(instance.appliedServerTime + instance.duration - Now()));
    }

    /// <summary>차단류 등 수치 없는 효과 적용 (배율 1).</summary>
    public void Apply(StatusEffectType type, float duration, ulong sourceId)
    {
        Apply(type, 1f, duration, sourceId);
    }

    /// <summary>
    /// 서버 전용. 같은 (type, sourceId)가 없으면 병존 추가.
    /// 있으면 maxStacks 1(기본) = 갱신(시간·수치 리셋, 스택 1), maxStacks 2+ = 스택 +1(상한까지) + 시간 갱신.
    /// </summary>
    public void Apply(StatusEffectType type, float magnitude, float duration, ulong sourceId, int maxStacks = 1)
    {
        if (!CanWrite || type == StatusEffectType.None)
            return;

        int index = IndexOf(type, sourceId);

        int stackCount = 1;
        if (index >= 0 && maxStacks > 1)
            stackCount = Mathf.Min(Mathf.Max(1, effects[index].stackCount) + 1, maxStacks);

        StatusEffectInstance instance = new StatusEffectInstance
        {
            type = type,
            magnitude = magnitude,
            duration = duration,
            appliedServerTime = Now(),
            sourceId = sourceId,
            stackCount = stackCount
        };

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

    // 만료 시간 기준을 Pause-aware NetworkClock.GameNow로 사용한다(멀티에선 ServerTime과 동일, 솔로 host Pause만 반영). (PLAN §12)
    // NetworkClock이 없으면(구성 누락) raw ServerTime으로 폴백한다. appliedServerTime 필드도 같은 도메인으로 기록된다.
    private double Now()
        => NetworkClock.Instance != null
            ? NetworkClock.Instance.GameNow
            : (NetworkManager != null ? NetworkManager.ServerTime.Time : 0.0);

    private void Update()
    {
        if (!CanWrite)
            return;

        double now = Now();
        for (int i = effects.Count - 1; i >= 0; i--)
        {
            StatusEffectInstance instance = effects[i];
            if (instance.duration > 0f && now >= instance.appliedServerTime + instance.duration)
                effects.RemoveAt(i);
        }
    }
}
