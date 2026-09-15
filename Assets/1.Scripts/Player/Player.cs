using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Unity.Netcode.Components;

[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerMotor))]
[RequireComponent(typeof(PlayerAimIndicator))]
[RequireComponent(typeof(DefaultAttackController))]
[RequireComponent(typeof(PlayerStateController))]
[RequireComponent(typeof(StatusEffectController))]
public class Player : Unit
{
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private const float ReconciliationHistorySeconds = 0.5f;
    private const double ReconciliationLogIntervalSeconds = 1.0;
    private const double MovementHeartbeatTimeoutSeconds = 2.0;
    private const double MovementRpcLogIntervalSeconds = 1.0;
    private const int MaxRepeatedServerInputTicks = 10;
    private const int TargetServerInputQueueTicks = 2;
    private const int MaxServerInputQueueTicks = 6;

    /// <summary>이 클라이언트가 조작하는 플레이어. HUD 등 로컬 UI 바인딩용.</summary>
    public static Player LocalPlayer { get; private set; }
    public static event System.Action<Player> LocalPlayerChanged;

    private static void SetLocalPlayer(Player player)
    {
        if (LocalPlayer == player)
            return;

        LocalPlayer = player;
        LocalPlayerChanged?.Invoke(player);
    }

    [SerializeField] private Animator animator;
    [SerializeField] private float interruptDuration = 0.5f;
    [SerializeField] private float interruptForwardDistance = 0.5f;

    [Header("\n초기화 값")]
    [SerializeField] int attackDamage;
    [SerializeField] float moveSpeed;
    [SerializeField] float attackSpeed;
    [SerializeField] int maxHp;
    [SerializeField] int defense;

    [Header("\n이동 플랫폼 캐리")]
    [Tooltip("발밑 검사 거리(m).")]
    [SerializeField] private float platformGroundCheckDistance = 0.6f;

    private PlayerStateController stateController;
    private DefaultAttackController defaultAttack;
    private FirstMeleePassive passive;
    private PlayerMotor motor;
    private PlayerGroundingSensor groundingSensor;
    private PlayerInvulnerability invulnerability;
    private PlayerInputReader inputReader;
    private NetworkTransform networkTransform;
    private bool networkTransformMissingWarningLogged;
    private PlayerTickRingBuffer<PlayerRawSimulationInput> ownerRawInputHistory;
    private PlayerTickRingBuffer<PlayerSimulationState> ownerSimulationStateHistory;
    private readonly Queue<ServerRawSimulationInput> serverRawInputQueue =
        new Queue<ServerRawSimulationInput>();
    private ServerRawSimulationInput lastServerRawInput;
    private bool hasLastServerRawInput;
    private int repeatedServerInputTicks;
    private int droppedServerInputCount;

    private int reconSampleCount;
    private int reconDiscardedSampleCount;
    private int reconTickMismatchSampleCount;
    private double reconDivergenceSum;
    private float reconMaxDivergence;
    private double reconWindowStartedAt;
    private long reconWindowFirstTick;
    private long reconWindowLastTick;
    private long reconWindowLastServerTick;

    // [MoveDiag] 관측 전용 카운터. 게임 상태나 RPC 게이트에는 사용하지 않는다.
    private double movementHeartbeatDeadline;
    private double movementRpcWindowEndsAt;
    private int movementRpcSentCount;
    private int movementRpcReceivedCount;
    private bool movementHeartbeatWarningLogged;

    private readonly struct ServerRawSimulationInput
    {
        public ServerRawSimulationInput(
            long tick,
            PlayerRawSimulationInput input,
            Vector3 ownerPredictedPosition,
            double rttSeconds)
        {
            Tick = tick;
            Input = input;
            OwnerPredictedPosition = ownerPredictedPosition;
            HasUsableOwnerPrediction = IsFinite(ownerPredictedPosition);
            RttSeconds = rttSeconds;
        }

        public long Tick { get; }
        public PlayerRawSimulationInput Input { get; }
        public Vector3 OwnerPredictedPosition { get; }
        public bool HasUsableOwnerPrediction { get; }
        public double RttSeconds { get; }
    }

    public PlayerActionState CurrentState => stateController != null ? stateController.CurrentState : PlayerActionState.Idle;
    public bool CanMove => stateController == null || stateController.CanMove;
    public bool CanMovementRotate => stateController == null || stateController.CanMovementRotate;
    public float InterruptDuration => interruptDuration;
    public float InterruptForwardDistance => interruptForwardDistance;

