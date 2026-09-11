using UnityEngine;

/// <summary>
/// FixedUpdate에서 플레이어의 속도/변위 의도를 합산하고 실제 위치를 한 번 적용한다.
/// 속도는 m/s, 변위는 m 단위이며 제출된 값은 매 물리 틱 소비 후 초기화된다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public sealed class PlayerMotor : MonoBehaviour
{
    private const int CastBufferSize = 8;
    private const float DefaultMaxWalkableSlopeAngle = 60f;
    private const float MovementComparisonEpsilon = 0.00001f;

    [Header("충돌 (최종 이동 스윕)")]
    [SerializeField] private PlayerGameRuleData gameRule;
    [SerializeField, Min(0f)] private float collisionSkin = 0.02f;
    [SerializeField, Min(1)] private int maxSweepIterations = 3;

    private readonly RaycastHit[] castBuffer = new RaycastHit[CastBufferSize];

    private Rigidbody playerRigidbody;
    private CapsuleCollider capsule;
    private PlayerGroundingSensor grounding;
    private PlayerMovement movement;
    private LayerMask obstacleMask;

    private Vector3 pendingVelocity;
    private Vector3 pendingDisplacement;
    private float lastVerticalIntentY;

    /// <summary>직전 Motor 틱에서 최종 이동 스윕이 요청 이동을 제한했는지 여부.</summary>
    public bool WasBlockedThisTick { get; private set; }

    /// <summary>이번 물리 틱에 적용할 월드 속도(m/s)를 더한다.</summary>
    public void AddVelocity(Vector3 worldVelocity)
    {
        pendingVelocity += worldVelocity;
    }

    /// <summary>이번 물리 틱에 적용할 월드 변위(m)를 더한다.</summary>
    public void AddDisplacement(Vector3 worldDelta)
    {
        pendingDisplacement += worldDelta;
    }

    private void Awake()
    {
        playerRigidbody = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        grounding = GetComponent<PlayerGroundingSensor>();
        movement = GetComponent<PlayerMovement>();

        // 현재 걷기/루트 이동이 쓰는 계약 유지: 유닛은 통과하고 정적 지오메트리만 막는다.
        obstacleMask = LayerMask.GetMask("Default", "Ground", "Wall", "Env");
    }

    private void FixedUpdate()
    {
        Tick(Time.fixedDeltaTime);
    }

    private void Tick(float deltaTime)
    {
        Vector3 desiredDelta = pendingVelocity * deltaTime + pendingDisplacement;
        pendingVelocity = Vector3.zero;
        pendingDisplacement = Vector3.zero;

        desiredDelta = ProjectOntoGround(desiredDelta);
        Vector3 resolvedDelta = PlayerMotionSweep.Resolve(
            capsule,
            desiredDelta,
            gameRule != null ? gameRule.MaxWalkableSlopeAngle : DefaultMaxWalkableSlopeAngle,
            obstacleMask,
            collisionSkin,
            maxSweepIterations,
            castBuffer);

        WasBlockedThisTick =
            (desiredDelta - resolvedDelta).sqrMagnitude >
            MovementComparisonEpsilon * MovementComparisonEpsilon;
        lastVerticalIntentY = resolvedDelta.y;

        if (resolvedDelta.sqrMagnitude > 0f)
            playerRigidbody.MovePosition(playerRigidbody.position + resolvedDelta);

        // Y 잠금은 이번 틱의 최종 수직 의도와 MovePosition 제출이 확정된 뒤 판정해야 한다.
        if (movement != null)
            movement.ApplyPostMotorGroundLock(lastVerticalIntentY);
    }

    private Vector3 ProjectOntoGround(Vector3 move)
    {
        if (grounding == null || !grounding.IsGrounded)
            return move;

        float distance = move.magnitude;
        if (distance <= Mathf.Epsilon)
            return move;

        Vector3 normal = grounding.GroundNormal;
        if (normal.y >= 0.999f)
            return move;

        Vector3 projected = Vector3.ProjectOnPlane(move, normal);
        if (projected.sqrMagnitude <= 1e-6f)
            return move;

        return projected.normalized * distance;
    }

    private void OnDisable()
    {
        pendingVelocity = Vector3.zero;
        pendingDisplacement = Vector3.zero;
        WasBlockedThisTick = false;
    }

    private void OnValidate()
    {
        collisionSkin = Mathf.Max(0f, collisionSkin);
        maxSweepIterations = Mathf.Max(1, maxSweepIterations);
    }
}
