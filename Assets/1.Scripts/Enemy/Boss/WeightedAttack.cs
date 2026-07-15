using UnityEngine;

public struct WeightedAttack<T> where T : System.Enum
{
    public T basicAttackType;
    public float attackMinDistance;
    public float attackMaxDistance;
    public float attackPercentage;

    public WeightedAttack(T basicAttackType, float attackMinDistance, float attackMaxDistance, float attackPercentage)
    {
        this.basicAttackType = basicAttackType;
        this.attackMinDistance = attackMinDistance;
        this.attackMaxDistance = attackMaxDistance;
        this.attackPercentage = attackPercentage;
    }
}