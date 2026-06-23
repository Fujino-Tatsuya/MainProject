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
        if (!IsServer) return;
        _stayTimer = 0f;

        if (triggerMode != TriggerMode.OnlyEnter) return;

        GameObject root = other.transform.root.gameObject;
        TakeDamage(root);
    }

    void OnTriggerStay(Collider other)
    {
        if (!IsServer) return;

        if (triggerMode != TriggerMode.OnlyStay) return;

        _stayTimer += Time.deltaTime;
        if (stayTime <= _stayTimer)
        {
            GameObject root = other.transform.root.gameObject;
            TakeDamage(root);
            _stayTimer = 0f;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;

        if (triggerMode != TriggerMode.OnlyExit) return;

        GameObject root = other.transform.root.gameObject;
        TakeDamage(root);
    }

    void TakeDamage(GameObject root)
    {
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