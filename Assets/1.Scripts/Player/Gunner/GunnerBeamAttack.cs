using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 거너 직선 판정(서버 전용). 굵은 직선(SphereCast)에서 <b>가장 먼저 닿은 유효 대상 1개</b>만 맞힌다.
/// 지형(blocking 마스크)에 먼저 닿으면 그 지점에서 끝. 적·공격 가능한 오브젝트는 관통하지 않는다(§4.2).
/// 죽은 대상(시체 콜라이더)은 건너뛴다 — 공격을 거절하므로 "맞은 대상"이 아니다.
/// </summary>
public class GunnerBeamAttack : BaseAttack
{
    private const int MaxHits = 32;

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
    /// <returns>대상을 맞혔는가</returns>
    public bool Fire(Vector3 origin, Vector3 direction, float range, float radius, LayerMask hittable, LayerMask blocking,
                     int damageSnapshot, bool triggersOnHit, out Vector3 end)
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

            if (TryHit(col, triggersOnHit))
            {
                end = point;
                return true;
            }
        }

        return false;
    }

    // ── 관통 판정(거너 Q) ─────────────────────────────────────────────────

    private readonly Collider[] boxHits = new Collider[64];
    private readonly HashSet<Unit> pierceUnits = new HashSet<Unit>();
    private readonly HashSet<Hurtbox> pierceHurtboxes = new HashSet<Hurtbox>();
    private readonly List<Unit> pierceLanded = new List<Unit>();

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
    public int FirePiercing(Vector3 origin, Vector3 direction, float length, float width, float height,
                            LayerMask hittable, int damageSnapshot, AttackType type,
                            AttackHitPattern pattern, bool triggersOnHit)
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

                if (!TryResolveHit(hurtbox, col, OnHitDamage(ownerUnit, triggersOnHit)))
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

            if (!TryResolveHit(target, OnHitDamage(target, triggersOnHit)))
                continue;

            resolved++;
            pierceUnits.Add(target);
            pierceLanded.Add(target);
        }

        if (owner != null && pierceLanded.Count > 0)
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

    private int OverlapBeamBox(Vector3 origin, Vector3 direction, float length, float width, float height,
                               LayerMask mask, QueryTriggerInteraction triggers)
    {
        Vector3 center = origin + direction * (length * 0.5f);
        var halfExtents = new Vector3(width * 0.5f, height * 0.5f, length * 0.5f);
        return Physics.OverlapBoxNonAlloc(center, halfExtents, boxHits, Quaternion.LookRotation(direction), mask, triggers);
    }

    private bool TryHit(Collider col, bool triggersOnHit)
    {
        landedBuffer.Clear();

        if (TryGetHurtbox(col, out Hurtbox hurtbox))
        {
            hurtbox.TryGetOwner(out Unit ownerUnit);
            if (ownerUnit == owner || (ownerUnit != null && ownerUnit.CurrentHealth <= 0))
                return false;

            if (!TryResolveHit(hurtbox, col, OnHitDamage(ownerUnit, triggersOnHit)))
                return false;

            if (ownerUnit != null)
                landedBuffer.Add(ownerUnit);
            RaiseLanded(triggersOnHit);
            return true;
        }

        Unit target = col.GetComponentInParent<Unit>();
        if (target == null || target == owner || target.CurrentHealth <= 0)
            return false;

        if (!TryResolveHit(target, OnHitDamage(target, triggersOnHit)))
            return false;

        landedBuffer.Add(target);
        RaiseLanded(triggersOnHit);
        return true;
    }

    private int? OnHitDamage(Unit target, bool triggersOnHit)
    {
        if (target == null || owner == null)
            return null;

        int bonus = owner.ServerTakeOnHitBonus(triggersOnHit, target);
        if (bonus <= 0)
            return null;
        return bonus >= int.MaxValue - damage ? int.MaxValue : damage + bonus;
    }

    private void RaiseLanded(bool triggersOnHit)
    {
        if (owner != null)
            owner.RaiseServerAttackLanded(attackType, triggersOnHit, landedBuffer, this);
    }
}
