using System;
using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

public enum DefaultAttackComboInputType
{
    HoldAutoChainOnce,
    HoldAutoRepeat,
    TimedInputWindow
}

public enum DefaultAttackMovementType
{
    None,
    ScriptedForwardDistance,
    AnimationRootMotionProjected
}

public enum DefaultAttackRotationType
{
    None,
    SnapOnStart,
    TrackAimDuringAttack
}

public enum DefaultAttackAnimationEventType
{
    Hit = 0,
    ComboWindowOpen = 1,
    ComboWindowClose = 2,
    End = 3
}

public enum DefaultAttackHitType
{
    Overlap,
    Projectile,
    Raycast
}

[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerAimIndicator))]
[RequireComponent(typeof(PlayerDefaultAttack))]
public class DefaultAttackController : BaseNetworkBehaviour
{
    private static readonly int DefaultAttackHash = Animator.StringToHash("DefaultAttack");
    private static readonly int AttackIndexHash = Animator.StringToHash("AttackIndex");
    private static readonly int IdleHash = Animator.StringToHash("Idle");
    private static readonly int[] AttackStateHashes =
    {
        Animator.StringToHash("Garen_Default_Attack0"),
        Animator.StringToHash("Garen_Default_Attack1"),
        Animator.StringToHash("Garen_Default_Attack2"),
        Animator.StringToHash("Garen_Default_Attack3")
    };

    [SerializeField] private Animator animator;
    [SerializeField] private DefaultAttackData attackData;
    [SerializeField] private PlayerDefaultAttack playerDefaultAttack;
    [SerializeField] private DefaultAttackComboInputType comboInputType = DefaultAttackComboInputType.HoldAutoRepeat;
    [SerializeField] private DefaultAttackStep[] attackSteps =
    {
        new DefaultAttackStep(),
        new DefaultAttackStep(),
        new DefaultAttackStep(),
        new DefaultAttackStep()
    };
    [SerializeField] private ColliderInfo defaultHitbox;
    [SerializeField] private LayerMask hittableLayers;
    [SerializeField] private int maxHitResults = 16;
    [SerializeField] private int damageOverride;
    [SerializeField] private float endFallbackPadding = 0.1f;

    private Player player;
    private PlayerInputReader inputReader;
    private PlayerMovement movement;
    private PlayerAimIndicator aimIndicator;
    private Vector3 attackDirection;
    private Vector3 queuedAttackDirection;
    private int currentAttackIndex;
    private float moveRemaining;
    private float moveSpeed;
    private float attackEndFallbackTime;
    private bool isRequestingAttack;
    private bool isComboWindowOpen;
    private bool hasQueuedNextAttack;
    private bool hasStartedAttack;

    public bool IsAttacking => player != null && player.CurrentState == PlayerActionState.Attack;
    public bool CanRequestStart => HasAttackSteps && CurrentStepDuration > 0f;
    public bool CanStartApprovedAttack => HasAttackSteps && CurrentStepDuration > 0f;
    private bool HasGameplayAuthority => !IsNetworkActive || IsServer;

    private void Awake()
    {
        player = GetComponent<Player>();
        inputReader = GetComponent<PlayerInputReader>();
        movement = GetComponent<PlayerMovement>();
        aimIndicator = GetComponent<PlayerAimIndicator>();

        if (playerDefaultAttack == null)
            playerDefaultAttack = GetComponent<PlayerDefaultAttack>();

        if (playerDefaultAttack == null)
            playerDefaultAttack = gameObject.AddComponent<PlayerDefaultAttack>();

        if (attackData != null)
            ApplyData(attackData);
        else
            playerDefaultAttack.Configure(defaultHitbox, hittableLayers, maxHitResults);

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator != null && !animator.TryGetComponent(out PlayerAnimationEventRelay _))
            animator.gameObject.AddComponent<PlayerAnimationEventRelay>();

