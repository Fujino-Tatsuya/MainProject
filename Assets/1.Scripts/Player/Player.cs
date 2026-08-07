using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

[RequireComponent(typeof(PlayerInputReader))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerAimIndicator))]
[RequireComponent(typeof(DefaultAttackController))]
[RequireComponent(typeof(PlayerStateController))]
[RequireComponent(typeof(StatusEffectController))]
public class Player : Unit
{
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

    /// <summary>이 클라이언트가 조작하는 플레이어. HUD 등 로컬 UI 바인딩용.</summary>
    public static Player LocalPlayer { get; private set; }
    public static event System.Action<Player> LocalPlayerChanged;

    private static void SetLocalPlayer(Player player)
    {
        if (LocalPlayer == player)
            return;

        LocalPlayer = player;
        LocalPlayerChanged?.Invoke(player);
    }

    [SerializeField] private Animator animator;
    [SerializeField] private float interruptDuration = 0.5f;
    [SerializeField] private float interruptForwardDistance = 0.5f;

    [Header("\n초기화 값")]
    [SerializeField] int attackDamage;
    [SerializeField] float moveSpeed;
    [SerializeField] float attackSpeed;
    [SerializeField] int maxHp;
    [SerializeField] int defense;

    [Header("\n이동 플랫폼 캐리")]
    [Tooltip("발밑 플랫폼 라이더 콜라이더 검사 레이어. 기본 전체.")]
    [SerializeField] private LayerMask platformRiderMask = ~0;
    [Tooltip("발밑 검사 거리(m).")]
    [SerializeField] private float platformGroundCheckDistance = 0.6f;

    private PlayerStateController stateController;
    private DefaultAttackController defaultAttack;
    private FirstMeleePassive passive;
    private PlayerMovement movement;
    private PlayerInvulnerability invulnerability;
    private Rigidbody playerRigidbody;
    private bool initialRigidbodyIsKinematic;
    private bool initialRigidbodyDetectCollisions;

    public PlayerActionState CurrentState => stateController != null ? stateController.CurrentState : PlayerActionState.Idle;
    public bool CanMove => stateController == null || stateController.CanMove;
    public bool CanMovementRotate => stateController == null || stateController.CanMovementRotate;
    public float InterruptDuration => interruptDuration;
    public float InterruptForwardDistance => interruptForwardDistance;

    private void Awake()
    {
        // StatusEffectController는 NetworkBehaviour라 런타임 추가가 불가 — 프리팹에 미리 부착돼 있어야 한다
        if (GetComponent<StatusEffectController>() == null)
            Debug.LogError("[Player] StatusEffectController가 프리팹에 부착되어 있지 않습니다.", this);

        stateController = GetComponent<PlayerStateController>();
        if (stateController == null)
            stateController = gameObject.AddComponent<PlayerStateController>();

        defaultAttack = GetComponent<DefaultAttackController>();
        passive = GetComponent<FirstMeleePassive>();
        movement = GetComponent<PlayerMovement>();
        invulnerability = GetComponent<PlayerInvulnerability>();
        playerRigidbody = GetComponent<Rigidbody>();
        if (playerRigidbody != null)
        {
            initialRigidbodyIsKinematic = playerRigidbody.isKinematic;
            initialRigidbodyDetectCollisions = playerRigidbody.detectCollisions;
        }

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // 내가 Owner인 플레이어가 스폰되면, 카메라 매니저에게 나를 따라오라고 알린다.
        if (IsOwner)
        {
            CameraTargetSwitcher.Active?.FocusOwnerPlayer();
            SetLocalPlayer(this);
            EnableLocalInput();
        }
        else
        {
            // HUD는 Player 프리팹 자식으로 스폰되므로, 원격 플레이어의 HUD 캔버스가 겹치지 않게 끈다
            CombatHUD childHud = GetComponentInChildren<CombatHUD>(true);
            if (childHud != null)
                childHud.gameObject.SetActive(false);

            // AudioListener는 씬에 하나만 활성이어야 한다 — 원격 플레이어 것은 끈다
            AudioListener audioListener = GetComponentInChildren<AudioListener>(true);
            if (audioListener != null)
                audioListener.enabled = false;
        }

        if (IsServer)
            Initialize(attackDamage, moveSpeed, attackSpeed, maxHp, defense);

        ConfigureMovementPhysicsAuthority();
    }

