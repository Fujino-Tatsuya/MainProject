using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 몬스터 시간제 상태이상. 서버 권한, 클라는 NetworkVariable 복제로만 반영.
//
// 현재 프로젝트 어디에도 "시간이 지나면 만료되는 상태이상"의 실체가 없어(플레이어/보스는 BT/즉발 위주)
// 여기서 최소 구현을 제공한다. StatusEffectType([Flags]) 값은 기존 정의를 그대로 사용한다.
//
// 규칙:
//  - 슈퍼아머(SuperArmor) 활성 중에는 들어오는 CC(SuperArmor 외 전부)를 무시한다.
//  - duration <= 0 이면 무한 지속(수동 해제 전까지). 그 외엔 서버 Tick으로 만료.
[DisallowMultipleComponent]
public class MonsterStatusEffect : NetworkBehaviour, IMonsterStatusFacade, IStatusEffectFacade
{
    // 개별 상태 플래그(단일 비트) 목록. 조합값(Flags) 순회/해제에 사용.
    static readonly StatusEffectType[] SingleFlags =
    {
        StatusEffectType.Airborne,
        StatusEffectType.Stunned,
        StatusEffectType.Slowed,
        StatusEffectType.Rooted,
        StatusEffectType.Silenced,
        StatusEffectType.Debilitated,
        StatusEffectType.SuperArmor,
        StatusEffectType.Concussion,
    };

