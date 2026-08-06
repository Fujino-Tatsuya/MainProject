using BaseNetCode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerInput))]
[RequireComponent(typeof(PlayerMovement))]
public class PlayerInputReader : BaseNetworkBehaviour
{
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

    public Vector2 Direction { get; private set; }
    public bool HasMoveInput => Direction.sqrMagnitude > 0.01f;
    public bool AttackPressed => CanReadCombatInput && attackAction != null && attackAction.WasPressedThisFrame();
    public bool AttackHeld => CanReadCombatInput && attackAction != null && attackAction.IsPressed();
    public bool InterruptPressed => CanReadCombatInput && interruptAction != null && interruptAction.WasPressedThisFrame();

    // 대시 입력은 입력 에셋의 "Dash" 액션에서 읽는다(바인딩 = Space · Gamepad South · XR).
    // 키는 에셋에서만 바꾼다 — 예전처럼 코드에 키를 박으면 리바인딩이 불가능해진다.
    // ⚠️ combatInputEnabled 게이트를 타지 않는 것은 의도다 — Soul 차단은 서버
    //    DashValidationPolicy(Dead||Soul)와 TryBeginPredictedDash의 CanMove가 담당한다.
    public bool DashPressed =>
        EffectiveInputEnabled && dashAction != null && dashAction.WasPressedThisFrame();

    private bool CanUseLocalControl =>
        !IsNetworkActive || IsOwner;
    private bool CanReadCombatInput =>
        EffectiveInputEnabled && combatInputEnabled;
    private bool EffectiveInputEnabled =>
        inputEnabled && !uiInputSuppressed;

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

        // 액션이 없으면 대시 입력이 조용히 사라진다(예전 Shift 직접 판정과 달리 폴백이 없다).
        if (dashAction == null)
            Debug.LogWarning("[DashAlert] 입력 에셋에 \"Dash\" 액션이 없어 대시 입력을 읽을 수 없습니다.", this);
    }

    public bool GetSkillPressed(PlayerSkillSlot slot)
    {
        if (!CanReadCombatInput)
            return false;

        InputAction action = GetSkillAction(slot);
        return action != null && action.WasPressedThisFrame();
    }

    public bool GetSkillHeld(PlayerSkillSlot slot)
    {
        if (!CanReadCombatInput)
            return false;

        InputAction action = GetSkillAction(slot);
        return action != null && action.IsPressed();
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
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        RefreshControlState();
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
            Direction = Vector2.zero;
    }

    private void Update()
    {
        if (!EffectiveInputEnabled)
        {
            Direction = Vector2.zero;
            return;
        }

        Direction = moveAction.ReadValue<Vector2>();
    }

    private void OnDisable()
    {
        Direction = Vector2.zero;
    }
}
