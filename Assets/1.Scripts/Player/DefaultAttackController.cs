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

// 스텝 End 에서 다음 타를 고르는 순수 규칙(할 일 122 — EditMode 로 고정). 예약(창 안 래치)이 없으면 언제나 종료다.
public static class DefaultAttackChainRules
{
    public static bool TryResolveNextStep(int currentIndex, int stepCount, DefaultAttackChainPolicy policy, bool queued,
        out int nextIndex)
    {
        nextIndex = currentIndex + 1;
        if (!queued || stepCount <= 0)
            return false;

        if (nextIndex < stepCount)
            return true;

        switch (policy)
        {
            case DefaultAttackChainPolicy.Loop:
                nextIndex = 0;
                return true;

            default:
                return false;
        }
    }

    // 되감기 진입 시각(클립 타임라인 초) → CrossFadeInFixedTime 의 오프셋(상태 시간 초). 콤보 상태는 Speed Multiplier =
    // AttackSpeed 파라미터라 엔진이 오프셋에 속도를 곱해 클립 시각으로 바꾼다 — 그대로 넘기면 공속이 오를수록 더 깊이 들어가
    // 제압 가속(1.2 × 2) 때 팔라딘 1타 창 열림(0.23)을 건너뛰고 홀드 순환이 끊겼다(할 일 122 후속). 각 피어가 자기 AttackSpeed 로 나눈다.
    public static float LoopBackFixedTimeOffset(float clipTime, float attackSpeed) =>
        attackSpeed > 0f ? clipTime / attackSpeed : clipTime;
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
public class DefaultAttackController : BaseNetworkBehaviour, IPlayerBasicAttack, IAnimationEventRequirementSource
{
    private static readonly int DefaultAttackHash = Animator.StringToHash("DefaultAttack");
    private static readonly int AttackIndexHash = Animator.StringToHash("AttackIndex");
    private static readonly int AttackSpeedHash = Animator.StringToHash("AttackSpeed");
    private static readonly int IdleHash = Animator.StringToHash("Idle");
    private static readonly int MoveHash = Animator.StringToHash("Walk");
    private const float TailToMovementBlendSeconds = 0.05f;
    // 공격 상태 이름 컨벤션: 모든 캐릭터 컨트롤러는 Default_Attack0..N 상태를 가진다.
    // 체인 수는 attackSteps.Length가 결정하며, ValidateAttackStates에서 컨트롤러와 대조한다.
    private static int GetAttackStateHash(int index)
    {
        return Animator.StringToHash($"Default_Attack{index}");
    }

    // ─── 평타 베기 연출 ────────────────────────────────────────────────
    // 🔴 명중과 헛스윙을 **가른다.** 애니메이션 이벤트로는 못 가른다 — 클립은 맞았는지 모른다.
    //    판정은 서버만 하므로(오너 판정 요청 RequestHitRpc → 서버 판정, 할 일 256) RPC 로 내보낸다.
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

    [Header("공격 꼬리 → 이동 전환")]
    [Tooltip("공격이 끝나고 남은 클립을 이동 입력으로 끊을 때의 블렌드 시간(초). 짧으면 툭 끊기고 길면 공격 포즈가 남아 보인다.")]
    [SerializeField, Min(0f)] private float tailToMovementBlendSeconds = 0.12f;
    [SerializeField] private DefaultAttackChainPolicy chainPolicy = DefaultAttackChainPolicy.Loop;
    // 단계는 데이터(attackData → ApplyData)에서만 온다 — 컴포넌트에 직렬화하지 않는다(할 일 153: 단계의 판정 도형이
    // [SerializeReference] 라 컴포넌트에 두면 프리팹 임포트마다 직렬화 레이아웃 오류가 났다). 아래는 데이터 없을 때의 기본값.
    [NonSerialized] private DefaultAttackStep[] attackSteps =
    {
        new DefaultAttackStep(),
        new DefaultAttackStep(),
        new DefaultAttackStep(),
        new DefaultAttackStep()
    };
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
    private bool isComboWindowOpen;
    private bool hasQueuedNextAttack;
    // 스킬 입력 때문에 지금 타로 마무리 중(할 일 77 후속) — 오너가 들고(체인은 오너가 주도, 할 일 256), 공격이 끝나면(ResetAttackRuntime) 풀린다.
    private bool isFinishingChain;
    private bool hasStartedAttack;
    // End에서 콤보로 안 이어질 때, 입력은 바로 풀어주되(EndAttackState) 화면에 남은
    // 이 스텝의 클립은 억지로 끊지 않고 자연 재생되게 둔다. 그동안에도 루트모션은
    // 계속 캐릭터 위치에 반영해야 발이 미끄러지지 않으므로, IsAttacking이 꺼진 뒤에도
    // "아직 이 클립이 실제로 재생 중인가"를 이걸로 따로 추적한다.
    private bool isFinishingAttackTail;
    private int finishingAttackIndex = -1;
    private int[] attackStateHashes = Array.Empty<int>();
    // 공격과 다른 상태를 섞으면 자세 차이도 deltaPosition에 들어온다.
    // 전환이 끝난 첫 평가에도 그 변위가 남으므로 직전 평가를 함께 기억한다.
    private bool wasBlendingWithNonAttackState;
    private bool hasObservedAnimationState;
    private bool wasInAttackAnimationState;
    // 다 돈 뒤 공격(할 일 43): SnapOnStart 단이 크게 꺾으면 TurnToward 가 도는 동안 클립 시작을 늦춘다.
    // 판정·콤보 창·End 는 클립 이벤트라 같이 늦춰지고, 전진은 아래 플래그로 막는다.
    // 지연은 오너가 정해(할 일 256 — 서버 상한 자르기 없음, 길면 보낸 쪽만 손해) 타 시작 통지 → PlayDefaultAttackRpc 로 다른 피어에 싣는다.
    private bool hasPendingAttackClip;
    private float pendingAttackClipTime;
    private bool pendingTriggerAttack;
    private float pendingCrossFadeStartTime;

