using System.Runtime.CompilerServices;
using BaseNetCode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerInput))]
[RequireComponent(typeof(PlayerMovement))]
[DataTableSheet("Player", Order = 8)]
public class PlayerInputReader : BaseNetworkBehaviour
{
    private const double InputDiagnosticLogIntervalSeconds = 1.0;

    [Tooltip("선입력 버퍼 유지 시간(초). 행동 중이라 바로 실행 못 한 평타·대시·스킬 입력을 이만큼 들고 있다가 실행 가능해진 첫 순간에 실행한다. 0 = 버퍼 끔")]
    [SerializeField, Min(0f)] private float inputBufferSeconds = 0.2f;

    private PlayerInput playerInput;
    private PlayerMovement movement;
    private InputAction moveAction;
    private InputAction attackAction;
    private InputAction interruptAction;
    private InputAction skillMainAction;
    private InputAction skillSubAction;
    private InputAction skillUltimateAction;
    private InputAction dashAction;
    private bool inputEnabled = true;
    private bool uiInputSuppressed;
    private bool combatInputEnabled = true;
    private bool controlEnabled = true;
    private bool dashPressedLatched;
    private bool dashCallbackRegistered;
    private readonly bool[] virtualSkillHeld = new bool[4];
    private readonly bool[] virtualSkillPressed = new bool[4];
    private readonly bool[] virtualSkillPressedObserved = new bool[4];
    private readonly int[] virtualSkillPressedFrame = { -1, -1, -1, -1 };

    // 선입력 버퍼(할 일 44) — 오너 로컬 입력만 기록한다(비오너는 EffectiveInputEnabled 가 꺼져 있다).
    // 유지 시간은 기록할 때마다 inputBufferSeconds 로 맞춘다(데이터 테이블·인스펙터 값이 Awake 뒤에 바뀌어도 따른다).
    private readonly PlayerInputBuffer inputBuffer = new PlayerInputBuffer(0f);
    private int inputBufferCapturedFrame = -1;

    // [MoveDiag] 입력/게이트 엣지를 최대 초당 한 줄로 묶기 위한 관측 전용 상태.
    private bool inputDiagnosticInitialized;
    private Vector2 inputDiagnosticObservedDirection;
    private bool inputDiagnosticObservedHasMove;
    private bool inputDiagnosticObservedEffective;
    private bool inputDiagnosticObservedPlayerInputEnabled;
    private int inputDiagnosticPendingEdges;
    private bool inputDiagnosticPendingSawMove;
    private Vector2 inputDiagnosticLastEdgeDirection;
    private string inputDiagnosticLastReason;
    private double inputDiagnosticNextLogAt;

    public Vector2 Direction { get; private set; }
    public bool HasMoveInput => Direction.sqrMagnitude > 0.01f;
    public bool AttackPressed => CanReadCombatInput && !SkillSlotHover.BlocksPrimaryInput &&
                                 attackAction != null && attackAction.WasPressedThisFrame();
    public bool AttackHeld => CanReadCombatInput && !SkillSlotHover.BlocksPrimaryInput &&
                              attackAction != null && attackAction.IsPressed();
    public bool InterruptPressed => CanReadCombatInput && !SkillSlotHover.BlocksPrimaryInput &&
                                    interruptAction != null && interruptAction.WasPressedThisFrame();

    // 대시 입력은 입력 에셋의 "Dash" 액션에서 읽는다(바인딩 = Space · Gamepad South · XR).
    // 키는 에셋에서만 바꾼다 — 예전처럼 코드에 키를 박으면 리바인딩이 불가능해진다.
    // ⚠️ combatInputEnabled 게이트를 타지 않는 것은 의도다 — Soul 차단은 서버
    //    DashValidationPolicy(Dead||Soul)와 TryBeginPredictedDash의 CanMove가 담당한다.
    public bool DashPressed => EffectiveInputEnabled && dashPressedLatched;

    private bool CanUseLocalControl =>
        !IsNetworkActive || IsOwner;
    private bool CanReadCombatInput =>
        EffectiveInputEnabled && combatInputEnabled;
    private bool EffectiveInputEnabled =>
        inputEnabled && !uiInputSuppressed;
    internal bool DiagnosticEffectiveInputEnabled => EffectiveInputEnabled;

