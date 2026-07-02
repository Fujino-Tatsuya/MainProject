using System;
using UnityEngine;

[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerAimIndicator))]
public class DefaultAttack : MonoBehaviour
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
    private int currentAttackIndex;
    private float moveRemaining;
    private float moveSpeed;

    public bool IsAttacking => player != null && player.CurrentState == PlayerActionState.Attack;
    public bool CanStart => HasAttackSteps &&
        aimIndicator != null &&
        aimIndicator.AimDirection.sqrMagnitude >= 0.001f &&
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
        if (IsAttacking || player == null)
            return;

        player.SetState(PlayerActionState.Attack);
    }

    public void BeginFromState()
    {
        StartAttack(0);
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
            StartAttack(nextIndex);
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

        if (animator != null)
            animator.CrossFadeInFixedTime(IdleHash, 0.05f);
    }

    private void StartAttack(int attackIndex)
    {
        if (!HasAttackStep(attackIndex))
            return;

        DefaultAttackStep step = attackSteps[attackIndex];
        Vector3 aimDirection = aimIndicator.AimDirection;
        bool shouldTriggerAttack = !IsAttacking;

        if (aimDirection.sqrMagnitude < 0.001f || step.Duration <= 0f)
            return;

        currentAttackIndex = attackIndex;
        attackDirection = aimDirection.normalized;
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
