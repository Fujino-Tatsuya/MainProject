using System;
using BaseNetCode;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

// 콤보 입력 베이스는 하나다: ComboWindowOpen~Close(또는 End) 사이에
// 버튼이 눌려 있(었)으면 다음 타가 예약된다(래치).
// 이 정책은 체인이 마지막 타까지 간 뒤의 동작만 결정한다.
public enum DefaultAttackChainPolicy
{
    // 한 바퀴 돌면 종료. 재시작하려면 버튼을 뗐다가 다시 눌러야 한다.
    Once,
    // 누르고 있는 동안 마지막 타 이후 첫 타로 순환.
    Loop
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
[RequireComponent(typeof(PlayerMotor))]
[RequireComponent(typeof(PlayerAimIndicator))]
[RequireComponent(typeof(PlayerDefaultAttack))]
public class DefaultAttackController : BaseNetworkBehaviour
{
    private static readonly int DefaultAttackHash = Animator.StringToHash("DefaultAttack");
    private static readonly int AttackIndexHash = Animator.StringToHash("AttackIndex");
    private static readonly int IdleHash = Animator.StringToHash("Idle");
    // 공격 상태 이름 컨벤션: 모든 캐릭터 컨트롤러는 Default_Attack0..N 상태를 가진다.
    // 체인 수는 attackSteps.Length가 결정하며, ValidateAttackStates에서 컨트롤러와 대조한다.
    private static int GetAttackStateHash(int index)
    {
        return Animator.StringToHash($"Default_Attack{index}");
    }

    [SerializeField] private Animator animator;
    [SerializeField] private DefaultAttackData attackData;
    [SerializeField] private PlayerDefaultAttack playerDefaultAttack;
    [SerializeField] private DefaultAttackChainPolicy chainPolicy = DefaultAttackChainPolicy.Loop;
    [SerializeField] private DefaultAttackStep[] attackSteps =
    {
        new DefaultAttackStep(),
        new DefaultAttackStep(),
        new DefaultAttackStep(),
        new DefaultAttackStep()
    };
    [SerializeField] private ColliderInfo defaultHitbox;
    [SerializeField] private LayerMask hittableLayers;
    [SerializeField] private float endFallbackPadding = 0.1f;

    private Player player;
    private PlayerStateController stateController;
    private PlayerInputReader inputReader;
    private PlayerMovement movement;
    private PlayerMotor motor;
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
    // End에서 콤보로 안 이어질 때, 입력은 바로 풀어주되(EndAttackState) 화면에 남은
    // 이 스텝의 클립은 억지로 끊지 않고 자연 재생되게 둔다. 그동안에도 루트모션은
    // 계속 캐릭터 위치에 반영해야 발이 미끄러지지 않으므로, IsAttacking이 꺼진 뒤에도
    // "아직 이 클립이 실제로 재생 중인가"를 이걸로 따로 추적한다.
    private bool isFinishingAttackTail;
    private int finishingAttackIndex = -1;

    public bool IsAttacking => player != null && player.CurrentState == PlayerActionState.Attack;
    public bool CanRequestStart => HasAttackSteps && CurrentStepDuration > 0f;
    public bool CanStartApprovedAttack => HasAttackSteps && CurrentStepDuration > 0f;
    private bool HasGameplayAuthority => !IsNetworkActive || IsServer;

    private void Awake()
    {
        player = GetComponent<Player>();
        stateController = GetComponent<PlayerStateController>();
        inputReader = GetComponent<PlayerInputReader>();
        movement = GetComponent<PlayerMovement>();
        motor = GetComponent<PlayerMotor>();
        aimIndicator = GetComponent<PlayerAimIndicator>();

        if (playerDefaultAttack == null)
            playerDefaultAttack = GetComponent<PlayerDefaultAttack>();

        if (playerDefaultAttack == null)
            playerDefaultAttack = gameObject.AddComponent<PlayerDefaultAttack>();

        if (attackData != null)
            ApplyData(attackData);
        else
            playerDefaultAttack.Configure(defaultHitbox, hittableLayers);

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator != null && !animator.TryGetComponent(out PlayerAnimationEventRelay _))
            animator.gameObject.AddComponent<PlayerAnimationEventRelay>();