    public override void OnNetworkDespawn()
    {
        if (LocalPlayer == this)
            SetLocalPlayer(null);

        RestoreRigidbodyDefaults();
        base.OnNetworkDespawn();
    }

    public override void OnGainedOwnership()
    {
        base.OnGainedOwnership();
        ConfigureMovementPhysicsAuthority();
    }

    public override void OnLostOwnership()
    {
        base.OnLostOwnership();
        ConfigureMovementPhysicsAuthority();
    }

    private void Start()
    {
        // 오프라인(비네트워크) 실행은 OnNetworkSpawn이 불리지 않는다 — 테스트 씬 HUD 바인딩/입력 활성 폴백
        if (!IsNetworkActive)
        {
            SetLocalPlayer(this);
            EnableLocalInput();
        }
    }

    /// <summary>
    /// PlayerInput은 프리팹에서 기본 비활성 — 원격 플레이어 클론이 스폰 시 디바이스 페어링을 시도하며
    /// "Cannot find matching control scheme" 경고를 내는 것을 막기 위해 로컬(오너/오프라인)만 켠다.
    /// </summary>
    private void EnableLocalInput()
    {
        PlayerInput playerInput = GetComponent<PlayerInput>();
        if (playerInput != null && !playerInput.enabled)
            playerInput.enabled = true;
    }

    public override void OnDestroy()
    {
        // NGO NetworkBehaviour의 OnDestroy가 내부 정리를 수행하므로 반드시 base 호출
        if (LocalPlayer == this)
            SetLocalPlayer(null);

        base.OnDestroy();
    }

    private void Update()
    {
        // 이동 플랫폼 캐리는 이동 권한 피어(오너/오프라인)에서만 적용한다.
        // 비오너는 루트 NetworkTransform으로 이미 동기되므로 여기서 적용하면 이중 적용된다.
        if (IsMovementAuthority)
        {
            ApplyPlatformCarry();
        }

        if (IsNetworkActive &&
            !stateController.ShouldTickForNetwork(IsOwner, HasStateAuthority))
        {
            return;
        }

        stateController.Tick();
    }

