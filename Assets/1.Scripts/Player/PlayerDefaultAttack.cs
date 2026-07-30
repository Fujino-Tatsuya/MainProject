using System.Collections.Generic;
using UnityEngine;

public class PlayerDefaultAttack : BaseAttack
{
    // SO(DefaultAttackData) 미할당 시 사용하는 기본 버퍼 크기.
    private const int DefaultMaxHitResults = 16;

    // 투사체 생성/레이캐스트 시작 위치. 비워두면 자기 위치를 사용.
    [SerializeField] private Transform muzzle;

    // 서버: 한 스윙(HitCurrentStep 1회)에서 명중시킨 적 목록을 통지한다. 패시브(불굴의 의지) 등이 구독한다.
    // 구독자가 없으면 무영향 — PlayerDefaultAttack은 구독자를 몰라도 된다.
    public event System.Action<IReadOnlyList<Unit>> ServerHitEnemiesResolved;

    private readonly HashSet<Unit> damagedUnits = new HashSet<Unit>();
    private readonly HashSet<Hurtbox> damagedHurtboxes = new HashSet<Hurtbox>();
    private readonly List<Unit> swingHitBuffer = new List<Unit>();
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
            Edit.LogWarning("[Player] PlayerDefaultAttack requires a ColliderInfo hitbox.", this);
            return;
        }

        swingHitBuffer.Clear();

        int hitCount = OverlapHitbox(hitbox);
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
                if (resolved)
                {
                    damagedHurtboxes.Add(hurtbox);
                    if (ownerUnit != null)
                    {
                        damagedUnits.Add(ownerUnit);
                        swingHitBuffer.Add(ownerUnit);
                    }
                }

                continue;
            }

            Unit target = hit.GetComponentInParent<Unit>();
            if (target == null || target == owner || damagedUnits.Contains(target))
                continue;

            if (TryResolveHit(target))
            {
                damagedUnits.Add(target);
                swingHitBuffer.Add(target);
            }
        }

        // 이번 스윙에 명중시킨 적이 있으면 통지 (패시브 발동 트리거). 허공 스윙은 통지하지 않는다.
        if (swingHitBuffer.Count > 0)
            ServerHitEnemiesResolved?.Invoke(swingHitBuffer);
        else
            LogEmptySwing(hitCount);
    }

    // 진단 — "때려도 안 맞는다"의 원인을 로그로 가른다(2026-07-30).
    // 적중 로그([Attack] …)는 성공 시에만 찍히므로, 실패한 스윙은 아무 흔적이 없어
    // ① 스윙 자체가 없었는지 ② 후보가 0인지 ③ 후보는 있는데 전부 걸러졌는지 구분할 수 없었다.
    // 유효 마스크도 함께 찍는다 — 프리팹은 256(Enemy)이고 SO(17664)가 런타임에 덮는 구조라,
    // ApplyData 가 안 돌면 EnemyHurtBox(14)가 마스크에서 빠져 보스를 못 때린다.
    private void LogEmptySwing(int hitCount)
    {
        if (hitCount == 0)
        {
            Edit.LogWarning(
                $"[Attack/진단] {name} 스윙 무효 — 히트박스 안 후보 0개, 유효 마스크={targetLayer.value}", this);
            return;
        }

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = hitResults[i];
            if (hit == null)
                continue;

            if (sb.Length > 0)
                sb.Append(", ");

            sb.Append(hit.name).Append("(layer ").Append(hit.gameObject.layer);
            sb.Append(hit.GetComponentInParent<Hurtbox>() != null ? ", hurtbox" : ", hurtbox없음");
            sb.Append(hit.GetComponentInParent<Unit>() != null ? ", unit)" : ", unit없음)");
        }

        Edit.LogWarning(
            $"[Attack/진단] {name} 스윙이 후보 {hitCount}개를 찾았지만 전부 걸러졌다 — " +
            $"유효 마스크={targetLayer.value} / 후보: {sb}", this);
    }

    private void SpawnProjectile()
    {
        if (currentStep.ProjectilePrefab == null)
        {
            Edit.LogWarning("[Player] Projectile default attack requires a projectile prefab.", this);
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
            return;
        }

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
