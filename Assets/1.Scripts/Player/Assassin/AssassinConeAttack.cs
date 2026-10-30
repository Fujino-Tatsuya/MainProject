using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 어쌔신 평타의 서버 판정. 피해 진입점과 적중 보너스/통지는 <see cref="PlayerDefaultAttack"/>과 같은 계약을 쓴다.
/// 판정 모양 = 공격 판정 도형(<see cref="AttackShapeSet"/>, 할 일 153 — 기준점 = 시전자 발밑 + 그 타의 조준 방향). 질의는 <see cref="AttackShapeQuery"/>.
/// 클래스 이름은 프리팹 참조 때문에 그대로 둔다.
/// </summary>
[RequireComponent(typeof(Player))]
public sealed class AssassinConeAttack : BaseAttack
{
    private Collider[] hitResults = new Collider[32];
    private readonly HashSet<Object> hitTargets = new HashSet<Object>();
    private readonly List<AttackShapeHit> shapeHits = new List<AttackShapeHit>();
    private readonly List<Unit> landedUnits = new List<Unit>();

    private Player owner;
    private PlayerTraitEffects traitEffects;
    private System.Func<Collider, Object> resolveTarget;

    /// <summary>[서버] 마지막 <see cref="Fire"/> 에서 맞은 Unit(상자 등 Unit 이 아닌 대상은 없다). 다음 Fire 전까지만 유효.</summary>
    public IReadOnlyList<Unit> LastLandedUnits => landedUnits;

