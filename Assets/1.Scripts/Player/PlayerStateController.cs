using UnityEngine;

[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerAimIndicator))]
[RequireComponent(typeof(DefaultAttack))]
[RequireComponent(typeof(StatusEffectController))]
public class PlayerStateController : MonoBehaviour
{
    private IPlayerState currentState;
    private PlayerStateContext context;

    public PlayerActionState CurrentState => currentState?.StateType ?? PlayerActionState.Idle;
    public bool CanMove => (CurrentState == PlayerActionState.Idle || CurrentState == PlayerActionState.Move) && !context.StatusEffects.BlocksMovement;
    public bool CanMovementRotate => (CurrentState == PlayerActionState.Idle || CurrentState == PlayerActionState.Move) && !context.StatusEffects.BlocksMovement;
    public bool CanAttack => (CurrentState == PlayerActionState.Idle || CurrentState == PlayerActionState.Move) && !context.StatusEffects.BlocksAttack;
    public bool CanInterrupt => (CurrentState == PlayerActionState.Idle || CurrentState == PlayerActionState.Move) && !context.StatusEffects.BlocksInterrupt;
    public bool HasSuperArmor => context.StatusEffects.HasSuperArmor;

    private void Awake()
    {
        StatusEffectController statusEffects = GetComponent<StatusEffectController>();
        if (statusEffects == null)
            statusEffects = gameObject.AddComponent<StatusEffectController>();

        context = new PlayerStateContext(
            this,
            GetComponent<Player>(),
            GetComponent<PlayerInputReader>(),
            GetComponent<PlayerMovement>(),
            GetComponent<PlayerAimIndicator>(),
            GetComponent<DefaultAttack>(),
            statusEffects,
            GetComponent<Rigidbody>(),
            GetComponentInChildren<Animator>()
        );

        currentState = CreateState(PlayerActionState.Idle);
        currentState.Enter(PlayerActionState.Idle);
    }

    public void Tick()
    {
        currentState?.Tick();
    }

    public bool ShouldTickForNetwork(bool isOwner, bool hasStateAuthority)
    {
        return isOwner || (hasStateAuthority && currentState.RequiresStateAuthorityTick);
    }

    public bool ChangeState(PlayerActionState nextState)
    {
        if (CurrentState == nextState)
            return true;

        if (!CanEnter(nextState))
            return false;

        PlayerActionState previousState = CurrentState;
        currentState?.Exit(nextState);
        currentState = CreateState(nextState);
        currentState.Enter(previousState);
        return true;
    }

    public void BeginGrab(Transform grabSocket, int startDamage)
    {
        if (CurrentState == PlayerActionState.Dead || grabSocket == null)
            return;

        context.Player.TakeDamage(new AttackInfo(startDamage));
        SetState(new PlayerGrabbedState(context, grabSocket));
    }

    public void ApplyGrabHoldDamage(int damage)
    {
        if (currentState is PlayerGrabbedState)
            context.Player.TakeDamage(new AttackInfo(damage));
    }

    public void ThrowGrabbed(Vector3 force, int landingDamage)
    {
        if (currentState is not PlayerGrabbedState grabbedState)
            return;

        SetState(grabbedState.CreateThrownState(force, landingDamage));
    }

    public bool BeginKnockback()
    {
        if (CurrentState == PlayerActionState.Dead || context.StatusEffects.HasSuperArmor)
            return false;

        SetState(new PlayerKnockbackState(context));
        return true;
    }

    public void EndKnockback()
    {
        if (CurrentState == PlayerActionState.Knockback)
            ChangeState(PlayerActionState.Idle);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (currentState is PlayerThrownState thrownState)
            thrownState.OnCollisionEnter(collision);
    }

    public void EndInterrupt()
    {
        if (CurrentState == PlayerActionState.Interrupt)
            ChangeState(PlayerActionState.Idle);
    }

    private bool CanEnter(PlayerActionState nextState)
    {
        if (CurrentState == PlayerActionState.Dead)
            return false;

        return nextState switch
        {
            PlayerActionState.Attack => CanAttack && context.DefaultAttack.CanStartApprovedAttack,
            PlayerActionState.Interrupt => CanInterrupt && PlayerInterruptState.CanStart(context),
            PlayerActionState.Move => !context.StatusEffects.BlocksMovement,
            PlayerActionState.Idle => true,
            PlayerActionState.Grabbed => true,
            PlayerActionState.Thrown => true,
            PlayerActionState.Knockback => !context.StatusEffects.HasSuperArmor,
            PlayerActionState.Dead => true,
            _ => false
        };
    }

