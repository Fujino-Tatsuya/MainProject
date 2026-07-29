using UnityEngine;
using System.Collections.Generic;
using System;
using Unity.Behavior;

public class TwentyThreeBasicAttackChoice : BaseAttackChoice
{
    List<WeightedAttack<TwentyThreeBasicAttackType>> attackChoices = new List<WeightedAttack<TwentyThreeBasicAttackType>>();
    List<WeightedAttack<TwentyThreeBasicAttackType>> validAttacks = new List<WeightedAttack<TwentyThreeBasicAttackType>>();

    [Header("Attack 수치 Scriptable Object (인덱스 = 페이지 번호)")]
    [SerializeField] List<TwentyThreeBasicAttackFigure> pages = new List<TwentyThreeBasicAttackFigure>();

    int _page = 0;

    void Awake()
    {
        // 5개 공격 타입을 기본 등록 (수치는 PageEvent에서 페이지 SO 값으로 채움)
        attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Hook, 0f, 0f, 0f));
        attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Upper, 0f, 0f, 0f));
        attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Grab, 0f, 0f, 0f));
        attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Jump, 0f, 0f, 0f));
        attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Dash, 0f, 0f, 0f));

        // 초기 페이즈 0 수치로 attackChoices 일괄 세팅
        PageEvent(0);
    }


    public override void AddType<T>(T type)
    {
        if (type is not TwentyThreeBasicAttackType attackType)
        {
            Edit.LogWarning($"[No.23] {type} is not a {nameof(TwentyThreeBasicAttackType)}.");
            return;
        }

        if (attackType == TwentyThreeBasicAttackType.None)
        {
            Edit.LogWarning($"[No.23] Unknown attack type {type}. Cannot add to attack choices.");
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

        TwentyThreeBasicAttackFigure figure = GetFigure(_page);
        if (figure == null)
        {
            Edit.LogWarning($"[No.23] page {_page}에 해당하는 AttackFigure가 없어 {type}을 추가하지 못했습니다.");
            return;
        }

        attackChoices.Add(MakeWeightedAttack(figure, attackType));
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

        RefreshAttackChoices(figure);   // attackChoices의 모든 항목을 해당 페이지 SO 수치로 재조정
    }

    /// <summary>
    /// page 인덱스에 해당하는 공격 수치 SO를 반환한다. 범위 밖이면 null.
    /// </summary>
    TwentyThreeBasicAttackFigure GetFigure(int page)
    {
        if (pages == null || page < 0 || page >= pages.Count)
            return null;

        return pages[page];
    }

    /// <summary>
    /// attackChoices에 있는 각 항목을 지정한 SO의 수치로 재조정한다.
    /// WeightedAttack는 struct라 요소를 새 값으로 통째 교체한다.
    /// (Add/Remove로 바뀐 구성은 존중 — 없는 타입은 새로 추가하지 않음)
    /// </summary>
    void RefreshAttackChoices(TwentyThreeBasicAttackFigure figure)
    {
        for (int i = 0; i < attackChoices.Count; i++)
        {
            TwentyThreeBasicAttackType type = attackChoices[i].basicAttackType;
            if (type == TwentyThreeBasicAttackType.None)
                continue;   // 알 수 없는 타입은 그대로 유지

            attackChoices[i] = MakeWeightedAttack(figure, type);
        }
    }

    /// <summary>
    /// 지정한 SO에서 해당 공격 타입의 수치를 읽어 WeightedAttack을 만든다.
    /// </summary>
    WeightedAttack<TwentyThreeBasicAttackType> MakeWeightedAttack(TwentyThreeBasicAttackFigure figure, TwentyThreeBasicAttackType type)
    {
        switch (type)
        {
            case TwentyThreeBasicAttackType.Hook:
                return new WeightedAttack<TwentyThreeBasicAttackType>(type, figure.hookAttackMinDistance, figure.hookAttackMaxDistance, figure.hookAttackPercentage);
            case TwentyThreeBasicAttackType.Upper:
                return new WeightedAttack<TwentyThreeBasicAttackType>(type, figure.upperAttackMinDistance, figure.upperAttackMaxDistance, figure.upperAttackPercentage);
            case TwentyThreeBasicAttackType.Grab:
                return new WeightedAttack<TwentyThreeBasicAttackType>(type, figure.grabAttackMinDistance, figure.grabAttackMaxDistance, figure.grabAttackPercentage);
            case TwentyThreeBasicAttackType.Jump:
                return new WeightedAttack<TwentyThreeBasicAttackType>(type, figure.jumpAttackMinDistance, figure.jumpAttackMaxDistance, figure.jumpAttackPercentage);
            case TwentyThreeBasicAttackType.Dash:
                return new WeightedAttack<TwentyThreeBasicAttackType>(type, figure.dashAttackMinDistance, figure.dashAttackMaxDistance, figure.dashAttackPercentage);
            default:
                return new WeightedAttack<TwentyThreeBasicAttackType>(type, 0f, 0f, 0f);
        }
    }
}
