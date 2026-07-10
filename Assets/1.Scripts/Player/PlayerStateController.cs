using UnityEngine;

[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerAimIndicator))]
[RequireComponent(typeof(DefaultAttackController))]
[RequireComponent(typeof(StatusEffectController))]
public class PlayerStateController : MonoBehaviour, IGrabInteractionReceiver
{
    [SerializeField] private PlayerActionState currentStateDebug;
    [SerializeField] private float minKnockbackTime = 0.15f;
    [SerializeField] private float maxKnockbackTime = 1.5f;
    [SerializeField, Min(0f)] private float serverKnockbackReportGraceTime = 0.25f;
    [SerializeField] private float knockbackStopSpeed = 0.15f;

    private IPlayerState currentState;
    private PlayerStateContext context;

    public PlayerActionState CurrentState => currentState?.StateType ?? PlayerActionState.Idle;
    public bool CanMove => (CurrentState == PlayerActionState.Idle || CurrentState == PlayerActionState.Move) && !context.StatusEffects.BlocksMovement;
    public bool CanMovementRotate => (CurrentState == PlayerActionState.Idle || CurrentState == PlayerActionState.Move) && !context.StatusEffects.BlocksMovement;
    public bool CanAttack => (CurrentState == PlayerActionState.Idle || CurrentState == PlayerActionState.Move) && !context.StatusEffects.BlocksAttack;
    public bool CanInterrupt => (CurrentState == PlayerActionState.Idle || CurrentState == PlayerActionState.Move) && !context.StatusEffects.BlocksInterrupt;
    public bool HasSuperArmor => context.StatusEffects.HasSuperArmor;
    public float MinKnockbackTime => minKnockbackTime;
    public float MaxKnockbackTime => maxKnockbackTime;
    public float ServerKnockbackReportGraceTime => serverKnockbackReportGraceTime;
    public float KnockbackStopSpeed => knockbackStopSpeed;

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
            GetComponent<DefaultAttackController>(),
            statusEffects,
            GetComponent<Rigidbody>(),
            GetComponentInChildren<Animator>()
        );

        currentState = CreateState(PlayerActionState.Idle);
        currentStateDebug = currentState.StateType;
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
        currentStateDebug = currentState.StateType;
        currentState.Enter(previousState);
        return true;
    }

    public bool TryReceiveGrab(GrabInteractionContext grabContext)
    {
        if (!CanReceiveServerInteraction())
            return false;

        return ApplyGrabbed(grabContext);
    }

    public bool ApplyGrabbedFromServer(GameObject instigator = null)
    {
        return ApplyGrabbed(new GrabInteractionContext(instigator, gameObject));
    }

    private bool ApplyGrabbed(GrabInteractionContext grabContext)
    {
        if (!CanReceiveGrab(grabContext))
            return false;

        SetState(new PlayerGrabbedState(context));
        return true;
    }

    public bool BeginGrabbed(GameObject instigator = null)
    {
        return TryReceiveGrab(new GrabInteractionContext(instigator, gameObject));
    }

    public bool EndGrabbed()
    {
        if (CurrentState != PlayerActionState.Grabbed)
            return false;

        ChangeState(PlayerActionState.Idle);
        return true;
    }

    public bool BeginKnockback()
    {
        return BeginKnockback(Vector3.zero, 0f);
    }

    public bool BeginKnockback(Vector3 direction, float strength)
    {
        bool isDead = CurrentState == PlayerActionState.Dead;
        bool hasSuperArmor = context.StatusEffects.HasSuperArmor;
        BeforeMergeTestLog.Info(
            "KNOCKBACK",
            "BEGIN_ATTEMPT",
            $"state={CurrentState}, isDead={isDead}, hasSuperArmor={hasSuperArmor}, movementAuthority={context.Player.IsMovementAuthority}, strength={strength}",
            context.Player);

        if (isDead || hasSuperArmor)
            return false;

        SetState(new PlayerKnockbackState(context, direction, strength));
        return true;
    }

    public bool ApplyKnockbackFromServer(Vector3 direction, float strength)
    {
        return BeginKnockback(direction, strength);
    }

    public void EndKnockback()
    {
        BeforeMergeTestLog.Info(
            "KNOCKBACK",
            "END_ATTEMPT",
            $"stateBefore={CurrentState}, willChange={CurrentState == PlayerActionState.Knockback}, movementAuthority={context.Player.IsMovementAuthority}",
            context.Player);
        if (CurrentState == PlayerActionState.Knockback)
            ChangeState(PlayerActionState.Idle);
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
            PlayerActionState.Grabbed => new PlayerGrabbedState(context),
            PlayerActionState.Knockback => new PlayerKnockbackState(context, Vector3.zero, 0f),
            PlayerActionState.Dead => new PlayerLockedState(context, PlayerActionState.Dead),
            _ => new PlayerIdleState(context)
        };
    }

    private void SetState(IPlayerState nextState)
    {
        PlayerActionState previousState = CurrentState;
        currentState?.Exit(nextState.StateType);
        currentState = nextState;
        currentStateDebug = currentState.StateType;
        currentState.Enter(previousState);
    }

    private bool CanReceiveGrab(GrabInteractionContext grabContext)
    {
        return CurrentState != PlayerActionState.Dead &&
            CurrentState != PlayerActionState.Grabbed;
    }

    private bool CanReceiveServerInteraction()
    {
        if (context?.Player == null)
            return false;

        return !context.Player.IsSpawned || context.Player.IsServer;
    }
}

