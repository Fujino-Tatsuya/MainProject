using UnityEngine;

public class KnockbackAttack : BaseAttack
{
    [SerializeField] float knockbackStrength = 5f;

    void Awake()
    {
        InitializeAttackInfo();
    }

    public void ApplyKnockbackAttack(GameObject collidedObject)
    {
        if (!IsServer) return;

        GameObject root = collidedObject.transform.root.gameObject;
        if ((targetLayer.value & (1 << root.layer)) != 0)
        {
            Unit unit = root.GetComponent<Unit>();
            if (unit == null)
            {
                Edit.LogError($"[No.23] 해당 오브젝트, {root.name}에 Unit 컴포넌트가 부착되어있지 않습니다.", this);
                return;
            }

            unit.TakeDamage(new AttackInfo(damage, attackType, isGroggyAttack));
            Vector3 dir = GetDirection(root);
            unit.Knockback(dir, knockbackStrength);
            Edit.Log($"[No.23] {name} 넉백 공격 적중: {unit.name} (피해 {damage})", this);
        }
    }

    Vector3 GetDirection(GameObject target)
    {
        Vector3 direction;

        Vector3 start = transform.position;
        start.y = target.transform.position.y;
        direction = target.transform.position - start;

        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            return target.transform.TransformDirection(Vector3.back);
        }

        return direction.normalized;
    }
}