    private IPlayerState CreateState(PlayerActionState state)
    {
        return state switch
        {
            PlayerActionState.Idle => new PlayerIdleState(context),
            PlayerActionState.Move => new PlayerMoveState(context),
            PlayerActionState.Attack => new PlayerAttackState(context),
            PlayerActionState.Interrupt => new PlayerInterruptState(context),
            PlayerActionState.Grabbed => new PlayerLockedState(context, PlayerActionState.Grabbed),
            PlayerActionState.Thrown => new PlayerLockedState(context, PlayerActionState.Thrown),
            PlayerActionState.Knockback => new PlayerKnockbackState(context),
            PlayerActionState.Dead => new PlayerLockedState(context, PlayerActionState.Dead),
            _ => new PlayerIdleState(context)
        };
    }

    private void SetState(IPlayerState nextState)
    {
        PlayerActionState previousState = CurrentState;
        currentState?.Exit(nextState.StateType);
        currentState = nextState;
        currentState.Enter(previousState);
    }
}

public enum PlayerActionState
{
    Idle,
    Move,
    Attack,
    Interrupt,
    Grabbed,
    Thrown,
    Knockback,
    Dead
}

public sealed class PlayerStateContext
{
    public PlayerStateContext(
        PlayerStateController controller,
        Player player,
        PlayerInputReader input,
        PlayerMovement movement,
        PlayerAimIndicator aim,
        DefaultAttack defaultAttack,
        StatusEffectController statusEffects,
        Rigidbody rigidbody,
        Animator animator)
    {
        Controller = controller;
        Player = player;
        Input = input;
        Movement = movement;
        Aim = aim;
        DefaultAttack = defaultAttack;
        StatusEffects = statusEffects;
        Rigidbody = rigidbody;
        Animator = animator;
    }

    public PlayerStateController Controller { get; }
    public Player Player { get; }
    public PlayerInputReader Input { get; }
    public PlayerMovement Movement { get; }
    public PlayerAimIndicator Aim { get; }
    public DefaultAttack DefaultAttack { get; }
    public StatusEffectController StatusEffects { get; }
    public Rigidbody Rigidbody { get; }
    public Animator Animator { get; }
}

public interface IPlayerState
{
    PlayerActionState StateType { get; }
    bool RequiresStateAuthorityTick { get; }
    void Enter(PlayerActionState previousState);
    void Tick();
    void Exit(PlayerActionState nextState);
}

public abstract class PlayerStateBase : IPlayerState
{
    protected readonly PlayerStateContext Context;

    protected PlayerStateBase(PlayerStateContext context)
    {
        Context = context;
    }

    public abstract PlayerActionState StateType { get; }
    public virtual bool RequiresStateAuthorityTick => false;

    public virtual void Enter(PlayerActionState previousState) { }
    public virtual void Tick() { }
    public virtual void Exit(PlayerActionState nextState) { }

    protected bool TryConsumeActionInput()
    {
        if ((Context.Input.AttackPressed || Context.Input.AttackHeld) &&
            Context.DefaultAttack.TryStart())
        {
            return true;
        }

        if (Context.Input.InterruptPressed &&
            Context.Controller.ChangeState(PlayerActionState.Interrupt))
        {
            return true;
        }

        return false;
    }
}

public sealed class PlayerIdleState : PlayerStateBase
{
    public PlayerIdleState(PlayerStateContext context) : base(context) { }

    public override PlayerActionState StateType => PlayerActionState.Idle;

    public override void Enter(PlayerActionState previousState)
    {
        Context.Player.SetAnimatorMoving(false);
    }

    public override void Tick()
    {
        if (TryConsumeActionInput())
            return;

        if (Context.Input.HasMoveInput)
            Context.Controller.ChangeState(PlayerActionState.Move);
    }
}

public sealed class PlayerMoveState : PlayerStateBase
{
    public PlayerMoveState(PlayerStateContext context) : base(context) { }

    public override PlayerActionState StateType => PlayerActionState.Move;

    public override void Enter(PlayerActionState previousState)
    {
        Context.Player.SetAnimatorMoving(true);
    }