    // ─── 오너 예측(할 일 256) ──────────────────────────────────────────
    // 오너 = 체인 주도(창·다음 타·End). 타마다 일련번호를 매겨 서버에 시작·판정·종료를 알린다.
    private byte swingSerial;
    private byte comboStartSerial;
    private int hitNumber;
    // 서버(원격 오너를 따라가는 쪽) — 따라가는 휘두름 번호·2차 검증·끊은 콤보의 뒤늦은 이어짐 무시·통지가 끊겼을 때의 안전망.
    private byte serverSwingSerial;
    private bool serverIgnoreContinues;
    private float serverFollowFallbackTime;
    private readonly BasicAttackServerVerifier verifier = new BasicAttackServerVerifier();
    private BasicAttackStepTiming[] stepTimings;
    // 서버 안전망 — 오너 End 시각 + 이만큼 지나도 종료 통지가 없으면 서버가 혼자 끝낸다(오너 끊김·패킷 유실).
    private const float ServerFollowGraceSeconds = 1f;
    // 판정 요청의 오너 위치가 서버가 보는 위치(NT)와 이만큼(수평 m) 넘게 다르면 경고만 남긴다.
    private const float OwnerPositionWarnDistance = 2f;

    public bool IsAttacking => player != null && player.CurrentState == PlayerActionState.Attack;
    // 판정 단계(읽기 전용) — 특성 랩 공격 범위 기즈모(할 일 152)가 단계의 판정 도형(HitArea)을 그린다.
    public System.Collections.Generic.IReadOnlyList<DefaultAttackStep> AttackSteps => attackSteps;

    public bool CanRequestStart => HasAttackSteps && CurrentStepDuration > 0f;
    public bool CanStartApprovedAttack => HasAttackSteps && CurrentStepDuration > 0f;
    // 콤보 중 언제든 대시로 끊는다(할 일 9). 판정이 나갔으면 피해는 그대로, 남은 동작·예약만 끊긴다(CancelCurrentAttack).
    public bool CanBeCanceledByDash => true;
    // 콤보는 누른 프레임으로만 시작하고, 공격 중 스킬 입력은 선입력 버퍼(0.2초) 그대로다(할 일 44 — 거너만 다름).
    public bool StartsOnHold => false;
    public bool ReservesSkillAfterAttack => false;

    // 체인(창·다음 타·End)을 주도하는 쪽 — 오너(호스트 오너 포함)·오프라인. 서버(원격 오너)·관전자는 통지를 따라간다.
    private bool IsChainAuthority => !IsNetworkActive || IsOwner;
    // 서버 처리(데미지 스냅샷·판정·튜토리얼 훅)를 이 인스턴스가 직접 하는가 — 호스트·오프라인.
    private bool HasServerAuthority => !IsNetworkActive || IsServer;

    // 버퍼의 스킬 입력 때문에 다음 타를 잇지 않는다(할 일 77 후속) — 체인은 오너가 주도하므로(할 일 256) 오너 래치만 세운다.
    // 이 타는 End 이벤트에서 기존 정상 종료로 끝난다.
    public void FinishWithoutChaining()
    {
        if (!IsAttacking || isFinishingChain || !IsChainAuthority)
            return;

        isFinishingChain = true;
        hasQueuedNextAttack = false;
    }

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
            playerDefaultAttack.Configure(hittableLayers);

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
        int attackStateCount = attackSteps?.Length ?? 0;
        if (attackStateHashes.Length != attackStateCount)
        {
            attackStateHashes = new int[attackStateCount];
            for (int i = 0; i < attackStateCount; i++)
                attackStateHashes[i] = GetAttackStateHash(i);
        }

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

        }