        if (animator != null && !animator.TryGetComponent(out PlayerRootMotionRelay _))
            animator.gameObject.AddComponent<PlayerRootMotionRelay>();
    }

    public bool TryStart()
    {
        if (IsAttacking || isRequestingAttack || player == null || !CanRequestStart)
            return false;

        Vector3 requestedDirection = GetCurrentAimDirection();

        if (!IsNetworkActive)
        {
            StartAttackServer(0, requestedDirection, true);
            return true;
        }

        if (!IsOwner)
            return false;

        isRequestingAttack = true;
        RequestStartAttackRpc(requestedDirection);
        return true;
    }

    public void BeginFromState()
    {
        // Attack state entry is driven by StartAttackServer/PlayAttackClientRpc.
    }

    public void ApplyData(DefaultAttackData data)
    {
        if (data == null)
            return;

        attackData = data;
        comboInputType = data.ComboInputType;
        attackSteps = data.AttackSteps;
        defaultHitbox = data.DefaultHitbox;
        hittableLayers = data.HittableLayers;
        maxHitResults = data.MaxHitResults;
        damageOverride = data.DamageOverride;

        if (playerDefaultAttack != null)
            playerDefaultAttack.Configure(defaultHitbox, hittableLayers, maxHitResults);
    }

    public void SetAnimator(Animator newAnimator)
    {
        if (newAnimator == null)
            return;

        animator = newAnimator;

        if (!animator.TryGetComponent(out PlayerAnimationEventRelay _))
            animator.gameObject.AddComponent<PlayerAnimationEventRelay>();

        if (!animator.TryGetComponent(out PlayerRootMotionRelay _))
            animator.gameObject.AddComponent<PlayerRootMotionRelay>();
    }

    public void Tick()
    {
        if ((!IsNetworkActive || IsOwner) && IsAttacking)
            TryQueueNextAttackFromInput();

        if (HasGameplayAuthority && IsAttacking)
            TickServerFallbacks();

        if (!IsNetworkActive || IsServer || IsOwner)
            TickMovementAndRotation();
    }

    public void CancelCurrentAttack()
    {
        ResetAttackRuntime();

        if (animator != null)
            animator.CrossFadeInFixedTime(IdleHash, 0.05f);
    }

    public void HandleAnimationEvent(DefaultAttackAnimationEventType eventType)
    {
        if (IsNetworkActive && !IsServer)
            return;

        switch (eventType)
        {
            case DefaultAttackAnimationEventType.Hit:
                playerDefaultAttack.HitCurrentStep();
                break;

            case DefaultAttackAnimationEventType.ComboWindowOpen:
                isComboWindowOpen = true;
                break;

            case DefaultAttackAnimationEventType.ComboWindowClose:
                isComboWindowOpen = false;
                break;

            case DefaultAttackAnimationEventType.End:
                CompleteCurrentAttackStep();
                break;
        }
    }

    public void EndCurrentAttack()
    {
        if (HasGameplayAuthority)
            CompleteCurrentAttackStep();
    }

    public void HitCurrentAttack()
    {
        if (HasGameplayAuthority)
            playerDefaultAttack.HitCurrentStep();
    }

    public void HandleAnimatorMove(Vector3 deltaPosition, Vector3 animatorForward)
    {
        if (!HasAttackStep(currentAttackIndex))
            return;

        if (IsNetworkActive && !IsServer && !IsOwner)
            return;

        DefaultAttackStep step = attackSteps[currentAttackIndex];
        if (step.MovementType != DefaultAttackMovementType.AnimationRootMotionProjected)
            return;

        animatorForward.y = 0f;
        if (animatorForward.sqrMagnitude < 0.001f)
            animatorForward = attackDirection;

        float forwardDistance = Vector3.Dot(deltaPosition, animatorForward.normalized);
        if (Mathf.Abs(forwardDistance) <= 0.0001f)
            return;

        movement.MoveRoot(attackDirection * forwardDistance);
    }

    [Rpc(SendTo.Server)]
    private void RequestStartAttackRpc(Vector3 requestedDirection, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!CanApproveServerAttack())
        {
            RejectStartAttackClientRpc(CreateOwnerClientRpcParams());
            return;
        }

        StartAttackServer(0, requestedDirection, true);
    }

    [Rpc(SendTo.Server)]
    private void RequestQueueNextAttackRpc(Vector3 requestedDirection, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || !IsAttacking)
            return;

        if (comboInputType == DefaultAttackComboInputType.TimedInputWindow && !isComboWindowOpen)
            return;

        hasQueuedNextAttack = true;
        queuedAttackDirection = ResolveAttackDirection(requestedDirection);
    }

    [ClientRpc]
    private void PlayDefaultAttackClientRpc(int attackIndex, Vector3 direction)
    {
        if (IsServer)
            return;

        isRequestingAttack = false;
        StartAttackPresentation(attackIndex, direction, true);

        if (player != null && player.CurrentState != PlayerActionState.Attack)
            player.SetState(PlayerActionState.Attack);
    }

    [ClientRpc]
    private void EndDefaultAttackClientRpc()
    {
        if (IsServer)
            return;

        ResetAttackRuntime();

        if (player != null && player.CurrentState == PlayerActionState.Attack)
            player.SetState(PlayerActionState.Idle);

        if (animator != null)
            animator.CrossFadeInFixedTime(IdleHash, 0.05f);
    }

    [ClientRpc]
    private void RejectStartAttackClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (IsOwner)
            isRequestingAttack = false;
    }

    private void StartAttackServer(int attackIndex, Vector3 direction, bool triggerAttack)
    {
        if (!HasAttackStep(attackIndex))
            return;

        DefaultAttackStep step = attackSteps[attackIndex];
        if (step.Duration <= 0f)
            return;

        isRequestingAttack = false;
        hasStartedAttack = true;
        hasQueuedNextAttack = false;
        isComboWindowOpen = false;
        currentAttackIndex = attackIndex;
        attackDirection = ResolveAttackDirection(direction);
        queuedAttackDirection = attackDirection;
        attackEndFallbackTime = Time.time + step.Duration + Mathf.Max(0f, endFallbackPadding);

        int damageSnapshot = CalculateDamageSnapshot(step);
        playerDefaultAttack.PrepareStep(step, damageSnapshot, attackDirection);

        if (player != null && player.CurrentState != PlayerActionState.Attack)
            player.SetState(PlayerActionState.Attack);

        StartAttackPresentation(attackIndex, attackDirection, triggerAttack);

        if (IsNetworkActive)
            PlayDefaultAttackClientRpc(attackIndex, attackDirection);
    }

    private void StartAttackPresentation(int attackIndex, Vector3 direction, bool triggerAttack)
    {
        if (!HasAttackStep(attackIndex))
            return;

        DefaultAttackStep step = attackSteps[attackIndex];
        currentAttackIndex = attackIndex;
        attackDirection = ResolveAttackDirection(direction);
        moveRemaining = step.MovementType == DefaultAttackMovementType.ScriptedForwardDistance
            ? Mathf.Max(step.ForwardDistance, 0f)
            : 0f;
        moveSpeed = step.Duration > 0f ? moveRemaining / step.Duration : 0f;

        if (step.RotationType == DefaultAttackRotationType.SnapOnStart)
            movement.RotateImmediately(attackDirection);

        player.SetAnimatorMoving(false);

        if (animator == null)
            return;

        animator.SetInteger(AttackIndexHash, currentAttackIndex);

        if (triggerAttack)
            animator.SetTrigger(DefaultAttackHash);
        else if (currentAttackIndex < AttackStateHashes.Length)
            animator.CrossFadeInFixedTime(AttackStateHashes[currentAttackIndex], 0.05f);
    }

    private void CompleteCurrentAttackStep()
    {
        if (!HasGameplayAuthority || !IsAttacking || !hasStartedAttack)
            return;

        if (ShouldStartNextAttack(out int nextIndex, out Vector3 nextDirection))
        {
            StartAttackServer(nextIndex, nextDirection, false);
            return;
        }

        EndAttackServer();
    }

    private bool ShouldStartNextAttack(out int nextIndex, out Vector3 nextDirection)
    {
        nextIndex = currentAttackIndex + 1;
        nextDirection = queuedAttackDirection;
        bool hasNext = nextIndex < attackSteps.Length;

        switch (comboInputType)
        {
            case DefaultAttackComboInputType.HoldAutoChainOnce:
                if (!hasQueuedNextAttack || !hasNext)
                    return false;
                break;

            case DefaultAttackComboInputType.HoldAutoRepeat:
                if (!hasQueuedNextAttack)
                    return false;
                if (!hasNext)
                    nextIndex = 0;
                break;

            case DefaultAttackComboInputType.TimedInputWindow:
                if (!hasQueuedNextAttack || !hasNext)
                    return false;
                break;

            default:
                return false;
        }

        return HasAttackStep(nextIndex);
    }

    private void EndAttackServer()
    {
        ResetAttackRuntime();

        if (player != null && player.CurrentState == PlayerActionState.Attack)
            player.SetState(PlayerActionState.Idle);

        if (animator != null)
            animator.CrossFadeInFixedTime(IdleHash, 0.05f);

        if (IsNetworkActive)
            EndDefaultAttackClientRpc();
    }

    private void TickServerFallbacks()
    {
        if (hasStartedAttack && attackEndFallbackTime > 0f && Time.time >= attackEndFallbackTime)
            CompleteCurrentAttackStep();
    }

    private void TickMovementAndRotation()
    {
        if (!HasAttackStep(currentAttackIndex))
            return;

        DefaultAttackStep step = attackSteps[currentAttackIndex];

        if (step.RotationType == DefaultAttackRotationType.TrackAimDuringAttack)
            movement.RotateToward(GetCurrentAimDirection(), step.TrackRotationSpeed);

        if (moveRemaining <= 0f)
            return;

        float moveDistance = Mathf.Min(moveSpeed * Time.deltaTime, moveRemaining);
        moveRemaining -= moveDistance;
        movement.MoveRoot(attackDirection * moveDistance);
    }

    private void TryQueueNextAttackFromInput()
    {
        bool shouldQueue = comboInputType switch
        {
            DefaultAttackComboInputType.HoldAutoChainOnce => inputReader.AttackHeld,
            DefaultAttackComboInputType.HoldAutoRepeat => inputReader.AttackHeld,
            DefaultAttackComboInputType.TimedInputWindow => inputReader.AttackPressed,
            _ => false
        };

        if (!shouldQueue)
            return;

        Vector3 direction = GetCurrentAimDirection();

        if (!IsNetworkActive)
        {
            hasQueuedNextAttack = true;
            queuedAttackDirection = direction;
            return;
        }

        RequestQueueNextAttackRpc(direction);
    }

    private bool CanApproveServerAttack()
    {
        if (!HasAttackStep(0))
            return false;

        if (player == null || player.CurrentState == PlayerActionState.Dead)
            return false;

        StatusEffectController statusEffects = GetComponent<StatusEffectController>();
        return statusEffects == null || !statusEffects.BlocksAttack;
    }

    private int CalculateDamageSnapshot(DefaultAttackStep step)
    {
        int baseDamage = player != null ? player.AttackDamage : 0;
        int calculatedDamage = Mathf.RoundToInt(baseDamage * step.AttackDamageMultiplier) + step.FlatDamageBonus;

        if (damageOverride > 0)
            calculatedDamage = damageOverride;

        return Mathf.Max(0, calculatedDamage);
    }

    private Vector3 GetCurrentAimDirection()
    {
        if (aimIndicator != null)
            return ResolveAttackDirection(aimIndicator.AimDirection);

        return ResolveAttackDirection(attackDirection);
    }

    private Vector3 ResolveAttackDirection(Vector3 direction)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude >= 0.001f)
            return direction.normalized;

        if (attackDirection.sqrMagnitude >= 0.001f)
            return attackDirection.normalized;

        return transform.forward;
    }

    private void ResetAttackRuntime()
    {
        moveRemaining = 0f;
        moveSpeed = 0f;
        attackEndFallbackTime = 0f;
        currentAttackIndex = 0;
        hasQueuedNextAttack = false;
        isComboWindowOpen = false;
        hasStartedAttack = false;
        isRequestingAttack = false;
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

    private bool HasAttackSteps => attackSteps != null && attackSteps.Length > 0;

    private bool HasAttackStep(int attackIndex)
    {
        return HasAttackSteps &&
            attackIndex >= 0 &&
            attackIndex < attackSteps.Length &&
            attackSteps[attackIndex] != null;
    }

    private float CurrentStepDuration => HasAttackStep(0) ? attackSteps[0].Duration : 0f;
}

