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
    private bool triggersOnHit;
    private bool onHitBonusTaken;
    // 1발 = 1판정. 명중 대상 1명을 Player.ServerAttackLanded 로 넘길 때 쓰는 버퍼.
    private readonly System.Collections.Generic.List<Unit> landedBuffer = new System.Collections.Generic.List<Unit>(1);

    protected override Unit AttackSourceUnit => owner;

    public void Launch(Unit owner, Vector3 direction, float speed, int damage, LayerMask targetLayer, bool triggersOnHit)
    {
        this.owner = owner;
        this.triggersOnHit = triggersOnHit;
        onHitBonusTaken = false;
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

            if (TryResolveHit(hurtbox, other, TakeOnHitBonus(ownerUnit)))
            {
                RaiseLanded(ownerUnit);
                Destroy(gameObject);
            }

            return;
        }

        Unit target = other.GetComponentInParent<Unit>();
        if (target == null || target == owner)
            return;

        if (TryResolveHit(target, TakeOnHitBonus(target)))
        {
            RaiseLanded(target);
            Destroy(gameObject);
        }
    }

    private int? TakeOnHitBonus(Unit target)
    {
        // 시체는 공격을 거절해 투사체가 관통한다 — 첫 대상 자리를 차지하지 않는다.
        if (onHitBonusTaken || target == null || target.CurrentHealth <= 0)
            return null;

        // 투사체 한 발 전체에서 살아 있는 첫 Unit 대상에게만 한 번 묻는다.
        onHitBonusTaken = true;
        int bonus = owner is Player player
            ? player.ServerTakeOnHitBonus(triggersOnHit, target)
            : 0;
        if (bonus <= 0)
            return null;

        return bonus >= int.MaxValue - damage ? int.MaxValue : damage + bonus;
    }

    // Unit 이 아닌 대상(상자 등)은 적중 통지에서 빠진다 — 평타 Overlap 과 같은 규칙.
    private void RaiseLanded(Unit target)
    {
        if (target == null || !(owner is Player player))
            return;

        landedBuffer.Clear();
        landedBuffer.Add(target);
        player.RaiseServerAttackLanded(attackType, triggersOnHit, landedBuffer, this);
    }
}