        // 판정·입력 창·종료는 스텝 클립 이벤트다 — ComboWindowOpen 이 없으면 이 스텝에서 다음 타를 예약할 수 없다.
        AnimationEventRequirements.WarnMissing(this, animator, this);
    }

    // 스텝마다 Hit·ComboWindowOpen·End. Close 는 없으면 End 가 창을 닫으므로 요구하지 않는다.
    public void CollectAnimationEventRequirements(System.Collections.Generic.List<AnimationEventRequirement> requirements)
    {
        if (attackSteps == null)
            return;

        foreach (DefaultAttackStep step in attackSteps)
        {
            AnimationClip clip = step?.Clip;
            if (clip == null)
                continue;

            requirements.Add(AnimationEventRequirement.ForAttackClip(clip, DefaultAttackAnimationEventType.Hit));
            requirements.Add(AnimationEventRequirement.ForAttackClip(clip, DefaultAttackAnimationEventType.ComboWindowOpen));
            requirements.Add(AnimationEventRequirement.ForAttackClip(clip, DefaultAttackAnimationEventType.End));
        }
    }

    /// <summary>
    /// 서버 2차 검증용 타별 클립 이벤트 시각(공속 1 기준 — 콤보 상태 Speed Multiplier 는 AttackSpeed 파라미터뿐이라 상태 속도 1).
    /// 루프백 진입은 단 데이터 LoopBackEntryTime. 데이터가 바뀌면(ApplyData) 다시 만든다.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<BasicAttackStepTiming> StepTimings
    {
        get
        {
            if (stepTimings == null || stepTimings.Length != (attackSteps?.Length ?? 0))
            {
                int count = attackSteps?.Length ?? 0;
                stepTimings = new BasicAttackStepTiming[count];
                for (int i = 0; i < count; i++)
                {
                    DefaultAttackStep step = attackSteps[i];
                    stepTimings[i] = BasicAttackStepTiming.FromClip(step?.Clip, nameof(PlayerAnimationEventRelay.HandleDefaultAttackEvent),
                        (int)DefaultAttackAnimationEventType.Hit, (int)DefaultAttackAnimationEventType.End, 1f,
                        step != null ? step.LoopBackEntryTime : 0f);
                }
            }

            return stepTimings;
        }
    }

    public bool IsOutOfResource => false;

    // 오너 1차 판단 후 즉시 재생(할 일 256) — 서버 승인을 기다리지 않는다. 서버는 시작 통지를 받고 상태만 따라간다.
    public bool TryStart()
    {
        if (IsAttacking || player == null || !CanRequestStart || !IsChainAuthority)
            return false;

        if (stateController != null && !stateController.CanAttack)
            return false;

        Vector3 requestedDirection = GetCurrentAimDirection();
        // 회전 지연은 오너의 몸 방향으로 잰다(오너가 정한다 — 할 일 256).
        float turnDelay = GetTurnDelay(0, requestedDirection);
        return StartSwingAsChainAuthority(0, requestedDirection, true, 0f, turnDelay, false);
    }

    private void Update()
    {
        if (hasPendingAttackClip && Time.time >= pendingAttackClipTime)
        {
            hasPendingAttackClip = false;
            PlayAttackClip(pendingTriggerAttack, pendingCrossFadeStartTime);
        }
    }

    public void BeginFromState()
    {
        // 공격 상태 진입은 StartSwingAsChainAuthority(오너)·NotifySwingStartRpc(서버)·PlayDefaultAttackRpc(관전자)가 연다.
    }

    public void ApplyData(DefaultAttackData data)
    {
        if (data == null)
            return;

        attackData = data;
        chainPolicy = data.ChainPolicy;
        attackSteps = data.AttackSteps;
        hittableLayers = data.HittableLayers;
        stepTimings = null;

        if (playerDefaultAttack != null)
            playerDefaultAttack.Configure(hittableLayers, data.MaxHitResults);

        ValidateAttackStates();
    }

    public void SetAnimator(Animator newAnimator)
    {
        if (newAnimator == null)
            return;

        animator = newAnimator;
        wasBlendingWithNonAttackState = false;
        hasObservedAnimationState = false;
        wasInAttackAnimationState = false;

        if (!animator.TryGetComponent(out PlayerAnimationEventRelay _))
            animator.gameObject.AddComponent<PlayerAnimationEventRelay>();

        if (!animator.TryGetComponent(out PlayerRootMotionRelay _))
            animator.gameObject.AddComponent<PlayerRootMotionRelay>();

        ValidateAttackStates();
    }

    public void Tick()
    {
        if (IsChainAuthority)
        {
            if (IsAttacking)
            {
                TryQueueNextAttackFromInput();
                TickChainFallback();
            }

            // 회전은 렌더 틱에서 최신 에임을 따른다. 전진은 FixedTickMovement에서 별도로 제출한다.
            TickRotation();
            return;
        }

        if (IsNetworkActive && IsServer && IsAttacking)
            TickServerFollowFallback();
    }

    public void FixedTickMovement()
    {
        // 오너는 예측용, 서버는 권위 시뮬레이션용으로 같은 전진 의도를 만든다.
        // 원격 프록시는 NetworkTransform 표시만 하므로 제출하지 않는다.
        if (player == null || !player.IsSimulating)
            return;

        TickScriptedMovement();
    }

    public void CancelCurrentAttack()
    {
        bool hadActiveAttack = IsAttacking || hasStartedAttack;

        // 정상 종료(EndAttackAsChainAuthority·EndAttackFollowServer·EndDefaultAttackRpc)가 player.EndAttackState()를 부르면
        // PlayerAttackState.Exit(Idle)이 여기로 다시 캐스케이드된다(Attack→다른 상태 전이는
        // 전부 이 메서드를 거치게 돼 있어서). isFinishingAttackTail이 이미 true라는 건
        // "우리가 방금 그 정상 종료 시퀀스를 스스로 시작했다"는 뜻이라, 이때는 이미
        // 클립을 자연 재생시키기로 한 결정을 이 캐스케이드가 덮어써서 강제로 Idle로
        // 끊으면 안 된다. 진짜 외부 인터럽트(넉백/구속/연출잠금/대시 등)일 때만
        // 즉시 Idle로 끊는다.
        bool isGracefulEndCascade = isFinishingAttackTail;
        byte ownerSerial = swingSerial;
        byte followedSerial = serverSwingSerial;

        ResetAttackRuntime();

        if (isGracefulEndCascade)
            return;

        isFinishingAttackTail = false;
        finishingAttackIndex = -1;

        if (!hadActiveAttack)
            return;

        if (animator != null)
            animator.CrossFadeInFixedTime(IdleHash, 0.05f);

        if (!IsNetworkActive)
            return;

        if (IsServer)
        {
            // 서버가 끊는 경로(넉백·구속·연출 잠금·숨김·대시) — 오너(원격이면)·관전자 모두 즉시 멈춘다.
            verifier.EndCombo();
            if (!IsOwner)
                serverIgnoreContinues = true;
            CancelAttackRpc(IsOwner ? ownerSerial : followedSerial);
        }
        else if (IsOwner)
        {
            // 원격 오너가 스스로 끊었다(대시 등) — 서버가 따라 끝낸다(이미 끊었으면 무시).
            NotifyAttackEndRpc(ownerSerial);
        }
    }

    /// <summary>
    /// 공격 종료 후 남은 클립(꼬리)을 **이동 입력 때문에** 끊는다.
    ///
    /// 왜 <see cref="CancelCurrentAttack"/> 로 안 되는가: 그쪽은 정상 종료 캐스케이드일 때
    /// (isFinishingAttackTail == true) **일부러 끊지 않고 그대로 돌려보낸다.** 공격이 스스로 끝나며
    /// 클립을 자연 재생하기로 한 결정을 같은 시퀀스가 도로 덮는 것을 막기 위한 가드다.
    /// 여기서는 반대로 **끊는 것이 목적**이라 별도 경로가 필요하다.
    ///
    /// 애니메이터 쪽 사정: 공격 상태에서 나가는 전환은 "다음 공격(AttackIndex)"과
    /// "ExitTime 1.0 → Idle" 둘뿐이고 IsMoving 조건이 없다. 그래서 이동 입력이 들어와도
    /// 클립이 끝날 때까지 이동 애니메이션으로 못 넘어간다. 코드에서 직접 빠져나온다.
    ///
    /// 🔴 플래그를 같이 내리는 것이 중요하다 — isFinishingAttackTail 이 남아 있으면
    /// <see cref="HandleAnimatorMove"/> 가 걷는 동안에도 공격 전진 변위를 계속 제출한다.
    /// </summary>
    public void CancelFinishingTailForMovement()
    {
        if (!isFinishingAttackTail)
            return;

        isFinishingAttackTail = false;
        finishingAttackIndex = -1;

        // 🔴 Idle 이 아니라 **Walk 로 바로** 넘긴다.
        // Idle 로 넘기면 공격 → Idle → Walk 로 두 번 전환되고, 이동 중인데 Idle 포즈를 스쳐
        // 눈에 띄게 부자연스럽다(2026-09-16 은희 체감). Walk 의 유일한 전환은
        // "IsMoving == false → Idle" 이므로 직접 넘겨도 멈추면 알아서 Idle 로 돌아간다.
        if (animator != null)
            animator.CrossFadeInFixedTime(MoveHash, tailToMovementBlendSeconds);
    }

    // 애니 이벤트는 체인 주도 쪽(오너·오프라인)만 쓴다(할 일 256). 서버(원격 오너)·관전자의 애니메이터도 같은 클립을 돌려
    // 이벤트가 늦게 또 터지므로 무시한다 — 서버 판정은 오너 판정 요청(RequestHitRpc)으로만 난다.
    public void HandleAnimationEvent(DefaultAttackAnimationEventType eventType)
    {
        if (!IsChainAuthority)
            return;

        // 회전 대기 중 이벤트는 이 단이 아니라 직전 클립의 것이다.
        if (!IsAttacking || hasPendingAttackClip)
        {
            return;
        }

        switch (eventType)
        {
            case DefaultAttackAnimationEventType.Hit:
                FireOwnerHit();
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
        if (IsChainAuthority)
            CompleteCurrentAttackStep();
    }

    public void HitCurrentAttack()
    {
        if (IsChainAuthority && IsAttacking)
            FireOwnerHit();
    }

    // [체인 주도] Hit 시점 — 호스트·오프라인은 바로 판정, 원격 오너는 자기 위치·방향으로 판정 요청(Reliable).
    private void FireOwnerHit()
    {
        if (HasServerAuthority)
            FireHitAndPlayVfx(transform.position, attackDirection);
        else
            RequestHitRpc(swingSerial, (byte)currentAttackIndex, (byte)Mathf.Clamp(hitNumber, 0, 255), transform.position, attackDirection);

        hitNumber++;
    }

    /// <summary>
    /// [서버] 판정 1회 + 그 결과에 맞는 베기 연출. 판정 지점이 셋(애니 이벤트 · 구형 경로 · 오너 판정 요청)이라 한 곳으로 모은다.
    /// </summary>
    private void FireHitAndPlayVfx(Vector3 casterPosition, Vector3 direction)
    {
        bool hit = playerDefaultAttack.HitCurrentStep(casterPosition, direction);
        PlaySlashVfxAllPeers(currentAttackIndex, hit);
    }

    private void PlaySlashVfxAllPeers(int attackIndex, bool hit)
    {
        // 🔴 여기서 직접 재생하면 안 된다. 이 경로는 서버에서만 돈다 — 호스트 화면에만 나온다.
        if (IsNetworkActive)
            PlaySlashVfxRpc(attackIndex, hit);
        else
            PlaySlashVfx(attackIndex, hit);   // 오프라인(단독 실행) 경로
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
        // 공격 중이 아닐 때도 갱신해야 다음 공격에 이전 전환 기록이 남지 않는다.
        bool suppressTransitionMotion = UpdateRootMotionTransitionGuard();

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
        if (stepIndex < 0 || hasPendingAttackClip)
            return;

        if (!HasAttackStep(stepIndex))
            return;

        // 오너 예측뿐 아니라 서버 권위 시뮬레이션에도 같은 루트모션 의도가 필요하다.
        // 원격 프록시는 NetworkTransform 표시만 하므로 제출하지 않는다.
        if (player == null || !player.IsSimulating)
            return;

        DefaultAttackStep step = attackSteps[stepIndex];
        if (step.MovementType != DefaultAttackMovementType.AnimationRootMotionProjected)
            return;

        if (suppressTransitionMotion)
            return;

        animatorForward.y = 0f;
        if (animatorForward.sqrMagnitude < 0.001f)
            animatorForward = attackDirection;

        float forwardDistance = Vector3.Dot(deltaPosition, animatorForward.normalized);
        if (Mathf.Abs(forwardDistance) <= 0.0001f)
            return;

        motor.AddDisplacement(attackDirection * forwardDistance);
    }

    private bool UpdateRootMotionTransitionGuard()
    {
        bool hasAnimationState = animator != null && animator.runtimeAnimatorController != null;
        bool isAttackState = hasAnimationState
            && IsAttackAnimationState(animator.GetCurrentAnimatorStateInfo(0).shortNameHash);
        bool isBlendingWithNonAttackState = hasAnimationState && animator.IsInTransition(0)
            && (!isAttackState || !IsAttackAnimationState(animator.GetNextAnimatorStateInfo(0).shortNameHash));
        // 낮은 프레임률에서 전환 전체가 한 평가 안에 끝나도 공격/비공격 경계는 잡는다.
        bool crossedAttackBoundary = hasAnimationState && hasObservedAnimationState
            && wasInAttackAnimationState != isAttackState;

        bool suppress = isBlendingWithNonAttackState || wasBlendingWithNonAttackState
            || crossedAttackBoundary;
        wasBlendingWithNonAttackState = isBlendingWithNonAttackState;
        wasInAttackAnimationState = isAttackState;
        hasObservedAnimationState = hasAnimationState;
        return suppress;
    }

    private bool IsAttackAnimationState(int stateHash)
    {
        for (int i = 0; i < attackStateHashes.Length; i++)
        {
            if (attackStateHashes[i] == stateHash)
                return true;
        }

        return false;
    }

    // ─── 체인 주도(오너·오프라인) ─────────────────────────────────────

    /// <summary>
    /// [체인 주도] 한 타를 지금 시작한다 — 자기 상태·연출을 바로 열고, 호스트·오프라인이면 서버 처리도 여기서, 원격 오너면 시작 통지.
    /// <paramref name="continuesCombo"/> = 직전 타의 창 안 입력으로 이은 타(서버 순서 검증: 직전 +1·루프 0).
    /// </summary>
    private bool StartSwingAsChainAuthority(int attackIndex, Vector3 direction, bool triggerAttack, float crossFadeStartTime,
        float turnDelay, bool continuesCombo)
    {
        if (!HasAttackStep(attackIndex) || attackSteps[attackIndex].MotionDuration <= 0f)
            return false;

        if (player != null && !player.BeginAttackState())
            return false;

        swingSerial++;
        if (!continuesCombo)
            comboStartSerial = swingSerial;

        BeginSwingRuntime(attackIndex, direction, turnDelay);

        if (HasServerAuthority)
            ServerOnSwingStarted(swingSerial, attackIndex, triggerAttack, crossFadeStartTime, turnDelay, continuesCombo);

        StartAttackPresentation(attackIndex, attackDirection, triggerAttack, crossFadeStartTime, turnDelay);

        if (IsNetworkActive && !IsServer)
            NotifySwingStartRpc(swingSerial, (byte)attackIndex, attackDirection, triggerAttack, crossFadeStartTime, turnDelay,
                continuesCombo, player.CreateStatReport());

        return true;
    }

    private void CompleteCurrentAttackStep()
    {
        if (!IsChainAuthority || !IsAttacking || !hasStartedAttack)
            return;

        if (ShouldStartNextAttack(out int nextIndex, out Vector3 nextDirection))
        {
            // 대부분의 스텝은 LoopBackEntryTime이 0이라 처음부터 재생된다 — 체인이
            // 마지막 스텝에서 첫 스텝으로 되감길 때(0번 스텝)만 0이 아니라서 윈드업을 건너뛴다.
            // 루프백 오프셋은 PlayDefaultAttackRpc 로 다른 피어도 같은 클립 시각에서 들어간다(각자 자기 AttackSpeed 로 나눔).
            StartSwingAsChainAuthority(nextIndex, nextDirection, false, attackSteps[nextIndex].LoopBackEntryTime,
                GetTurnDelay(nextIndex, nextDirection), true);
            return;
        }

        EndAttackAsChainAuthority();
    }

    private bool ShouldStartNextAttack(out int nextIndex, out Vector3 nextDirection)
    {
        nextDirection = queuedAttackDirection;

        if (!DefaultAttackChainRules.TryResolveNextStep(
                currentAttackIndex, attackSteps.Length, chainPolicy, hasQueuedNextAttack, out nextIndex))
            return false;

        return HasAttackStep(nextIndex);
    }

    private void EndAttackAsChainAuthority()
    {
        byte serial = swingSerial;
        isFinishingAttackTail = true;
        finishingAttackIndex = currentAttackIndex;

        ResetAttackRuntime();
        if (HasServerAuthority)
            verifier.EndCombo();

        // 콤보로 안 이어질 때는 조작(이동/스킬/대시)만 바로 풀어주고, 지금 재생 중인
        // 클립은 억지로 Idle로 끊지 않는다 — 애니메이터에 이미 있는 exitTime=1 → Idle
        // 자동전이가 자연스럽게 마무리하거나, 새 입력이 들어오면 그쪽이 알아서 덮어쓴다.
        player?.EndAttackState();

        if (!IsNetworkActive)
            return;

        if (IsServer)
            EndDefaultAttackRpc();
        else
            NotifyAttackEndRpc(serial);
    }

    private void TickChainFallback()
    {
        // End 이벤트가 빠졌을 때의 안전망 — 체인 주도 쪽이 스스로 이 타를 마무리한다.
        if (hasStartedAttack && attackEndFallbackTime > 0f && Time.time >= attackEndFallbackTime)
            CompleteCurrentAttackStep();
    }

    private void TryQueueNextAttackFromInput()
    {
        // 콤보 윈도우 안에서 한 순간이라도 눌려 있었으면 예약되는 래치. 창은 오너 자기 애니 이벤트다(할 일 256) —
        // 서버 왕복이 없어 창 끝 탭도 오너 화면 그대로 이어진다.
        // 스킬 입력으로 마무리 중이면 누르고 있어도 잇지 않는다(할 일 77 후속, FinishWithoutChaining).
        if (!PlayerBasicAttackInputRules.ShouldRequestNextMeleeAttack(
                inputReader.AttackHeld, inputReader.AttackPressed, isFinishingChain))
            return;

        if (!isComboWindowOpen)
            return;

        hasQueuedNextAttack = true;
        queuedAttackDirection = GetCurrentAimDirection();
        // 예약에 쓴 입력은 선입력 버퍼에서 지운다(할 일 44). 창 밖 입력은 남겨 공격이 끝난 뒤 새 평타로 실행한다.
        inputReader.ConsumeBufferedInput(PlayerBufferedInput.Attack);
    }

    // ─── 서버 ────────────────────────────────────────────────────────

    /// <summary>
    /// [서버] 한 타가 시작됐다(호스트 오너는 직접, 원격 오너는 시작 통지로) — 2차 검증 상태·데미지 스냅샷·튜토리얼 훅,
    /// 그리고 오너를 뺀 피어에 기존 재생 RPC 로 전달(NetworkAnimator 에 기대지 않는다).
    /// </summary>
    private void ServerOnSwingStarted(byte serial, int attackIndex, bool triggerAttack, float crossFadeStartTime, float turnDelay,
        bool continuesCombo)
    {
        DefaultAttackStep step = attackSteps[attackIndex];
        verifier.RecordAttackSpeed(Time.unscaledTimeAsDouble, AttackSpeed);
        verifier.BeginSwing(serial, attackIndex, attackSteps.Length, chainPolicy == DefaultAttackChainPolicy.Loop, continuesCombo);

        int damageSnapshot = CalculateDamageSnapshot(step);
        playerDefaultAttack.PrepareStep(step, damageSnapshot, attackDirection);
        player?.RaiseServerBasicAttackStarted();   // 튜토리얼 훅 — 한 타 시작(적중 무관)

        if (IsNetworkActive)
            PlayDefaultAttackRpc(attackIndex, attackDirection, triggerAttack, crossFadeStartTime, turnDelay);
    }

    // [서버] 원격 오너의 타 시작 통지 — 거부하지 않고 공격 상태를 따라간다. 서버가 상태에 못 들어가면(넉백·구속 중 등) 끊기 경로로 돌려보낸다.
    [Rpc(SendTo.Server)]
    private void NotifySwingStartRpc(byte serial, byte attackIndex, Vector3 direction, bool triggerAttack, float crossFadeStartTime,
        float turnDelay, bool continuesCombo, PlayerStatReport statReport, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        player.ValidateStatReport(statReport, rpcParams.Receive.SenderClientId, "기본 공격 타 시작");

        // 서버가 끊은 콤보의 뒤늦은 '이어짐' — 오너는 끊기 RPC 로 이미 멈췄거나 곧 멈춘다. 따라가지 않는다.
        if (continuesCombo && serverIgnoreContinues)
            return;
        if (!continuesCombo)
            serverIgnoreContinues = false;

        int index = attackIndex;
        if (!HasAttackStep(index) || attackSteps[index].MotionDuration <= 0f)
            return;

        if (player == null || !player.BeginAttackState())
        {
            verifier.EndCombo();
            serverIgnoreContinues = true;
            CancelAttackRpc(serial);
            return;
        }

        serverSwingSerial = serial;
        BeginSwingRuntime(index, direction, turnDelay);
        ServerOnSwingStarted(serial, index, triggerAttack, crossFadeStartTime, turnDelay, continuesCombo);
        StartAttackPresentation(index, attackDirection, triggerAttack, crossFadeStartTime, turnDelay);
    }

    // [서버] 원격 오너 Hit 시점 판정 요청(Reliable). 오너가 보낸 위치·방향으로 판정, 2차 검증에 걸리면 데미지만 없다(모션은 그대로).
    [Rpc(SendTo.Server)]
    private void RequestHitRpc(byte serial, byte attackIndex, byte hit, Vector3 casterPosition, Vector3 direction,
        RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!IsAttacking || !hasStartedAttack)
            return;

        BasicAttackHitVerdict verdict = verifier.VerifyHit(Time.unscaledTimeAsDouble, serial, attackIndex, hit, StepTimings, AttackSpeed,
            BasicAttackVerifySettings.Current);
        if (verdict != BasicAttackHitVerdict.Accepted)
        {
            Edit.LogWarning($"[평타 검증] {name} {attackIndex + 1}타 히트{hit} 데미지 없음 — {verdict}", this);
            PlaySlashVfxAllPeers(attackIndex, false);
            return;
        }

        Vector3 offset = casterPosition - transform.position;
        offset.y = 0f;
        if (offset.sqrMagnitude > OwnerPositionWarnDistance * OwnerPositionWarnDistance)
            Edit.LogWarning($"[평타 검증] {name} 판정 위치가 서버가 보는 위치와 {offset.magnitude:0.00}m 다르다(경고만, 오너 위치로 판정).", this);

        FireHitAndPlayVfx(casterPosition, ResolveAttackDirection(direction));
    }

    // [서버] 원격 오너의 공격 종료(정상 종료·스스로 끊음). 지금 따라가는 휘두름이 아니면 무시(이미 끊었다).
    [Rpc(SendTo.Server)]
    private void NotifyAttackEndRpc(byte serial, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!IsAttacking || serial != serverSwingSerial)
            return;

        EndAttackFollowServer();
    }

    private void EndAttackFollowServer()
    {
        isFinishingAttackTail = true;
        finishingAttackIndex = currentAttackIndex;

        ResetAttackRuntime();
        verifier.EndCombo();
        player?.EndAttackState();

        if (IsNetworkActive)
            EndDefaultAttackRpc();
    }

    private void TickServerFollowFallback()
    {
        if (!hasStartedAttack || serverFollowFallbackTime <= 0f || Time.time < serverFollowFallbackTime)
            return;

        Edit.LogWarning($"[평타] {name} 종료 통지가 오지 않아 서버가 공격 상태를 끝낸다(안전망).", this);
        EndAttackFollowServer();
    }

    // ─── 다른 피어 ────────────────────────────────────────────────────

    // [오너 뺀 피어] 타 재생. 오너는 이미 자기 화면에서 틀었다. 서버(원격 오너)는 통지 처리에서 직접 튼다.
    [Rpc(SendTo.NotOwner)]
    private void PlayDefaultAttackRpc(int attackIndex, Vector3 direction, bool triggerAttack, float crossFadeStartTime, float turnDelay)
    {
        if (IsServer)
            return;

        if (player != null && !player.BeginAttackState())
        {
            ResetAttackRuntime();
            return;
        }

        StartAttackPresentation(attackIndex, direction, triggerAttack, crossFadeStartTime, turnDelay);
    }

    // [오너 뺀 피어] 정상 종료 — 남은 클립은 자연 재생.
    [Rpc(SendTo.NotOwner)]
    private void EndDefaultAttackRpc()
    {
        if (IsServer)
            return;

        isFinishingAttackTail = true;
        finishingAttackIndex = currentAttackIndex;

        ResetAttackRuntime();

        player?.EndAttackState();
    }

    // [서버 뺀 피어] 서버가 끊었다 — 오너(원격)·관전자 즉시 멈춤. 오너는 끊긴 콤보 뒤에 새로 시작한 콤보면 무시한다.
    [Rpc(SendTo.NotServer)]
    private void CancelAttackRpc(byte serial)
    {
        if (IsOwner)
        {
            if (!IsAttacking && !hasStartedAttack)
                return;

            // 일련번호가 지금 콤보 시작보다 앞이면(바이트 순환 비교) 이미 끝난 옛 콤보에 대한 끊기다.
            if ((byte)(serial - comboStartSerial) >= 128)
                return;
        }

        isFinishingAttackTail = false;
        if (IsAttacking)
            player.EndAttackState();   // Exit → CancelCurrentAttack(Idle 로 끊기)
        else
            CancelCurrentAttack();
    }

    // ─── 공용 ────────────────────────────────────────────────────────

    // 한 타의 진행 상태(체인 주도·서버 공용). 데미지 스냅샷·튜토리얼 훅은 ServerOnSwingStarted.
    private void BeginSwingRuntime(int attackIndex, Vector3 direction, float turnDelay)
    {
        DefaultAttackStep step = attackSteps[attackIndex];
        hasStartedAttack = true;
        hasQueuedNextAttack = false;
        isComboWindowOpen = false;
        hitNumber = 0;
        currentAttackIndex = attackIndex;
        attackDirection = ResolveAttackDirection(direction);
        queuedAttackDirection = attackDirection;
        attackEndFallbackTime = Time.time + turnDelay + GetPlaybackDuration(step) + Mathf.Max(0f, endFallbackPadding);
        serverFollowFallbackTime = attackEndFallbackTime + ServerFollowGraceSeconds;
    }

    private void StartAttackPresentation(int attackIndex, Vector3 direction, bool triggerAttack, float crossFadeStartTime, float turnDelay)
    {
        if (!HasAttackStep(attackIndex))
            return;

        DefaultAttackStep step = attackSteps[attackIndex];
        currentAttackIndex = attackIndex;
        // 새 공격이 직전 공격의 꼬리를 대신한다. 꼬리 플래그가 남으면(같은 스텝 클립을 다시 틀면
        // HandleAnimatorMove 가 영영 못 내린다) 이번 공격을 대시로 끊을 때 CancelCurrentAttack 이
        // 정상 종료 캐스케이드로 오인해 Idle 전환·끊기 RPC 를 건너뛴다.
        isFinishingAttackTail = false;
        finishingAttackIndex = -1;
        attackDirection = ResolveAttackDirection(direction);
        moveRemaining = step.MovementType == DefaultAttackMovementType.ScriptedForwardDistance
            ? Mathf.Max(step.ForwardDistance, 0f)
            : 0f;
        float playbackDuration = GetPlaybackDuration(step);
        moveSpeed = playbackDuration > 0f ? moveRemaining / playbackDuration : 0f;

        if (step.RotationType == DefaultAttackRotationType.SnapOnStart)
            movement.TurnToward(attackDirection);

        player.SetAnimatorMoving(false);

        hasPendingAttackClip = turnDelay > 0f;
        if (hasPendingAttackClip)
        {
            pendingAttackClipTime = Time.time + turnDelay;
            pendingTriggerAttack = triggerAttack;
            pendingCrossFadeStartTime = crossFadeStartTime;
            return;
        }

        PlayAttackClip(triggerAttack, crossFadeStartTime);
    }

    private void PlayAttackClip(bool triggerAttack, float crossFadeStartTime)
    {
        if (animator == null)
            return;

        animator.SetInteger(AttackIndexHash, currentAttackIndex);
        animator.SetFloat(AttackSpeedHash, AttackSpeed);

        if (triggerAttack)
            animator.SetTrigger(DefaultAttackHash);
        else
            animator.CrossFadeInFixedTime(GetAttackStateHash(currentAttackIndex), 0.05f, -1,
                DefaultAttackChainRules.LoopBackFixedTimeOffset(crossFadeStartTime, AttackSpeed));
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

        if (moveRemaining <= 0f || hasPendingAttackClip)
            return;

        float moveDistance = Mathf.Min(moveSpeed * Time.fixedDeltaTime, moveRemaining);
        moveRemaining -= moveDistance;
        motor.AddDisplacement(attackDirection * moveDistance);
    }

    private int CalculateDamageSnapshot(DefaultAttackStep step)
    {
        // 공격 스탯 종합(기본%·전체%·가하는 피해%) 기준 — 툴팁과 같은 식(PlayerStatFormula). 특성 평타 배율 포함(Dref, 할 일 125)
        return player != null
            ? player.ResolveDamage(step.AttackDamageMultiplier, step.FlatDamageBonus, new TraitAttackKey(AttackType.Default))
            : 0;
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
        serverFollowFallbackTime = 0f;
        currentAttackIndex = 0;
        hitNumber = 0;
        hasQueuedNextAttack = false;
        isFinishingChain = false;
        isComboWindowOpen = false;
        hasStartedAttack = false;
        hasPendingAttackClip = false;
    }

    // SnapOnStart 단만 TurnToward 로 돌므로 그 단만 회전을 기다린다.
    private float GetTurnDelay(int attackIndex, Vector3 direction)
    {
        if (!HasAttackStep(attackIndex) || attackSteps[attackIndex].RotationType != DefaultAttackRotationType.SnapOnStart)
            return 0f;

        return movement.GetAttackTurnDelay(ResolveAttackDirection(direction));
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

    // 테이블 모드 Play 는 시작 때 SO 값을 덮어쓰므로 캐싱하지 않고 매번 읽는다.
    // 스탯 공격속도 배율(할 일 91 T5)을 곱한다 — 각 피어가 복제된 상태이상으로 같은 값을 낸다. 판정·창·End 는 클립 이벤트라 같이 빨라진다.
    private float AttackSpeed =>
        (attackData != null ? Mathf.Max(attackData.AttackSpeed, 0.1f) : 1f) * (player != null ? player.AttackSpeedRate : 1f);

    // MotionDuration 은 클립 타임라인 기준이라 실제 재생 시간은 공격속도로 나눈다.
    // 종료 안전망(attackEndFallbackTime)과 ScriptedForwardDistance 전진 속도(거리 ÷ 재생 시간 — 거리는 배율과 무관하게 같다)가 이 값을 쓴다.
    private float GetPlaybackDuration(DefaultAttackStep step) => step.MotionDuration / AttackSpeed;
}


