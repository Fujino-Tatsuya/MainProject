using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 거너 직선 판정(서버 전용). 굵은 직선(SphereCast)에서 <b>가장 먼저 닿은 유효 대상 1개</b>만 맞힌다.
/// 지형(blocking 마스크)에 먼저 닿으면 그 지점에서 끝. 적·공격 가능한 오브젝트는 관통하지 않는다(§4.2).
/// 죽은 대상(시체 콜라이더)은 건너뛴다 — 공격을 거절하므로 "맞은 대상"이 아니다.
/// 특성(할 일 127): 관통 탄도 = <see cref="FireThrough"/>(적 관통·벽 차단), 대상별 수정 = <see cref="TargetHitHook"/>(집중 포화·충격 레이저),
/// 파생 공격 = <see cref="TraitHitFlags.Derived"/>(적중 사건·적중 시 추가 피해 없음).
/// </summary>
public class GunnerBeamAttack : BaseAttack
{
    private const int MaxHits = 32;

    /// <summary>[서버] 판정 1건 직전 대상별 수정 — 피해(집중 포화)·넉백 필드(충격 레이저). 주인 없는 히트박스면 target = null.</summary>
    public delegate void TargetHitHook(Unit target, ref AttackInfo info);

    private readonly RaycastHit[] hits = new RaycastHit[MaxHits];
    private readonly List<Unit> landedBuffer = new List<Unit>(1);
    private static readonly Comparison<RaycastHit> ByDistance = (a, b) => a.distance.CompareTo(b.distance);
    private Player owner;

    private void Awake()
    {
        owner = GetComponent<Player>();
        SetAttackType(AttackType.Default);
    }

    /// <param name="end">판정이 끝난 지점(명중점·지형 충돌점·최대 사거리) — 연출용</param>
    /// <param name="flags">원래 공격 = Original, 파생 공격 = Derived(적중 사건·적중 시 추가 피해 없음)</param>
    /// <param name="hook">대상별 수정(집중 포화 등). 없으면 null</param>
    /// <returns>대상을 맞혔는가</returns>
    public bool Fire(Vector3 origin, Vector3 direction, float range, float radius, LayerMask hittable, LayerMask blocking,
                     int damageSnapshot, bool triggersOnHit, out Vector3 end,
                     TraitHitFlags flags = TraitHitFlags.Original, TargetHitHook hook = null)
    {
        end = origin + direction * range;
        if (!IsServer)
            return false;

        SetTargetLayer(hittable);
        SetAttackType(AttackType.Default);
        SetHitPattern(AttackHitPattern.Single);
        SetDamageSnapshot(damageSnapshot);

        int count = Physics.SphereCastNonAlloc(origin, radius, direction, hits, range, hittable | blocking,
                                               QueryTriggerInteraction.Collide);
        Array.Sort(hits, 0, count, Comparer<RaycastHit>.Create(ByDistance));

        for (int i = 0; i < count; i++)
        {
            Collider col = hits[i].collider;
            if (col == null)
                continue;

            // 자기 몸(시체 콜라이더·자식 오브젝트 등)은 대상도 벽도 아니다.
            if (owner != null && col.transform.IsChildOf(owner.transform))
                continue;

            // SphereCast 가 시작부터 겹친 대상은 distance 0 · point 0 으로 온다 — 시작점으로 본다.
            Vector3 point = hits[i].distance > 0f ? origin + direction * hits[i].distance : origin;

            if (!IsInTargetLayer(col))
            {
                // 지형은 실제 막힘(비트리거)만 — 맵의 트리거 영역(투명화 존 등)이 Default 레이어라 레이저를 끊지 않게 한다.
                if (!col.isTrigger && (blocking.value & (1 << col.gameObject.layer)) != 0)
                {
                    end = point;
                    return false;
                }
                continue;
            }

            if (TryHit(col, triggersOnHit, flags, hook))
            {
                end = point;
                return true;
            }
        }

        return false;
    }

    // ── 관통 탄도(거너 기본 공격 특성) ─────────────────────────────────────

    private readonly List<GunnerBeamCandidate> rawCandidates = new List<GunnerBeamCandidate>();
    private readonly List<GunnerBeamCandidate> orderedCandidates = new List<GunnerBeamCandidate>();
    private readonly List<Collider> candidateColliders = new List<Collider>();

