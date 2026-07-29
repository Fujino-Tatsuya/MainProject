using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(Rigidbody))]
public sealed class PlayerGroundingSensor : NetworkBehaviour
{
    public enum GroundingMode
    {
        Alive,
        Soul
    }

    public enum VerticalMotionState
    {
        Stable,
        Rising,
        Falling
    }

    private const float ProbeRadiusScale = 0.95f;
    private const float DefaultMinimumGroundUpDot = 0.5f; // 규칙 미할당 시 폴백(60도)
    private const float VerticalVelocityEpsilon = 0.01f;
    private const int MaxProbeHits = 8;

    [Header("Ground Probe")]
    [SerializeField, Min(0f)] private float probeDistance = 0.1f;
    [SerializeField] private LayerMask aliveGroundMask = ~0;
    [SerializeField] private LayerMask soulGroundMask = ~0;
    [Tooltip("걸을 수 있는 최대 경사각 등 공용 규칙. 미할당 시 기본 60도(dot 0.5)로 폴백한다.")]
    [SerializeField] private PlayerGameRuleData gameRule;

    private readonly RaycastHit[] probeHits = new RaycastHit[MaxProbeHits];

    private CapsuleCollider capsuleCollider;
    private Rigidbody playerRigidbody;
    private GroundingMode groundingMode;
    private float previousWorldY;
    private bool hasPreviousWorldY;

    public bool IsGrounded { get; private set; }
    public Vector3 GroundNormal { get; private set; } = Vector3.up;
    public Collider GroundCollider { get; private set; }
    public bool IsMovingPlatform { get; private set; }
    public float VerticalVelocity { get; private set; }
    public VerticalMotionState VerticalState { get; private set; }
    public bool IsRising => VerticalState == VerticalMotionState.Rising;
    public bool IsFalling => VerticalState == VerticalMotionState.Falling;
    public GroundingMode Mode => groundingMode;

    private void Awake()
    {
        capsuleCollider = GetComponent<CapsuleCollider>();
        playerRigidbody = GetComponent<Rigidbody>();
        previousWorldY = transform.position.y;
        hasPreviousWorldY = true;
    }

    private void OnEnable()
    {
        previousWorldY = transform.position.y;
        hasPreviousWorldY = true;
    }

    private void FixedUpdate()
    {
        if (!ShouldRunPhysicsProbe())
        {
            ClearSample();
            return;
        }

        SampleVerticalMotion();
        SampleGround();
    }

    public void SetGroundingMode(GroundingMode mode)
    {
        groundingMode = mode;
    }

    public void RefreshNow()
    {
        if (!ShouldRunPhysicsProbe())
        {
            ClearSample();
            return;
        }

        SampleVerticalMotion();
        SampleGround();
    }

    private bool ShouldRunPhysicsProbe()
    {
        return !IsSpawned || IsOwner || IsServer;
    }

    private void SampleVerticalMotion()
    {
        float currentWorldY = transform.position.y;

        if (playerRigidbody != null && !playerRigidbody.isKinematic)
        {
            VerticalVelocity = playerRigidbody.linearVelocity.y;
        }
        else if (hasPreviousWorldY && Time.fixedDeltaTime > 0f)
        {
            VerticalVelocity = (currentWorldY - previousWorldY) / Time.fixedDeltaTime;
        }
        else
        {
            VerticalVelocity = 0f;
        }

        previousWorldY = currentWorldY;
        hasPreviousWorldY = true;

        if (VerticalVelocity > VerticalVelocityEpsilon)
            VerticalState = VerticalMotionState.Rising;
        else if (VerticalVelocity < -VerticalVelocityEpsilon)
            VerticalState = VerticalMotionState.Falling;
        else
            VerticalState = VerticalMotionState.Stable;
    }

