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

            if (TryCast(capsule, owner, accumulated, dir, dist + skin, maxWalkableAngle, obstacleMask, skin, buffer,
                    out RaycastHit hit, out float hitDistance))
            {
                float allowed = Mathf.Max(0f, hitDistance - skin);
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

    /// <param name="skin">
    /// 캐스트 반경을 줄이는 여유 두께.
    ///
    /// ⚠️ 캡슐 반경을 그대로 쏘면 <b>이미 닿아 있는 면</b>이 distance 0으로 히트한다. 특히 캡슐
    /// 하단이 지면에 1~2cm 파묻힌 상태(스폰 Y가 낮을 때)에서는 수평 스윕이 바닥 메시의 측면
    /// 삼각형(법선 수평 → 벽 판정)을 매 tick 때려 이동 전량이 클램프된다 — 대시가 제자리에서
    /// 끝나는 원인이었다. 반경을 skin만큼 줄여 접촉면을 떼고, 그만큼 늘어난 히트 거리는
    /// <paramref name="hitDistance"/>에서 되돌린다. (MoveRoot·PlayerGroundingSensor와 같은 패턴)
    /// </param>
    /// <param name="hitDistance">인셋 보정을 되돌린 히트 거리(원래 반경 기준). 호출부는 이 값을 쓴다.</param>
    private static bool TryCast(
        CapsuleCollider capsule,
        Transform owner,
        Vector3 originOffset,
        Vector3 dir,
        float maxDistance,
        float maxWalkableAngle,
        LayerMask obstacleMask,
        float skin,
        RaycastHit[] buffer,
        out RaycastHit best,
        out float hitDistance)
    {
        best = default;
        hitDistance = 0f;

        Vector3 lossy = owner.lossyScale;
        float radiusScale = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z));
        float heightScale = Mathf.Abs(lossy.y);
        float fullRadius = capsule.radius * radiusScale;

        // 캡슐 끝점(p1/p2)은 원래 지오메트리 그대로 두고 반경만 줄인다 → 스윕 볼륨이 전 방향으로
        // skin만큼 축소돼 접촉면에서 떨어진다(캡슐이 길어지지 않는다).
        float radius = Mathf.Max(0.01f, fullRadius - skin);
        float inset = Mathf.Max(0f, fullRadius - radius);

        float height = Mathf.Max(capsule.height * heightScale, fullRadius * 2f);
        float half = Mathf.Max(0f, height * 0.5f - fullRadius);
        Vector3 center = owner.TransformPoint(capsule.center) + originOffset;
        Vector3 up = owner.up;
        Vector3 p1 = center + up * half;
        Vector3 p2 = center - up * half;

        // 반경을 줄인 만큼 같은 벽을 inset 늦게 만난다 — 검사 거리도 그만큼 늘려야 사거리가 보존된다.
        int count = Physics.CapsuleCastNonAlloc(
            p1, p2, radius, dir, buffer, maxDistance + inset, obstacleMask, QueryTriggerInteraction.Ignore);

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

        // 인셋 보정을 되돌려 원래 반경 기준 거리로 환산한다 → 호출부의 정지 지점(hit - skin)이 종전과 같다.
        hitDistance = found ? Mathf.Max(0f, best.distance - inset) : 0f;
        return found;
    }
}
