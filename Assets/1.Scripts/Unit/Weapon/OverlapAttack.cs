using UnityEngine;

public class OverlapAttack : BaseAttack
{
    [SerializeField] private ColliderInfo colliderInfo;
    [SerializeField] private int maxHitCount = 16;

    private Collider[] results;

    private void Awake()
    {
        InitializeAttackInfo();
        results = new Collider[Mathf.Max(1, maxHitCount)];
    }

    public void Hit()
    {
        if (!IsServer)
            return;

        if (colliderInfo == null)
        {
            Debug.LogError("[Unit] OverlapAttack requires ColliderInfo.", this);
            return;
        }

        int hitCount = Overlap();
        for (int i = 0; i < hitCount; i++)
            TryResolveHit(results[i]);
    }

    private int Overlap()
    {
        switch (colliderInfo.OverlapCollider)
        {
            case OverlapCollider.Box:
            {
                BoxColliderInfo info = default;
                colliderInfo.GetBoxColliderInfo(ref info);
                return Physics.OverlapBoxNonAlloc(
                    info.center,
                    info.halfExtents,
                    results,
                    info.orientation,
                    targetLayer,
                    QueryTriggerInteraction.Collide);
            }

            case OverlapCollider.Sphere:
            {
                SphereColliderInfo info = default;
                colliderInfo.GetSphereColliderInfo(ref info);
                return Physics.OverlapSphereNonAlloc(
                    info.center,
                    info.radius,
                    results,
                    targetLayer,
                    QueryTriggerInteraction.Collide);
            }

            case OverlapCollider.Capsule:
            {
                CapsuleColliderInfo info = default;
                colliderInfo.GetCapsuleColliderInfo(ref info);
                return Physics.OverlapCapsuleNonAlloc(
                    info.point0,
                    info.point1,
                    info.radius,
                    results,
                    targetLayer,
                    QueryTriggerInteraction.Collide);
            }

            default:
                return 0;
        }
    }
}
