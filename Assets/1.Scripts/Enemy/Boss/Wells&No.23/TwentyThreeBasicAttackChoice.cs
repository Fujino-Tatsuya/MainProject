using UnityEngine;
using System.Collections.Generic;
using System;
using Unity.Behavior;

public class TwentyThreeBasicAttackChoice : BaseAttackChoice
{
    float hookAttackMinDistance;
    float hookAttackMaxDistance;
    float hookAttackPercentage;

    float upperAttackMinDistance;
    float upperAttackMaxDistance;
    float upperAttackPercentage;

    float grabAttackMinDistance;
    float grabAttackMaxDistance;
    float grabAttackPercentage;

    float jumpAttackMinDistance;
    float jumpAttackMaxDistance;
    float jumpAttackPercentage;

    float dashAttackMinDistance;
    float dashAttackMaxDistance;
    float dashAttackPercentage;

    List<WeightedAttack<TwentyThreeBasicAttackType>> attackChoices = new List<WeightedAttack<TwentyThreeBasicAttackType>>();
    List<WeightedAttack<TwentyThreeBasicAttackType>> validAttacks = new List<WeightedAttack<TwentyThreeBasicAttackType>>();

    [Header("Attack 수치 Scriptable Object")]
    [SerializeField] TwentyThreeBasicAttackFigure page0;
    [SerializeField] TwentyThreeBasicAttackFigure page1;
    [SerializeField] TwentyThreeBasicAttackFigure page2;

    int _page = 0;