    /// <summary>
    /// [서버] 관통 탄도(GUN_3_PIERCE) — <see cref="Fire"/> 와 같은 굵은 직선이 적을 꿰뚫고 지형(비트리거 blocking)에서 멈춘다(원래 사거리 유지).
    /// 대상 단위로 합쳐 거리순(동률 = 네트워크 ID) 한 발당 대상별 1회, 첫 적중 100%·이후 각각 <paramref name="secondaryRatio"/>(<see cref="GunnerPierceRules"/>).
    /// 적중 시 추가 피해는 첫 적중에만(관통 전과 같은 '첫 대상').
    /// </summary>
    /// <param name="end">지형 충돌점 또는 최대 사거리 — 연출용(관통이라 적에서 멈추지 않는다)</param>
    public bool FireThrough(Vector3 origin, Vector3 direction, float range, float radius, LayerMask hittable, LayerMask blocking,
                            int damageSnapshot, float secondaryRatio, bool triggersOnHit, out Vector3 end,
                            TraitHitFlags flags = TraitHitFlags.Original, TargetHitHook hook = null)
    {
        end = origin + direction * range;
        if (!IsServer)
            return false;

        SetTargetLayer(hittable);
        SetAttackType(AttackType.Default);
        SetHitPattern(AttackHitPattern.Single);
        SetDamageSnapshot(damageSnapshot);

        int count = Physics.SphereCastNonAlloc(origin, radius, direction, hits, range, hittable | blocking,
                                               QueryTriggerInteraction.Collide);
        float wall = float.PositiveInfinity;
        rawCandidates.Clear();
        candidateColliders.Clear();

        for (int i = 0; i < count; i++)
        {
            Collider col = hits[i].collider;
            if (col == null || (owner != null && col.transform.IsChildOf(owner.transform)))
                continue;

            if (!IsInTargetLayer(col))
            {
                // Fire 와 같은 지형 규칙 — 실제 막힘(비트리거)만.
                if (!col.isTrigger && (blocking.value & (1 << col.gameObject.layer)) != 0 && hits[i].distance < wall)
                    wall = hits[i].distance;
                continue;
            }

            if (!TryResolveTarget(col, out Hurtbox hurtbox, out Unit unit))
                continue;

            int key = unit != null ? unit.GetInstanceID() : hurtbox.GetInstanceID();
            ulong networkId = unit != null ? unit.NetworkObjectId : 0UL;
            rawCandidates.Add(new GunnerBeamCandidate(key, networkId, hits[i].distance, candidateColliders.Count));
            candidateColliders.Add(col);
        }

        if (!float.IsPositiveInfinity(wall))
            end = wall > 0f ? origin + direction * wall : origin;

        GunnerPierceRules.Order(rawCandidates, wall, orderedCandidates);

        landedBuffer.Clear();
        int resolved = 0;
        for (int i = 0; i < orderedCandidates.Count; i++)
        {
            Collider col = candidateColliders[orderedCandidates[i].SourceIndex];
            if (!TryResolveTarget(col, out Hurtbox hurtbox, out Unit unit))
                continue;

            int hitDamage = GunnerPierceRules.Damage(damageSnapshot, resolved, secondaryRatio);
            if (!ResolveTarget(hurtbox, col, unit, hitDamage, triggersOnHit && resolved == 0, flags, hook))
                continue;

            resolved++;
            if (unit != null)
                landedBuffer.Add(unit);
        }

        if (resolved > 0 && (flags & TraitHitFlags.CanProcTraits) != 0)
            RaiseLanded(triggersOnHit);
        return resolved > 0;
    }

    // 콜라이더 → 맞을 대상(히트박스·유닛). 자기 자신·시체·유닛 없는 비히트박스는 false — Fire 의 TryHit 과 같은 거름.
    private bool TryResolveTarget(Collider col, out Hurtbox hurtbox, out Unit unit)
    {
        unit = null;
        if (TryGetHurtbox(col, out hurtbox))
        {
            hurtbox.TryGetOwner(out unit);
            return unit != owner && (unit == null || unit.CurrentHealth > 0);
        }

        unit = col.GetComponentInParent<Unit>();
        return unit != null && unit != owner && unit.CurrentHealth > 0;
    }

    // ── 관통 판정(거너 Q) ─────────────────────────────────────────────────

    private readonly Collider[] boxHits = new Collider[64];
    private readonly HashSet<Unit> pierceUnits = new HashSet<Unit>();
    private readonly HashSet<Hurtbox> pierceHurtboxes = new HashSet<Hurtbox>();
    private readonly List<Unit> pierceLanded = new List<Unit>();

