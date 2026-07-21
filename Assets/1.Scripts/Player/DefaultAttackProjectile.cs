using UnityEngine;

[RequireComponent(typeof(Collider))]
public class DefaultAttackProjectile : BaseAttack
{
    [SerializeField] private float lifetime = 3f;

    private Unit owner;
    private Vector3 direction;
    private float speed;
    private float despawnTime;
    private bool launched;

    public void Launch(Unit owner, Vector3 direction, float speed, int damage, LayerMask targetLayer)
    {
        this.owner = owner;
        this.direction = direction.sqrMagnitude >= 0.001f ? direction.normalized : transform.forward;
        this.speed = Mathf.Max(0f, speed);
        despawnTime = Time.time + Mathf.Max(0.1f, lifetime);
        launched = true;

        SetDamageSnapshot(damage);
        SetTargetLayer(targetLayer);
        SetAttackType(AttackType.Default);
    }

    private void Update()
    {
        if (!launched)
            return;

        transform.position += direction * speed * Time.deltaTime;

        if (Time.time >= despawnTime)
            Destroy(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!launched || !IsServer)
            return;

        // targetLayer 밖의 콜라이더(아군 Hurtbox 등)가 Unit 폴백으로 피해를 입지 않도록 차단
        if (!IsInTargetLayer(other))
            return;

        if (TryGetHurtbox(other, out Hurtbox hurtbox))
        {
            hurtbox.TryGetOwner(out Unit ownerUnit);
            if (ownerUnit == owner)
                return;

            if (TryResolveHit(hurtbox, other))
                Destroy(gameObject);

            return;
        }

        Unit target = other.GetComponentInParent<Unit>();
        if (target == null || target == owner)
            return;

        if (TryResolveHit(target))
            Destroy(gameObject);
    }
}
