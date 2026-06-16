using UnityEngine;

[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerAimIndicator))]
[RequireComponent(typeof(DefaultAttack))]
public class Player : Unit
{
    private static readonly int InterruptHash = Animator.StringToHash("Interrupt");
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

    [SerializeField] private Animator animator;
    [SerializeField] private float interruptDuration = 0.5f;
    [SerializeField] private float interruptForwardDistance = 0.5f;

    private PlayerInputReader inputReader;
    private PlayerMovement movement;
    private PlayerAimIndicator aimIndicator;
    private DefaultAttack defaultAttack;
    private Vector3 actionDirection;
    private float actionEndTime;
    private float actionMoveRemaining;
    private float actionMoveSpeed;

    public PlayerState CurrentState { get; private set; } = PlayerState.Idle;
    public bool CanMove => CurrentState == PlayerState.Idle || CurrentState == PlayerState.Move;
    public bool CanMovementRotate => CurrentState == PlayerState.Idle || CurrentState == PlayerState.Move;

    private void Awake()
    {
        inputReader = GetComponent<PlayerInputReader>();
        movement = GetComponent<PlayerMovement>();
        aimIndicator = GetComponent<PlayerAimIndicator>();
        defaultAttack = GetComponent<DefaultAttack>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    private void Update()
    {
        if (CurrentState == PlayerState.Attack)
        {
            SetAnimatorMoving(false);
            defaultAttack.Tick();
            return;
        }

        if (CurrentState == PlayerState.Interrupt)
        {
            SetAnimatorMoving(false);
            MoveDuringAction();

            if (Time.time >= actionEndTime)
                EndCurrentAction();

            return;
        }

        UpdateLocomotionState();

        if (inputReader.AttackPressed || inputReader.AttackHeld)
            defaultAttack.TryStart();
        else if (inputReader.InterruptPressed)
            StartInterrupt();
    }

    private void UpdateLocomotionState()
    {
        bool isMoving = inputReader.HasMoveInput;

        CurrentState = isMoving ? PlayerState.Move : PlayerState.Idle;
        SetAnimatorMoving(isMoving);
    }

    private void StartInterrupt()
    {
        StartAction(
            PlayerState.Interrupt,
            InterruptHash,
            interruptDuration,
            interruptForwardDistance
        );
    }

    private void StartAction(PlayerState state, int triggerHash, float duration, float forwardDistance)
    {
        Vector3 aimDirection = aimIndicator.AimDirection;

        if (aimDirection.sqrMagnitude < 0.001f || duration <= 0f)
            return;

        actionDirection = aimDirection.normalized;
        actionEndTime = Time.time + duration;
        actionMoveRemaining = Mathf.Max(forwardDistance, 0f);
        actionMoveSpeed = actionMoveRemaining / duration;

        movement.RotateImmediately(actionDirection);
        CurrentState = state;
        SetAnimatorMoving(false);

        if (animator != null)
            animator.SetTrigger(triggerHash);
    }

    private void MoveDuringAction()
    {
        if (actionMoveRemaining <= 0f)
            return;

        float moveDistance = Mathf.Min(actionMoveSpeed * Time.deltaTime, actionMoveRemaining);
        actionMoveRemaining -= moveDistance;
        movement.MoveRoot(actionDirection * moveDistance);
    }

    public void EndDefaultAttack()
    {
        defaultAttack.EndCurrentAttack();
    }

    public void EndInterrupt()
    {
        if (CurrentState != PlayerState.Interrupt)
            return;

        EndCurrentAction();
    }

    private void EndCurrentAction()
    {
        actionMoveRemaining = 0f;
        CurrentState = PlayerState.Idle;
        SetAnimatorMoving(false);
    }

    public void SetState(PlayerState state)
    {
        CurrentState = state;
    }

    public void SetAnimatorMoving(bool isMoving)
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
    Interrupt,
    Skill,
    Stun,
    Dead
}