    private void Awake()
    {
        // StatusEffectController는 NetworkBehaviour라 런타임 추가가 불가 — 프리팹에 미리 부착돼 있어야 한다
        if (GetComponent<StatusEffectController>() == null)
            Debug.LogError("[Player] StatusEffectController가 프리팹에 부착되어 있지 않습니다.", this);

        stateController = GetComponent<PlayerStateController>();
        if (stateController == null)
            stateController = gameObject.AddComponent<PlayerStateController>();

        defaultAttack = GetComponent<DefaultAttackController>();
        passive = GetComponent<FirstMeleePassive>();
        motor = GetComponent<PlayerMotor>();
        groundingSensor = GetComponent<PlayerGroundingSensor>();
        invulnerability = GetComponent<PlayerInvulnerability>();
        inputReader = GetComponent<PlayerInputReader>();
        networkTransform = GetComponent<NetworkTransform>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        int historyCapacity = PlayerTickRingBuffer<PlayerSimulationState>.CapacityForSeconds(
            ReconciliationHistorySeconds,
            Time.fixedDeltaTime);
        ownerRawInputHistory = new PlayerTickRingBuffer<PlayerRawSimulationInput>(historyCapacity);
        ownerSimulationStateHistory = new PlayerTickRingBuffer<PlayerSimulationState>(historyCapacity);

        if (motor != null)
            motor.SimulationCompleted += HandleOwnerSimulationCompleted;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        ResetReconciliationObservation();
        BeginMovementDiagnostics();

        // 내가 Owner인 플레이어가 스폰되면, 카메라 매니저에게 나를 따라오라고 알린다.
        if (IsOwner)
        {
            CameraTargetSwitcher.Active?.FocusOwnerPlayer();
            SetLocalPlayer(this);
            EnableLocalInput();
        }
        else
        {
            // HUD는 Player 프리팹 자식으로 스폰되므로, 원격 플레이어의 HUD 캔버스가 겹치지 않게 끈다
            CombatHUD childHud = GetComponentInChildren<CombatHUD>(true);
            if (childHud != null)
                childHud.gameObject.SetActive(false);

            // AudioListener는 씬에 하나만 활성이어야 한다 — 원격 플레이어 것은 끈다
            AudioListener audioListener = GetComponentInChildren<AudioListener>(true);
            if (audioListener != null)
                audioListener.enabled = false;
        }

        if (IsServer)
            Initialize(attackDamage, moveSpeed, attackSpeed, maxHp, defense);

        ConfigureMovementAuthority("network-spawn");
    }

    public override void OnNetworkDespawn()
    {
        if (LocalPlayer == this)
            SetLocalPlayer(null);

        if (motor != null)
        {
            motor.ResetServerObservation();
            motor.enabled = false;
        }
        ownerRawInputHistory?.Clear();
        ownerSimulationStateHistory?.Clear();
        serverRawInputQueue.Clear();
        lastServerRawInput = default;
        hasLastServerRawInput = false;
        repeatedServerInputTicks = 0;
        droppedServerInputCount = 0;
        base.OnNetworkDespawn();
    }

    public override void OnGainedOwnership()
    {
        base.OnGainedOwnership();
        BeginMovementDiagnostics();
        ConfigureMovementAuthority("gained-ownership");
    }

    public override void OnLostOwnership()
    {
        base.OnLostOwnership();
        ConfigureMovementAuthority("lost-ownership");
    }

    private void Start()
    {
        // 오프라인(비네트워크) 실행은 OnNetworkSpawn이 불리지 않는다 — 테스트 씬 HUD 바인딩/입력 활성 폴백
        if (!IsNetworkActive)
        {
            BeginMovementDiagnostics();
            if (motor != null)
                motor.enabled = true;
            SetLocalPlayer(this);
            EnableLocalInput();
            LogMovementAuthorityState("offline-start");
        }
    }

    /// <summary>
    /// PlayerInput은 프리팹에서 기본 비활성 — 원격 플레이어 클론이 스폰 시 디바이스 페어링을 시도하며
    /// "Cannot find matching control scheme" 경고를 내는 것을 막기 위해 로컬(오너/오프라인)만 켠다.
    /// </summary>
    private void EnableLocalInput()
    {
        PlayerInput playerInput = GetComponent<PlayerInput>();
        if (playerInput != null && !playerInput.enabled)
            playerInput.enabled = true;
    }