    /// <summary>발밑에 캐리 표면이 있으면 그 이동량을 플레이어 이동에 가산한다.</summary>
    private void ApplyPlatformCarry()
    {
        if (movement == null)
        {
            return;
        }

        // RaycastAll: 자기 콜라이더가 먼저 맞아 캐리 표면 검출을 막지 않도록 전체 히트를 확인한다.
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            platformGroundCheckDistance,
            platformRiderMask,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hits.Length; i++)
        {
            ISurfaceCarrier carrier =
                hits[i].collider.GetComponentInParent<ISurfaceCarrier>();
            if (carrier != null)
            {
                movement.AddCarryDelta(
                    carrier.GetCarryDelta(transform.position, Time.deltaTime));
                break;
            }
        }
    }

    public void EndDefaultAttack()
    {
        defaultAttack.EndCurrentAttack();
    }

    public void HitDefaultAttack()
    {
        defaultAttack.HitCurrentAttack();
    }

    public void HandleDefaultAttackEvent(DefaultAttackAnimationEventType eventType)
    {
        defaultAttack.HandleAnimationEvent(eventType);
    }

    public void EndInterrupt()
    {
        stateController.EndInterrupt();
    }

    public bool BeginAttackState()
    {
        return stateController.ChangeState(PlayerActionState.Attack);
    }

    public bool EndAttackState()
    {
        if (stateController.CurrentState != PlayerActionState.Attack)
            return false;

        return stateController.ChangeState(PlayerActionState.Idle);
    }

    protected override void OnKnockback(Vector3 direction, float strength)
    {
        // 서버가 거부(사망 — 슈퍼아머는 Unit.Knockback에서 선차단)하면 오너에게도 전파하지 않는다
        bool accepted = stateController.BeginKnockback(direction, strength);
        if (!accepted)
            return;

        ApplyKnockbackClientRpc(direction, strength, CreateOwnerClientRpcParams());
    }

    /// <summary>
    /// 서버가 플레이어를 instigator에 구속한다(잡기 = <see cref="RestraintMode.Carry"/> /
    /// 돌진 밀기 = <see cref="RestraintMode.Push"/>).
    ///
    /// 반환값이 곧 계약이다 — <b>false면 구속되지 않았다</b>. 시전자는 이 값으로 후처리를 갈라야 한다
    /// (예: 돌진이 벽에 닿았을 때 <b>실제로 밀린 대상만</b> 기절시킨다). 데미지는 이 값과 무관하게 별도 경로다.
    ///
    /// Push는 시전자가 슈퍼아머 대상을 밀지 못한다(<see cref="Unit.Knockback"/>과 같은 규칙).
    /// Carry는 슈퍼아머와 무관하게 걸린다 — 기존 보스 Grab 체인의 동작이다.
    /// </summary>
    /// <param name="frontOffset">Push 전용. 시전자 정면으로 이만큼 앞에 붙는다.</param>
    public bool BeginRestrainedByInstigator(
        GameObject instigator, RestraintMode mode = RestraintMode.Carry, float frontOffset = 0f)
    {
        if (!IsServer)
            return false;

        NetworkObject instigatorNetworkObject =
            instigator != null ? instigator.GetComponentInParent<NetworkObject>() : null;
        if (instigatorNetworkObject == null)
        {
            Debug.LogError("[Player] Restraint instigator must belong to a spawned NetworkObject.", this);
            return false;
        }

        if (!stateController.BeginRestrained(instigator, mode, frontOffset))
            return false;

        // Transform은 복제할 수 없으므로 종류(byte)와 offset만 싣는다 — 오너가 같은 규칙으로 목표를 계산한다.
        BeginRestrainedClientRpc(
            new NetworkObjectReference(instigatorNetworkObject), (byte)mode, frontOffset,
            CreateOwnerClientRpcParams());
        return true;
    }

    public bool EndRestrainedByInstigator()
    {
        if (!IsServer)
            return false;

        bool ended = stateController.EndRestrained();
        EndRestrainedClientRpc(CreateOwnerClientRpcParams());
        return ended;
    }

    /// <summary>기존 잡기 호출부 호환 래퍼. 보스 Grab 체인이 이 시그니처로 동작 중이라 유지한다.</summary>
    public bool BeginGrabbedByInstigator(GameObject instigator)
    {
        return BeginRestrainedByInstigator(instigator, RestraintMode.Carry);
    }

    /// <summary>기존 잡기 호출부 호환 래퍼.</summary>
    public bool EndGrabbedByInstigator()
    {
        return EndRestrainedByInstigator();
    }

    [ClientRpc]
    private void BeginRestrainedClientRpc(
        NetworkObjectReference instigatorReference,
        byte restraintMode,
        float frontOffset,
        ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
            return;

        if (!instigatorReference.TryGet(out NetworkObject instigatorNetworkObject))
        {
            Debug.LogError("[Player] Restraint instigator NetworkObject could not be resolved on the owner.", this);
            return;
        }

        // 서버가 이미 승인한 전이다 — 오너는 슈퍼아머를 다시 판정하지 않는다(복제 지연 시 상태가 갈린다).
        stateController.ApplyRestrainedFromServer(
            instigatorNetworkObject.gameObject, (RestraintMode)restraintMode, frontOffset);
    }

    [ClientRpc]
    private void EndRestrainedClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
            return;

        stateController.EndRestrained();
    }

    [ClientRpc]
    private void ApplyKnockbackClientRpc(Vector3 direction, float strength, ClientRpcParams clientRpcParams = default)
    {
        // 호스트는 서버 경로의 BeginKnockback으로 이미 상태 진입 — 재진입 시 임펄스가 이중 적용됨
        if (!IsOwner || IsServer)
            return;

        stateController.ApplyKnockbackFromServer(direction, strength);
    }

    /// <summary>이동은 오너 권위(networking.md) — 넉백 물리를 시뮬레이션할 피어인지 여부.</summary>
    public bool IsMovementAuthority => !IsNetworkActive || IsOwner;

    /// <summary>
    /// Player 위치는 owner-authority NetworkTransform이 복제한다.
    /// 비권한 피어의 Rigidbody는 kinematic으로 두어 중력·충돌 반응이 복제 위치와 경쟁하지 않게 한다.
    /// 콜라이더 감지는 유지하므로 서버의 공격 판정과 Overlap 쿼리에는 계속 참여한다.
    /// </summary>
    private void ConfigureMovementPhysicsAuthority()
    {
        if (playerRigidbody == null)
            return;

        playerRigidbody.linearVelocity = Vector3.zero;
        playerRigidbody.angularVelocity = Vector3.zero;

        if (IsMovementAuthority)
        {
            playerRigidbody.detectCollisions = initialRigidbodyDetectCollisions;
            playerRigidbody.isKinematic = initialRigidbodyIsKinematic;
            return;
        }

        playerRigidbody.detectCollisions = initialRigidbodyDetectCollisions;
        playerRigidbody.isKinematic = true;
    }

    private void RestoreRigidbodyDefaults()
    {
        if (playerRigidbody == null)
            return;

        playerRigidbody.linearVelocity = Vector3.zero;
        playerRigidbody.angularVelocity = Vector3.zero;
        playerRigidbody.detectCollisions = initialRigidbodyDetectCollisions;
        playerRigidbody.isKinematic = initialRigidbodyIsKinematic;
    }

    public void NotifyKnockbackEnded()
    {
        if (!IsNetworkActive || IsServer)
            return;

        NotifyKnockbackEndedServerRpc();
    }

    [ServerRpc] // RequireOwnership 기본값 true — 오너만 호출 가능
    private void NotifyKnockbackEndedServerRpc()
    {
        stateController.EndKnockback();
    }

    public void SetAnimatorMoving(bool isMoving)
    {
        if (animator != null)
            animator.SetBool(IsMovingHash, isMoving);
    }

    public override void TakeDamage(AttackInfo attackInfo)
    {
        base.TakeDamage(attackInfo);
    }

    // 피격당하면(데미지량 무관) 패시브(불굴의 의지) 쿨다운을 감소시킨다. 서버 권위에서만 유효.
    public override bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)
    {
        bool result = base.ReceiveAttack(attackInfo, hitContext);
        passive?.NotifyOwnerHit();
        return result;
    }

    // 추락 피해는 방어력·쉴드·일반 무적을 무시한다(서버 전용 Bypass Context). (PLAN §11, §13 / W10)
    private bool _fallDamageBypass;

    // 무적(대시 등) 동안 일반 피해를 차단한다. 단 추락 Bypass 중에는 통과시킨다. (PLAN §11 / W5·W10)
    protected override bool CanApplyHealthDamage(int damage)
    {
        if (_fallDamageBypass)
            return true;

        if (invulnerability != null && invulnerability.IsServerInvulnerable)
            return false;

        return base.CanApplyHealthDamage(damage);
    }

    /// <summary>
    /// 서버 전용. 추락 피해를 적용한다: BreakShield → ceil(FinalMaxHp * ratio) 직접 피해(무적 우회).
    /// 공격 Passive/Hit 반응을 발생시키지 않는다. (PLAN §13)
    /// </summary>
    public void ApplyFallDamage(float ratio)
    {
        if (!IsServer)
            return;

        _fallDamageBypass = true;
        try
        {
            BreakShield();
            ApplyDirectHealthDamage(Mathf.CeilToInt(FinalMaxHp * Mathf.Max(0f, ratio)));
        }
        finally
        {
            _fallDamageBypass = false;
        }
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
}
