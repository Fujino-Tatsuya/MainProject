using UnityEngine;

/// <summary>
/// 인터럽트 판정 형상의 방향 무관(전방위) 사거리 — 순수 계산. 인터럽트 강조(할 일 5) HUD 가 쓴다.
/// <list type="bullet">
/// <item>사거리 = 판정 앵커 형상을 플레이어 중심(<c>pivot</c>) 수직축으로 어느 방향으로 돌려도 닿는 최대 수평(XZ) 거리.
///       수직축 회전은 XZ 거리를 바꾸지 않으므로, 지금 방향의 월드 형상에서 pivot 으로부터 가장 먼 점까지의 XZ 거리와 같다.
///       전방 오프셋(형상 중심까지의 거리)과 크기(반폭·반경)가 함께 들어간다.</item>
/// <item>대상까지 거리 = pivot 에서 대상 콜라이더 실제 표면까지 XZ 거리(<see cref="HorizontalDistanceToCollider"/>, 안이면 0, 할 일 139-4).
///       판정은 형상이 대상 콜라이더에 닿기만 하면 맞으므로 대상 중심이 아니라 표면 기준이다.</item>
/// <item>탑다운 바닥 평면 게임이라 수평(XZ)만 본다. 높이는 호출 쪽 후보 질의가 판정 형상의 높이 띠로 거른다.</item>
/// </list>
/// </summary>
public static class InterruptOmniRangeModel
{
    /// <summary>박스(월드 중심·반크기·회전)의 전방위 사거리 = 8 꼭짓점 중 pivot 에서 가장 먼 XZ 거리.</summary>
    public static float BoxReach(Vector3 pivot, Vector3 center, Vector3 halfExtents, Quaternion orientation)
    {
        float reach = 0f;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                (i & 1) == 0 ? -halfExtents.x : halfExtents.x,
                (i & 2) == 0 ? -halfExtents.y : halfExtents.y,
                (i & 4) == 0 ? -halfExtents.z : halfExtents.z);
            reach = Mathf.Max(reach, HorizontalDistance(pivot, center + orientation * corner));
        }

        return reach;
    }

    /// <summary>구의 전방위 사거리 = 중심까지 XZ 거리 + 반경.</summary>
    public static float SphereReach(Vector3 pivot, Vector3 center, float radius)
    {
        return HorizontalDistance(pivot, center) + Mathf.Max(0f, radius);
    }

    /// <summary>캡슐의 전방위 사거리 = 두 끝점 중 더 먼 XZ 거리 + 반경.</summary>
    public static float CapsuleReach(Vector3 pivot, Vector3 point0, Vector3 point1, float radius)
    {
        return Mathf.Max(HorizontalDistance(pivot, point0), HorizontalDistance(pivot, point1)) + Mathf.Max(0f, radius);
    }

    /// <summary>
    /// pivot 에서 대상 콜라이더 실제 표면까지 XZ 거리(할 일 139-4). pivot 이 콜라이더 안이면 0.
    /// <list type="bullet">
    /// <item>높이 = 질의점을 콜라이더 AABB 중심 높이로 옮겨 묻는다. 바닥 높이 그대로 물으면 세운 캡슐은 아래 반구로 가서 수평 거리가 커진다.
    ///       세운 캡슐·Y축만 돈 상자·구는 중심 높이 단면이 가장 넓은 단면이라 수평 표면 거리 그대로다.</item>
    /// <item>트리거도 그대로 잰다(Hurtbox 가 트리거다). ClosestPoint 를 못 쓰는 콜라이더(비볼록 MeshCollider 등)는
    ///       AABB 표면으로 근사한다(<c>UnitWallImpacts</c> 와 같은 규칙) — 실제보다 가깝게 나와 강조가 넓은 쪽으로 틀린다.</item>
    /// </list>
    /// 이전 AABB XZ 사각형 거리는 대각선에서 최대 r(√2−1) 짧았다(23호 캡슐 r 1.7 → 0.70m).
    /// </summary>
    public static float HorizontalDistanceToCollider(Vector3 pivot, Collider target)
    {
        Vector3 probe = new Vector3(pivot.x, target.bounds.center.y, pivot.z);
        Vector3 closest = SupportsClosestPoint(target) ? target.ClosestPoint(probe) : target.ClosestPointOnBounds(probe);
        return HorizontalDistance(probe, closest);
    }

    /// <summary>대상 콜라이더가 방향과 상관없이 사거리 안인가 — 실제 표면까지 XZ 거리 ≤ 사거리.</summary>
    public static bool IsInReach(Vector3 pivot, Collider target, float reach)
    {
        return reach > 0f && target != null && HorizontalDistanceToCollider(pivot, target) <= reach;
    }

    private static bool SupportsClosestPoint(Collider c) =>
        c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (c is MeshCollider mesh && mesh.convex);

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = b.x - a.x;
        float dz = b.z - a.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