        if (animator != null && !animator.TryGetComponent(out PlayerRootMotionRelay _))
            animator.gameObject.AddComponent<PlayerRootMotionRelay>();

        ValidateAttackStates();
    }

    // 데이터의 콤보 스텝 수만큼 Default_Attack0..N 상태가 컨트롤러에 있는지 검증한다.
    // 어긋나면 CrossFade가 조용히 실패하므로 초기화 시점에 에러로 드러낸다.
    private void ValidateAttackStates()
    {
        if (animator == null || animator.runtimeAnimatorController == null || attackSteps == null)
            return;

        for (int i = 0; i < attackSteps.Length; i++)
        {
            if (!animator.HasState(0, GetAttackStateHash(i)))
            {
                Debug.LogError(
                    $"[Player] Animator Controller '{animator.runtimeAnimatorController.name}'에 'Default_Attack{i}' 상태가 없습니다. " +
                    $"공격 데이터는 {attackSteps.Length}타 콤보를 요구합니다.",
                    this);
            }

            AnimationClip stepClip = attackSteps[i]?.Clip;
            if (stepClip != null && !HasComboWindowOpenEvent(stepClip))
            {
                Edit.LogWarning(
                    $"[Player] 클립 '{stepClip.name}'에 ComboWindowOpen 이벤트" +
                    $"(HandleDefaultAttackEvent, int={(int)DefaultAttackAnimationEventType.ComboWindowOpen})가 없습니다. " +
                    "윈도우가 열리지 않으면 이 스텝에서 다음 타를 예약할 수 없습니다.",
                    this);
            }
        }
    }

    private static bool HasComboWindowOpenEvent(AnimationClip clip)
    {
        foreach (AnimationEvent clipEvent in clip.events)
        {
            if (clipEvent.functionName == nameof(PlayerAnimationEventRelay.HandleDefaultAttackEvent) &&
                clipEvent.intParameter == (int)DefaultAttackAnimationEventType.ComboWindowOpen)
                return true;
        }

        return false;
    }

    public bool TryStart()
    {
        if (IsAttacking || isRequestingAttack || player == null || !CanRequestStart)
        {
            return false;
        }

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
        chainPolicy = data.ChainPolicy;
        attackSteps = data.AttackSteps;
        hittableLayers = data.HittableLayers;

        if (playerDefaultAttack != null)
            playerDefaultAttack.Configure(defaultHitbox, hittableLayers, data.MaxHitResults);

        ValidateAttackStates();
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

        ValidateAttackStates();
    }

    public void Tick()
    {
        if ((!IsNetworkActive || IsOwner) && IsAttacking)
            TryQueueNextAttackFromInput();

        if (HasGameplayAuthority && IsAttacking)
            TickServerFallbacks();

        // 회전은 렌더 틱에서 최신 에임을 따른다. 전진은 FixedTickMovement에서 별도로 제출한다.
        if (!IsNetworkActive || IsOwner)
            TickRotation();
    }

    public void FixedTickMovement()
    {
        // 전진 변위는 owner-authority NetworkTransform의 쓰기 주체만 수행한다.
        // 서버는 승인·판정·종료 장부만 관리하고 오너가 복제한 위치를 사용한다.
        if (IsNetworkActive && !IsOwner)
            return;

        TickScriptedMovement();
    }

    public void CancelCurrentAttack()
    {
        bool hadActiveAttack = IsAttacking || hasStartedAttack || isRequestingAttack;

        // EndAttackServer/EndDefaultAttackClientRpc가 player.EndAttackState()를 부르면
        // PlayerAttackState.Exit(Idle)이 여기로 다시 캐스케이드된다(Attack→다른 상태 전이는
        // 전부 이 메서드를 거치게 돼 있어서). isFinishingAttackTail이 이미 true라는 건
        // "우리가 방금 그 정상 종료 시퀀스를 스스로 시작했다"는 뜻이라, 이때는 이미
        // 클립을 자연 재생시키기로 한 결정을 이 캐스케이드가 덮어써서 강제로 Idle로
        // 끊으면 안 된다. 진짜 외부 인터럽트(넉백/구속/연출잠금/대시 등)일 때만
        // 즉시 Idle로 끊는다.
        bool isGracefulEndCascade = isFinishingAttackTail;

        ResetAttackRuntime();

        if (isGracefulEndCascade)
            return;

        isFinishingAttackTail = false;
        finishingAttackIndex = -1;

        if (hadActiveAttack && animator != null)
            animator.CrossFadeInFixedTime(IdleHash, 0.05f);

        if (hadActiveAttack && IsNetworkActive && IsServer)
            EndDefaultAttackClientRpc();
    }

