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
    };

    // 활성 상태(복제). 서버 write / 모두 read.
    readonly NetworkVariable<StatusEffectType> _active = new NetworkVariable<StatusEffectType>(
        StatusEffectType.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public StatusEffectType Active => _active.Value;

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
        _active.Value = next;
    }

    public void ClearAll()
    {
        if (!IsServer) return;
        _expiry.Clear();
        _active.Value = StatusEffectType.None;
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