    public float InputBufferSeconds => inputBufferSeconds;

    private void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        movement = GetComponent<PlayerMovement>();

        moveAction = playerInput.actions["Move"];
        attackAction = playerInput.actions["Attack"];
        interruptAction = playerInput.actions["Interrupt"];

        // 스킬 액션은 에셋에 없을 수 있어 FindAction(null 허용)으로 조회한다 (인덱서는 예외 발생)
        skillMainAction = playerInput.actions.FindAction("SkillMain");
        skillSubAction = playerInput.actions.FindAction("SkillSub");
        skillUltimateAction = playerInput.actions.FindAction("SkillUltimate");
        dashAction = playerInput.actions.FindAction("Dash");
        RegisterDashCallback();

        // 액션이 없으면 대시 입력이 조용히 사라진다(예전 Shift 직접 판정과 달리 폴백이 없다).
        if (dashAction == null)
            Debug.LogWarning("[DashAlert] 입력 에셋에 \"Dash\" 액션이 없어 대시 입력을 읽을 수 없습니다.", this);
    }

    /// <summary>PlayerStateController가 상태 판단을 마친 뒤 그 틱의 엣지 입력을 소비한다.</summary>
    public void ConsumeStateTickEdges()
    {
        CaptureBufferedPresses(); // 래치를 내리기 전에 이번 프레임 대시를 버퍼에 남긴다
        dashPressedLatched = false;
    }

    // 버퍼 기록 + 진단 press. 기록 규칙은 inputBuffer.Record 그대로다.
    private void RecordBufferedPress(PlayerBufferedInput input, float now)
    {
        inputBuffer.Record(input, now);
        DiagnoseBufferPress(input, now);
    }

    /// <summary>
    /// 선입력 버퍼에 그 종류가 살아 있는가. 각 행동의 '이번 프레임 눌림'과 OR 해서 쓴다 — 버퍼가 꺼져 있어도
    /// 눌린 프레임 입력은 그대로 읽힌다.
    /// </summary>
    public bool HasBufferedInput(PlayerBufferedInput input)
    {
        CaptureBufferedPresses();
        return inputBuffer.Has(input, Time.time);
    }

    /// <summary>행동 중에 원시 입력을 받아들인 소비처가 부른다 — 그 입력이 행동이 끝난 뒤 한 번 더 실행되지 않게 지운다.</summary>
    // 호출자 정보는 진단 로그(consumed 사유)에만 쓴다.
    public void ConsumeBufferedInput(
        PlayerBufferedInput input,
        [CallerMemberName] string callerMember = null,
        [CallerFilePath] string callerFile = null)
    {
        CaptureBufferedPresses();
        DiagnoseBufferConsumed(input, callerMember, callerFile);
        inputBuffer.Consume(input);
    }

    public void ConsumeBufferedSkillInput(
        PlayerSkillSlot slot,
        [CallerMemberName] string callerMember = null,
        [CallerFilePath] string callerFile = null) =>
        ConsumeBufferedInput(PlayerInputBuffer.FromSkillSlot(slot), callerMember, callerFile);

    /// <summary>Idle/Move 에서 그 입력으로 시작을 시도한 결과를 버퍼에 반영한다 — 시작 = 비움, 막힘(바쁨·CC) = 유지, 그 밖의 거부 = 그 입력만 버림.</summary>
    public void ResolveBufferedInput(PlayerBufferedInput input, PlayerActionAttempt attempt)
    {
        CaptureBufferedPresses();
        DiagnoseBufferResolve(input, attempt);
        inputBuffer.Resolve(input, attempt);
    }

    /// <summary>입력으로 새 행동을 시작했을 때 버퍼를 비운다(진단: 들고 있던 입력 = 실행됨).</summary>
    public void ClearInputBufferOnActionStarted()
    {
        CaptureBufferedPresses();
        DiagnoseBufferConsumed(PlayerBufferedInput.None, "행동 시작", null);
        inputBuffer.Clear();
    }

    /// <summary>입력을 버려야 할 때(조준 억제·사망·연출 잠금) 버퍼를 비운다. 호출자 정보는 진단 로그(dropped 사유)에만 쓴다.</summary>
    public void ClearInputBuffer(
        [CallerMemberName] string callerMember = null,
        [CallerFilePath] string callerFile = null)
    {
        CaptureBufferedPresses(); // 이번 프레임 눌림이 비운 뒤에 뒤늦게 기록되지 않게 먼저 받는다
        DiagnoseBufferDropped(PlayerBufferedInput.None, callerMember, callerFile);
        inputBuffer.Clear();
    }

    // 이번 프레임에 새로 눌린 입력을 버퍼에 남긴다. 프레임당 한 번 — 소비처·상태 머신·Update 중 먼저 부른 쪽이 받는다.
    // 그래서 누가 먼저 돌든 '받아서 지움'이 '뒤늦게 기록'에 덮이지 않는다.
    // 같은 프레임에 여럿이면 마지막 기록이 남으므로 우선순위 낮은 것부터 적는다(대시 > 공격 > 스킬 > 인터럽트).
    // HUD 가상 스킬 press 는 SetVirtualSkillInput 에서 바로 기록한다.
    private void CaptureBufferedPresses()
    {
        if (inputBufferCapturedFrame == Time.frameCount)
            return;

        inputBufferCapturedFrame = Time.frameCount;
        inputBuffer.HoldSeconds = inputBufferSeconds;
        if (inputBufferSeconds <= 0f || !EffectiveInputEnabled)
            return;

        float now = Time.time;
        DiagnoseBufferFrame(now);
        if (InterruptPressed)
            RecordBufferedPress(PlayerBufferedInput.SkillInterrupt, now);
        if (WasSkillActionPressed(PlayerSkillSlot.Ultimate))
            RecordBufferedPress(PlayerBufferedInput.SkillUltimate, now);
        if (WasSkillActionPressed(PlayerSkillSlot.Sub))
            RecordBufferedPress(PlayerBufferedInput.SkillSub, now);
        if (WasSkillActionPressed(PlayerSkillSlot.Main))
            RecordBufferedPress(PlayerBufferedInput.SkillMain, now);
        if (AttackPressed)
            RecordBufferedPress(PlayerBufferedInput.Attack, now);
        if (DashPressed)
            RecordBufferedPress(PlayerBufferedInput.Dash, now);
    }

    // 키보드 스킬 액션만 본다 — GetSkillPressed 는 가상 press 를 '관측됨'으로 표시하므로 기록용으로 쓰지 않는다.
    private bool WasSkillActionPressed(PlayerSkillSlot slot)
    {
        if (!CanReadCombatInput)
            return false;

        InputAction action = GetSkillAction(slot);
        return action != null && action.WasPressedThisFrame();
    }

    private void RegisterDashCallback()
    {
        if (dashAction == null || dashCallbackRegistered)
            return;

        dashAction.performed += OnDashPerformed;
        dashCallbackRegistered = true;
    }

    private void UnregisterDashCallback()
    {
        if (dashAction == null || !dashCallbackRegistered)
            return;

        dashAction.performed -= OnDashPerformed;
        dashCallbackRegistered = false;
    }

    private void OnDashPerformed(InputAction.CallbackContext context)
    {
        if (EffectiveInputEnabled)
            dashPressedLatched = true;
    }

    private void OnEnable()
    {
        RegisterDashCallback();

        // 설정창 키 리바인드(PLAN-settings S7). PlayerInput 이 전역 에셋의 복제본을 쓸 수 있어 여기서 다시 건다.
        ApplyKeyBindings();
        KeyBindingConfig.Changed += ApplyKeyBindings;
    }

    private void ApplyKeyBindings()
    {
        if (playerInput != null && playerInput.actions != InputSystem.actions)
            KeyBindingConfig.ApplyTo(playerInput.actions);
    }

    public bool GetSkillPressed(PlayerSkillSlot slot)
    {
        if (!CanReadCombatInput)
            return false;

        int index = (int)slot;
        if (index >= 0 && index < virtualSkillPressed.Length && virtualSkillPressed[index])
        {
            virtualSkillPressedObserved[index] = true;
            return true;
        }

        InputAction action = GetSkillAction(slot);
        return action != null && action.WasPressedThisFrame();
    }

    public bool GetSkillHeld(PlayerSkillSlot slot)
    {
        if (!CanReadCombatInput)
            return false;

        int index = (int)slot;
        bool virtualHeld = index >= 0 && index < virtualSkillHeld.Length && virtualSkillHeld[index];
        InputAction action = GetSkillAction(slot);
        return virtualHeld || (action != null && action.IsPressed());
    }

    /// <summary>
    /// HUD 칸 좌클릭을 실제 스킬 액션과 같은 조회 경로에 합성한다.
    /// Press는 게임 로직이 관측할 때까지(늦어도 다음 프레임) 유지하고, Hold는 포인터를 놓을 때까지 유지한다.
    /// </summary>
    public void SetVirtualSkillInput(PlayerSkillSlot slot, bool held)
    {
        int index = (int)slot;
        if (index < 0 || index >= virtualSkillHeld.Length || !CanUseLocalControl)
            return;

        if (held && !virtualSkillHeld[index])
        {
            virtualSkillPressed[index] = true;
            virtualSkillPressedObserved[index] = false;
            virtualSkillPressedFrame[index] = Time.frameCount;

            if (CanReadCombatInput)
            {
                CaptureBufferedPresses();
                RecordBufferedPress(PlayerInputBuffer.FromSkillSlot(slot), Time.time);
            }
        }

        virtualSkillHeld[index] = held;
    }

    private InputAction GetSkillAction(PlayerSkillSlot slot)
    {
        return slot switch
        {
            PlayerSkillSlot.Main => skillMainAction,
            PlayerSkillSlot.Sub => skillSubAction,
            PlayerSkillSlot.Interrupt => interruptAction, // 우클릭 — 기존 Interrupt 액션 재사용
            PlayerSkillSlot.Ultimate => skillUltimateAction,
            _ => null
        };
    }

    private void Start()
    {
        RefreshControlState();
        ObserveInputDiagnosticEdges("start");
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        RefreshControlState();
        ObserveInputDiagnosticEdges("network-spawn");
    }

    public override void OnNetworkDespawn()
    {
        SetLocalControl(false);
        base.OnNetworkDespawn();
    }

    public void SetInputEnabled(bool isEnabled)
    {
        inputEnabled = isEnabled;
        ApplyInputState();
    }

    public void SetUiInputSuppressed(bool suppressed)
    {
        uiInputSuppressed = suppressed;
        ApplyInputState();
    }

    /// <summary>
    /// 이동 입력은 유지하면서 공격/인터럽트/스킬 입력만 허용하거나 차단한다.
    /// Soul 생명주기 정책이 로컬 오너 입력에만 적용한다.
    /// </summary>
    public void SetCombatInputEnabled(bool isEnabled)
    {
        combatInputEnabled = isEnabled;
        if (!isEnabled)
        {
            DiagnoseBufferDropped(PlayerBufferedInput.None, "전투 입력 차단", null);
            inputBuffer.Clear();
        }
    }

    private void RefreshControlState()
    {
        SetLocalControl(CanUseLocalControl);
    }

    private void SetLocalControl(bool isEnabled)
    {
        if (controlEnabled == isEnabled)
            return;

        controlEnabled = isEnabled;
        SetInputEnabled(isEnabled);

        if (movement != null)
            movement.enabled = isEnabled;
    }

    private void ApplyInputState()
    {
        if (playerInput != null)
            playerInput.enabled = EffectiveInputEnabled;

        if (!EffectiveInputEnabled)
        {
            Direction = Vector2.zero;
            dashPressedLatched = false;
            DiagnoseBufferDropped(PlayerBufferedInput.None, "입력 차단", null);
            inputBuffer.Clear();
        }

        ObserveInputDiagnosticEdges("gate-change");
    }

    private void Update()
    {
        if (!EffectiveInputEnabled)
        {
            Direction = Vector2.zero;
            ObserveInputDiagnosticEdges("update-gated");
            return;
        }

        // 행동 중이라 이번 프레임에 아무도 입력을 읽지 않아도 눌린 순간은 버퍼에 남는다.
        CaptureBufferedPresses();
        Direction = moveAction.ReadValue<Vector2>();
        ObserveInputDiagnosticEdges("reader-update");
    }

    private void LateUpdate()
    {
        for (int i = 0; i < virtualSkillPressed.Length; i++)
        {
            if (!virtualSkillPressed[i])
                continue;

            // EventSystem이 플레이어 Update 뒤에 클릭을 보낸 경우 다음 프레임까지 한 번 보존한다.
            if (virtualSkillPressedObserved[i] || Time.frameCount > virtualSkillPressedFrame[i])
            {
                virtualSkillPressed[i] = false;
                virtualSkillPressedObserved[i] = false;
                virtualSkillPressedFrame[i] = -1;
            }
        }
    }

    private void OnDisable()
    {
        KeyBindingConfig.Changed -= ApplyKeyBindings;
        Direction = Vector2.zero;
        dashPressedLatched = false;
        DiagnoseBufferDropped(PlayerBufferedInput.None, "컴포넌트 비활성", null);
        inputBuffer.Clear();
        for (int i = 0; i < virtualSkillHeld.Length; i++)
        {
            virtualSkillHeld[i] = false;
            virtualSkillPressed[i] = false;
            virtualSkillPressedObserved[i] = false;
            virtualSkillPressedFrame[i] = -1;
        }
        UnregisterDashCallback();
        ObserveInputDiagnosticEdges("component-disable");
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void ObserveInputDiagnosticEdges(string reason)
    {
        bool hasMove = HasMoveInput;
        bool effective = EffectiveInputEnabled;
        bool playerInputEnabled = playerInput != null && playerInput.enabled;
        double now = Time.realtimeSinceStartupAsDouble;

        if (!inputDiagnosticInitialized)
        {
            inputDiagnosticInitialized = true;
            inputDiagnosticObservedDirection = Direction;
            inputDiagnosticObservedHasMove = hasMove;
            inputDiagnosticObservedEffective = effective;
            inputDiagnosticObservedPlayerInputEnabled = playerInputEnabled;
            inputDiagnosticNextLogAt = now + InputDiagnosticLogIntervalSeconds;
            LogInputDiagnosticEdges($"initial/{reason}", 1, hasMove, Direction);
            return;
        }

        bool changed =
            (Direction - inputDiagnosticObservedDirection).sqrMagnitude > 0.0001f ||
            hasMove != inputDiagnosticObservedHasMove ||
            effective != inputDiagnosticObservedEffective ||
            playerInputEnabled != inputDiagnosticObservedPlayerInputEnabled;
        if (changed)
        {
            inputDiagnosticPendingEdges++;
            inputDiagnosticPendingSawMove |= hasMove;
            inputDiagnosticLastEdgeDirection = Direction;
            inputDiagnosticLastReason = reason;
            inputDiagnosticObservedDirection = Direction;
            inputDiagnosticObservedHasMove = hasMove;
            inputDiagnosticObservedEffective = effective;
            inputDiagnosticObservedPlayerInputEnabled = playerInputEnabled;
        }

        if (inputDiagnosticPendingEdges == 0 || now < inputDiagnosticNextLogAt)
            return;

        LogInputDiagnosticEdges(
            inputDiagnosticLastReason,
            inputDiagnosticPendingEdges,
            inputDiagnosticPendingSawMove,
            inputDiagnosticLastEdgeDirection);
        inputDiagnosticPendingEdges = 0;
        inputDiagnosticPendingSawMove = false;
        inputDiagnosticNextLogAt = now + InputDiagnosticLogIntervalSeconds;
    }

    private void LogInputDiagnosticEdges(
        string reason,
        int edgeCount,
        bool sawMove,
        Vector2 lastEdgeDirection)
    {
        ulong localClientId = NetworkManager != null
            ? NetworkManager.LocalClientId
            : ulong.MaxValue;
        Edit.Log(
            $"[MoveDiag] input edge ({reason}): ownerClientId={OwnerClientId}, " +
            $"localClientId={localClientId}, edges={edgeCount}, Direction={Direction}, " +
            $"HasMoveInput={HasMoveInput}, sawMove={sawMove}, lastEdgeDirection={lastEdgeDirection}, " +
            $"EffectiveInputEnabled={EffectiveInputEnabled}, inputEnabled={inputEnabled}, " +
            $"uiSuppressed={uiInputSuppressed}, controlEnabled={controlEnabled}, " +
            $"reader.enabled={enabled}, PlayerInput.enabled={playerInput != null && playerInput.enabled}, " +
            $"MoveAction.enabled={moveAction != null && moveAction.enabled}",
            this);
    }

    // ── 선입력 버퍼 진단(할 일 44 후속) ── 오너 로컬만, 관측 전용. Docs/tech/skill-reject-feedback.md
    // 훅은 [Conditional] 이라 심볼이 없으면 호출(인자 계산 포함)째 사라지고, 토글이 꺼져 있으면 추적기를 만들지 않는다.

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD"), System.Diagnostics.Conditional("DEV_HOTKEYS")]
    private void DiagnoseBufferFrame(float now)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
        GetInputBufferDiag()?.BeginFrame(now, Time.frameCount);
#endif
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD"), System.Diagnostics.Conditional("DEV_HOTKEYS")]
    private void DiagnoseBufferPress(PlayerBufferedInput input, float now)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
        GetInputBufferDiag()?.Press(input, now, Time.frameCount, inputBuffer.HoldSeconds);
#endif
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD"), System.Diagnostics.Conditional("DEV_HOTKEYS")]
    private void DiagnoseBufferConsumed(PlayerBufferedInput input, string callerMember, string callerFile)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
        PlayerInputBufferDiagTracker diag = GetInputBufferDiag();
        if (diag != null && diag.Pending != PlayerBufferedInput.None)
            diag.Consumed(input, Time.time, Time.frameCount, DescribeInputBufferCaller(callerMember, callerFile));
#endif
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD"), System.Diagnostics.Conditional("DEV_HOTKEYS")]
    private void DiagnoseBufferDropped(PlayerBufferedInput input, string callerMember, string callerFile)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
        PlayerInputBufferDiagTracker diag = GetInputBufferDiag();
        if (diag != null && diag.Pending != PlayerBufferedInput.None)
            diag.Dropped(input, Time.time, Time.frameCount, DescribeInputBufferCaller(callerMember, callerFile));
#endif
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD"), System.Diagnostics.Conditional("DEV_HOTKEYS")]
    private void DiagnoseBufferResolve(PlayerBufferedInput input, PlayerActionAttempt attempt)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
        PlayerInputBufferDiagTracker diag = GetInputBufferDiag();
        if (diag == null || diag.Pending == PlayerBufferedInput.None)
            return;

        float now = Time.time;
        int frame = Time.frameCount;
        switch (attempt)
        {
            case PlayerActionAttempt.Started:
                diag.Consumed(PlayerBufferedInput.None, now, frame, $"{input} 시작");
                break;
            case PlayerActionAttempt.Blocked:
                // 기본 공격 중의 Blocked 는 스킬 끊기를 발 잠금이 미룬 것뿐이다(거너, 할 일 44).
                diag.Blocked(input, now, frame,
                    IsInputBufferDiagIncapacitated() ? "CC"
                    : InputBufferDiagState == PlayerActionState.Attack || InputBufferDiagState == PlayerActionState.AttackReady
                        ? "바쁨(발 잠금)"
                        : "바쁨(요청 대기)");
                break;
            case PlayerActionAttempt.OutOfResource:
                diag.Dropped(input, now, frame, "자원 부족(쿨타임·충전·과열)");
                break;
            case PlayerActionAttempt.BlockedByEffect:
                diag.Dropped(input, now, frame, "차단 효과");
                break;
            default:
                diag.Dropped(input, now, frame, "기타 거부(사용 조건·빈 슬롯·비접지 등)");
                break;
        }
#endif
    }

    /// <summary>기본 공격 중 스킬 입력으로 공격을 끊고 스킬을 요청했을 때 상태기계가 부른다(cancel-for-skill, 거너).</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD"), System.Diagnostics.Conditional("DEV_HOTKEYS")]
    internal void DiagnoseCancelForSkill(PlayerBufferedInput input, string detail)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
        GetInputBufferDiag()?.CancelForSkill(input, Time.time, Time.frameCount, detail);