    public void HandleAnimationEvent(DefaultAttackAnimationEventType eventType)
    {
        if (IsNetworkActive && !IsServer)
            return;

        if (!IsAttacking)
        {
            return;
        }

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
        // OnAnimatorMove는 Walk/Idle 중에도 매 프레임 호출되므로,
        // 공격 상태의 루트모션만 이동으로 변환한다.
        if (isFinishingAttackTail)
        {
            bool stillPlayingThisClip = animator != null
                && HasAttackStep(finishingAttackIndex)
                && animator.GetCurrentAnimatorStateInfo(0).shortNameHash == GetAttackStateHash(finishingAttackIndex);

            if (!stillPlayingThisClip)
            {
                isFinishingAttackTail = false;
                finishingAttackIndex = -1;
            }
        }

        int stepIndex = IsAttacking ? currentAttackIndex : isFinishingAttackTail ? finishingAttackIndex : -1;
        if (stepIndex < 0)
            return;

        if (!HasAttackStep(stepIndex))
            return;

        // 루트모션 변위도 일반 scripted 이동과 동일하게 오너만 적용한다.
        if (IsNetworkActive && !IsOwner)
            return;

        DefaultAttackStep step = attackSteps[stepIndex];
        if (step.MovementType != DefaultAttackMovementType.AnimationRootMotionProjected)
            return;

        animatorForward.y = 0f;
        if (animatorForward.sqrMagnitude < 0.001f)
            animatorForward = attackDirection;

        float forwardDistance = Vector3.Dot(deltaPosition, animatorForward.normalized);
        if (Mathf.Abs(forwardDistance) <= 0.0001f)
            return;

        motor.AddDisplacement(attackDirection * forwardDistance);
    }

    [Rpc(SendTo.Server)]
    private void RequestStartAttackRpc(Vector3 requestedDirection, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        bool canApprove = CanApproveServerAttack();
        if (!canApprove)
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

        if (!isComboWindowOpen)
            return;

        hasQueuedNextAttack = true;
        queuedAttackDirection = ResolveAttackDirection(requestedDirection);
    }

    [ClientRpc]
    private void PlayDefaultAttackClientRpc(int attackIndex, Vector3 direction, bool triggerAttack, float crossFadeStartTime)
    {
        if (IsServer)
            return;

        isRequestingAttack = false;

        if (player != null && !player.BeginAttackState())
        {
            ResetAttackRuntime();
            return;
        }

        StartAttackPresentation(attackIndex, direction, triggerAttack, crossFadeStartTime);
    }

    [ClientRpc]
    private void EndDefaultAttackClientRpc()
    {
        if (IsServer)
            return;

        isFinishingAttackTail = true;
        finishingAttackIndex = currentAttackIndex;

        ResetAttackRuntime();

        player?.EndAttackState();
    }