    // 활성 상태(복제). 서버 write / 모두 read.
    readonly NetworkVariable<StatusEffectType> _active = new NetworkVariable<StatusEffectType>(
        StatusEffectType.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public StatusEffectType Active => _active.Value;

    /// <summary>
    /// [전 피어] 활성 상태가 바뀔 때마다(적용·해제·만료) 난다 — 무엇이 바뀌었는지는 <see cref="Active"/> 로 다시 읽는다.
    /// 플레이어 <c>StatusEffectController.EffectsChanged</c> 와 같은 모양이다(몬스터 머리 위 상태 이펙트가 받는다).
    /// </summary>
    public event System.Action EffectsChanged;

    public override void OnNetworkSpawn() => _active.OnValueChanged += HandleActiveChanged;

    public override void OnNetworkDespawn() => _active.OnValueChanged -= HandleActiveChanged;

    void HandleActiveChanged(StatusEffectType previous, StatusEffectType current) => EffectsChanged?.Invoke();

    // 플래그별 만료 시각(서버 전용). float.PositiveInfinity = 무한.
    readonly Dictionary<StatusEffectType, float> _expiry = new Dictionary<StatusEffectType, float>();

    #region 조회(모든 피어에서 유효 — 복제된 _active 기반)
    // 이동 봉쇄: 에어본/기절/속박.
    public bool BlocksMovement =>
        Has(StatusEffectType.Airborne) || Has(StatusEffectType.Stunned) || Has(StatusEffectType.Rooted);

    // 공격 봉쇄: 에어본/기절.
    public bool BlocksAttack =>
        Has(StatusEffectType.Airborne) || Has(StatusEffectType.Stunned);

    // 경직(Hit 전이) 봉쇄 = 슈퍼아머면 공격 취소/피격 리액션을 막는다.
    public bool BlocksInterrupt => HasSuperArmor;

    public bool HasSuperArmor => Has(StatusEffectType.SuperArmor);

    // IStatusEffectFacade — Unit의 Final* 스탯 계산용 배율.
    // 몹은 아직 스탯 modifier(버프/디버프 배율) 시스템이 없어 항상 1f(무효과).
    // 몹 버프/디버프 도입 시 여기서 실제 배율을 계산한다(플레이어 StatusEffectController.GetStatMultiplier 참고).
    public float GetStatMultiplier(StatusEffectType statType) => 1f;

    bool Has(StatusEffectType flag) => (_active.Value & flag) != 0;
    #endregion

    #region 파사드 구현 (IMonsterStatusFacade)
    public void ApplyStatus(StatusEffectType type, float duration)
    {
        if (!IsServer || type == StatusEffectType.None) return;

        // 뇌진탕은 만료형 플래그가 아니라 '넉백 끝나면 기절' 대기라 따로 처리한다(ConcussionState).
        if ((type & StatusEffectType.Concussion) != 0)
        {
            ApplyConcussion(duration);
            type &= ~StatusEffectType.Concussion;
            if (type == StatusEffectType.None) return;
        }

        float expireAt = duration <= 0f ? float.PositiveInfinity : Time.time + duration;
        StatusEffectType next = _active.Value;

        foreach (StatusEffectType flag in SingleFlags)
        {
            if ((type & flag) == 0) continue;

            // 슈퍼아머 중에는 CC(슈퍼아머 외) 적용 무시.
            if (flag != StatusEffectType.SuperArmor && HasSuperArmor)
                continue;

            next |= flag;
            _expiry[flag] = expireAt;
        }

        _active.Value = next;
    }

    public void RemoveStatus(StatusEffectType type)
    {
        if (!IsServer || type == StatusEffectType.None) return;

        StatusEffectType next = _active.Value;
        foreach (StatusEffectType flag in SingleFlags)
        {
            if ((type & flag) == 0) continue;
            next &= ~flag;
            _expiry.Remove(flag);
        }
        if ((type & StatusEffectType.Concussion) != 0)
            _concussion.Cancel();
        _active.Value = next;
    }

    public void ClearAll()
    {
        if (!IsServer) return;
        _expiry.Clear();
        _concussion.Cancel();
        _active.Value = StatusEffectType.None;
    }
    #endregion

    #region 뇌진탕(할 일 125 — 규칙은 ConcussionState)
    // 서버 전용 대기 상태. 복제되는 것은 Concussion·Stunned 플래그뿐이다.
    ConcussionState _concussion;
    MonsterBase _monster;

    /// <summary>[서버] 넉백이 끝나면 걸 기절이 대기 중인가.</summary>
    public bool HasPendingConcussion => _concussion.IsPending;

    bool IsKnockedBack
    {
        get
        {
            if (_monster == null) _monster = GetComponent<MonsterBase>();
            return _monster != null && _monster.State == MonsterState.Knockback;
        }
    }

    // 넉백 중 = 대기(Concussion 플래그 + 기절 표시를 같이 건다 — 효과·머리 위 표시는 기절과 같다), 아니면 즉시 기절(큰 값).
    void ApplyConcussion(float stunSeconds)
    {
        switch (_concussion.Apply(stunSeconds, IsKnockedBack, _active.Value))
        {
            case ConcussionState.ApplyResult.Pending:
                _expiry[StatusEffectType.Concussion] = float.PositiveInfinity;   // 넉백 끝(소비)·취소로만 지운다
                SetStunAtLeast(stunSeconds, StatusEffectType.Concussion);
                break;
            case ConcussionState.ApplyResult.StunNow:
                SetStunAtLeast(stunSeconds, StatusEffectType.None);
                break;
        }
    }

    /// <summary>
    /// [서버] 넉백 종료(MonsterBase.ExitKnockback) — 대기 중이던 기절 길이를 돌려주고 대기·Concussion 플래그를 지운다.
    /// 지금 슈퍼아머면 0(자격 재확인). 기절 적용은 호출부가 <see cref="ExtendStun"/> 으로(경직과 큰 값).
    /// </summary>
    public float ConsumeConcussionOnKnockbackEnd()
    {
        if (!IsServer || !_concussion.IsPending) return 0f;
        float stun = _concussion.ConsumeOnKnockbackEnd(_active.Value);
        ClearConcussionFlag();
        return stun;
    }

    /// <summary>[서버] 대기 중인 '밀치기 후 기절' 취소(제압·그로기 진입 등 — <see cref="CancelsPendingConcussion"/>).</summary>
    public void CancelConcussion()
    {
        if (!IsServer || !_concussion.Cancel()) return;
        ClearConcussionFlag();
    }

    /// <summary>[서버] 기절을 남은 시간과 큰 값으로 건다(합산 아님 — 뇌진탕 명세 §4). 슈퍼아머면 무시.</summary>
    public void ExtendStun(float duration) => SetStunAtLeast(duration, StatusEffectType.None);

    /// <summary>
    /// 몬스터 상태 전이가 대기 중인 뇌진탕을 취소하는가 — 그로기(제압 포함) 진입, 또는 넉백이 정상 종료(ExitKnockback 이 먼저 소비) 말고 다른 길로 끝남.
    /// </summary>
    public static bool CancelsPendingConcussion(MonsterState previous, MonsterState next) =>
        next == MonsterState.Groggy || (previous == MonsterState.Knockback && next != MonsterState.Knockback);

    void SetStunAtLeast(float duration, StatusEffectType alsoSet)
    {
        if (duration <= 0f || HasSuperArmor) return;

        float expireAt = Time.time + duration;
        if (_expiry.TryGetValue(StatusEffectType.Stunned, out float existing) && (_active.Value & StatusEffectType.Stunned) != 0)
            expireAt = Time.time + ConcussionState.MergeStun(existing - Time.time, duration);
        _expiry[StatusEffectType.Stunned] = expireAt;
        _active.Value |= StatusEffectType.Stunned | alsoSet;
    }

    void ClearConcussionFlag()
    {
        _expiry.Remove(StatusEffectType.Concussion);
        _active.Value &= ~StatusEffectType.Concussion;
    }
    #endregion

    void Update()
    {
        if (!IsServer || _expiry.Count == 0) return;

        StatusEffectType next = _active.Value;
        bool changed = false;

        // 만료 스캔. 순회 중 삭제를 피하려고 임시 목록 사용.
        _expiredScratch.Clear();
        foreach (KeyValuePair<StatusEffectType, float> kv in _expiry)
        {
            if (Time.time >= kv.Value)
                _expiredScratch.Add(kv.Key);
        }

        for (int i = 0; i < _expiredScratch.Count; i++)
        {
            next &= ~_expiredScratch[i];
            _expiry.Remove(_expiredScratch[i]);
            changed = true;
        }

        if (changed)
            _active.Value = next;
    }

    readonly List<StatusEffectType> _expiredScratch = new List<StatusEffectType>();
}
