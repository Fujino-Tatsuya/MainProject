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

    /// <summary>접지 변위 채널이 허용하는 수직 성분 한계(m). 이보다 크면 제출자가 계약을 깬 것이다.</summary>
    private const float NonPlanarDisplacementTolerance = 0.0001f;

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
    private Vector3 pendingGroundedDisplacement;
    private Vector3 pendingDisplacement;
    private Vector3 pendingPosePosition;
    private Quaternion pendingPoseRotation;
    private bool hasPendingPose;
    private float lastVerticalIntentY;
    private bool warnedNonPlanarGroundedDisplacement;

    /// <summary>직전 Motor 틱에서 최종 이동 스윕이 요청 이동을 제한했는지 여부.</summary>
    public bool WasBlockedThisTick { get; private set; }

    /// <summary>Motor가 한 물리 틱의 요청 이동을 스윕한 직후 알린다.</summary>
    public event System.Action<Vector3, Vector3, bool> MovementResolved;

    /// <summary>Rigidbody 기준 현재 위치. 지연 적용되는 MovePosition과 같은 좌표계를 쓴다.</summary>
    public Vector3 Position => playerRigidbody != null ? playerRigidbody.position : transform.position;

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

    /// <summary>
    /// 이번 물리 틱에 적용할 자발 이동 변위(m)를 더한다.
    /// 걷기 속도 채널과 같이 접지면에 투영되지만, 플랫폼 캐리 같은 외부 변위와는 분리된다.
    ///
    /// 🔴 <b>반드시 수평(y≈0) 벡터만 넣을 것.</b> <see cref="ProjectOntoGround"/>는 크기를 보존한 채
    /// 방향만 바꾸므로, 수직 성분이 섞이면 기울어진 지면에서 아래 방향이 **위쪽으로 뒤집히고**
    /// 그 크기만큼 재정규화되어 캐릭터가 떠오른다. 중력·낙하 같은 수직 성분은
    /// <see cref="AddDisplacement"/>(투영 없음)로 따로 제출한다.
    /// </summary>
    public void AddGroundedDisplacement(Vector3 worldDelta)
    {
        // 위 주석의 전제(수평 전용)를 깨면 캐릭터가 조용히 떠오른다 — 증상만 보고는 원인을 찾기
        // 어려우므로 제출 시점에 잡는다. 인스턴스당 한 번만 남겨 스팸을 막는다.
        if (!warnedNonPlanarGroundedDisplacement &&
            Mathf.Abs(worldDelta.y) > NonPlanarDisplacementTolerance)
        {
            warnedNonPlanarGroundedDisplacement = true;
            Edit.LogWarning(
                $"[Motor] AddGroundedDisplacement에 수직 성분이 섞였습니다(y={worldDelta.y:F4}). " +
                "접지면 투영은 크기를 보존한 채 방향만 바꾸므로 경사에서 아래 방향이 위로 뒤집힙니다. " +
                "수직 성분은 AddDisplacement로 따로 제출하세요.", this);
        }

        pendingGroundedDisplacement += worldDelta;
    }

    /// <summary>
    /// 구속 추종처럼 절대 월드 포즈가 필요한 이동을 제출한다. 같은 물리 틱에는 마지막 제출이 이기며,
    /// 일반 이동 의도보다 우선한다. 실제 Rigidbody 위치/회전 쓰기는 여전히 Motor만 수행한다.
    /// </summary>
    public void SetPoseTarget(Vector3 worldPosition, Quaternion worldRotation)
    {
        pendingPosePosition = worldPosition;
        pendingPoseRotation = worldRotation;
        hasPendingPose = true;
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
        if (hasPendingPose)
        {
            Vector3 requestedPoseDelta = pendingPosePosition - playerRigidbody.position;

            pendingVelocity = Vector3.zero;
            pendingGroundedDisplacement = Vector3.zero;
            pendingDisplacement = Vector3.zero;
            hasPendingPose = false;
            WasBlockedThisTick = false;
            lastVerticalIntentY = requestedPoseDelta.y;

            ApplyRigidbodyPose(pendingPosePosition, pendingPoseRotation, true);
            MovementResolved?.Invoke(requestedPoseDelta, requestedPoseDelta, false);

            if (movement != null)
                movement.ApplyPostMotorGroundLock(lastVerticalIntentY);

            return;
        }

        // 🔴 경사 투영은 "스스로 걷는 이동"(속도 채널)에만 적용한다. 플랫폼 캐리 같은 외부 변위는
        // 월드가 정한 이동이라 지면 평면으로 회전시키면 안 된다 — ProjectOntoGround는 크기를 보존한
        // 채 방향만 바꾸므로, 경사면 위에서 수직으로 움직이는 플랫폼의 변위가 수평 이동으로 뒤바뀐다.
        // 변경 전 PlayerMovement.Move()도 `ProjectOntoGround(inputMove) + _carryDelta` 였다.
        // 대시는 grounded displacement 채널로 들어와 같은 투영을 공유한다.
        Vector3 desiredDelta =
            ProjectOntoGround(pendingVelocity * deltaTime + pendingGroundedDisplacement) +
            pendingDisplacement;
        pendingVelocity = Vector3.zero;
        pendingGroundedDisplacement = Vector3.zero;
        pendingDisplacement = Vector3.zero;

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
        MovementResolved?.Invoke(desiredDelta, resolvedDelta, WasBlockedThisTick);

        if (resolvedDelta.sqrMagnitude > 0f)
            ApplyRigidbodyPose(playerRigidbody.position + resolvedDelta, default, false);

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

    private void ApplyRigidbodyPose(Vector3 position, Quaternion rotation, bool applyRotation)
    {
        playerRigidbody.MovePosition(position);
        if (applyRotation)
            playerRigidbody.MoveRotation(rotation);
    }

    private void OnDisable()
    {
        pendingVelocity = Vector3.zero;
        pendingGroundedDisplacement = Vector3.zero;
        pendingDisplacement = Vector3.zero;
        hasPendingPose = false;
        WasBlockedThisTick = false;
    }

    private void OnValidate()
    {
        collisionSkin = Mathf.Max(0f, collisionSkin);
        maxSweepIterations = Mathf.Max(1, maxSweepIterations);
    }
}
