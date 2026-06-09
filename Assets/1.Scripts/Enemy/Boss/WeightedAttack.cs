using UnityEngine;

public struct WeightedAttack<T> where T : System.Enum
{
    public T basicAttackType;
    public float attackDistance;
    public float attackPercentage;

    public WeightedAttack(T basicAttackType, float attackDistance, float attackPercentage)
    {
        this.basicAttackType = basicAttackType;
        this.attackDistance = attackDistance;
        this.attackPercentage = attackPercentage;
    }
}