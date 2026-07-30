using UnityEngine;

public enum TriggerMode
{
    OnlyEnter,
    OnlyStay,
    OnlyExit,
    OnlyStayAndKnockBack
}

public class ColliderBasicAttack : BaseAttack
{
    [SerializeField] private TriggerMode triggerMode;
    [SerializeField] private float stayTime;

    KnockbackAttack _knockbackAttack;
    private float _stayTimer = 0f;

    private void Awake()
    {
        InitializeAttackInfo();
        _knockbackAttack = GetComponent<KnockbackAttack>();
    }

    private void OnTriggerEnter(Collider other)
    {
        OnAttackTriggerEnter(other);
    }

    public void OnAttackTriggerEnter(Collider other)
    {
        if (!IsServer)
            return;

        _stayTimer = 0f;

        if (triggerMode != TriggerMode.OnlyEnter)
            return;

        TryResolveHit(other);
    }

    private void OnTriggerStay(Collider other)
    {
        OnAttackTriggerStay(other);
    }

    public void OnAttackTriggerStay(Collider other)
    {
        if (!IsServer)
            return;

        if (triggerMode != TriggerMode.OnlyStay && triggerMode != TriggerMode.OnlyStayAndKnockBack)
            return;

        _stayTimer += Time.deltaTime;
        if (stayTime > _stayTimer)
            return;

        TryResolveHit(other);
        if (triggerMode == TriggerMode.OnlyStayAndKnockBack)
            _knockbackAttack?.ApplyKnockbackAttack(other.gameObject);

        _stayTimer = 0f;
    }

    private void OnTriggerExit(Collider other)
    {
        OnAttackTriggerExit(other);
    }

    public void OnAttackTriggerExit(Collider other)
    {
        if (!IsServer)
            return;

        if (triggerMode != TriggerMode.OnlyExit)
            return;

        TryResolveHit(other);
    }
}
