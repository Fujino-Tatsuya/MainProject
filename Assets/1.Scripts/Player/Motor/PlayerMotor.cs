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
    public enum MotorMode
    {
        Kinematic,
        Dynamic
    }

    private const int CastBufferSize = 8;
    private const float DefaultMaxWalkableSlopeAngle = 60f;
    private const float DefaultStepOffset = 0.3f;
    private const float DefaultMaxFallSpeed = 30f;
    private const float MovementComparisonEpsilon = 0.00001f;
    private const float UpwardIntentEpsilon = 0.00005f;

    /// <summary>접지 변위 채널이 허용하는 수직 성분 한계(m). 이보다 크면 제출자가 계약을 깬 것이다.</summary>
    private const float NonPlanarDisplacementTolerance = 0.0001f;

    /// <summary>요청한 수평 이동의 이 비율 미만만 적용됐으면 "막혔다"로 본다(벽 판정용).</summary>
    private const float BlockedRatioThreshold = 0.1f;

    [Header("충돌 (최종 이동 스윕)")]
    [SerializeField] private PlayerGameRuleData gameRule;
    [SerializeField, Min(0f)] private float collisionSkin = 0.02f;
    [SerializeField, Min(1)] private int maxSweepIterations = 3;

    private readonly RaycastHit[] castBuffer = new RaycastHit[CastBufferSize];

    private Rigidbody playerRigidbody;
    private CapsuleCollider capsule;
    private PlayerGroundingSensor grounding;

    private Vector3 pendingVelocity;
    private Vector3 pendingGroundedDisplacement;
    private Vector3 pendingDisplacement;
    private Vector3 pendingPosePosition;
    private Quaternion pendingPoseRotation;
    private bool hasPendingPose;
    private bool warnedNonPlanarGroundedDisplacement;
    private bool gravityEnabled = true;
    private float verticalVelocity;
    private MotorMode mode;

    /// <summary>직전 Motor 틱에서 최종 이동 스윕이 요청 이동을 제한했는지 여부.</summary>
    public bool WasBlockedThisTick { get; private set; }

    /// <summary>Motor가 한 물리 틱의 요청 이동을 스윕한 직후 알린다.</summary>
    public event System.Action<Vector3, Vector3, bool> MovementResolved;

    /// <summary>Rigidbody 기준 현재 위치. 지연 적용되는 MovePosition과 같은 좌표계를 쓴다.</summary>
    public Vector3 Position => playerRigidbody != null ? playerRigidbody.position : transform.position;
    public float VerticalVelocity => verticalVelocity;
    public bool GravityEnabled => gravityEnabled;
    public MotorMode Mode => mode;
    public PlayerGameRuleData GameRule => gameRule;

    /// <summary>이번 물리 틱에 적용할 월드 속도(m/s)를 더한다.</summary>
    public void AddVelocity(Vector3 worldVelocity)
    {
        if (!isActiveAndEnabled)
            return;

        pendingVelocity += worldVelocity;
    }

    /// <summary>이번 물리 틱에 적용할 월드 변위(m)를 더한다.</summary>
    public void AddDisplacement(Vector3 worldDelta)
    {
        if (!isActiveAndEnabled)
            return;

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
        if (!isActiveAndEnabled)
            return;

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
        if (!isActiveAndEnabled)
            return;

        pendingPosePosition = worldPosition;
        pendingPoseRotation = worldRotation;
        hasPendingPose = true;
    }

    /// <summary>Soul 부유처럼 Motor의 수동 중력 채널만 켜고 끈다.</summary>
    public void SetGravityEnabled(bool enabled)
    {
        gravityEnabled = enabled;
        if (!enabled)
            verticalVelocity = 0f;

        if (mode == MotorMode.Dynamic && playerRigidbody != null)
            playerRigidbody.useGravity = enabled;
    }

    /// <summary>
    /// 향후 진짜 PhysX가 필요한 상태를 위한 도피구. 현재 gameplay 사용처는 없다.
    /// Kinematic→Dynamic은 Motor 수직 속도를 Rigidbody로 넘기고, 반대 전환은 되받는다.
    /// </summary>
    public void SetMode(MotorMode nextMode)
    {
        if (playerRigidbody == null || mode == nextMode)
            return;

        if (nextMode == MotorMode.Dynamic)
        {
            playerRigidbody.isKinematic = false;
            playerRigidbody.useGravity = gravityEnabled;
            playerRigidbody.linearVelocity = Vector3.up * verticalVelocity;
        }
        else
        {
            verticalVelocity = playerRigidbody.linearVelocity.y;
            playerRigidbody.useGravity = false;
            playerRigidbody.linearVelocity = Vector3.zero;
            playerRigidbody.angularVelocity = Vector3.zero;
            playerRigidbody.isKinematic = true;
        }

        mode = nextMode;
        ClearPendingMotion();
    }

    private void Awake()
    {
        playerRigidbody = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        grounding = GetComponent<PlayerGroundingSensor>();
        mode = playerRigidbody != null && !playerRigidbody.isKinematic
            ? MotorMode.Dynamic
            : MotorMode.Kinematic;
    }

    private void FixedUpdate()
    {
        if (mode == MotorMode.Dynamic)
        {
            ClearPendingMotion();
            verticalVelocity = playerRigidbody != null ? playerRigidbody.linearVelocity.y : 0f;
            return;
        }

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
            verticalVelocity = 0f;

            ApplyRigidbodyPose(pendingPosePosition, pendingPoseRotation, true);
            MovementResolved?.Invoke(requestedPoseDelta, requestedPoseDelta, false);

            return;
        }

        // 🔴 경사 투영은 "스스로 걷는 이동"(속도 채널)에만 적용한다. 플랫폼 캐리 같은 외부 변위는
        // 월드가 정한 이동이라 지면 평면으로 회전시키면 안 된다 — ProjectOntoGround는 크기를 보존한
        // 채 방향만 바꾸므로, 경사면 위에서 수직으로 움직이는 플랫폼의 변위가 수평 이동으로 뒤바뀐다.
        // 변경 전 PlayerMovement.Move()도 `ProjectOntoGround(inputMove) + _carryDelta` 였다.
        // 대시는 grounded displacement 채널로 들어와 같은 투영을 공유한다.
        Vector3 selfPropelledDelta = pendingVelocity * deltaTime + pendingGroundedDisplacement;
        Vector3 externalDelta = pendingDisplacement;
        bool hasUpwardIntent = selfPropelledDelta.y + externalDelta.y > UpwardIntentEpsilon;

        Vector3 gravityDelta = Vector3.zero;
        Vector3 groundSnapDelta = Vector3.zero;
        bool grounded = grounding != null && grounding.IsGrounded;

        if (grounded)
        {
            verticalVelocity = 0f;
            if (!hasUpwardIntent)
            {
                // 센서가 복원한 캡슐 표면 간격: +gap은 아래로, -penetration은 위로 보정한다.
                groundSnapDelta = Vector3.down * grounding.GroundSurfaceDistance;
            }
        }
        else if (gravityEnabled)
        {
            verticalVelocity += Physics.gravity.y * deltaTime;
            float maxFallSpeed = gameRule != null ? gameRule.MaxFallSpeed : DefaultMaxFallSpeed;
            verticalVelocity = Mathf.Max(verticalVelocity, -maxFallSpeed);
            gravityDelta = Vector3.up * (verticalVelocity * deltaTime);
        }

        // 제출된 의도(= Motor 보정 제외). 막힘 판정의 기준이다 — 아래 WasBlockedThisTick 주석 참조.
        Vector3 intentDelta = ProjectOntoGround(selfPropelledDelta) + externalDelta;
        Vector3 desiredDelta = intentDelta + gravityDelta + groundSnapDelta;
        pendingVelocity = Vector3.zero;
        pendingGroundedDisplacement = Vector3.zero;
        pendingDisplacement = Vector3.zero;

        Vector3 resolvedDelta = PlayerMotionSweep.Resolve(
            capsule,
            desiredDelta,
            new Vector3(intentDelta.x, 0f, intentDelta.z),
            grounded,
            gameRule != null ? gameRule.StepOffset : DefaultStepOffset,
            gameRule != null ? gameRule.MaxWalkableSlopeAngle : DefaultMaxWalkableSlopeAngle,
            ResolveObstacleMask(),
            collisionSkin,
            maxSweepIterations,
            castBuffer);

        // 🔴 "막혔다" 판정은 **제출된 수평 의도**만 본다. desiredDelta에는 Motor 자신의 보정
        // (중력·접지 스냅)이 섞여 있는데, 접지 중 수평 이동에 작은 스냅이 더해지면 스윕이 1회차에
        // 지면을 막고 접선으로 흘린다(ProjectOnPlane). 수평분은 2회차에서 복구되지만 아래 방향
        // 스냅분은 사라지므로, 전체 벡터를 1e-5로 비교하면 **정상 보행 중에도 매 틱 true**가 된다.
        // 넉백이 이 값을 보고 자기 속도를 0으로 만들기 때문에(벽 정지 정책), 그대로 두면 접지
        // 상태에서 넉백이 첫 틱에 취소돼 "경직만 걸리고 안 밀리는" 증상이 된다.
        //
        // 그래서 수직 성분을 빼고, 절대 오차가 아니라 **상대 부족분**으로 본다(기존 대시 진단이
        // 쓰던 `applied < requested * 0.1` 과 같은 결).
        Vector3 intentPlanar = new Vector3(intentDelta.x, 0f, intentDelta.z);
        Vector3 appliedPlanar = new Vector3(resolvedDelta.x, 0f, resolvedDelta.z);
        float intentDistance = intentPlanar.magnitude;
        WasBlockedThisTick =
            intentDistance > MovementComparisonEpsilon &&
            appliedPlanar.magnitude < intentDistance * BlockedRatioThreshold;

        MovementResolved?.Invoke(desiredDelta, resolvedDelta, WasBlockedThisTick);

        if (resolvedDelta.sqrMagnitude > 0f)
            ApplyRigidbodyPose(playerRigidbody.position + resolvedDelta, default, false);
    }

    private LayerMask ResolveObstacleMask()
    {
        int mask = gameRule != null
            ? gameRule.ObstacleMask.value
            : LayerMask.GetMask("Default", "Ground", "Wall", "Env");
        int playerBit = LayerMask.GetMask("Player");
        int soulBit = LayerMask.GetMask("Soul");

        // Player 충돌은 bool 하나로만 결정하고 Soul은 항상 Player와 상호 통과한다.
        mask &= ~playerBit;
        mask &= ~soulBit;
        bool isSoul = grounding != null && grounding.Mode == PlayerGroundingSensor.GroundingMode.Soul;
        if (!isSoul && gameRule != null && gameRule.BlockOtherPlayers)
            mask |= playerBit;

        return mask;
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
        ClearPendingMotion();
        verticalVelocity = 0f;
    }

    private void ClearPendingMotion()
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
