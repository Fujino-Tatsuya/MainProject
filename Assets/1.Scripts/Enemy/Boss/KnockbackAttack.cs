using UnityEngine;

public class KnockbackAttack : BaseWeapon
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
                Debug.LogError($"해당 오브젝트, {root.name}에 Unit 컴포넌트가 부착되어있지 않습니다.", this);
                return;
            }

            unit.TakeDamage(_attackInfo);
            Vector3 dir = GetDirection(root);
            unit.Knockback(dir, knockbackStrength);
        }
    }

    Vector3 GetDirection(GameObject target)
    {
        Vector3 direction;

        Vector3 start = transform.position;
        start.y = target.transform.position.y;
        direction = target.transform.position - start;

        return direction.normalized;
    }
}
