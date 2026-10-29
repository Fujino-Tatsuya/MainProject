using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>도형 판정에 걸린 대상 1개(할 일 153).</summary>
public readonly struct AttackShapeHit
{
    public readonly Collider Collider;
    /// <summary>1회 피격 키(Unit, 없으면 Hurtbox 등 — 호출자의 해석 함수가 정한다).</summary>
    public readonly Object Target;
    /// <summary>판정에 쓴 점(몸 표면 가장 가까운 점, 발밑 겹침이면 바닥 평면만 중심 쪽).</summary>
    public readonly Vector3 Point;

    public AttackShapeHit(Collider collider, Object target, Vector3 point)
    {
        Collider = collider;
        Target = target;
        Point = point;
    }
}

/// <summary>
/// [서버] 도형 판정 질의(할 일 153). 흐름: 도형마다 넓은 상자 <c>Physics.OverlapBoxNonAlloc</c> → 대상 콜라이더의 <c>ClosestPoint(기준점)</c>
/// (= 몸 표면에서 기준점에 가장 가까운 점, 예전 어쌔신·진격의 충격과 같은 점) → 그 점이 도형 하나라도 안이면 피격 → 같은 대상은 한 공격에 1회.
/// 발밑 겹침(가장 가까운 점 = 기준점 — 기준점이 몸 안)은 바닥 평면 위치만 콜라이더 중심으로 바꾼다(높이는 가장 가까운 점 그대로).
/// 순수 부분(<see cref="ProbePoint"/>·<see cref="AnyContains"/>·<see cref="TryAccept{TKey}"/>)은 Physics 없이 테스트한다.
/// </summary>
public static class AttackShapeQuery
{
    // 바닥 평면 거리 제곱이 이보다 작으면 "가장 가까운 점 = 기준점"(발밑 겹침) — 예전 QBlastInSector 와 같은 값.
    private const float FootOverlapSqr = 0.0001f;

    /// <summary>판정 점 — 보통 <paramref name="closestPoint"/>. 바닥 평면에서 기준점과 겹치면 x·z 만 <paramref name="boundsCenter"/>.</summary>
    public static Vector3 ProbePoint(Vector3 origin, Vector3 closestPoint, Vector3 boundsCenter)
    {
        float dx = closestPoint.x - origin.x;
        float dz = closestPoint.z - origin.z;
        if (dx * dx + dz * dz >= FootOverlapSqr)
            return closestPoint;

        return new Vector3(boundsCenter.x, closestPoint.y, boundsCenter.z);
    }

    /// <summary>합집합 — 도형 하나라도 점을 품는가. null 원소는 건너뛴다.</summary>
    public static bool AnyContains(IReadOnlyList<AttackShape> shapes, in AttackShapePose pose, Vector3 point)
    {
        if (shapes == null)
            return false;

        for (int i = 0; i < shapes.Count; i++)
        {
            if (shapes[i] != null && shapes[i].Contains(pose, point))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 순수 판정 1건 — 판정 점(<see cref="ProbePoint"/>)이 합집합 안이고 <paramref name="key"/> 가 이 공격에서 처음이면 true(그리고 <paramref name="seen"/> 에 넣는다).
    /// 키가 null 이면 false(대상 아님 — 자기 자신 등은 해석 함수가 null 을 낸다). 밖이면 seen 에 넣지 않는다(같은 대상의 다른 콜라이더가 안이면 맞는다).
    /// </summary>
    public static bool TryAccept<TKey>(IReadOnlyList<AttackShape> shapes, in AttackShapePose pose, Vector3 closestPoint, Vector3 boundsCenter,
        TKey key, HashSet<TKey> seen, out Vector3 probe)
    {
        probe = ProbePoint(pose.Position, closestPoint, boundsCenter);
        if (key == null || seen.Contains(key) || !AnyContains(shapes, pose, probe))
            return false;

        return seen.Add(key);
    }

    /// <summary>
    /// [서버] 도형 영역 안 대상을 모은다(<paramref name="results"/> 에 덧붙임, 반환 = 이번에 더한 수).
    /// <paramref name="resolveTarget"/> = 콜라이더 → 1회 피격 키(Unit, 없으면 Hurtbox). 시전자 자신·대상 아님은 null.
    /// <paramref name="seen"/> = 이 공격에서 이미 맞은 키 — 공격 하나 동안 유지하고 새 공격에서 비운다(여러 틱·여러 도형에 걸쳐도 1회).
    /// <paramref name="buffer"/> 가 꽉 차면 넘친 콜라이더는 빠진다(크기는 호출자가 정한다).
    /// </summary>
    public static int Collect(IReadOnlyList<AttackShape> shapes, in AttackShapePose pose, int layerMask, QueryTriggerInteraction triggers,
        Collider[] buffer, Func<Collider, Object> resolveTarget, HashSet<Object> seen, List<AttackShapeHit> results)
    {
        if (shapes == null || buffer == null || resolveTarget == null || seen == null || results == null)
            return 0;

        int added = 0;
        for (int s = 0; s < shapes.Count; s++)
        {
            AttackShape shape = shapes[s];
            if (shape == null)
                continue;

            shape.GetBroadBox(pose, out Vector3 center, out Vector3 halfExtents, out Quaternion rotation);
            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, buffer, rotation, layerMask, triggers);
            for (int i = 0; i < count; i++)
            {
                Collider hit = buffer[i];
                if (hit == null)
                    continue;

                Object target = resolveTarget(hit);
                if (target == null || seen.Contains(target))
                    continue;

                if (!TryAccept(shapes, pose, ClosestPoint(hit, pose.Position), hit.bounds.center, target, seen, out Vector3 probe))
                    continue;

                results.Add(new AttackShapeHit(hit, target, probe));
                added++;
            }
        }

        return added;
    }

    /// <summary><see cref="AttackShapeSet"/> 판. 특성 배율은 <see cref="AttackShapeSet.Scaled"/> 복사본을 넘긴다.</summary>
    public static int Collect(AttackShapeSet set, in AttackShapePose pose, int layerMask, QueryTriggerInteraction triggers,
        Collider[] buffer, Func<Collider, Object> resolveTarget, HashSet<Object> seen, List<AttackShapeHit> results) =>
        set == null ? 0 : Collect(set.Shapes, pose, layerMask, triggers, buffer, resolveTarget, seen, results);

    /// <summary>몸 표면에서 <paramref name="point"/> 에 가장 가까운 점. 비볼록 MeshCollider 는 ClosestPoint 를 못 써 경계 상자로(<c>UnitWallImpacts</c> 와 같다).</summary>
    public static Vector3 ClosestPoint(Collider collider, Vector3 point) =>
        collider is MeshCollider mesh && !mesh.convex ? collider.ClosestPointOnBounds(point) : collider.ClosestPoint(point);
}