public readonly struct GrabInteractionContext
{
    public GrabInteractionContext(GameObject instigator, GameObject receiver)
    {
        Instigator = instigator;
        Receiver = receiver;
    }

    public GameObject Instigator { get; }
    public GameObject Receiver { get; }
}

public interface IGrabInteractionReceiver
{
    bool TryReceiveGrab(GrabInteractionContext context);
    bool BeginGrabbed(GameObject instigator = null);
    bool EndGrabbed();
}

public enum PlayerActionState
{
    Idle,
    Move,
    Attack,
    Interrupt,
    Grabbed,
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
        DefaultAttackController defaultAttack,
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
    public DefaultAttackController DefaultAttack { get; }
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
        // 공격 시작은 누른 프레임(press)에만 허용한다. 홀드 상태로는 시작되지 않으므로,
        // Once 정책에서 체인이 끝난 뒤 계속 누르고 있어도 재시작되지 않는다(릴리즈 요구).
        if (Context.Input.AttackPressed &&
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
    public override bool RequiresStateAuthorityTick => true;

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
    private bool wasKinematic;
    private bool wasDetectingCollisions;
    private bool hadRigidbody;

    public PlayerGrabbedState(PlayerStateContext context) : base(context) { }

    public override PlayerActionState StateType => PlayerActionState.Grabbed;

    public override void Enter(PlayerActionState previousState)
    {
        Context.Player.SetAnimatorMoving(false);
        DelegatePhysicsAndCollisionToInstigator();
        // TODO: Play the grabbed animation here after the Animator parameter/clip is configured.
        // Example: Context.Animator.SetBool("IsGrabbed", true);
    }

    public override void Exit(PlayerActionState nextState)
    {
        RestorePlayerPhysicsAndCollision();
        ResetRootRotation();
        // TODO: Stop the grabbed animation here after the Animator parameter/clip is configured.
        // Example: Context.Animator.SetBool("IsGrabbed", false);
    }

    private void DelegatePhysicsAndCollisionToInstigator()
    {
        if (Context.Rigidbody == null)
            return;

        hadRigidbody = true;
        wasKinematic = Context.Rigidbody.isKinematic;
        wasDetectingCollisions = Context.Rigidbody.detectCollisions;

        Context.Rigidbody.linearVelocity = Vector3.zero;
        Context.Rigidbody.angularVelocity = Vector3.zero;
        Context.Rigidbody.isKinematic = true;
        Context.Rigidbody.detectCollisions = false;
    }

    private void RestorePlayerPhysicsAndCollision()
    {
        if (!hadRigidbody || Context.Rigidbody == null)
            return;

        Context.Rigidbody.isKinematic = wasKinematic;
        Context.Rigidbody.detectCollisions = wasDetectingCollisions;
        Context.Rigidbody.linearVelocity = Vector3.zero;
        Context.Rigidbody.angularVelocity = Vector3.zero;
    }

    private void ResetRootRotation()
    {
        if (Context.Rigidbody != null)
            Context.Rigidbody.rotation = Quaternion.identity;

        Context.Player.transform.rotation = Quaternion.identity;
    }
}

public sealed class PlayerKnockbackState : PlayerStateBase
{
    private readonly Vector3 direction;
    private readonly float strength;
    private float startTime;

