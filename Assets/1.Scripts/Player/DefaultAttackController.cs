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

    // ─── 평타 베기 연출 ────────────────────────────────────────────────
    // 🔴 명중과 헛스윙을 **가른다.** 애니메이션 이벤트로는 못 가른다 — 클립은 맞았는지 모른다.
    //    판정은 서버만 하므로(HandleAnimationEvent 가 IsServer 로 막혀 있다) RPC 로 내보낸다.
    //    소켓이 필요해서 카탈로그 룩업이 아니라 EffectSocketPlayer 직접 참조다(보스 연출과 같은 규약).
    // 🔴 **타마다 소켓이 다르다**(2026-09-16). 4타는 스윙 궤적이 전부 달라서 검기가 나가는
    //    자리·각도도 다르다. 그래서 칼에 소켓 하나를 붙여 공용으로 쓰던 방식을 버리고,
    //    플레이어 루트 아래 `Slash01~04` 를 두고 **공격 단계로 인덱싱**한다.
    //    배열 첨자 = Default_Attack{N} 의 N 이다 — 순서를 섞지 말 것.
    [Header("연출 — 평타 (첨자 = 공격 단계)")]
    [Tooltip("각 타 명중 시. [0~2]=FX_SingleSlash_O · [3]=FX_DoubleSlash_O.\n" +
             "비워두면 그 타의 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer[] slashHit = new EffectSocketPlayer[4];
    [Tooltip("각 타 헛스윙 시. [0~2]=FX_SingleSlash_X · [3]=FX_DoubleSlash_X")]
    [SerializeField] private EffectSocketPlayer[] slashMiss = new EffectSocketPlayer[4];

    private bool warnedNoSlashVfx;

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

        // 전진·회전 변위는 owner-authority NetworkTransform의 쓰기 주체만 수행한다.
        // 서버는 승인·판정·종료 장부만 관리하고 오너가 복제한 위치를 사용한다.
        if (!IsNetworkActive || IsOwner)
            TickMovementAndRotation();
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
                FireHitAndPlayVfx();
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
            FireHitAndPlayVfx();
    }

    /// <summary>
    /// 판정 1회 + 그 결과에 맞는 베기 연출. 판정 지점이 둘(<see cref="HandleAnimationEvent"/> ·
    /// <see cref="HitCurrentAttack"/>)이라 한 곳으로 모은다 — 갈라 두면 한쪽만 고쳐 어긋난다.
    /// </summary>
    private void FireHitAndPlayVfx()
    {
        bool hit = playerDefaultAttack.HitCurrentStep();

        // 🔴 여기서 직접 재생하면 안 된다. 이 경로는 서버에서만 돈다 — 호스트 화면에만 나온다.
        if (IsNetworkActive)
            PlaySlashVfxRpc(currentAttackIndex, hit);
        else
            PlaySlashVfx(currentAttackIndex, hit);   // 오프라인(단독 실행) 경로
    }

    /// <summary>
    /// [전 피어] 평타 베기 연출. 어느 단계였는지와 맞췄는지를 실어 보낸다.
    ///
    /// 둘 다 인자로 싣는 이유는 같다 — <b>클라가 스스로 알 수 없다.</b> 명중 판정은 서버 전용이고,
    /// <see cref="currentAttackIndex"/> 도 클라에서는 RPC 로 받은 값이라 판정 시점과 어긋날 수 있다.
    ///
    /// Unreliable: 순수 연출이라 한 대 분이 빠져도 상태가 발산하지 않는다.
    /// </summary>
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    private void PlaySlashVfxRpc(int attackIndex, bool hit) => PlaySlashVfx(attackIndex, hit);

    private void PlaySlashVfx(int attackIndex, bool hit)
    {
        EffectSocketPlayer[] players = hit ? slashHit : slashMiss;

        // 배열이 콤보 길이보다 짧게 저작돼 있어도 조용히 빠진다 — 연출이 없는 것이
        // 전투가 예외로 멈추는 것보다 낫다. 대신 아래 경고가 어느 첨자인지 짚어 준다.
        if (players == null || attackIndex < 0 || attackIndex >= players.Length)
        {
            WarnNoSlashVfxOnce(attackIndex, hit);
            return;
        }

        EffectSocketPlayer player = players[attackIndex];
        if (player == null)
        {
            WarnNoSlashVfxOnce(attackIndex, hit);
            return;
        }

        player.PlayOnce();
    }

    // 비어 있으면 평타마다 로그가 쏟아지므로 한 번만 알린다(연출은 빠지되 전투는 계속된다).
    private void WarnNoSlashVfxOnce(int attackIndex, bool hit)
    {
        if (warnedNoSlashVfx)
            return;

        warnedNoSlashVfx = true;

        string slot = hit ? "slashHit" : "slashMiss";
        Debug.LogWarning(
            $"{name}: {attackIndex}타 평타 연출이 비어 있다 — 프리팹의 DefaultAttackController 에 " +
            $"{slot}[{attackIndex}] 을 물릴 것.", this);
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

        movement.MoveRoot(attackDirection * forwardDistance);
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
