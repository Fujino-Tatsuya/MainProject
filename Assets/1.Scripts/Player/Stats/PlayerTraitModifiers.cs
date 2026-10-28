using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 특성 수치 출처(할 일 125 2/5) — 상태이상(StatusEffect)을 쓰지 않는 영구 출처. <c>Player.prefab</c>(역할 base)에 붙는다.
/// 복제 = 서버가 고른 특성 해시 목록(<see cref="TraitDirector"/> 획득 행)만. 수치는 서버·클라가 각자 같은 SO 에서 같은 순수 함수(<see cref="TraitModifierSet"/>)로 계산한다.
/// 판정은 서버 값이고 클라 값은 툴팁·HUD·서버 2차 검증 보고용이다(player-stats.md §5).
/// 그래서 보스방 입장 디버프 정리·연출 잠금(상태이상 일괄 해제)과 무관하고, 1 미만 배율(페널티)도 그대로 남는다.
///
/// 소비처: <see cref="PlayerStats"/>(스탯 5종 %·가하는 피해% — <see cref="IPlayerStatSource"/>), 피해 기준(Dref, <see cref="Player.ResolveDamage(float,int,TraitAttackKey)"/>),
/// 스킬 전체 쿨(<see cref="PlayerSkillController.GetEffectiveCooldownTime"/>). 회복 배율은 질의만 있고 소비처는 3/5.
/// 획득 목록이 바뀌면(획득·특성 초기화·재합류·디렉터 교체) 다음 LateUpdate 에 다시 합성하고 <see cref="Changed"/> 를 낸다.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerTraitModifiers : MonoBehaviour, IPlayerStatSource
{
    private const string Tag = "[TraitModifiers]";

    private readonly TraitModifierSet set = new TraitModifierSet();
    private readonly List<int> acquiredScratch = new List<int>();
    private NetworkObject networkObject;
    private TraitDirector boundDirector;
    private bool boundSpawned;
    private ulong boundOwner;
    private bool dirty = true;

    /// <summary>기여가 바뀌었다(다시 합성 뒤). PlayerStats 검증 유예·쿨 진행률 보존이 구독한다.</summary>
    public event Action Changed;

    /// <summary>지금 반영된 특성 해시(중복 제거, 획득 순서).</summary>
    public IReadOnlyList<int> TraitHashes => set.TraitHashes;

    /// <summary>프리팹에 있으면 그것, 없으면 붙인다 — Awake 순서와 무관하게 같은 인스턴스(<see cref="PlayerCooldownClock.For"/> 와 같은 방식).</summary>
    public static PlayerTraitModifiers For(Component owner)
    {
        if (owner == null)
            return null;

        return owner.TryGetComponent(out PlayerTraitModifiers modifiers)
            ? modifiers
            : owner.gameObject.AddComponent<PlayerTraitModifiers>();
    }

    /// <summary>공격 계열 원래 피해 배율(Dref 에 한 번 곱한다). 없으면 1.</summary>
    public float DamageMultiplier(TraitAttackKey key) => set.DamageMultiplier(key);

    /// <summary>슬롯 전체 쿨 배율(일반/대체 형태 구분). 없으면 1.</summary>
    public float CooldownMultiplier(PlayerSkillSlot slot, TraitAttackForm form) => set.CooldownMultiplier(slot, form);

    /// <summary>회복 배율. 없으면 1. 소비처는 3/5.</summary>
    public float HealMultiplier => set.HealMultiplier;

    public float GetBasePercent(PlayerStatKind kind) => set.BasePercent(kind);
    public float GetTotalPercent(PlayerStatKind kind) => set.TotalPercent(kind);
    public float DealtDamagePercent => set.DealtDamagePercent;

    private void Awake() => networkObject = GetComponent<NetworkObject>();

    private void OnDisable() => Bind(null);

    private void LateUpdate()
    {
        TraitDirector director = TraitDirector.Instance;
        if (director != boundDirector)
            Bind(director);

        // 스폰 전에는 OwnerClientId 가 정해지지 않는다 — 스폰·오너가 바뀌면 다시 읽는다(재합류 = 새 오브젝트·새 clientId).
        bool spawned = networkObject != null && networkObject.IsSpawned;
        ulong owner = spawned ? networkObject.OwnerClientId : 0UL;
        if (spawned != boundSpawned || owner != boundOwner)
        {
            boundSpawned = spawned;
            boundOwner = owner;
            dirty = true;
        }

        if (dirty)
            Recompute();
    }

    private void Bind(TraitDirector director)
    {
        if (boundDirector != null)
            boundDirector.ClientStateChanged -= MarkDirty;

        boundDirector = director;
        if (boundDirector != null)
            boundDirector.ClientStateChanged += MarkDirty;
        dirty = true;
    }

    private void MarkDirty() => dirty = true;

    private void Recompute()
    {
        dirty = false;
        acquiredScratch.Clear();
        if (boundDirector != null && boundSpawned)
            boundDirector.GetAcquired(boundOwner, acquiredScratch);

        TraitDirector director = boundDirector;
        if (!set.Rebuild(acquiredScratch, hash => director != null ? director.GetTrait(hash) : null))
            return;

        IReadOnlyList<string> errors = set.Errors;
        for (int i = 0; i < errors.Count; i++)
            Edit.LogError($"{Tag} '{name}' {errors[i]}", this);

        Edit.Log($"{Tag} '{name}' 특성 수치 다시 합성 — 특성 {set.TraitHashes.Count}개, 기여 {set.Modifiers.Count}건.", this);
        Changed?.Invoke();
    }
}
