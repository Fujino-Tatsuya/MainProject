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
    public bool CanMove => (CurrentState == PlayerActionState.Idle || CurrentState == PlayerActionState.Move || AllowsSkillMovement) && !context.StatusEffects.BlocksMovement;
    public bool CanMovementRotate => (CurrentState == PlayerActionState.Idle || CurrentState == PlayerActionState.Move || AllowsSkillMovementRotate) && !context.StatusEffects.BlocksMovement;
    public bool CanAttack => (CurrentState == PlayerActionState.Idle || CurrentState == PlayerActionState.Move) && !context.StatusEffects.BlocksAttack;
    public bool CanInterrupt => (CurrentState == PlayerActionState.Idle || CurrentState == PlayerActionState.Move) && !context.StatusEffects.BlocksInterrupt;
    public bool CanUseSkill => (CurrentState == PlayerActionState.Idle || CurrentState == PlayerActionState.Move) && !context.StatusEffects.BlocksSkill;

    // 스킬 실행 중 이동/회전 허용 여부는 스킬 정의에 위임 (단일 Skill 상태)
    private bool AllowsSkillMovement => currentState is PlayerSkillState skillState && skillState.AllowsMovement;
    private bool AllowsSkillMovementRotate => currentState is PlayerSkillState skillState && skillState.AllowsMovementRotate;
    public bool HasSuperArmor => context.StatusEffects.HasSuperArmor;
    public float MinKnockbackTime => minKnockbackTime;
    public float MaxKnockbackTime => maxKnockbackTime;
    public float ServerKnockbackReportGraceTime => serverKnockbackReportGraceTime;
    public float KnockbackStopSpeed => knockbackStopSpeed;

    private void Awake()
    {
        // StatusEffectController는 NetworkBehaviour라 런타임 추가가 불가 — 프리팹에 미리 부착돼 있어야 한다
        StatusEffectController statusEffects = GetComponent<StatusEffectController>();
        if (statusEffects == null)
            Debug.LogError("[Player] StatusEffectController가 프리팹에 부착되어 있지 않습니다.", this);

        context = new PlayerStateContext(
            this,
            GetComponent<Player>(),
            GetComponent<PlayerInputReader>(),
            GetComponent<PlayerMovement>(),
            GetComponent<PlayerAimIndicator>(),
            GetComponent<DefaultAttackController>(),
            statusEffects,
            GetComponent<Rigidbody>(),
            GetComponentInChildren<Animator>(),
            GetComponent<PlayerSkillController>()
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

        SetState(new PlayerGrabbedState(context, grabContext.Instigator));
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

    public bool BeginKnockback(Vector3 direction, float strength)
    {
        bool isDead = CurrentState == PlayerActionState.Dead;
        bool hasSuperArmor = context.StatusEffects.HasSuperArmor;

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
        if (CurrentState == PlayerActionState.Knockback)
            ChangeState(PlayerActionState.Idle);
    }

    public void EndInterrupt()
    {
        if (CurrentState == PlayerActionState.Interrupt)
            ChangeState(PlayerActionState.Idle);
    }

    // Skill 상태는 실행할 스킬 인스턴스가 필요해 BeginKnockback처럼 인스턴스 주입 경로로만 진입한다.
    public bool BeginSkill(PlayerSkillBase skill)
    {
        if (skill == null || !CanUseSkill)
            return false;

        SetState(new PlayerSkillState(context, skill));
        return true;
    }

    public void EndSkill()
    {
        if (CurrentState == PlayerActionState.Skill)
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
            PlayerActionState.Skill => false, // 스킬 인스턴스가 필수라 BeginSkill(skill)으로만 진입
            PlayerActionState.Move => !context.StatusEffects.BlocksMovement,
            PlayerActionState.Idle => true,
            PlayerActionState.Grabbed => true,
            PlayerActionState.Knockback => false, // 방향·세기가 필수라 BeginKnockback(direction, strength)으로만 진입
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
    Dead,
    Skill
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
        Animator animator,
        PlayerSkillController skills)
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
        Skills = skills;
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
    // 스킬 시스템 미장착 프리팹에서는 null — 사용처는 전부 null 허용으로 다룬다
    public PlayerSkillController Skills { get; }
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

        if (TryStartSkillInput())
            return true;

        if (Context.Input.InterruptPressed &&
            Context.Controller.ChangeState(PlayerActionState.Interrupt))
        {
            return true;
        }

        return false;
    }

    private bool TryStartSkillInput()
    {
        PlayerSkillController skills = Context.Skills;
        if (skills == null)
            return false;

        // Interrupt 슬롯에 스킬(단죄의 방패)이 배정되면 여기서 소비되어
        // 아래 기존 Interrupt 상태 경로를 자연히 대체한다. 미배정이면 TryUse가 false라 기존 경로 유지.
        return (Context.Input.GetSkillPressed(PlayerSkillSlot.Main) && skills.TryUse(PlayerSkillSlot.Main)) ||
            (Context.Input.GetSkillPressed(PlayerSkillSlot.Sub) && skills.TryUse(PlayerSkillSlot.Sub)) ||
            (Context.Input.GetSkillPressed(PlayerSkillSlot.Interrupt) && skills.TryUse(PlayerSkillSlot.Interrupt)) ||
            (Context.Input.GetSkillPressed(PlayerSkillSlot.Ultimate) && skills.TryUse(PlayerSkillSlot.Ultimate));
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
    private readonly GameObject instigator;
    private bool wasKinematic;
    private bool wasDetectingCollisions;
    private bool hadRigidbody;
    private Transform followTarget;

    public PlayerGrabbedState(PlayerStateContext context, GameObject instigator = null) : base(context)
    {
        this.instigator = instigator;
    }

    public override PlayerActionState StateType => PlayerActionState.Grabbed;
    public override bool RequiresStateAuthorityTick => true;

    public override void Enter(PlayerActionState previousState)
    {
        Context.DefaultAttack.CancelCurrentAttack();
        Context.Player.SetAnimatorMoving(false);
        GrabController grabController = instigator != null
            ? instigator.GetComponentInChildren<GrabController>()
            : null;
        followTarget = grabController != null ? grabController.GrabSocket : null;
        DelegatePhysicsAndCollisionToInstigator();
        FaceInstigator();
        // TODO: Play the grabbed animation here after the Animator parameter/clip is configured.
        // Example: Context.Animator.SetBool("IsGrabbed", true);
    }

    public override void Exit(PlayerActionState nextState)
    {
        RestorePlayerPhysicsAndCollision();
        FaceInstigator();
        // TODO: Stop the grabbed animation here after the Animator parameter/clip is configured.
        // Example: Context.Animator.SetBool("IsGrabbed", false);
    }

    public override void Tick()
    {
        if (!Context.Player.IsMovementAuthority || followTarget == null)
            return;

        if (Context.Rigidbody != null)
        {
            Context.Rigidbody.MovePosition(followTarget.position);
            Context.Rigidbody.MoveRotation(followTarget.rotation);
            return;
        }

        Context.Player.transform.SetPositionAndRotation(followTarget.position, followTarget.rotation);
    }

    private void DelegatePhysicsAndCollisionToInstigator()
    {
        if (!Context.Player.IsMovementAuthority || Context.Rigidbody == null)
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

    // 잡기 소켓에 슬레이브되며 생긴 기울어짐을 정리하고 보스 방향(yaw만)으로 세운다.
    // instigator가 없으면 현재 바라보던 방향을 유지한 채 똑바로만 세운다.
    private void FaceInstigator()
    {
        Vector3 lookDirection = instigator != null
            ? instigator.transform.position - Context.Player.transform.position
            : Context.Player.transform.forward;
        lookDirection.y = 0f;

        if (lookDirection.sqrMagnitude < 0.0001f)
            return;

        Quaternion rotation = Quaternion.LookRotation(lookDirection.normalized);

        if (Context.Rigidbody != null)
            Context.Rigidbody.rotation = rotation;

        Context.Player.transform.rotation = rotation;
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
        Context.DefaultAttack.CancelCurrentAttack();

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
        Context.Controller.EndKnockback();
        Context.Player.NotifyKnockbackEnded();
    }
}