    public override void OnDestroy()
    {
        // NGO NetworkBehaviour의 OnDestroy가 내부 정리를 수행하므로 반드시 base 호출
        if (LocalPlayer == this)
            SetLocalPlayer(null);

        if (motor != null)
            motor.SimulationCompleted -= HandleOwnerSimulationCompleted;

        base.OnDestroy();
    }

    private void Update()
    {
        UpdateMovementDiagnostics();

        if (IsNetworkActive &&
            !stateController.ShouldTickForNetwork(IsOwner, HasStateAuthority))
        {
            return;
        }

        stateController.Tick();
    }

    private void FixedUpdate()
    {
        // 플랫폼 변위는 Motor를 실제로 돌리는 피어만 제출한다. 원격 프록시는 서버 NT 결과만 표시한다.
        if (IsSimulating)
        {
            ApplyPlatformCarry();
        }

        if (!IsNetworkActive ||
            stateController.ShouldTickForNetwork(IsOwner, HasStateAuthority))
        {
            stateController.FixedTick();
        }

        // 네트워크 Update에서 도착한 raw 입력은 서버 물리 틱 파이프라인에서만 소비한다.
        if (IsNetworkActive && IsServer && !IsOwner)
            ProcessServerObservationInputs();
    }

    /// <summary>발밑에 캐리 표면이 있으면 그 이동량을 플레이어 이동에 가산한다.</summary>
    private void ApplyPlatformCarry()
    {
        if (motor == null)
        {
            return;
        }

        // RaycastAll: 자기 콜라이더가 먼저 맞아 캐리 표면 검출을 막지 않도록 전체 히트를 확인한다.
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            platformGroundCheckDistance,
            ResolvePlatformRiderMask(),
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hits.Length; i++)
        {
            ISurfaceCarrier carrier =
                hits[i].collider.GetComponentInParent<ISurfaceCarrier>();
            if (carrier != null)
            {
                motor.AddDisplacement(
                    carrier.GetCarryDelta(transform.position, Time.fixedDeltaTime));
                break;
            }
        }
    }

    private LayerMask ResolvePlatformRiderMask()
    {
        PlayerGameRuleData rule = motor != null ? motor.GameRule : null;
        if (rule == null)
            return LayerMask.GetMask("Default", "Ground", "Env");

        bool isSoul = groundingSensor != null &&
                      groundingSensor.Mode == PlayerGroundingSensor.GroundingMode.Soul;
        return rule.GetGroundMask(isSoul);
    }

    public void EndDefaultAttack()
    {
        defaultAttack.EndCurrentAttack();
    }

    public void HitDefaultAttack()
    {
        defaultAttack.HitCurrentAttack();
    }

    public void HandleDefaultAttackEvent(DefaultAttackAnimationEventType eventType)
    {
        defaultAttack.HandleAnimationEvent(eventType);
    }

    public void EndInterrupt()
    {
        stateController.EndInterrupt();
    }

    public bool BeginAttackState()
    {
        return stateController.ChangeState(PlayerActionState.Attack);
    }

    public bool EndAttackState()
    {
        if (stateController.CurrentState != PlayerActionState.Attack)
            return false;

        return stateController.ChangeState(PlayerActionState.Idle);
    }

    protected override void OnKnockback(Vector3 direction, float strength)
    {
        // 서버가 거부(사망 — 슈퍼아머는 Unit.Knockback에서 선차단)하면 오너에게도 전파하지 않는다
        bool accepted = stateController.BeginKnockback(direction, strength);
        if (!accepted)
            return;

        ApplyKnockbackClientRpc(direction, strength, CreateOwnerClientRpcParams());
    }

    /// <summary>
    /// 서버가 플레이어를 instigator에 구속한다(잡기 = <see cref="RestraintMode.Carry"/> /
    /// 돌진 밀기 = <see cref="RestraintMode.Push"/>).
    ///
    /// 반환값이 곧 계약이다 — <b>false면 구속되지 않았다</b>. 시전자는 이 값으로 후처리를 갈라야 한다
    /// (예: 돌진이 벽에 닿았을 때 <b>실제로 밀린 대상만</b> 기절시킨다). 데미지는 이 값과 무관하게 별도 경로다.
    ///
    /// Push는 시전자가 슈퍼아머 대상을 밀지 못한다(<see cref="Unit.Knockback"/>과 같은 규칙).
    /// Carry는 슈퍼아머와 무관하게 걸린다 — 기존 보스 Grab 체인의 동작이다.
    /// </summary>
    /// <param name="frontOffset">Push 전용. 시전자 정면으로 이만큼 앞에 붙는다.</param>
    public bool BeginRestrainedByInstigator(
        GameObject instigator, RestraintMode mode = RestraintMode.Carry, float frontOffset = 0f)
    {
        if (!IsServer)
            return false;

        NetworkObject instigatorNetworkObject =
            instigator != null ? instigator.GetComponentInParent<NetworkObject>() : null;
        if (instigatorNetworkObject == null)
        {
            Debug.LogError("[Player] Restraint instigator must belong to a spawned NetworkObject.", this);
            return false;
        }

        if (!stateController.BeginRestrained(instigator, mode, frontOffset))
            return false;

        // Transform은 복제할 수 없으므로 종류(byte)와 offset만 싣는다 — 오너가 같은 규칙으로 목표를 계산한다.
        BeginRestrainedClientRpc(
            new NetworkObjectReference(instigatorNetworkObject), (byte)mode, frontOffset,
            CreateOwnerClientRpcParams());
        return true;
    }

    public bool EndRestrainedByInstigator()
    {
        if (!IsServer)
            return false;

        bool ended = stateController.EndRestrained();
        EndRestrainedClientRpc(CreateOwnerClientRpcParams());
        return ended;
    }

    /// <summary>기존 잡기 호출부 호환 래퍼. 보스 Grab 체인이 이 시그니처로 동작 중이라 유지한다.</summary>
    public bool BeginGrabbedByInstigator(GameObject instigator)
    {
        return BeginRestrainedByInstigator(instigator, RestraintMode.Carry);
    }

    /// <summary>기존 잡기 호출부 호환 래퍼.</summary>
    public bool EndGrabbedByInstigator()
    {
        return EndRestrainedByInstigator();
    }

    [ClientRpc]
    private void BeginRestrainedClientRpc(
        NetworkObjectReference instigatorReference,
        byte restraintMode,
        float frontOffset,
        ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
            return;

        if (!instigatorReference.TryGet(out NetworkObject instigatorNetworkObject))
        {
            Debug.LogError("[Player] Restraint instigator NetworkObject could not be resolved on the owner.", this);
            return;
        }

        // 서버가 이미 승인한 전이다 — 오너는 슈퍼아머를 다시 판정하지 않는다(복제 지연 시 상태가 갈린다).
        stateController.ApplyRestrainedFromServer(
            instigatorNetworkObject.gameObject, (RestraintMode)restraintMode, frontOffset);
    }

    [ClientRpc]
    private void EndRestrainedClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
            return;

        stateController.EndRestrained();
    }

    [ClientRpc]
    private void ApplyKnockbackClientRpc(Vector3 direction, float strength, ClientRpcParams clientRpcParams = default)
    {
        // 호스트는 서버 경로의 BeginKnockback으로 이미 상태 진입 — 재진입 시 임펄스가 이중 적용됨
        if (!IsOwner || IsServer)
            return;

        stateController.ApplyKnockbackFromServer(direction, strength);
    }

    /// <summary>로컬 입력 장치와 로컬 UI를 읽는 주체.</summary>
    public bool IsInputSource => !IsNetworkActive || IsOwner;

    /// <summary>PlayerMotor 시뮬레이션을 로컬에서 수행하는 주체.</summary>
    public bool IsSimulating => !IsNetworkActive || IsOwner || IsServer;

    /// <summary>이동 결과를 진실로 확정하는 주체.</summary>
    public bool IsMotionAuthority => !IsNetworkActive || IsServer;

    /// <summary>서버가 확정해 복제한 이동 결과만 표시하는 원격 프록시.</summary>
    public bool IsRemoteProxy => IsNetworkActive && !IsOwner && !IsServer;

    /// <summary>
    /// Player 위치는 server-authority NetworkTransform이 복제한다.
    /// Rigidbody는 전 피어에서 kinematic이며, 원격 프록시는 Motor만 꺼 복제 위치와 경쟁하지 않게 한다.
    /// 콜라이더는 유지하므로 서버 공격 판정과 Overlap 쿼리에는 계속 참여한다.
    /// </summary>
    private void ConfigureMovementAuthority(string reason)
    {
        if (motor != null)
            motor.enabled = IsSimulating;

        if (networkTransform == null)
        {
            if (!networkTransformMissingWarningLogged)
            {
                networkTransformMissingWarningLogged = true;
                Debug.LogWarning("[Player] 루트 NetworkTransform을 찾지 못해 이동 복제 활성 상태를 구성할 수 없습니다.", this);
            }
        }
        else
        {
            bool enableNetworkTransform;
            if (IsServer && IsOwner)
            {
                // 호스트 자기 캐릭터: 서버 권위 결과를 다른 피어에 송신해야 하므로 NT를 유지한다.
                enableNetworkTransform = true;
            }
            else if (IsOwner)
            {
                // 클라이언트 자기 캐릭터: 서버 NT가 로컬 예측 위치를 덮어쓰지 않도록 NT만 끈다.
                enableNetworkTransform = false;
            }
            else
            {
                // 남의 캐릭터: 서버 확정 상태를 NT 보간으로 표시해야 하므로 NT를 유지한다.
                enableNetworkTransform = true;
            }

            networkTransform.enabled = enableNetworkTransform;
        }

        LogMovementAuthorityState(reason);
    }

    private void HandleOwnerSimulationCompleted(
        PlayerRawSimulationInput rawInput,
        PlayerSimulationState resultState)
    {
        NetworkClock clock = NetworkClock.Instance;
        if (!IsSpawned || !IsOwner || clock == null || !clock.IsRunning || !clock.HasMainGameStarted)
            return;

        long tick = CurrentSharedSimulationTick();

        // b2의 되감기/재생 입력과 서버 비교 대상. 동일 틱은 마지막 물리 호출 결과로 교체된다.
        ownerRawInputHistory.Store(tick, rawInput);
        ownerSimulationStateHistory.Store(tick, resultState);

        // 예측 위치는 같은 틱의 발산을 계측하기 위한 클라이언트 보고값일 뿐이며 게임 로직에 쓰지 않는다.
        // 호스트 오너는 이 인스턴스의 Motor 틱 자체가 서버 커밋이다. RPC 관측 경로를 다시 돌리면 이중 시뮬레이션된다.
        if (!IsServer)
        {
            SubmitMovementInputServerRpc(
                tick,
                rawInput.MoveDirection.x,
                rawInput.MoveDirection.y,
                rawInput.HasMoveInput,
                resultState.Position);
            RecordMovementRpcSent();
        }
    }

    [ServerRpc] // RequireOwnership 기본값 true. 예측 위치는 신뢰하지 않는 계측값이며 판정·이동·보정에 쓰지 않는다.
    private void SubmitMovementInputServerRpc(
        long tick,
        float moveX,
        float moveY,
        bool hasMoveInput,
        Vector3 ownerPredictedPosition,
        ServerRpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        RecordMovementRpcReceived();
        if (senderClientId != OwnerClientId)
        {
            Edit.LogWarning(
                $"[Recon] raw 입력 거부: sender={senderClientId}, owner={OwnerClientId}, tick={tick}",
                this);
            return;
        }

        Vector2 direction = new Vector2(moveX, moveY);
        if (!IsFinite(direction))
        {
            Edit.LogWarning($"[Recon] 비정상 raw 입력을 0으로 대체: owner={OwnerClientId}, tick={tick}", this);
            direction = Vector2.zero;
            hasMoveInput = false;
        }
        else
        {
            // 키보드 대각선은 (1, 1)일 수 있으므로 벡터 크기를 1로 정규화하면 오너 입력과 달라진다.
            // 축 범위만 제한하고 HasMoveInput은 InputReader가 만든 raw 플래그 그대로 사용한다.
            direction.x = Mathf.Clamp(direction.x, -1f, 1f);
            direction.y = Mathf.Clamp(direction.y, -1f, 1f);
        }

        serverRawInputQueue.Enqueue(new ServerRawSimulationInput(
            tick,
            new PlayerRawSimulationInput(direction, hasMoveInput),
            ownerPredictedPosition,
            GetSenderRttSeconds(senderClientId)));

        // 버스트가 최대치를 넘으면 최신 입력을 보존하고 가장 오래된 입력부터 폐기한다.
        while (serverRawInputQueue.Count > MaxServerInputQueueTicks)
        {
            serverRawInputQueue.Dequeue();
            droppedServerInputCount++;
        }
    }

    private void ProcessServerObservationInputs()
    {
        if (motor == null)
        {
            serverRawInputQueue.Clear();
            return;
        }

        // 정상 틱에는 하나를 소비한다. 버스트로 목표 길이를 넘은 경우에는 오래된 입력도 실제로
        // 시뮬레이션해 목표 길이까지 따라잡고, 최대 길이 초과분만 RPC 수신 시 폐기한다.
        int freshInputsToConsume = serverRawInputQueue.Count > 0
            ? 1 + Mathf.Max(0, serverRawInputQueue.Count - TargetServerInputQueueTicks)
            : 0;

        if (freshInputsToConsume > 0)
        {
            for (int i = 0; i < freshInputsToConsume; i++)
            {
                ServerRawSimulationInput freshInput = serverRawInputQueue.Dequeue();
                lastServerRawInput = freshInput;
                hasLastServerRawInput = true;
                repeatedServerInputTicks = 0;

                if (!SimulateServerObservationInput(freshInput, true))
                    return;
            }

            return;
        }

        ServerRawSimulationInput inputForTick;
        if (hasLastServerRawInput && repeatedServerInputTicks < MaxRepeatedServerInputTicks)
        {
            inputForTick = lastServerRawInput;
            repeatedServerInputTicks++;
        }
        else
        {
            // 입력 기아가 10틱을 넘으면 입력을 놓은 것으로 간주한다. 감속/중력은 계속 서버에서 시뮬레이션된다.
            inputForTick = new ServerRawSimulationInput(
                CurrentSharedSimulationTick(),
                default,
                default,
                hasLastServerRawInput ? lastServerRawInput.RttSeconds : 0.0);
        }

        SimulateServerObservationInput(inputForTick, false);
    }

    private bool SimulateServerObservationInput(
        ServerRawSimulationInput inputForTick,
        bool receivedFreshInput)
    {
        long serverTick = CurrentSharedSimulationTick();
        if (!motor.TrySimulateServerObservation(
                inputForTick.Input,
                Time.fixedDeltaTime,
                out PlayerSimulationState serverState))
        {
            return false;
        }

        // [Recon]은 실제 수신 샘플만 대상으로 하며 반복 입력에는 클라이언트의 같은 틱 보고가 없다.
        if (receivedFreshInput)
        {
            RecordReconciliationObservation(inputForTick, serverTick, serverState);
        }

        return true;
    }

    private void RecordReconciliationObservation(
        ServerRawSimulationInput input,
        long serverTick,
        PlayerSimulationState serverState)
    {
        double now = NetworkClock.Instance != null
            ? NetworkClock.Instance.MainGameElapsed
            : 0.0;

        if (reconSampleCount == 0 && reconDiscardedSampleCount == 0)
        {
            reconWindowStartedAt = now;
            reconWindowFirstTick = input.Tick;
        }

        reconWindowLastTick = input.Tick;
        reconWindowLastServerTick = serverTick;

        // 발산은 같은 틱 N의 오너 예측 위치와 서버 확정 위치만 비교한다. 서버가 N을 아직 돌지
        // 않았거나 이미 지났다면(input.Tick != serverTick) 억지로 정렬하지 않고 샘플을 폐기한다.
        // OwnerPredictedPosition은 클라이언트가 보고한 비신뢰 계측값이며 게임 로직에는 절대 사용하지 않는다.
        if (input.Tick != serverTick ||
            !input.HasUsableOwnerPrediction ||
            !IsFinite(serverState.Position))
        {
            reconDiscardedSampleCount++;
            if (input.Tick != serverTick)
                reconTickMismatchSampleCount++;
        }
        else
        {
            float divergence = Vector3.Distance(input.OwnerPredictedPosition, serverState.Position);
            if (!IsFinite(divergence))
            {
                reconDiscardedSampleCount++;
            }
            else
            {
                reconSampleCount++;
                reconDivergenceSum += divergence;
                reconMaxDivergence = Mathf.Max(reconMaxDivergence, divergence);
            }
        }

        if (now - reconWindowStartedAt < ReconciliationLogIntervalSeconds)
            return;

        string average = reconSampleCount > 0
            ? $"{reconDivergenceSum / reconSampleCount:F3}m"
            : "n/a";
        string maximum = reconSampleCount > 0
            ? $"{reconMaxDivergence:F3}m"
            : "n/a";
        Edit.Log(
            $"[Recon] owner={OwnerClientId} inputTicks={reconWindowFirstTick}..{reconWindowLastTick} " +
            $"serverTick={reconWindowLastServerTick} lagTicks={reconWindowLastServerTick - reconWindowLastTick} " +
            $"samples={reconSampleCount} discardedSamples={reconDiscardedSampleCount} " +
            $"tickMismatchSamples={reconTickMismatchSampleCount} droppedInputsTotal={droppedServerInputCount} " +
            $"divergence avg={average} max={maximum} " +
            $"RTT={input.RttSeconds * 1000.0:F1}ms (비신뢰 관측 전용, 보정 없음)",
            this);

        reconSampleCount = 0;
        reconDiscardedSampleCount = 0;
        reconTickMismatchSampleCount = 0;
        reconDivergenceSum = 0.0;
        reconMaxDivergence = 0f;
        reconWindowStartedAt = now;
    }

    private double GetSenderRttSeconds(ulong senderClientId)
    {
        if (NetworkManager == null ||
            senderClientId == NetworkManager.ServerClientId ||
            senderClientId == NetworkManager.LocalClientId)
        {
            return 0.0;
        }

        var transport = NetworkManager.NetworkConfig != null
            ? NetworkManager.NetworkConfig.NetworkTransport
            : null;
        if (transport == null)
            return 0.0;

        return System.Math.Max(0.0, transport.GetCurrentRtt(senderClientId) / 1000.0);
    }

    private static long CurrentSharedSimulationTick()
    {
        NetworkClock clock = NetworkClock.Instance;
        return PlayerSimulationTick.FromMainGameElapsed(
            clock != null ? clock.MainGameElapsed : 0.0,
            Time.fixedDeltaTime);
    }

    private void ResetReconciliationObservation()
    {
        ownerRawInputHistory?.Clear();
        ownerSimulationStateHistory?.Clear();
        serverRawInputQueue.Clear();
        lastServerRawInput = default;
        hasLastServerRawInput = false;
        repeatedServerInputTicks = 0;
        droppedServerInputCount = 0;
        motor?.ResetServerObservation();
        reconSampleCount = 0;
        reconDiscardedSampleCount = 0;
        reconTickMismatchSampleCount = 0;
        reconDivergenceSum = 0.0;
        reconMaxDivergence = 0f;
        reconWindowStartedAt = 0.0;
        reconWindowFirstTick = 0L;
        reconWindowLastTick = 0L;
        reconWindowLastServerTick = 0L;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void BeginMovementDiagnostics()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        movementHeartbeatDeadline = now + MovementHeartbeatTimeoutSeconds;
        movementRpcWindowEndsAt = now + MovementRpcLogIntervalSeconds;
        movementRpcSentCount = 0;
        movementRpcReceivedCount = 0;
        movementHeartbeatWarningLogged = false;
        motor?.BeginMovementDiagnostics();
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void UpdateMovementDiagnostics()
    {
        if (IsSimulating &&
            !movementHeartbeatWarningLogged &&
            motor != null &&
            motor.MovementDiagnosticTickCount == 0 &&
            Time.realtimeSinceStartupAsDouble >= movementHeartbeatDeadline)
        {
            movementHeartbeatWarningLogged = true;
            Edit.LogWarning(
                $"[MoveDiag] Motor has not Tick'ed within {MovementHeartbeatTimeoutSeconds:F1}s: " +
                $"{MovementDiagnosticIdentity()}, enabled={motor.enabled}, mode={motor.Mode}",
                this);
        }

        if (!IsSpawned || (!IsOwner && !IsServer))
            return;

        double now = Time.realtimeSinceStartupAsDouble;
        if (now < movementRpcWindowEndsAt)
            return;

        NetworkClock clock = NetworkClock.Instance;
        string sent = IsOwner ? movementRpcSentCount.ToString() : "n/a";
        string received = IsServer ? movementRpcReceivedCount.ToString() : "n/a";
        Edit.Log(
            $"[MoveDiag] RPC 1s summary: {MovementDiagnosticIdentity()}, sent={sent}, " +
            $"received={received}, queued={serverRawInputQueue.Count}, droppedInputsTotal={droppedServerInputCount}, " +
            $"motorTicks={motor?.MovementDiagnosticTickCount ?? 0}, " +
            $"clock={(clock != null ? "present" : "missing")}/running={clock != null && clock.IsRunning}" +
            $"/mainStarted={clock != null && clock.HasMainGameStarted}",
            this);

        movementRpcSentCount = 0;
        movementRpcReceivedCount = 0;
        movementRpcWindowEndsAt = now + MovementRpcLogIntervalSeconds;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void RecordMovementRpcSent()
    {
        movementRpcSentCount++;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void RecordMovementRpcReceived()
    {
        movementRpcReceivedCount++;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogMovementAuthorityState(string reason)
    {
        NetworkClock clock = NetworkClock.Instance;
        PlayerInput unityPlayerInput = GetComponent<PlayerInput>();
        Edit.Log(
            $"[MoveDiag] authority ({reason}): {MovementDiagnosticIdentity()}, IsOwner={IsOwner}, " +
            $"IsServer={IsServer}, IsInputSource={IsInputSource}, IsSimulating={IsSimulating}, " +
            $"IsMotionAuthority={IsMotionAuthority}, IsRemoteProxy={IsRemoteProxy}, " +
            $"motor.enabled={motor != null && motor.enabled}, Motor.Mode={(motor != null ? motor.Mode.ToString() : "missing")}, " +
            $"NetworkTransform.enabled={(networkTransform != null ? networkTransform.enabled.ToString() : "missing")}, " +
            $"NetworkClock={(clock != null ? "present" : "missing")}/running={clock != null && clock.IsRunning}" +
            $"/mainStarted={clock != null && clock.HasMainGameStarted}, " +
            $"PlayerInput.enabled={unityPlayerInput != null && unityPlayerInput.enabled}, " +
            $"EffectiveInputEnabled={inputReader != null && inputReader.DiagnosticEffectiveInputEnabled}",
            this);
    }

    private string MovementDiagnosticIdentity()
    {
        ulong localClientId = NetworkManager != null
            ? NetworkManager.LocalClientId
            : ulong.MaxValue;
        return $"ownerClientId={OwnerClientId}, localClientId={localClientId}";
    }

    private static bool IsFinite(Vector2 value)
    {
        return IsFinite(value.x) && IsFinite(value.y);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public void NotifyKnockbackEnded()
    {
        if (!IsNetworkActive || IsServer)
            return;

        NotifyKnockbackEndedServerRpc();
    }

    [ServerRpc] // RequireOwnership 기본값 true — 오너만 호출 가능
    private void NotifyKnockbackEndedServerRpc()
    {
        stateController.EndKnockback();
    }

    public void SetAnimatorMoving(bool isMoving)
    {
        if (animator != null)
            animator.SetBool(IsMovingHash, isMoving);
    }

    public override void TakeDamage(AttackInfo attackInfo)
    {
        base.TakeDamage(attackInfo);
    }

    // 피격당하면(데미지량 무관) 패시브(불굴의 의지) 쿨다운을 감소시킨다. 서버 권위에서만 유효.
    public override bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)
    {
        bool result = base.ReceiveAttack(attackInfo, hitContext);
        passive?.NotifyOwnerHit();
        return result;
    }

    // 추락 피해는 방어력·쉴드·일반 무적을 무시한다(서버 전용 Bypass Context). (PLAN §11, §13 / W10)
    private bool _fallDamageBypass;

    // 무적(대시 등) 동안 일반 피해를 차단한다. 단 추락 Bypass 중에는 통과시킨다. (PLAN §11 / W5·W10)
    protected override bool CanApplyHealthDamage(int damage)
    {
        if (_fallDamageBypass)
            return true;

        if (invulnerability != null && invulnerability.IsServerInvulnerable)
            return false;

        return base.CanApplyHealthDamage(damage);
    }

    /// <summary>
    /// 서버 전용. 추락 피해를 적용한다: BreakShield → ceil(FinalMaxHp * ratio) 직접 피해(무적 우회).
    /// 공격 Passive/Hit 반응을 발생시키지 않는다. (PLAN §13)
    /// </summary>
    public void ApplyFallDamage(float ratio)
    {
        if (!IsServer)
            return;

        _fallDamageBypass = true;
        try
        {
            BreakShield();
            ApplyDirectHealthDamage(Mathf.CeilToInt(FinalMaxHp * Mathf.Max(0f, ratio)));
        }
        finally
        {
            _fallDamageBypass = false;
        }
    }

    private ClientRpcParams CreateOwnerClientRpcParams()
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        };
    }
}
