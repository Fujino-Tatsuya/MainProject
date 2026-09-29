using System.Collections.Generic;
using UnityEngine;

public class PlayerDefaultAttack : BaseAttack
{
    // SO(DefaultAttackData) 미할당 시 사용하는 기본 버퍼 크기.
    private const int DefaultMaxHitResults = 16;

    // 투사체 생성/레이캐스트 시작 위치. 비워두면 자기 위치를 사용.
    [SerializeField] private Transform muzzle;

    // 적중 통지는 Player.ServerAttackLanded 하나로 모인다(평타·스킬·투사체 공용). 여기는 판정 1회마다 목록만 넘긴다.

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

    /// <summary>
    /// 이번 판정에서 <b>실제로 무언가를 맞췄는가.</b>
    ///
    /// 반환값을 쓰는 곳은 연출이다 — 헛스윙과 명중에 다른 이펙트를 내려면 이걸 밖에서 알아야 하는데,
    /// <b>애니메이션 이벤트로는 못 가른다</b>(클립은 맞았는지 모른다). 예전에는 이 정보가
    /// <see cref="HitOverlap"/> 안의 지역 변수로만 있다가 진단 로그에 쓰이고 버려졌다.
    ///
    /// ⚠️ 투사체는 **항상 false** 다. 발사 시점에는 맞을지 알 수 없다 — 명중은 날아간 뒤에 난다.
    ///    투사체 평타에 명중 연출이 필요해지면 <c>DefaultAttackProjectile</c> 쪽에서 따로 내야 한다.
    /// </summary>
    /// <returns>명중 대상이 하나라도 있으면 true. 서버가 아니거나 판정이 없었으면 false.</returns>
    public bool HitCurrentStep()
    {
        if (!IsServer)
            return false;

        if (currentStep == null)
            return false;

        switch (currentStep.HitType)
        {
            case DefaultAttackHitType.Overlap:
                return HitOverlap();

            case DefaultAttackHitType.Projectile:
                SpawnProjectile();
                return false;

            case DefaultAttackHitType.Raycast:
                return HitRaycast();
        }

        return false;
    }

    /// <returns>하나라도 명중했으면 true.</returns>
    private bool HitOverlap()
    {
        if (hitbox == null)
        {
            Edit.LogWarning("[Player] PlayerDefaultAttack requires a ColliderInfo hitbox.", this);
            return false;
        }

        swingHitBuffer.Clear();

        // "무언가 맞췄다"와 "Unit을 맞췄다"는 다르다. 파괴 가능한 상자처럼 Unit이 아닌
        // IAttackReceiver도 명중 대상이므로, 진단은 이쪽으로 판단해야 한다
        // (swingHitBuffer는 적중 통지(Player.ServerAttackLanded)용이라 Unit만 담는다 — 아래 참고).
        bool anyResolved = false;
        bool onHitBonusTaken = false;

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

                int? hurtboxDamage = TakeOverlapOnHitBonus(ownerUnit, ref onHitBonusTaken);
                bool resolved = TryResolveHit(hurtbox, hit, hurtboxDamage);
                if (resolved)
                {
                    anyResolved = true;
                    damagedHurtboxes.Add(hurtbox);

                    // Unit이 없는 대상(상자 등)은 중복 방지를 Hurtbox로 하고,
                    // 적중 통지에서는 빠진다 — 구독자는 Unit 대상만 받는다.
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

            int? unitDamage = TakeOverlapOnHitBonus(target, ref onHitBonusTaken);
            if (TryResolveHit(target, unitDamage))
            {
                anyResolved = true;
                damagedUnits.Add(target);
                swingHitBuffer.Add(target);
            }
        }

        // 이번 스윙에 명중시킨 적 통지. 허공 스윙(0명)은 발행되지 않는다.
        RaiseLanded();

        // 진단은 통지와 별개다. swingHitBuffer로 판단하면 Unit이 아닌 대상만 맞췄을 때
        // "전부 걸러졌다"고 거짓 보고한다(상자를 실제로 부수고도 경고가 찍혔다).
        if (!anyResolved)
            LogEmptySwing(hitCount);

        return anyResolved;
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

        projectile.Launch(owner, attackDirection, currentStep.ProjectileSpeed, damage, targetLayer, currentStep.TriggersOnHit);
    }

    // swingHitBuffer 를 이번 판정의 적중 목록으로 Player 에 넘긴다.
    private void RaiseLanded()
    {
        if (owner != null)
            owner.RaiseServerAttackLanded(attackType, currentStep != null && currentStep.TriggersOnHit, swingHitBuffer, this);
    }

    /// <returns>명중했으면 true. 레이가 빗나갔거나 대상이 자기 자신이면 false.</returns>
    private bool HitRaycast()
    {
        Vector3 origin = muzzle != null ? muzzle.position : transform.position;

        if (!Physics.Raycast(origin, attackDirection, out RaycastHit hit, currentStep.RaycastRange, targetLayer, QueryTriggerInteraction.Collide))
        {
            return false;
        }

        swingHitBuffer.Clear();

        if (TryGetHurtbox(hit.collider, out Hurtbox hurtbox))
        {
            hurtbox.TryGetOwner(out Unit ownerUnit);
            if (ownerUnit == owner ||
                !TryResolveHit(hurtbox, hit.collider, TakeRaycastOnHitBonus(ownerUnit)))
                return false;

            // Unit 이 아닌 대상(상자 등)은 적중 통지에서 빠진다 — Overlap 과 같은 규칙.
            if (ownerUnit != null)
                swingHitBuffer.Add(ownerUnit);

            RaiseLanded();
            return true;
        }

        Unit target = hit.collider.GetComponentInParent<Unit>();
        if (target == null || target == owner || !TryResolveHit(target, TakeRaycastOnHitBonus(target)))
            return false;

        swingHitBuffer.Add(target);
        RaiseLanded();
        return true;
    }

    private int? TakeOverlapOnHitBonus(Unit target, ref bool bonusTaken)
    {
        if (bonusTaken || target == null)
            return null;

        // 첫 Unit 대상이 죽어 있어 보너스를 거절해도 다음 대상으로 넘기지 않는다.
        bonusTaken = true;
        int bonus = owner != null
            ? owner.ServerTakeOnHitBonus(currentStep != null && currentStep.TriggersOnHit, target)
            : 0;
        return AddBonusToDamage(bonus);
    }

    private int? TakeRaycastOnHitBonus(Unit target)
    {
        if (target == null)
            return null;

        int bonus = owner != null
            ? owner.ServerTakeOnHitBonus(currentStep != null && currentStep.TriggersOnHit, target)
            : 0;
        return AddBonusToDamage(bonus);
    }

    private int? AddBonusToDamage(int bonus)
    {
        if (bonus <= 0)
            return null;

        return bonus >= int.MaxValue - damage ? int.MaxValue : damage + bonus;
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