[Serializable]
public class DefaultAttackStep
{
    [SerializeField] private AnimationClip clip;
    [SerializeField] private float duration = 0.5f;
    [SerializeField] private DefaultAttackMovementType movementType = DefaultAttackMovementType.ScriptedForwardDistance;
    [SerializeField] private float forwardDistance = 0.5f;
    [SerializeField] private DefaultAttackRotationType rotationType = DefaultAttackRotationType.SnapOnStart;
    [SerializeField] private float trackRotationSpeed = 12f;
    [SerializeField] private ColliderInfo hitbox;
    [SerializeField] private DefaultAttackHitType hitType = DefaultAttackHitType.Overlap;
    [SerializeField] private Transform muzzle;
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private float projectileSpeed = 12f;
    [SerializeField] private float raycastRange = 20f;
    [SerializeField] private float attackDamageMultiplier = 1f;
    [SerializeField] private int flatDamageBonus;

    public AnimationClip Clip => clip;
    public float Duration => duration > 0f ? duration : ClipDuration;
    public DefaultAttackMovementType MovementType => movementType;
    public float ForwardDistance => forwardDistance;
    public DefaultAttackRotationType RotationType => rotationType;
    public float TrackRotationSpeed => trackRotationSpeed;
    public ColliderInfo Hitbox => hitbox;
    public DefaultAttackHitType HitType => hitType;
    public Transform Muzzle => muzzle;
    public GameObject ProjectilePrefab => projectilePrefab;
    public float ProjectileSpeed => projectileSpeed;
    public float RaycastRange => raycastRange;
    public float AttackDamageMultiplier => attackDamageMultiplier;
    public int FlatDamageBonus => flatDamageBonus;

    private float ClipDuration => clip != null ? clip.length : 0f;
}
