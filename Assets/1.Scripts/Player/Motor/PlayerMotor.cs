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
    private Player player;
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

    // 서버의 관측 전용 병행 시뮬레이션은 실제 Motor 상태/Transform과 완전히 분리한다.
    private Vector3 serverPendingVelocity;
    private Vector3 serverPendingGroundedDisplacement;
    private Vector3 serverPendingDisplacement;
    private Vector3 serverPendingPosePosition;
    private Quaternion serverPendingPoseRotation;
    private bool serverHasPendingPose;
    private bool hasServerObservationState;
    private PlayerSimulationState serverObservationState;

    public bool WasBlockedThisTick => simulationState.WasBlockedThisTick;
    public event System.Action<Vector3, Vector3, bool> MovementResolved;
    public event System.Action<PlayerRawSimulationInput, PlayerSimulationState> SimulationCompleted;

    public Vector3 Position => playerRigidbody != null ? playerRigidbody.position : transform.position;
    public float VerticalVelocity => simulationState.VerticalVelocity;
    public Vector3 KnockbackVelocity => simulationState.KnockbackVelocity;
    public bool GravityEnabled => simulationState.GravityEnabled;
    public MotorMode Mode => mode;
    public PlayerGameRuleData GameRule => gameRule;
    public PlayerSimulationState SimulationState => simulationState;
    internal PlayerSimulationState ServerObservationState => serverObservationState;

    private bool CapturesServerObservation =>
        player != null && player.IsSpawned && player.IsServer;

    /// <summary>이번 물리 틱에 적용할 월드 속도(m/s)를 더한다.</summary>
    public void AddVelocity(Vector3 worldVelocity)
    {
        if (isActiveAndEnabled)
            pendingVelocity += worldVelocity;
        if (CapturesServerObservation)
            serverPendingVelocity += worldVelocity;
    }

    /// <summary>플랫폼 캐리·루트모션 같은 월드 변위(m)를 더한다. 경사 투영하지 않는다.</summary>
    public void AddDisplacement(Vector3 worldDelta)
    {
        if (isActiveAndEnabled)
            pendingDisplacement += worldDelta;
        if (CapturesServerObservation)
            serverPendingDisplacement += worldDelta;
    }

    /// <summary>
    /// 걷기·대시와 같이 접지면에 투영할 자발 이동 변위(m)를 더한다.
    /// 반드시 수평(y≈0) 벡터만 넣고 수직 성분은 <see cref="AddDisplacement"/>로 분리한다.
    /// </summary>
    public void AddGroundedDisplacement(Vector3 worldDelta)
    {
        if (!isActiveAndEnabled && !CapturesServerObservation)
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

        if (isActiveAndEnabled)
            pendingGroundedDisplacement += worldDelta;
        if (CapturesServerObservation)
            serverPendingGroundedDisplacement += worldDelta;
    }

    /// <summary>구속 추종용 절대 포즈. 같은 틱에는 마지막 값이 이기고 다른 이동보다 우선한다.</summary>
    public void SetPoseTarget(Vector3 worldPosition, Quaternion worldRotation)
    {
        if (!isActiveAndEnabled && !CapturesServerObservation)
            return;

        if (isActiveAndEnabled)
        {
            pendingPosePosition = worldPosition;
            pendingPoseRotation = worldRotation;
            hasPendingPose = true;
        }

        if (CapturesServerObservation)
        {
            serverPendingPosePosition = worldPosition;
            serverPendingPoseRotation = worldRotation;
            serverHasPendingPose = true;
        }
    }

    public void SetGravityEnabled(bool enabled)
    {
        simulationState.GravityEnabled = enabled;
        if (!enabled)
            simulationState.VerticalVelocity = 0f;

        if (hasServerObservationState)
        {
            serverObservationState.GravityEnabled = enabled;
            if (!enabled)
                serverObservationState.VerticalVelocity = 0f;
        }

        if (mode == MotorMode.Dynamic && playerRigidbody != null)
            playerRigidbody.useGravity = enabled;
    }

    public void SetKnockbackVelocity(Vector3 velocity)
    {
        simulationState.KnockbackVelocity = velocity;
        if (hasServerObservationState)
            serverObservationState.KnockbackVelocity = velocity;
    }

    public void ClearKnockbackVelocity() => SetKnockbackVelocity(Vector3.zero);

    public void SetDashMotion(Vector3 direction, float speed, float remainingTime)
    {
        direction.y = 0f;
        simulationState.DashDirection = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector3.zero;
        simulationState.DashSpeed = Mathf.Max(0f, speed);
        simulationState.DashRemainingTime = Mathf.Max(0f, remainingTime);

        if (hasServerObservationState)
        {
            serverObservationState.DashDirection = simulationState.DashDirection;
            serverObservationState.DashSpeed = simulationState.DashSpeed;
            serverObservationState.DashRemainingTime = simulationState.DashRemainingTime;
        }
    }

    public void ClearDashMotion()
    {
        simulationState.DashDirection = Vector3.zero;
        simulationState.DashSpeed = 0f;
        simulationState.DashRemainingTime = 0f;

        if (hasServerObservationState)
        {
            serverObservationState.DashDirection = Vector3.zero;
            serverObservationState.DashSpeed = 0f;
            serverObservationState.DashRemainingTime = 0f;
        }
    }

    public void SynchronizeArmatureRotation(Quaternion rotation, bool? hasRotate = null)
    {
        simulationState.ArmatureRotation = rotation;
        if (hasRotate.HasValue)
            simulationState.HasRotate = hasRotate.Value;

        if (hasServerObservationState)
        {
            serverObservationState.ArmatureRotation = rotation;
            if (hasRotate.HasValue)
                serverObservationState.HasRotate = hasRotate.Value;
        }
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
        player = GetComponent<Player>();
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

        PlayerRawSimulationInput rawInput = movement != null
            ? movement.CaptureRawSimulationInput()
            : default;
        PlayerSimulationInput input = movement != null
            ? movement.CaptureSimulationInput(rawInput)
            : new PlayerSimulationInput { MoveSpeedMultiplier = 1f };
        input.AddedVelocity = pendingVelocity;
        input.GroundedDisplacement = pendingGroundedDisplacement;
        input.Displacement = pendingDisplacement;
        input.HasPoseTarget = hasPendingPose;
        input.PosePosition = pendingPosePosition;
        input.PoseRotation = pendingPoseRotation;

        PlayerSimulationSettings settings = CaptureSimulationSettings(simulationState.IsSoul);

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

        SimulationCompleted?.Invoke(rawInput, simulationState);
    }

    /// <summary>
    /// 서버가 받은 raw 입력을 서버 자신의 게이트/배율/게임 로직 산출물과 합쳐 병행 계산한다.
    /// 결과는 관측 상태에만 저장하며 Rigidbody, Transform, Movement 상태, 이벤트에는 적용하지 않는다.
    /// </summary>
    internal bool TrySimulateServerObservation(
        PlayerRawSimulationInput rawInput,
        float deltaTime,
        out PlayerSimulationState resultState)
    {
        resultState = default;
        if (!CapturesServerObservation || mode == MotorMode.Dynamic)
            return false;

        if (!hasServerObservationState)
        {
            serverObservationState = simulationState;
            CaptureSceneState(ref serverObservationState, true);
            serverObservationState.WasBlockedThisTick = false;
            hasServerObservationState = true;
        }
        else
        {
            // 위치/회전은 관측 상태를 누적하고, 접지처럼 서버가 매 틱 직접 아는 환경 값만 갱신한다.
            CaptureSceneState(ref serverObservationState, false);
        }

        PlayerSimulationInput input = movement != null
            ? movement.CaptureSimulationInput(rawInput)
            : new PlayerSimulationInput
            {
                MoveDirection = rawInput.MoveDirection,
                HasMoveInput = rawInput.HasMoveInput,
                MoveSpeedMultiplier = 1f
            };
        input.AddedVelocity = serverPendingVelocity;
        input.GroundedDisplacement = serverPendingGroundedDisplacement;
        input.Displacement = serverPendingDisplacement;
        input.HasPoseTarget = serverHasPendingPose;
        input.PosePosition = serverPendingPosePosition;
        input.PoseRotation = serverPendingPoseRotation;

        PlayerSimulationResult result = PlayerMovementSimulation.Simulate(
            serverObservationState,
            input,
            CaptureSimulationSettings(serverObservationState.IsSoul),
            motionResolver,
            deltaTime);

        ClearServerPendingMotion();
        serverObservationState = result.State;
        resultState = serverObservationState;
        return true;
    }

    internal void ResetServerObservation()
    {
        hasServerObservationState = false;
        serverObservationState = default;
        ClearServerPendingMotion();
    }

    private PlayerSimulationSettings CaptureSimulationSettings(bool isSoul)
    {
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
        settings.ObstacleMask = ResolveObstacleMask(isSoul);
        settings.CollisionSkin = collisionSkin;
        settings.MaxSweepIterations = maxSweepIterations;
        return settings;
    }

    private void CaptureSceneState()
    {
        CaptureSceneState(ref simulationState, true);
    }

    private void CaptureSceneState(ref PlayerSimulationState state, bool capturePose)
    {
        if (capturePose)
        {
            state.Position = playerRigidbody != null ? playerRigidbody.position : transform.position;
            state.RootRotation = playerRigidbody != null ? playerRigidbody.rotation : transform.rotation;
            if (movement != null)
                state.ArmatureRotation = movement.ArmatureRotation;
        }

        state.IsGrounded = grounding != null && grounding.IsGrounded;
        state.GroundNormal = grounding != null ? grounding.GroundNormal : Vector3.up;
        state.GroundSurfaceDistance = grounding != null ? grounding.GroundSurfaceDistance : 0f;
        state.IsSoul = grounding != null && grounding.Mode == PlayerGroundingSensor.GroundingMode.Soul;
    }

    private LayerMask ResolveObstacleMask(bool isSoul)
    {
        int mask = gameRule != null
            ? gameRule.ObstacleMask.value
            : LayerMask.GetMask("Default", "Ground", "Wall", "Env");
        int playerBit = LayerMask.GetMask("Player");
        int soulBit = LayerMask.GetMask("Soul");

        mask &= ~playerBit;
        mask &= ~soulBit;
        if (!isSoul && gameRule != null && gameRule.BlockOtherPlayers)
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

    private void ClearServerPendingMotion()
    {
        serverPendingVelocity = Vector3.zero;
        serverPendingGroundedDisplacement = Vector3.zero;
        serverPendingDisplacement = Vector3.zero;
        serverHasPendingPose = false;
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