[Serializable]
public class DefaultAttackStep
{
    [SerializeField] private AnimationClip clip;
    // 이 스텝(1타)의 모션 재생 길이. End 이벤트 누락 시 종료 fallback과
    // ScriptedForwardDistance 이동 속도 계산의 기준. 0이면 클립 길이를 사용.
    [FormerlySerializedAs("duration")]
    [DataTableIgnore] [SerializeField] private float motionDuration = 0;
    [SerializeField] private DefaultAttackMovementType movementType = DefaultAttackMovementType.ScriptedForwardDistance;
    [SerializeField] private float forwardDistance = 0.5f;
    [SerializeField] private DefaultAttackRotationType rotationType = DefaultAttackRotationType.SnapOnStart;
    [DataTableIgnore] [SerializeField] private float trackRotationSpeed = 12f;
    [SerializeField] private DefaultAttackHitType hitType = DefaultAttackHitType.Overlap;
    [Tooltip("Overlap 판정 영역(할 일 153) — 기준점 = 시전자 발밑 + 이 타의 조준 방향(CasterAim). 예전 프리팹 상자 Armature/DefaultAttack/AAC1 을 옮긴 직사각형이 기본.")]
    [SerializeField] private AttackShapeSet hitArea = new AttackShapeSet(AttackShapeOrigin.CasterAim, PaladinStrikeRect());
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private float projectileSpeed = 12f;
    [SerializeField] private float raycastRange = 20f;
    [SerializeField] private float attackDamageMultiplier = 1f;
    [SerializeField] private int flatDamageBonus;
    // 이 스텝이 체인의 첫 스텝(0번)일 때만 의미가 있다: 마지막 스텝의 LoopCheck에서
    // 조기 루프백으로 들어올 때 처음(0초)이 아니라 여기서부터 재생해 윈드업을 건너뛴다.
    // 초 단위, 클립 원본(스케일 안 된) 타임라인 기준 — 공격속도와 무관하게 같은 클립 시각으로 들어간다(DefaultAttackChainRules.LoopBackFixedTimeOffset).
    [DataTableIgnore] [SerializeField] private float loopBackEntryTime = 0f;
    // 이 스텝의 적중이 "적중 시 발동" 효과(패시브 버프 등)를 소모할 수 있는가. Player.ServerAttackLanded 에 실린다.
    [SerializeField] private bool triggersOnHit = true;