    public override void Tick()
    {
        if (TryConsumeActionInput())
            return;

        if (!Context.Input.HasMoveInput || Context.StatusEffects.BlocksMovement)
        {
            Context.Controller.ChangeState(PlayerActionState.Idle);
            return;
        }

        Context.Player.SetAnimatorMoving(true);
    }

    public override void Exit(PlayerActionState nextState)
    {
        if (nextState != PlayerActionState.Move)
            Context.Player.SetAnimatorMoving(false);
    }
}

public sealed class PlayerAttackState : PlayerStateBase
{
    public PlayerAttackState(PlayerStateContext context) : base(context) { }

    public override PlayerActionState StateType => PlayerActionState.Attack;

    public override void Enter(PlayerActionState previousState)
    {
        Context.Player.SetAnimatorMoving(false);
        Context.DefaultAttack.BeginFromState();
    }

    public override void Tick()
    {
        Context.Player.SetAnimatorMoving(false);
        Context.DefaultAttack.Tick();
    }

    public override void Exit(PlayerActionState nextState)
    {
        if (nextState != PlayerActionState.Attack)
            Context.DefaultAttack.CancelCurrentAttack();
    }
}

public sealed class PlayerInterruptState : PlayerStateBase
{
    private static readonly int InterruptHash = Animator.StringToHash("Interrupt");

    private Vector3 actionDirection;
    private float actionEndTime;
    private float actionMoveRemaining;
    private float actionMoveSpeed;

    public PlayerInterruptState(PlayerStateContext context) : base(context) { }

    public override PlayerActionState StateType => PlayerActionState.Interrupt;

    public static bool CanStart(PlayerStateContext context)
    {
        return context.Player.InterruptDuration > 0f &&
            context.Aim.AimDirection.sqrMagnitude >= 0.001f;
    }

    public override void Enter(PlayerActionState previousState)
    {
        Vector3 aimDirection = Context.Aim.AimDirection;

        if (aimDirection.sqrMagnitude < 0.001f || Context.Player.InterruptDuration <= 0f)
        {
            Context.Controller.ChangeState(PlayerActionState.Idle);
            return;
        }

        actionDirection = aimDirection.normalized;
        actionEndTime = Time.time + Context.Player.InterruptDuration;
        actionMoveRemaining = Mathf.Max(Context.Player.InterruptForwardDistance, 0f);
        actionMoveSpeed = actionMoveRemaining / Context.Player.InterruptDuration;

        Context.Movement.RotateImmediately(actionDirection);
        Context.Player.SetAnimatorMoving(false);

        if (Context.Animator != null)
            Context.Animator.SetTrigger(InterruptHash);
    }

    public override void Tick()
    {
        Context.Player.SetAnimatorMoving(false);
        MoveDuringAction();

        if (Time.time >= actionEndTime)
            Context.Controller.ChangeState(PlayerActionState.Idle);
    }

    private void MoveDuringAction()
    {
        if (actionMoveRemaining <= 0f)
            return;

        float moveDistance = Mathf.Min(actionMoveSpeed * Time.deltaTime, actionMoveRemaining);
        actionMoveRemaining -= moveDistance;
        Context.Movement.MoveRoot(actionDirection * moveDistance);
    }
}

public sealed class PlayerLockedState : PlayerStateBase
{
    public PlayerLockedState(PlayerStateContext context, PlayerActionState stateType) : base(context)
    {
        StateType = stateType;
    }

    public override PlayerActionState StateType { get; }

    public override void Enter(PlayerActionState previousState)
    {
        Context.Player.SetAnimatorMoving(false);
    }
}

public sealed class PlayerGrabbedState : PlayerStateBase
{
    private readonly Transform grabSocket;
    private bool wasMovementEnabled;
    private bool wasUseGravity;
    private bool wasKinematic;

    public PlayerGrabbedState(PlayerStateContext context, Transform grabSocket) : base(context)
    {
        this.grabSocket = grabSocket;
    }

    public override PlayerActionState StateType => PlayerActionState.Grabbed;
    public override bool RequiresStateAuthorityTick => true;