    private void SampleGround()
    {
        IsGrounded = false;
        GroundNormal = Vector3.up;
        GroundCollider = null;
        IsMovingPlatform = false;

        if (capsuleCollider == null || !capsuleCollider.enabled)
            return;

        GetWorldCapsuleBottom(out Vector3 bottomSphereCenter, out float worldRadius);

        float probeRadius = worldRadius * ProbeRadiusScale;
        float radiusInset = worldRadius - probeRadius;
        float castDistance = probeDistance + radiusInset;
        LayerMask groundMask = groundingMode == GroundingMode.Soul
            ? soulGroundMask
            : aliveGroundMask;

        // 걸을 수 있는 경사 한계는 공용 규칙에서 온다(대시 등판각과 단일 소스). 미할당 시 60도 폴백.
        float minGroundUpDot = gameRule != null ? gameRule.WalkableGroundUpDot : DefaultMinimumGroundUpDot;

        int hitCount = Physics.SphereCastNonAlloc(
            bottomSphereCenter,
            probeRadius,
            Vector3.down,
            probeHits,
            castDistance,
            groundMask,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = probeHits[i];
            if (hit.collider == null || IsOwnCollider(hit.collider))
                continue;

            if (Vector3.Dot(hit.normal, Vector3.up) < minGroundUpDot)
                continue;

            if (hit.distance >= nearestDistance)
                continue;

            nearestDistance = hit.distance;
            IsGrounded = true;
            GroundNormal = hit.normal.normalized;
            GroundCollider = hit.collider;
        }

        IsMovingPlatform =
            GroundCollider != null &&
            GroundCollider.GetComponentInParent<MovingPlatform>() != null;
    }

    private void GetWorldCapsuleBottom(out Vector3 bottomSphereCenter, out float worldRadius)
    {
        Vector3 localAxis = GetLocalCapsuleAxis(capsuleCollider.direction);
        Vector3 worldAxisVector = transform.TransformVector(localAxis);
        Vector3 worldAxis = worldAxisVector.sqrMagnitude > 0f
            ? worldAxisVector.normalized
            : Vector3.up;

        float axisScale = worldAxisVector.magnitude;
        GetPerpendicularAxes(capsuleCollider.direction, out Vector3 localPerpendicularA, out Vector3 localPerpendicularB);
        float perpendicularScaleA = transform.TransformVector(localPerpendicularA).magnitude;
        float perpendicularScaleB = transform.TransformVector(localPerpendicularB).magnitude;
        float radiusScale = Mathf.Max(perpendicularScaleA, perpendicularScaleB);

        worldRadius = capsuleCollider.radius * radiusScale;
        float worldHeight = Mathf.Max(capsuleCollider.height * axisScale, worldRadius * 2f);
        float halfCylinderLength = Mathf.Max(0f, worldHeight * 0.5f - worldRadius);
        Vector3 worldCenter = transform.TransformPoint(capsuleCollider.center);

        Vector3 endpointA = worldCenter + worldAxis * halfCylinderLength;
        Vector3 endpointB = worldCenter - worldAxis * halfCylinderLength;
        bottomSphereCenter = endpointA.y <= endpointB.y ? endpointA : endpointB;
    }

    private bool IsOwnCollider(Collider candidate)
    {
        return candidate.transform == transform || candidate.transform.IsChildOf(transform);
    }

    private void ClearSample()
    {
        IsGrounded = false;
        GroundNormal = Vector3.up;
        GroundCollider = null;
        IsMovingPlatform = false;
        VerticalVelocity = 0f;
        VerticalState = VerticalMotionState.Stable;
        hasPreviousWorldY = false;
    }

    private static Vector3 GetLocalCapsuleAxis(int direction)
    {
        switch (direction)
        {
            case 0:
                return Vector3.right;
            case 2:
                return Vector3.forward;
            default:
                return Vector3.up;
        }
    }

    private static void GetPerpendicularAxes(
        int direction,
        out Vector3 perpendicularA,
        out Vector3 perpendicularB)
    {
        switch (direction)
        {
            case 0:
                perpendicularA = Vector3.up;
                perpendicularB = Vector3.forward;
                break;
            case 2:
                perpendicularA = Vector3.right;
                perpendicularB = Vector3.up;
                break;
            default:
                perpendicularA = Vector3.right;
                perpendicularB = Vector3.forward;
                break;
        }
    }

    private void OnValidate()
    {
        probeDistance = Mathf.Max(0f, probeDistance);
    }
}