    public AnimationClip Clip => clip;
    public float MotionDuration => motionDuration > 0f ? motionDuration : ClipDuration;
    public DefaultAttackMovementType MovementType => movementType;
    public float ForwardDistance => forwardDistance;
    public DefaultAttackRotationType RotationType => rotationType;
    public float TrackRotationSpeed => trackRotationSpeed;
    public DefaultAttackHitType HitType => hitType;
    public AttackShapeSet HitArea => hitArea;
    public GameObject ProjectilePrefab => projectilePrefab;
    public float ProjectileSpeed => projectileSpeed;
    public float RaycastRange => raycastRange;
    public float AttackDamageMultiplier => attackDamageMultiplier;
    public int FlatDamageBonus => flatDamageBonus;
    public float LoopBackEntryTime => loopBackEntryTime;
    public bool TriggersOnHit => triggersOnHit;

    private float ClipDuration => clip != null ? clip.length : 0f;

    /// <summary>
    /// 가붕이 평타 기본 판정 도형(할 일 153) — 예전 프리팹 상자(<c>Armature/DefaultAttack/AAC1</c>, 4타 공용 BoxCollider
    /// center (0, 1, 0.5)·size (2.6, 2, 2.1))를 발밑 기준 직사각형으로 옮긴 값.
    /// 앞 1.55 / 뒤 0.55 / 폭 2.6, 상자 기준 축의 Armature 대비 회전 −0.03°(<c>DefaultAttack</c> 로컬 y 359.97).
    /// 높이 0 ~ 2.039: 상자는 Armature 높이(0.039) 위 0~2 m 였다. 아래 끝 0 은 판정 점이 발밑 최근접 점 하나이던 때 맞춘 값이라
    /// 몸 겹침 판정인 지금은 예전 상자보다 0.039 m 아래로 넓다(차이 없음에 가깝다).
    /// Armature 의 수평 위치(루트 축 x −0.142·z 0.014 — 루트는 돌지 않아 조준과 함께 돌지 않는 고정 어긋남)는 옮기지 않았다(기준점 = 루트 발밑).
    /// </summary>
    public static AttackRectShape PaladinStrikeRect() =>
        new AttackRectShape(1.55f, 2.6f, 0.55f) { YawOffset = -0.03f, MinHeight = 0f, MaxHeight = 2.039f };
}
