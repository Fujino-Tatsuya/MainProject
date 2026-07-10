using System.Collections.Generic;
using UnityEngine;

public class PlayerDefaultAttack : BaseAttack
{
    // SO(DefaultAttackData) 미할당 시 사용하는 기본 버퍼 크기.
    private const int DefaultMaxHitResults = 16;

    // 투사체 생성/레이캐스트 시작 위치. 비워두면 자기 위치를 사용.
    [SerializeField] private Transform muzzle;

    private readonly HashSet<Unit> damagedUnits = new HashSet<Unit>();
    private readonly HashSet<Hurtbox> damagedHurtboxes = new HashSet<Hurtbox>();
    private Collider[] hitResults = new Collider[DefaultMaxHitResults];
    private Player owner;
    private ColliderInfo defaultHitbox;
    private ColliderInfo hitbox;
    private DefaultAttackStep currentStep;
    private Vector3 attackDirection;

    private void Awake()
    {
        owner = GetComponent<Player>();
        SetAttackType(AttackType.Default);
    }

    public void Configure(ColliderInfo defaultHitbox, LayerMask hittableLayers, int maxHitResults = DefaultMaxHitResults)
    {
        this.defaultHitbox = defaultHitbox;
        hitbox = defaultHitbox;
        SetTargetLayer(hittableLayers);

        int resultCount = Mathf.Max(1, maxHitResults);
        if (hitResults.Length != resultCount)
            hitResults = new Collider[resultCount];
    }

    public void PrepareStep(DefaultAttackStep step, int damageSnapshot, Vector3 direction)
    {
        currentStep = step;
        attackDirection = direction.sqrMagnitude >= 0.001f ? direction.normalized : transform.forward;
        hitbox = step != null && step.Hitbox != null ? step.Hitbox : defaultHitbox;
        SetDamageSnapshot(damageSnapshot);
        damagedUnits.Clear();
        damagedHurtboxes.Clear();
    }

    public void HitCurrentStep()
    {
        BeforeMergeTestLog.Info(
            "HIT",
            "STEP_BEGIN",
            $"IsServer={IsServer}, step={(currentStep == null ? "null" : currentStep.HitType.ToString())}, mask={targetLayer.value}",
            this);

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
        if (hitbox == null)
        {
            Debug.LogWarning("PlayerDefaultAttack requires a ColliderInfo hitbox.", this);
            return;
        }

        int hitCount = OverlapHitbox(hitbox);
        BeforeMergeTestLog.Info("HIT", "OVERLAP_RESULT", $"hitCount={hitCount}", this);
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = hitResults[i];
            if (hit == null)
                continue;

            if (TryGetHurtbox(hit, out Hurtbox hurtbox))
            {
                hurtbox.TryGetOwner(out Unit ownerUnit);
                if (ownerUnit == owner || damagedHurtboxes.Contains(hurtbox))
                    continue;

                if (ownerUnit != null && damagedUnits.Contains(ownerUnit))
                    continue;

                bool resolved = TryResolveHit(hurtbox, hit);
                BeforeMergeTestLog.Info(
                    "HIT",
                    "HURTBOX_RESOLVE",
                    $"hit[{i}]={hit.name}, layer={hit.gameObject.layer}, resolved={resolved}",
                    hit);
                if (resolved)
                {
                    damagedHurtboxes.Add(hurtbox);
                    if (ownerUnit != null)
                        damagedUnits.Add(ownerUnit);
                }

                continue;
            }

            Unit target = hit.GetComponentInParent<Unit>();
            BeforeMergeTestLog.Info(
                "HIT",
                "UNIT_FALLBACK",
                $"hit[{i}]={hit.name}, layer={hit.gameObject.layer}, target={(target == null ? "Unit 없음" : target.name)}{(target == owner ? " (자기 자신, 스킵)" : "")}",
                hit);
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

        Vector3 position = muzzle != null ? muzzle.position : transform.position;
        Quaternion rotation = Quaternion.LookRotation(attackDirection);
        GameObject projectileObject = Instantiate(currentStep.ProjectilePrefab, position, rotation);

        if (!projectileObject.TryGetComponent(out DefaultAttackProjectile projectile))
            projectile = projectileObject.AddComponent<DefaultAttackProjectile>();

        projectile.Launch(owner, attackDirection, currentStep.ProjectileSpeed, damage, targetLayer);
    }

    private void HitRaycast()
    {
        Vector3 origin = muzzle != null ? muzzle.position : transform.position;

        if (!Physics.Raycast(origin, attackDirection, out RaycastHit hit, currentStep.RaycastRange, targetLayer, QueryTriggerInteraction.Collide))
        {
            BeforeMergeTestLog.Info("HIT", "RAYCAST_MISS", $"range={currentStep.RaycastRange}", this);
            return;
        }

        BeforeMergeTestLog.Info(
            "HIT",
            "RAYCAST_HIT",
            $"collider={hit.collider.name}, layer={hit.collider.gameObject.layer}",
            hit.collider);

        if (TryGetHurtbox(hit.collider, out Hurtbox hurtbox))
        {
            hurtbox.TryGetOwner(out Unit ownerUnit);
            if (ownerUnit != owner)
                TryResolveHit(hurtbox, hit.collider);

            return;
        }

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
                    QueryTriggerInteraction.Collide);

            case OverlapCollider.Sphere:
                SphereColliderInfo sphereInfo = default;
                hitbox.GetSphereColliderInfo(ref sphereInfo);
                return Physics.OverlapSphereNonAlloc(
                    sphereInfo.center,
                    sphereInfo.radius,
                    hitResults,
                    targetLayer,
                    QueryTriggerInteraction.Collide);

            case OverlapCollider.Capsule:
                CapsuleColliderInfo capsuleInfo = default;
                hitbox.GetCapsuleColliderInfo(ref capsuleInfo);
                return Physics.OverlapCapsuleNonAlloc(
                    capsuleInfo.point0,
                    capsuleInfo.point1,
                    capsuleInfo.radius,
                    hitResults,
                    targetLayer,
                    QueryTriggerInteraction.Collide);

            default:
                return 0;
        }
    }

}
