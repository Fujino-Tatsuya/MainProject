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
    // 한 틱 최대 이동량(대시 ~20m/s × 0.02s = 0.4m)보다 넉넉히 크게 잡아 정상 이동을 오탐하지 않는다.
    private const float ExternalMoveDetectionThreshold = 1.0f;

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

    // 서버가 원격 오너의 raw 입력을 소비하는 커밋 경로. 입력/상태는 일반 Motor 틱과 분리해 한 번만 계산한다.
    private Vector3 serverPendingVelocity;
    private Vector3 serverPendingGroundedDisplacement;
    private Vector3 serverPendingDisplacement;
    private Vector3 serverPendingPosePosition;
    private Quaternion serverPendingPoseRotation;
    private bool serverHasPendingPose;
    private bool hasServerObservationState;

    // 외부 이동 감지용 — Motor가 직전 틱에 커밋한 위치. 이번 틱에 읽은 transform과 다르면
    // 이 경로 밖의 무언가가 오브젝트를 옮긴 것이다(낙사 복귀·텔레포트·복제 등). 원인을 몰라도 범인이 잡힌다.
    private Vector3 lastCommittedServerPosition;
    private bool hasLastCommittedServerPosition;
    private PlayerSimulationState serverObservationState;

    // [MoveDiag] 진단 전용 상태. UNITY_EDITOR 빌드에서만 호출되며 시뮬레이션 값에는 관여하지 않는다.
    private int movementDiagnosticTickCount;
    private bool reportedLocalNonFinite;
    private bool reportedServerNonFinite;

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
    internal int MovementDiagnosticTickCount => movementDiagnosticTickCount;

    private bool CapturesServerObservation =>
        player != null && player.IsSpawned && player.IsServer && !player.IsOwner;

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
        if (CapturesServerObservation)
        {
            // 원격 플레이어는 Player.ProcessServerObservationInputs가 틱당 한 번만 시뮬레이션한다.
            // enabled 상태는 외부 이동 채널을 받기 위해 유지하되 이 자체 FixedUpdate는 커밋하지 않는다.
            ClearPendingMotion();
            return;
        }

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
        RecordMovementDiagnosticTick();
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

        DiagnoseSimulationInput("owner", rawInput, simulationState, input, settings, deltaTime);

        PlayerSimulationResult result = PlayerMovementSimulation.Simulate(
            simulationState, input, settings, motionResolver, deltaTime);

        DiagnoseSimulationResult("owner", result);

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
    /// 서버가 받은 raw 입력을 서버 자신의 게이트/배율/게임 로직 산출물과 합쳐 계산하고 커밋한다.
    /// 원격 오너의 일반 FixedUpdate는 이 경로와 이중 구동되지 않도록 건너뛴다.
    /// </summary>
    internal bool TrySimulateServerObservation(
        PlayerRawSimulationInput rawInput,
        float deltaTime,
        out PlayerSimulationState resultState)
    {
        resultState = default;
        if (!CapturesServerObservation || mode == MotorMode.Dynamic)
            return false;

        RecordMovementDiagnosticTick();
        if (!hasServerObservationState)
        {
            serverObservationState = simulationState;
            serverObservationState.WasBlockedThisTick = false;
            hasServerObservationState = true;
        }

        // 🔴 위치·회전을 **매 틱 실제 transform에서 다시 읽는다.**
        //
        // b1에서 이 경로는 순수 관측이라 첫 틱에만 포즈를 잡고 이후에는 접지만 갱신했다. 복제된
        // 위치가 비교를 오염시키면 안 됐기 때문이고, 그때는 그게 옳았다.
        // 4b2-α가 이 경로를 **커밋 경로로 승격**시키면서 그 전제가 뒤집혔다 — 이제 이 상태가
        // 권위이므로 실제 transform에 앵커돼 있어야 한다.
        //
        // 앵커하지 않으면 스폰 포즈·낙사 복귀·보스 텔레포트·NGO 스폰 동기화처럼 이 경로 **밖에서**
        // 오브젝트를 옮기는 모든 것이 관측 상태를 영구히 어긋나게 만든다. 어긋난 뒤로는 두 시뮬레이션이
        // 같은 입력으로 같은 운동을 하되 서로 다른 원점에서 굴러, 발산이 **고정 오프셋**으로 굳는다.
        // 2026-09-15 실측: avg=max=50.593m 가 소수점까지 변하지 않았다(은희 MPPM 세션).
        //
        // 일반 모터 경로는 이미 매 틱 CaptureSceneState()로 같은 일을 한다(이 파일 286행).
        // 커밋 경로도 같아야 한다. 정상 상태에서는 우리 자신의 커밋을 되읽는 것이라 무해하고,
        // 외부가 오브젝트를 옮기면 다음 틱에 스스로 복구된다.
        // 🔴 외부 이동 감지 — 앵커로 읽어들이기 **직전에** 비교해야 한다.
        // Motor가 직전 틱에 커밋한 위치와 지금 transform이 다르면 이 경로 밖의 무언가가 옮긴 것이다.
        // 낙사 복귀(PlayerFallRecovery는 transform/body.position을 직접 쓴다), 보스 텔레포트,
        // 복제, 스폰 재배치 등. 원인을 몰라도 "누가 옮겼나"가 잡힌다.
        Vector3 positionBeforeAnchor = playerRigidbody != null ? playerRigidbody.position : transform.position;
        if (hasLastCommittedServerPosition)
        {
            float externalShift = Vector3.Distance(positionBeforeAnchor, lastCommittedServerPosition);
            if (externalShift > ExternalMoveDetectionThreshold)
            {
                Transform parent = transform.parent;
                Edit.LogWarning(
                    $"[MoveDiag] 외부가 서버 플레이어를 옮겼습니다 — Motor 커밋={lastCommittedServerPosition}, " +
                    $"이번 틱 rb.position={positionBeforeAnchor}, 이동거리={externalShift:F3}m | " +
                    $"transform.position={transform.position}, localPosition={transform.localPosition}, " +
                    $"parent={(parent != null ? parent.name : "none")}" +
                    $"{(parent != null ? $"@{parent.position} scale{parent.lossyScale}" : string.Empty)}, " +
                    $"lossyScale={transform.lossyScale}, isKinematic={playerRigidbody?.isKinematic}, " +
                    $"interpolation={playerRigidbody?.interpolation}", this);
            }
        }

        CaptureSceneState(ref serverObservationState, true);

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

        PlayerSimulationSettings settings =
            CaptureSimulationSettings(serverObservationState.IsSoul);
        DiagnoseSimulationInput(
            "server-observation",
            rawInput,
            serverObservationState,
            input,
            settings,
            deltaTime);

        PlayerSimulationResult result = PlayerMovementSimulation.Simulate(
            serverObservationState,
            input,
            settings,
            motionResolver,
            deltaTime);

        DiagnoseSimulationResult("server-observation", result);

        ClearServerPendingMotion();
        serverObservationState = result.State;
        resultState = serverObservationState;

        simulationState = serverObservationState;
        movement?.CommitSimulationState(simulationState, !result.AppliesPoseRotation);

        if (result.AppliesPoseRotation)
        {
            ApplyRigidbodyPose(simulationState.Position, simulationState.RootRotation, true);
            MovementResolved?.Invoke(result.RequestedDelta, result.ResolvedDelta, result.WasBlocked);
        }
        else
        {
            MovementResolved?.Invoke(result.RequestedDelta, result.ResolvedDelta, result.WasBlocked);
            if (result.ResolvedDelta.sqrMagnitude > 0f)
                ApplyRigidbodyPose(simulationState.Position, default, false);
        }

        // 다음 틱의 외부 이동 감지 기준. 커밋한 위치를 그대로 기억한다.
        lastCommittedServerPosition = simulationState.Position;
        hasLastCommittedServerPosition = true;

        return true;
    }

    internal void ResetServerObservation()
    {
        hasServerObservationState = false;
        serverObservationState = default;
        lastCommittedServerPosition = Vector3.zero;
        hasLastCommittedServerPosition = false;
        ClearServerPendingMotion();
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    internal void BeginMovementDiagnostics()
    {
        movementDiagnosticTickCount = 0;
        reportedLocalNonFinite = false;
        reportedServerNonFinite = false;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void RecordMovementDiagnosticTick()
    {
        movementDiagnosticTickCount++;
        if (movementDiagnosticTickCount != 1)
            return;

        Edit.Log(
            $"[MoveDiag] Motor first Tick: {DiagnosticIdentity()}, mode={mode}, enabled={enabled}",
            this);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void DiagnoseSimulationInput(
        string path,
        PlayerRawSimulationInput rawInput,
        PlayerSimulationState state,
        PlayerSimulationInput input,
        PlayerSimulationSettings settings,
        float deltaTime)
    {
        if (HasReportedNonFinite(path))
            return;

        System.Text.StringBuilder channels = null;
        AppendIfNonFinite(ref channels, "raw.Direction", rawInput.MoveDirection);
        AppendIfNonFinite(ref channels, "state.Position", state.Position);
        AppendIfNonFinite(ref channels, "state.RootRotation", state.RootRotation);
        AppendIfNonFinite(ref channels, "state.ArmatureRotation", state.ArmatureRotation);
        AppendIfNonFinite(ref channels, "state.PreviousRotateDirection", state.PreviousRotateDirection);
        AppendIfNonFinite(ref channels, "state.VerticalVelocity", state.VerticalVelocity);
        AppendIfNonFinite(ref channels, "state.CurrentSpeed", state.CurrentSpeed);
        AppendIfNonFinite(ref channels, "ground.GroundNormal", state.GroundNormal);
        AppendIfNonFinite(ref channels, "ground.GroundSurfaceDistance", state.GroundSurfaceDistance);
        AppendIfNonFinite(ref channels, "velocity.Knockback", state.KnockbackVelocity);
        AppendIfNonFinite(ref channels, "velocity.DashDirection", state.DashDirection);
        AppendIfNonFinite(ref channels, "velocity.DashSpeed", state.DashSpeed);
        AppendIfNonFinite(ref channels, "velocity.DashRemainingTime", state.DashRemainingTime);
        AppendIfNonFinite(ref channels, "input.MoveDirection", input.MoveDirection);
        AppendIfNonFinite(ref channels, "input.FixedMoveSpeed", input.FixedMoveSpeed);
        AppendIfNonFinite(ref channels, "input.MoveSpeedMultiplier", input.MoveSpeedMultiplier);
        AppendIfNonFinite(ref channels, "input.AddedVelocity", input.AddedVelocity);
        AppendIfNonFinite(ref channels, "input.GroundedDisplacement", input.GroundedDisplacement);
        AppendIfNonFinite(ref channels, "input.Displacement", input.Displacement);
        if (input.HasPoseTarget)
        {
            AppendIfNonFinite(ref channels, "input.PosePosition", input.PosePosition);
            AppendIfNonFinite(ref channels, "input.PoseRotation", input.PoseRotation);
        }
        AppendIfNonFinite(ref channels, "settings.ViewYaw", settings.ViewYaw);
        AppendIfNonFinite(ref channels, "settings.GravityY", settings.GravityY);
        AppendIfNonFinite(ref channels, "settings.MaxFallSpeed", settings.MaxFallSpeed);
        AppendIfNonFinite(ref channels, "settings.KnockbackDeceleration", settings.KnockbackDeceleration);
        AppendIfNonFinite(ref channels, "deltaTime", deltaTime);

        if (channels == null)
            return;

        MarkReportedNonFinite(path);
        Edit.LogError(
            $"[MoveDiag] non-finite before Simulate: {DiagnosticIdentity()}, path={path}, " +
            $"channels={channels}, raw={rawInput.MoveDirection}/{rawInput.HasMoveInput}, " +
            $"position={state.Position}, ground={state.GroundNormal}/{state.GroundSurfaceDistance}",
            this);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void DiagnoseSimulationResult(string path, PlayerSimulationResult result)
    {
        if (HasReportedNonFinite(path))
            return;

        System.Text.StringBuilder channels = null;
        AppendIfNonFinite(ref channels, "sweep.DesiredDelta", result.RequestedDelta);
        AppendIfNonFinite(ref channels, "sweep.ResolvedDelta", result.ResolvedDelta);
        AppendIfNonFinite(ref channels, "result.FinalPosition", result.State.Position);
        AppendIfNonFinite(ref channels, "result.RootRotation", result.State.RootRotation);
        AppendIfNonFinite(ref channels, "result.ArmatureRotation", result.State.ArmatureRotation);
        AppendIfNonFinite(ref channels, "result.VerticalVelocity", result.State.VerticalVelocity);
        AppendIfNonFinite(ref channels, "result.CurrentSpeed", result.State.CurrentSpeed);
        AppendIfNonFinite(ref channels, "result.GroundNormal", result.State.GroundNormal);
        AppendIfNonFinite(ref channels, "result.GroundSurfaceDistance", result.State.GroundSurfaceDistance);
        AppendIfNonFinite(ref channels, "result.KnockbackVelocity", result.State.KnockbackVelocity);

        if (channels == null)
            return;

        MarkReportedNonFinite(path);
        Edit.LogError(
            $"[MoveDiag] non-finite after Simulate: {DiagnosticIdentity()}, path={path}, " +
            $"channels={channels}, desired={result.RequestedDelta}, resolved={result.ResolvedDelta}, " +
            $"finalPosition={result.State.Position}",
            this);
    }

    private bool HasReportedNonFinite(string path)
    {
        return path == "server-observation" ? reportedServerNonFinite : reportedLocalNonFinite;
    }

    private void MarkReportedNonFinite(string path)
    {
        if (path == "server-observation")
            reportedServerNonFinite = true;
        else
            reportedLocalNonFinite = true;
    }

    private string DiagnosticIdentity()
    {
        if (player == null)
            return "ownerClientId=unknown, localClientId=unknown";

        ulong localClientId = player.NetworkManager != null
            ? player.NetworkManager.LocalClientId
            : ulong.MaxValue;
        return $"ownerClientId={player.OwnerClientId}, localClientId={localClientId}";
    }

    private static void AppendIfNonFinite(
        ref System.Text.StringBuilder channels,
        string channel,
        float value)
    {
        if (!float.IsNaN(value) && !float.IsInfinity(value))
            return;

        channels ??= new System.Text.StringBuilder();
        if (channels.Length > 0)
            channels.Append(", ");
        channels.Append(channel).Append('=').Append(value);
    }

    private static void AppendIfNonFinite(
        ref System.Text.StringBuilder channels,
        string channel,
        Vector2 value)
    {
        if (IsFinite(value.x) && IsFinite(value.y))
            return;

        channels ??= new System.Text.StringBuilder();
        if (channels.Length > 0)
            channels.Append(", ");
        channels.Append(channel).Append('=').Append(value);
    }

    private static void AppendIfNonFinite(
        ref System.Text.StringBuilder channels,
        string channel,
        Vector3 value)
    {
        if (IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z))
            return;

        channels ??= new System.Text.StringBuilder();
        if (channels.Length > 0)
            channels.Append(", ");
        channels.Append(channel).Append('=').Append(value);
    }

    private static void AppendIfNonFinite(
        ref System.Text.StringBuilder channels,
        string channel,
        Quaternion value)
    {
        if (IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w))
            return;

        channels ??= new System.Text.StringBuilder();
        if (channels.Length > 0)
            channels.Append(", ");
        channels.Append(channel).Append('=').Append(value);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
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
