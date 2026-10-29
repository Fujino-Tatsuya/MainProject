using UnityEngine;

/// <summary>
/// 판정 앵커(<see cref="ColliderInfo"/>) 형상 하나의 월드 값 — 서버 판정 오버랩(<see cref="PlayerSkillBase"/> <c>OverlapHitboxAnchor</c>)과
/// 간파 시도 예측(<see cref="PlayerInterruptSkillBase"/>)이 같은 형상 계산·같은 오버랩 질의를 쓴다(할 일 139-3).
/// 예측은 지금 형상을 공격 방향으로 돌리고(<see cref="Aimed"/>) 판정 위치로 옮겨(<see cref="Offset"/>) 판정 때 놓일 자리에서 질의한다.
/// </summary>
public struct HitboxAnchorShape
{
    public OverlapCollider Kind;
    public BoxColliderInfo Box;
    public SphereColliderInfo Sphere;
    public CapsuleColliderInfo Capsule;

    public static HitboxAnchorShape FromBox(Vector3 center, Vector3 halfExtents, Quaternion orientation) =>
        new HitboxAnchorShape
        {
            Kind = OverlapCollider.Box,
            Box = new BoxColliderInfo { center = center, halfExtents = halfExtents, orientation = orientation },
        };

    public static HitboxAnchorShape FromSphere(Vector3 center, float radius) =>
        new HitboxAnchorShape
        {
            Kind = OverlapCollider.Sphere,
            Sphere = new SphereColliderInfo { center = center, radius = radius },
        };

    public static HitboxAnchorShape FromCapsule(Vector3 point0, Vector3 point1, float radius) =>
        new HitboxAnchorShape
        {
            Kind = OverlapCollider.Capsule,
            Capsule = new CapsuleColliderInfo { point0 = point0, point1 = point1, radius = radius },
        };

    /// <summary>앵커의 지금 월드 형상. 앵커가 없거나 형상이 하나(Box·Sphere·Capsule)가 아니면 false.</summary>
    public static bool TryRead(ColliderInfo anchor, out HitboxAnchorShape shape)
    {
        shape = default;
        if (anchor == null)
            return false;

        switch (anchor.OverlapCollider)
        {
            case OverlapCollider.Box:
                shape.Kind = OverlapCollider.Box;
                anchor.GetBoxColliderInfo(ref shape.Box);
                return true;
            case OverlapCollider.Sphere:
                shape.Kind = OverlapCollider.Sphere;
                anchor.GetSphereColliderInfo(ref shape.Sphere);
                return true;
            case OverlapCollider.Capsule:
                shape.Kind = OverlapCollider.Capsule;
                anchor.GetCapsuleColliderInfo(ref shape.Capsule);
                return true;
            default:
                return false;
        }
    }

    /// <summary>월드 <paramref name="offset"/> 만큼 옮긴 형상.</summary>
    public HitboxAnchorShape Offset(Vector3 offset)
    {
        HitboxAnchorShape s = this;
        s.Box.center += offset;
        s.Sphere.center += offset;
        s.Capsule.point0 += offset;
        s.Capsule.point1 += offset;
        return s;
    }

    /// <summary>
    /// <paramref name="pivot"/> 수직축으로 돌려 수평 정면 <paramref name="currentForward"/> 가 <paramref name="attackDirection"/> 을 향하게 한 형상.
    /// 둘 중 하나라도 수평 성분이 없으면 그대로다. 높이는 바뀌지 않는다.
    /// </summary>
    public HitboxAnchorShape Aimed(Vector3 pivot, Vector3 currentForward, Vector3 attackDirection)
    {
        currentForward.y = 0f;
        attackDirection.y = 0f;
        if (currentForward.sqrMagnitude < 0.0001f || attackDirection.sqrMagnitude < 0.0001f)
            return this;

        Quaternion yaw = Quaternion.AngleAxis(Vector3.SignedAngle(currentForward, attackDirection, Vector3.up), Vector3.up);
        HitboxAnchorShape s = this;
        s.Box.center = pivot + yaw * (Box.center - pivot);
        s.Box.orientation = yaw * Box.orientation;
        s.Sphere.center = pivot + yaw * (Sphere.center - pivot);
        s.Capsule.point0 = pivot + yaw * (Capsule.point0 - pivot);
        s.Capsule.point1 = pivot + yaw * (Capsule.point1 - pivot);
        return s;
    }

    /// <summary>이 형상으로 판정 레이어 오버랩(트리거 포함). results 에 채운 개수, 형상이 없으면 0.</summary>
    public int Overlap(Collider[] results, LayerMask layers)
    {
        switch (Kind)
        {
            case OverlapCollider.Box:
                return Physics.OverlapBoxNonAlloc(Box.center, Box.halfExtents, results, Box.orientation,
                    layers, QueryTriggerInteraction.Collide);
            case OverlapCollider.Sphere:
                return Physics.OverlapSphereNonAlloc(Sphere.center, Sphere.radius, results,
                    layers, QueryTriggerInteraction.Collide);
            case OverlapCollider.Capsule:
                return Physics.OverlapCapsuleNonAlloc(Capsule.point0, Capsule.point1, Capsule.radius, results,
                    layers, QueryTriggerInteraction.Collide);
            default:
                return 0;
        }
    }
}