    void Awake()
    {
        // 5개 공격 타입을 기본 등록 (수치는 PageEvent에서 페이지1로 채움)
        attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Hook, 0f, 0f, 0f));
        attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Upper, 0f, 0f, 0f));
        attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Grab, 0f, 0f, 0f));
        attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Jump, 0f, 0f, 0f));
        attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Dash, 0f, 0f, 0f));

        // 초기 페이즈 0 수치로 로컬 필드 + attackChoices 일괄 세팅
        PageEvent(0);
    }


    public override void AddType<T>(T type)
    {
        if (type is not TwentyThreeBasicAttackType attackType)
        {
            Edit.LogWarning($"[No.23] {type} is not a {nameof(TwentyThreeBasicAttackType)}.");
            return;
        }

        for (int i = 0; i < attackChoices.Count; i++)
        {
            if (attackChoices[i].basicAttackType == attackType)
            {
                Edit.LogWarning($"[No.23] Attack type {type} already exists in the attack choices. Use UpdateType to modify its properties.");
                return;
            }
                
        }

        switch (type)
        {
            case TwentyThreeBasicAttackType.Hook:
                attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(attackType, hookAttackMinDistance, hookAttackMaxDistance, hookAttackPercentage));
                break;
            case TwentyThreeBasicAttackType.Upper:
                attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(attackType, upperAttackMinDistance, upperAttackMaxDistance, upperAttackPercentage));
                break;
            case TwentyThreeBasicAttackType.Grab:
                attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(attackType, grabAttackMinDistance, grabAttackMaxDistance, grabAttackPercentage));
                break;
            case TwentyThreeBasicAttackType.Jump:
                attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(attackType, jumpAttackMinDistance, jumpAttackMaxDistance, jumpAttackPercentage));
                break;
            case TwentyThreeBasicAttackType.Dash:
                attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(attackType, dashAttackMinDistance, dashAttackMaxDistance, dashAttackPercentage));
                break;
            default:
                Edit.LogWarning($"[No.23] Unknown attack type {type}. Cannot add to attack choices.");
                break;
        }
    }


    public override void RemoveType<T>(T type)
    {
        if (type is not TwentyThreeBasicAttackType attackType)
        {
            Edit.LogWarning($"[No.23] {type} is not a {nameof(TwentyThreeBasicAttackType)}.");
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

    public override int GetRandomAttack(float currentDistance)
    {
        TwentyThreeBasicAttackType res = TwentyThreeBasicAttackType.None;

        validAttacks.Clear();

        foreach (var attack in attackChoices)
        {
            if (currentDistance <= attack.attackMaxDistance && currentDistance >= attack.attackMinDistance)
            {
                validAttacks.Add(attack);
            }
        }

        if (validAttacks.Count == 0)
        {
            return Convert.ToInt32(res);
        }

        float totalPercentage = 0f;

        foreach (var attack in validAttacks)
        {
            totalPercentage += Mathf.Max(0f, attack.attackPercentage);
        }

        if (totalPercentage <= 0f)
            return Convert.ToInt32(res);

        float randomPercentage = UnityEngine.Random.Range(0, totalPercentage);

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

    public override void PageEvent(int page)
    {
        _page = page;

        TwentyThreeBasicAttackFigure figure = GetFigure(_page);
        if (figure == null)
        {
            Edit.LogWarning($"[No.23] page {page}에 해당하는 AttackFigure가 없습니다. 공격 수치를 변경하지 않습니다.");
            return;
        }

        ApplyFigure(figure);        // 로컬 수치 필드에 SO 값 대입
        RefreshAttackChoices();     // attackChoices의 모든 항목을 로컬 수치로 재조정
    }

    /// <summary>
    /// page 번호(1-based)에 해당하는 공격 수치 SO를 반환한다. 범위 밖이면 null.
    /// </summary>
    TwentyThreeBasicAttackFigure GetFigure(int page)
    {
        switch (page)
        {
            case 0: return page0;
            case 1: return page1;
            case 2: return page2;
            default: return null;
        }
    }

    /// <summary>
    /// 선택된 SO의 수치를 로컬 필드에 대입한다.
    /// </summary>
    void ApplyFigure(TwentyThreeBasicAttackFigure figure)
    {
        hookAttackMinDistance = figure.hookAttackMinDistance;
        hookAttackMaxDistance = figure.hookAttackMaxDistance;
        hookAttackPercentage = figure.hookAttackPercentage;

        upperAttackMinDistance = figure.upperAttackMinDistance;
        upperAttackMaxDistance = figure.upperAttackMaxDistance;
        upperAttackPercentage = figure.upperAttackPercentage;

        grabAttackMinDistance = figure.grabAttackMinDistance;
        grabAttackMaxDistance = figure.grabAttackMaxDistance;
        grabAttackPercentage = figure.grabAttackPercentage;

        jumpAttackMinDistance = figure.jumpAttackMinDistance;
        jumpAttackMaxDistance = figure.jumpAttackMaxDistance;
        jumpAttackPercentage = figure.jumpAttackPercentage;

        dashAttackMinDistance = figure.dashAttackMinDistance;
        dashAttackMaxDistance = figure.dashAttackMaxDistance;
        dashAttackPercentage = figure.dashAttackPercentage;
    }

    /// <summary>
    /// attackChoices에 있는 각 항목을 현재 로컬 수치로 재조정한다.
    /// WeightedAttack는 struct라 요소를 새 값으로 통째 교체한다.
    /// (Add/Remove로 바뀐 구성은 존중 — 없는 타입은 새로 추가하지 않음)
    /// </summary>
    void RefreshAttackChoices()
    {
        for (int i = 0; i < attackChoices.Count; i++)
        {
            switch (attackChoices[i].basicAttackType)
            {
                case TwentyThreeBasicAttackType.Hook:
                    attackChoices[i] = new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Hook, hookAttackMinDistance, hookAttackMaxDistance, hookAttackPercentage);
                    break;
                case TwentyThreeBasicAttackType.Upper:
                    attackChoices[i] = new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Upper, upperAttackMinDistance, upperAttackMaxDistance, upperAttackPercentage);
                    break;
                case TwentyThreeBasicAttackType.Grab:
                    attackChoices[i] = new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Grab, grabAttackMinDistance, grabAttackMaxDistance, grabAttackPercentage);
                    break;
                case TwentyThreeBasicAttackType.Jump:
                    attackChoices[i] = new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Jump, jumpAttackMinDistance, jumpAttackMaxDistance, jumpAttackPercentage);
                    break;
                case TwentyThreeBasicAttackType.Dash:
                    attackChoices[i] = new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Dash, dashAttackMinDistance, dashAttackMaxDistance, dashAttackPercentage);
                    break;
                default:
                    // 알 수 없는 타입은 그대로 유지
                    break;
            }
        }
    }
}