    public PlayerKnockbackState(PlayerStateContext context, Vector3 direction, float strength) : base(context)
    {
        this.direction = direction;
        this.strength = strength;
    }

    public override PlayerActionState StateType => PlayerActionState.Knockback;
    public override bool RequiresStateAuthorityTick => true;

    public override void Enter(PlayerActionState previousState)
    {
        BeforeMergeTestLog.Info(
            "KNOCKBACK",
            "STATE_ENTER",
            $"movementAuthority={Context.Player.IsMovementAuthority}, prev={previousState}, strength={strength}, maxDuration={Context.Controller.MaxKnockbackTime}",
            Context.Player);

        Context.Player.SetAnimatorMoving(false);

        startTime = Time.time;

        // 물리 적용은 이동 권위(오너/오프라인)만 — 서버(비오너) 사본은 상태 장부만 기록
        if (!Context.Player.IsMovementAuthority || Context.Rigidbody == null)
            return;

        Context.Rigidbody.isKinematic = false;
        Context.Rigidbody.linearVelocity = Vector3.zero;
        Context.Rigidbody.angularVelocity = Vector3.zero;

        if (direction.sqrMagnitude > 0.001f && strength > 0f)
            Context.Rigidbody.AddForce(direction.normalized * strength, ForceMode.Impulse);
    }

    public override void Tick()
    {
        if (!Context.Player.IsMovementAuthority)
        {
            // 서버(비오너) 사본: 오너의 종료 보고가 1차 경로, 타임아웃은 보고 유실 대비 안전망
            float serverFallbackTimeout = Context.Controller.MaxKnockbackTime +
                                          Context.Controller.ServerKnockbackReportGraceTime;
            if (Time.time - startTime >= serverFallbackTimeout)
            {
                BeforeMergeTestLog.Warning(
                    "KNOCKBACK",
                    "SERVER_FALLBACK_TIMEOUT",
                    $"elapsed={Time.time - startTime:F3}, ownerMaxDuration={Context.Controller.MaxKnockbackTime:F3}, reportGrace={Context.Controller.ServerKnockbackReportGraceTime:F3}, serverTimeout={serverFallbackTimeout:F3} — 오너 보고가 안 왔음",
                    Context.Player);
                Context.Controller.EndKnockback();
            }
            return;
        }

        if (Context.Rigidbody == null)
        {
            EndAndNotifyServer("rigidbody-null", Time.time - startTime, -1f);
            return;
        }

        float elapsed = Time.time - startTime;
        if (elapsed < Context.Controller.MinKnockbackTime)
            return;

        float stopSpeed = Context.Controller.KnockbackStopSpeed;
        bool slowEnough = Context.Rigidbody.linearVelocity.sqrMagnitude <= stopSpeed * stopSpeed;
        bool timeout = elapsed >= Context.Controller.MaxKnockbackTime;

        if (slowEnough || timeout)
        {
            string reason = slowEnough ? "slow-enough" : "owner-timeout";
            EndAndNotifyServer(reason, elapsed, Context.Rigidbody.linearVelocity.magnitude);
        }
    }

    public override void Exit(PlayerActionState nextState)
    {
        if (!Context.Player.IsMovementAuthority || Context.Rigidbody == null)
            return;

        Context.Rigidbody.linearVelocity = Vector3.zero;
        Context.Rigidbody.angularVelocity = Vector3.zero;
    }

    private void EndAndNotifyServer(string reason, float elapsed, float speed)
    {
        BeforeMergeTestLog.Info(
            "KNOCKBACK",
            "OWNER_END",
            $"reason={reason}, elapsed={elapsed:F3}, speed={speed:F3}, maxDuration={Context.Controller.MaxKnockbackTime:F3} → 서버 보고 시도",
            Context.Player);
        Context.Controller.EndKnockback();
        Context.Player.NotifyKnockbackEnded();
    }
}
