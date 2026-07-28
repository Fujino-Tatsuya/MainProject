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
    private bool inputEnabled = true;
    private bool combatInputEnabled = true;
    private bool controlEnabled = true;

    public Vector2 Direction { get; private set; }
    public bool HasMoveInput => Direction.sqrMagnitude > 0.01f;
    public bool AttackPressed => CanReadCombatInput && attackAction != null && attackAction.WasPressedThisFrame();
    public bool AttackHeld => CanReadCombatInput && attackAction != null && attackAction.IsPressed();
    public bool InterruptPressed => CanReadCombatInput && interruptAction != null && interruptAction.WasPressedThisFrame();

    // v1 대시 입력은 키보드 Shift 직접 판정(입력 에셋에 Dash 액션이 없어도 동작). (PLAN §7)
    public bool DashPressed =>
        inputEnabled &&
        Keyboard.current != null &&
        Keyboard.current.leftShiftKey.wasPressedThisFrame;

    private bool CanUseLocalControl =>
        !IsNetworkActive || IsOwner;
    private bool CanReadCombatInput =>
        inputEnabled && combatInputEnabled;

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

        if (playerInput != null)
            playerInput.enabled = isEnabled;

        if (!inputEnabled)
            Direction = Vector2.zero;
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

    private void Update()
    {
        if (!inputEnabled)
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