    private void Awake()
    {
        owner = GetComponent<Player>();
        resolveTarget = ResolveTarget;
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

    /// <summary>
    /// 서버에서 한 타당 한 번 호출한다. <paramref name="area"/> = 그 타의 판정 도형(특성 배율을 바른 것), <paramref name="pose"/> = 발밑 + 조준 방향.
    /// <paramref name="castId"/> = 이 판정이 속한 기본 공격 동작의 시전 ID(특성 '동작당 1회' 묶음, 0 = 이 판정 하나).
    /// <paramref name="traitKey"/> 가 있으면 대상마다 적중 직전 특성 수정자(⑥ 밀치기 — 급소 파괴)를 거친다. 방향 = 플레이어 → 대상(같은 위치면 <paramref name="direction"/>).
    /// </summary>
    public bool Fire(
        Vector3 direction,
        AttackShapeSet area,
        in AttackShapePose pose,
        int damageSnapshot,
        bool triggersOnHit,
        int castId = 0,
        TraitAttackKey? traitKey = null)
    {
        if (!IsServer || owner == null)
            return false;

        direction.y = 0f;
        direction = direction.sqrMagnitude >= 0.001f ? direction.normalized : transform.forward;
        if (traitKey.HasValue)
            traitEffects ??= PlayerTraitEffects.For(owner);

        SetDamageSnapshot(damageSnapshot);
        landedUnits.Clear();

        bool anyResolved = false;
        bool onHitBonusTaken = false;
        CollectHits(area, pose);

        foreach (AttackShapeHit shapeHit in shapeHits)
        {
            Collider hit = shapeHit.Collider;
            if (TryGetHurtbox(hit, out Hurtbox hurtbox))
            {
                hurtbox.TryGetOwner(out Unit target);
                int? resolvedDamage = TakeOnHitBonus(target, triggersOnHit, ref onHitBonusTaken);
                bool backAttack = AssassinHitTargets.IsBackAttack(target, _attackInfo, CreateHitContext(hit));
                bool resolved = traitKey.HasValue
                    ? ResolveWithTraitModifiers(hurtbox, null, hit, resolvedDamage, traitKey.Value, castId, target, direction)
                    : TryResolveHit(hurtbox, hit, resolvedDamage);
                if (!resolved)
                    continue;

                if (backAttack)
                    AssassinHitTargets.NotifyBackAttackHit(owner, hit, target, CreateHitContext(hit));
                anyResolved = true;
                if (target != null)
                    landedUnits.Add(target);

                continue;
            }

            if (!(shapeHit.Target is Unit unit))
                continue;

            int? unitDamage = TakeOnHitBonus(unit, triggersOnHit, ref onHitBonusTaken);
            bool unitBackAttack = AssassinHitTargets.IsBackAttack(unit, _attackInfo, CreateHitContext(null));
            bool unitResolved = traitKey.HasValue
                ? ResolveWithTraitModifiers(null, unit, null, unitDamage, traitKey.Value, castId, unit, direction)
                : TryResolveHit(unit, unitDamage);
            if (!unitResolved)
                continue;

            if (unitBackAttack)
                AssassinHitTargets.NotifyBackAttackHit(owner, hit, unit, CreateHitContext(null));
            anyResolved = true;
            landedUnits.Add(unit);
        }

        if (landedUnits.Count > 0)
            owner.RaiseServerAttackLanded(AttackType.Default, triggersOnHit, landedUnits, this, castId);

        return anyResolved;
    }

    /// <summary>
    /// [서버] 파생 공격(⑧ 끝없는 칼날) 판정 — 저장한 판정 도형·포즈(<paramref name="pose"/> = 그때 시전자 발밑·조준 방향)에 피해만 한 번(<see cref="TraitHitFlags.Derived"/>).
    /// 적중 보너스·특성 사건(<see cref="Player.RaiseServerAttackLanded"/>)·밀치기·백어택 연출 없음. 백어택 피해 배율은 맞는 쪽 규칙대로(지금 상태).
    /// 대상별 1회는 이 판정 안에서만 센다. 반환 = 맞은 대상 수.
    /// <paramref name="landedResult"/> 가 있으면 맞은 유닛을 덧붙인다(특성 연출 사건용, 할 일 145 — 분노 등 원래 적중 목록과 따로).
    /// </summary>
    public int FireDerived(AttackShapeSet area, in AttackShapePose pose, int damageSnapshot, List<Unit> landedResult = null)
    {
        if (!IsServer || owner == null)
            return 0;

        SetDamageSnapshot(damageSnapshot);
        landedUnits.Clear();

        int landed = 0;
        CollectHits(area, pose);
        foreach (AttackShapeHit shapeHit in shapeHits)
        {
            Collider hit = shapeHit.Collider;
            Hurtbox hurtbox = hit.GetComponentInParent<Hurtbox>();
            Unit unit = null;
            if (hurtbox != null)
                hurtbox.TryGetOwner(out unit);
            else
                unit = shapeHit.Target as Unit;

            if (hurtbox == null && unit == null)
                continue;

            AttackInfo info = _attackInfo;
            var context = new AttackHitContext(pose.Position, transform, hurtbox != null ? hit : null, owner);
            bool resolved = hurtbox != null ? hurtbox.ReceiveAttack(info, context) : unit.ReceiveAttack(info, context);
            if (!resolved)
                continue;

            landed++;
            if (unit != null)
                landedResult?.Add(unit);
            Edit.Log($"[Assassin/평타] 파생 적중 — {(unit != null ? unit.name : hurtbox.name)} 피해 {info.damage}", this);
        }

        landedUnits.Clear();   // 파생은 원래 적중 목록(분노 등)에 남기지 않는다
        return landed;
    }

    // 도형 질의 — 같은 대상(Unit, 없으면 Hurtbox)은 이 판정에서 1회. 판정마다 새로 센다.
    private void CollectHits(AttackShapeSet area, in AttackShapePose pose)
    {
        hitTargets.Clear();
        shapeHits.Clear();
        AttackShapeQuery.Collect(area, pose, targetLayer, QueryTriggerInteraction.Collide, hitResults, resolveTarget, hitTargets, shapeHits);
    }

    // 1회 피격 키 — Hurtbox 면 그 주인 Unit(없으면 Hurtbox 자체), 아니면 부모 Unit. 시전자 자신·대상 아님은 null.
    private Object ResolveTarget(Collider hit)
    {
        if (TryGetHurtbox(hit, out Hurtbox hurtbox))
        {
            hurtbox.TryGetOwner(out Unit target);
            if (target == null)
                return hurtbox;
            return target == owner ? null : target;
        }

        Unit unit = hit.GetComponentInParent<Unit>();
        return unit == null || unit == owner ? null : unit;
    }

    // 원래 타격 1건 — 공격 정보를 대상마다 복사해 특성 수정자(⑥ 밀치기)를 거친 뒤 넣는다(BaseAttack.TryResolveHit 와 같은 정보·문맥).
    private bool ResolveWithTraitModifiers(Hurtbox hurtbox, Unit unit, Collider hit, int? overrideDamage,
        TraitAttackKey key, int castId, Unit target, Vector3 aimDirection)
    {
        AttackInfo info = _attackInfo;
        if (overrideDamage.HasValue)
            info.damage = Mathf.Max(0, overrideDamage.Value);

        if (traitEffects != null && target != null)
        {
            Vector3 pushDirection = AssassinStrikePushRules.Direction(transform.position, target.transform.position, aimDirection);
            traitEffects.ModifyOutgoingAttackServer(key, castId, target, pushDirection, ref info);
        }

        AttackHitContext context = CreateHitContext(hit);
        bool resolved = hurtbox != null ? hurtbox.ReceiveAttack(info, context) : unit != null && unit.ReceiveAttack(info, context);
        if (resolved)
            Edit.Log($"[Attack] {name} -> {(target != null ? target.name : hurtbox != null ? hurtbox.name : "?")} 적중 (피해 {info.damage}, {info.attackType}{(info.knockbackDuration > 0f ? ", 밀치기" : string.Empty)})", this);
        return resolved;
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
