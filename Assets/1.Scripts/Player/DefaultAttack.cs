using System;
using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerAimIndicator))]
public class DefaultAttack : BaseNetworkBehaviour
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
    [SerializeField] private DefaultAttackStep[] attackSteps =
    {
        new DefaultAttackStep(),
        new DefaultAttackStep(),
        new DefaultAttackStep(),
        new DefaultAttackStep()
    };

    private Player player;
    private PlayerInputReader inputReader;
    private PlayerMovement movement;
    private PlayerAimIndicator aimIndicator;
    private Vector3 attackDirection;
    private Vector3 approvedAttackDirection;
    private int currentAttackIndex;
    private float moveRemaining;
    private float moveSpeed;
    private bool hasApprovedAttackDirection;
    private bool isRequestingAttack;

    public bool IsAttacking => player != null && player.CurrentState == PlayerActionState.Attack;
    public bool CanRequestStart => HasAttackSteps &&
        aimIndicator != null &&
        aimIndicator.AimDirection.sqrMagnitude >= 0.001f &&
        CurrentStepDuration > 0f;
    public bool CanStartApprovedAttack => HasAttackSteps &&
        hasApprovedAttackDirection &&
        approvedAttackDirection.sqrMagnitude >= 0.001f &&
        CurrentStepDuration > 0f;

    private void Awake()
    {
        player = GetComponent<Player>();
        inputReader = GetComponent<PlayerInputReader>();
        movement = GetComponent<PlayerMovement>();
        aimIndicator = GetComponent<PlayerAimIndicator>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator != null && !animator.TryGetComponent(out PlayerAnimationEventRelay _))
            animator.gameObject.AddComponent<PlayerAnimationEventRelay>();
    }

    public void TryStart()
    {
        if (IsAttacking || isRequestingAttack || player == null || !CanRequestStart)
            return;

        Vector3 requestedDirection = aimIndicator.AimDirection.normalized;

        if (!IsNetworkActive)
        {
            ApproveLocalAttack(requestedDirection);
            return;
        }

        if (!IsOwner)
            return;

        isRequestingAttack = true;
        RequestStartAttackRpc(requestedDirection);
    }

    public void BeginFromState()
    {
        StartAttack(0, approvedAttackDirection);
        hasApprovedAttackDirection = false;
        isRequestingAttack = false;
    }

    public void Tick()
    {
        MoveDuringAttack();
    }

    public void EndCurrentAttack()
    {
        if (!IsAttacking)
            return;

        moveRemaining = 0f;

        if (inputReader.AttackHeld && HasAttackSteps)
        {
            int nextIndex = (currentAttackIndex + 1) % attackSteps.Length;
            Vector3 nextDirection = aimIndicator.AimDirection.sqrMagnitude >= 0.001f
                ? aimIndicator.AimDirection.normalized
                : attackDirection;
            StartAttack(nextIndex, nextDirection);
            return;
        }

        player.SetState(PlayerActionState.Idle);
        player.SetAnimatorMoving(false);
        currentAttackIndex = 0;

        if (animator != null)
            animator.CrossFadeInFixedTime(IdleHash, 0.05f);
    }

    public void CancelCurrentAttack()
    {
        moveRemaining = 0f;
        currentAttackIndex = 0;
        hasApprovedAttackDirection = false;
        isRequestingAttack = false;

        if (animator != null)
            animator.CrossFadeInFixedTime(IdleHash, 0.05f);
    }

    [Rpc(SendTo.Server)]
    private void RequestStartAttackRpc(Vector3 requestedDirection, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!CanApproveServerAttack(requestedDirection))
        {
            RejectStartAttackClientRpc(CreateOwnerClientRpcParams());
            return;
        }

        ApproveStartAttackClientRpc(requestedDirection.normalized, CreateOwnerClientRpcParams());
    }

    [ClientRpc]
    private void ApproveStartAttackClientRpc(Vector3 approvedDirection, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
            return;

        ApproveLocalAttack(approvedDirection);
    }

    [ClientRpc]
    private void RejectStartAttackClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (IsOwner)
            isRequestingAttack = false;
    }

    private void ApproveLocalAttack(Vector3 approvedDirection)
    {
        isRequestingAttack = false;
        approvedAttackDirection = approvedDirection.normalized;
        hasApprovedAttackDirection = true;
        player.SetState(PlayerActionState.Attack);
    }

    private bool CanApproveServerAttack(Vector3 requestedDirection)
    {
        if (!HasAttackStep(0))
            return false;

        if (requestedDirection.sqrMagnitude < 0.001f)
            return false;

        if (player == null || player.CurrentState == PlayerActionState.Dead)
            return false;

        StatusEffectController statusEffects = GetComponent<StatusEffectController>();
        return statusEffects == null || !statusEffects.BlocksAttack;
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

    private void StartAttack(int attackIndex, Vector3 direction)
    {
        if (!HasAttackStep(attackIndex))
            return;

        DefaultAttackStep step = attackSteps[attackIndex];
        bool shouldTriggerAttack = !IsAttacking;

        if (direction.sqrMagnitude < 0.001f || step.Duration <= 0f)
            return;

        currentAttackIndex = attackIndex;
        attackDirection = direction.normalized;
        moveRemaining = Mathf.Max(step.ForwardDistance, 0f);
        moveSpeed = moveRemaining / step.Duration;

        movement.RotateImmediately(attackDirection);
        player.SetAnimatorMoving(false);

        if (animator != null)
        {
            animator.SetInteger(AttackIndexHash, currentAttackIndex);

            if (shouldTriggerAttack)
            {
                animator.SetTrigger(DefaultAttackHash);
            }
            else if (currentAttackIndex < AttackStateHashes.Length)
            {
                animator.CrossFadeInFixedTime(AttackStateHashes[currentAttackIndex], 0.05f);
            }
        }
    }

    private void MoveDuringAttack()
    {
        if (moveRemaining <= 0f)
            return;

        float moveDistance = Mathf.Min(moveSpeed * Time.deltaTime, moveRemaining);
        moveRemaining -= moveDistance;
        movement.MoveRoot(attackDirection * moveDistance);
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
    [SerializeField] private float forwardDistance = 0.5f;

    public AnimationClip Clip => clip;
    public float Duration => duration > 0f ? duration : ClipDuration;
    public float ForwardDistance => forwardDistance;

    private float ClipDuration => clip != null ? clip.length : 0f;
}
