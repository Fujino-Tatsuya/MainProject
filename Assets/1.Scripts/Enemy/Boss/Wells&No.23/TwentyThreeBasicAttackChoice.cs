using UnityEngine;
using System.Collections.Generic;
using System;

public class TwentyThreeBasicAttackChoice : BaseAttackChoice
{
    [Header("Hook Attack")]
    [SerializeField] float hookAttackMinDistance;
    [SerializeField] float hookAttackMaxDistance;
    [SerializeField] float hookAttackPercentage;

    [Header("Upper Attack")]
    [SerializeField] float upperAttackMinDistance;
    [SerializeField] float upperAttackMaxDistance;
    [SerializeField] float upperAttackPercentage;

    [Header("Grab Attack")]
    [SerializeField] float grabAttackMinDistance;
    [SerializeField] float grabAttackMaxDistance;
    [SerializeField] float grabAttackPercentage;

    [Header("Jump Attack")]
    [SerializeField] float jumpAttackMinDistance;
    [SerializeField] float jumpAttackMaxDistance;
    [SerializeField] float jumpAttackPercentage;

    [Header("Dash Attack")]
    [SerializeField] float dashAttackMinDistance;
    [SerializeField] float dashAttackMaxDistance;
    [SerializeField] float dashAttackPercentage;

    List<WeightedAttack<TwentyThreeBasicAttackType>> attackChoices = new List<WeightedAttack<TwentyThreeBasicAttackType>>();
    List<WeightedAttack<TwentyThreeBasicAttackType>> validAttacks = new List<WeightedAttack<TwentyThreeBasicAttackType>>();

    void Awake()
    {
        attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Hook, hookAttackMinDistance, hookAttackMaxDistance, hookAttackPercentage));
        attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Upper, upperAttackMinDistance, upperAttackMaxDistance, upperAttackPercentage));
        attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Grab, grabAttackMinDistance, grabAttackMaxDistance, grabAttackPercentage));
        attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Jump, jumpAttackMinDistance, jumpAttackMaxDistance, jumpAttackPercentage));
        attackChoices.Add(new WeightedAttack<TwentyThreeBasicAttackType>(TwentyThreeBasicAttackType.Dash, dashAttackMinDistance, dashAttackMaxDistance, dashAttackPercentage));
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
}