#endif
    }

    /// <summary>PlayerStateController 가 상태를 바꾼 뒤 부른다 — 바쁨 → Idle/Move 전환을 actionable 로 남긴다.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD"), System.Diagnostics.Conditional("DEV_HOTKEYS")]
    internal void DiagnoseActionStateChanged(PlayerActionState state)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
        PlayerInputBufferDiagTracker diag = GetInputBufferDiag();
        if (diag == null)
            return;

        bool actionable = state == PlayerActionState.Idle || state == PlayerActionState.Move;
        if (actionable != diag.IsActionable)
            diag.StateChanged(actionable, Time.time, Time.frameCount, $"→{state}");
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
    private PlayerInputBufferDiagTracker inputBufferDiag;
    private PlayerStateController inputBufferDiagState;
    private PlayerSkillController inputBufferDiagSkills;
    private IPlayerBasicAttack inputBufferDiagAttack;
    private StatusEffectController inputBufferDiagEffects;

    // 오너 로컬(버퍼를 기록하는 쪽)이고 토글이 켜져 있을 때만 추적기를 준다.
    private PlayerInputBufferDiagTracker GetInputBufferDiag()
    {
        if (!PlayerInputBufferDiagnostics.Enabled || !CanUseLocalControl)
            return null;

        if (inputBufferDiag == null)
        {
            inputBufferDiagState = GetComponent<PlayerStateController>();
            inputBufferDiagSkills = GetComponent<PlayerSkillController>();
            inputBufferDiagAttack = GetComponent<IPlayerBasicAttack>();
            inputBufferDiagEffects = GetComponent<StatusEffectController>();
            inputBufferDiag = new PlayerInputBufferDiagTracker(WriteInputBufferDiag, DescribeInputBufferWait);
        }

        return inputBufferDiag;
    }

    private PlayerActionState InputBufferDiagState =>
        inputBufferDiagState != null ? inputBufferDiagState.CurrentState : PlayerActionState.Idle;

    private void WriteInputBufferDiag(InputBufferDiagRecord record) =>
        PlayerInputBufferDiagnostics.Write(record, InputBufferDiagState, DescribeInputBufferAction(), inputBufferSeconds);

    // 현재 행동 이름 — 스킬이면 슬롯:스킬 클래스, 평타면 평타 컴포넌트, 그 밖은 "-".
    private string DescribeInputBufferAction()
    {
        switch (InputBufferDiagState)
        {
            case PlayerActionState.Skill:
            case PlayerActionState.Focus:
                PlayerSkillBase skill = inputBufferDiagSkills != null ? inputBufferDiagSkills.ActiveSkill : null;
                return skill != null ? $"{skill.Slot}:{skill.GetType().Name}" : "skill";
            case PlayerActionState.Attack:
            case PlayerActionState.AttackReady:
                return inputBufferDiagAttack != null ? inputBufferDiagAttack.GetType().Name : "attack";
            default:
                return "-";
        }
    }

    // 누른 프레임에 실행 못 한 이유(buffered 사유).
    private string DescribeInputBufferWait()
    {
        PlayerActionState state = InputBufferDiagState;
        if (state != PlayerActionState.Idle && state != PlayerActionState.Move)
            return $"행동 중({state})";
        if (IsInputBufferDiagIncapacitated())
            return "CC";
        if (inputBufferDiagSkills != null && inputBufferDiagSkills.IsBusy)
            return "스킬 요청 대기";
        return "다음 프레임 처리";
    }

    private bool IsInputBufferDiagIncapacitated() =>
        inputBufferDiagEffects != null &&
        (inputBufferDiagEffects.ActiveEffects & StatusEffectController.IncapacitatingEffects) != 0;

    private static string DescribeInputBufferCaller(string callerMember, string callerFile) =>
        string.IsNullOrEmpty(callerFile)
            ? callerMember
            : $"{callerMember}@{System.IO.Path.GetFileNameWithoutExtension(callerFile)}";
#endif
}
