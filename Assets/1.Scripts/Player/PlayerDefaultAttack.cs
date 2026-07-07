using System.Collections.Generic;
using UnityEngine;

public class PlayerDefaultAttack : BaseAttack
{
    [SerializeField] private int maxHitResults = 16;

    private readonly HashSet<Unit> damagedUnits = new HashSet<Unit>();
    private Collider[] hitResults;
    private Player owner;
    private ColliderInfo currentHitbox;
    private ColliderInfo fallbackHitbox;
    private DefaultAttackStep currentStep;
    private Vector3 attackDirection;

    private void Awake()
    {
        owner = GetComponent<Player>();
        hitResults = new Collider[Mathf.Max(1, maxHitResults)];
        SetAttackType(AttackType.Default);
    }

    public void Configure(ColliderInfo defaultHitbox, LayerMask hittableLayers, int maxResults)
    {
        fallbackHitbox = defaultHitbox;
        SetTargetLayer(hittableLayers);

        int resultCount = Mathf.Max(1, maxResults);
        if (hitResults == null || hitResults.Length != resultCount)
            hitResults = new Collider[resultCount];
    }

    public void PrepareStep(DefaultAttackStep step, int damageSnapshot, Vector3 direction)
    {
        currentStep = step;
        currentHitbox = step != null && step.Hitbox != null ? step.Hitbox : fallbackHitbox;
        attackDirection = direction.sqrMagnitude >= 0.001f ? direction.normalized : transform.forward;
        SetDamageSnapshot(damageSnapshot);
        damagedUnits.Clear();
    }

    public void HitCurrentStep()
    {
        if (!IsServer)
            return;

        if (currentStep == null)
            return;

        switch (currentStep.HitType)
        {
            case DefaultAttackHitType.Overlap:
                HitOverlap();
                break;

            case DefaultAttackHitType.Projectile:
                SpawnProjectile();
                break;

            case DefaultAttackHitType.Raycast:
                HitRaycast();
                break;
        }
    }

    private void HitOverlap()
    {
        if (currentHitbox == null)
        {
            Debug.LogWarning("PlayerDefaultAttack requires a ColliderInfo hitbox.", this);
            return;
        }

        int hitCount = OverlapHitbox(currentHitbox);
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = hitResults[i];
            if (hit == null)
                continue;

            Unit target = hit.GetComponentInParent<Unit>();
            if (target == null || target == owner || damagedUnits.Contains(target))
                continue;

            if (TryResolveHit(target))
                damagedUnits.Add(target);
        }
    }

    private void SpawnProjectile()
    {
        if (currentStep.ProjectilePrefab == null)
        {
            Debug.LogWarning("Projectile default attack requires a projectile prefab.", this);
            return;
        }

        Transform muzzle = currentStep.Muzzle;
        Vector3 position = muzzle != null ? muzzle.position : transform.position;
        Quaternion rotation = Quaternion.LookRotation(attackDirection);
        GameObject projectileObject = Instantiate(currentStep.ProjectilePrefab, position, rotation);

        if (!projectileObject.TryGetComponent(out DefaultAttackProjectile projectile))
            projectile = projectileObject.AddComponent<DefaultAttackProjectile>();

        projectile.Launch(owner, attackDirection, currentStep.ProjectileSpeed, damage, targetLayer);
    }

    private void HitRaycast()
    {
        Transform muzzle = currentStep.Muzzle;
        Vector3 origin = muzzle != null ? muzzle.position : transform.position;

        if (!Physics.Raycast(origin, attackDirection, out RaycastHit hit, currentStep.RaycastRange, targetLayer, QueryTriggerInteraction.Ignore))
            return;

        Unit target = hit.collider.GetComponentInParent<Unit>();
        if (target == null || target == owner)
            return;

        TryResolveHit(target);
    }

    private int OverlapHitbox(ColliderInfo hitbox)
    {
        switch (hitbox.OverlapCollider)
        {
            case OverlapCollider.Box:
                BoxColliderInfo boxInfo = default;
                hitbox.GetBoxColliderInfo(ref boxInfo);
                return Physics.OverlapBoxNonAlloc(
                    boxInfo.center,
                    boxInfo.halfExtents,
                    hitResults,
                    boxInfo.orientation,
                    targetLayer,
                    QueryTriggerInteraction.Ignore);

            case OverlapCollider.Sphere:
                SphereColliderInfo sphereInfo = default;
                hitbox.GetSphereColliderInfo(ref sphereInfo);
                return Physics.OverlapSphereNonAlloc(
                    sphereInfo.center,
                    sphereInfo.radius,
                    hitResults,
                    targetLayer,
                    QueryTriggerInteraction.Ignore);

            case OverlapCollider.Capsule:
                CapsuleColliderInfo capsuleInfo = default;
                hitbox.GetCapsuleColliderInfo(ref capsuleInfo);
                return Physics.OverlapCapsuleNonAlloc(
                    capsuleInfo.point0,
                    capsuleInfo.point1,
                    capsuleInfo.radius,
                    hitResults,
                    targetLayer,
                    QueryTriggerInteraction.Ignore);

            default:
                return 0;
        }
    }
}
