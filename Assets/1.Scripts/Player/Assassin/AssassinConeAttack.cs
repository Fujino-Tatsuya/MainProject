using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 어쌔신 평타의 서버 부채꼴 판정. 피해 진입점과 적중 보너스/통지는 <see cref="PlayerDefaultAttack"/>과 같은 계약을 쓴다.
/// </summary>
[RequireComponent(typeof(Player))]
public sealed class AssassinConeAttack : BaseAttack
{
    private Collider[] hitResults = new Collider[32];
    private readonly HashSet<Unit> damagedUnits = new HashSet<Unit>();
    private readonly HashSet<Hurtbox> damagedHurtboxes = new HashSet<Hurtbox>();
    private readonly List<Unit> landedUnits = new List<Unit>();

    private Player owner;

    private void Awake()
    {
        owner = GetComponent<Player>();
        SetAttackType(AttackType.Default);
        SetHitPattern(AttackHitPattern.Single);
    }

    public void Configure(LayerMask hittableLayers, int maxHitResults)
    {
        SetTargetLayer(hittableLayers);
        int size = Mathf.Max(1, maxHitResults);
        if (hitResults.Length != size)
            hitResults = new Collider[size];
    }

    /// <summary>서버에서 한 타당 한 번 호출한다.</summary>
    public bool Fire(
        Vector3 direction,
        float range,
        float angle,
        int damageSnapshot,
        bool triggersOnHit)
    {
        if (!IsServer || owner == null)
            return false;

        direction.y = 0f;
        direction = direction.sqrMagnitude >= 0.001f ? direction.normalized : transform.forward;
        range = Mathf.Max(0f, range);
        float halfAngle = Mathf.Clamp(angle, 0f, 360f) * 0.5f;

        SetDamageSnapshot(damageSnapshot);
        damagedUnits.Clear();
        damagedHurtboxes.Clear();
        landedUnits.Clear();

        bool anyResolved = false;
        bool onHitBonusTaken = false;
        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            range,
            hitResults,
            targetLayer,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            Collider hit = hitResults[i];
            if (hit == null || !IsInsideCone(hit, direction, range, halfAngle))
                continue;

            if (TryGetHurtbox(hit, out Hurtbox hurtbox))
            {
                hurtbox.TryGetOwner(out Unit target);
                if (target == owner || damagedHurtboxes.Contains(hurtbox) ||
                    (target != null && damagedUnits.Contains(target)))
                    continue;

                int? resolvedDamage = TakeOnHitBonus(target, triggersOnHit, ref onHitBonusTaken);
                if (!TryResolveHit(hurtbox, hit, resolvedDamage))
                    continue;

                anyResolved = true;
                damagedHurtboxes.Add(hurtbox);
                if (target != null)
                {
                    damagedUnits.Add(target);
                    landedUnits.Add(target);
                }

                continue;
            }

            Unit unit = hit.GetComponentInParent<Unit>();
            if (unit == null || unit == owner || damagedUnits.Contains(unit))
                continue;

            int? unitDamage = TakeOnHitBonus(unit, triggersOnHit, ref onHitBonusTaken);
            if (!TryResolveHit(unit, unitDamage))
                continue;

            anyResolved = true;
            damagedUnits.Add(unit);
            landedUnits.Add(unit);
        }

        if (landedUnits.Count > 0)
            owner.RaiseServerAttackLanded(AttackType.Default, triggersOnHit, landedUnits, this);

        return anyResolved;
    }

    private bool IsInsideCone(Collider hit, Vector3 forward, float range, float halfAngle)
    {
        Vector3 closest = hit.ClosestPoint(transform.position);
        Vector3 toTarget = closest - transform.position;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude > range * range)
            return false;

        // 발밑을 포함하는 콜라이더는 중심 방향으로 한 번 더 판정한다.
        if (toTarget.sqrMagnitude < 0.0001f)
        {
            toTarget = hit.bounds.center - transform.position;
            toTarget.y = 0f;
        }

        return toTarget.sqrMagnitude < 0.0001f || Vector3.Angle(forward, toTarget) <= halfAngle;
    }

    private int? TakeOnHitBonus(Unit target, bool triggersOnHit, ref bool bonusTaken)
    {
        if (bonusTaken || target == null || target.CurrentHealth <= 0)
            return null;

        bonusTaken = true;
        int bonus = owner.ServerTakeOnHitBonus(triggersOnHit, target);
        if (bonus <= 0)
            return null;

        return bonus >= int.MaxValue - damage ? int.MaxValue : damage + bonus;
    }
}
