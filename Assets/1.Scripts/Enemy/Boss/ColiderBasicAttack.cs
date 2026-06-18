using UnityEngine;

public class ColiderBasicAttack : BaseWeapon
{
    void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        GameObject root = other.transform.root.gameObject;
        if ((layerMask.value & (1 << root.layer)) != 0)
        {
            Unit unit = root.GetComponent<Unit>();
            if (unit == null)
            {
                Debug.LogError($"해당 오브젝트, {root.name}에 Unit 컴포넌트가 부착되어있지 않습니다.", this);
                return;
            }

            unit.TakeDamage(damage);
        }
    }
}