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
            GetComponent<PlayerSkillController>(),
            GetComponent<PlayerDashController>(),
            GetComponent<PlayerGroundingSensor>()
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

    // 슈퍼아머 거부는 Unit.Knockback 공통 진입점에서 처리 — 여기서는 사망만 거부한다
    public bool BeginKnockback(Vector3 direction, float strength)
    {
        if (CurrentState == PlayerActionState.Dead)
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

    // Dash는 방향·속도·지속시간이 필수라 인스턴스 주입 경로로만 진입한다. (예측 게이트는 PlayerDashController가 확인)
    public bool BeginDash(Vector3 planarDirection, float speed, float duration, DashMotionSettings motion)
    {
        if (CurrentState == PlayerActionState.Dead)
            return false;

        SetState(new PlayerDashState(context, planarDirection, speed, duration, motion));
        return true;
    }

    public void EndDash()
    {
        if (CurrentState == PlayerActionState.Dash)
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
            PlayerActionState.Dash => false, // 방향·속도·지속이 필수라 BeginDash(...)로만 진입
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
    Skill,
    Dash
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
        PlayerSkillController skills,
        PlayerDashController dash,
        PlayerGroundingSensor groundingSensor)
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
        Dash = dash;
        GroundingSensor = groundingSensor;
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
    // 대시 컨트롤러 미장착 프리팹에서는 null — 사용처는 null 허용으로 다룬다
    public PlayerDashController Dash { get; }
    // 접지 센서 미장착 프리팹에서는 null — 사용처는 null 허용으로 다룬다
    public PlayerGroundingSensor GroundingSensor { get; }
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
        // 조준 모드 중에는 일반 액션 입력(공격/다른 스킬/인터럽트)을 억제한다.
        // 좌클릭 확정·Esc/재입력 취소는 PlayerSkillTargeting이 직접 처리한다.
        if (Context.Skills != null && Context.Skills.IsChoosingTarget)
            return false;

        // 대시 우선: Idle/Move에서 대시와 공격·스킬이 같은 프레임이면 대시가 이긴다. (PLAN §7)
        // 사용 불가능한 대시 입력은 여기서 소비되지 않아 아래 공격·스킬 경로가 보존된다.
        if (Context.Dash != null &&
            Context.Input.DashPressed &&
            Context.Dash.TryBeginPredictedDash())
        {
            return true;
        }

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

// 오너 예측 대시. 방향·Root Yaw를 시작 순간 확정하고 지속시간 동안 바꾸지 않는다. (PLAN §7, 불변식 6)
// W2는 평지 단순 이동만 담당한다. 경사·벽·절벽·공중 관성은 W3에서 대체·확장한다.
public sealed class PlayerDashState : PlayerStateBase
{
    private const int CastBufferSize = 8;

    private readonly Vector3 direction; // 평면 정규화 방향(시작 순간 확정)
    private readonly float speed;
    private readonly float endTime;
    private readonly DashMotionSettings motion;
    private readonly CapsuleCollider capsule;
    private readonly RaycastHit[] castBuffer = new RaycastHit[CastBufferSize];

    public PlayerDashState(PlayerStateContext context, Vector3 planarDirection, float speed, float duration, DashMotionSettings motion)
        : base(context)
    {
        Vector3 planar = planarDirection;
        planar.y = 0f;
        direction = planar.sqrMagnitude > 0.0001f
            ? planar.normalized
            : Context.Movement.CurrentFacing;
        this.speed = Mathf.Max(0f, speed);
        this.motion = motion;
        endTime = Time.time + Mathf.Max(0f, duration);
        capsule = Context.Player != null ? Context.Player.GetComponent<CapsuleCollider>() : null;
    }

    public override PlayerActionState StateType => PlayerActionState.Dash;

    public override void Enter(PlayerActionState previousState)
    {
        Context.Player.SetAnimatorMoving(false);
        Context.Movement.RotateImmediately(direction);
    }

    public override void Tick()
    {
        // 정면 벽으로 이동이 0이 되어도 대시 상태는 원래 종료시각까지 유지한다. (불변식: 상태·무적 유지)
        if (speed > 0f)
        {
            Vector3 moveDir = ResolvePlanarSlopeDirection();
            MoveWithSweep(moveDir * speed * Time.deltaTime);
        }

        if (Time.time >= endTime)
            Context.Controller.ChangeState(PlayerActionState.Idle);
    }

    // 지면이 걷기 가능 경사면 지면 평면에 투영해 오르막/내리막을 따라가고,
    // maxWalkableSlopeAngle 초과 급경사는 벽으로 취급해 투영하지 않는다(스윕이 클램프). (PLAN §8 / W3a·W3b)
    private Vector3 ResolvePlanarSlopeDirection()
    {
        PlayerGroundingSensor sensor = Context.GroundingSensor;
        if (sensor != null && sensor.IsGrounded)
        {
            float angle = Vector3.Angle(sensor.GroundNormal, Vector3.up);
            if (angle <= motion.MaxWalkableSlopeAngle)
            {
                Vector3 projected = Vector3.ProjectOnPlane(direction, sensor.GroundNormal);
                if (projected.sqrMagnitude > 0.0001f)
                    return projected.normalized;
            }
        }

        return direction;
    }

    // 실제 CapsuleCollider 형상으로 Sweep해 벽/지형 관통을 막고, 비스듬한 충돌은 접선으로 미끄러진다.
    // MovePosition이 지연 적용되므로 한 Tick의 모든 캐스트는 누적 오프셋으로 근사하고, MoveRoot는 마지막에 1회만 호출한다.
    private void MoveWithSweep(Vector3 delta)
    {
        if (capsule == null)
        {
            if (delta.sqrMagnitude > 0f)
                Context.Movement.MoveRoot(delta);
            return;
        }

        Vector3 accumulated = Vector3.zero;
        Vector3 remaining = delta;
        float skin = motion.CollisionSkin;
        int iterations = Mathf.Max(1, motion.MaxSweepIterations);

        for (int i = 0; i < iterations; i++)
        {
            float dist = remaining.magnitude;
            if (dist <= 1e-5f)
                break;

            Vector3 dir = remaining / dist;

            if (TryCapsuleCast(accumulated, dir, dist + skin, out RaycastHit hit))
            {
                float allowed = Mathf.Max(0f, hit.distance - skin);
                accumulated += dir * allowed;
                Vector3 leftover = dir * (dist - allowed);
                remaining = Vector3.ProjectOnPlane(leftover, hit.normal);
            }
            else
            {
                accumulated += remaining;
                break;
            }
        }

        if (accumulated.sqrMagnitude > 1e-10f)
            Context.Movement.MoveRoot(accumulated);
    }

    private bool TryCapsuleCast(Vector3 originOffset, Vector3 dir, float maxDistance, out RaycastHit best)
    {
        best = default;
        ComputeWorldCapsule(originOffset, out Vector3 p1, out Vector3 p2, out float radius);

        int count = Physics.CapsuleCastNonAlloc(
            p1, p2, radius, dir, castBuffer, maxDistance, motion.ObstacleMask, QueryTriggerInteraction.Ignore);

        float nearest = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = castBuffer[i];
            if (hit.collider == null || IsSelfCollider(hit.collider))
                continue;
            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                best = hit;
                found = true;
            }
        }

        return found;
    }

    // 캡슐 방향은 Y축(direction==1) 가정. 대부분의 Player 캡슐과 일치한다.
    private void ComputeWorldCapsule(Vector3 originOffset, out Vector3 p1, out Vector3 p2, out float radius)
    {
        Transform t = capsule.transform;
        Vector3 lossy = t.lossyScale;
        float radiusScale = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z));
        float heightScale = Mathf.Abs(lossy.y);

        radius = capsule.radius * radiusScale;
        float height = Mathf.Max(capsule.height * heightScale, radius * 2f);
        float half = Mathf.Max(0f, height * 0.5f - radius);

        Vector3 center = t.TransformPoint(capsule.center) + originOffset;
        Vector3 up = t.up;
        p1 = center + up * half;
        p2 = center - up * half;
    }

    private bool IsSelfCollider(Collider other)
    {
        Transform playerTransform = Context.Player.transform;
        return other.transform == playerTransform || other.transform.IsChildOf(playerTransform);
    }
}
