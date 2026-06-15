using UnityEngine;

[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerAimIndicator))]
public class Player : Unit
{
    private static readonly int DefaultAttackHash = Animator.StringToHash("DefaultAttack");
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

    [SerializeField] private Animator animator;
    [SerializeField] private float defaultAttackDuration = 0.5f;

    private PlayerInputReader inputReader;
    private PlayerMovement movement;
    private PlayerAimIndicator aimIndicator;
    private float defaultAttackEndTime;

    public PlayerState CurrentState { get; private set; } = PlayerState.Idle;
    public bool CanMove => CurrentState == PlayerState.Idle || CurrentState == PlayerState.Move;
    public bool CanMovementRotate => CurrentState == PlayerState.Idle || CurrentState == PlayerState.Move;

    private void Awake()
    {
        inputReader = GetComponent<PlayerInputReader>();
        movement = GetComponent<PlayerMovement>();
        aimIndicator = GetComponent<PlayerAimIndicator>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    private void Update()
    {
        if (CurrentState == PlayerState.Attack)
        {
            SetAnimatorMoving(false);

            if (Time.time >= defaultAttackEndTime)
                EndDefaultAttack();

            return;
        }

        UpdateLocomotionState();

        if (inputReader.AttackPressed)
            StartDefaultAttack();
    }

    private void UpdateLocomotionState()
    {
        bool isMoving = inputReader.HasMoveInput;

        CurrentState = isMoving ? PlayerState.Move : PlayerState.Idle;
        SetAnimatorMoving(isMoving);
    }

    private void StartDefaultAttack()
    {
        Vector3 aimDirection = aimIndicator.AimDirection;

        if (aimDirection.sqrMagnitude < 0.001f)
            return;

        movement.RotateImmediately(aimDirection);
        CurrentState = PlayerState.Attack;
        defaultAttackEndTime = Time.time + defaultAttackDuration;
        SetAnimatorMoving(false);

        if (animator != null)
            animator.SetTrigger(DefaultAttackHash);
    }

    public void EndDefaultAttack()
    {
        if (CurrentState != PlayerState.Attack)
            return;

        CurrentState = PlayerState.Idle;
        SetAnimatorMoving(false);
    }

    private void SetAnimatorMoving(bool isMoving)
    {
        if (animator != null)
            animator.SetBool(IsMovingHash, isMoving);
    }
}

public enum PlayerState
{
    Idle,
    Move,
    Attack,
    Skill,
    Stun,
    Dead
}
