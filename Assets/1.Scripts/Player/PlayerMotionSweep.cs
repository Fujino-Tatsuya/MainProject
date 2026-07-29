using UnityEngine;

/// <summary>
/// 대시와 일반 이동이 공유하는 경사/벽 충돌 해석(단일 소스). (PLAN §5, §8)
///
/// - 걸을 수 있는 경사(법선 각 &lt;= maxWalkableAngle)는 장애물로 보지 않고 통과시켜 타고 오르게 한다.
/// - 급경사/벽/천장만 막고, 비스듬한 충돌은 접선으로 미끄러진다.
/// - Rigidbody.MovePosition이 지연 적용되므로 한 프레임의 모든 캐스트는 누적 오프셋으로 근사하고,
///   호출자는 반환된 delta로 MovePosition을 1회만 적용한다.
///
/// 캡슐 방향은 Y축(direction==1) 가정.
/// </summary>
public static class PlayerMotionSweep
{
    /// <summary>desiredDelta를 충돌 해석해 실제 적용할 이동량으로 보정해 반환한다.</summary>
    public static Vector3 Resolve(
        CapsuleCollider capsule,
        Vector3 desiredDelta,
        float maxWalkableAngle,
        LayerMask obstacleMask,
        float skin,
        int maxIterations,
        RaycastHit[] buffer)
    {
        if (capsule == null || buffer == null || desiredDelta.sqrMagnitude <= 1e-10f)
            return desiredDelta;

        Transform owner = capsule.transform;
        Vector3 accumulated = Vector3.zero;
        Vector3 remaining = desiredDelta;
        int iterations = Mathf.Max(1, maxIterations);

        for (int i = 0; i < iterations; i++)
        {
            float dist = remaining.magnitude;
            if (dist <= 1e-5f)
                break;

            Vector3 dir = remaining / dist;

            if (TryCast(capsule, owner, accumulated, dir, dist + skin, maxWalkableAngle, obstacleMask, buffer, out RaycastHit hit))
            {
                float allowed = Mathf.Max(0f, hit.distance - skin);
                accumulated += dir * allowed;
                Vector3 leftover = dir * (dist - allowed);
                remaining = Vector3.ProjectOnPlane(leftover, hit.normal);
            }
            else
            {
                accumulated += remaining;
                break;
            }
        }

        return accumulated;
    }

    private static bool TryCast(
        CapsuleCollider capsule,
        Transform owner,
        Vector3 originOffset,
        Vector3 dir,
        float maxDistance,
        float maxWalkableAngle,
        LayerMask obstacleMask,
        RaycastHit[] buffer,
        out RaycastHit best)
    {
        best = default;

        Vector3 lossy = owner.lossyScale;
        float radiusScale = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z));
        float heightScale = Mathf.Abs(lossy.y);
        float radius = capsule.radius * radiusScale;
        float height = Mathf.Max(capsule.height * heightScale, radius * 2f);
        float half = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 center = owner.TransformPoint(capsule.center) + originOffset;
        Vector3 up = owner.up;
        Vector3 p1 = center + up * half;
        Vector3 p2 = center - up * half;

        int count = Physics.CapsuleCastNonAlloc(
            p1, p2, radius, dir, buffer, maxDistance, obstacleMask, QueryTriggerInteraction.Ignore);

        float nearest = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = buffer[i];
            if (hit.collider == null)
                continue;
            if (hit.collider.transform == owner || hit.collider.transform.IsChildOf(owner))
                continue;
            // 걸을 수 있는 경사(지면)는 막지 않는다 — 타고 오른다. 초기 겹침(normal≈0)도 여기서 무시.
            if (Vector3.Angle(hit.normal, Vector3.up) <= maxWalkableAngle)
                continue;
            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                best = hit;
                found = true;
            }
        }

        return found;
    }
}
