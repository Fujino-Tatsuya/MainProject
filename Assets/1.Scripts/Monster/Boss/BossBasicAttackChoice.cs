using System;
using System.Collections.Generic;
using UnityEngine;

// 코드 FSM 보스 기본 공격 선택기. TwentyThreeBasicAttackChoice 패턴을 미러링하되 근접 2종(Slam/Sweep)만.
//
// 의도(기존 BT 리버스 엔지니어링 보존):
//  - 거리창([min,max]) 필터 → 유효 공격만 후보로.
//  - 가중치(percentage) 룰렛으로 후보 중 하나 선택.
//  - 쿨다운 재등록 패턴: 특정 공격을 잠시 빼고(RemoveType) 나중에 되돌리는(AddType) 방식으로
//    "이 공격은 지금 쿨다운"을 표현. (WeightedAttack<BossBasicAttackType> 리스트로 관리.)
//
// GetRandomAttack은 (int)type을 반환한다. 유효 후보가 없거나 확률 합이 0이면 None(=0).
public class BossBasicAttackChoice : BaseAttackChoice
{
    [Header("Slam Attack (근접 내려찍기)")]
    [SerializeField] float slamAttackMinDistance = 0f;
    [SerializeField] float slamAttackMaxDistance = 3f;
    [SerializeField] float slamAttackPercentage = 60f;

    [Header("Sweep Attack (근접 휩쓸기)")]
    [SerializeField] float sweepAttackMinDistance = 0f;
    [SerializeField] float sweepAttackMaxDistance = 4f;
    [SerializeField] float sweepAttackPercentage = 40f;

    readonly List<WeightedAttack<BossBasicAttackType>> attackChoices = new List<WeightedAttack<BossBasicAttackType>>();
    readonly List<WeightedAttack<BossBasicAttackType>> validAttacks = new List<WeightedAttack<BossBasicAttackType>>();

    void Awake()
    {
        attackChoices.Add(new WeightedAttack<BossBasicAttackType>(
            BossBasicAttackType.Slam, slamAttackMinDistance, slamAttackMaxDistance, slamAttackPercentage));
        attackChoices.Add(new WeightedAttack<BossBasicAttackType>(
            BossBasicAttackType.Sweep, sweepAttackMinDistance, sweepAttackMaxDistance, sweepAttackPercentage));
    }

    // 쿨다운 재등록용: 빠져 있던 공격 타입을 인스펙터 값으로 다시 후보에 추가.
    // 이미 존재하면 경고 후 무시(TwentyThree 패턴 그대로).
    public override void AddType<T>(T type)
    {
        if (type is not BossBasicAttackType attackType)
        {
            Debug.LogWarning($"{type} is not a {nameof(BossBasicAttackType)}.");
            return;
        }

        for (int i = 0; i < attackChoices.Count; i++)
        {
            if (attackChoices[i].basicAttackType == attackType)
            {
                Debug.LogWarning($"Attack type {type} already exists in the attack choices.");
                return;
            }
        }

        switch (attackType)
        {
            case BossBasicAttackType.Slam:
                attackChoices.Add(new WeightedAttack<BossBasicAttackType>(
                    attackType, slamAttackMinDistance, slamAttackMaxDistance, slamAttackPercentage));
                break;
            case BossBasicAttackType.Sweep:
                attackChoices.Add(new WeightedAttack<BossBasicAttackType>(
                    attackType, sweepAttackMinDistance, sweepAttackMaxDistance, sweepAttackPercentage));
                break;
            default:
                Debug.LogWarning($"Unknown attack type {type}. Cannot add to attack choices.");
                break;
        }
    }

    // 쿨다운 재등록용: 특정 공격 타입을 후보에서 제거(발동 직후 호출 → 잠시 뒤 AddType로 복구).
    public override void RemoveType<T>(T type)
    {
        if (type is not BossBasicAttackType attackType)
        {
            Debug.LogWarning($"{type} is not a {nameof(BossBasicAttackType)}.");
            return;
        }

        for (int i = 0; i < attackChoices.Count; i++)
        {
            if (attackChoices[i].basicAttackType == attackType)
            {
                attackChoices.RemoveAt(i);
                return;
            }
        }
    }

    // 거리창 필터 + 가중치 룰렛. 유효 후보 없음/확률 0 → None(0).
    public override int GetRandomAttack(float currentDistance)
    {
        BossBasicAttackType res = BossBasicAttackType.None;

        validAttacks.Clear();
        foreach (var attack in attackChoices)
        {
            if (currentDistance <= attack.attackMaxDistance && currentDistance >= attack.attackMinDistance)
                validAttacks.Add(attack);
        }

        if (validAttacks.Count == 0)
            return Convert.ToInt32(res);

        float totalPercentage = 0f;
        foreach (var attack in validAttacks)
            totalPercentage += Mathf.Max(0f, attack.attackPercentage);

        if (totalPercentage <= 0f)
            return Convert.ToInt32(res);

        float randomPercentage = UnityEngine.Random.Range(0f, totalPercentage);
        for (int i = 0; i < validAttacks.Count; i++)
        {
            float percentage = Mathf.Max(0f, validAttacks[i].attackPercentage);
            randomPercentage -= percentage;
            if (randomPercentage <= 0f)
            {
                res = validAttacks[i].basicAttackType;
                break;
            }
        }

        return Convert.ToInt32(res);
    }
}