    /// <summary>
    /// 직전 <see cref="FirePiercing"/> 에서 <b>실제로 피해가 들어간</b> 유닛들(중복 없음).
    ///
    /// 관통 연출용이다 — 끝점에서 한 번만 터뜨리면 여러 마리를 꿰뚫어도 한 마리만
    /// 맞은 것처럼 보인다. 호출자는 <b>그 프레임 안에</b> 읽고 버릴 것(다음 발사에 덮어쓴다).
    /// </summary>
    public IReadOnlyList<Unit> LastPierceLanded => pierceLanded;

    /// <summary>
    /// 지형(비트리거 blocking)에 닿기까지의 거리. 굵은 직선(SphereCast)으로 잰다. 자기 콜라이더는 무시.
    /// </summary>
    public float CastLength(Vector3 origin, Vector3 direction, float range, float radius, LayerMask blocking)
    {
        int count = Physics.SphereCastNonAlloc(origin, radius, direction, hits, range, blocking, QueryTriggerInteraction.Ignore);
        float length = range;
        for (int i = 0; i < count; i++)
        {
            Collider col = hits[i].collider;
            if (col == null || (owner != null && col.transform.IsChildOf(owner.transform)))
                continue;
            if (hits[i].distance > 0f && hits[i].distance < length)
                length = hits[i].distance;
        }
        return length;
    }

    /// <summary>
    /// [서버] 직육면체(시작점→length) 안의 모든 유효 대상에게 1회씩 피해(관통). 적·공격 가능한 오브젝트 모두.
    /// </summary>
    /// <returns>맞힌 대상 수(Unit 이 아닌 오브젝트 포함)</returns>
    /// <param name="flags">원래 공격 = Original, 파생 공격(잔류 포격) = Derived(적중 사건·적중 시 추가 피해 없음)</param>
    /// <param name="hook">대상별 수정(집중 포화·충격 레이저). 없으면 null</param>
    public int FirePiercing(Vector3 origin, Vector3 direction, float length, float width, float height,
                            LayerMask hittable, int damageSnapshot, AttackType type,
                            AttackHitPattern pattern, bool triggersOnHit,
                            TraitHitFlags flags = TraitHitFlags.Original, TargetHitHook hook = null)
    {
        if (!IsServer)
            return 0;

        SetTargetLayer(hittable);
        SetAttackType(type);
        SetHitPattern(pattern);
        SetDamageSnapshot(damageSnapshot);
        pierceUnits.Clear();
        pierceHurtboxes.Clear();
        pierceLanded.Clear();

        int count = OverlapBeamBox(origin, direction, length, width, height, hittable, QueryTriggerInteraction.Collide);
        int resolved = 0;
        for (int i = 0; i < count; i++)
        {
            Collider col = boxHits[i];
            if (col == null || (owner != null && col.transform.IsChildOf(owner.transform)))
                continue;

            if (TryGetHurtbox(col, out Hurtbox hurtbox))
            {
                hurtbox.TryGetOwner(out Unit ownerUnit);
                if (ownerUnit == owner || pierceHurtboxes.Contains(hurtbox) ||
                    (ownerUnit != null && (pierceUnits.Contains(ownerUnit) || ownerUnit.CurrentHealth <= 0)))
                    continue;

                if (!ResolveTarget(hurtbox, col, ownerUnit, damageSnapshot, triggersOnHit, flags, hook))
                    continue;

                resolved++;
                pierceHurtboxes.Add(hurtbox);
                if (ownerUnit != null && pierceUnits.Add(ownerUnit))
                    pierceLanded.Add(ownerUnit);
                continue;
            }

            if (!IsInTargetLayer(col))
                continue;

            Unit target = col.GetComponentInParent<Unit>();
            if (target == null || target == owner || target.CurrentHealth <= 0 || pierceUnits.Contains(target))
                continue;

            if (!ResolveTarget(null, null, target, damageSnapshot, triggersOnHit, flags, hook))
                continue;

            resolved++;
            pierceUnits.Add(target);
            pierceLanded.Add(target);
        }

        if (owner != null && pierceLanded.Count > 0 && (flags & TraitHitFlags.CanProcTraits) != 0)
            owner.RaiseServerAttackLanded(attackType, triggersOnHit, pierceLanded, this);

        return resolved;
    }