    [ClientRpc]
    private void RejectStartAttackClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (IsOwner)
            isRequestingAttack = false;
    }

    private void StartAttackServer(int attackIndex, Vector3 direction, bool triggerAttack, float crossFadeStartTime = 0f)
    {
        if (!HasAttackStep(attackIndex))
        {
            return;
        }

        DefaultAttackStep step = attackSteps[attackIndex];
        if (step.MotionDuration <= 0f)
        {
            return;
        }

        isRequestingAttack = false;
        hasStartedAttack = true;
        hasQueuedNextAttack = false;
        isComboWindowOpen = false;
        currentAttackIndex = attackIndex;
        attackDirection = ResolveAttackDirection(direction);
        queuedAttackDirection = attackDirection;
        attackEndFallbackTime = Time.time + step.MotionDuration + Mathf.Max(0f, endFallbackPadding);

        int damageSnapshot = CalculateDamageSnapshot(step);
        playerDefaultAttack.PrepareStep(step, damageSnapshot, attackDirection);

        if (player != null)
        {
            player.BeginAttackState();
        }

        StartAttackPresentation(attackIndex, attackDirection, triggerAttack, crossFadeStartTime);

        if (IsNetworkActive)
            PlayDefaultAttackClientRpc(attackIndex, attackDirection, triggerAttack, crossFadeStartTime);
    }

    private void StartAttackPresentation(int attackIndex, Vector3 direction, bool triggerAttack, float crossFadeStartTime = 0f)
    {
        if (!HasAttackStep(attackIndex))
            return;

        DefaultAttackStep step = attackSteps[attackIndex];
        currentAttackIndex = attackIndex;
        attackDirection = ResolveAttackDirection(direction);
        moveRemaining = step.MovementType == DefaultAttackMovementType.ScriptedForwardDistance
            ? Mathf.Max(step.ForwardDistance, 0f)
            : 0f;
        moveSpeed = step.MotionDuration > 0f ? moveRemaining / step.MotionDuration : 0f;

        if (step.RotationType == DefaultAttackRotationType.SnapOnStart)
            movement.RotateImmediately(attackDirection);

        player.SetAnimatorMoving(false);

        if (animator == null)
            return;

        animator.SetInteger(AttackIndexHash, currentAttackIndex);

        if (triggerAttack)
            animator.SetTrigger(DefaultAttackHash);
        else
            animator.CrossFadeInFixedTime(GetAttackStateHash(currentAttackIndex), 0.05f, -1, crossFadeStartTime);
    }

    private void CompleteCurrentAttackStep()
    {
        if (!HasGameplayAuthority || !IsAttacking || !hasStartedAttack)
            return;

        if (ShouldStartNextAttack(out int nextIndex, out Vector3 nextDirection))
        {
            // 대부분의 스텝은 LoopBackEntryTime이 0이라 처음부터 재생된다 — 체인이
            // 마지막 스텝에서 첫 스텝으로 되감길 때(0번 스텝)만 0이 아니라서 윈드업을 건너뛴다.
            StartAttackServer(nextIndex, nextDirection, false, attackSteps[nextIndex].LoopBackEntryTime);
            return;
        }

        EndAttackServer();
    }

    private bool ShouldStartNextAttack(out int nextIndex, out Vector3 nextDirection)
    {
        nextIndex = currentAttackIndex + 1;
        nextDirection = queuedAttackDirection;
        bool hasNext = nextIndex < attackSteps.Length;

        if (!hasQueuedNextAttack)
            return false;

        switch (chainPolicy)
        {
            case DefaultAttackChainPolicy.Once:
                if (!hasNext)
                    return false;
                break;

            case DefaultAttackChainPolicy.Loop:
                if (!hasNext)
                    nextIndex = 0;
                break;

            default:
                return false;
        }

        return HasAttackStep(nextIndex);
    }

    private void EndAttackServer()
    {
        isFinishingAttackTail = true;
        finishingAttackIndex = currentAttackIndex;

        ResetAttackRuntime();

        // 콤보로 안 이어질 때는 조작(이동/스킬/대시)만 바로 풀어주고, 지금 재생 중인
        // 클립은 억지로 Idle로 끊지 않는다 — 애니메이터에 이미 있는 exitTime=1 → Idle
        // 자동전이가 자연스럽게 마무리하거나, 새 입력이 들어오면 그쪽이 알아서 덮어쓴다.
        player?.EndAttackState();

        if (IsNetworkActive)
            EndDefaultAttackClientRpc();
    }

    private void TickServerFallbacks()
    {
        if (hasStartedAttack && attackEndFallbackTime > 0f && Time.time >= attackEndFallbackTime)
            CompleteCurrentAttackStep();
    }

    private void TickRotation()
    {
        if (!HasAttackStep(currentAttackIndex))
            return;

        DefaultAttackStep step = attackSteps[currentAttackIndex];

        if (step.RotationType == DefaultAttackRotationType.TrackAimDuringAttack)
            movement.RotateToward(GetCurrentAimDirection(), step.TrackRotationSpeed);
    }

    private void TickScriptedMovement()
    {
        if (!HasAttackStep(currentAttackIndex))
            return;

        DefaultAttackStep step = attackSteps[currentAttackIndex];

        if (moveRemaining <= 0f)
            return;

        float moveDistance = Mathf.Min(moveSpeed * Time.fixedDeltaTime, moveRemaining);
        moveRemaining -= moveDistance;
        motor.AddDisplacement(attackDirection * moveDistance);
    }

    private void TryQueueNextAttackFromInput()
    {
        // 콤보 윈도우 안에서 한 순간이라도 눌려 있었으면 예약되는 래치.
        // 윈도우 밖 입력은 아래 가드(오프라인)와 서버 RPC 가드에서 걸러진다.
        if (!inputReader.AttackHeld && !inputReader.AttackPressed)
            return;

        Vector3 direction = GetCurrentAimDirection();

        if (!IsNetworkActive)
        {
            if (!isComboWindowOpen)
                return;

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

        if (player == null || stateController == null || !stateController.CanAttack)
            return false;

        return true;
    }

    private int CalculateDamageSnapshot(DefaultAttackStep step)
    {
        // 상태이상 modifier가 반영된 최종 공격력 기준
        int baseDamage = player != null ? player.FinalAttackDamage : 0;
        int calculatedDamage = Mathf.RoundToInt(baseDamage * step.AttackDamageMultiplier) + step.FlatDamageBonus;

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

    private float CurrentStepDuration => HasAttackStep(0) ? attackSteps[0].MotionDuration : 0f;
}

[Serializable]
public class DefaultAttackStep
{
    [SerializeField] private AnimationClip clip;
    // 이 스텝(1타)의 모션 재생 길이. End 이벤트 누락 시 종료 fallback과
    // ScriptedForwardDistance 이동 속도 계산의 기준. 0이면 클립 길이를 사용.
    [FormerlySerializedAs("duration")]
    [SerializeField] private float motionDuration = 0;
    [SerializeField] private DefaultAttackMovementType movementType = DefaultAttackMovementType.ScriptedForwardDistance;
    [SerializeField] private float forwardDistance = 0.5f;
    [SerializeField] private DefaultAttackRotationType rotationType = DefaultAttackRotationType.SnapOnStart;
    [SerializeField] private float trackRotationSpeed = 12f;
    [SerializeField] private DefaultAttackHitType hitType = DefaultAttackHitType.Overlap;
    [SerializeField] private ColliderInfo hitbox;
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private float projectileSpeed = 12f;
    [SerializeField] private float raycastRange = 20f;
    [SerializeField] private float attackDamageMultiplier = 1f;
    [SerializeField] private int flatDamageBonus;
    // 이 스텝이 체인의 첫 스텝(0번)일 때만 의미가 있다: 마지막 스텝의 LoopCheck에서
    // 조기 루프백으로 들어올 때 처음(0초)이 아니라 여기서부터 재생해 윈드업을 건너뛴다.
    // 초 단위, 클립 원본(스케일 안 된) 타임라인 기준.
    [SerializeField] private float loopBackEntryTime = 0f;

    public AnimationClip Clip => clip;
    public float MotionDuration => motionDuration > 0f ? motionDuration : ClipDuration;
    public DefaultAttackMovementType MovementType => movementType;
    public float ForwardDistance => forwardDistance;
    public DefaultAttackRotationType RotationType => rotationType;
    public float TrackRotationSpeed => trackRotationSpeed;
    public DefaultAttackHitType HitType => hitType;
    public ColliderInfo Hitbox => hitbox;
    public GameObject ProjectilePrefab => projectilePrefab;
    public float ProjectileSpeed => projectileSpeed;
    public float RaycastRange => raycastRange;
    public float AttackDamageMultiplier => attackDamageMultiplier;
    public int FlatDamageBonus => flatDamageBonus;
    public float LoopBackEntryTime => loopBackEntryTime;

    private float ClipDuration => clip != null ? clip.length : 0f;
}
