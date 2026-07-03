using UnityEngine;

public enum TriggerMode
{
    OnlyEnter,
    OnlyStay,
    OnlyExit
}

public class ColliderBasicAttack : BaseWeapon
{
    [SerializeField] TriggerMode triggerMode;
    [SerializeField] float stayTime;
    float _stayTimer = 0f;

    void OnTriggerEnter(Collider other)
    {
        OnAttackTriggerEnter(other);
    }

    public void OnAttackTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        _stayTimer = 0f;

        if (triggerMode != TriggerMode.OnlyEnter) return;

        GameObject collidedObject = other.gameObject;
        TakeDamage(collidedObject);
    }

    void OnTriggerStay(Collider other)
    {
        OnAttackTriggerStay(other);
    }

    public void OnAttackTriggerStay(Collider other)
    {
        if (!IsServer) return;

        if (triggerMode != TriggerMode.OnlyStay) return;

        _stayTimer += Time.deltaTime;
        if (stayTime <= _stayTimer)
        {
            GameObject collidedObject = other.gameObject;
            TakeDamage(collidedObject);
            _stayTimer = 0f;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        OnAttackTriggerExit(other);
    }

    public void OnAttackTriggerExit(Collider other)
    {
        if (!IsServer) return;

        if (triggerMode != TriggerMode.OnlyExit) return;

        GameObject collidedObject = other.gameObject;
        TakeDamage(collidedObject);
    }

    void TakeDamage(GameObject collidedObject)
    {
        if ((targetLayer.value & (1 << collidedObject.layer)) != 0)
        {
            Unit unit = collidedObject.GetComponent<Unit>();
            if (unit == null)
            {
                Debug.LogError($"해당 오브젝트, {collidedObject.name}에 Unit 컴포넌트가 부착되어있지 않습니다.", this);
                return;
            }

            unit.TakeDamage(damage);
        }
    }
}