    /// <summary>같은 직육면체 안의 살아 있는 아군 플레이어(시전자 제외)를 모은다 — 보호막 대상.</summary>
    public void CollectAllies(Vector3 origin, Vector3 direction, float length, float width, float height,
                              LayerMask allyLayers, List<Player> result)
    {
        result.Clear();
        int count = OverlapBeamBox(origin, direction, length, width, height, allyLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            Player ally = boxHits[i] != null ? boxHits[i].GetComponentInParent<Player>() : null;
            if (ally == null || ally == owner || ally.CurrentHealth <= 0 || result.Contains(ally))
                continue;
            result.Add(ally);
        }
    }

    // 상자 기하는 SkillHitVolume.Beam 한 곳 — 오너의 스킬 범위 외곽선(할 일 58)도 같은 값으로 맞을 적을 고른다.
    private int OverlapBeamBox(Vector3 origin, Vector3 direction, float length, float width, float height,
                               LayerMask mask, QueryTriggerInteraction triggers)
    {
        return SkillHitVolume.Beam(origin, direction, length, width, height, mask).Overlap(boxHits, mask, triggers);
    }

    private bool TryHit(Collider col, bool triggersOnHit, TraitHitFlags flags, TargetHitHook hook)
    {
        landedBuffer.Clear();

        if (TryGetHurtbox(col, out Hurtbox hurtbox))
        {
            hurtbox.TryGetOwner(out Unit ownerUnit);
            if (ownerUnit == owner || (ownerUnit != null && ownerUnit.CurrentHealth <= 0))
                return false;

            if (!ResolveTarget(hurtbox, col, ownerUnit, damage, triggersOnHit, flags, hook))
                return false;

            if (ownerUnit != null)
                landedBuffer.Add(ownerUnit);
            if ((flags & TraitHitFlags.CanProcTraits) != 0)
                RaiseLanded(triggersOnHit);
            return true;
        }

        Unit target = col.GetComponentInParent<Unit>();
        if (target == null || target == owner || target.CurrentHealth <= 0)
            return false;

        if (!ResolveTarget(null, null, target, damage, triggersOnHit, flags, hook))
            return false;

        landedBuffer.Add(target);
        if ((flags & TraitHitFlags.CanProcTraits) != 0)
            RaiseLanded(triggersOnHit);
        return true;
    }

    /// <summary>
    /// [서버] 판정 1건 — 피해 → 대상별 수정(hook: 집중 포화·밀치기) → 적중 시 추가 피해(원래 공격만) 순으로 공격 정보를 만들어 넣는다.
    /// 히트박스가 있으면 히트박스로(맞은 콜라이더 문맥 포함), 없으면 유닛으로 — <see cref="BaseAttack"/> 의 TryResolveHit 과 같은 수신 경로.
    /// </summary>
    private bool ResolveTarget(Hurtbox hurtbox, Collider col, Unit target, int baseDamage, bool triggersOnHit,
                               TraitHitFlags flags, TargetHitHook hook)
    {
        if (!IsServer || (hurtbox == null && target == null))
            return false;

        var info = new AttackInfo(baseDamage, attackType, hitPattern: hitPattern);
        hook?.Invoke(target, ref info);

        // 적중 시 추가 피해(패시브 등)는 원래 공격만 소모한다 — 파생 공격은 자원·패시브 없음(명세 §3).
        if ((flags & TraitHitFlags.CanGainResource) != 0 && target != null && owner != null)
        {
            int bonus = owner.ServerTakeOnHitBonus(triggersOnHit, target);
            if (bonus > 0)
                info.damage = bonus >= int.MaxValue - info.damage ? int.MaxValue : info.damage + bonus;
        }

        bool resolved = hurtbox != null
            ? hurtbox.ReceiveAttack(info, CreateHitContext(col))
            : target.ReceiveAttack(info, CreateHitContext(null));
        if (resolved)
            Edit.Log($"[Attack] {name} -> {(target != null ? target.name : hurtbox.name)} 적중 (피해 {info.damage}, {info.attackType}" +
                     $"{((flags & TraitHitFlags.CanProcTraits) == 0 ? ", 파생" : "")})", this);
        return resolved;
    }

    private void RaiseLanded(bool triggersOnHit)
    {
        if (owner != null)
            owner.RaiseServerAttackLanded(attackType, triggersOnHit, landedBuffer, this);
    }
}
