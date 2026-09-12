using UnityEngine;

/// <summary>
/// 물리 틱의 입력/외부 이동 의도를 순수 시뮬레이션에 전달하고 최종 결과만 씬에 반영한다.
/// 속도는 m/s, 변위는 m 단위이며 제출된 값은 매 물리 틱 소비 후 초기화된다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public sealed class PlayerMotor : MonoBehaviour
{
    public enum MotorMode { Kinematic, Dynamic }

    private const int CastBufferSize = 8;
    private const float DefaultMaxWalkableSlopeAngle = 60f;
    private const float DefaultStepOffset = 0.3f;
    private const float DefaultMaxFallSpeed = 30f;
    private const float DefaultKnockbackDeceleration = 6f;
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
    private RuntimeMotionResolver motionResolver;

    private Vector3 pendingVelocity;
    private Vector3 pendingGroundedDisplacement;
    private Vector3 pendingDisplacement;
    private Vector3 pendingPosePosition;
    private Quaternion pendingPoseRotation;
    private bool hasPendingPose;
    private bool warnedNonPlanarGroundedDisplacement;
    private MotorMode mode;
    private PlayerSimulationState simulationState;

    public bool WasBlockedThisTick => simulationState.WasBlockedThisTick;
    public event System.Action<Vector3, Vector3, bool> MovementResolved;

    public Vector3 Position => playerRigidbody != null ? playerRigidbody.position : transform.position;
    public float VerticalVelocity => simulationState.VerticalVelocity;
    public Vector3 KnockbackVelocity => simulationState.KnockbackVelocity;
    public bool GravityEnabled => simulationState.GravityEnabled;
    public MotorMode Mode => mode;
    public PlayerGameRuleData GameRule => gameRule;
    public PlayerSimulationState SimulationState => simulationState;

    /// <summary>이번 물리 틱에 적용할 월드 속도(m/s)를 더한다.</summary>
    public void AddVelocity(Vector3 worldVelocity)
    {
        if (isActiveAndEnabled)
            pendingVelocity += worldVelocity;
    }

    /// <summary>플랫폼 캐리·루트모션 같은 월드 변위(m)를 더한다. 경사 투영하지 않는다.</summary>
    public void AddDisplacement(Vector3 worldDelta)
    {
        if (isActiveAndEnabled)
            pendingDisplacement += worldDelta;
    }

    /// <summary>
    /// 걷기·대시와 같이 접지면에 투영할 자발 이동 변위(m)를 더한다.
    /// 반드시 수평(y≈0) 벡터만 넣고 수직 성분은 <see cref="AddDisplacement"/>로 분리한다.
    /// </summary>
    public void AddGroundedDisplacement(Vector3 worldDelta)
    {
        if (!isActiveAndEnabled)
            return;

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

    /// <summary>구속 추종용 절대 포즈. 같은 틱에는 마지막 값이 이기고 다른 이동보다 우선한다.</summary>
    public void SetPoseTarget(Vector3 worldPosition, Quaternion worldRotation)
    {
        if (!isActiveAndEnabled)
            return;

        pendingPosePosition = worldPosition;
        pendingPoseRotation = worldRotation;
        hasPendingPose = true;
    }

    public void SetGravityEnabled(bool enabled)
    {
        simulationState.GravityEnabled = enabled;
        if (!enabled)
            simulationState.VerticalVelocity = 0f;

        if (mode == MotorMode.Dynamic && playerRigidbody != null)
            playerRigidbody.useGravity = enabled;
    }

    public void SetKnockbackVelocity(Vector3 velocity) => simulationState.KnockbackVelocity = velocity;
    public void ClearKnockbackVelocity() => simulationState.KnockbackVelocity = Vector3.zero;

    public void SetDashMotion(Vector3 direction, float speed, float remainingTime)
    {
        direction.y = 0f;
        simulationState.DashDirection = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector3.zero;
        simulationState.DashSpeed = Mathf.Max(0f, speed);
        simulationState.DashRemainingTime = Mathf.Max(0f, remainingTime);
    }

    public void ClearDashMotion()
    {
        simulationState.DashDirection = Vector3.zero;
        simulationState.DashSpeed = 0f;
        simulationState.DashRemainingTime = 0f;
    }

    public void SynchronizeArmatureRotation(Quaternion rotation, bool? hasRotate = null)
    {
        simulationState.ArmatureRotation = rotation;
        if (hasRotate.HasValue)
            simulationState.HasRotate = hasRotate.Value;
    }

    public void SetMode(MotorMode nextMode)
    {
        if (playerRigidbody == null || mode == nextMode)
            return;

        if (nextMode == MotorMode.Dynamic)
        {
            playerRigidbody.isKinematic = false;
            playerRigidbody.useGravity = simulationState.GravityEnabled;
            playerRigidbody.linearVelocity = Vector3.up * simulationState.VerticalVelocity;
        }
        else
        {
            simulationState.VerticalVelocity = playerRigidbody.linearVelocity.y;
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
        movement = GetComponent<PlayerMovement>();
        motionResolver = new RuntimeMotionResolver(this);
        mode = playerRigidbody != null && !playerRigidbody.isKinematic
            ? MotorMode.Dynamic
            : MotorMode.Kinematic;

        simulationState = new PlayerSimulationState
        {
            Position = playerRigidbody != null ? playerRigidbody.position : transform.position,
            RootRotation = playerRigidbody != null ? playerRigidbody.rotation : transform.rotation,
            ArmatureRotation = movement != null ? movement.ArmatureRotation : transform.rotation,
            PreviousRotateDirection = movement != null ? movement.PreviousRotateDirection : new Vector2(0f, -1f),
            HasRotate = movement == null || movement.HasRotate,
            CurrentSpeed = movement != null ? movement.CurrentSpeed : 0f,
            GroundNormal = Vector3.up,
            GravityEnabled = true
        };
    }

    private void FixedUpdate()
    {
        if (mode == MotorMode.Dynamic)
        {
            ClearPendingMotion();
            simulationState.Position = playerRigidbody != null ? playerRigidbody.position : transform.position;
            simulationState.RootRotation = playerRigidbody != null ? playerRigidbody.rotation : transform.rotation;
            simulationState.VerticalVelocity = playerRigidbody != null ? playerRigidbody.linearVelocity.y : 0f;
            return;
        }

        Tick(Time.fixedDeltaTime);
    }

    private void Tick(float deltaTime)
    {
        CaptureSceneState();

        PlayerSimulationInput input = movement != null
            ? movement.CaptureSimulationInput()
            : new PlayerSimulationInput { MoveSpeedMultiplier = 1f };
        input.AddedVelocity = pendingVelocity;
        input.GroundedDisplacement = pendingGroundedDisplacement;
        input.Displacement = pendingDisplacement;
        input.HasPoseTarget = hasPendingPose;
        input.PosePosition = pendingPosePosition;
        input.PoseRotation = pendingPoseRotation;

        PlayerSimulationSettings settings = movement != null
            ? movement.CaptureSimulationSettings()
            : default;
        settings.GravityY = Physics.gravity.y;
        settings.MaxFallSpeed = gameRule != null ? gameRule.MaxFallSpeed : DefaultMaxFallSpeed;
        settings.KnockbackDeceleration = gameRule != null
            ? gameRule.KnockbackDeceleration
            : DefaultKnockbackDeceleration;
        settings.StepOffset = gameRule != null ? gameRule.StepOffset : DefaultStepOffset;
        settings.MaxWalkableSlopeAngle = gameRule != null
            ? gameRule.MaxWalkableSlopeAngle
            : DefaultMaxWalkableSlopeAngle;
        settings.ObstacleMask = ResolveObstacleMask();
        settings.CollisionSkin = collisionSkin;
        settings.MaxSweepIterations = maxSweepIterations;

        PlayerSimulationResult result = PlayerMovementSimulation.Simulate(
            simulationState, input, settings, motionResolver, deltaTime);

        ClearPendingMotion();
        simulationState = result.State;
        movement?.CommitSimulationState(simulationState, !result.AppliesPoseRotation);

        if (result.AppliesPoseRotation)
        {
            ApplyRigidbodyPose(simulationState.Position, simulationState.RootRotation, true);
            MovementResolved?.Invoke(result.RequestedDelta, result.ResolvedDelta, result.WasBlocked);
        }
        else
        {
            // 이벤트와 Rigidbody 쓰기는 실제 틱의 최종 결과에만 일어난다.
            MovementResolved?.Invoke(result.RequestedDelta, result.ResolvedDelta, result.WasBlocked);
            if (result.ResolvedDelta.sqrMagnitude > 0f)
                ApplyRigidbodyPose(simulationState.Position, default, false);
        }
    }

    private void CaptureSceneState()
    {
        simulationState.Position = playerRigidbody != null ? playerRigidbody.position : transform.position;
        simulationState.RootRotation = playerRigidbody != null ? playerRigidbody.rotation : transform.rotation;
        if (movement != null)
            simulationState.ArmatureRotation = movement.ArmatureRotation;
        simulationState.IsGrounded = grounding != null && grounding.IsGrounded;
        simulationState.GroundNormal = grounding != null ? grounding.GroundNormal : Vector3.up;
        simulationState.GroundSurfaceDistance = grounding != null ? grounding.GroundSurfaceDistance : 0f;
        simulationState.IsSoul = grounding != null && grounding.Mode == PlayerGroundingSensor.GroundingMode.Soul;
    }

    private LayerMask ResolveObstacleMask()
    {
        int mask = gameRule != null
            ? gameRule.ObstacleMask.value
            : LayerMask.GetMask("Default", "Ground", "Wall", "Env");
        int playerBit = LayerMask.GetMask("Player");
        int soulBit = LayerMask.GetMask("Soul");

        mask &= ~playerBit;
        mask &= ~soulBit;
        if (!simulationState.IsSoul && gameRule != null && gameRule.BlockOtherPlayers)
            mask |= playerBit;

        return mask;
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
        simulationState.VerticalVelocity = 0f;
    }

    private void ClearPendingMotion()
    {
        pendingVelocity = Vector3.zero;
        pendingGroundedDisplacement = Vector3.zero;
        pendingDisplacement = Vector3.zero;
        hasPendingPose = false;
        simulationState.WasBlockedThisTick = false;
    }

    private void OnValidate()
    {
        collisionSkin = Mathf.Max(0f, collisionSkin);
        maxSweepIterations = Mathf.Max(1, maxSweepIterations);
    }

    private sealed class RuntimeMotionResolver : IPlayerSimulationMotionResolver
    {
        private readonly PlayerMotor owner;

        public RuntimeMotionResolver(PlayerMotor owner) => this.owner = owner;

        public Vector3 Resolve(
            PlayerSimulationState state,
            Vector3 desiredDelta,
            Vector3 horizontalStepDelta,
            PlayerSimulationSettings settings)
        {
            if (owner.capsule == null)
                return desiredDelta;

            Vector3 currentPosition = owner.playerRigidbody != null
                ? owner.playerRigidbody.position
                : owner.transform.position;
            return PlayerMotionSweep.Resolve(
                owner.capsule,
                state.Position - currentPosition,
                desiredDelta,
                horizontalStepDelta,
                state.IsGrounded,
                settings.StepOffset,
                settings.MaxWalkableSlopeAngle,
                settings.ObstacleMask,
                settings.CollisionSkin,
                settings.MaxSweepIterations,
                owner.castBuffer);
        }
    }
}