    public override void Enter(PlayerActionState previousState)
    {
        Context.Player.SetAnimatorMoving(false);

        if (Context.Movement != null)
        {
            wasMovementEnabled = Context.Movement.enabled;
            Context.Movement.enabled = false;
        }

        if (Context.Rigidbody != null)
        {
            wasUseGravity = Context.Rigidbody.useGravity;
            wasKinematic = Context.Rigidbody.isKinematic;
            Context.Rigidbody.linearVelocity = Vector3.zero;
            Context.Rigidbody.angularVelocity = Vector3.zero;
            Context.Rigidbody.useGravity = false;
            Context.Rigidbody.isKinematic = true;
            Context.Rigidbody.position = grabSocket.position;
            Context.Rigidbody.rotation = grabSocket.rotation;
        }
        else
        {
            Context.Player.transform.SetPositionAndRotation(grabSocket.position, grabSocket.rotation);
        }
    }

    public override void Tick()
    {
        if (grabSocket == null)
            return;

        if (Context.Rigidbody != null)
        {
            Context.Rigidbody.MovePosition(grabSocket.position);
            Context.Rigidbody.MoveRotation(grabSocket.rotation);
        }
        else
        {
            Context.Player.transform.SetPositionAndRotation(grabSocket.position, grabSocket.rotation);
        }
    }

    public override void Exit(PlayerActionState nextState)
    {
        if (nextState == PlayerActionState.Thrown)
            return;

        RestoreControl();
    }

    public PlayerThrownState CreateThrownState(Vector3 force, int landingDamage)
    {
        return new PlayerThrownState(
            Context,
            force,
            landingDamage,
            wasMovementEnabled,
            wasUseGravity,
            wasKinematic
        );
    }

    private void RestoreControl()
    {
        if (Context.Movement != null)
            Context.Movement.enabled = wasMovementEnabled;

        if (Context.Rigidbody != null)
        {
            Context.Rigidbody.useGravity = wasUseGravity;
            Context.Rigidbody.isKinematic = wasKinematic;
            Context.Rigidbody.linearVelocity = Vector3.zero;
            Context.Rigidbody.angularVelocity = Vector3.zero;
        }
    }
}

public sealed class PlayerThrownState : PlayerStateBase
{
    private readonly Vector3 force;
    private readonly int landingDamage;
    private readonly bool wasMovementEnabled;
    private readonly bool wasUseGravity;
    private readonly bool wasKinematic;

    public PlayerThrownState(
        PlayerStateContext context,
        Vector3 force,
        int landingDamage,
        bool wasMovementEnabled,
        bool wasUseGravity,
        bool wasKinematic) : base(context)
    {
        this.force = force;
        this.landingDamage = landingDamage;
        this.wasMovementEnabled = wasMovementEnabled;
        this.wasUseGravity = wasUseGravity;
        this.wasKinematic = wasKinematic;
    }

    public override PlayerActionState StateType => PlayerActionState.Thrown;

    public override void Enter(PlayerActionState previousState)
    {
        Context.Player.SetAnimatorMoving(false);

        if (Context.Rigidbody == null)
        {
            RestoreControl();
            Context.Controller.ChangeState(PlayerActionState.Idle);
            return;
        }

        Context.Rigidbody.isKinematic = false;
        Context.Rigidbody.useGravity = true;
        Context.Rigidbody.linearVelocity = Vector3.zero;
        Context.Rigidbody.angularVelocity = Vector3.zero;
        Context.Rigidbody.AddForce(force, ForceMode.VelocityChange);
    }

    public void OnCollisionEnter(Collision collision)
    {
        int groundLayer = LayerMask.NameToLayer("Surface");
        if (collision.gameObject.layer != groundLayer)
            return;

        Context.Player.TakeDamage(new AttackInfo(landingDamage));
        RestoreControl();
        Context.Controller.ChangeState(PlayerActionState.Idle);
    }

    private void RestoreControl()
    {
        if (Context.Movement != null)
            Context.Movement.enabled = wasMovementEnabled;

        if (Context.Rigidbody != null)
        {
            Context.Rigidbody.useGravity = wasUseGravity;
            Context.Rigidbody.isKinematic = wasKinematic;
            Context.Rigidbody.linearVelocity = Vector3.zero;
            Context.Rigidbody.angularVelocity = Vector3.zero;
        }
    }
}

public sealed class PlayerKnockbackState : PlayerStateBase
{
    public PlayerKnockbackState(PlayerStateContext context) : base(context) { }

    public override PlayerActionState StateType => PlayerActionState.Knockback;

    public override void Enter(PlayerActionState previousState)
    {
        Context.Player.SetAnimatorMoving(false);
    }
}
